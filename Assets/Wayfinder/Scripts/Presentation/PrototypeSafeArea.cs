using UnityEngine;

namespace Wayfinder.Presentation
{
    public sealed class PrototypeSafeArea : MonoBehaviour
    {
        Rect _last;
        Vector2Int _size;
        void Update()
        {
            var safe = Screen.safeArea;
            if (Screen.width <= 0 || Screen.height <= 0) return;
            var size = new Vector2Int(Screen.width, Screen.height);
            if (_last == safe && _size == size) return;
            _last = safe;
            _size = size;
            var rect = (RectTransform)transform;
            rect.anchorMin = new Vector2(safe.xMin / Screen.width, safe.yMin / Screen.height);
            rect.anchorMax = new Vector2(safe.xMax / Screen.width, safe.yMax / Screen.height);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
        }
    }
}
