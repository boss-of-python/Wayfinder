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

                // A .tmp left by a killed process is never trusted (§17.4 step 6) and must not be
                // mistaken for this revision's buffer (SYS-SAV-001). FileMode.Create would truncate
                // it, but deleting first also removes a read-only or partially-locked leftover that
                // would otherwise fail the write with an IOException.
                CleanupStaleTemp(slot);

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
                // Do not leave a half-written buffer behind for the next boot to explain away.
                TryDelete(PathFor(slot, TempFileName));
                return new SaveResult(SaveResultStatus.FailedWrite, revision, ex.Message);
            }
            catch (UnauthorizedAccessException ex)
            {
                TryDelete(PathFor(slot, TempFileName));
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

        /// <summary>
        /// Removes a temp buffer left by an interrupted write (§17.4 step 6: temp files are never
        /// trusted). Called before every write, and safe to call on boot.
        /// </summary>
        public void CleanupStaleTemp(SaveSlotId slot) => TryDelete(PathFor(slot, TempFileName));

        /// <summary>Manual slot deletion requires confirmation upstream (§17.5); this just performs it.</summary>
        public void DeleteSlot(SaveSlotId slot)
        {
            string dir = SlotDir(slot);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    /// <summary>
    /// One forward schema step: reads a payload at <see cref="FromVersion"/> and rewrites it for
    /// <c>FromVersion + 1</c> (§17.5, SYS-SAV-002). Steps are pure payload transforms; they never
    /// write to disk and never touch currency outside an explicit refund rule.
    /// </summary>
    public interface ISaveMigrator
    {
        /// <summary>Schema version this step consumes; it produces <c>FromVersion + 1</c>.</summary>
        int FromVersion { get; }

        /// <summary>Rewrites one payload in place. Must preserve stable content IDs.</summary>
        void Apply(JObject payload);
    }

    /// <summary>Delegate-backed <see cref="ISaveMigrator"/>, so a step stays a lambda.</summary>
    public sealed class SaveMigration : ISaveMigrator
    {
        readonly Action<JObject> _apply;

        public SaveMigration(int fromVersion, Action<JObject> apply)
        {
            if (fromVersion < 1) throw new ArgumentOutOfRangeException(nameof(fromVersion), "schema versions start at 1");
            _apply = apply ?? throw new ArgumentNullException(nameof(apply));
            FromVersion = fromVersion;
        }

        public int FromVersion { get; }

        public void Apply(JObject payload) => _apply(payload ?? throw new ArgumentNullException(nameof(payload)));
    }

    /// <summary>
    /// Sequential pure migrations v1→v2→… (§17.5). Preserve stable IDs; removed items get explicit
    /// replacement/refund rules; moving a landmark never removes its discovered flag.
    /// <para>
    /// <b>Gaps now fail loudly (SYS-SAV-002).</b> The previous loop incremented <c>schemaVersion</c>
    /// whether or not a step existed, so a save written at v1 and read by a build at v3 was
    /// relabelled "v3" with no transformation applied and then written back at v3 — a silent schema
    /// lie that makes the <i>next</i> load trust un-migrated data. A missing step is now an
    /// <see cref="InvalidOperationException"/>, which <c>CampaignSession.Continue</c> surfaces
    /// instead of overwriting.
    /// </para>
    /// </summary>
    public static class SaveMigrations
    {
        /// <summary>Built-in steps, keyed by the version they consume. None are required at schema 1.</summary>
        static readonly ISaveMigrator[] BuiltIn =
        {
            // new SaveMigration(1, payload => { /* v1 → v2 */ }),
        };

        public static IReadOnlyList<ISaveMigrator> Steps => BuiltIn;

        /// <summary>Runs migrations from the envelope's schema to the current schema, in order.</summary>
        public static SaveEnvelope MigrateToCurrent(SaveEnvelope envelope) =>
            Migrate(envelope, SaveService.CurrentSchemaVersion, BuiltIn);

        /// <summary>
        /// Runs <paramref name="steps"/> until <paramref name="targetVersion"/> is reached. Public and
        /// target-versioned so the pipeline itself is testable before a second schema exists.
        /// </summary>
        public static SaveEnvelope Migrate(SaveEnvelope envelope, int targetVersion, IEnumerable<ISaveMigrator> steps)
        {
            if (envelope == null) throw new ArgumentNullException(nameof(envelope));
            if (targetVersion < 1) throw new ArgumentOutOfRangeException(nameof(targetVersion));
            if (envelope.schemaVersion > targetVersion)
                throw new InvalidOperationException("schema too new; open read-only recovery instead");

            var ordered = (steps ?? Array.Empty<ISaveMigrator>())
                .Where(s => s != null)
                .OrderBy(s => s.FromVersion)
                .ToArray();

            // Atomic: every step runs against a working copy, and the envelope is only updated once
            // the whole chain succeeded. A failure mid-pipeline therefore leaves the caller holding
            // exactly the save it loaded — never a half-migrated payload labelled with a new version.
            var working = JObject.FromObject(envelope.payload);
            int version = envelope.schemaVersion;
            while (version < targetVersion)
            {
                int from = version;
                ISaveMigrator step = ordered.FirstOrDefault(s => s.FromVersion == from);
                if (step == null)
                    throw new InvalidOperationException(
                        $"no save migration from v{from} to v{from + 1}; this build cannot upgrade that save");

                step.Apply(working);
                version = from + 1;
            }

            // SaveJson.Reader() carries ObjectCreationHandling.Replace: without it, ToObject APPENDS
            // the migrated arrays to the DTO field initialisers (the KI-013 bug).
            envelope.payload = working.ToObject<SavePayload>(SaveJson.Reader())
                              ?? throw new InvalidOperationException("save migration produced no payload");
            envelope.schemaVersion = version;
            return envelope;
        }

        /// <summary>
        /// Static audit of a step list against a target version: duplicate starts, steps that can
        /// never run, and gaps. Returns human-readable problems; an empty list means the pipeline is
        /// sound. Exercised by tests today; the release check calls it once a second schema exists.
        /// </summary>
        public static IReadOnlyList<string> ValidatePipeline(int targetVersion, IEnumerable<ISaveMigrator> steps)
        {
            var problems = new List<string>();
            var byVersion = new Dictionary<int, ISaveMigrator>();
            foreach (var step in steps ?? Array.Empty<ISaveMigrator>())
            {
                if (step == null) { problems.Add("null migration step"); continue; }
                if (byVersion.ContainsKey(step.FromVersion))
                    problems.Add($"duplicate migration from v{step.FromVersion}");
                else
                    byVersion[step.FromVersion] = step;
                if (step.FromVersion < 1 || step.FromVersion >= targetVersion)
                    problems.Add($"migration from v{step.FromVersion} can never run (target v{targetVersion})");
            }
            for (int from = 1; from < targetVersion; from++)
                if (!byVersion.ContainsKey(from))
                    problems.Add($"missing migration from v{from} to v{from + 1}");
            return problems;
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
