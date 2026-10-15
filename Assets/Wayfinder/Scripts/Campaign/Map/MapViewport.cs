using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Campaign
{
    /// <summary>Normalized texture rectangle (Unity <c>RawImage.uvRect</c> convention: origin bottom-left).</summary>
    public readonly struct MapUvRect
    {
        public readonly float X, Y, Width, Height;

        public MapUvRect(float x, float y, float width, float height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }
    }

    /// <summary>
    /// Pan/zoom camera for the map screen and minimap (§13.4 "pinch zoom, drag pan, center on
    /// car"). Screen space is the map panel's local pixel space with the origin at the bottom-left
    /// and y up (Unity RectTransform-local convention); world space is metres, x east, z north.
    /// The view centre is clamped to <see cref="Bounds"/> so the sheet can never be lost off-screen.
    /// </summary>
    public sealed class MapViewport
    {
        public const float DefaultMinMetersPerPixel = 0.5f;

        public float CenterX { get; private set; }
        public float CenterZ { get; private set; }
        public float MetersPerPixel { get; private set; }
        public float WidthPixels { get; private set; }
        public float HeightPixels { get; private set; }
        public float MinMetersPerPixel { get; set; } = DefaultMinMetersPerPixel;
        public float MaxMetersPerPixel { get; set; } = 16f;
        /// <summary>World rectangle the centre may move within (minX, minZ, maxX, maxZ).</summary>
        public (float minX, float minZ, float maxX, float maxZ) Bounds { get; private set; }

        public MapViewport(float widthPixels, float heightPixels, float metersPerPixel,
            float minX, float minZ, float maxX, float maxZ)
        {
            Bounds = (minX, minZ, maxX, maxZ);
            Resize(widthPixels, heightPixels);
            MetersPerPixel = MathUtil.Clamp(metersPerPixel, MinMetersPerPixel, MaxMetersPerPixel);
            CenterOn((minX + maxX) * 0.5f, (minZ + maxZ) * 0.5f);
        }

        /// <summary>Viewport over a raster window, zoom limited between 0.5 m/px and "whole sheet visible".</summary>
        public static MapViewport ForRaster(MapRaster raster, float widthPixels, float heightPixels)
        {
            if (raster == null) throw new ArgumentNullException(nameof(raster));
            var v = new MapViewport(widthPixels, heightPixels, 2f, raster.MinWorldX, raster.MinWorldZ,
                raster.MinWorldX + raster.WidthMeters, raster.MinWorldZ + raster.HeightMeters);
            v.MaxMetersPerPixel = Math.Max(v.MinMetersPerPixel,
                Math.Max(raster.WidthMeters, raster.HeightMeters) / Math.Max(1f, Math.Min(v.WidthPixels, v.HeightPixels)));
            v.SetZoom(v.MaxMetersPerPixel);
            return v;
        }

        public void SetBounds(float minX, float minZ, float maxX, float maxZ)
        {
            Bounds = (minX, minZ, maxX, maxZ);
            CenterOn(CenterX, CenterZ);
        }

        public void Resize(float widthPixels, float heightPixels)
        {
            WidthPixels = Math.Max(1f, widthPixels);
            HeightPixels = Math.Max(1f, heightPixels);
        }

        public (float sx, float sy) WorldToScreen(float x, float z) =>
            ((x - CenterX) / MetersPerPixel + WidthPixels * 0.5f, (z - CenterZ) / MetersPerPixel + HeightPixels * 0.5f);

        public (float x, float z) ScreenToWorld(float sx, float sy) =>
            (CenterX + (sx - WidthPixels * 0.5f) * MetersPerPixel, CenterZ + (sy - HeightPixels * 0.5f) * MetersPerPixel);

        public bool IsOnScreen(float sx, float sy, float marginPixels = 0f) =>
            sx >= -marginPixels && sy >= -marginPixels && sx <= WidthPixels + marginPixels && sy <= HeightPixels + marginPixels;

        public void CenterOn(float x, float z)
        {
            var b = Bounds;
            CenterX = MathUtil.Clamp(x, Math.Min(b.minX, b.maxX), Math.Max(b.minX, b.maxX));
            CenterZ = MathUtil.Clamp(z, Math.Min(b.minZ, b.maxZ), Math.Max(b.minZ, b.maxZ));
        }

        /// <summary>Drag pan: content follows the finger (dragging right moves the map right).</summary>
        public void Pan(float deltaXPixels, float deltaYPixels) =>
            CenterOn(CenterX - deltaXPixels * MetersPerPixel, CenterZ - deltaYPixels * MetersPerPixel);

        public void SetZoom(float metersPerPixel) =>
            MetersPerPixel = MathUtil.Clamp(metersPerPixel, MinMetersPerPixel, Math.Max(MinMetersPerPixel, MaxMetersPerPixel));

        /// <summary>
        /// Zoom by <paramref name="factor"/> (&gt; 1 zooms in) keeping the world point under the
        /// focus pixel fixed — pinch midpoint or mouse cursor (§13.4 pinch zoom).
        /// </summary>
        public void ZoomAt(float factor, float focusX, float focusY)
        {
            if (factor <= 0f || float.IsNaN(factor)) return;
            var (wx, wz) = ScreenToWorld(focusX, focusY);
            SetZoom(MetersPerPixel / factor);
            var (ax, az) = ScreenToWorld(focusX, focusY);
            CenterOn(CenterX + (wx - ax), CenterZ + (wz - az));
        }

        /// <summary>Texture sub-rectangle showing exactly this view of <paramref name="raster"/>.</summary>
        public MapUvRect UvFor(MapRaster raster)
        {
            float left = CenterX - WidthPixels * 0.5f * MetersPerPixel;
            float bottom = CenterZ - HeightPixels * 0.5f * MetersPerPixel;
            return new MapUvRect((left - raster.MinWorldX) / raster.WidthMeters, (bottom - raster.MinWorldZ) / raster.HeightMeters,
                WidthPixels * MetersPerPixel / raster.WidthMeters, HeightPixels * MetersPerPixel / raster.HeightMeters);
        }

        /// <summary>
        /// Tap selection (§13.4 "tap select"): the nearest point marker within
        /// <paramref name="radiusPixels"/>; else the player arrow if within range; else the
        /// search area whose centre is nearest among those containing the tap; -1 when none.
        /// </summary>
        public int HitTest(IReadOnlyList<MapMarker> markers, float sx, float sy, float radiusPixels)
        {
            int best = -1, player = -1, area = -1;
            float bestD2 = radiusPixels * radiusPixels, playerD2 = bestD2, areaD = float.MaxValue;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                var (mx, my) = WorldToScreen(m.X, m.Z);
                float dx = mx - sx, dy = my - sy, d2 = dx * dx + dy * dy;
                if (m.Kind == MapMarkerKind.ObjectiveArea)
                {
                    float r = m.RadiusMeters / MetersPerPixel, d = (float)Math.Sqrt(d2);
                    if (d <= r && d < areaD)
                    {
                        areaD = d;
                        area = i;
                    }
                    continue;
                }
                if (m.Kind == MapMarkerKind.Player)
                {
                    if (d2 <= playerD2)
                    {
                        playerD2 = d2;
                        player = i;
                    }
                    continue;
                }
                if (d2 <= bestD2)
                {
                    bestD2 = d2;
                    best = i;
                }
            }
            return best >= 0 ? best : (player >= 0 ? player : area);
        }
    }

    public enum MapGestureKind
    {
        None,
        Tap,
        LongPress,
        Pan,
        Pinch
    }

    public readonly struct MapGesture
    {
        public readonly MapGestureKind Kind;
        public readonly float X, Y;
        public readonly float DeltaX, DeltaY;
        public readonly float Scale;

        public MapGesture(MapGestureKind kind, float x, float y, float dx = 0f, float dy = 0f, float scale = 1f)
        {
            Kind = kind;
            X = x;
            Y = y;
            DeltaX = dx;
            DeltaY = dy;
            Scale = scale;
        }

        public static readonly MapGesture None = new MapGesture(MapGestureKind.None, 0f, 0f);
    }

    /// <summary>
    /// Touch/mouse gesture recognizer for the map (§13.4): tap select, long-press (custom marker,
    /// confirmed by the UI), one-finger drag pan, two-finger pinch zoom + pan. Engine-free: the
    /// Unity layer forwards pointer events with unscaled time (the map pauses the simulation, so
    /// scaled time would freeze). Movement beyond <see cref="SlopPixels"/> turns a press into a
    /// pan and cancels tap/long-press; a second finger always cancels them.
    /// </summary>
    public sealed class MapGestureRecognizer
    {
        public const float SlopPixels = 14f;
        public const float LongPressSeconds = 0.6f;
        public const float TapMaxSeconds = 0.4f;

        struct Pointer
        {
            public int Id;
            public float X, Y;
            public bool Active;
        }

        readonly Pointer[] _pointers = new Pointer[2];
        float _downX, _downY, _downTime;
        bool _dragging, _longPressFired, _multiTouch;
        float _lastPinchDistance;

        public int ActivePointers => (_pointers[0].Active ? 1 : 0) + (_pointers[1].Active ? 1 : 0);

        public void PointerDown(int id, float x, float y, float time)
        {
            int slot = !_pointers[0].Active ? 0 : (!_pointers[1].Active ? 1 : -1);
            if (slot < 0) return; // a third finger is ignored
            _pointers[slot] = new Pointer { Id = id, X = x, Y = y, Active = true };
            if (ActivePointers == 1)
            {
                _downX = x;
                _downY = y;
                _downTime = time;
                _dragging = false;
                _longPressFired = false;
                _multiTouch = false;
            }
            else
            {
                _multiTouch = true;
                _lastPinchDistance = PinchDistance();
            }
        }

        public MapGesture PointerMove(int id, float x, float y, float time)
        {
            int slot = Find(id);
            if (slot < 0) return MapGesture.None;
            if (ActivePointers == 2)
            {
                var (mx0, my0) = Midpoint();
                _pointers[slot].X = x;
                _pointers[slot].Y = y;
                var (mx1, my1) = Midpoint();
                float distance = PinchDistance();
                float scale = _lastPinchDistance > 1f && distance > 1f ? distance / _lastPinchDistance : 1f;
                _lastPinchDistance = distance;
                return new MapGesture(MapGestureKind.Pinch, mx1, my1, mx1 - mx0, my1 - my0, scale);
            }
            float dx = x - _pointers[slot].X, dy = y - _pointers[slot].Y;
            _pointers[slot].X = x;
            _pointers[slot].Y = y;
            if (_multiTouch) return MapGesture.None; // the remaining finger after a pinch never taps or long-presses
            if (!_dragging)
            {
                float tx = x - _downX, ty = y - _downY;
                if (tx * tx + ty * ty < SlopPixels * SlopPixels) return MapGesture.None;
                _dragging = true;
                dx = tx; // first pan step includes the slop so the map does not jump
                dy = ty;
            }
            return new MapGesture(MapGestureKind.Pan, x, y, dx, dy);
        }

        public MapGesture PointerUp(int id, float x, float y, float time)
        {
            int slot = Find(id);
            if (slot < 0) return MapGesture.None;
            _pointers[slot].Active = false;
            bool wasSingle = !_multiTouch && !_dragging && !_longPressFired;
            if (ActivePointers == 0) _multiTouch = false;
            if (!wasSingle || ActivePointers > 0) return MapGesture.None;
            float tx = x - _downX, ty = y - _downY;
            if (tx * tx + ty * ty >= SlopPixels * SlopPixels || time - _downTime > TapMaxSeconds) return MapGesture.None;
            return new MapGesture(MapGestureKind.Tap, x, y);
        }

        /// <summary>Call every frame (unscaled time): fires one LongPress per stationary press.</summary>
        public MapGesture Tick(float time)
        {
            if (ActivePointers != 1 || _multiTouch || _dragging || _longPressFired) return MapGesture.None;
            if (time - _downTime < LongPressSeconds) return MapGesture.None;
            _longPressFired = true;
            return new MapGesture(MapGestureKind.LongPress, _downX, _downY);
        }

        public void Cancel()
        {
            _pointers[0].Active = _pointers[1].Active = false;
            _dragging = _longPressFired = _multiTouch = false;
        }

        int Find(int id)
        {
            for (int i = 0; i < 2; i++)
                if (_pointers[i].Active && _pointers[i].Id == id) return i;
            return -1;
        }

        (float x, float y) Midpoint() =>
            ((_pointers[0].X + _pointers[1].X) * 0.5f, (_pointers[0].Y + _pointers[1].Y) * 0.5f);

        float PinchDistance()
        {
            float dx = _pointers[0].X - _pointers[1].X, dy = _pointers[0].Y - _pointers[1].Y;
            return (float)Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
