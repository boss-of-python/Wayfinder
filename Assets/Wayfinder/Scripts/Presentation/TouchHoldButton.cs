using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace Wayfinder.Presentation
{
    /// <summary>Each control owns one pointer; a second finger cannot release the first.</summary>
    public sealed class TouchHoldButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler
    {
        public event Action<bool> Changed;
        int? _pointer;
        public void OnPointerDown(PointerEventData data)
        {
            if (_pointer.HasValue) return;
            _pointer = data.pointerId;
            Changed?.Invoke(true);
        }
        public void OnPointerUp(PointerEventData data)
        {
            if (_pointer != data.pointerId) return;
            Release();
        }
        public void OnPointerExit(PointerEventData data) => OnPointerUp(data);
        void OnDisable() => Release();
        public void Release()
        {
            if (!_pointer.HasValue) return;
            _pointer = null;
            Changed?.Invoke(false);
        }
    }
}
