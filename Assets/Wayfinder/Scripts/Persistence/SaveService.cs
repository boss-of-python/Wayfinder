using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Wayfinder.Core;

namespace Wayfinder.Persistence
{
    /// <summary>
    /// Canonical JSON serialization (D-010): sorted keys, invariant culture, no floating noise
    /// beyond round-trippable shortest form. The checksum covers the canonical payload bytes.
    /// </summary>
    public static class CanonicalJson
    {
        static readonly JsonSerializerSettings Settings = new JsonSerializerSettings
        {
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            NullValueHandling = NullValueHandling.Ignore,
            Formatting = Formatting.None,
            DateParseHandling = DateParseHandling.None
        };

        public static string Serialize(object value)
        {
            string json = JsonConvert.SerializeObject(value, Settings);
            return Canonicalize(json);
        }

        /// <summary>Sorts object keys recursively so checksums are stable across dictionary orderings.</summary>
        public static string Canonicalize(string json)
        {
            // DateParseHandling.None: a string that merely looks like a date must stay a string,
            // otherwise Json.NET would re-format it and the checksum would drift between write and read.
            JToken token;
            using (var reader = new JsonTextReader(new StringReader(json))
            {
                DateParseHandling = DateParseHandling.None,
                FloatParseHandling = FloatParseHandling.Double
            })
            {
                token = JToken.ReadFrom(reader);
            }
            var sorted = SortToken(token);
            return sorted.ToString(Formatting.None);
        }

        static JToken SortToken(JToken token)
        {
            if (token is JObject obj)
            {
                var result = new JObject();
                foreach (var prop in new List<JProperty>(obj.Properties()).OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    result.Add(prop.Name, SortToken(prop.Value));
                }
                return result;
            }
            if (token is JArray arr)
            {
                var result = new JArray();
                foreach (var item in arr) result.Add(SortToken(item));
                return result;
            }
            return token;
        }

        public static string Sha256Hex(string canonicalPayload)
        {
            using (var sha = SHA256.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(canonicalPayload));
                var sb = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>
    /// Json.NET settings for reading/writing save files. <b>ObjectCreationHandling.Replace is
    /// mandatory</b>: the DTOs initialise their lists with defaults (e.g. <c>ownedVehicleIds</c>
    /// contains the Scout), and Json.NET's default <c>Auto</c> handling would APPEND the JSON array
    /// to that default list on load — duplicating entries and breaking the payload checksum.
    /// (Found by the first executed run of PersistenceTests; see Docs/KNOWN_ISSUES.md KI-013.)
    /// </summary>
    public static class SaveJson
    {
        public static readonly JsonSerializerSettings ReadSettings = new JsonSerializerSettings
        {
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            DateParseHandling = DateParseHandling.None,
            MissingMemberHandling = MissingMemberHandling.Ignore
        };

        public static readonly JsonSerializerSettings WriteSettings = new JsonSerializerSettings
        {
            Culture = System.Globalization.CultureInfo.InvariantCulture,
            DateParseHandling = DateParseHandling.None,
            Formatting = Formatting.Indented
        };

        public static JsonSerializer Reader() => JsonSerializer.Create(ReadSettings);
    }

    /// <summary>
    /// Atomic persistence and recovery (§17.4):
    /// 1. capture immutable state revision N+1 on main thread
    /// 2. serialize canonical payload + checksum
    /// 3. write same-directory temp file; flush and close
    /// 4. verify it can be read and parsed
    /// 5. rotate validated backups and replace current (safest supported atomic op)
    /// 6. on boot choose highest valid complete revision; temp files are not trusted
    /// 7. newer incompatible schema opens read-only recovery, never silent overwrite
    /// </summary>
    /// <summary>Persistence checkpoints inside <see cref="SaveService.Save"/> (§12.4 steps 4–5).</summary>
    public enum SaveStep
    {
        Begin,
        TempWritten,
        TempVerified,
        Backup2Rotated,
        Backup1Rotated,
        CurrentReplaced
    }

    public sealed class SaveService
    {
        /// <summary>
        /// Test seam: invoked after each persistence step. Throwing from it simulates the process
        /// dying at that point, so tests can prove every interruption leaves either the old or the
        /// new complete save on disk (QA case 4). Null in production.
        /// </summary>
        public Action<SaveStep> StepHook { get; set; }

        void Step(SaveStep step) => StepHook?.Invoke(step);

        public const int CurrentSchemaVersion = 1;
        public const string CurrentFileName = "current.json";
        public const string Backup1FileName = "backup1.json";
        public const string Backup2FileName = "backup2.json";
        public const string TempFileName = "current.json.tmp";

        readonly string _rootDirectory;
        readonly Action<string, string, string> _atomicReplace;

        /// <summary>Injected so EditMode tests can run without UnityEngine (atomic replace strategy).</summary>
        public SaveService(string rootDirectory, Action<string, string, string> atomicReplace = null)
        {
            _rootDirectory = rootDirectory ?? throw new ArgumentNullException(nameof(rootDirectory));
            Directory.CreateDirectory(_rootDirectory);
            _atomicReplace = atomicReplace ?? DefaultAtomicReplace;
        }

        static void DefaultAtomicReplace(string tempFile, string destination, string backup)
        {
            if (File.Exists(destination))
            {
                File.Replace(tempFile, destination, backup);
            }
            else
            {
                File.Move(tempFile, destination);
            }
        }

        string SlotDir(SaveSlotId slot)
        {
            string dir = Path.Combine(_rootDirectory, slot.FileName);
            Directory.CreateDirectory(dir);
            return dir;
        }

        string PathFor(SaveSlotId slot, string fileName) => Path.Combine(SlotDir(slot), fileName);

        /// <summary>Writes a new revision atomically. Returns Saved only after verify succeeds (§12.4 step 5).</summary>
        public SaveResult Save(SaveSlotId slot, SavePayload payload, int revision)
        {
            try
            {
                var envelope = new SaveEnvelope
                {
                    schemaVersion = CurrentSchemaVersion,
                    slotId = slot.FileName,
                    revision = revision,
                    savedAtUtc = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ"),
                    payload = payload
                };

                string canonical = CanonicalJson.Serialize(payload);
                envelope.payloadChecksum = CanonicalJson.Sha256Hex(canonical);
                string envelopeJson = JsonConvert.SerializeObject(envelope, SaveJson.WriteSettings);

                Step(SaveStep.Begin);
                string tempPath = PathFor(slot, TempFileName);
                using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(envelopeJson);
                    writer.Flush();
                    stream.Flush(true);
                }
                Step(SaveStep.TempWritten);

                // 4. Verify the temp file parses and checksums match BEFORE touching current.
                var (verified, detail) = VerifyFile(tempPath);
                if (!verified)
                {
                    TryDelete(tempPath);
                    return new SaveResult(SaveResultStatus.FailedVerify, revision, detail);
                }
                Step(SaveStep.TempVerified);

                // 5. Rotate backups and replace current.
                string current = PathFor(slot, CurrentFileName);
                string backup1 = PathFor(slot, Backup1FileName);
                string backup2 = PathFor(slot, Backup2FileName);
                if (File.Exists(backup1))
                    File.Copy(backup1, backup2, overwrite: true);
                Step(SaveStep.Backup2Rotated);
                if (File.Exists(current))
                    File.Copy(current, backup1, overwrite: true);
                Step(SaveStep.Backup1Rotated);

                _atomicReplace(tempPath, current, backup1);
                Step(SaveStep.CurrentReplaced);
                return new SaveResult(SaveResultStatus.Saved, revision);
            }
            catch (IOException ex)
            {
                return new SaveResult(SaveResultStatus.FailedWrite, revision, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                return new SaveResult(SaveResultStatus.FailedWrite, revision, ex.Message);
            }
            // DEF-022: only IO and access failures were handled, so anything else — a corrupt-grid
            // FormatException out of the verify step, a serialization error, a bad path — escaped
            // Save() and took the calling frame with it. A save that cannot be written is a typed
            // failure, never an exception.
            // The filter is deliberately narrow: an exception thrown by the (test-only) StepHook
            // stands for the process dying, which is not a save failure and must reach the caller.
            catch (Exception ex) when (ex is FormatException || ex is Newtonsoft.Json.JsonException ||
                                       ex is ArgumentException || ex is NotSupportedException ||
                                       ex is InvalidOperationException || ex is ObjectDisposedException ||
                                       ex is System.Security.SecurityException)
            {
                TryDelete(PathFor(slot, TempFileName));
                return new SaveResult(SaveResultStatus.FailedWrite, revision, ex.GetType().Name + ": " + ex.Message);
            }
        }

        /// <summary>Boot load: highest valid complete revision among current → backup1 → backup2.</summary>
        public (LoadResultStatus status, SaveEnvelope envelope, string detail) LoadBestValid(SaveSlotId slot)
        {
            string[] candidates =
            {
                PathFor(slot, CurrentFileName),
                PathFor(slot, Backup1FileName),
                PathFor(slot, Backup2FileName)
            };

            int bestRevision = -1;
            SaveEnvelope best = null;
            string bestPath = null;
            bool sawNewerSchema = false;

            foreach (string path in candidates)
            {
                if (!File.Exists(path)) continue;
                var (ok, envelope, detail) = TryRead(path);
                if (!ok)
                {
                    if (detail == "schema-too-new") sawNewerSchema = true;
                    continue; // incomplete/corrupt files are never trusted (§17.4 step 6)
                }
                // DEF-022: a save carries the slot it was written for. Without this check, a file
                // copied or restored into another slot loads that slot's progress into a different
                // one — three isolated save slots (§1.4) silently stop being isolated.
                if (!string.IsNullOrEmpty(envelope.slotId) &&
                    !string.Equals(envelope.slotId, slot.FileName, StringComparison.Ordinal))
                    continue; // belongs to another slot: not a candidate for this one
                if (envelope.revision > bestRevision)
                {
                    bestRevision = envelope.revision;
                    best = envelope;
                    bestPath = path;
                }
            }

            if (best == null)
            {
                if (sawNewerSchema)
                    return (LoadResultStatus.SchemaTooNewReadOnly, null, "save schema newer than this build");
                return (LoadResultStatus.NoSave, null, "no valid save found");
            }

            bool loadedBackup = !string.Equals(bestPath, candidates[0], StringComparison.Ordinal);
            return (loadedBackup ? LoadResultStatus.LoadedBackup : LoadResultStatus.LoadedCurrent, best, string.Empty);
        }

        (bool ok, SaveEnvelope envelope, string detail) TryRead(string path)
        {
            try
            {
                string json = File.ReadAllText(path);
                var envelope = JsonConvert.DeserializeObject<SaveEnvelope>(json, SaveJson.ReadSettings);
                if (envelope == null || envelope.payload == null)
                    return (false, null, "empty");
                if (envelope.schemaVersion > CurrentSchemaVersion)
                    return (false, null, "schema-too-new");

                string canonical = CanonicalJson.Serialize(envelope.payload);
                string expected = CanonicalJson.Sha256Hex(canonical);
                if (!string.Equals(expected, envelope.payloadChecksum, StringComparison.OrdinalIgnoreCase))
                    return (false, null, "checksum-mismatch");
                return (true, envelope, string.Empty);
            }
            catch (Exception ex) when (ex is IOException || ex is JsonException || ex is UnauthorizedAccessException)
            {
                return (false, null, ex.Message);
            }
        }

        (bool verified, string detail) VerifyFile(string path)
        {
            var (ok, _, detail) = TryRead(path);
            return (ok, detail);
        }

        static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
        }

        /// <summary>Manual slot deletion requires confirmation upstream (§17.5); this just performs it.</summary>
        public void DeleteSlot(SaveSlotId slot)
        {
            string dir = SlotDir(slot);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// Sequential pure migrations v1→v2→… (§17.5). Preserve stable IDs; removed items get explicit
    /// replacement/refund rules; moving a landmark never removes its discovered flag.
    /// </summary>
    public static class SaveMigrations
    {
        public delegate void Migration(JObject payload);

        static readonly SortedDictionary<int, Migration> Steps = new SortedDictionary<int, Migration>
        {
            // [1] = payload => { /* v1 → v2 example; none required at schema 1 */ },
        };

        /// <summary>Runs migrations from the envelope's schema to the current schema, in order.</summary>
        public static SaveEnvelope MigrateToCurrent(SaveEnvelope envelope)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (envelope.schemaVersion > SaveService.CurrentSchemaVersion)
                throw new InvalidOperationException("schema too new; open read-only recovery instead");

            // Payload-level migrations run here as JObject transforms when schema grows past 1.
            while (envelope.schemaVersion < SaveService.CurrentSchemaVersion)
            {
                if (Steps.TryGetValue(envelope.schemaVersion, out var step))
                {
                    var obj = JObject.FromObject(envelope.payload);
                    step(obj);
                    envelope.payload = obj.ToObject<SavePayload>(SaveJson.Reader());
                }
                envelope.schemaVersion++;
            }
            return envelope;
        }

        /// <summary>QA case 19: migrating an old save must preserve discovery flags and researched modules.</summary>
        public static bool PreservesStableIds(SaveEnvelope before, SaveEnvelope after)
        {
            foreach (var id in before.payload.researchedUpgradeIds)
                if (!after.payload.researchedUpgradeIds.Contains(id)) return false;
            foreach (var id in before.payload.world.discoveredLandmarkIds)
                if (!after.payload.world.discoveredLandmarkIds.Contains(id)) return false;
            foreach (var id in before.payload.completedMissionIds)
                if (!after.payload.completedMissionIds.Contains(id)) return false;
            return true;
        }
    }
}
