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
    /// §10.4 community logistics: property stored at any hub joins one shared logistics inventory
    /// ONLY through a transfer action — free, instant, only while the player is at a hub, and never
    /// "magically accessible for field repairs". Garage material storage is the one unlimited shared
    /// store; each camp keeps its own mass-limited store (D-039, D-042).
    /// </summary>
    public class CampaignLogisticsTransferTests
    {
        const string Home = "garage.home";
        const string Canyon = "garage.canyon";
        const string CampA = "camp.forest.a";
        const string CampB = "camp.forest.b";

        /// <summary>camp.forest.a's socket in vertical_slice.json (1240, 0, 2520) r = 8 m.</summary>
        static readonly Pose AtCampA = new Pose(1240f, 0f, 2520f, 0f);
        static readonly Pose OpenField = new Pose(1100f, 0.6f, 2400f, 0f);

        string _root;
        SaveService _saves;

        [SetUp]
        public void SetUp()
        {
            _root = NewTempRoot();
            _saves = new SaveService(_root);
        }

        [TearDown]
        public void TearDown() => DeleteRoot(_root);

        /// <summary>Q01–Q03 played: camp.forest.a is activated and the session is parked at home.</summary>
        CampaignSession WithCampA()
        {
            var s = NewGame(_saves);
            PlayQ01(s);
            PlayQ02(s);
            PlayQ03(s);
            s.DrainNotices();
            return s;
        }

        static void DriveTo(CampaignSession s, Pose pose) =>
            s.UpdateVehicle(pose, 40f, 100, 100, 100, 100, 0.1f);

        static void Seed(CampaignSession s, InventoryId store, string itemId, int quantity) =>
            Ok(s.Inventory.Add(store, new ItemStack(new ContentId(itemId), quantity)),
                $"seed {quantity} × {itemId}");

        static int QuantityIn(CampaignSession s, InventoryId store, string itemId) =>
            s.Inventory.QuantityOf(store, itemId);

        static InventoryId CampStore(string campId) => CampaignSession.CampStorageFor(campId);

        // ================================================================== the transfer itself

        [Test]
        public void TransferToHub_MovesPropertyFromTheHubYouAreAt_ToAnotherActivatedHub()
        {
            var s = WithCampA();
            Assert.AreEqual(Home, s.CurrentHubId, "parked at the home garage after extraction");
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 20);

            Ok(s.TransferToHub(CampA, "scrap_metal", 20), "send the scrap to camp A");

            Assert.AreEqual(0, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"), "it left the garage");
            Assert.AreEqual(20, QuantityIn(s, CampStore(CampA), "scrap_metal"),
                "and arrived in THAT camp's own store (D-039), not a pooled one");
            Assert.AreEqual(20f, s.Inventory.TotalMassKg(CampStore(CampA)), 1e-3f);
        }

        [Test]
        public void TransferFromHub_PullsPropertyIntoTheHubYouAreAt()
        {
            var s = WithCampA();
            Seed(s, CampStore(CampA), "polymer", 6);
            Seed(s, CampStore(CampA), "electronics", 2);

            Ok(s.TransferFromHub(CampA, "polymer", 6), "pull the polymer home");

            Assert.AreEqual(0, QuantityIn(s, CampStore(CampA), "polymer"));
            Assert.AreEqual(6, QuantityIn(s, CampaignSession.GarageStorage, "polymer"));
            Assert.AreEqual(2, QuantityIn(s, CampStore(CampA), "electronics"),
                "only the requested stack moves; the rest stays put");
        }

        [Test]
        public void ATransferIsFreeAndInstant_NoFeeAndNoClockAdvance()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 5);
            int banked = s.Economy.BankedSalvage, escrow = s.Economy.EscrowedSalvage;
            double elapsed = s.Clock.ElapsedSimulationSeconds;
            float dayTime = s.Clock.DayTimeSeconds;

            Ok(s.TransferToHub(CampA, "scrap_metal", 5), "§10.4: free instant transfer");

            Assert.AreEqual(banked, s.Economy.BankedSalvage, "no fee is charged");
            Assert.AreEqual(escrow, s.Economy.EscrowedSalvage, "and nothing is escrowed");
            Assert.AreEqual(elapsed, s.Clock.ElapsedSimulationSeconds, "instant: the clock does not move");
            Assert.AreEqual(dayTime, s.Clock.DayTimeSeconds);
        }

        [Test]
        public void TransfersSurviveSaveAndReload()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 12);
            Ok(s.TransferToHub(CampA, "scrap_metal", 12), "send");
            Assert.IsTrue(s.SaveNow().Success, "save");
            var expected = Canonical(s);

            var reloaded = Reload(_root);
            Assert.AreEqual(expected, Canonical(reloaded), "the transfer is durable");
            Assert.AreEqual(12, QuantityIn(reloaded, CampStore(CampA), "scrap_metal"),
                "property is still in that camp's own store");
            Assert.AreEqual(0, QuantityIn(reloaded, CampaignSession.GarageStorage, "scrap_metal"));
        }

        [Test]
        public void ATransferIsCritical_AFailedVerifiedSaveRollsItBack()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 10);

            _saves.StepHook = step =>
            {
                if (step == SaveStep.TempVerified) throw new IOException("disk full");
            };
            var result = s.TransferToHub(CampA, "scrap_metal", 10);
            _saves.StepHook = null;

            Assert.AreEqual(FailureReason.SaveIoFailure, result.Reason,
                "the move is acknowledged only with a verified save (§12.4)");
            Assert.AreEqual(10, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"),
                "and a failed save rolls the whole transfer back (§17.4)");
            Assert.AreEqual(0, QuantityIn(s, CampStore(CampA), "scrap_metal"));

            Ok(s.TransferToHub(CampA, "scrap_metal", 10), "once the disk works, the same transfer succeeds");
            Assert.AreEqual(10, QuantityIn(s, CampStore(CampA), "scrap_metal"));
        }

        [Test]
        public void TransfersAnnounceThemselvesAsCommunityLogistics()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 3);
            s.DrainNotices();

            Ok(s.TransferToHub(CampA, "scrap_metal", 3), "send");

            var notices = s.DrainNotices();
            var notice = notices.FirstOrDefault(n => n.Kind == CampaignNoticeKind.LogisticsTransferred);
            Assert.AreEqual(CampaignNoticeKind.LogisticsTransferred, notice.Kind,
                "a committed transfer announces itself (§13.2)");
            Assert.AreEqual(CampA, notice.Id, "and names the hub that received the property");
            Assert.AreEqual(3, notice.Amount);
            StringAssert.Contains("Community logistics", notice.Text,
                "§10.4: explain this as community logistics, not magic access");
        }

        // ================================================================== where you may transfer

        [Test]
        public void NoFieldAccess_TransfersAreRefusedInTheOpenField()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 5);
            Ok(s.Depart(), "depart");
            DriveTo(s, OpenField);
            Assert.IsNull(s.CurrentHubId, "the open field is not a hub");

            var sent = s.TransferToHub(CampA, "scrap_metal", 5);
            Assert.AreEqual(FailureReason.WrongState, sent.Reason,
                "§10.4: no field access — stored property is not magically reachable");
            var pulled = s.TransferFromHub(CampA, "scrap_metal", 5);
            Assert.AreEqual(FailureReason.WrongState, pulled.Reason);
            Assert.AreEqual(5, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"), "nothing moved");
        }

        [Test]
        public void StandingAtACamp_ThatCampIsTheCurrentHub_AndPropertyMovesFromIt()
        {
            var s = WithCampA();
            Seed(s, CampStore(CampA), "scrap_metal", 8);
            Ok(s.Depart(), "depart");
            DriveTo(s, AtCampA);
            Assert.AreEqual(CampA, s.CurrentHubId, "inside an activated camp's socket, the camp is the hub");

            Ok(s.TransferToHub(Home, "scrap_metal", 8), "send it to the home garage");
            Assert.AreEqual(0, QuantityIn(s, CampStore(CampA), "scrap_metal"));
            Assert.AreEqual(8, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"));

            Ok(s.TransferFromHub(Home, "scrap_metal", 8), "and pull it back to the camp");
            Assert.AreEqual(8, QuantityIn(s, CampStore(CampA), "scrap_metal"),
                "a camp is a full logistics partner, not just a drop-off");
        }

        [Test]
        public void AnUnactivatedHubCannotSendOrReceive()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 5);

            var unestablished = s.TransferToHub(CampB, "scrap_metal", 5);
            Assert.AreEqual(FailureReason.InvalidTarget, unestablished.Reason, "a camp nobody has established");
            StringAssert.Contains("not activated", unestablished.Detail);
            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferToHub(Canyon, "scrap_metal", 5).Reason,
                "a garage nobody has unlocked");
            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferFromHub(Canyon, "scrap_metal", 5).Reason);
            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferToHub(string.Empty, "scrap_metal", 5).Reason,
                "and an empty selection");
            Assert.AreEqual(5, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"), "nothing moved");
        }

        [Test]
        public void TwoGaragesShareOneUnlimitedStore_SoGarageToGarageIsRefused()
        {
            var s = WithCampA();
            s.Garage.UnlockGarage(Canyon);
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 5);

            Assert.IsTrue(s.HubStore(Home).Value.Equals(CampaignSession.GarageStorage),
                "the home garage addresses the shared garage store");
            Assert.IsTrue(s.HubStore(Canyon).Value.Equals(s.HubStore(Home).Value),
                "§10.4: garage material storage is ONE unlimited store, shared by every garage");

            var refused = s.TransferToHub(Canyon, "scrap_metal", 5);
            Assert.AreEqual(FailureReason.WrongState, refused.Reason,
                "so there is nothing to move between two garages");
            StringAssert.Contains("garage", refused.Detail, "and explains why, not just that it refused");
            Assert.AreEqual(5, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"));
        }

        [Test]
        public void TransferToTheHubYouAreAlreadyAt_IsRefused()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 4);

            var sent = s.TransferToHub(Home, "scrap_metal", 4);
            Assert.AreEqual(FailureReason.WrongState, sent.Reason, "already at that hub");
            StringAssert.Contains("already at that hub", sent.Detail,
                "and says so, instead of blaming the shared garage store");
            var pulled = s.TransferFromHub(Home, "scrap_metal", 4);
            Assert.AreEqual(FailureReason.WrongState, pulled.Reason);
            StringAssert.Contains("already at that hub", pulled.Detail);
            Assert.AreEqual(4, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"));
        }

        [Test]
        public void LogisticsHubs_AreDeterministicallyOrdered()
        {
            var s = WithCampA();
            CollectionAssert.AreEqual(new[] { Home, CampA }, s.LogisticsHubs(),
                "garages first, then camps, each sorted — so a UI list and a capture agree");

            s.Garage.UnlockGarage(Canyon);
            CollectionAssert.AreEqual(new[] { Canyon, Home, CampA }, s.LogisticsHubs());
            Assert.IsNull(s.HubStore("camp.canyon.a"), "an unactivated hub addresses no store");
        }

        // ================================================================== what may move

        [Test]
        public void UnknownItemsProtectedObjectsAndEmptyQuantities_AreRefused()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 5);

            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferToHub(CampA, "unobtainium", 1).Reason);
            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferToHub(CampA, string.Empty, 1).Reason);
            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferToHub(CampA, "scrap_metal", 0).Reason,
                "nothing to transfer");
            Assert.AreEqual(FailureReason.InvalidTarget, s.TransferToHub(CampA, "scrap_metal", -3).Reason);
            Assert.AreEqual(FailureReason.ItemNotUsableHere, s.TransferToHub(CampA, "sensor_core", 1).Reason,
                "protected ledger objects never move through ordinary slots (§10.4)");
            Assert.AreEqual(5, QuantityIn(s, CampaignSession.GarageStorage, "scrap_metal"));
            Assert.AreEqual(0, QuantityIn(s, CampStore(CampA), "scrap_metal"));
        }

        [Test]
        public void ACampStoreIsMassLimited_AndAFailedTransferMovesNothing()
        {
            var s = WithCampA();
            Assert.AreEqual(500f, s.CampStoreCapacityKg(CampA), 1e-3f, "§10.4: camp storage holds 500 kg");
            Seed(s, CampaignSession.GarageStorage, "camp_kit", 60);   // 12 kg each, one per slot

            Ok(s.TransferToHub(CampA, "camp_kit", 41), "41 kits = 492 kg, inside the limit");
            Assert.AreEqual(492f, s.Inventory.TotalMassKg(CampStore(CampA)), 1e-3f);

            var refused = s.TransferToHub(CampA, "camp_kit", 1);
            Assert.IsFalse(refused.Success);
            Assert.AreEqual(FailureReason.InsufficientMassCapacity, refused.Reason,
                "the 500 kg camp limit binds, and the transfer is atomic");
            Assert.AreEqual(41, QuantityIn(s, CampStore(CampA), "camp_kit"), "nothing arrived");
            Assert.AreEqual(19, QuantityIn(s, CampaignSession.GarageStorage, "camp_kit"), "nothing left");
        }

        // ================================================================== garage panel (§13.3)

        [Test]
        public void TheGaragePanelListsEveryActivatedHubWithItsCapacity()
        {
            var s = WithCampA();
            Seed(s, CampaignSession.GarageStorage, "scrap_metal", 7);
            Seed(s, CampStore(CampA), "polymer", 4);

            var model = GarageModel.Build(s);
            Assert.AreEqual(Home, model.CurrentHubId);
            Assert.AreEqual(2, model.Logistics.Count, "the home garage and camp A");

            var home = model.Logistics.First(r => r.HubId == Home);
            Assert.AreEqual("Home Garage", home.Name);
            Assert.IsTrue(home.IsCurrent && home.SharedStore, "you are standing at the shared store");
            Assert.IsFalse(home.CanExchange, "so there is nothing to exchange with yourself");
            Assert.AreEqual(7, home.Stacks.Sum(st => st.Quantity));

            var camp = model.Logistics.First(r => r.HubId == CampA);
            Assert.AreEqual("Forest A Camp", camp.Name);
            Assert.IsFalse(camp.IsCurrent || camp.SharedStore);
            Assert.IsTrue(camp.CanExchange);
            Assert.AreEqual(500f, camp.CapacityKg, 1e-3f, "§10.4: show capacity");
            Assert.AreEqual(2f, camp.MassKg, 1e-3f, "4 × 0.5 kg polymer");
            CollectionAssert.AreEqual(new[] { "polymer" }, camp.Stacks.Select(st => st.ItemId));
            Assert.IsFalse(camp.Stacks[0].Sellable, "you cannot sell from a hub you are not at");
        }
    }
}
