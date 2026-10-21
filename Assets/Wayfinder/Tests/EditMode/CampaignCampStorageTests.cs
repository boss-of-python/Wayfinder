using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Per-camp storage (§12.2, D-039, schema 2): every activated camp owns its store, sized by its
    /// own content, and the v1 pooled <c>campStorage</c> container no longer exists. Includes the
    /// on-disk migration of a schema-1 save, which must never destroy a single unit (§17.5).
    /// Closes the storage half of KI-028.
    /// </summary>
    public class CampaignCampStorageTests
    {
        const string CampA = "camp.forest.a";
        const string CampB = "camp.forest.b";

        /// <summary>The pooled v1 container id. It must never be configured or hold anything again.</summary>
        static readonly InventoryId PooledV1Store = new InventoryId("campStorage");

        static InventoryId Store(string campId) => CampaignSession.CampStorageFor(campId);

        string _root;

        [SetUp]
        public void SetUp() => _root = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        /// <summary>Q01–Q03 played: camp.forest.a is activated and the session is parked at home.</summary>
        CampaignSession WithCampA(out SaveService saves)
        {
            saves = new SaveService(_root);
            var s = NewGame(saves);
            PlayQ01(s);
            PlayQ02(s);
            PlayQ03(s);
            s.DrainNotices();
            return s;
        }

        /// <summary>Puts items into garage storage and loads them, the way a garage loadout does (§10.4).</summary>
        static void LoadFromGarage(CampaignSession s, params (string itemId, int quantity)[] items)
        {
            foreach (var (itemId, quantity) in items)
            {
                Ok(s.Inventory.Add(CampaignSession.GarageStorage, new ItemStack(new ContentId(itemId), quantity)),
                    $"seed {itemId} into garage storage");
                Ok(s.LoadCargo(itemId, quantity), $"load {itemId}");
            }
        }

        // ================================================================== one store per camp

        [Test]
        public void EveryCampKeepsItsOwnStore_AndThePooledV1ContainerIsGone()
        {
            var s = WithCampA(out _);
            // Only Q03 grants a kit in the slice, so the second one is seeded (items.json camp_kit).
            LoadFromGarage(s, ("camp_kit", 1), ("scrap_metal", 5), ("polymer", 3));
            Ok(s.Depart(), "depart");

            Ok(s.DepositAtCamp(CampA), "deposit at camp A");
            Assert.AreEqual(5, s.Inventory.QuantityOf(Store(CampA), "scrap_metal"), "camp A owns its scrap");
            Assert.AreEqual(3, s.Inventory.QuantityOf(Store(CampA), "polymer"));
            Assert.AreEqual(0, s.Inventory.QuantityOf(Store(CampB), "scrap_metal"), "camp B has nothing");
            Assert.IsFalse(s.Inventory.HasContainer(Store(CampB)), "an unestablished camp has no store");
            Assert.AreEqual(1, s.Inventory.QuantityOf(CampaignSession.Cargo, "camp_kit"),
                "a deposit stores materials only — the kit stays in cargo");

            Ok(s.EstablishCamp(CampB), "establish camp B");
            Assert.IsTrue(s.Inventory.HasContainer(Store(CampB)), "its store exists immediately, no reload");
            Assert.AreEqual(CampService.CampStorageCapacityKg, s.Inventory.CapacityOf(Store(CampB)).massKg, 0.001f);

            // A field pickup, modelled directly (as CampaignFieldTests does).
            Ok(s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("electronics"), 2)), "pick up electronics");
            Ok(s.DepositAtCamp(CampB), "deposit at camp B");
            Assert.AreEqual(2, s.Inventory.QuantityOf(Store(CampB), "electronics"), "camp B owns its electronics");
            Assert.AreEqual(0, s.Inventory.QuantityOf(Store(CampA), "electronics"), "camp A never sees camp B's deposit");
            Assert.AreEqual(5, s.Inventory.QuantityOf(Store(CampA), "scrap_metal"), "and camp A's store is untouched");

            Assert.IsFalse(s.Inventory.HasContainer(PooledV1Store), "the v1 pooled container is not configured");
            Assert.AreEqual(0, s.Inventory.QuantityOf(PooledV1Store, "scrap_metal"), "nothing lands in a shared pool");
        }

        [Test]
        public void EachStoreIsSizedByItsOwnContent_NotByASharedConstant()
        {
            // A real catalog with camp.forest.a rebuilt at 5 kg: the session must honour per-camp capacity.
            var content = ContentWithCampCapacity(CampA, 5f);
            Assert.AreEqual(5f, content.Camps[CampA].StorageCapacityKg, 0.001f, "the mutated catalog parsed");
            Assert.AreEqual(500f, content.Camps[CampB].StorageCapacityKg, 0.001f, "other camps are untouched");

            var opened = CampaignSession.StartNew(new SaveService(_root), content, new SaveSlotId(0));
            Assert.AreEqual(CampaignOpenStatus.NewGameSaved, opened.Status, opened.Detail);
            var s = opened.Session;
            PlayQ01(s);
            PlayQ02(s);
            PlayQ03(s);   // establishes camp.forest.a → configures its store

            Assert.AreEqual(5f, s.CampStoreCapacityKg(CampA), 0.001f);
            Assert.AreEqual(500f, s.CampStoreCapacityKg(CampB), 0.001f, "camp B still uses its own content");
            Assert.AreEqual(CampService.CampStorageCapacityKg, s.CampStoreCapacityKg("camp.nowhere"), 0.001f,
                "an unknown camp falls back to the service default");
            Assert.AreEqual(5f, s.Inventory.CapacityOf(Store(CampA)).massKg, 0.001f,
                "establishing the camp configured its store from content");

            Ok(s.Depart(), "depart again");
            Ok(s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("scrap_metal"), 8)),
                "pick up 8 kg of scrap (1 kg per unit)");
            Ok(s.DepositAtCamp(CampA), "deposit into the 5 kg store");

            Assert.AreEqual(5, s.Inventory.QuantityOf(Store(CampA), "scrap_metal"), "only what fits is stored");
            Assert.AreEqual(3, s.Inventory.QuantityOf(CampaignSession.Cargo, "scrap_metal"),
                "the rest stays in cargo — a full camp store never destroys property (§17.5)");
            Assert.LessOrEqual(s.Inventory.TotalMassKg(Store(CampA)), 5f + 1e-3f);
        }

        [Test]
        public void CampStoresSurviveSaveAndReload_SeparatelyPerCamp()
        {
            var s = WithCampA(out _);
            LoadFromGarage(s, ("camp_kit", 1), ("scrap_metal", 5), ("electronics", 2));
            Ok(s.Depart(), "depart");
            Ok(s.DepositAtCamp(CampA), "deposit at camp A");
            Ok(s.EstablishCamp(CampB), "establish camp B");
            Ok(s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("polymer"), 4)), "pick up polymer");
            Ok(s.DepositAtCamp(CampB), "deposit at camp B");

            string before = Canonical(s);
            Assert.AreEqual(SaveResultStatus.Saved, s.SaveNow().Status, "a camp save is a camp function (§10.4)");

            var reloaded = Reload(_root);
            Assert.AreEqual(5, reloaded.Inventory.QuantityOf(Store(CampA), "scrap_metal"));
            Assert.AreEqual(2, reloaded.Inventory.QuantityOf(Store(CampA), "electronics"));
            Assert.AreEqual(4, reloaded.Inventory.QuantityOf(Store(CampB), "polymer"));
            Assert.AreEqual(0, reloaded.Inventory.QuantityOf(Store(CampB), "scrap_metal"), "no cross-camp leakage");
            Assert.AreEqual(0, reloaded.Inventory.QuantityOf(PooledV1Store, "scrap_metal"), "no pooled store on reload");
            Assert.AreEqual(before, Canonical(reloaded), "save → load → capture is byte-identical");
        }

        [Test]
        public void HubTravelLeavesBothCampStoresIntact()
        {
            var s = WithCampA(out _);
            LoadFromGarage(s, ("camp_kit", 1), ("scrap_metal", 5));
            Ok(s.Depart(), "depart");
            Ok(s.DepositAtCamp(CampA), "deposit at camp A");
            Ok(s.EstablishCamp(CampB), "establish camp B");
            Ok(s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("polymer"), 4)), "pick up polymer");
            Ok(s.DepositAtCamp(CampB), "deposit at camp B");
            Return(s);

            Ok(s.TravelToHub(CampA, inWater: false, inDeepMud: false), "travel to camp A");
            Ok(s.TravelToHub(Home, inWater: false, inDeepMud: false), "and back home");

            Assert.AreEqual(5, s.Inventory.QuantityOf(Store(CampA), "scrap_metal"), "camp A's store travelled nowhere");
            Assert.AreEqual(4, s.Inventory.QuantityOf(Store(CampB), "polymer"), "nor did camp B's");
            var reloaded = Reload(_root);
            Assert.AreEqual(5, reloaded.Inventory.QuantityOf(Store(CampA), "scrap_metal"));
            Assert.AreEqual(4, reloaded.Inventory.QuantityOf(Store(CampB), "polymer"));
        }

        [Test]
        public void ADepositIsStillACriticalSave_ThatRollsTheCampStoreBack()
        {
            var s = WithCampA(out var saves);
            Ok(s.Depart(), "depart");
            Ok(s.Inventory.Add(CampaignSession.Cargo, new ItemStack(new ContentId("scrap_metal"), 6)), "pick up scrap");

            saves.StepHook = step => { if (step == SaveStep.TempVerified) throw new IOException("simulated write failure"); };
            var failed = s.DepositAtCamp(CampA);
            Assert.AreEqual(FailureReason.SaveIoFailure, failed.Reason);
            Assert.AreEqual(6, s.Inventory.QuantityOf(CampaignSession.Cargo, "scrap_metal"),
                "an unverified deposit gives the materials back (§12.4)");
            Assert.AreEqual(0, s.Inventory.QuantityOf(Store(CampA), "scrap_metal"), "the camp store rolled back too");

            saves.StepHook = null;
            Ok(s.DepositAtCamp(CampA), "the retry commits");
            Assert.AreEqual(6, s.Inventory.QuantityOf(Store(CampA), "scrap_metal"));
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.Cargo, "scrap_metal"));
        }

        // ================================================================== schema 1 → 2 on disk

        [Test]
        public void ASchema1SaveWithPooledCampStorage_MigratesIntoTheFirstActivatedCamp()
        {
            var s = WithCampA(out var saves);
            int revision = s.Revision + 1000;   // higher than any backup, so the v1 file is the one that loads
            int garageUnits = s.Inventory.Stacks(CampaignSession.GarageStorage).Sum(x => x.Quantity);

            // A v1 payload: pooled camp stacks, no campStores. The checksum covers the payload only,
            // so writing it and then downgrading the envelope's schemaVersion yields a genuine v1 file.
            var payload = s.CaptureSnapshot();
            payload.campStores = new List<CampStoreDto>();
            payload.campStorage = new List<ItemStackDto>
            {
                new ItemStackDto { itemId = "scrap_metal", quantity = 7 },
                new ItemStackDto { itemId = "scrap_metal", quantity = 3 },
                new ItemStackDto { itemId = "electronics", quantity = 2 }
            };
            Assert.AreEqual(SaveResultStatus.Saved, saves.Save(new SaveSlotId(0), payload, revision).Status);
            DowngradeToSchema1(_root, revision);

            var migrated = Reload(_root);
            Assert.AreEqual(revision, migrated.Revision, "the migrated save is the one that loaded");
            Assert.AreEqual(10, migrated.Inventory.QuantityOf(Store(CampA), "scrap_metal"),
                "pooled stacks merge by item — no unit is lost (§17.5)");
            Assert.AreEqual(2, migrated.Inventory.QuantityOf(Store(CampA), "electronics"));
            Assert.IsFalse(migrated.Inventory.HasContainer(PooledV1Store), "the pooled container is gone");
            Assert.AreEqual(garageUnits, migrated.Inventory.Stacks(CampaignSession.GarageStorage).Sum(x => x.Quantity),
                "garage storage is untouched while a camp exists to own the store");

            // The next save is schema 2 and never writes the pooled field again.
            Assert.AreEqual(SaveResultStatus.Saved, migrated.SaveNow().Status);
            string json = File.ReadAllText(Path.Combine(_root, "slot-0", "current.json"));
            StringAssert.Contains($"\"schemaVersion\": {SaveService.CurrentSchemaVersion}", json);
            Assert.AreEqual(10, QuantityOf(CurrentPayload(_root), Store(CampA), "scrap_metal"),
                "the per-camp store is what lands on disk");
        }

        [Test]
        public void ASchema1SaveWithNoActivatedCamp_MigratesPooledStorageIntoGarageStorage()
        {
            var saves = new SaveService(_root);
            var s = NewGame(saves);
            int revision = s.Revision + 1000;
            int garageBefore = s.Inventory.QuantityOf(CampaignSession.GarageStorage, "scrap_metal");

            var payload = s.CaptureSnapshot();
            payload.activatedCampIds = new List<string>();
            payload.campStores = new List<CampStoreDto>();
            payload.campStorage = new List<ItemStackDto> { new ItemStackDto { itemId = "scrap_metal", quantity = 6 } };
            Assert.AreEqual(SaveResultStatus.Saved, saves.Save(new SaveSlotId(0), payload, revision).Status);
            DowngradeToSchema1(_root, revision);

            var migrated = Reload(_root);
            Assert.AreEqual(garageBefore + 6, migrated.Inventory.QuantityOf(CampaignSession.GarageStorage, "scrap_metal"),
                "with no camp established the pooled store becomes garage stock — nothing is destroyed");
            Assert.IsFalse(migrated.Garage.IsCampActivated(CampA), "no camp was ever established");
            Assert.IsFalse(migrated.Inventory.HasContainer(Store(CampA)), "and no camp store is invented");
        }

        [Test]
        public void APayloadThatStillCarriesPooledRows_AdoptsThemIntoTheFirstCamp()
        {
            // Belt and braces for §17.5: a schema-2 file that somehow still holds pooled v1 rows (a
            // hand-edited save, or one written by a build whose migration was skipped) must not lose
            // them. Restore adopts them into the first activated camp's own store.
            var saves = new SaveService(_root);
            var s = WithCampA(out _);
            int revision = s.Revision + 1000;

            var payload = s.CaptureSnapshot();
            payload.campStores = new List<CampStoreDto>();
            payload.campStorage = new List<ItemStackDto> { new ItemStackDto { itemId = "scrap_metal", quantity = 4 } };
            Assert.AreEqual(SaveResultStatus.Saved, saves.Save(new SaveSlotId(0), payload, revision).Status);

            var reloaded = Reload(_root);
            Assert.AreEqual(4, reloaded.Inventory.QuantityOf(Store(CampA), "scrap_metal"), "adopted, not dropped");
            Assert.IsFalse(reloaded.Inventory.HasContainer(PooledV1Store), "and still no pooled container");
        }

        [Test]
        public void Content_DeclaresAStoreCapacityForEveryCamp()
        {
            Assert.AreEqual(10, Content.Camps.Count, "release-scope camps");
            foreach (var camp in Content.Camps.Values)
                Assert.Greater(camp.StorageCapacityKg, 0f, $"{camp.Id} declares its own storageCapacityKg");
        }

        // ================================================================== helpers

        /// <summary>Rewrites a just-written save as schema 1, leaving the payload (and its checksum) alone.</summary>
        static void DowngradeToSchema1(string root, int revision)
        {
            string current = Path.Combine(root, "slot-0", "current.json");
            string json = File.ReadAllText(current);
            StringAssert.Contains($"\"schemaVersion\": {SaveService.CurrentSchemaVersion}", json, "written at the current schema");
            StringAssert.Contains($"\"revision\": {revision}", json);
            File.WriteAllText(current, json.Replace(
                $"\"schemaVersion\": {SaveService.CurrentSchemaVersion}", "\"schemaVersion\": 1"));
        }

        static SavePayload CurrentPayload(string root)
        {
            var (_, envelope, detail) = new SaveService(root).LoadBestValid(new SaveSlotId(0));
            Assert.IsNotNull(envelope, detail);
            return envelope.payload;
        }

        static int QuantityOf(SavePayload payload, InventoryId container, string itemId)
        {
            var stores = payload.campStores ?? new List<CampStoreDto>();
            List<ItemStackDto> items = container.Value.StartsWith(CampaignSession.CampStoragePrefix)
                ? stores.FirstOrDefault(x => x != null && x.campId == container.Value.Substring(CampaignSession.CampStoragePrefix.Length))?.items
                : payload.garageStorage;
            return QuantityOf(items, itemId);
        }

        static int QuantityOf(List<ItemStackDto> items, string itemId) =>
            (items ?? new List<ItemStackDto>()).Where(x => x != null && x.itemId == itemId).Sum(x => x.quantity);

        /// <summary>Rebuilds the real catalog with one camp's <c>storageCapacityKg</c> changed.</summary>
        static CampaignContent ContentWithCampCapacity(string campId, float capacityKg)
        {
            var files = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var path in Directory.GetFiles(ContentDir, "*.json"))
                files[Path.GetFileName(path)] = File.ReadAllText(path);

            var array = Newtonsoft.Json.Linq.JArray.Parse(files["camps.json"]);
            bool changed = false;
            foreach (var token in array)
            {
                var entry = (Newtonsoft.Json.Linq.JObject)token;
                if (entry.Value<string>("id") != campId) continue;
                entry["storageCapacityKg"] = capacityKg;
                changed = true;
            }
            Assert.IsTrue(changed, $"storageCapacityKg set for {campId}");
            files["camps.json"] = array.ToString(Newtonsoft.Json.Formatting.None);
            return CampaignContent.FromJson(files);
        }
    }
}
