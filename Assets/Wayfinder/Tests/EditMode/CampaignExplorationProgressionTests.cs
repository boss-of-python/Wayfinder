using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;
using Wayfinder.World;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    /// <summary>
    /// MAP-03 (§8.4) — exploration rewards and the finale's map gate.
    ///
    /// §8.4: every new landmark pays 120 Salvage once; every 50 newly revealed countable cells grants
    /// 50 Salvage once, with a persistent count and paid-threshold index so a claim cannot duplicate; a
    /// relay mission may award its story reward in addition to a landmark reward but the ledger records
    /// separate reward keys; 100% exploration is optional and the finale needs 60% countable coverage,
    /// all five relays and the main mission sequence.
    ///
    /// §8.4 also mandates a target progression test — "story routes plus reasonable detours can achieve
    /// 60%; otherwise adjust route exposure or the threshold". That test is here, and it reports the
    /// target is NOT met by the content that exists today (KI-048).
    /// </summary>
    public class CampaignExplorationProgressionTests
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

        /// <summary>
        /// Every point of interest the slice places, plus each checkpoint route walked at 32 m steps —
        /// "story routes plus reasonable detours" as current content defines them.
        /// </summary>
        static IEnumerable<(float x, float z, string what)> PlacedTargets()
        {
            foreach (var kv in Content.Landmarks)
                yield return (kv.Value.Position.X, kv.Value.Position.Z, "landmark " + kv.Key);
            foreach (var kv in Content.Caches)
                yield return (kv.Value.Position.X, kv.Value.Position.Z, "cache " + kv.Key);
            foreach (var kv in Content.Sites)
                yield return (kv.Value.Position.X, kv.Value.Position.Z, "site " + kv.Key);
            foreach (var kv in Content.Garages)
                yield return (kv.Value.Position.X, kv.Value.Position.Z, "garage " + kv.Key);
            // Camps carry no position of their own — they are placed by their sockets (§12.2).
            foreach (var socket in Content.Slice.CampSockets)
                yield return (socket.Position.X, socket.Position.Z, "camp socket " + socket.Id);

            foreach (var route in Content.Slice.CheckpointRoutes)
            {
                var pts = route.Checkpoints;
                for (int i = 0; i + 1 < pts.Count; i++)
                {
                    var a = pts[i];
                    var b = pts[i + 1];
                    float dx = b.X - a.X, dz = b.Z - a.Z;
                    float len = (float)System.Math.Sqrt(dx * dx + dz * dz);
                    int steps = System.Math.Max(1, (int)(len / 32f));
                    for (int k = 0; k <= steps; k++)
                        yield return (a.X + dx * k / steps, a.Z + dz * k / steps, $"route {route.Id} leg {i}");
                }
            }
        }

        /// <summary>Reveals countable cells until at least <paramref name="cells"/> are revealed.</summary>
        static void RevealAtLeast(CampaignSession s, int cells)
        {
            // Walk a long line at 32 m steps: each stop reveals a 96 m disc (§8.1), terrain-filtered.
            for (float x = 700f; x <= 5600f && s.Discovery.RevealedCountableCells < cells; x += 32f)
                s.RevealAround(x, 1500f, false);
        }

        // ======================================================= §8.4 coverage and mapping rewards

        [Test]
        public void CoverageIsZeroOnANewGame_AndIsMeasuredAgainstTheCountableMask()
        {
            var s = NewGame(_saves);
            Assert.AreEqual(0, s.Discovery.RevealedCountableCells, "a new game has revealed nothing (§8.1)");
            Assert.AreEqual(0f, s.Discovery.MapCoveragePercent, 1e-4f, "so coverage is 0%");
            Assert.Greater(s.Discovery.CountableMask.CountSet(), 0, "the countable mask is baked");
            Assert.IsFalse(s.Discovery.MeetsFinaleMapRequirement(), "0% does not meet the 60% finale gate");

            s.Depart();
            s.RevealAround(1024f, 2200f, false);
            Assert.Greater(s.Discovery.RevealedCountableCells, 0, "driving reveals cells");
            Assert.Greater(s.Discovery.MapCoveragePercent, 0f, "and coverage rises above zero");
        }

        [Test]
        public void EveryFiftyCountableCellsPaysFiftySalvageOnce_UnderItsOwnRewardKey()
        {
            var s = NewGame(_saves);
            s.Depart();
            Assert.AreEqual(0, s.Economy.EscrowedSalvage, "nothing is escrowed before any mapping");
            Assert.AreEqual(0, s.Discovery.MappingRewardsPaidThroughCount);

            RevealAtLeast(s, DiscoveryService.MappingRewardThresholdCells);
            Assert.GreaterOrEqual(s.Discovery.RevealedCountableCells,
                DiscoveryService.MappingRewardThresholdCells, "at least one threshold's worth of cells");

            // §8.4: the reward is escrowed, not banked — it lands when the run is banked.
            Assert.AreEqual(DiscoveryService.MappingRewardSalvage, s.Economy.EscrowedSalvage,
                "50 newly revealed countable cells escrow exactly 50 Salvage");
            Assert.AreEqual(DiscoveryService.MappingRewardThresholdCells,
                s.Discovery.MappingRewardsPaidThroughCount, "the paid-threshold index advances by 50");
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed("reward.map.50"),
                "under its own reward key, so it can never be claimed twice");

            // Revealing more within the same 50-cell band pays nothing further.
            int escrow = s.Economy.EscrowedSalvage;
            s.RevealAround(1100f, 1520f, false);
            if (s.Discovery.RevealedCountableCells < 2 * DiscoveryService.MappingRewardThresholdCells)
                Assert.AreEqual(escrow, s.Economy.EscrowedSalvage,
                    "no second payment before the next 50-cell threshold");
        }

        [Test]
        public void ThePaidThresholdIndexSurvivesReload_AndCannotPayTheSameThresholdTwice()
        {
            var s = NewGame(_saves);
            s.Depart();
            RevealAtLeast(s, 2 * DiscoveryService.MappingRewardThresholdCells);
            int paidThrough = s.Discovery.MappingRewardsPaidThroughCount;
            Assert.GreaterOrEqual(paidThrough, 2 * DiscoveryService.MappingRewardThresholdCells);
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed("reward.map.50"));
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed("reward.map.100"),
                "each threshold gets its own key, so the index and the ledger agree");

            // RevealAround only marks the autosave dirty, so write the save explicitly before reloading.
            Assert.IsTrue(s.SaveNow().Success, "the run is saved before the reload");
            var reloaded = Reload(_root);
            Assert.AreEqual(paidThrough, reloaded.Discovery.MappingRewardsPaidThroughCount,
                "the persistent count is restored (§8.4)");
            Assert.AreEqual(s.Discovery.RevealedCountableCells, reloaded.Discovery.RevealedCountableCells,
                "and so is the reveal itself");
            Assert.IsTrue(reloaded.Discovery.RewardLedger.IsClaimed("reward.map.100"),
                "the claimed keys are restored too");

            // Re-revealing the same ground after a reload must not pay again.
            reloaded.Depart();
            int escrowBefore = reloaded.Economy.EscrowedSalvage;
            int cellsBefore = reloaded.Discovery.RevealedCountableCells;
            RevealAtLeast(reloaded, cellsBefore);
            Assert.AreEqual(cellsBefore, reloaded.Discovery.RevealedCountableCells,
                "already-revealed cells are not newly revealed");
            Assert.AreEqual(escrowBefore, reloaded.Economy.EscrowedSalvage,
                "so a reloaded save cannot re-earn the same mapping rewards");
        }

        [Test]
        public void ALandmarkPays120Once_UnderALandmarkKey_NotAMappingKey()
        {
            var s = NewGame(_saves);
            s.Depart();
            // A NON-vista landmark, so discovering it reveals no 384 m vista disc and crosses no mapping
            // threshold: the 120 Salvage can be asserted exactly. The vista case is its own test below.
            string id = Content.Slice.Landmarks.First(l => !l.Vista).Id;

            var found = s.DiscoverLandmark(id, DiscoveryService.LandmarkDiscoveryMaxSpeedKph - 1f,
                DiscoveryService.LandmarkDiscoveryHoldSeconds + 0.5f);
            Assert.IsTrue(found.Success, found.Detail);
            Assert.AreEqual(0, s.Discovery.MappingRewardsPaidThroughCount,
                "a non-vista landmark crosses no mapping threshold");
            Assert.AreEqual(DiscoveryService.LandmarkDiscoveryRewardSalvage, s.Economy.EscrowedSalvage,
                "§8.4: a new landmark escrows 120 Salvage");
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed($"reward.landmark.{id}"),
                "under a landmark-namespaced key");
            Assert.IsFalse(s.Discovery.RewardLedger.IsClaimed("reward.map.50"),
                "a landmark reward is not a mapping reward — the keys stay separate");

            var again = s.DiscoverLandmark(id, 5f, 3f);
            Assert.IsFalse(again.Success, "the same landmark cannot be discovered twice");
            Assert.AreEqual(FailureReason.AlreadyApplied, again.Reason);
            Assert.AreEqual(DiscoveryService.LandmarkDiscoveryRewardSalvage, s.Economy.EscrowedSalvage,
                "and cannot pay twice");
        }

        [Test]
        public void AVistaLandmarkPaysItsOwn120_PlusTheMappingThresholdsItsRevealCrosses()
        {
            var s = NewGame(_saves);
            s.Depart();
            string id = Content.Slice.Landmarks.First(l => l.Vista).Id;

            // §8.1: discovering a vista surveys a 384 m disc, which reveals a lot of countable cells —
            // so §8.4's mapping thresholds fire from the same action. Both payments are correct, and
            // they must stay separable in the ledger rather than being folded into one reward.
            Assert.IsTrue(s.DiscoverLandmark(id, 5f, 3f).Success);

            int mappingPaid = s.Discovery.MappingRewardsPaidThroughCount;
            Assert.Greater(mappingPaid, 0, "a 384 m vista reveal crosses at least one 50-cell threshold");
            Assert.AreEqual(DiscoveryService.LandmarkDiscoveryRewardSalvage + mappingPaid,
                s.Economy.EscrowedSalvage,
                "120 for the landmark plus 50 per mapping threshold (the count and the Salvage agree " +
                "because both step by 50)");
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed($"reward.landmark.{id}"), "landmark key");
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed("reward.map.50"), "and the first mapping key");
        }

        [Test]
        public void MappingAndLandmarkRewardsShareOneLedger_WithSeparateKeys()
        {
            var s = NewGame(_saves);
            s.Depart();
            RevealAtLeast(s, DiscoveryService.MappingRewardThresholdCells);
            int mappingBefore = s.Discovery.MappingRewardsPaidThroughCount;
            string id = Content.Slice.Landmarks.First(l => !l.Vista).Id;
            Assert.IsTrue(s.DiscoverLandmark(id, 5f, 3f).Success);

            // §8.4: one ledger, separate keys — a mapping threshold and a landmark are both paid, and
            // neither can be confused with the other or claimed twice.
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed("reward.map.50"));
            Assert.IsTrue(s.Discovery.RewardLedger.IsClaimed($"reward.landmark.{id}"));
            Assert.AreEqual(mappingBefore + DiscoveryService.LandmarkDiscoveryRewardSalvage,
                s.Economy.EscrowedSalvage, "the mapping thresholds already paid, plus 120 for the landmark");
            Assert.IsFalse(s.Discovery.RewardLedger.TryClaim("reward.map.50"),
                "a claimed key refuses a second claim");
            Assert.IsFalse(s.Discovery.RewardLedger.TryClaim($"reward.landmark.{id}"));
        }

        // ============================================================ §8.4 the finale's 60% gate

        [Test]
        public void TheFinaleGateIsExactlyTheSixtyPercentCoverageRule()
        {
            var s = NewGame(_saves);
            s.Depart();
            var mask = s.Discovery.CountableMask;
            int total = mask.CountSet();

            // Just under the threshold: reveal 60% of the countable cells minus one.
            // 60% of 9216 is 5529.6, so 5530 cells are needed; 5529 is one cell short of the gate.
            int needed = (int)System.Math.Ceiling(total * DiscoveryService.FinaleMapCoveragePercent / 100f);
            int target = needed - 1;
            int revealed = 0;
            for (int z = 0; z < mask.Height && revealed < target; z++)
                for (int x = 0; x < mask.Width && revealed < target; x++)
                    if (mask.Get(x, z) && !s.Discovery.RevealedCells.Get(x, z))
                    {
                        s.Discovery.RevealedCells.Set(x, z, true);
                        revealed++;
                    }

            Assert.AreEqual(target, s.Discovery.RevealedCountableCells, "one cell short of 60%");
            Assert.Less(s.Discovery.MapCoveragePercent, DiscoveryService.FinaleMapCoveragePercent,
                "so coverage is below the gate");
            Assert.IsFalse(s.Discovery.MeetsFinaleMapRequirement(), "and the finale gate stays closed");

            // One more countable cell crosses it.
            bool added = false;
            for (int z = 0; z < mask.Height && !added; z++)
                for (int x = 0; x < mask.Width && !added; x++)
                    if (mask.Get(x, z) && !s.Discovery.RevealedCells.Get(x, z))
                    {
                        s.Discovery.RevealedCells.Set(x, z, true);
                        added = true;
                    }
            Assert.IsTrue(added, "there is always one more countable cell to reveal");
            Assert.GreaterOrEqual(s.Discovery.MapCoveragePercent,
                DiscoveryService.FinaleMapCoveragePercent, "coverage reaches 60%");
            Assert.IsTrue(s.Discovery.MeetsFinaleMapRequirement(), "and the gate opens at exactly 60%");
        }

        [Test]
        public void TheCountableMaskCountsEveryCell_BecauseNoCanonicalCellSitsAtOrBelowSeaLevel()
        {
            var s = NewGame(_saves);
            int total = s.Discovery.CountableMask.CountSet();
            int grid = WorldConstants.MapCellsPerSide * WorldConstants.MapCellsPerSide;

            // BakeCountableMask keeps only cells whose base height is above sea level. On the canonical
            // seed nothing falls below it, so the mask excludes no cell at all — the 60% denominator is
            // the whole 96x96 map, not the playable slice. That is what makes the gate unreachable from
            // story content (KI-048), and this test pins it so a change to the mask is a deliberate one.
            Assert.AreEqual(grid, total, "every cell of the 96x96 grid counts");
            Assert.AreEqual(9216, total, "96 x 96 = 9216 countable cells");
        }

        // ============================ §8.4's mandated target progression test (the finding)

        [Test]
        public void StoryRoutesPlusEveryPlacedDetourDoNotReachTheSixtyPercentGate()
        {
            var s = NewGame(_saves);

            // The story: Q01-Q04 played end to end at domain level.
            PlayQ01(s); PlayQ02(s); PlayQ03(s); PlayQ04(s);
            float afterStory = s.Discovery.MapCoveragePercent;
            int cellsAfterStory = s.Discovery.RevealedCountableCells;

            // Plus every reasonable detour content offers: each placed landmark, cache, site, garage and
            // camp socket, and each checkpoint route walked at 32 m steps.
            s.Depart();
            int stops = 0;
            foreach (var (x, z, _) in PlacedTargets()) { s.RevealAround(x, z, false); stops++; }
            float afterDetours = s.Discovery.MapCoveragePercent;

            Assert.Less(afterStory, 5f,
                $"measured: the story alone reveals {afterStory:F2}% ({cellsAfterStory} cells)");
            Assert.Less(afterDetours, 15f,
                $"measured: story + every placed detour ({stops} reveal stops) reveals " +
                $"{afterDetours:F2}% ({s.Discovery.RevealedCountableCells} of " +
                $"{s.Discovery.CountableMask.CountSet()} cells)");
            Assert.Less(afterStory, DiscoveryService.FinaleMapCoveragePercent, "the story is under 60%");
            Assert.Less(afterDetours, DiscoveryService.FinaleMapCoveragePercent,
                "and so is the story plus every detour content offers");
            Assert.IsFalse(s.Discovery.MeetsFinaleMapRequirement(),
                "§8.4's finale gate cannot be met by the content that ships today, so per §8.4 either " +
                "route exposure or the threshold must change (KI-048, D-046)");
        }

    }
}
