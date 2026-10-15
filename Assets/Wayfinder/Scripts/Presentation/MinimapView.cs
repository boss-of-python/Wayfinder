using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Wayfinder.Campaign;
// UnityEngine also defines a Pose struct; keep every campaign Pose unambiguous.
using Pose = Wayfinder.Campaign.Pose;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// HUD minimap (§13.2 "minimap/compass"): a north-up crop of the shared exploration-map texture
    /// via <see cref="RawImage.uvRect"/> (no second texture, no extra render), centred on the
    /// vehicle, with the player arrow, the tracked objective and the route target. Off-screen
    /// targets stick to the rim. Tap to open the full map (§13.4).
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class MinimapView : MonoBehaviour, IPointerClickHandler
    {
        public const float MetersPerUnit = 1.6f; // ≈ 360 m across a 230-unit minimap

        MapPresenter _presenter;
        RawImage _image;
        RectTransform _mapRect, _player, _tracked, _route;
        MapViewport _viewport;

        public event Action Clicked;

        public static MinimapView Create(Transform parent, MapPresenter presenter, Vector2 anchorMin, Vector2 anchorMax)
        {
            var rect = PrototypeUi.Rect("Minimap", parent, anchorMin, anchorMax);
            var frame = rect.gameObject.AddComponent<Image>();
            frame.color = new Color(.03f, .06f, .07f, .85f);
            var view = rect.gameObject.AddComponent<MinimapView>();
            view.Init(presenter);
            return view;
        }

        void Init(MapPresenter presenter)
        {
            _presenter = presenter;
            _mapRect = PrototypeUi.Rect("Map", transform, new Vector2(.03f, .03f), new Vector2(.97f, .97f));
            _mapRect.gameObject.AddComponent<RectMask2D>();
            _image = _mapRect.gameObject.AddComponent<RawImage>();
            _image.texture = presenter.Binding.Texture;
            _image.raycastTarget = false;
            _tracked = Icon("Tracked objective", MapGlyphs.Disc, MapGlyphs.ColorOf(MapMarkerKind.Objective), 11f);
            _route = Icon("Route target", MapGlyphs.Diamond, MapGlyphs.ColorOf(MapMarkerKind.Waypoint), 11f);
            _player = Icon("You", MapGlyphs.Arrow, MapGlyphs.ColorOf(MapMarkerKind.Player), 18f);
            var caption = PrototypeUi.Label(transform, "MAP (M)", new Vector2(0f, .86f), new Vector2(1f, 1f), 12);
            caption.color = new Color(.15f, .15f, .12f);
        }

        RectTransform Icon(string name, Sprite sprite, Color color, float size)
        {
            var rect = PrototypeUi.Rect(name, _mapRect, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        void LateUpdate()
        {
            if (_presenter == null) return;
            var size = _mapRect.rect.size;
            if (size.x < 2f || size.y < 2f) return;
            var raster = _presenter.Map.Raster;
            if (_viewport == null)
            {
                // The centre clamps to the sheet: near its edge the arrow moves off-centre instead.
                _viewport = new MapViewport(size.x, size.y, MetersPerUnit, raster.MinWorldX, raster.MinWorldZ,
                    raster.MinWorldX + raster.WidthMeters, raster.MinWorldZ + raster.HeightMeters);
            }
            _viewport.Resize(size.x, size.y);

            MapMarker player = default, tracked = default, route = default;
            bool hasTracked = false, hasRoute = false;
            foreach (var m in _presenter.Map.Markers)
            {
                if (m.Kind == MapMarkerKind.Player) player = m;
                else if (m.Kind == MapMarkerKind.Waypoint && m.Tracked)
                {
                    route = m;
                    hasRoute = true;
                }
                else if (m.Tracked && !hasTracked)
                {
                    tracked = m;
                    hasTracked = true;
                }
            }
            _viewport.CenterOn(player.X, player.Z);
            var uv = _viewport.UvFor(raster);
            _image.uvRect = new Rect(uv.X, uv.Y, uv.Width, uv.Height);

            Place(_player, player.X, player.Z, false);
            _player.localEulerAngles = new Vector3(0f, 0f, -player.YawDegrees);
            _tracked.gameObject.SetActive(hasTracked);
            if (hasTracked) Place(_tracked, tracked.X, tracked.Z, true);
            _route.gameObject.SetActive(hasRoute);
            if (hasRoute) Place(_route, route.X, route.Z, true);
        }

        void Place(RectTransform icon, float x, float z, bool clampToRim)
        {
            var (sx, sy) = _viewport.WorldToScreen(x, z);
            if (clampToRim)
            {
                const float margin = 8f;
                sx = Mathf.Clamp(sx, margin, _viewport.WidthPixels - margin);
                sy = Mathf.Clamp(sy, margin, _viewport.HeightPixels - margin);
            }
            icon.anchoredPosition = new Vector2(sx, sy);
        }

        public void OnPointerClick(PointerEventData eventData) => Clicked?.Invoke();
    }
}
