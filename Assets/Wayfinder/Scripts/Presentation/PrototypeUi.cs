using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace Wayfinder.Presentation
{
    /// <summary>Small offline prototype UI, not the final campaign presentation.</summary>
    public static class PrototypeUi
    {
        public static RectTransform CreateRoot(string name)
        {
            var go = new GameObject(name, typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 720);
            scaler.matchWidthOrHeight = 0.5f;
            if (Object.FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            }
            var safe = new GameObject("Safe Area", typeof(RectTransform), typeof(PrototypeSafeArea));
            safe.transform.SetParent(go.transform, false);
            return safe.GetComponent<RectTransform>();
        }

        public static RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var rect = new GameObject(name, typeof(RectTransform)).GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        public static Text Label(Transform parent, string value, Vector2 min, Vector2 max, int size = 24)
        {
            var text = Rect("Label", parent, min, max).gameObject.AddComponent<Text>();
            text.text = value;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = size;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.raycastTarget = false;
            return text;
        }

        public static Button Button(Transform parent, string label, Vector2 min, Vector2 max)
        {
            var rect = Rect(label, parent, min, max);
            var image = rect.gameObject.AddComponent<Image>();
            image.color = new Color(0.10f, 0.20f, 0.22f, 0.95f);
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            Label(rect, label, Vector2.zero, Vector2.one);
            return button;
        }
    }
}
