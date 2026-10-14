using System;
using System.Collections.Generic;
using System.IO;
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
            string text = File.ReadAllText(current).Replace("\"schemaVersion\": 1", "\"schemaVersion\": 99");
            File.WriteAllText(current, text);

            var (status, _, _) = service.LoadBestValid(slot);
            Assert.AreEqual(LoadResultStatus.SchemaTooNewReadOnly, status,
                "newer schema must never be silently overwritten");
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
