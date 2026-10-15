using System;
using Wayfinder.Core;

namespace Wayfinder.World
{
    /// <summary>
    /// Cheap terrain line-of-sight for map reveal (§8.1): "driving reveals eligible cells within
    /// 96 m, using cheap terrain line-of-sight samples to avoid revealing behind major ridges";
    /// vista surveys (384 m) respect the same large-scale occlusion.
    ///
    /// The test walks the straight segment from the observer's eye to a target cell centre in
    /// ~16 m steps and reports the cell as hidden only when the terrain rises more than
    /// <see cref="MajorRidgeToleranceMeters"/> above the sight line — small bumps, ditches and
    /// props never punch holes in the map. Heights come from a sampler (baked heightfield or the
    /// slice greybox profile, never scene colliders), and an unknown height (NaN) never hides a
    /// cell. Cells whose centre lies within <see cref="AlwaysRevealRadiusMeters"/> are always
    /// revealed so the cell under the vehicle can never stay fogged. Pure C#, no allocation per
    /// sample; a 96 m drive reveal costs ≈ 9 cells × ≤ 6 samples at 2 Hz (§16.2).
    /// </summary>
    public static class TerrainLineOfSight
    {
        public const float EyeHeightMeters = 2f;             // Scout driver eye above the ground
        public const float VistaEyeHeightMeters = 6f;        // vista viewpoint socket / lookout platform (§8.2)
        public const float TargetHeightMeters = 2f;          // scrub/ground features at a cell centre
        public const float MajorRidgeToleranceMeters = 4f;   // terrain must clear the sight line by this much to block it
        public const float SampleSpacingMeters = 16f;
        public const int MaxSamples = 32;                    // 384 m vista → 23 samples
        public const float AlwaysRevealRadiusMeters = 48f;   // > half a cell diagonal (45.25 m): the current cell always reveals

        /// <summary>True when nothing taller than a major ridge blocks eye → target.</summary>
        public static bool IsVisible(Func<float, float, float> heightAt, float eyeX, float eyeZ, float targetX, float targetZ,
            float eyeHeightMeters = EyeHeightMeters)
        {
            if (heightAt == null) return true;
            float dx = targetX - eyeX, dz = targetZ - eyeZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);
            int samples = Math.Min(MaxSamples, (int)(distance / SampleSpacingMeters));
            if (samples <= 0) return true;

            float eyeGround = heightAt(eyeX, eyeZ);
            float targetGround = heightAt(targetX, targetZ);
            if (float.IsNaN(eyeGround) || float.IsNaN(targetGround)) return true;
            float eye = eyeGround + eyeHeightMeters;
            float target = targetGround + TargetHeightMeters;

            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)(samples + 1);
                float ground = heightAt(eyeX + dx * t, eyeZ + dz * t);
                if (float.IsNaN(ground)) continue;
                float sightLine = eye + (target - eye) * t;
                if (ground - sightLine > MajorRidgeToleranceMeters) return false;
            }
            return true;
        }

        /// <summary>
        /// Map-cell predicate for <see cref="BitGrid.RevealCircle"/>: always reveals cells near the
        /// observer, otherwise tests the cell centre. Returns null (= no occlusion) without a sampler.
        /// </summary>
        public static Func<int, int, bool> CellFilter(Func<float, float, float> heightAt, float eyeX, float eyeZ,
            float cellSizeMeters, float eyeHeightMeters = EyeHeightMeters)
        {
            if (heightAt == null) return null;
            float always2 = AlwaysRevealRadiusMeters * AlwaysRevealRadiusMeters;
            return (x, z) =>
            {
                float cx = (x + 0.5f) * cellSizeMeters, cz = (z + 0.5f) * cellSizeMeters;
                float dx = cx - eyeX, dz = cz - eyeZ;
                if (dx * dx + dz * dz <= always2) return true;
                return IsVisible(heightAt, eyeX, eyeZ, cx, cz, eyeHeightMeters);
            };
        }
    }
}
