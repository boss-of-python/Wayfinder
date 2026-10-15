using UnityEngine;
using Wayfinder.Campaign;
// UnityEngine also defines a Pose struct; keep every campaign Pose unambiguous.
using Pose = Wayfinder.Campaign.Pose;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Procedural map icon sprites (white alpha masks tinted per marker kind) so the prototype map
    /// needs no art assets. Shapes stay distinguishable without colour (§19 colour-blind support):
    /// squares = safe hubs, discs = discovered/objective, rings = unconfirmed/search areas,
    /// arrow = you, diamond = your markers.
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public static class MapGlyphs
    {
        static Sprite _disc, _ring, _wideRing, _arrow, _diamond, _square;

        public static Sprite Disc => _disc != null ? _disc : _disc = Make(64, (u, v) => Coverage(Radius(u, v), 1f));
        public static Sprite Ring => _ring != null ? _ring : _ring = Make(64, (u, v) =>
            Mathf.Min(Coverage(Radius(u, v), 1f), 1f - Coverage(Radius(u, v), 0.62f)));
        /// <summary>Thin ring for search areas (drawn 100–450 px wide).</summary>
        public static Sprite WideRing => _wideRing != null ? _wideRing : _wideRing = Make(256, (u, v) =>
            Mathf.Min(Coverage(Radius(u, v), 1f), 1f - Coverage(Radius(u, v), 0.955f)));
        public static Sprite Arrow => _arrow != null ? _arrow : _arrow = Make(64, ArrowMask);
        public static Sprite Diamond => _diamond != null ? _diamond : _diamond = Make(64, (u, v) =>
            Coverage(Mathf.Abs(u) + Mathf.Abs(v), 0.95f));
        public static Sprite Square => _square != null ? _square : _square = Make(64, (u, v) =>
            Coverage(Mathf.Max(Mathf.Abs(u), Mathf.Abs(v)), 0.85f));

        public static Sprite SpriteOf(MapMarkerKind kind)
        {
            switch (kind)
            {
                case MapMarkerKind.Player: return Arrow;
                case MapMarkerKind.Garage:
                case MapMarkerKind.GarageLocked:
                case MapMarkerKind.CampActive:
                case MapMarkerKind.Relay:
                    return Square;
                case MapMarkerKind.CampSite:
                case MapMarkerKind.LandmarkSuspected:
                case MapMarkerKind.RelayOffline:
                    return Ring;
                case MapMarkerKind.ObjectiveArea: return WideRing;
                case MapMarkerKind.Waypoint: return Diamond;
                default: return Disc;
            }
        }

        public static Color ColorOf(MapMarkerKind kind)
        {
            switch (kind)
            {
                case MapMarkerKind.Player: return new Color(.08f, .08f, .08f);
                case MapMarkerKind.Garage: return new Color(.12f, .25f, .58f);
                case MapMarkerKind.GarageLocked: return new Color(.45f, .48f, .55f);
                case MapMarkerKind.CampActive:
                case MapMarkerKind.CampSite:
                    return new Color(.86f, .47f, .12f);
                case MapMarkerKind.LandmarkDiscovered:
                case MapMarkerKind.LandmarkSuspected:
                    return new Color(.90f, .74f, .16f);
                case MapMarkerKind.CacheOpened: return new Color(.16f, .63f, .67f);
                case MapMarkerKind.Relay:
                case MapMarkerKind.RelayOffline:
                    return new Color(.47f, .47f, .51f);
                case MapMarkerKind.Objective:
                case MapMarkerKind.ObjectiveArea:
                    return new Color(.80f, .24f, .16f);
                case MapMarkerKind.Waypoint: return new Color(.70f, .24f, .67f);
                default: return Color.white;
            }
        }

        /// <summary>Icon edge length in canvas units (search areas are sized from their radius instead).</summary>
        public static float SizeOf(MapMarkerKind kind, bool compact)
        {
            float s;
            switch (kind)
            {
                case MapMarkerKind.Player: s = 26f; break;
                case MapMarkerKind.Garage:
                case MapMarkerKind.CampActive:
                    s = 18f;
                    break;
                case MapMarkerKind.CacheOpened: s = 9f; break;
                default: s = 15f; break;
            }
            return compact ? s * 0.7f : s;
        }

        public static string Hex(MapMarkerKind kind) => ColorUtility.ToHtmlStringRGB(ColorOf(kind));

        static float Radius(float u, float v) => Mathf.Sqrt(u * u + v * v);

        /// <summary>Anti-aliased "inside" test: 1 well inside <paramref name="edge"/>, 0 outside.</summary>
        static float Coverage(float distance, float edge) => Mathf.Clamp01((edge - distance) * 24f + 0.5f);

        static float ArrowMask(float u, float v)
        {
            // Upward chevron: tip at v = 0.95, notch at v = -0.35, wings at v = -0.9.
            float halfWidth = Mathf.Lerp(0f, 0.75f, Mathf.InverseLerp(0.95f, -0.9f, v));
            float notch = Mathf.Lerp(0f, 0.75f, Mathf.InverseLerp(-0.35f, -0.9f, v));
            float inside = Coverage(Mathf.Abs(u), halfWidth) * Coverage(v, 0.95f) * Coverage(-v, 0.9f);
            return v < -0.35f ? inside * (1f - Coverage(Mathf.Abs(u), notch)) : inside;
        }

        static Sprite Make(int size, System.Func<float, float, float> alpha)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false) { name = "Map Glyph", filterMode = FilterMode.Bilinear };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = (x + 0.5f) / size * 2f - 1f, v = (y + 0.5f) / size * 2f - 1f;
                pixels[y * size + x] = new Color32(255, 255, 255, (byte)Mathf.RoundToInt(Mathf.Clamp01(alpha(u, v)) * 255f));
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(.5f, .5f), 100f);
        }
    }
}
