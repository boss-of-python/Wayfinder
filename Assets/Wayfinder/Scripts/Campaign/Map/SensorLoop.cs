using System;
using System.Collections.Generic;

namespace Wayfinder.Campaign
{
    /// <summary>
    /// §8.3's passive sensor loop — pure and engine-free so it can be executed headlessly; the map and
    /// HUD only render its readings.
    ///
    /// A signal pulses when it is within the resolved signal radius: 80 m baseline, Radar I 140 m,
    /// Radar II 220 m (§11.2 tiers 9 and 19, composed as <c>Stat.SignalRadiusM</c>, which nothing
    /// consumed before this model existed). Directional precision improves at 45 m: inside it a reading
    /// carries a trustworthy bearing and the signal's true position; outside it the reading reports the
    /// deterministic search-area centre instead, because §8.3 says "the map shows a search area, not
    /// exact vertical navigation".
    ///
    /// Audio, haptic and visual channels all read the same readings, which is what allows a player to
    /// disable pulse audio/haptics and keep the visual equivalent (§8.3, DRIVE-04).
    /// </summary>
    public static class SensorLoop
    {
        /// <summary>§8.3 baseline pulse range — also the resolver's default for SignalRadiusM.</summary>
        public const float BaseSignalRadiusM = 80f;

        /// <summary>§8.3: "directional precision improves at 45 m".</summary>
        public const float DirectionalPrecisionRadiusM = 45f;

        /// <summary>§11.2 tier 9: "Signal radius 80 → 140 m".</summary>
        public const float RadarISignalRadiusM = 140f;

        /// <summary>§11.2 tier 19: "Signal radius 140 → 220 m".</summary>
        public const float RadarIISignalRadiusM = 220f;

        /// <summary>One thing the sensor can hear: a placed site, relay, quest item or relic socket.</summary>
        public readonly struct Signal
        {
            public readonly string Id;
            public readonly float X, Z;

            public Signal(string id, float x, float z)
            {
                Id = id ?? string.Empty;
                X = x;
                Z = z;
            }
        }

        public readonly struct Reading
        {
            public readonly string Id;
            public readonly float DistanceM;

            /// <summary>Within the resolved signal radius, so the sensor pulses.</summary>
            public readonly bool Pulsing;

            /// <summary>Within 45 m, so the bearing is trustworthy and the true position may be shown.</summary>
            public readonly bool Directional;

            /// <summary>Compass bearing in degrees, 0 = +Z, clockwise. Zero unless <see cref="Directional"/>.</summary>
            public readonly float BearingDegrees;

            /// <summary>
            /// What the map may draw: the true position when directional, otherwise the deterministic
            /// search-area centre — never the exact position from a distance (§8.3).
            /// </summary>
            public readonly float ShowX, ShowZ;

            /// <summary>Search-area radius to draw, or 0 when the exact position is known.</summary>
            public readonly float ShowRadiusM;

            public Reading(string id, float distanceM, bool pulsing, bool directional, float bearingDegrees,
                float showX, float showZ, float showRadiusM)
            {
                Id = id ?? string.Empty;
                DistanceM = distanceM;
                Pulsing = pulsing;
                Directional = directional;
                BearingDegrees = bearingDegrees;
                ShowX = showX;
                ShowZ = showZ;
                ShowRadiusM = showRadiusM;
            }
        }

        /// <summary>Reads one signal from one observer position at one resolved radius.</summary>
        public static Reading Read(Signal signal, float observerX, float observerZ, float signalRadiusM)
        {
            float dx = signal.X - observerX;
            float dz = signal.Z - observerZ;
            float distance = (float)Math.Sqrt(dx * dx + dz * dz);

            // A missing or non-positive radius falls back to the §8.3 baseline rather than deafening
            // the sensor: content that forgets to compose SignalRadiusM must not silently disable it.
            float radius = float.IsNaN(signalRadiusM) || signalRadiusM <= 0f ? BaseSignalRadiusM : signalRadiusM;

            bool pulsing = distance <= radius;
            bool directional = distance <= DirectionalPrecisionRadiusM;

            float bearing = directional
                ? (float)((Math.Atan2(dx, dz) * 180d / Math.PI + 360d) % 360d)
                : 0f;

            float showX = signal.X, showZ = signal.Z, showRadius = 0f;
            if (!directional)
            {
                var centre = MapMarkerRules.SearchAreaCenter(signal.Id, signal.X, signal.Z);
                showX = centre.x;
                showZ = centre.z;
                showRadius = MapMarkerRules.SearchAreaRadiusMeters;
            }

            return new Reading(signal.Id, distance, pulsing, directional, bearing, showX, showZ, showRadius);
        }

        /// <summary>Every pulsing signal, nearest first. Signals outside the radius are not reported.</summary>
        public static List<Reading> Scan(IEnumerable<Signal> signals, float observerX, float observerZ,
            float signalRadiusM)
        {
            var hits = new List<Reading>();
            if (signals == null) return hits;
            foreach (var signal in signals)
            {
                var reading = Read(signal, observerX, observerZ, signalRadiusM);
                if (reading.Pulsing) hits.Add(reading);
            }
            hits.Sort((a, b) => a.DistanceM.CompareTo(b.DistanceM));
            return hits;
        }
    }
}
