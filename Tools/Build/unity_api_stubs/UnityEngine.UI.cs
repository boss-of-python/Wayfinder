// ---------------------------------------------------------------------------------------------
// UnityEngine.UI (STUB) — the com.unity.ugui 2.0.0 runtime assembly
//
// Kept in its OWN stub assembly on purpose: Unity asmdefs require *direct* references, so a
// Wayfinder assembly that uses these types without `UnityEngine.UI` in its .asmdef is a real
// CS0234. Splitting them out is what lets the type-check catch that class of error.
// See the header of UnityEngine.CoreModule.cs for what these stubs can and cannot prove.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityEngine.EventSystems
{
    public interface IEventSystemHandler { }

    public class BaseEventData
    {
        public BaseEventData(EventSystem eventSystem) { }
        public EventSystem currentInputModule => throw new NotImplementedException();
        public GameObject selectedObject { get; set; }
    }

    public class PointerEventData : BaseEventData
    {
        public PointerEventData(EventSystem eventSystem) : base(eventSystem) { }
        public int pointerId { get; set; }
        public Vector2 position { get; set; }
        public Vector2 delta { get; set; }
        public Vector2 pressPosition { get; set; }
        public Vector2 scrollDelta { get; set; }
        public float clickTime { get; set; }
        public int clickCount { get; set; }
        public bool dragging { get; set; }
        public bool used { get; set; }
        public bool eligibleForClick { get; set; }
        public GameObject pointerEnter { get; set; }
        public GameObject pointerPress { get; set; }
        public GameObject pointerDrag { get; set; }
        public GameObject pointerCurrentRaycast => throw new NotImplementedException();
        public GameObject rawPointerPress { get; set; }
        public RaycastResult pointerPressRaycast { get; set; }
        public RaycastResult pointerCurrentRaycastResult => throw new NotImplementedException();
        public InputButton button { get; set; }
        public List<RaycastResult> hovered => throw new NotImplementedException();
        public bool IsPointerMoving() => throw new NotImplementedException();
        public bool IsScrolling() => throw new NotImplementedException();
        public Camera pressEventCamera => throw new NotImplementedException();
        public Camera enterEventCamera => throw new NotImplementedException();
    }

    public struct RaycastResult
    {
        public GameObject gameObject { get; set; }
        public Camera eventCamera { get; set; }
        public float distance { get; set; }
        public int depth { get; set; }
        public int sortingLayer { get; set; }
        public int sortingOrder { get; set; }
        public Vector3 worldPosition { get; set; }
        public Vector2 screenPosition { get; set; }
        public bool isValid => throw new NotImplementedException();
        public void Clear() => throw new NotImplementedException();
    }

    public enum InputButton { Left = 0, Right = 1, Middle = 2 }

    public class AxisEventData : BaseEventData
    {
        public AxisEventData(EventSystem eventSystem) : base(eventSystem) { }
        public Vector2 moveVector { get; set; }
        public MoveDirection moveDir { get; set; }
    }

    public enum MoveDirection { Left = 0, Up = 1, Right = 2, Down = 3, None = 4 }

    public abstract class UIBehaviour : MonoBehaviour
    {
        protected virtual void Awake() { }
        protected virtual void OnEnable() { }
        protected virtual void Start() { }
        protected virtual void OnDisable() { }
        protected virtual void OnDestroy() { }
        public bool IsActive() => throw new NotImplementedException();
        public bool IsDestroyed() => throw new NotImplementedException();
    }

    public class EventSystem : UIBehaviour
    {
        public static EventSystem current { get; set; }
        public BaseInputModule currentInputModule => throw new NotImplementedException();
        public GameObject currentSelectedGameObject { get; set; }
        public GameObject firstSelectedGameObject { get; set; }
        public int currentSelectedGameObjectIndex => throw new NotImplementedException();
        public bool sendNavigationEvents { get; set; }
        public int pixelDragThreshold { get; set; }
        public bool alreadySelecting => throw new NotImplementedException();
        public void SetSelectedGameObject(GameObject selected) => throw new NotImplementedException();
        public void SetSelectedGameObject(GameObject selected, BaseEventData pointer) => throw new NotImplementedException();
        public void UpdateModules() => throw new NotImplementedException();
        public bool IsPointerOverGameObject() => throw new NotImplementedException();
        public bool IsPointerOverGameObject(int pointerId) => throw new NotImplementedException();
        public void RaycastAll(PointerEventData eventData, List<RaycastResult> raycastResults) => throw new NotImplementedException();
    }

    public abstract class BaseInputModule : UIBehaviour
    {
        public EventSystem eventSystem => throw new NotImplementedException();
        public BaseInput input => throw new NotImplementedException();
        public virtual void Process() => throw new NotImplementedException();
        protected static RaycastResult FindFirstRaycast(List<RaycastResult> candidates) => throw new NotImplementedException();
        protected static MoveDirection DetermineMoveDirection(float x, float y) => throw new NotImplementedException();
        protected static MoveDirection DetermineMoveDirection(float x, float y, float deadZone) => throw new NotImplementedException();
    }

    public abstract class PointerInputModule : BaseInputModule
    {
        protected PointerEventData GetPointerData(int id, out bool created) => throw new NotImplementedException();
        protected PointerEventData GetPointerData(int id) => throw new NotImplementedException();
        protected void RemovePointerData(PointerEventData data) => throw new NotImplementedException();
        protected PointerEventData GetLastPointerEventData(int id) => throw new NotImplementedException();
    }

    public class StandaloneInputModule : PointerInputModule
    {
        public string horizontalAxis { get; set; }
        public string verticalAxis { get; set; }
        public string submitButton { get; set; }
        public string cancelButton { get; set; }
        public float inputActionsPerSecond { get; set; }
        public float repeatDelay { get; set; }
        public bool forceModuleActive { get; set; }
    }

    public class BaseInput : UIBehaviour
    {
        public virtual Vector2 compositionCursorPos { get; set; }
        public virtual bool mousePresent => throw new NotImplementedException();
        public virtual Vector2 mousePosition => throw new NotImplementedException();
        public virtual Vector2 mouseScrollDelta => throw new NotImplementedException();
        public virtual bool touchSupported => throw new NotImplementedException();
        public virtual int touchCount => throw new NotImplementedException();
        public virtual bool GetMouseButtonDown(int button) => throw new NotImplementedException();
        public virtual bool GetMouseButtonUp(int button) => throw new NotImplementedException();
        public virtual bool GetMouseButton(int button) => throw new NotImplementedException();
        public virtual float GetAxisRaw(string axisName) => throw new NotImplementedException();
        public virtual bool GetButtonDown(string buttonName) => throw new NotImplementedException();
    }

    public interface IPointerEnterHandler : IEventSystemHandler
    {
        void OnPointerEnter(PointerEventData eventData);
    }

    public interface IPointerExitHandler : IEventSystemHandler
    {
        void OnPointerExit(PointerEventData eventData);
    }

    public interface IPointerDownHandler : IEventSystemHandler
    {
        void OnPointerDown(PointerEventData eventData);
    }

    public interface IPointerUpHandler : IEventSystemHandler
    {
        void OnPointerUp(PointerEventData eventData);
    }

    public interface IPointerClickHandler : IEventSystemHandler
    {
        void OnPointerClick(PointerEventData eventData);
    }

    public interface IBeginDragHandler : IEventSystemHandler
    {
        void OnBeginDrag(PointerEventData eventData);
    }

    public interface IDragHandler : IEventSystemHandler
    {
        void OnDrag(PointerEventData eventData);
    }

    public interface IEndDragHandler : IEventSystemHandler
    {
        void OnEndDrag(PointerEventData eventData);
    }

    public interface IScrollHandler : IEventSystemHandler
    {
        void OnScroll(PointerEventData eventData);
    }

    public interface ISelectHandler : IEventSystemHandler
    {
        void OnSelect(BaseEventData eventData);
    }

    public interface IDeselectHandler : IEventSystemHandler
    {
        void OnDeselect(BaseEventData eventData);
    }

    public interface IMoveHandler : IEventSystemHandler
    {
        void OnMove(AxisEventData eventData);
    }

    public interface ISubmitHandler : IEventSystemHandler
    {
        void OnSubmit(BaseEventData eventData);
    }

    public interface ICancelHandler : IEventSystemHandler
    {
        void OnCancel(BaseEventData eventData);
    }

    public interface IInitializePotentialDragHandler : IEventSystemHandler
    {
        void OnInitializePotentialDrag(PointerEventData eventData);
    }
}

namespace UnityEngine
{
    public class GraphicRaycaster : Behaviour
    {
        public int sortOrderPriority => throw new NotImplementedException();
        public EventSystems.EventSystem eventCamera => throw new NotImplementedException();
        public bool ignoreReversedGraphics { get; set; }
        public EventSystems.BlockingObjects blockingObjects { get; set; }
    }

    public class RectMask2D : EventSystems.UIBehaviour
    {
        public RectTransform rectTransform => throw new NotImplementedException();
        public Vector4 padding { get; set; }
        public Vector2Int softness { get; set; }
    }
}

namespace UnityEngine.EventSystems
{
    public enum BlockingObjects { None = 0, TwoD = 1, ThreeD = 2, All = 3 }
}

namespace UnityEngine.UI
{
    public class Selectable : EventSystems.UIBehaviour
    {
        public UnityEngine.UI.Navigation navigation { get; set; }
        public Selectable.Transition transition { get; set; }
        public ColorBlock colors { get; set; }
        public SpriteState spriteState { get; set; }
        public Graphic targetGraphic { get; set; }
        public bool interactable { get; set; }
        public Selectable FindSelectable(UnityEngine.Vector3 dir) => throw new NotImplementedException();
        public Selectable FindSelectableOnLeft() => throw new NotImplementedException();
        public Selectable FindSelectableOnRight() => throw new NotImplementedException();
        public Selectable FindSelectableOnUp() => throw new NotImplementedException();
        public Selectable FindSelectableOnDown() => throw new NotImplementedException();
        public void Select() => throw new NotImplementedException();
        public bool IsHighlighted() => throw new NotImplementedException();
        public bool IsPressed() => throw new NotImplementedException();
        public static Selectable[] allSelectablesArray => throw new NotImplementedException();
        public enum Transition { None = 0, ColorTint = 1, SpriteSwap = 2, Animation = 3 }
    }

    public struct Navigation
    {
        public Navigation.Mode mode { get; set; }
        public Selectable selectOnUp { get; set; }
        public Selectable selectOnDown { get; set; }
        public Selectable selectOnLeft { get; set; }
        public Selectable selectOnRight { get; set; }
        public static Navigation defaultNavigation => throw new NotImplementedException();
        public enum Mode { None = 0, Horizontal = 1, Vertical = 2, Automatic = 3, Explicit = 4 }
    }

    public struct ColorBlock
    {
        public UnityEngine.Color normalColor { get; set; }
        public UnityEngine.Color highlightedColor { get; set; }
        public UnityEngine.Color pressedColor { get; set; }
        public UnityEngine.Color selectedColor { get; set; }
        public UnityEngine.Color disabledColor { get; set; }
        public float colorMultiplier { get; set; }
        public float fadeDuration { get; set; }
        public static ColorBlock defaultColorBlock => throw new NotImplementedException();
    }

    public struct SpriteState
    {
        public UnityEngine.Sprite highlightedSprite { get; set; }
        public UnityEngine.Sprite pressedSprite { get; set; }
        public UnityEngine.Sprite selectedSprite { get; set; }
        public UnityEngine.Sprite disabledSprite { get; set; }
    }

    public class Button : Selectable
    {
        public class ButtonClickedEvent : UnityEngine.Events.UnityEvent { }
        public ButtonClickedEvent onClick => throw new NotImplementedException();
        public void OnPointerClick(UnityEngine.EventSystems.PointerEventData eventData) => throw new NotImplementedException();
        public void OnSubmit(UnityEngine.EventSystems.BaseEventData eventData) => throw new NotImplementedException();
        public virtual void Press() => throw new NotImplementedException();
    }

    public class Text : UnityEngine.MaskableGraphic
    {
        public UnityEngine.Font font { get; set; }
        public string text { get; set; }
        public int fontSize { get; set; }
        public FontStyle fontStyle { get; set; }
        public float lineSpacing { get; set; }
        public bool supportRichText { get; set; }
        public TextAnchor alignment { get; set; }
        public HorizontalWrapMode horizontalOverflow { get; set; }
        public VerticalWrapMode verticalOverflow { get; set; }
        public bool resizeTextForBestFit { get; set; }
        public int resizeTextMinSize { get; set; }
        public int resizeTextMaxSize { get; set; }
        public float pixelsPerUnit => throw new NotImplementedException();
        public int cachedTextGeneratorLineCount => throw new NotImplementedException();
        public float preferredWidth => throw new NotImplementedException();
        public float preferredHeight => throw new NotImplementedException();
        public float flexibleWidth => throw new NotImplementedException();
        public float flexibleHeight => throw new NotImplementedException();
        public float minWidth => throw new NotImplementedException();
        public float minHeight => throw new NotImplementedException();
        public int layoutPriority => throw new NotImplementedException();
        public void CalculateLayoutInputHorizontal() => throw new NotImplementedException();
        public void CalculateLayoutInputVertical() => throw new NotImplementedException();
        public void FontTextureChanged() => throw new NotImplementedException();
        public void SetVerticesDirty() => throw new NotImplementedException();
    }

    public enum FontStyle { Normal = 0, Bold = 1, Italic = 2, BoldAndItalic = 3 }
    public enum TextAnchor { UpperLeft, UpperCenter, UpperRight, MiddleLeft, MiddleCenter, MiddleRight, LowerLeft, LowerCenter, LowerRight }
    public enum HorizontalWrapMode { Wrap = 0, Overflow = 1 }
    public enum VerticalWrapMode { Truncate = 0, Overflow = 1 }

    public class Image : UnityEngine.MaskableGraphic
    {
        public UnityEngine.Sprite sprite { get; set; }
        public UnityEngine.Sprite overrideSprite { get; set; }
        public Image.Type type { get; set; }
        public Image.FillMethod fillMethod { get; set; }
        public float fillAmount { get; set; }
        public bool fillClockwise { get; set; }
        public int fillOrigin { get; set; }
        public float pixelsPerUnitMultiplier { get; set; }
        public UnityEngine.Texture mainTexture => throw new NotImplementedException();
        public virtual UnityEngine.Texture GetMainTexture() => throw new NotImplementedException();
        public enum Type { Simple = 0, Sliced = 1, Tiled = 2, Filled = 3 }
        public enum FillMethod { Horizontal = 0, Vertical = 1, Radial90 = 2, Radial180 = 3, Radial360 = 4 }
        public enum OriginHorizontal { Left = 0, Right = 1 }
        public virtual void SetNativeSize() => throw new NotImplementedException();
    }

    public class RawImage : UnityEngine.MaskableGraphic
    {
        public UnityEngine.Texture texture { get; set; }
        public UnityEngine.Rect uvRect { get; set; }
        public virtual UnityEngine.Texture GetMainTexture() => throw new NotImplementedException();
        public void SetNativeSize() => throw new NotImplementedException();
    }

    public class InputField : Selectable
    {
        public string text { get; set; }
        public int characterLimit { get; set; }
        public InputField.ContentType contentType { get; set; }
        public Text textComponent { get; set; }
        public Graphic placeholder { get; set; }
        public enum ContentType { Standard = 0, Autocorrected = 1, IntegerNumber = 2, DecimalNumber = 3, Alphanumeric = 4, Name = 5, EmailAddress = 6, Password = 7, Pin = 8, Custom = 9 }
    }

    public class Toggle : Selectable
    {
        public bool isOn { get; set; }
        public Graphic graphic { get; set; }
        public Toggle.ToggleEvent onValueChanged => throw new NotImplementedException();
        public class ToggleEvent : UnityEngine.Events.UnityEvent<bool> { }
    }

    public class Slider : Selectable
    {
        public float value { get; set; }
        public float minValue { get; set; }
        public float maxValue { get; set; }
        public bool wholeNumbers { get; set; }
        public Slider.SliderEvent onValueChanged => throw new NotImplementedException();
        public class SliderEvent : UnityEngine.Events.UnityEvent<float> { }
    }

    public class Scrollbar : Selectable
    {
        public float value { get; set; }
        public float size { get; set; }
        public Scrollbar.ScrollEvent onValueChanged => throw new NotImplementedException();
        public class ScrollEvent : UnityEngine.Events.UnityEvent<float> { }
    }

    public class ScrollRect : Selectable
    {
        public UnityEngine.RectTransform content { get; set; }
        public bool horizontal { get; set; }
        public bool vertical { get; set; }
        public ScrollRect.MovementType movementType { get; set; }
        public float elasticity { get; set; }
        public bool inertia { get; set; }
        public float decelerationRate { get; set; }
        public float scrollSensitivity { get; set; }
        public UnityEngine.Vector2 velocity { get; set; }
        public UnityEngine.Vector2 normalizedPosition { get; set; }
        public ScrollRect.ScrollRectEvent onValueChanged => throw new NotImplementedException();
        public class ScrollRectEvent : UnityEngine.Events.UnityEvent<UnityEngine.Vector2> { }
        public enum MovementType { Unrestricted = 0, Elastic = 1, Clamped = 2 }
        public virtual void OnScroll(UnityEngine.EventSystems.PointerEventData data) => throw new NotImplementedException();
    }

    public class Mask : UnityEngine.MaskableGraphic
    {
        public bool showMaskGraphic { get; set; }
    }

    public class LayoutGroup : UnityEngine.EventSystems.UIBehaviour { public UnityEngine.RectOffset padding { get; set; } }

    public class HorizontalOrVerticalLayoutGroup : LayoutGroup
    {
        public float spacing { get; set; }
        public bool childControlWidth { get; set; }
        public bool childControlHeight { get; set; }
        public bool childForceExpandWidth { get; set; }
        public bool childForceExpandHeight { get; set; }
        public bool childScaleWidth { get; set; }
        public bool childScaleHeight { get; set; }
        public bool reverseArrangement { get; set; }
    }

    public class HorizontalLayoutGroup : HorizontalOrVerticalLayoutGroup { }
    public class VerticalLayoutGroup : HorizontalOrVerticalLayoutGroup { }

    public class GridLayoutGroup : LayoutGroup
    {
        public UnityEngine.Vector2 cellSize { get; set; }
        public UnityEngine.Vector2 spacing { get; set; }
        public GridLayoutGroup.Corner startCorner { get; set; }
        public GridLayoutGroup.Axis startAxis { get; set; }
        public TextAnchor childAlignment { get; set; }
        public GridLayoutGroup.Constraint constraint { get; set; }
        public int constraintCount { get; set; }
        public enum Corner { UpperLeft = 0, UpperRight = 1, LowerLeft = 2, LowerRight = 3 }
        public enum Axis { Horizontal = 0, Vertical = 1 }
        public enum Constraint { Flexible = 0, FixedColumnCount = 1, FixedRowCount = 2 }
    }

    public class LayoutElement : UnityEngine.EventSystems.UIBehaviour
    {
        public bool ignoreLayout { get; set; }
        public float minWidth { get; set; }
        public float minHeight { get; set; }
        public float preferredWidth { get; set; }
        public float preferredHeight { get; set; }
        public float flexibleWidth { get; set; }
        public float flexibleHeight { get; set; }
        public int layoutPriority { get; set; }
        public void CalculateLayoutInputHorizontal() => throw new NotImplementedException();
        public void CalculateLayoutInputVertical() => throw new NotImplementedException();
    }

    public class ContentSizeFitter : UnityEngine.EventSystems.UIBehaviour
    {
        public ContentSizeFitter.FitMode horizontalFit { get; set; }
        public ContentSizeFitter.FitMode verticalFit { get; set; }
        public enum FitMode { Unconstrained = 0, MinSize = 1, PreferredSize = 2 }
    }

    public class AspectRatioFitter : UnityEngine.EventSystems.UIBehaviour
    {
        public AspectRatioFitter.AspectMode aspectMode { get; set; }
        public float aspectRatio { get; set; }
        public enum AspectMode { None = 0, WidthControlsHeight = 1, HeightControlsWidth = 2, FitInParent = 3, EnvelopeParent = 4 }
    }
}

namespace UnityEngine
{
    public class RectOffset
    {
        public RectOffset() { }
        public RectOffset(int left, int right, int top, int bottom) { }
        public int left { get; set; }
        public int right { get; set; }
        public int top { get; set; }
        public int bottom { get; set; }
    }
}
