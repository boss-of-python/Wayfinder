using System.Collections.Generic;
using NUnit.Framework;
using Wayfinder.Core;

namespace Wayfinder.Tests
{
    /// <summary>Stable hash/PRNG determinism (§7.3) and map bitset semantics (§8.1).</summary>
    public class CoreDomainTests
    {
        [Test]
        public void MobileLifecycle_FocusCannotResumePausedApplication()
        {
            var gate = new MobileLifecycleGate();
            Assert.IsTrue(gate.SetApplicationPaused(true));
            Assert.IsFalse(gate.SetFocus(false));
            Assert.IsFalse(gate.SetFocus(true));
            Assert.IsTrue(gate.IsSuspended);
            Assert.IsTrue(gate.SetApplicationPaused(false));
            Assert.IsFalse(gate.IsSuspended);
        }

        [Test]
        public void MobileLifecycle_UnpauseCannotResumeUnfocusedApplication()
        {
            var gate = new MobileLifecycleGate();
            Assert.IsTrue(gate.SetFocus(false));
            Assert.IsFalse(gate.SetApplicationPaused(true));
            Assert.IsFalse(gate.SetApplicationPaused(false));
            Assert.IsTrue(gate.IsSuspended);
            Assert.IsTrue(gate.SetFocus(true));
            Assert.IsFalse(gate.IsSuspended);
        }

        [Test]
        public void MobileLifecycle_DuplicateCallbacksDoNotRepeatTransitions()
        {
            var gate = new MobileLifecycleGate();
            Assert.IsFalse(gate.SetFocus(true));
            Assert.IsFalse(gate.SetApplicationPaused(false));
            Assert.IsTrue(gate.SetFocus(false));
            Assert.IsFalse(gate.SetFocus(false));
            Assert.IsTrue(gate.SetFocus(true));
            Assert.IsFalse(gate.SetFocus(true));
        }

        [Test]
        public void MobileLifecycle_BothCallbackOrdersRequireBothSignals()
        {
            foreach (bool focusFirst in new[] { true, false })
            {
                var gate = new MobileLifecycleGate();
                if (focusFirst) { gate.SetFocus(false); gate.SetApplicationPaused(true); }
                else { gate.SetApplicationPaused(true); gate.SetFocus(false); }
                Assert.IsTrue(gate.IsSuspended);
                if (focusFirst) { gate.SetApplicationPaused(false); Assert.IsTrue(gate.IsSuspended); gate.SetFocus(true); }
                else { gate.SetFocus(true); Assert.IsTrue(gate.IsSuspended); gate.SetApplicationPaused(false); }
                Assert.IsFalse(gate.IsSuspended);
            }
        }

        [Test]
        public void StableHash_IsDeterministicAndOrdinal()
        {
            Assert.AreEqual(StableHash.Hash32("cache.forest.017"), StableHash.Hash32("cache.forest.017"));
            Assert.AreNotEqual(StableHash.Hash32("cache.forest.017"), StableHash.Hash32("cache.forest.018"));
            Assert.AreEqual(StableHash.Hash64("x"), StableHash.Hash64("x"));
        }

        [Test]
        public void ChunkSeed_DerivesFromAllInputs()
        {
            var a = StableHash.ChunkSeed(481516, 1, 5, 9, 2);
            Assert.AreEqual(a, StableHash.ChunkSeed(481516, 1, 5, 9, 2));
            Assert.AreNotEqual(a, StableHash.ChunkSeed(481516, 1, 5, 9, 3));
            Assert.AreNotEqual(a, StableHash.ChunkSeed(481516, 1, 6, 9, 2));
        }

        [Test]
        public void StableRandom_SameSeedSameSequence()
        {
            var r1 = new StableRandom(12345);
            var r2 = new StableRandom(12345);
            for (int i = 0; i < 64; i++)
                Assert.AreEqual(r1.NextUInt(), r2.NextUInt());
        }

        [Test]
        public void BitGrid_Base64RoundTrips()
        {
            var grid = new BitGrid(96, 96);
            grid.Set(3, 7, true);
            grid.Set(95, 95, true);
            string b64 = grid.ToBase64();
            var restored = BitGrid.FromBase64(96, 96, b64);
            Assert.IsTrue(restored.Get(3, 7));
            Assert.IsTrue(restored.Get(95, 95));
            Assert.IsFalse(restored.Get(0, 0));
            Assert.AreEqual(2, restored.CountSet());
        }

        [Test]
        public void BitGrid_RevealCircle_CountsOnlyNewCells()
        {
            var grid = new BitGrid(96, 96);
            int first = grid.RevealCircle(320f, 320f, 96f, 64f);
            int second = grid.RevealCircle(320f, 320f, 96f, 64f);
            Assert.Greater(first, 0);
            Assert.AreEqual(0, second); // idempotent reveal
        }

        [Test]
        public void ClaimedRewardLedger_PaysExactlyOnce()
        {
            var ledger = new ClaimedRewardLedger();
            Assert.IsTrue(ledger.TryClaim("reward.landmark.landmark.forest.01"));
            Assert.IsFalse(ledger.TryClaim("reward.landmark.landmark.forest.01")); // QA case 1
            Assert.IsTrue(ledger.TryClaim("reward.story.Q01"));
            Assert.AreEqual(2, ledger.Count);
        }

        [Test]
        public void PauseTokens_AreStrictlyOwned()
        {
            var registry = new PauseTokenRegistry();
            var t1 = registry.Acquire();
            Assert.IsTrue(registry.IsPaused);
            var t2 = registry.Acquire();
            t1.Dispose();
            Assert.IsTrue(registry.IsPaused, "closing one overlay must not unpause another (§2.5)");
            t2.Dispose();
            Assert.IsFalse(registry.IsPaused);
        }
    }
}
