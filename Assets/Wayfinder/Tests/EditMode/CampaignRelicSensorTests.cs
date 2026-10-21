using System.Collections.Generic;
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
    /// MAP-03 (§8.3). Two halves.
    ///
    /// Relics: five sets of five named relics loaded from <c>relics.json</c>, collected into the
    /// protected relic ledger with no weight, unsellable, each claiming its own reward key once, and a
    /// completed biome set claiming a separate set key that unlocks cosmetic memorabilia and journal
    /// content — never essential traction or fuel.
    ///
    /// Sensor loop: a passive pulse inside the resolved signal radius (80 m baseline, Radar I 140 m,
    /// Radar II 220 m), with directional precision and the true position only inside 45 m and a search
    /// area instead of exact navigation beyond it. Before this batch <c>Stat.SignalRadiusM</c> was
    /// composed correctly by research and read by nothing, so the radar upgrades changed no behaviour;
    /// these tests pin that they now change what the sensor hears.
    /// </summary>
    public class CampaignRelicSensorTests
    {
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

        /// <summary>Every canonical tier up to and including <paramref name="tier"/> (§11.2 ordering).</summary>
        static List<string> Through(CampaignSession s, int tier) =>
            s.Research.Tiers.Where(t => t.Tier <= tier).Select(t => t.Id.Value).ToList();

        static List<string> ForestRelics => Content.RelicSets["forest"];

        // ============================================================== relic content and ledger

        [Test]
        public void RelicContentIsFiveBiomeSetsOfFive_EachProtectedAndUnsellable()
        {
            Assert.AreEqual(25, Content.Relics.Count, "twenty-five named relics (§8.3)");
            Assert.AreEqual(5, Content.RelicSets.Count, "one set per biome (§8.3)");
            foreach (var kv in Content.RelicSets)
                Assert.AreEqual(5, kv.Value.Count, $"{kv.Key}: five relics per set (§8.3)");

            CollectionAssert.AreEqual(
                new[] { "relic.forest.01", "relic.forest.02", "relic.forest.03", "relic.forest.04",
                        "relic.forest.05" },
                Content.RelicSets["forest"],
                "a set's order is deterministic (Ordinal), so a capture and a UI always agree");

            var rewardKeys = new HashSet<string>();
            foreach (var relic in Content.Relics.Values)
            {
                Assert.IsFalse(string.IsNullOrEmpty(relic.Biome), $"{relic.Id}: belongs to a biome");
                Assert.IsTrue(Content.RelicSets.ContainsKey(relic.Biome),
                    $"{relic.Id}: biome '{relic.Biome}' has a set");
                Assert.IsTrue(Content.RelicSets[relic.Biome].Contains(relic.Id),
                    $"{relic.Id}: is listed in its own biome's set");
                Assert.IsTrue(relic.ProtectedLedger, $"{relic.Id}: lives in the protected ledger (§8.3)");
                Assert.IsFalse(relic.Sellable, $"{relic.Id}: cannot be sold (§8.3)");
                Assert.IsTrue(rewardKeys.Add(relic.RewardKey), $"{relic.Id}: reward key is unique (§8.4)");
            }
        }

        [Test]
        public void CollectingARelicFillsTheLedger_NotTheCargo()
        {
            var s = NewGame(_saves);
            s.Depart();
            int slotsBefore = s.Inventory.UsedSlots(CampaignSession.Cargo);
            float cargoMassBefore = s.ResolveStats().CargoMassKg;
            int cargoSlotsBefore = s.ResolveStats().CargoSlots;

            Assert.IsTrue(s.CollectRelic("relic.forest.01").Success, "a placed relic can be collected");
            Assert.IsTrue(s.Discovery.HasRelic("relic.forest.01"));
            Assert.AreEqual(1, s.Discovery.RelicCount);
            Assert.AreEqual(1, s.RelicsFoundInSet("forest"));

            // §8.3: relics have no weight and are not ordinary cargo.
            Assert.AreEqual(slotsBefore, s.Inventory.UsedSlots(CampaignSession.Cargo), "no cargo slot is used");
            Assert.AreEqual(cargoMassBefore, s.ResolveStats().CargoMassKg, 1e-3f, "no mass is added");
            Assert.AreEqual(cargoSlotsBefore, s.ResolveStats().CargoSlots, "cargo capacity is untouched");
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.Cargo, "relic.forest.01"),
                "a relic never becomes a cargo stack");
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.GarageStorage, "relic.forest.01"),
                "nor a garage stack — it cannot be stored, transferred or sold");
        }

        [Test]
        public void ARelicIsCollectedOnce_AndClaimsItsOwnRewardKeyOnce()
        {
            var s = NewGame(_saves);
            s.Depart();
            int claimedBefore = s.Discovery.RewardLedger.Count;

            Assert.IsTrue(s.CollectRelic("relic.forest.01").Success);
            Assert.AreEqual(claimedBefore + 1, s.Discovery.RewardLedger.Count,
                "the relic's own reward key is claimed (§8.4)");
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed("reward.relic.forest.01"));

            var again = s.CollectRelic("relic.forest.01");
            Assert.IsFalse(again.Success, "a relic cannot be collected twice");
            Assert.AreEqual(FailureReason.AlreadyCollected, again.Reason);
            Assert.AreEqual(claimedBefore + 1, s.Discovery.RewardLedger.Count, "and cannot pay twice");
            Assert.AreEqual(1, s.Discovery.RelicCount);
        }

        [Test]
        public void AnUnknownRelicAndNoActiveExpeditionAreBothRefused()
        {
            var parked = NewGame(_saves);
            var noField = parked.CollectRelic("relic.forest.01");
            Assert.IsFalse(noField.Success, "parked: there is no field to collect in");
            Assert.AreEqual(FailureReason.WrongState, noField.Reason);
            Assert.AreEqual(0, parked.Discovery.RelicCount);

            parked.Depart();
            var unknown = parked.CollectRelic("relic.nowhere.99");
            Assert.IsFalse(unknown.Success);
            Assert.AreEqual(FailureReason.ContentMissing, unknown.Reason,
                "the id must exist in relics.json — there is no hardcoded relic table (§14)");
            Assert.AreEqual(0, parked.Discovery.RelicCount, "a refused collection claims nothing");

            var empty = parked.CollectRelic("");
            Assert.IsFalse(empty.Success, "an empty id is refused, not treated as content");
            Assert.AreEqual(FailureReason.ContentMissing, empty.Reason);
        }

        [Test]
        public void CompletingABiomesSetClaimsTheSetKeyOnce_AndAnnouncesIt()
        {
            var s = NewGame(_saves);
            s.Depart();
            var set = ForestRelics;
            Assert.AreEqual(5, set.Count, "the forest set has five named relics (§8.3)");

            for (int i = 0; i < set.Count - 1; i++)
            {
                Assert.IsTrue(s.CollectRelic(set[i]).Success, set[i]);
                Assert.IsFalse(s.IsRelicSetComplete("forest"), "four of five is not a completed set");
                Assert.AreEqual(0, s.CompletedRelicSets().Count);
                Assert.AreEqual(i + 1, s.RelicsFoundInSet("forest"));
            }
            Assert.IsFalse(s.Discovery.RewardLedger.IsClaimed(CampaignSession.RelicSetRewardKey("forest")),
                "an incomplete set pays no set reward");

            Assert.IsTrue(s.CollectRelic(set[set.Count - 1]).Success);
            Assert.IsTrue(s.IsRelicSetComplete("forest"));
            Assert.AreEqual(new[] { "forest" }, s.CompletedRelicSets().ToArray());
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed(CampaignSession.RelicSetRewardKey("forest")),
                "the completed set claims a separate reward key (§8.4)");

            var notices = s.DrainNotices();
            var completion = notices.FirstOrDefault(n => n.Kind == CampaignNoticeKind.RelicSetCompleted);
            Assert.IsNotNull(completion.Id, "the player is told the set unlocked memorabilia and journal content");
            Assert.AreEqual("forest", completion.Id, "the notice names the biome");
            Assert.AreEqual(set.Count, completion.Amount, "and reports the set size");
            StringAssert.Contains("memorabilia", completion.Text, "§8.3: the reward is cosmetic, not mechanical");
            Assert.IsTrue(notices.Count(n => n.Kind == CampaignNoticeKind.RelicCollected) >= set.Count,
                "each relic announced its own recovery");

            int claimed = s.Discovery.RewardLedger.Count;
            Assert.IsFalse(s.CollectRelic(set[set.Count - 1]).Success);
            Assert.AreEqual(claimed, s.Discovery.RewardLedger.Count, "the set key is claimed exactly once");
        }

        [Test]
        public void ACompletedRelicSetChangesNoVehicleStat()
        {
            var s = NewGame(_saves);
            s.Depart();
            var before = s.ResolveStats();
            float gripBefore = before.EffectiveGrip;
            float tireGripBefore = before.TireGripFactor;
            float signalBefore = before.SignalRadiusM;
            float payloadBefore = before.PayloadKg;
            float fuelBefore = before.FuelCapacityLiters;
            float wadingBefore = before.SafeWadingDepthM;
            float clearanceBefore = before.GroundClearanceM;
            float torqueBefore = before.PeakTorqueNm;

            foreach (var id in ForestRelics) Assert.IsTrue(s.CollectRelic(id).Success, id);
            Assert.IsTrue(s.IsRelicSetComplete("forest"));

            // §8.3: a set unlocks cosmetic expedition memorabilia and journal content, "not essential
            // traction or fuel" — so no resolved stat may move.
            var after = s.ResolveStats();
            Assert.AreEqual(gripBefore, after.EffectiveGrip, 1e-4f, "no traction from a relic set");
            Assert.AreEqual(tireGripBefore, after.TireGripFactor, 1e-4f, "no tire grip");
            Assert.AreEqual(signalBefore, after.SignalRadiusM, 1e-4f, "no sensor range");
            Assert.AreEqual(payloadBefore, after.PayloadKg, 1e-4f, "no payload");
            Assert.AreEqual(fuelBefore, after.FuelCapacityLiters, 1e-4f, "no fuel capacity");
            Assert.AreEqual(wadingBefore, after.SafeWadingDepthM, 1e-4f, "no wading depth");
            Assert.AreEqual(clearanceBefore, after.GroundClearanceM, 1e-4f, "no ground clearance");
            Assert.AreEqual(torqueBefore, after.PeakTorqueNm, 1e-4f, "no torque");
        }

        [Test]
        public void ARelicIsNeverSellable_BecauseItIsNeverAnInventoryItem()
        {
            var s = NewGame(_saves);
            Assert.IsTrue(s.AtGarage, "a new game is parked at home, where selling is legal");

            // §8.3: a relic cannot be sold. That guarantee is structural — a relic exists only in the
            // protected ledger and is never an item, so no sell path can reach it at all.
            var check = s.CheckSell("relic.forest.01", 1, true, out int price);
            Assert.IsFalse(check.Success, "selling a relic is refused");
            Assert.AreEqual(0, price, "and nothing is offered for it");
            Assert.IsFalse(s.SellItems("relic.forest.01", 1, true).Success, "the sale itself is refused too");

            s.Depart();
            Assert.IsTrue(s.CollectRelic("relic.forest.01").Success);
            Assert.AreEqual(0, s.Inventory.QuantityOf(CampaignSession.Cargo, "relic.forest.01"),
                "collecting creates no sellable stock");
            Assert.IsTrue(s.Discovery.HasRelic("relic.forest.01"), "the relic is held in the ledger");
            Assert.AreEqual(1, s.Discovery.RelicCount, "and cannot be lost");
        }

        [Test]
        public void RelicsAndCompletedSetsSurviveSaveAndReload()
        {
            var s = NewGame(_saves);
            s.Depart();
            foreach (var id in ForestRelics) Assert.IsTrue(s.CollectRelic(id).Success, id);
            Assert.IsTrue(s.CollectRelic("relic.canyon.01").Success, "a second biome, only partly found");
            Assert.AreEqual(6, s.Discovery.RelicCount);
            string snapshot = Canonical(s);
            StringAssert.Contains("relic.forest.05", snapshot,
                "the relic ledger is part of the canonical save");
            StringAssert.Contains("relic.canyon.01", snapshot,
                "including the second biome's single relic");

            var reloaded = Reload(_root);
            Assert.AreEqual(6, reloaded.Discovery.RelicCount, "all six relics are restored");
            Assert.IsTrue(reloaded.Discovery.HasRelic("relic.forest.05"));
            Assert.IsTrue(reloaded.IsRelicSetComplete("forest"), "set completion is derived from the ledger");
            Assert.IsFalse(reloaded.IsRelicSetComplete("canyon"), "and one relic of five is not complete");
            Assert.AreEqual(1, reloaded.RelicsFoundInSet("canyon"));
            Assert.AreEqual(new[] { "forest" }, reloaded.CompletedRelicSets().ToArray());
            Assert.IsTrue(reloaded.Discovery.RewardLedger.IsClaimed(CampaignSession.RelicSetRewardKey("forest")),
                "the set reward stays claimed, so a reload cannot pay it again");
        }

        // ==================================================================== §8.3 sensor loop

        [Test]
        public void TheSensorPulsesInsideTheResolvedRadius_AndNotBeyond()
        {
            var signal = new SensorLoop.Signal("site.a", 100f, 100f);

            Assert.IsTrue(SensorLoop.Read(signal, 100f, 170f, SensorLoop.BaseSignalRadiusM).Pulsing,
                "70 m is inside the passive 80 m sensor (§8.3)");
            Assert.IsTrue(SensorLoop.Read(signal, 100f, 180f, SensorLoop.BaseSignalRadiusM).Pulsing,
                "exactly 80 m still pulses");
            Assert.IsFalse(SensorLoop.Read(signal, 100f, 181f, SensorLoop.BaseSignalRadiusM).Pulsing,
                "81 m is beyond it");
            Assert.AreEqual(0,
                SensorLoop.Scan(new[] { signal }, 100f, 400f, SensorLoop.BaseSignalRadiusM).Count,
                "a scan reports nothing when no signal is in range");
            Assert.AreEqual(0, SensorLoop.Scan(null, 0f, 0f, SensorLoop.BaseSignalRadiusM).Count,
                "and a missing signal list is an empty sweep, not a crash");
        }

        [Test]
        public void DirectionalPrecisionArrivesAtFortyFiveMetres_AndWithItTheTruePosition()
        {
            var signal = new SensorLoop.Signal("site.a", 100f, 100f);

            var close = SensorLoop.Read(signal, 100f, 140f, SensorLoop.BaseSignalRadiusM); // 40 m
            Assert.IsTrue(close.Pulsing);
            Assert.IsTrue(close.Directional, "inside 45 m the bearing is trustworthy (§8.3)");
            Assert.AreEqual(100f, close.ShowX, 1e-3f, "so the true position may be drawn");
            Assert.AreEqual(100f, close.ShowZ, 1e-3f);
            Assert.AreEqual(0f, close.ShowRadiusM, 1e-3f, "and no search area is needed");
            Assert.AreEqual(180f, close.BearingDegrees, 0.5f, "the signal lies due -Z from the observer");

            var atFortyFive = SensorLoop.Read(signal, 100f, 145f, SensorLoop.BaseSignalRadiusM);
            Assert.IsTrue(atFortyFive.Directional, "exactly 45 m is still precise (§8.3)");
            var justBeyond = SensorLoop.Read(signal, 100f, 145.5f, SensorLoop.BaseSignalRadiusM);
            Assert.IsFalse(justBeyond.Directional, "and precision is lost immediately beyond it");

            Assert.AreEqual(0f,
                SensorLoop.Read(new SensorLoop.Signal("site.n", 100f, 140f), 100f, 100f,
                    SensorLoop.BaseSignalRadiusM).BearingDegrees, 0.5f, "0° is +Z");
            Assert.AreEqual(90f,
                SensorLoop.Read(new SensorLoop.Signal("site.e", 140f, 100f), 100f, 100f,
                    SensorLoop.BaseSignalRadiusM).BearingDegrees, 0.5f, "90° is +X, clockwise");
            Assert.AreEqual(270f,
                SensorLoop.Read(new SensorLoop.Signal("site.w", 60f, 100f), 100f, 100f,
                    SensorLoop.BaseSignalRadiusM).BearingDegrees, 0.5f, "270° is -X");
        }

        [Test]
        public void BeyondFortyFiveMetresTheMapShowsTheSearchAreaCentre_NotTheExactPosition()
        {
            var signal = new SensorLoop.Signal("site.a", 100f, 100f);
            var far = SensorLoop.Read(signal, 100f, 160f, SensorLoop.BaseSignalRadiusM); // 60 m

            Assert.IsTrue(far.Pulsing, "60 m is inside 80 m, so it still pulses");
            Assert.IsFalse(far.Directional, "but it is beyond the 45 m precision range");
            Assert.AreEqual(0f, far.BearingDegrees, 1e-3f, "no bearing is claimed beyond 45 m");
            Assert.AreEqual(MapMarkerRules.SearchAreaRadiusMeters, far.ShowRadiusM, 1e-3f,
                "a search area is drawn instead");

            // §8.3: "the map shows a search area, not exact vertical navigation". The centre shown must
            // be the same deterministic search-area centre the marker rules already use, so the sensor
            // and the map can never disagree about where to look.
            var centre = MapMarkerRules.SearchAreaCenter(signal.Id, signal.X, signal.Z);
            Assert.AreEqual(centre.x, far.ShowX, 1e-3f, "the sensor draws the deterministic search-area centre");
            Assert.AreEqual(centre.z, far.ShowZ, 1e-3f);

            var sameIdFromElsewhere = SensorLoop.Read(signal, 0f, 0f, SensorLoop.RadarIISignalRadiusM);
            Assert.AreEqual(far.ShowX, sameIdFromElsewhere.ShowX, 1e-3f,
                "the same signal always shows the same search area, whoever is looking");
            Assert.AreEqual(far.ShowZ, sameIdFromElsewhere.ShowZ, 1e-3f);
        }

        [Test]
        public void RadarIAndRadarIIExtendWhatTheSensorHears_EndToEndThroughResearch()
        {
            var s = NewGame(_saves);
            var site = Content.Sites.Values.First();
            float sx = site.Position.X, sz = site.Position.Z;

            // 130 m from the site: beyond the passive 80 m sensor, inside Radar I's 140 m (§11.2 tier 9).
            float at130 = sx + 130f;
            Assert.AreEqual(SensorLoop.BaseSignalRadiusM, s.ResolveStats().SignalRadiusM, 1e-3f,
                "a new game has the 80 m baseline");
            Assert.IsFalse(s.SensorScan(at130, sz).Any(r => r.Id == site.Id),
                "80 m cannot hear a site 130 m away");

            s.Research.Restore(Through(s, 9));
            Assert.AreEqual(SensorLoop.RadarISignalRadiusM, s.ResolveStats().SignalRadiusM, 1e-3f,
                "Radar I raises the signal radius to 140 m (§11.2 tier 9)");
            Assert.IsTrue(s.SensorScan(at130, sz).Any(r => r.Id == site.Id),
                "so the very same site now pulses — the upgrade changes behaviour, not just a number");

            // 200 m from the site: beyond Radar I, inside Radar II's 220 m (§11.2 tier 19).
            float at200 = sx + 200f;
            Assert.IsFalse(s.SensorScan(at200, sz).Any(r => r.Id == site.Id), "140 m cannot hear 200 m");

            s.Research.Restore(Through(s, 19));
            Assert.AreEqual(SensorLoop.RadarIISignalRadiusM, s.ResolveStats().SignalRadiusM, 1e-3f,
                "Radar II raises it to 220 m (§11.2 tier 19)");
            Assert.IsTrue(s.SensorScan(at200, sz).Any(r => r.Id == site.Id), "and 200 m now pulses");
        }

        [Test]
        public void TheScanReportsNearestFirst_AndOnlyPulsingSignals()
        {
            var signals = new[]
            {
                new SensorLoop.Signal("far", 0f, 300f),
                new SensorLoop.Signal("near", 0f, 20f),
                new SensorLoop.Signal("mid", 0f, 60f),
                new SensorLoop.Signal("edge", 0f, 80f),
            };
            var hits = SensorLoop.Scan(signals, 0f, 0f, SensorLoop.BaseSignalRadiusM);

            Assert.AreEqual(3, hits.Count, "the 300 m signal is out of range and is not reported");
            CollectionAssert.AreEqual(new[] { "near", "mid", "edge" }, hits.Select(r => r.Id).ToArray(),
                "nearest first, so the HUD can lead with the strongest pulse");
            for (int i = 1; i < hits.Count; i++)
                Assert.LessOrEqual(hits[i - 1].DistanceM, hits[i].DistanceM);
            Assert.IsTrue(hits.All(r => r.Pulsing), "every reported reading pulses");
            Assert.AreEqual(1,
                SensorLoop.Scan(signals, 0f, 0f, SensorLoop.DirectionalPrecisionRadiusM).Count,
                "a 45 m radius hears only the 20 m signal: the precision range is not a pulse range");
        }

        [Test]
        public void AMissingSignalRadiusFallsBackToTheBaseline_NotToSilenceOrToUnlimitedRange()
        {
            var inside = new SensorLoop.Signal("site.a", 0f, 70f);
            var outside = new SensorLoop.Signal("site.b", 0f, 90f);

            foreach (var broken in new[] { 0f, float.NaN, -5f })
            {
                Assert.IsTrue(SensorLoop.Read(inside, 0f, 0f, broken).Pulsing,
                    $"radius {broken} falls back to the 80 m baseline (§8.3), so the sensor is not deafened");
                Assert.IsFalse(SensorLoop.Read(outside, 0f, 0f, broken).Pulsing,
                    $"radius {broken} falls back to 80 m, not to unlimited range");
            }
        }

        [Test]
        public void TheSensorSweepsFromTheVehiclesOwnPose_AndIsSilentWhenParked()
        {
            var site = Content.Sites.Values.First();
            var onSite = new Pose(new WorldPoint(site.Position.X, 0f, site.Position.Z), 0f);

            // Parked: the sensor does not sweep at all. That has to stay true with the pose sitting on
            // top of a signal, otherwise "parked" and "nowhere near anything" are indistinguishable and
            // a sweep that ignores the parked state passes silently.
            var parked = NewGame(_saves);
            parked.Expedition.Pose = onSite;
            Assert.AreEqual(0, parked.SensorScan().Count,
                "parked: there is no field to sense, even with the pose on top of a signal");

            var s = NewGame(_saves);
            s.Depart();
            s.Expedition.Pose = onSite;
            var hits = s.SensorScan();
            Assert.IsTrue(hits.Any(r => r.Id == site.Id),
                "the sweep follows the vehicle, so a site underneath it pulses");
            CollectionAssert.AreEqual(
                s.SensorScan(site.Position.X, site.Position.Z).Select(r => r.Id).ToArray(),
                hits.Select(r => r.Id).ToArray(),
                "the pose overload and the explicit overload agree at the same position");
            Assert.IsTrue(hits.All(r => r.Pulsing), "only pulsing signals are reported");
            Assert.AreEqual(s.ResolveStats().SignalRadiusM, SensorLoop.BaseSignalRadiusM, 1e-3f,
                "an unresearched vehicle sweeps at the 80 m baseline");

            // Drive 300 m away: the same site goes quiet, so the sweep cannot be reading a fixed point.
            s.Expedition.Pose = new Pose(new WorldPoint(site.Position.X + 300f, 0f, site.Position.Z), 0f);
            Assert.IsFalse(s.SensorScan().Any(r => r.Id == site.Id),
                "after driving away the site is out of range");
        }

        [Test]
        public void TheSensorSweepsThePlacedSitesFromContent_NotAHardcodedTable()
        {
            var s = NewGame(_saves);
            Assert.Greater(Content.Sites.Count, 0, "the slice places signal sites (§14: content, not code)");

            // Standing exactly on a placed site must hear it, whatever its id, kind or biome.
            foreach (var kv in Content.Sites)
            {
                var site = kv.Value;
                var hits = s.SensorScan(site.Position.X, site.Position.Z);
                var mine = hits.FirstOrDefault(r => r.Id == site.Id);
                Assert.AreEqual(site.Id, mine.Id, $"{site.Id}: a signal at the observer's feet pulses");
                Assert.AreEqual(0f, mine.DistanceM, 1e-3f, $"{site.Id}: and reports zero distance");
                Assert.IsTrue(mine.Directional, $"{site.Id}: at zero distance the bearing is exact");
                Assert.AreEqual(site.Position.X, mine.ShowX, 1e-3f,
                    $"{site.Id}: inside 45 m the true position may be shown");
            }
        }
    }
}
