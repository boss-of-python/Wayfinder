using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using Wayfinder.Campaign;
// UnityEngine also defines a Pose struct; keep every campaign Pose unambiguous.
using Pose = Wayfinder.Campaign.Pose;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Full-screen exploration map (§13.4 wireframe): header "Region · % mapped", the revealed
    /// sheet with pinch zoom / drag pan / mouse-wheel zoom, tap-to-select marker details, long-press
    /// (or [Mark centre]) → confirm → custom marker, route target with a dashed straight-line
    /// estimate, [Center on car], [Clear route], legend and layer filters, plus the return
    /// estimate to the nearest safe hub. All rules and text come from the engine-free
    /// <see cref="ExplorationMap"/> / <see cref="MapScreenModel"/>; this view only lays out icons and
    /// forwards pointer events to <see cref="MapGestureRecognizer"/>. The caller pauses the
    /// simulation while it is open (AppState MapPaused, §9).
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class MapOverlayView : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler
    {
        const float TapRadius = 28f;
        const float DotSpacing = 14f;
        const int MaxRouteDots = 160;

        MapPresenter _presenter;
        CampaignSession _session;
        Action _onClosed;
        Action<string> _toast;
        RectTransform _root, _panel, _routeLayer, _iconLayer, _confirm, _pendingPin, _selection;
        RawImage _image;
        Text _header, _coverage, _return, _route, _selectionTitle, _selectionBody, _markerCount, _confirmText;
        Button _routeButton, _removeButton;
        readonly Dictionary<MapLayers, Image> _filterImages = new Dictionary<MapLayers, Image>();
        readonly MapGestureRecognizer _gestures = new MapGestureRecognizer();
        readonly MapScreenModel _model = new MapScreenModel();
        readonly List<RectTransform> _icons = new List<RectTransform>();
        readonly List<Text> _labels = new List<Text>();
        readonly List<RectTransform> _dots = new List<RectTransform>();
        MapViewport _viewport;
        string _selectedId = string.Empty;
        float _pendingX, _pendingZ, _nextText;
        int _openedFrame;
        bool _closed;

        public static MapOverlayView Create(Transform parent, MapPresenter presenter, CampaignSession session,
            Action onClosed, Action<string> toast)
        {
            var root = PrototypeUi.Rect("Map Screen", parent, Vector2.zero, Vector2.one);
            root.gameObject.AddComponent<Image>().color = new Color(.05f, .07f, .07f, .97f);
            var panel = PrototypeUi.Rect("Map", root, new Vector2(.02f, .04f), new Vector2(.70f, .92f));
            panel.pivot = Vector2.zero; // local pointer coordinates = bottom-left origin, like MapViewport
            panel.gameObject.AddComponent<RectMask2D>();
            var image = panel.gameObject.AddComponent<RawImage>();
            image.texture = presenter.Binding.Texture;
            image.raycastTarget = true;
            var view = panel.gameObject.AddComponent<MapOverlayView>();
            view.Init(root, panel, image, presenter, session, onClosed, toast);
            return view;
        }

        void Init(RectTransform root, RectTransform panel, RawImage image, MapPresenter presenter, CampaignSession session,
            Action onClosed, Action<string> toast)
        {
            _root = root;
            _panel = panel;
            _image = image;
            _presenter = presenter;
            _session = session;
            _onClosed = onClosed;
            _toast = toast;
            _openedFrame = Time.frameCount;
            _routeLayer = PrototypeUi.Rect("Route", panel, Vector2.zero, Vector2.one);
            _iconLayer = PrototypeUi.Rect("Icons", panel, Vector2.zero, Vector2.one);
            _selection = NewIcon(_iconLayer, MapGlyphs.Ring, Color.white, 30f);
            _pendingPin = NewIcon(_iconLayer, MapGlyphs.Diamond, MapGlyphs.ColorOf(MapMarkerKind.Waypoint), 18f);
            _pendingPin.gameObject.SetActive(false);

            _header = Left(PrototypeUi.Label(root, "", new Vector2(.02f, .93f), new Vector2(.70f, .99f), 22));
            _coverage = Left(PrototypeUi.Label(root, "", new Vector2(.72f, .89f), new Vector2(.98f, .99f), 13));
            _return = Left(PrototypeUi.Label(root, "", new Vector2(.72f, .80f), new Vector2(.98f, .89f), 13));
            _route = Left(PrototypeUi.Label(root, "", new Vector2(.72f, .72f), new Vector2(.98f, .80f), 12));
            _selectionTitle = Left(PrototypeUi.Label(root, "", new Vector2(.72f, .665f), new Vector2(.98f, .72f), 17));
            _selectionBody = Left(PrototypeUi.Label(root, "Tap a marker for details. Long-press to place your own.",
                new Vector2(.72f, .535f), new Vector2(.98f, .665f), 13));
            _routeButton = PrototypeUi.Button(root, "Route here", new Vector2(.72f, .475f), new Vector2(.845f, .53f));
            _routeButton.onClick.AddListener(OnRouteHere);
            _removeButton = PrototypeUi.Button(root, "Remove", new Vector2(.855f, .475f), new Vector2(.98f, .53f));
            _removeButton.onClick.AddListener(OnRemove);
            Left(PrototypeUi.Label(root, LegendText(), new Vector2(.72f, .225f), new Vector2(.98f, .465f), 12));
            _markerCount = PrototypeUi.Label(root, "", new Vector2(.72f, .21f), new Vector2(.98f, .235f), 12);

            Filter(root, "Hubs", MapLayers.SafeHubs, .72f);
            Filter(root, "Sites", MapLayers.Landmarks | MapLayers.Caches | MapLayers.Infrastructure, .787f);
            Filter(root, "Tasks", MapLayers.Objectives, .854f);
            Filter(root, "Marks", MapLayers.Waypoints, .921f);
            PrototypeUi.Button(root, "Center on car", new Vector2(.72f, .10f), new Vector2(.845f, .155f)).onClick.AddListener(CenterOnCar);
            PrototypeUi.Button(root, "Mark centre", new Vector2(.855f, .10f), new Vector2(.98f, .155f)).onClick.AddListener(
                () => AskPlace(_viewport != null ? _viewport.WidthPixels * .5f : 0f, _viewport != null ? _viewport.HeightPixels * .5f : 0f));
            PrototypeUi.Button(root, "Clear route", new Vector2(.72f, .04f), new Vector2(.845f, .095f)).onClick.AddListener(
                () => Report(_session.SetRouteTarget(string.Empty)));
            PrototypeUi.Button(root, "Close (M)", new Vector2(.855f, .04f), new Vector2(.98f, .095f)).onClick.AddListener(Close);

            _confirm = PrototypeUi.Rect("Confirm marker", root, new Vector2(.20f, .40f), new Vector2(.52f, .58f));
            _confirm.gameObject.AddComponent<Image>().color = new Color(.03f, .06f, .07f, .95f);
            _confirmText = PrototypeUi.Label(_confirm, "Place a marker here?", new Vector2(.05f, .5f), new Vector2(.95f, .95f), 16);
            PrototypeUi.Button(_confirm, "Place", new Vector2(.08f, .08f), new Vector2(.46f, .42f)).onClick.AddListener(ConfirmPlace);
            PrototypeUi.Button(_confirm, "Cancel", new Vector2(.54f, .08f), new Vector2(.92f, .42f)).onClick.AddListener(CancelPlace);
            _confirm.gameObject.SetActive(false);

            _presenter.RebuildMarkersNow();
            RefreshFilterColors();
        }

        // ------------------------------------------------------------------ frame

        void Update()
        {
            if (_closed) return;
            if (Time.frameCount > _openedFrame && CloseKeyPressed())
            {
                Close();
                return;
            }
            KeyboardNavigation();
            var gesture = _gestures.Tick(Time.unscaledTime);
            if (gesture.Kind == MapGestureKind.LongPress) AskPlace(gesture.X, gesture.Y);
        }

        void LateUpdate()
        {
            if (_closed || !EnsureViewport()) return;
            var raster = _presenter.Map.Raster;
            var uv = _viewport.UvFor(raster);
            _image.uvRect = new Rect(uv.X, uv.Y, uv.Width, uv.Height);
            int selected = SelectedIndex();
            LayoutIcons(selected);
            LayoutRoute();
            if (Time.unscaledTime >= _nextText)
            {
                _nextText = Time.unscaledTime + .25f;
                UpdateTexts(selected);
            }
        }

        bool EnsureViewport()
        {
            var size = _panel.rect.size;
            if (size.x < 2f || size.y < 2f) return false;
            if (_viewport == null)
            {
                _viewport = MapViewport.ForRaster(_presenter.Map.Raster, size.x, size.y);
                CenterOnCar();
            }
            _viewport.Resize(size.x, size.y);
            return true;
        }

        void LayoutIcons(int selected)
        {
            var markers = _presenter.Map.Markers;
            bool zoomedIn = _viewport.MetersPerPixel <= 2.2f;
            float pulse = 1f + .15f * Mathf.Sin(Time.unscaledTime * 4f);
            int used = 0;
            for (int i = 0; i < markers.Count; i++)
            {
                var m = markers[i];
                var (sx, sy) = _viewport.WorldToScreen(m.X, m.Z);
                float size = m.Kind == MapMarkerKind.ObjectiveArea
                    ? 2f * m.RadiusMeters / _viewport.MetersPerPixel
                    : MapGlyphs.SizeOf(m.Kind, !zoomedIn) * (m.Tracked ? pulse : 1f);
                if (!_viewport.IsOnScreen(sx, sy, size)) continue;
                if (used == _icons.Count)
                {
                    _icons.Add(NewIcon(_iconLayer, MapGlyphs.Disc, Color.white, 10f));
                    var label = PrototypeUi.Label(_icons[used], "", new Vector2(1f, 0f), new Vector2(1f, 1f), 12);
                    label.rectTransform.sizeDelta = new Vector2(180f, 20f);
                    label.rectTransform.anchoredPosition = new Vector2(96f, 0f);
                    label.alignment = TextAnchor.MiddleLeft;
                    label.color = new Color(.12f, .12f, .10f);
                    _labels.Add(label);
                }
                var icon = _icons[used];
                icon.gameObject.SetActive(true);
                icon.anchoredPosition = new Vector2(sx, sy);
                icon.sizeDelta = new Vector2(size, size);
                icon.localEulerAngles = new Vector3(0f, 0f, m.Kind == MapMarkerKind.Player ? -m.YawDegrees : 0f);
                var image = icon.GetComponent<Image>();
                image.sprite = MapGlyphs.SpriteOf(m.Kind);
                image.color = MapGlyphs.ColorOf(m.Kind);
                bool named = zoomedIn && (m.Kind == MapMarkerKind.Garage || m.Kind == MapMarkerKind.CampActive ||
                                          m.Kind == MapMarkerKind.LandmarkDiscovered || m.Kind == MapMarkerKind.Waypoint);
                _labels[used].text = named ? m.Label : string.Empty;
                _labels[used].rectTransform.localEulerAngles = Vector3.zero;
                used++;
            }
            for (int i = used; i < _icons.Count; i++) _icons[i].gameObject.SetActive(false);

            bool hasSelection = selected >= 0;
            _selection.gameObject.SetActive(hasSelection);
            if (hasSelection)
            {
                var m = markers[selected];
                var (sx, sy) = _viewport.WorldToScreen(m.X, m.Z);
                _selection.anchoredPosition = new Vector2(sx, sy);
                float size = m.Kind == MapMarkerKind.ObjectiveArea ? 2f * m.RadiusMeters / _viewport.MetersPerPixel + 8f : 30f;
                _selection.sizeDelta = new Vector2(size, size);
                _selection.SetAsLastSibling();
            }
            if (_pendingPin.gameObject.activeSelf)
            {
                var (px, py) = _viewport.WorldToScreen(_pendingX, _pendingZ);
                _pendingPin.anchoredPosition = new Vector2(px, py);
                _pendingPin.SetAsLastSibling();
            }
        }

        void LayoutRoute()
        {
            int used = 0;
            if (_model.HasRoute)
            {
                var markers = _presenter.Map.Markers;
                var player = markers[markers.Count - 1]; // the player arrow is always last
                var (ax, ay) = _viewport.WorldToScreen(player.X, player.Z);
                var (bx, by) = _viewport.WorldToScreen(_model.RouteTargetX, _model.RouteTargetZ);
                float length = Mathf.Sqrt((bx - ax) * (bx - ax) + (by - ay) * (by - ay));
                int count = Mathf.Min(MaxRouteDots, Mathf.FloorToInt(length / DotSpacing));
                for (int i = 1; i < count; i++)
                {
                    float t = i / (float)count;
                    float x = ax + (bx - ax) * t, y = ay + (by - ay) * t;
                    if (!_viewport.IsOnScreen(x, y, 4f)) continue;
                    if (used == _dots.Count) _dots.Add(NewIcon(_routeLayer, MapGlyphs.Disc, MapGlyphs.ColorOf(MapMarkerKind.Waypoint), 6f));
                    _dots[used].gameObject.SetActive(true);
                    _dots[used].anchoredPosition = new Vector2(x, y);
                    used++;
                }
            }
            for (int i = used; i < _dots.Count; i++) _dots[i].gameObject.SetActive(false);
        }

        void UpdateTexts(int selected)
        {
            MapScreenModel.Build(_presenter.Map, selected, _model);
            _header.text = _model.HeaderLine;
            _coverage.text = _model.CoverageLine;
            _return.text = _model.ReturnLine;
            _return.color = _model.ReturnFuelWarning ? new Color(1f, .75f, .3f) : Color.white;
            _route.text = _model.RouteLine;
            _markerCount.text = _model.MarkerCountLine;
            if (selected >= 0)
            {
                _selectionTitle.text = _model.SelectionTitle;
                _selectionBody.text = _model.SelectionBody;
            }
            bool waypoint = selected >= 0 && _presenter.Map.Markers[selected].Kind == MapMarkerKind.Waypoint;
            _routeButton.interactable = waypoint && !_presenter.Map.Markers[selected].Tracked;
            _removeButton.interactable = waypoint;
        }

        // ------------------------------------------------------------------ input

        public void OnPointerDown(PointerEventData eventData)
        {
            if (Local(eventData, out var p)) _gestures.PointerDown(eventData.pointerId, p.x, p.y, Time.unscaledTime);
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (Local(eventData, out var p)) Apply(_gestures.PointerMove(eventData.pointerId, p.x, p.y, Time.unscaledTime));
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            Local(eventData, out var p);
            Apply(_gestures.PointerUp(eventData.pointerId, p.x, p.y, Time.unscaledTime));
        }

        public void OnScroll(PointerEventData eventData)
        {
            if (_viewport == null || !Local(eventData, out var p)) return;
            float y = eventData.scrollDelta.y; // units differ between Input System versions: use the sign only
            if (y != 0f) _viewport.ZoomAt(y > 0f ? 1.2f : 1f / 1.2f, p.x, p.y);
        }

        bool Local(PointerEventData eventData, out Vector2 local) =>
            RectTransformUtility.ScreenPointToLocalPointInRectangle(_panel, eventData.position, eventData.pressEventCamera, out local);

        void Apply(MapGesture g)
        {
            if (_viewport == null) return;
            switch (g.Kind)
            {
                case MapGestureKind.Tap:
                    int hit = _viewport.HitTest(_presenter.Map.Markers, g.X, g.Y, TapRadius);
                    _selectedId = hit >= 0 ? Key(_presenter.Map.Markers[hit]) : string.Empty;
                    if (hit < 0)
                    {
                        _selectionTitle.text = string.Empty;
                        _selectionBody.text = "Tap a marker for details. Long-press to place your own.";
                    }
                    _nextText = 0f;
                    break;
                case MapGestureKind.Pan:
                    _viewport.Pan(g.DeltaX, g.DeltaY);
                    break;
                case MapGestureKind.Pinch:
                    _viewport.Pan(g.DeltaX, g.DeltaY);
                    _viewport.ZoomAt(g.Scale, g.X, g.Y);
                    break;
            }
        }

        void KeyboardNavigation()
        {
            var k = Keyboard.current;
            if (k == null || _viewport == null) return;
            float cx = _viewport.WidthPixels * .5f, cy = _viewport.HeightPixels * .5f;
            if (k.equalsKey.wasPressedThisFrame || k.numpadPlusKey.wasPressedThisFrame) _viewport.ZoomAt(1.25f, cx, cy);
            if (k.minusKey.wasPressedThisFrame || k.numpadMinusKey.wasPressedThisFrame) _viewport.ZoomAt(1f / 1.25f, cx, cy);
            float step = 400f * Time.unscaledDeltaTime;
            float dx = (k.leftArrowKey.isPressed ? step : 0f) - (k.rightArrowKey.isPressed ? step : 0f);
            float dy = (k.downArrowKey.isPressed ? step : 0f) - (k.upArrowKey.isPressed ? step : 0f);
            if (dx != 0f || dy != 0f) _viewport.Pan(dx, dy);
            if (k.cKey.wasPressedThisFrame) CenterOnCar();
        }

        static bool CloseKeyPressed() =>
            (Keyboard.current != null && (Keyboard.current.mKey.wasPressedThisFrame || Keyboard.current.escapeKey.wasPressedThisFrame)) ||
            (Gamepad.current != null && (Gamepad.current.selectButton.wasPressedThisFrame || Gamepad.current.buttonEast.wasPressedThisFrame));

        // ------------------------------------------------------------------ commands

        void AskPlace(float sx, float sy)
        {
            if (_viewport == null) return;
            var (x, z) = _viewport.ScreenToWorld(sx, sy);
            if (!_presenter.Map.Raster.ContainsWorld(x, z))
            {
                Report(Wayfinder.Core.CommandResult.Fail(Wayfinder.Core.FailureReason.InvalidTarget, "Markers must be on the survey sheet"));
                return;
            }
            _pendingX = x;
            _pendingZ = z;
            _pendingPin.gameObject.SetActive(true);
            var markers = _presenter.Map.Markers;
            var player = markers[markers.Count - 1];
            _confirmText.text = $"Place a marker {MapScreenModel.Distance(player.X, player.Z, x, z)} " +
                                $"{MapScreenModel.Bearing(player.X, player.Z, x, z)} of you? ({_model.MarkerCountLine})";
            _confirm.gameObject.SetActive(true);
            _confirm.SetAsLastSibling();
        }

        void ConfirmPlace()
        {
            _confirm.gameObject.SetActive(false);
            _pendingPin.gameObject.SetActive(false);
            var result = _session.PlaceMapMarker(_pendingX, _pendingZ, null, out var id);
            Report(result);
            if (!result.Success) return;
            _presenter.RebuildMarkersNow();
            _selectedId = "Waypoint:" + id;
            _nextText = 0f;
        }

        void CancelPlace()
        {
            _confirm.gameObject.SetActive(false);
            _pendingPin.gameObject.SetActive(false);
        }

        void OnRouteHere()
        {
            int selected = SelectedIndex();
            if (selected < 0) return;
            Report(_session.SetRouteTarget(_presenter.Map.Markers[selected].Id));
        }

        void OnRemove()
        {
            int selected = SelectedIndex();
            if (selected < 0) return;
            Report(_session.RemoveMapMarker(_presenter.Map.Markers[selected].Id));
            _selectedId = string.Empty;
            _selectionTitle.text = string.Empty;
            _selectionBody.text = "Marker removed.";
        }

        void CenterOnCar()
        {
            if (_viewport == null) return;
            var markers = _presenter.Map.Markers;
            if (markers.Count == 0) return;
            var player = markers[markers.Count - 1];
            _viewport.CenterOn(player.X, player.Z);
        }

        void Report(Wayfinder.Core.CommandResult result)
        {
            if (!result.Success)
            {
                string message = string.IsNullOrEmpty(result.Detail) ? result.Reason.ToString() : result.Detail;
                _toast?.Invoke(message);           // HUD toast (visible after closing)
                _selectedId = string.Empty;        // and right here, since the map covers the HUD
                _selectionTitle.text = "Can't do that";
                _selectionBody.text = message;
            }
            _presenter.RebuildMarkersNow();
            _nextText = 0f;
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            _gestures.Cancel();
            Destroy(_root.gameObject);
            _onClosed?.Invoke();
        }

        // ------------------------------------------------------------------ helpers

        int SelectedIndex()
        {
            if (string.IsNullOrEmpty(_selectedId)) return -1;
            var markers = _presenter.Map.Markers;
            for (int i = 0; i < markers.Count; i++)
                if (Key(markers[i]) == _selectedId) return i;
            return -1;
        }

        static string Key(MapMarker m) => m.Kind + ":" + m.Id;

        void Filter(Transform root, string caption, MapLayers layers, float x)
        {
            var button = PrototypeUi.Button(root, caption, new Vector2(x, .16f), new Vector2(x + .059f, .205f));
            _filterImages[layers] = button.GetComponent<Image>();
            button.onClick.AddListener(() =>
            {
                var now = _presenter.Layers;
                _presenter.SetLayers((now & layers) == layers ? now & ~layers : now | layers);
                RefreshFilterColors();
            });
        }

        void RefreshFilterColors()
        {
            foreach (var pair in _filterImages)
                pair.Value.color = (_presenter.Layers & pair.Key) == pair.Key
                    ? new Color(.10f, .32f, .30f, .95f)
                    : new Color(.18f, .18f, .18f, .95f);
        }

        static RectTransform NewIcon(Transform parent, Sprite sprite, Color color, float size)
        {
            var rect = PrototypeUi.Rect("Icon", parent, Vector2.zero, Vector2.zero);
            rect.sizeDelta = new Vector2(size, size);
            var image = rect.gameObject.AddComponent<Image>();
            image.sprite = sprite;
            image.color = color;
            image.raycastTarget = false;
            return rect;
        }

        static Text Left(Text text)
        {
            text.alignment = TextAnchor.UpperLeft;
            return text;
        }

        static string LegendText()
        {
            var sb = new StringBuilder("<b>Legend</b>\n");
            foreach (var entry in MapScreenModel.Legend)
                sb.Append("<color=#").Append(MapGlyphs.Hex(entry.Kind)).Append('>').Append(Shape(entry.Kind))
                    .Append("</color> ").Append(entry.Label).Append('\n');
            sb.Append("Fog = not yet explored · contours = surveyed from a vista");
            return sb.ToString();
        }

        static string Shape(MapMarkerKind kind)
        {
            var sprite = MapGlyphs.SpriteOf(kind);
            if (sprite == MapGlyphs.Arrow) return "▲";
            if (sprite == MapGlyphs.Square) return "■";
            if (sprite == MapGlyphs.Ring || sprite == MapGlyphs.WideRing) return "○";
            if (sprite == MapGlyphs.Diamond) return "◊";
            return "●";
        }
    }
}
