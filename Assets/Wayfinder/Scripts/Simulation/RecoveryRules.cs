using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Recovery rules (§5.6). Guarantees: a player with zero currency and zero supplies can
    /// always reach safety. Full rescue never destroys equipment, banked currency, relics,
    /// story items, accepted objectives or discovered landmarks (QA case 6).
    /// </summary>
    public sealed class RecoveryRules
    {
        public const float GentleResetCooldownSeconds = 30f;
        public const float LastSafePoseMaxDistanceM = 25f;
        public const float RescueSalvageLossFraction = 0.25f; // loose salvage_bundle units only
        public const float SafePoseMaxSpeedMs = 12f;
        public const float SafePoseMaxRollDegrees = 15f;
        public const float SafePoseSampleIntervalSeconds = 2f;

        float _gentleResetCooldown;

        public void Tick(float simulationDeltaSeconds) =>
            _gentleResetCooldown = Math.Max(0f, _gentleResetCooldown - simulationDeltaSeconds);

        public bool GentleResetAvailable(float distanceToLastSafePoseM)
        {
            return _gentleResetCooldown <= 0f && distanceToLastSafePoseM <= LastSafePoseMaxDistanceM;
        }

        public CommandResult TryGentleReset(float distanceToLastSafePoseM)
        {
            if (_gentleResetCooldown > 0f)
                return CommandResult.Fail(FailureReason.WrongState, "reset cooldown active");
            if (distanceToLastSafePoseM > LastSafePoseMaxDistanceM)
                return CommandResult.Fail(FailureReason.TooFarAway, "no valid safe pose within 25 m; offer full rescue");
            _gentleResetCooldown = GentleResetCooldownSeconds;
            return CommandResult.Ok();
        }

        /// <summary>
        /// A pose qualifies as a safe reset pose when: grounded, speed &lt; 12 m/s, roll &lt; 15°,
        /// outside water and deep mud, sufficient clearance, not inside an unresolved mission trigger.
        /// </summary>
        public static bool IsValidSafePose(bool grounded, float speedMs, float rollDegrees,
            bool inWater, bool inDeepMud, float clearanceM, bool insideUnresolvedMissionTrigger)
        {
            return grounded
                   && Math.Abs(speedMs) <= SafePoseMaxSpeedMs
                   && Math.Abs(rollDegrees) < SafePoseMaxRollDegrees
                   && !inWater
                   && !inDeepMud
                   && clearanceM >= 0.10f
                   && !insideUnresolvedMissionTrigger;
        }

        /// <summary>
        /// Full rescue loss: 25% of loose salvage_bundle units, rounded down. Applies ONLY to
        /// salvage_bundle — never to materials, quest items, relics or unique items.
        /// </summary>
        public static int RescueSalvageLoss(int looseSalvageBundleUnits)
        {
            if (looseSalvageBundleUnits <= 0) return 0;
            return (int)(looseSalvageBundleUnits * RescueSalvageLossFraction); // floor
        }

        /// <summary>Full rescue preserves everything except the salvage loss computed above.</summary>
        public static RescueOutcome FullRescue(int looseSalvageBundleUnits, ContentId destinationGarageId)
        {
            return new RescueOutcome(
                destinationGarageId,
                RescueSalvageLoss(looseSalvageBundleUnits),
                repaired: true,
                fuelRefilled: true);
        }

        public readonly struct RescueOutcome
        {
            public readonly ContentId DestinationGarageId;
            public readonly int LooseSalvageLost;
            public readonly bool VehicleRepaired;
            public readonly bool FuelRefilled;

            public RescueOutcome(ContentId destinationGarageId, int looseSalvageLost, bool repaired, bool fuelRefilled)
            {
                DestinationGarageId = destinationGarageId;
                LooseSalvageLost = looseSalvageLost;
                VehicleRepaired = repaired;
                FuelRefilled = fuelRefilled;
            }
        }
    }

    /// <summary>
    /// Winch contract (Appendix C): anchor scoring, spring-like damped pull capped by module force.
    /// Never teleports the chassis; never uses a rigid constraint.
    /// </summary>
    public sealed class WinchMath
    {
        public const float MaxEngageSpeedMs = 1f;
        public const float ReelSpeedMs = 0.8f;
        public const float ExcessTensionPauseSeconds = 2f;
        public const float DefaultForceCapN = 8000f;
        public const float UpgradedForceCapN = 12000f;
        public const float DefaultReachM = 12f;
        public const float UpgradedReachM = 20f;

        /// <summary>
        /// Scores anchors by forward usefulness, distance and visibility. Higher is better.
        /// forwardUsefulness: 0..1 how much the anchor lies in the desired travel direction.
        /// visible: unobstructed cable path (validated by caller capsule casts).
        /// </summary>
        public static float ScoreAnchor(float distanceM, float maxReachM, float forwardUsefulness, bool visible)
        {
            if (!visible) return float.NegativeInfinity;
            if (distanceM > maxReachM) return float.NegativeInfinity;
            float distanceScore = 1f - (distanceM / maxReachM) * 0.5f;
            return forwardUsefulness * 2f + distanceScore;
        }

        public static int SelectBestAnchor(Span<float> scores)
        {
            int best = -1;
            float bestScore = float.NegativeInfinity;
            for (int i = 0; i < scores.Length; i++)
            {
                if (scores[i] > bestScore)
                {
                    bestScore = scores[i];
                    best = i;
                }
            }
            return best;
        }

        /// <summary>
        /// Damped spring-like pull along cable direction (cable length = current separation at
        /// attach). Pull magnitude capped at module force and clamped to non-negative tension so
        /// a slack cable cannot push the chassis backwards (DEF-027). Overload time accumulates
        /// continuously while stretch exceeds <paramref name="forceCapN"/> and decays only below
        /// the cap (DEF-026: previously decayed unconditionally first, freezing at deltaSeconds).
        /// </summary>
        public float ComputePullForce(
            float currentCableLengthM,
            float targetCableLengthM,
            float cableTensionVelocityMs,
            float forceCapN,
            float deltaSeconds,
            out bool reelingPaused)
        {
            const float springNPerM = 4000f;
            const float dampingNPerMs = 800f;

            float dt = Math.Max(0f, deltaSeconds);
            float cap = Math.Max(0f, forceCapN);
            float extension = currentCableLengthM - targetCableLengthM;
            float rawForce = extension > 0f
                ? Math.Max(0f, extension * springNPerM - cableTensionVelocityMs * dampingNPerMs)
                : 0f;

            float force;
            if (cap > 0f && rawForce > cap)
            {
                force = cap;
                _excessTensionTimer = Math.Min(ExcessTensionPauseSeconds, _excessTensionTimer + dt);
            }
            else
            {
                force = Math.Min(rawForce, cap);
                _excessTensionTimer = Math.Max(0f, _excessTensionTimer - dt);
            }
            reelingPaused = _excessTensionTimer >= ExcessTensionPauseSeconds;
            return force;
        }

        float _excessTensionTimer;
        public float ExcessTensionSeconds => _excessTensionTimer;

        /// <summary>Reel input decreases target length at up to 0.8 m/s.</summary>
        public static float Reel(float targetCableLengthM, float reelInput, float deltaSeconds)
        {
            reelInput = MathUtil.Clamp01(reelInput);
            return Math.Max(0.5f, targetCableLengthM - reelInput * ReelSpeedMs * deltaSeconds);
        }

        public void Reset() => _excessTensionTimer = 0f;
    }
}
