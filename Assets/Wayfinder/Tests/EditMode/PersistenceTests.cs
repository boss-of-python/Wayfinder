using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Core;
using Wayfinder.Persistence;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Save atomicity, backup recovery and migration tests (§17.3–17.5). Mandatory QA cases 4, 5, 19.
    /// </summary>
    public class PersistenceTests
    {
        string _dir;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "wayfinder-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
        }

        SavePayload SamplePayload()
        {
            return new SavePayload
            {
                wallet = new WalletDto { salvage = 1840 },
                researchedUpgradeIds = new List<string> { "upgrade.scout.01.trailTires1" },
                completedMissionIds = new List<string> { "mission.Q01", "mission.Q02" },
                world = new WorldStateDto
                {
                    discoveredLandmarkIds = new List<string> { "landmark.forest.01" },
                    collectedCacheIds = new List<string> { "cache.forest.001" },
                    mappingRewardsPaidThroughCount = 50
                }
            };
        }

        [Test]
        public void SaveThenLoad_RoundTrips()
        {
            var service = new SaveService(_dir);
            var slot = new SaveSlotId(0);
            var result = service.Save(slot, SamplePayload(), 42);
            Assert.IsTrue(result.Success, result.Detail);

            var (status, envelope, detail) = service.LoadBestValid(slot);
            Assert.AreEqual(LoadResultStatus.LoadedCurrent, status, detail);
            Assert.AreEqual(42, envelope.revision);
            Assert.AreEqual(1840, envelope.payload.wallet.salvage);
            Assert.AreEqual("upgrade.scout.01.trailTires1", envelope.payload.researchedUpgradeIds[0]);
        }

        [Test]
        public void Checksum_DetectsCorruption()
        {
            var service = new SaveService(_dir);
            var slot = new SaveSlotId(0);
            service.Save(slot, SamplePayload(), 1);

            string current = Path.Combine(_dir, slot.FileName, "current.json");
            string text = File.ReadAllText(current);
            File.WriteAllText(current, text.Replace("\"salvage\": 1840", "\"salvage\": 999999"));

            var (status, _, _) = service.LoadBestValid(slot);
            Assert.AreNotEqual(LoadResultStatus.LoadedCurrent, status, "tampered/corrupt current must not load");
        }

        [Test]
        public void BackupRecovery_LoadsLatestValid_QA5()
        {
            var service = new SaveService(_dir);
            var slot = new SaveSlotId(0);
            service.Save(slot, SamplePayload(), 1);
            service.Save(slot, SamplePayload(), 2);
            service.Save(slot, SamplePayload(), 3);

            // Corrupt current entirely.
            File.WriteAllText(Path.Combine(_dir, slot.FileName, "current.json"), "{ not json");
            var (status, envelope, _) = service.LoadBestValid(slot);
            Assert.AreEqual(LoadResultStatus.LoadedBackup, status);
            Assert.AreEqual(2, envelope.revision, "backup rotation keeps previous valid revision");
        }

        [Test]
        public void InterruptedWrite_NeverMixesStates_QA4()
        {
            // Simulate a crash after temp write but before replace: temp file exists, current untouched.
            var service = new SaveService(_dir);
            var slot = new SaveSlotId(0);
            service.Save(slot, SamplePayload(), 1);

            string slotDir = Path.Combine(_dir, slot.FileName);
            File.WriteAllText(Path.Combine(slotDir, "current.json.tmp"), "garbage-partial");

            var (status, envelope, _) = service.LoadBestValid(slot);
            Assert.AreEqual(LoadResultStatus.LoadedCurrent, status, "temp files are never trusted (§17.4)");
            Assert.AreEqual(1, envelope.revision);
        }

        [Test]
        public void SchemaTooNew_OpensReadOnly_QA19()
        {
            var service = new SaveService(_dir);
            var slot = new SaveSlotId(0);
            service.Save(slot, SamplePayload(), 5);

            string current = Path.Combine(_dir, slot.FileName, "current.json");
            string text = File.ReadAllText(current)
                .Replace($"\"schemaVersion\": {SaveService.CurrentSchemaVersion}", "\"schemaVersion\": 99");
            StringAssert.Contains("\"schemaVersion\": 99", text, "the fixture really rewrote the version");
            File.WriteAllText(current, text);

            var (status, _, _) = service.LoadBestValid(slot);
            Assert.AreEqual(LoadResultStatus.SchemaTooNewReadOnly, status,
                "newer schema must never be silently overwritten");
        }

        static int Qty(List<ItemStackDto> items, string itemId, string mission = "") =>
            (items ?? new List<ItemStackDto>())
                .Where(x => x != null && x.itemId == itemId && (x.missionInstanceId ?? string.Empty) == mission)
                .Sum(x => x.quantity);

        static SaveEnvelope MigrateV1(SavePayload payload) =>
            SaveMigrations.MigrateToCurrent(new SaveEnvelope { schemaVersion = 1, payload = payload });

        /// <summary>Schema 2 (D-039): the pooled v1 camp store becomes the first activated camp's own store.</summary>
        [Test]
        public void Migration_Step1_MovesPooledCampStorageIntoTheFirstActivatedCamp_QA19()
        {
            var payload = SamplePayload();
            payload.activatedCampIds = new List<string> { "camp.forest.b", "camp.forest.a" };
            payload.campStorage = new List<ItemStackDto>
            {
                new ItemStackDto { itemId = "scrap_metal", quantity = 7 },
                new ItemStackDto { itemId = "scrap_metal", quantity = 3 },
                new ItemStackDto { itemId = "electronics", quantity = 2 }
            };
            payload.garageStorage = new List<ItemStackDto> { new ItemStackDto { itemId = "polymer", quantity = 4 } };

            var after = MigrateV1(payload);

            Assert.AreEqual(SaveService.CurrentSchemaVersion, after.schemaVersion);
            Assert.AreEqual(1, after.payload.campStores.Count, "one store, for the first camp listed");
            Assert.AreEqual("camp.forest.b", after.payload.campStores[0].campId);
            Assert.AreEqual(10, Qty(after.payload.campStores[0].items, "scrap_metal"), "merged by item, no unit lost (§17.5)");
            Assert.AreEqual(2, after.payload.campStores[0].items.Count, "one stack per (item, mission instance)");
            Assert.AreEqual(2, Qty(after.payload.campStores[0].items, "electronics"));
            Assert.IsEmpty(after.payload.campStorage, "the pooled field is emptied so the step cannot double-apply");
            Assert.AreEqual(4, Qty(after.payload.garageStorage, "polymer"), "garage storage is untouched");
            Assert.AreEqual(CanonicalJson.Sha256Hex(CanonicalJson.Serialize(after.payload)), after.payloadChecksum,
                "the checksum is recomputed for the migrated payload (§17.4)");
            Assert.IsTrue(SaveMigrations.PreservesStableIds(new SaveEnvelope { payload = SamplePayload() }, after));
        }

        [Test]
        public void Migration_Step1_FallsBackToGarageStorage_WhenNoCampWasEstablished_QA19()
        {
            var payload = SamplePayload();
            payload.activatedCampIds = new List<string>();
            payload.campStorage = new List<ItemStackDto> { new ItemStackDto { itemId = "scrap_metal", quantity = 6 } };
            payload.garageStorage = new List<ItemStackDto> { new ItemStackDto { itemId = "scrap_metal", quantity = 1 } };

            var after = MigrateV1(payload);

            Assert.IsEmpty(after.payload.campStores, "no camp exists to own a store");
            Assert.AreEqual(7, Qty(after.payload.garageStorage, "scrap_metal"), "merged into garage stock, nothing destroyed");
            Assert.IsEmpty(after.payload.campStorage);
        }

        [Test]
        public void Migration_Step1_KeepsMissionScopedStacksApart_AndDropsRowsThatCarryNothing()
        {
            var payload = SamplePayload();
            payload.activatedCampIds = new List<string> { "camp.forest.a" };
            payload.campStorage = new List<ItemStackDto>
            {
                new ItemStackDto { itemId = "polymer", quantity = 2, missionInstanceId = "mission.Q07" },
                new ItemStackDto { itemId = "polymer", quantity = 3 },
                new ItemStackDto { itemId = "scrap_metal", quantity = 0 },          // carries nothing
                new ItemStackDto { itemId = null, quantity = 5 },                   // malformed
                null
            };

            var after = MigrateV1(payload);

            var items = after.payload.campStores[0].items;
            Assert.AreEqual(2, Qty(items, "polymer", "mission.Q07"), "a mission-scoped stack stays its own stack (DEF-021)");
            Assert.AreEqual(3, Qty(items, "polymer"), "and does not merge into the unscoped one");
            Assert.AreEqual(2, items.Count, "two real stacks: the empty, malformed and null rows carried nothing");
            Assert.AreEqual(5, items.Sum(x => x.quantity), "and every unit that did exist survived (§17.5)");
        }

        [Test]
        public void Migration_NeverRunsOnACurrentSchemaSave_AndRefusesANewerOne()
        {
            var payload = SamplePayload();
            payload.campStorage = new List<ItemStackDto> { new ItemStackDto { itemId = "scrap_metal", quantity = 6 } };
            var current = new SaveEnvelope { schemaVersion = SaveService.CurrentSchemaVersion, payload = payload };

            var untouched = SaveMigrations.MigrateToCurrent(current);
            Assert.AreEqual(6, Qty(untouched.payload.campStorage, "scrap_metal"),
                "a current-schema payload is never rewritten — pooled rows on disk are this build's own business");
            Assert.IsEmpty(untouched.payload.campStores);

            Assert.Throws<InvalidOperationException>(() => SaveMigrations.MigrateToCurrent(
                new SaveEnvelope { schemaVersion = SaveService.CurrentSchemaVersion + 1, payload = SamplePayload() }),
                "a newer schema opens read-only recovery instead (§17.5)");
        }

        [Test]
        public void Migration_PreservesStableIds_QA19()
        {
            var before = new SaveEnvelope { payload = SamplePayload() };
            var after = SaveMigrations.MigrateToCurrent(
                new SaveEnvelope { schemaVersion = 1, payload = SamplePayload() });
            Assert.IsTrue(SaveMigrations.PreservesStableIds(before, after));
        }

        [Test]
        public void CanonicalJson_KeyOrderDoesNotChangeChecksum()
        {
            string a = CanonicalJson.Serialize(new Dictionary<string, int> { ["b"] = 2, ["a"] = 1 });
            string b = CanonicalJson.Serialize(new Dictionary<string, int> { ["a"] = 1, ["b"] = 2 });
            Assert.AreEqual(a, b);
            Assert.AreEqual(CanonicalJson.Sha256Hex(a), CanonicalJson.Sha256Hex(b));
        }

        [Test]
        public void SaveResult_FailedWriteIsReportedNeverFaked()
        {
            // Read-only directory → save must report failure, not success (§13.5).
            string roDir = Path.Combine(_dir, "readonly");
            Directory.CreateDirectory(roDir);
            var service = new SaveService(roDir, (temp, dest, backup) =>
                throw new IOException("disk full"));
            var result = service.Save(new SaveSlotId(0), SamplePayload(), 1);
            Assert.IsFalse(result.Success);
            Assert.AreEqual(SaveResultStatus.FailedWrite, result.Status);
        }
    }
}
