// THROWAWAY hand-written stubs (Batch C stubs + the surface the Batch D map views use): just enough
// UnityEngine/InputSystem/UI/EventSystems surface (and signatures copied from the project's
// Unity-dependent classes) to type-check the campaign + map glue against the REAL compiled
// Wayfinder assemblies. Not Unity. Not evidence. A pass says nothing about real Unity API drift.
using System;
using System.Collections.Generic;
namespace UnityEngine
{
    public class Object { public string name; public static void Destroy(Object o) { } public static void DontDestroyOnLoad(Object o) { } public static T FindFirstObjectByType<T>() where T : Object => null; }
    public class Component : Object { public GameObject gameObject; public Transform transform; public T GetComponent<T>() => default; public T GetComponentInChildren<T>() => default; public T[] GetComponentsInChildren<T>() => null; }
    public class Behaviour : Component { public bool enabled; }
    public class MonoBehaviour : Behaviour { }
    public sealed class GameObject : Object { public GameObject(string n) { } public GameObject(string n, params Type[] c) { } public T AddComponent<T>() where T : Component => default; public T GetComponent<T>() => default; public void SetActive(bool v) { } public bool activeSelf; public Transform transform; }
    public class Transform : Component { public Vector3 position; public Quaternion rotation; public Vector3 eulerAngles; public Vector3 localEulerAngles; public void SetPositionAndRotation(Vector3 p, Quaternion r) { } public void SetParent(Transform p, bool w) { } public void SetAsLastSibling() { } }
    public sealed class RectTransform : Transform { public Vector2 anchorMin, anchorMax, offsetMin, offsetMax, pivot, sizeDelta, anchoredPosition; public Rect rect; }
    public struct Rect { public Rect(float x, float y, float w, float h) { size = new Vector2(w, h); } public Vector2 size; public float width => size.x; public float height => size.y; }
    public class Camera : Behaviour { }
    public static class RectTransformUtility { public static bool ScreenPointToLocalPointInRectangle(RectTransform r, Vector2 screen, Camera cam, out Vector2 local) { local = default; return true; } }
    public enum TextureFormat { RGBA32 }
    public enum FilterMode { Point, Bilinear }
    public enum TextureWrapMode { Repeat, Clamp }
    public class Texture : Object { public FilterMode filterMode; public TextureWrapMode wrapMode; }
    public sealed class Texture2D : Texture { public Texture2D(int w, int h, TextureFormat f, bool mips) { } public Texture2D(int w, int h, TextureFormat f, bool mips, bool linear) { } public void LoadRawTextureData(byte[] data) { } public void Apply(bool updateMipmaps, bool makeNoLongerReadable) { } public void SetPixels32(Color32[] c) { } }
    public struct Color32 { public Color32(byte r, byte g, byte b, byte a) { } }
    public sealed class Sprite : Object { public static Sprite Create(Texture2D t, Rect r, Vector2 pivot, float ppu) => null; }
    public static class ColorUtility { public static string ToHtmlStringRGB(Color c) => ""; }
    public struct Vector2 { public float x, y; public Vector2(float x, float y) { this.x = x; this.y = y; } public static Vector2 zero => default; public static Vector2 one => new Vector2(1, 1); public float sqrMagnitude => x * x + y * y; public static float Distance(Vector2 a, Vector2 b) => 0f; }
    public struct Vector3 { public float x, y, z; public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; } public static Vector3 zero => default; }
    public struct Quaternion { public static Quaternion Euler(float x, float y, float z) => default; }
    public struct Pose { public Vector3 position; public Quaternion rotation; } // real Unity type: proves the alias is needed
    public struct Color { public Color(float r, float g, float b, float a = 1f) { } public static Color yellow => default; public static Color white => default; }
    public static class Mathf { public static float DeltaAngle(float a, float b) => 0f; public static float Max(float a, float b) => a; public static float Min(float a, float b) => a; public static int Min(int a, int b) => a; public const float Rad2Deg = 57.29578f; public static float Atan2(float y, float x) => 0f; public static float Sqrt(float f) => f; public static float Abs(float f) => f; public static float Clamp(float v, float a, float b) => v; public static float Clamp01(float v) => v; public static float Lerp(float a, float b, float t) => a; public static float InverseLerp(float a, float b, float v) => 0f; public static float Sin(float f) => f; public static int RoundToInt(float f) => 0; public static int FloorToInt(float f) => 0; }
    public static class Time { public static float deltaTime, unscaledDeltaTime, unscaledTime, timeScale, fixedDeltaTime; public static int frameCount; }
    public static class Debug { public static void Log(object m) { } public static void LogError(object m) { } public static void LogWarning(object m) { } }
    public static class Application { public static string persistentDataPath = "", temporaryCachePath = ""; public static bool isMobilePlatform; }
    public static class Resources { public static T Load<T>(string path) where T : Object => default; }
    public class TextAsset : Object { public string text; }
    public class Collider : Component { public bool isTrigger; }
    public sealed class Rigidbody : Component { public Vector3 position, linearVelocity, angularVelocity; public Quaternion rotation; }
    public struct RaycastHit { public Collider collider; }
    public enum QueryTriggerInteraction { UseGlobal, Ignore, Collide }
    public static class Physics { public const int DefaultRaycastLayers = -5; public static bool Linecast(Vector3 a, Vector3 b, out RaycastHit hit, int mask, QueryTriggerInteraction q) { hit = default; return false; } }
    public enum TextAnchor { UpperLeft, MiddleLeft, MiddleCenter }
}
namespace UnityEngine.Events { public class UnityEvent { public void AddListener(Action a) { } } }
namespace UnityEngine.UI
{
    public class Graphic : MonoBehaviour { public Color color; public bool raycastTarget; public RectTransform rectTransform => null; }
    public class Text : Graphic { public string text; public TextAnchor alignment; public int fontSize; }
    public class Image : Graphic { public Sprite sprite; }
    public class RawImage : Graphic { public Texture texture; public Rect uvRect; }
    public class RectMask2D : MonoBehaviour { }
    public struct Navigation { public Mode mode; public enum Mode { None, Automatic } }
    public class Button : MonoBehaviour { public UnityEngine.Events.UnityEvent onClick = new UnityEngine.Events.UnityEvent(); public bool interactable; public Navigation navigation; }
}
namespace UnityEngine.EventSystems
{
    public class PointerEventData { public int pointerId; public UnityEngine.Vector2 position, scrollDelta; public UnityEngine.Camera pressEventCamera; }
    public interface IPointerDownHandler { void OnPointerDown(PointerEventData e); }
    public interface IPointerUpHandler { void OnPointerUp(PointerEventData e); }
    public interface IPointerClickHandler { void OnPointerClick(PointerEventData e); }
    public interface IDragHandler { void OnDrag(PointerEventData e); }
    public interface IScrollHandler { void OnScroll(PointerEventData e); }
}
namespace UnityEngine.SceneManagement { public static class SceneManager { public static void LoadScene(string n) { } } }
namespace UnityEngine.InputSystem
{
    public class ButtonControl { public bool wasPressedThisFrame; public bool isPressed; }
    public class Keyboard { public static Keyboard current; public ButtonControl escapeKey, mKey, cKey, equalsKey, minusKey, numpadPlusKey, numpadMinusKey, leftArrowKey, rightArrowKey, upArrowKey, downArrowKey; }
    public class Gamepad { public static Gamepad current; public ButtonControl startButton, selectButton, buttonEast; }
}
// ---- Signatures copied from the project's Unity-dependent classes (as of this batch) ----
namespace Wayfinder.Simulation
{
    public sealed class VehicleController : UnityEngine.MonoBehaviour
    {
        public VehicleSpec spec;
        public float WaterDepthMeters { get; private set; }
        public float FuelLiters => 0f;
        public float SpeedKph => 0f;
        public DamageModel Damage => null;
        public Wayfinder.Core.VehicleState State { get; private set; }
        public void SetDriveCommand(float steer, float throttle, float brake, bool handbrake, bool crawl) { }
        public void ZeroInputsAndHold() { }
        public void SetCargoMass(float kg) { }
        public void SetFuel(float liters) { }
        public VehicleStatsResolver StatsResolver => null;
        public float FuelCapacityLiters => 0f;
        public void RestartAfterWaterRecovery() { }
    }
    public sealed class ChaseCameraBehaviour : UnityEngine.MonoBehaviour { public UnityEngine.Transform target; public void SetOrbitInput(float yawDelta, float pitchDelta) { } }
}
namespace Wayfinder.Platform
{
    public sealed class InputRouter : UnityEngine.MonoBehaviour
    {
        public struct DriveCommand { public float Steer, Throttle, Brake; public bool Handbrake, Crawl, ReverseIndicated; }
        public DriveCommand Current { get; private set; }
        public bool InteractHeld { get; private set; }
        public UnityEngine.Vector2 CameraLookDelta { get; private set; }
        public bool MapPressed { get; private set; }
        public void SuppressInputs(bool suppressed) { }
        public void SetTouchPedal(float throttle01, float brake01) { }
        public void SetTouchSteer(float steerMinus1To1) { }
    }
}
namespace Wayfinder.Presentation
{
    public static class PrototypeUi
    {
        public static UnityEngine.RectTransform CreateRoot(string name) => null;
        public static UnityEngine.RectTransform Rect(string name, UnityEngine.Transform parent, UnityEngine.Vector2 min, UnityEngine.Vector2 max) => null;
        public static UnityEngine.UI.Text Label(UnityEngine.Transform parent, string value, UnityEngine.Vector2 min, UnityEngine.Vector2 max, int size = 24) => null;
        public static UnityEngine.UI.Button Button(UnityEngine.Transform parent, string label, UnityEngine.Vector2 min, UnityEngine.Vector2 max) => null;
    }
    public sealed class TouchHoldButton : UnityEngine.MonoBehaviour { public event Action<bool> Changed; public void Release() { Changed?.Invoke(false); } }
}
