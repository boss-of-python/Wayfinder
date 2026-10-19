// ---------------------------------------------------------------------------------------------
// Unity.InputSystem (STUB) — com.unity.inputsystem 1.20.1
//
// Only the surface this repository uses. See UnityEngine.CoreModule.cs for what stubs prove.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityEngine.InputSystem
{
    public class InputControl
    {
        public string name => throw new NotImplementedException();
        public string path => throw new NotImplementedException();
        public InputDevice device => throw new NotImplementedException();
        public bool IsPressed() => throw new NotImplementedException();
        public bool IsActuated(float threshold = 0f) => throw new NotImplementedException();
        public T ReadValue<T>() where T : struct => throw new NotImplementedException();
        public object ReadValueAsObject() => throw new NotImplementedException();
        public T ReadUnprocessedValue<T>() where T : struct => throw new NotImplementedException();
        public string ReadValueAsString() => throw new NotImplementedException();
        public bool wasPressedThisFrame => throw new NotImplementedException();
        public bool wasReleasedThisFrame => throw new NotImplementedException();
        public bool wasPerformedThisFrame => throw new NotImplementedException();
        public Type valueType => throw new NotImplementedException();
    }

    public class InputControl<TValue> : InputControl where TValue : struct
    {
        public new TValue ReadValue() => throw new NotImplementedException();
        public TValue ReadUnprocessedValue() => throw new NotImplementedException();
    }

    public class ButtonControl : InputControl<float> { public bool isPressed => throw new NotImplementedException(); }
    public class AxisControl : InputControl<float> { }
    public class Vector2Control : InputControl<UnityEngine.Vector2> { public AxisControl x => throw new NotImplementedException(); public AxisControl y => throw new NotImplementedException(); }
    public class Vector3Control : InputControl<UnityEngine.Vector3> { public AxisControl x => throw new NotImplementedException(); public AxisControl y => throw new NotImplementedException(); public AxisControl z => throw new NotImplementedException(); }
    public class QuaternionControl : InputControl<UnityEngine.Quaternion> { }
    public class AnyKeyControl : ButtonControl { }
    public class KeyControl : ButtonControl { public Key keyCode => throw new NotImplementedException(); }
    public class TouchControl : InputControl { public ButtonControl press => throw new NotImplementedException(); public IntegerControl touchId => throw new NotImplementedException(); public Vector2Control position => throw new NotImplementedException(); public Vector2Control startPosition => throw new NotImplementedException(); public AxisControl pressure => throw new NotImplementedException(); public Vector2Control delta => throw new NotImplementedException(); public TouchPhase phase => throw new NotImplementedException(); }
    public class IntegerControl : InputControl<int> { }

    public enum TouchPhase { None = 0, Began = 1, Moved = 2, Ended = 3, Canceled = 4 }
    public enum Key { None = 0, Space = 32, Escape = 27, Enter = 13, A = 97, D = 100, S = 115, W = 119, LeftShift = 304, LeftCtrl = 306 }

    public class InputDevice : InputControl
    {
        public string deviceId => throw new NotImplementedException();
        public string layout => throw new NotImplementedException();
        public bool enabled { get; set; }
        public bool canRunInBackground => throw new NotImplementedException();
        public bool added => throw new NotImplementedException();
        public bool remote => throw new NotImplementedException();
        public bool native => throw new NotImplementedException();
        public bool updateBeforeRender => throw new NotImplementedException();
        public InputControl this[string path] => throw new NotImplementedException();
        public InputControl TryGetChildControl(string path) => throw new NotImplementedException();
        public InputControl GetChildControl(string path) => throw new NotImplementedException();
        public IEnumerable<InputControl> allControls => throw new NotImplementedException();
        public InputDeviceChange lastUpdateTime => throw new NotImplementedException();
        public void MakeCurrent() => throw new NotImplementedException();
        public static InputDevice ReadValueFromDevice<TValue>(int deviceId) => throw new NotImplementedException();
    }

    public enum InputDeviceChange { Added = 0, Removed = 1, Disconnected = 2, Reconnected = 3, Enabled = 4, Disabled = 5, UsageChanged = 6, ConfigurationChanged = 7, Destroyed = 8 }

    public class InputDeviceDescription
    {
        public string interfaceName { get; set; }
        public string deviceClass { get; set; }
        public string manufacturer { get; set; }
        public string product { get; set; }
        public string serial { get; set; }
        public string version { get; set; }
        public string capabilities { get; set; }
    }

    public class Keyboard : InputDevice
    {
        public static Keyboard current { get; }
        public AnyKeyControl anyKey => throw new NotImplementedException();
        public KeyControl spaceKey => throw new NotImplementedException();
        public KeyControl escapeKey => throw new NotImplementedException();
        public KeyControl enterKey => throw new NotImplementedException();
        public KeyControl tabKey => throw new NotImplementedException();
        public KeyControl backspaceKey => throw new NotImplementedException();
        public KeyControl deleteKey => throw new NotImplementedException();
        public KeyControl leftShiftKey => throw new NotImplementedException();
        public KeyControl rightShiftKey => throw new NotImplementedException();
        public KeyControl leftCtrlKey => throw new NotImplementedException();
        public KeyControl leftAltKey => throw new NotImplementedException();
        public KeyControl aKey => throw new NotImplementedException();
        public KeyControl bKey => throw new NotImplementedException();
        public KeyControl cKey => throw new NotImplementedException();
        public KeyControl dKey => throw new NotImplementedException();
        public KeyControl eKey => throw new NotImplementedException();
        public KeyControl fKey => throw new NotImplementedException();
        public KeyControl gKey => throw new NotImplementedException();
        public KeyControl hKey => throw new NotImplementedException();
        public KeyControl iKey => throw new NotImplementedException();
        public KeyControl jKey => throw new NotImplementedException();
        public KeyControl kKey => throw new NotImplementedException();
        public KeyControl lKey => throw new NotImplementedException();
        public KeyControl mKey => throw new NotImplementedException();
        public KeyControl nKey => throw new NotImplementedException();
        public KeyControl oKey => throw new NotImplementedException();
        public KeyControl pKey => throw new NotImplementedException();
        public KeyControl qKey => throw new NotImplementedException();
        public KeyControl rKey => throw new NotImplementedException();
        public KeyControl sKey => throw new NotImplementedException();
        public KeyControl tKey => throw new NotImplementedException();
        public KeyControl uKey => throw new NotImplementedException();
        public KeyControl vKey => throw new NotImplementedException();
        public KeyControl wKey => throw new NotImplementedException();
        public KeyControl xKey => throw new NotImplementedException();
        public KeyControl yKey => throw new NotImplementedException();
        public KeyControl zKey => throw new NotImplementedException();
        public KeyControl digit1Key => throw new NotImplementedException();
        public KeyControl digit2Key => throw new NotImplementedException();
        public KeyControl digit3Key => throw new NotImplementedException();
        public KeyControl digit4Key => throw new NotImplementedException();
        public KeyControl digit5Key => throw new NotImplementedException();
        public KeyControl digit6Key => throw new NotImplementedException();
        public KeyControl digit7Key => throw new NotImplementedException();
        public KeyControl digit8Key => throw new NotImplementedException();
        public KeyControl digit9Key => throw new NotImplementedException();
        public KeyControl digit0Key => throw new NotImplementedException();
        public KeyControl equalsKey => throw new NotImplementedException();
        public KeyControl minusKey => throw new NotImplementedException();
        public KeyControl numpadPlusKey => throw new NotImplementedException();
        public KeyControl numpadMinusKey => throw new NotImplementedException();
        public KeyControl numpadEnterKey => throw new NotImplementedException();
        public KeyControl numpad0Key => throw new NotImplementedException();
        public KeyControl upArrowKey => throw new NotImplementedException();
        public KeyControl downArrowKey => throw new NotImplementedException();
        public KeyControl leftArrowKey => throw new NotImplementedException();
        public KeyControl rightArrowKey => throw new NotImplementedException();
        public Vector2Control imeCursor => throw new NotImplementedException();
        public KeyControl this[Key key] => throw new NotImplementedException();
        public bool IsPressed() => throw new NotImplementedException();
    }

    public class Mouse : InputDevice
    {
        public static Mouse current { get; }
        public ButtonControl leftButton => throw new NotImplementedException();
        public ButtonControl rightButton => throw new NotImplementedException();
        public ButtonControl middleButton => throw new NotImplementedException();
        public Vector2Control position => throw new NotImplementedException();
        public Vector2Control delta => throw new NotImplementedException();
        public Vector2Control scroll => throw new NotImplementedException();
    }

    public class Pointer : InputDevice
    {
        public static Pointer current { get; }
        public Vector2Control position => throw new NotImplementedException();
        public Vector2Control delta => throw new NotImplementedException();
        public ButtonControl press => throw new NotImplementedException();
        public Vector2Control scroll => throw new NotImplementedException();
        public int pointerId => throw new NotImplementedException();
    }

    public class Touchscreen : InputDevice
    {
        public static Touchscreen current { get; }
        public TouchControl primaryTouch => throw new NotImplementedException();
        public UnityEngine.Vector2 primaryTouchPosition => throw new NotImplementedException();
        public int activeTouches => throw new NotImplementedException();
    }

    public class Gamepad : InputDevice
    {
        public static Gamepad current { get; }
        public static UnityEngine.InputSystem.Utilities.ReadOnlyArray<Gamepad> all => throw new NotImplementedException();
        public ButtonControl buttonSouth => throw new NotImplementedException();
        public ButtonControl buttonNorth => throw new NotImplementedException();
        public ButtonControl buttonEast => throw new NotImplementedException();
        public ButtonControl buttonWest => throw new NotImplementedException();
        public ButtonControl startButton => throw new NotImplementedException();
        public ButtonControl selectButton => throw new NotImplementedException();
        public ButtonControl leftShoulder => throw new NotImplementedException();
        public ButtonControl rightShoulder => throw new NotImplementedException();
        public ButtonControl dpadUp => throw new NotImplementedException();
        public ButtonControl dpadDown => throw new NotImplementedException();
        public ButtonControl dpadLeft => throw new NotImplementedException();
        public ButtonControl dpadRight => throw new NotImplementedException();
        public StickControl leftStick => throw new NotImplementedException();
        public StickControl rightStick => throw new NotImplementedException();
        public AxisControl leftTrigger => throw new NotImplementedException();
        public AxisControl rightTrigger => throw new NotImplementedException();
        public bool IsPressed() => throw new NotImplementedException();
    }

    public class StickControl : Vector2Control
    {
        public ButtonControl up => throw new NotImplementedException();
        public ButtonControl down => throw new NotImplementedException();
        public ButtonControl left => throw new NotImplementedException();
        public ButtonControl right => throw new NotImplementedException();
    }

    public class Joystick : InputDevice { public static Joystick current { get; } public StickControl stick => throw new NotImplementedException(); public ButtonControl trigger => throw new NotImplementedException(); }

    public class Accelerometer : InputDevice
    {
        public static Accelerometer current { get; }
        public Vector3Control acceleration => throw new NotImplementedException();
        public UnityEngine.Vector3 accelerationValue => throw new NotImplementedException();
    }

    public class Gyroscope : InputDevice
    {
        public static Gyroscope current { get; }
        public Vector3Control angularVelocity => throw new NotImplementedException();
        public UnityEngine.Vector3 angularVelocityValue => throw new NotImplementedException();
    }

    public class LinearAccelerationSensor : InputDevice { public static LinearAccelerationSensor current { get; } public Vector3Control acceleration => throw new NotImplementedException(); }

    public class InputActionPhasePlaceholder { }

    public enum InputActionPhase { Disabled = 0, Waiting = 1, Started = 2, Performed = 3, Canceled = 4 }

    public class InputAction : IDisposable
    {
        public InputAction() { }
        public InputAction(string name = null, InputActionType type = InputActionType.Value, string binding = null, string interactions = null, string processors = null, string groups = null, string expectedControlType = null) { }
        public string name { get; set; }
        public InputActionType type { get; set; }
        public string id => throw new NotImplementedException();
        public string expectedControlType { get; set; }
        public InputActionMap actionMap => throw new NotImplementedException();
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputBinding> bindings => throw new NotImplementedException();
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputControl> controls => throw new NotImplementedException();
        public bool enabled { get; }
        public InputActionPhase phase => throw new NotImplementedException();
        public bool inProgress => throw new NotImplementedException();
        public bool triggered => throw new NotImplementedException();
        public InputControl activeControl => throw new NotImplementedException();
        public event Action<InputAction.CallbackContext> started;
        public event Action<InputAction.CallbackContext> performed;
        public event Action<InputAction.CallbackContext> canceled;
        public TValue ReadValue<TValue>() where TValue : struct => throw new NotImplementedException();
        public object ReadValueAsObject() => throw new NotImplementedException();
        public bool IsPressed() => throw new NotImplementedException();
        public bool IsInProgress() => throw new NotImplementedException();
        public bool WasPressedThisFrame() => throw new NotImplementedException();
        public bool WasReleasedThisFrame() => throw new NotImplementedException();
        public bool WasPerformedThisFrame() => throw new NotImplementedException();
        public bool WasCompletedThisFrame() => throw new NotImplementedException();
        public void Enable() => throw new NotImplementedException();
        public void Disable() => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public void Reset() => throw new NotImplementedException();
        public InputAction Clone() => throw new NotImplementedException();
        public string ToJson() => throw new NotImplementedException();
        public void LoadFromJson(string json) => throw new NotImplementedException();

        public struct CallbackContext
        {
            public InputActionPhase phase => throw new NotImplementedException();
            public bool started => throw new NotImplementedException();
            public bool performed => throw new NotImplementedException();
            public bool canceled => throw new NotImplementedException();
            public InputAction action => throw new NotImplementedException();
            public InputControl control => throw new NotImplementedException();
            public double startTime => throw new NotImplementedException();
            public double time => throw new NotImplementedException();
            public float duration => throw new NotImplementedException();
            public TValue ReadValue<TValue>() where TValue : struct => throw new NotImplementedException();
            public object ReadValueAsObject() => throw new NotImplementedException();
            public override string ToString() => throw new NotImplementedException();
        }
    }

    public enum InputActionType { Value = 0, Button = 1, PassThrough = 2 }

    public class InputActionMap : IDisposable, IEnumerable<InputAction>
    {
        public InputActionMap() { }
        public InputActionMap(string name) { }
        public string name { get; set; }
        public string id => throw new NotImplementedException();
        public bool enabled { get; }
        public InputActionAsset asset => throw new NotImplementedException();
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputAction> actions => throw new NotImplementedException();
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputBinding> bindings => throw new NotImplementedException();
        public event Action<InputAction.CallbackContext> actionTriggered;
        public InputAction FindAction(string nameOrId, bool throwIfNotFound = false) => throw new NotImplementedException();
        public InputAction FindAction(Guid id) => throw new NotImplementedException();
        public void Enable() => throw new NotImplementedException();
        public void Disable() => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public bool IsEnabled() => throw new NotImplementedException();
        public IEnumerator<InputAction> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
        public IEnumerable<InputBinding> EnumerateBindings() => throw new NotImplementedException();
    }

    public struct InputBinding
    {
        public string path { get; set; }
        public string overridePath { get; set; }
        public string id { get; set; }
        public string name { get; set; }
        public string groups { get; set; }
        public string action { get; set; }
        public string interactions { get; set; }
        public string processors { get; set; }
        public bool isComposite { get; set; }
        public bool isPartOfComposite { get; set; }
        public string effectivePath => throw new NotImplementedException();
        public bool hasOverrides => throw new NotImplementedException();
    }

    public class InputActionAsset : UnityEngine.ScriptableObject, IDisposable, IEnumerable<InputActionMap>
    {
        public string name { get; set; }
        public bool enabled { get; }
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputActionMap> actionMaps => throw new NotImplementedException();
        public IEnumerable<InputControlScheme> controlSchemes => throw new NotImplementedException();
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputBinding> bindings => throw new NotImplementedException();
        public static InputActionAsset FromJson(string json) => throw new NotImplementedException();
        public string ToJson() => throw new NotImplementedException();
        public void LoadFromJson(string json) => throw new NotImplementedException();
        public InputActionMap FindActionMap(string nameOrId, bool throwIfNotFound = false) => throw new NotImplementedException();
        public InputActionMap FindActionMap(Guid id) => throw new NotImplementedException();
        public InputAction FindAction(string nameOrId, bool throwIfNotFound = false) => throw new NotImplementedException();
        public InputAction FindAction(Guid guid) => throw new NotImplementedException();
        public void Enable() => throw new NotImplementedException();
        public void Disable() => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
        public bool Contains(InputAction action) => throw new NotImplementedException();
        public IEnumerator<InputActionMap> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }

    public struct InputControlScheme
    {
        public string name => throw new NotImplementedException();
        public UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputControlScheme.DeviceRequirement> deviceRequirements => throw new NotImplementedException();
        public struct DeviceRequirement
        {
            public string controlPath => throw new NotImplementedException();
            public bool isOptional => throw new NotImplementedException();
        }
    }

    public class InputActionReference : UnityEngine.ScriptableObject
    {
        public InputAction action => throw new NotImplementedException();
        public InputActionAsset asset => throw new NotImplementedException();
        public static InputActionReference Create(InputAction action) => throw new NotImplementedException();
        public override string ToString() => throw new NotImplementedException();
    }

    public class PlayerInput : UnityEngine.MonoBehaviour
    {
        public InputActionAsset actions { get; set; }
        public string currentControlScheme => throw new NotImplementedException();
        public string defaultControlScheme { get; set; }
        public PlayerInput.NotificationBehavior notificationBehavior { get; set; }
        public InputActionMap currentActionMap { get; set; }
        public UnityEngine.Camera camera { get; set; }
        public UnityEngine.GameObject uiInputModuleGameObjectOverride { get; set; }
        public bool active => throw new NotImplementedException();
        public void SwitchCurrentActionMap(string mapNameOrId) => throw new NotImplementedException();
        public void SwitchCurrentControlScheme(string schemeName, params InputDevice[] devices) => throw new NotImplementedException();
        public void ActivateInput() => throw new NotImplementedException();
        public void DeactivateInput() => throw new NotImplementedException();
        public InputAction FindAction(string actionNameOrId, bool throwIfNotFound = false) => throw new NotImplementedException();
        public enum NotificationBehavior { SendMessages = 0, BroadcastMessages = 1, InvokeUnityEvents = 2, InvokeCSharpEvents = 3 }
    }

    public static class InputSystem
    {
        public static UnityEngine.InputSystem.Utilities.ReadOnlyArray<InputDevice> devices => throw new NotImplementedException();
        public static InputDevice[] disconnectedDevices => throw new NotImplementedException();
        public static InputSettings settings { get; set; }
        public static float pollingFrequency { get; set; }
        public static bool runInBackground { get; set; }
        public static event Action<InputDevice, InputDeviceChange> onDeviceChange;
        public static event Action<InputDevice, InputDeviceChange> onDeviceCommand;
        public static event Action onBeforeUpdate;
        public static event Action onAfterUpdate;
        public static void AddDevice(InputDevice device) => throw new NotImplementedException();
        public static void RemoveDevice(InputDevice device) => throw new NotImplementedException();
        public static InputDevice GetDeviceById(int deviceId) => throw new NotImplementedException();
        public static InputDevice GetDevice(string nameOrLayout) => throw new NotImplementedException();
        public static void EnableDevice(InputDevice device) => throw new NotImplementedException();
        public static void DisableDevice(InputDevice device) => throw new NotImplementedException();
        public static void Update() => throw new NotImplementedException();
        public static void PauseHaptics() => throw new NotImplementedException();
        public static void ResumeHaptics() => throw new NotImplementedException();
        public static void ResetHaptics() => throw new NotImplementedException();
    }

    public class InputSettings : UnityEngine.ScriptableObject
    {
        public float defaultDeadzoneMin { get; set; }
        public float defaultDeadzoneMax { get; set; }
        public bool editorInputBehaviorInPlayMode { get; set; }
    }
}

namespace UnityEngine.InputSystem.UI
{
    public class InputSystemUIInputModule : UnityEngine.EventSystems.BaseInputModule
    {
        public UnityEngine.InputSystem.InputActionReference point { get; set; }
        public UnityEngine.InputSystem.InputActionReference move { get; set; }
        public UnityEngine.InputSystem.InputActionReference submit { get; set; }
        public UnityEngine.InputSystem.InputActionReference cancel { get; set; }
        public UnityEngine.InputSystem.InputActionReference leftClick { get; set; }
        public UnityEngine.InputSystem.InputActionReference middleClick { get; set; }
        public UnityEngine.InputSystem.InputActionReference rightClick { get; set; }
        public UnityEngine.InputSystem.InputActionReference scrollWheel { get; set; }
        public UnityEngine.InputSystem.InputActionAsset actionsAsset { get; set; }
        public float moveRepeatDelay { get; set; }
        public float moveRepeatRate { get; set; }
        public bool deselectOnBackgroundClick { get; set; }
        public UnityEngine.InputSystem.InputActionMap actions { get; set; }
        public void AssignDefaultActions() => throw new NotImplementedException();
    }

    public class TrackedDeviceRaycaster : UnityEngine.MonoBehaviour { }
}

namespace UnityEngine.InputSystem.Utilities
{
    public struct ReadOnlyArray<TValue> : System.Collections.Generic.IReadOnlyList<TValue>
    {
        public int Count => throw new NotImplementedException();
        public TValue this[int index] => throw new NotImplementedException();
        public System.Collections.Generic.IEnumerator<TValue> GetEnumerator() => throw new NotImplementedException();
        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => throw new NotImplementedException();
    }
}
