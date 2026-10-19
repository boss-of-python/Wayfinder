// ---------------------------------------------------------------------------------------------
// UnityEngine.CoreModule (STUB)
//
// Hand-written, signature-faithful stubs of the Unity APIs this repository actually uses, so the
// four Wayfinder assemblies that no Unity editor has ever compiled (Wayfinder.Platform,
// Wayfinder.Presentation, Wayfinder.Editor and the Editor/PlayMode test assemblies — KI-034) can
// be *type-checked* by the pinned Roslyn 3.9 toolchain in Tools/Build/headless_cs.py.
//
// WHAT THIS IS FOR
//   Catching C# compile errors (CS0xxx) before a 6-minute cloud build burns on them. A compile
//   error in an Editor assembly aborts Unity batchmode before -executeMethod runs, which is why
//   five consecutive dispatches died with nothing but "Build failed with exit code 1".
//
// WHAT THIS IS NOT
//   NOT the real Unity API. Nothing here executes: every member is a stub that throws
//   NotImplementedException at runtime. A member that exists here with the wrong signature is a
//   false negative, and a member missing here is a false positive. Treat a clean run as
//   "no compile error we can see", never as "Unity will compile it".
//   Where a signature was uncertain, it is marked `// verify:` — check it against the editor.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections;
using System.Collections.Generic;

namespace UnityEngine
{
    public class Object
    {
        public string name { get; set; }
        public int GetInstanceID() => throw new NotImplementedException();
        public override string ToString() => name;
        public static void Destroy(Object obj) => throw new NotImplementedException();
        public static void Destroy(Object obj, float delay) => throw new NotImplementedException();
        public static void DestroyImmediate(Object obj) => throw new NotImplementedException();
        public static void DestroyImmediate(Object obj, bool allowDestroyingAssets) => throw new NotImplementedException();
        public static void DontDestroyOnLoad(Object target) => throw new NotImplementedException();
        public static T Instantiate<T>(T original) where T : Object => throw new NotImplementedException();
        public static T Instantiate<T>(T original, Transform parent) where T : Object => throw new NotImplementedException();
        public static T Instantiate<T>(T original, Vector3 position, Quaternion rotation) where T : Object => throw new NotImplementedException();
        public static T FindFirstObjectByType<T>() where T : Component => throw new NotImplementedException();
        public static T[] FindObjectsByType<T>(FindObjectsSortMode sortMode) where T : Component => throw new NotImplementedException();
        public static bool operator ==(Object a, Object b) => ReferenceEquals(a, b);
        public static bool operator !=(Object a, Object b) => !ReferenceEquals(a, b);
        public override bool Equals(object other) => ReferenceEquals(this, other);
        public override int GetHashCode() => 0;
        public static implicit operator bool(Object exists) => !ReferenceEquals(exists, null);
    }

    public enum FindObjectsSortMode { None = 0, InstanceID = 1 }

    public class ScriptableObject : Object
    {
        public static T CreateInstance<T>() where T : ScriptableObject => throw new NotImplementedException();
        public static ScriptableObject CreateInstance(Type type) => throw new NotImplementedException();
    }

    public class Component : Object
    {
        public Transform transform => throw new NotImplementedException();
        public GameObject gameObject => throw new NotImplementedException();
        public string tag { get; set; }
        public T GetComponent<T>() => throw new NotImplementedException();
        public Component GetComponent(Type type) => throw new NotImplementedException();
        public Component GetComponent(string type) => throw new NotImplementedException();
        public T GetComponentInChildren<T>() => throw new NotImplementedException();
        public T GetComponentInChildren<T>(bool includeInactive) => throw new NotImplementedException();
        public T GetComponentInParent<T>() => throw new NotImplementedException();
        public T[] GetComponentsInChildren<T>() => throw new NotImplementedException();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => throw new NotImplementedException();
        public T[] GetComponentsInParent<T>() => throw new NotImplementedException();
        public bool TryGetComponent<T>(out T component) => throw new NotImplementedException();
    }

    public class Behaviour : Component
    {
        public bool enabled { get; set; }
        public bool isActiveAndEnabled => throw new NotImplementedException();
    }

    public class MonoBehaviour : Behaviour
    {
        public Coroutine StartCoroutine(IEnumerator routine) => throw new NotImplementedException();
        public void StopCoroutine(IEnumerator routine) => throw new NotImplementedException();
        public void StopAllCoroutines() => throw new NotImplementedException();
        public void Invoke(string methodName, float time) => throw new NotImplementedException();
        public void CancelInvoke() => throw new NotImplementedException();
        public void print(object message) => throw new NotImplementedException();
    }

    public class Coroutine { }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public class RequireComponent : Attribute
    {
        public RequireComponent(Type requiredComponent) { }
        public RequireComponent(Type requiredComponent, Type requiredComponent2) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SerializeField : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public class HideInInspector : Attribute { }

    [AttributeUsage(AttributeTargets.Field)]
    public class HeaderAttribute : Attribute
    {
        public HeaderAttribute(string header) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class TooltipAttribute : Attribute
    {
        public TooltipAttribute(string tooltip) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class RangeAttribute : Attribute
    {
        public RangeAttribute(float min, float max) { }
    }

    [AttributeUsage(AttributeTargets.Field)]
    public class SpaceAttribute : Attribute
    {
        public SpaceAttribute() { }
        public SpaceAttribute(float height) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class ExecuteAlways : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { }
    }

    public class Transform : Component, IEnumerable
    {
        public Vector3 position { get; set; }
        public Vector3 localPosition { get; set; }
        public Quaternion rotation { get; set; }
        public Quaternion localRotation { get; set; }
        public Vector3 localScale { get; set; }
        public Vector3 eulerAngles { get; set; }
        public Vector3 localEulerAngles { get; set; }
        public Vector3 forward { get; set; }
        public Vector3 right { get; set; }
        public Vector3 up { get; set; }
        public Transform parent { get; set; }
        public Transform root => throw new NotImplementedException();
        public int childCount => throw new NotImplementedException();
        public Matrix4x4 localToWorldMatrix => throw new NotImplementedException();
        public Matrix4x4 worldToLocalMatrix => throw new NotImplementedException();
        public void SetParent(Transform p) => throw new NotImplementedException();
        public void SetParent(Transform p, bool worldPositionStays) => throw new NotImplementedException();
        public void SetAsLastSibling() => throw new NotImplementedException();
        public void SetAsFirstSibling() => throw new NotImplementedException();
        public void SetSiblingIndex(int index) => throw new NotImplementedException();
        public int GetSiblingIndex() => throw new NotImplementedException();
        public Transform Find(string n) => throw new NotImplementedException();
        public Transform GetChild(int index) => throw new NotImplementedException();
        public void Translate(Vector3 translation) => throw new NotImplementedException();
        public void Rotate(Vector3 eulers) => throw new NotImplementedException();
        public void Rotate(float xAngle, float yAngle, float zAngle) => throw new NotImplementedException();
        public void LookAt(Transform target) => throw new NotImplementedException();
        public void LookAt(Vector3 worldPosition) => throw new NotImplementedException();
        public Vector3 TransformPoint(Vector3 p) => throw new NotImplementedException();
        public Vector3 TransformDirection(Vector3 d) => throw new NotImplementedException();
        public Vector3 InverseTransformPoint(Vector3 p) => throw new NotImplementedException();
        public Vector3 InverseTransformDirection(Vector3 d) => throw new NotImplementedException();
        public void SetPositionAndRotation(Vector3 p, Quaternion q) => throw new NotImplementedException();
        public void DetachChildren() => throw new NotImplementedException();
        public IEnumerator GetEnumerator() => throw new NotImplementedException();
    }

    public struct Matrix4x4
    {
        public Vector3 MultiplyPoint(Vector3 v) => throw new NotImplementedException();
        public Vector3 MultiplyVector(Vector3 v) => throw new NotImplementedException();
        public static Matrix4x4 identity => throw new NotImplementedException();
    }

    public class GameObject : Object
    {
        public GameObject() { }
        public GameObject(string name) { }
        public GameObject(string name, params Type[] components) { }
        public Transform transform => throw new NotImplementedException();
        public int layer { get; set; }
        public string tag { get; set; }
        public bool activeSelf => throw new NotImplementedException();
        public bool activeInHierarchy => throw new NotImplementedException();
        public SceneManagement.Scene scene => throw new NotImplementedException();
        public T AddComponent<T>() where T : Component => throw new NotImplementedException();
        public Component AddComponent(Type componentType) => throw new NotImplementedException();
        public T GetComponent<T>() => throw new NotImplementedException();
        public Component GetComponent(Type type) => throw new NotImplementedException();
        public Component GetComponent(string type) => throw new NotImplementedException();
        public T GetComponentInChildren<T>() => throw new NotImplementedException();
        public T GetComponentInChildren<T>(bool includeInactive) => throw new NotImplementedException();
        public T[] GetComponentsInChildren<T>() => throw new NotImplementedException();
        public T[] GetComponentsInChildren<T>(bool includeInactive) => throw new NotImplementedException();
        public bool TryGetComponent<T>(out T component) => throw new NotImplementedException();
        public void SetActive(bool value) => throw new NotImplementedException();
        public static GameObject CreatePrimitive(PrimitiveType type) => throw new NotImplementedException();
        public void SendMessage(string methodName) => throw new NotImplementedException();
        public void SendMessage(string methodName, object value) => throw new NotImplementedException();
        public void SendMessage(string methodName, object value, SendMessageOptions options) => throw new NotImplementedException();
        public void SendMessage(string methodName, SendMessageOptions options) => throw new NotImplementedException();
        public void BroadcastMessage(string methodName) => throw new NotImplementedException();
        public void BroadcastMessage(string methodName, object parameter, SendMessageOptions options) => throw new NotImplementedException();
        public void SendMessageUpwards(string methodName) => throw new NotImplementedException();
        public void SendMessageUpwards(string methodName, SendMessageOptions options) => throw new NotImplementedException();
        public static GameObject Find(string name) => throw new NotImplementedException();
        public static GameObject FindGameObjectWithTag(string tag) => throw new NotImplementedException();
        public static void Destroy(Object obj) => throw new NotImplementedException();
    }

    public enum SendMessageOptions { RequireReceiver = 0, DontRequireReceiver = 1 }

    public enum PrimitiveType { Sphere = 0, Capsule = 1, Cylinder = 2, Cube = 3, Plane = 4, Quad = 5 }

    public struct Vector2
    {
        public const float kEpsilon = 1.401298E-45f;
        public float x;
        public float y;
        public Vector2(float x, float y) { this.x = x; this.y = y; }
        public float this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public static Vector2 zero => new Vector2(0f, 0f);
        public static Vector2 one => new Vector2(1f, 1f);
        public static Vector2 up => new Vector2(0f, 1f);
        public static Vector2 down => new Vector2(0f, -1f);
        public static Vector2 left => new Vector2(-1f, 0f);
        public static Vector2 right => new Vector2(1f, 0f);
        public float magnitude => throw new NotImplementedException();
        public float sqrMagnitude => throw new NotImplementedException();
        public Vector2 normalized => throw new NotImplementedException();
        public void Set(float newX, float newY) => throw new NotImplementedException();
        public static float Distance(Vector2 a, Vector2 b) => throw new NotImplementedException();
        public static float Dot(Vector2 a, Vector2 b) => throw new NotImplementedException();
        public static Vector2 Lerp(Vector2 a, Vector2 b, float t) => throw new NotImplementedException();
        public static Vector2 LerpUnclamped(Vector2 a, Vector2 b, float t) => throw new NotImplementedException();
        public static Vector2 Max(Vector2 a, Vector2 b) => throw new NotImplementedException();
        public static Vector2 Min(Vector2 a, Vector2 b) => throw new NotImplementedException();
        public static Vector2 Scale(Vector2 a, Vector2 b) => throw new NotImplementedException();
        public static Vector2 operator +(Vector2 a, Vector2 b) => new Vector2(a.x + b.x, a.y + b.y);
        public static Vector2 operator -(Vector2 a, Vector2 b) => new Vector2(a.x - b.x, a.y - b.y);
        public static Vector2 operator -(Vector2 a) => new Vector2(-a.x, -a.y);
        public static Vector2 operator *(Vector2 a, float d) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator *(float d, Vector2 a) => new Vector2(a.x * d, a.y * d);
        public static Vector2 operator /(Vector2 a, float d) => new Vector2(a.x / d, a.y / d);
        public static bool operator ==(Vector2 a, Vector2 b) => a.x == b.x && a.y == b.y;
        public static bool operator !=(Vector2 a, Vector2 b) => !(a == b);
        public static implicit operator Vector2(Vector3 v) => new Vector2(v.x, v.y);
        public static implicit operator Vector3(Vector2 v) => new Vector3(v.x, v.y, 0f);
        public override bool Equals(object other) => throw new NotImplementedException();
        public override int GetHashCode() => throw new NotImplementedException();
        public override string ToString() => $"({x}, {y})";
    }

    public struct Vector3
    {
        public const float kEpsilon = 1.401298E-45f;
        public float x;
        public float y;
        public float z;
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0f; }
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public float this[int index] { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public static Vector3 zero => new Vector3(0f, 0f, 0f);
        public static Vector3 one => new Vector3(1f, 1f, 1f);
        public static Vector3 up => new Vector3(0f, 1f, 0f);
        public static Vector3 down => new Vector3(0f, -1f, 0f);
        public static Vector3 left => new Vector3(-1f, 0f, 0f);
        public static Vector3 right => new Vector3(1f, 0f, 0f);
        public static Vector3 forward => new Vector3(0f, 0f, 1f);
        public static Vector3 back => new Vector3(0f, 0f, -1f);
        public static Vector3 positiveInfinity => throw new NotImplementedException();
        public static Vector3 negativeInfinity => throw new NotImplementedException();
        public float magnitude => throw new NotImplementedException();
        public float sqrMagnitude => throw new NotImplementedException();
        public Vector3 normalized => throw new NotImplementedException();
        public void Set(float newX, float newY, float newZ) => throw new NotImplementedException();
        public static float Distance(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static float Dot(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 Cross(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) => throw new NotImplementedException();
        public static Vector3 LerpUnclamped(Vector3 a, Vector3 b, float t) => throw new NotImplementedException();
        public static Vector3 Slerp(Vector3 a, Vector3 b, float t) => throw new NotImplementedException();
        public static Vector3 MoveTowards(Vector3 current, Vector3 target, float maxDistanceDelta) => throw new NotImplementedException();
        public static Vector3 Max(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 Min(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 Scale(Vector3 a, Vector3 b) => throw new NotImplementedException();
        public static Vector3 Project(Vector3 vector, Vector3 onNormal) => throw new NotImplementedException();
        public static Vector3 ProjectOnPlane(Vector3 vector, Vector3 planeNormal) => throw new NotImplementedException();
        public static Vector3 Reflect(Vector3 inDirection, Vector3 inNormal) => throw new NotImplementedException();
        public static float Angle(Vector3 from, Vector3 to) => throw new NotImplementedException();
        public static float SignedAngle(Vector3 from, Vector3 to, Vector3 axis) => throw new NotImplementedException();
        public static Vector3 Normalize(Vector3 value) => throw new NotImplementedException();
        // Real signature: (current, target, ref currentVelocity, smoothTime, maxSpeed = +Infinity,
        // deltaTime = Time.deltaTime). The last default is a runtime value, so the stub uses a
        // compile-time stand-in; every caller in this repo passes deltaTime explicitly.
        public static Vector3 SmoothDamp(Vector3 current, Vector3 target, ref Vector3 currentVelocity, float smoothTime, float maxSpeed = float.PositiveInfinity, float deltaTime = 0.02f) => throw new NotImplementedException();
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => a.x == b.x && a.y == b.y && a.z == b.z;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object other) => throw new NotImplementedException();
        public override int GetHashCode() => throw new NotImplementedException();
        public override string ToString() => $"({x}, {y}, {z})";
    }

    public struct Vector4
    {
        public float x, y, z, w;
        public Vector4(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Vector4 zero => new Vector4(0f, 0f, 0f, 0f);
        public static Vector4 one => new Vector4(1f, 1f, 1f, 1f);
    }

    public struct Quaternion
    {
        public const float kEpsilon = 1E-06f;
        public float x, y, z, w;
        public Quaternion(float x, float y, float z, float w) { this.x = x; this.y = y; this.z = z; this.w = w; }
        public static Quaternion identity => new Quaternion(0f, 0f, 0f, 1f);
        public Vector3 eulerAngles { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public Quaternion normalized => throw new NotImplementedException();
        public void Set(float newX, float newY, float newZ, float newW) => throw new NotImplementedException();
        public void SetFromToRotation(Vector3 fromDirection, Vector3 toDirection) => throw new NotImplementedException();
        public void SetLookRotation(Vector3 view) => throw new NotImplementedException();
        public void SetLookRotation(Vector3 view, Vector3 up) => throw new NotImplementedException();
        public void ToAngleAxis(out float angle, out Vector3 axis) => throw new NotImplementedException();
        public static Quaternion Euler(float x, float y, float z) => throw new NotImplementedException();
        public static Quaternion Euler(Vector3 euler) => throw new NotImplementedException();
        public static Quaternion AngleAxis(float angle, Vector3 axis) => throw new NotImplementedException();
        public static Quaternion LookRotation(Vector3 forward) => throw new NotImplementedException();
        public static Quaternion LookRotation(Vector3 forward, Vector3 upwards) => throw new NotImplementedException();
        public static Quaternion FromToRotation(Vector3 fromDirection, Vector3 toDirection) => throw new NotImplementedException();
        public static Quaternion Slerp(Quaternion a, Quaternion b, float t) => throw new NotImplementedException();
        public static Quaternion SlerpUnclamped(Quaternion a, Quaternion b, float t) => throw new NotImplementedException();
        public static Quaternion Lerp(Quaternion a, Quaternion b, float t) => throw new NotImplementedException();
        public static Quaternion LerpUnclamped(Quaternion a, Quaternion b, float t) => throw new NotImplementedException();
        public static Quaternion RotateTowards(Quaternion from, Quaternion to, float maxDegreesDelta) => throw new NotImplementedException();
        public static Quaternion Inverse(Quaternion rotation) => throw new NotImplementedException();
        public static float Angle(Quaternion a, Quaternion b) => throw new NotImplementedException();
        public static float Dot(Quaternion a, Quaternion b) => throw new NotImplementedException();
        public static Vector3 operator *(Quaternion rotation, Vector3 point) => throw new NotImplementedException();
        public static Quaternion operator *(Quaternion lhs, Quaternion rhs) => throw new NotImplementedException();
        public static bool operator ==(Quaternion lhs, Quaternion rhs) => throw new NotImplementedException();
        public static bool operator !=(Quaternion lhs, Quaternion rhs) => throw new NotImplementedException();
        public override bool Equals(object other) => throw new NotImplementedException();
        public override int GetHashCode() => throw new NotImplementedException();
    }

    public struct Color
    {
        public float r, g, b, a;
        public Color(float r, float g, float b) { this.r = r; this.g = g; this.b = b; this.a = 1f; }
        public Color(float r, float g, float b, float a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static Color white => new Color(1f, 1f, 1f, 1f);
        public static Color black => new Color(0f, 0f, 0f, 1f);
        public static Color clear => new Color(0f, 0f, 0f, 0f);
        public static Color red => new Color(1f, 0f, 0f, 1f);
        public static Color green => new Color(0f, 1f, 0f, 1f);
        public static Color blue => new Color(0f, 0f, 1f, 1f);
        public static Color yellow => new Color(1f, 0.9215686f, 0.01568628f, 1f);
        public static Color cyan => new Color(0f, 1f, 1f, 1f);
        public static Color magenta => new Color(1f, 0f, 1f, 1f);
        public static Color gray => new Color(0.5f, 0.5f, 0.5f, 1f);
        public static Color grey => new Color(0.5f, 0.5f, 0.5f, 1f);
        public float grayscale => throw new NotImplementedException();
        public Color gamma => throw new NotImplementedException();
        public Color linear => throw new NotImplementedException();
        public static Color Lerp(Color a, Color b, float t) => throw new NotImplementedException();
        public static Color LerpUnclamped(Color a, Color b, float t) => throw new NotImplementedException();
        public override string ToString() => $"RGBA({r}, {g}, {b}, {a})";
        public static Color operator +(Color a, Color b) => new Color(a.r + b.r, a.g + b.g, a.b + b.b, a.a + b.a);
        public static Color operator -(Color a, Color b) => new Color(a.r - b.r, a.g - b.g, a.b - b.b, a.a - b.a);
        public static Color operator *(Color a, Color b) => new Color(a.r * b.r, a.g * b.g, a.b * b.b, a.a * b.a);
        public static Color operator *(Color a, float b) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator *(float b, Color a) => new Color(a.r * b, a.g * b, a.b * b, a.a * b);
        public static Color operator /(Color a, float b) => new Color(a.r / b, a.g / b, a.b / b, a.a / b);
        public static bool operator ==(Color a, Color b) => a.r == b.r && a.g == b.g && a.b == b.b && a.a == b.a;
        public static bool operator !=(Color a, Color b) => !(a == b);
        public static implicit operator Vector4(Color c) => new Vector4(c.r, c.g, c.b, c.a);
        public static implicit operator Color(Vector4 v) => new Color(v.x, v.y, v.z, v.w);
        public override bool Equals(object other) => throw new NotImplementedException();
        public override int GetHashCode() => throw new NotImplementedException();
    }

    public struct Color32
    {
        public byte r, g, b, a;
        public Color32(byte r, byte g, byte b, byte a) { this.r = r; this.g = g; this.b = b; this.a = a; }
        public static implicit operator Color(Color32 c) => new Color(c.r / 255f, c.g / 255f, c.b / 255f, c.a / 255f);
        public static implicit operator Color32(Color c) => throw new NotImplementedException();
    }

    public struct Rect
    {
        public float x, y, width, height;
        public Rect(float x, float y, float width, float height) { this.x = x; this.y = y; this.width = width; this.height = height; }
        public Rect(Vector2 position, Vector2 size) { x = position.x; y = position.y; width = size.x; height = size.y; }
        public static Rect zero => new Rect(0f, 0f, 0f, 0f);
        public Vector2 position { get => new Vector2(x, y); set { x = value.x; y = value.y; } }
        public Vector2 size { get => new Vector2(width, height); set { width = value.x; height = value.y; } }
        public Vector2 center => new Vector2(x + width / 2f, y + height / 2f);
        public Vector2 min => new Vector2(xMin, yMin);
        public Vector2 max => new Vector2(xMax, yMax);
        public float xMin { get => x; set => throw new NotImplementedException(); }
        public float yMin { get => y; set => throw new NotImplementedException(); }
        public float xMax { get => x + width; set => throw new NotImplementedException(); }
        public float yMax { get => y + height; set => throw new NotImplementedException(); }
        public bool Contains(Vector2 point) => throw new NotImplementedException();
        public bool Contains(Vector3 point) => throw new NotImplementedException();
        public bool Overlaps(Rect other) => throw new NotImplementedException();
        public void Set(float x, float y, float width, float height) => throw new NotImplementedException();
        public static Rect MinMaxRect(float xmin, float ymin, float xmax, float ymax) => throw new NotImplementedException();
        public static bool operator ==(Rect lhs, Rect rhs) => lhs.x == rhs.x && lhs.y == rhs.y && lhs.width == rhs.width && lhs.height == rhs.height;
        public static bool operator !=(Rect lhs, Rect rhs) => !(lhs == rhs);
        public override bool Equals(object obj) => obj is Rect other && this == other;
        public override int GetHashCode() => throw new NotImplementedException();
        public override string ToString() => $"(x:{x}, y:{y}, width:{width}, height:{height})";
    }

    public struct RectInt
    {
        public int x, y, width, height;
        public RectInt(int xMin, int yMin, int width, int height) { x = xMin; y = yMin; this.width = width; this.height = height; }
        public Vector2Int min => throw new NotImplementedException();
        public Vector2Int max => throw new NotImplementedException();
        public Vector2Int size => throw new NotImplementedException();
        public bool Contains(Vector2Int position) => throw new NotImplementedException();
    }

    public struct Vector2Int
    {
        public int x, y;
        public Vector2Int(int x, int y) { this.x = x; this.y = y; }
        public static Vector2Int zero => new Vector2Int(0, 0);
        public static Vector2Int one => new Vector2Int(1, 1);
        public static float Distance(Vector2Int a, Vector2Int b) => throw new NotImplementedException();
        public static Vector2Int operator +(Vector2Int a, Vector2Int b) => new Vector2Int(a.x + b.x, a.y + b.y);
        public static Vector2Int operator -(Vector2Int a, Vector2Int b) => new Vector2Int(a.x - b.x, a.y - b.y);
        public static Vector2Int operator *(Vector2Int a, int b) => new Vector2Int(a.x * b, a.y * b);
        public static bool operator ==(Vector2Int lhs, Vector2Int rhs) => lhs.x == rhs.x && lhs.y == rhs.y;
        public static bool operator !=(Vector2Int lhs, Vector2Int rhs) => !(lhs == rhs);
        public override bool Equals(object obj) => obj is Vector2Int other && this == other;
        public override int GetHashCode() => throw new NotImplementedException();
        public override string ToString() => $"({x}, {y})";
        public static implicit operator Vector2(Vector2Int v) => new Vector2(v.x, v.y);
    }

    public struct Vector3Int
    {
        public int x, y, z;
        public Vector3Int(int x, int y, int z) { this.x = x; this.y = y; this.z = z; }
        public static Vector3Int zero => new Vector3Int(0, 0, 0);
    }

    public struct Bounds
    {
        public Bounds(Vector3 center, Vector3 size)
        {
            this.center = center;
            this.size = size;
            extents = Vector3.zero;
            min = Vector3.zero;
            max = Vector3.zero;
        }
        public Vector3 center;
        public Vector3 size { get; set; }
        public Vector3 extents { get; set; }
        public Vector3 min { get; set; }
        public Vector3 max { get; set; }
        public bool Contains(Vector3 point) => throw new NotImplementedException();
        public void Encapsulate(Vector3 point) => throw new NotImplementedException();
        public bool Intersects(Bounds bounds) => throw new NotImplementedException();
    }

    public struct Plane
    {
        public Plane(Vector3 inNormal, Vector3 inPoint) { normal = inNormal; distance = 0f; }
        public Plane(Vector3 a, Vector3 b, Vector3 c) { normal = Vector3.zero; distance = 0f; }
        public Vector3 normal;
        public float distance;
        public float GetDistanceToPoint(Vector3 point) => throw new NotImplementedException();
        public bool Raycast(Ray ray, out float enter) => throw new NotImplementedException();
    }

    public struct Ray
    {
        public Ray(Vector3 origin, Vector3 direction) { this.origin = origin; this.direction = direction; }
        public Vector3 origin;
        public Vector3 direction;
        public Vector3 GetPoint(float distance) => throw new NotImplementedException();
    }

    public static class Mathf
    {
        public const float PI = 3.14159274f;
        public const float Infinity = float.PositiveInfinity;
        public const float NegativeInfinity = float.NegativeInfinity;
        public const float Deg2Rad = 0.0174532924f;
        public const float Rad2Deg = 57.29578f;
        public const float Epsilon = 1.401298E-45f;
        public static float Abs(float f) => throw new NotImplementedException();
        public static int Abs(int f) => throw new NotImplementedException();
        public static float Acos(float f) => throw new NotImplementedException();
        public static float Asin(float f) => throw new NotImplementedException();
        public static float Atan(float f) => throw new NotImplementedException();
        public static float Atan2(float y, float x) => throw new NotImplementedException();
        public static float Ceil(float f) => throw new NotImplementedException();
        public static int CeilToInt(float f) => throw new NotImplementedException();
        public static float Clamp(float value, float min, float max) => throw new NotImplementedException();
        public static int Clamp(int value, int min, int max) => throw new NotImplementedException();
        public static float Clamp01(float value) => throw new NotImplementedException();
        public static float Cos(float f) => throw new NotImplementedException();
        public static float DeltaAngle(float current, float target) => throw new NotImplementedException();
        public static bool Approximately(float a, float b) => throw new NotImplementedException();
        public static float Exp(float power) => throw new NotImplementedException();
        public static float Floor(float f) => throw new NotImplementedException();
        public static int FloorToInt(float f) => throw new NotImplementedException();
        public static float GammaToLinearSpace(float value) => throw new NotImplementedException();
        public static float InverseLerp(float a, float b, float value) => throw new NotImplementedException();
        public static float Lerp(float a, float b, float t) => throw new NotImplementedException();
        public static float LerpAngle(float a, float b, float t) => throw new NotImplementedException();
        public static float LerpUnclamped(float a, float b, float t) => throw new NotImplementedException();
        public static float LinearToGammaSpace(float value) => throw new NotImplementedException();
        public static float Log(float f) => throw new NotImplementedException();
        public static float Max(float a, float b) => throw new NotImplementedException();
        public static int Max(int a, int b) => throw new NotImplementedException();
        public static float Min(float a, float b) => throw new NotImplementedException();
        public static int Min(int a, int b) => throw new NotImplementedException();
        public static float MoveTowards(float current, float target, float maxDelta) => throw new NotImplementedException();
        public static float MoveTowardsAngle(float current, float target, float maxDelta) => throw new NotImplementedException();
        public static float PingPong(float t, float length) => throw new NotImplementedException();
        public static float Pow(float f, float p) => throw new NotImplementedException();
        public static float Repeat(float t, float length) => throw new NotImplementedException();
        public static float Round(float f) => throw new NotImplementedException();
        public static int RoundToInt(float f) => throw new NotImplementedException();
        public static float Sign(float f) => throw new NotImplementedException();
        public static float Sin(float f) => throw new NotImplementedException();
        public static float SmoothDamp(float current, float target, ref float currentVelocity, float smoothTime) => throw new NotImplementedException();
        public static float SmoothStep(float from, float to, float t) => throw new NotImplementedException();
        public static float Sqrt(float f) => throw new NotImplementedException();
        public static float Tan(float f) => throw new NotImplementedException();
    }

    public static class Time
    {
        public static float time => throw new NotImplementedException();
        public static float deltaTime => throw new NotImplementedException();
        public static float fixedDeltaTime { get; set; }
        public static float fixedTime => throw new NotImplementedException();
        public static float unscaledTime => throw new NotImplementedException();
        public static float unscaledDeltaTime => throw new NotImplementedException();
        public static float timeScale { get; set; }
        public static float maximumDeltaTime { get; set; }
        public static int frameCount => throw new NotImplementedException();
        public static float realtimeSinceStartup => throw new NotImplementedException();
        public static float smoothDeltaTime => throw new NotImplementedException();
        public static bool inFixedTimeStep => throw new NotImplementedException();
    }

    public static class Application
    {
        public static bool isPlaying => throw new NotImplementedException();
        public static bool isEditor => throw new NotImplementedException();
        public static bool isBatchMode => throw new NotImplementedException();
        public static bool isFocused => throw new NotImplementedException();
        public static bool isMobilePlatform => throw new NotImplementedException();
        public static string unityVersion => throw new NotImplementedException();
        public static string version => throw new NotImplementedException();
        public static string companyName => throw new NotImplementedException();
        public static string productName => throw new NotImplementedException();
        public static string dataPath => throw new NotImplementedException();
        public static string persistentDataPath => throw new NotImplementedException();
        public static string streamingAssetsPath => throw new NotImplementedException();
        public static string temporaryCachePath => throw new NotImplementedException();
        public static RuntimePlatform platform => throw new NotImplementedException();
        public static SystemLanguage systemLanguage => throw new NotImplementedException();
        public static int targetFrameRate { get; set; }
        public static bool runInBackground { get; set; }
        public static void Quit() => throw new NotImplementedException();
        public static void OpenURL(string url) => throw new NotImplementedException();
    }

    public enum RuntimePlatform { Android = 11, IPhonePlayer = 8, WindowsEditor = 33, LinuxEditor = 36, OSXEditor = 7 }
    public enum SystemLanguage { English = 10, Unknown = 0 }
    public enum ColorSpace { Uninitialized = -1, Gamma = 0, Linear = 1 }
    public enum UIOrientation { Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, AutoRotation = 5 }
    public enum ScreenOrientation { Unknown = 0, Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, AutoRotation = 5 }
    public enum DeviceOrientation { Unknown = 0, Portrait = 1, PortraitUpsideDown = 2, LandscapeLeft = 3, LandscapeRight = 4, FaceUp = 5, FaceDown = 6 }
    public enum LogType { Error = 0, Assert = 1, Warning = 2, Log = 3, Exception = 4 }

    public static class Screen
    {
        public static int width => throw new NotImplementedException();
        public static int height => throw new NotImplementedException();
        public static float dpi => throw new NotImplementedException();
        public static Rect safeArea => throw new NotImplementedException();
        public static Rect[] cutouts => throw new NotImplementedException();
        public static ScreenOrientation orientation { get; set; }
        public static bool autorotateToLandscapeLeft { get; set; }
        public static bool autorotateToLandscapeRight { get; set; }
        public static bool autorotateToPortrait { get; set; }
        public static bool autorotateToPortraitUpsideDown { get; set; }
        public static int sleepTimeout { get; set; }
        public static Resolution currentResolution => throw new NotImplementedException();
    }

    public struct Resolution
    {
        public int width;
        public int height;
        public int refreshRate;
    }

    public static class SystemInfo
    {
        public static bool supportsGyroscope => throw new NotImplementedException();
        public static bool supportsAccelerometer => throw new NotImplementedException();
        public static bool supportsVibration => throw new NotImplementedException();
        public static string deviceModel => throw new NotImplementedException();
        public static string deviceName => throw new NotImplementedException();
        public static string graphicsDeviceName => throw new NotImplementedException();
        public static string graphicsDeviceVersion => throw new NotImplementedException();
        public static int graphicsMemorySize => throw new NotImplementedException();
        public static int processorCount => throw new NotImplementedException();
        public static int systemMemorySize => throw new NotImplementedException();
        public static int maxTextureSize => throw new NotImplementedException();
        public static bool supportsInstancing => throw new NotImplementedException();
        public static DeviceType deviceType => throw new NotImplementedException();
    }

    public enum DeviceType { Unknown = 0, Handheld = 1, Console = 2, Desktop = 3 }

    public static class Debug
    {
        public static void Log(object message) => throw new NotImplementedException();
        public static void Log(object message, Object context) => throw new NotImplementedException();
        public static void LogFormat(string format, params object[] args) => throw new NotImplementedException();
        public static void LogWarning(object message) => throw new NotImplementedException();
        public static void LogWarning(object message, Object context) => throw new NotImplementedException();
        public static void LogError(object message) => throw new NotImplementedException();
        public static void LogError(object message, Object context) => throw new NotImplementedException();
        public static void LogException(Exception exception) => throw new NotImplementedException();
        public static void Assert(bool condition, string message) => throw new NotImplementedException();
        public static void Break() => throw new NotImplementedException();
        public static void DrawLine(Vector3 start, Vector3 end, Color color) => throw new NotImplementedException();
        public static void DrawRay(Vector3 start, Vector3 dir, Color color) => throw new NotImplementedException();
    }

    public static class Resources
    {
        public static T Load<T>(string path) where T : Object => throw new NotImplementedException();
        public static Object Load(string path) => throw new NotImplementedException();
        public static Object Load(string path, Type systemTypeInstance) => throw new NotImplementedException();
        public static T[] LoadAll<T>(string path) where T : Object => throw new NotImplementedException();
        public static Object[] LoadAll(string path) => throw new NotImplementedException();
        public static void UnloadAsset(Object assetToUnload) => throw new NotImplementedException();
        public static AsyncOperation UnloadUnusedAssets() => throw new NotImplementedException();
        public static T GetBuiltinResource<T>(string path) where T : Object => throw new NotImplementedException();
        public static Object GetBuiltinResource(Type type, string path) => throw new NotImplementedException();
    }

    public class AsyncOperation : YieldInstruction
    {
        public bool isDone => throw new NotImplementedException();
        public float progress => throw new NotImplementedException();
        public bool allowSceneActivation { get; set; }
    }

    public class YieldInstruction { }
    public class WaitForSeconds : YieldInstruction { public WaitForSeconds(float seconds) { } }
    public class WaitForFixedUpdate : YieldInstruction { }
    public class WaitForEndOfFrame : YieldInstruction { }
    public class WaitUntil : YieldInstruction { public WaitUntil(Func<bool> predicate) { } }
    public class WaitWhile : YieldInstruction { public WaitWhile(Func<bool> predicate) { } }

    public class TextAsset : Object
    {
        public string text => throw new NotImplementedException();
        public byte[] bytes => throw new NotImplementedException();
    }

    public class Texture : Object
    {
        public int width { get; set; }
        public int height { get; set; }
        public FilterMode filterMode { get; set; }
        public TextureWrapMode wrapMode { get; set; }
        public int anisoLevel { get; set; }
        public float mipMapBias { get; set; }
        public static void SetGlobalFloat(string name, float value) => throw new NotImplementedException();
    }

    public enum FilterMode { Point = 0, Bilinear = 1, Trilinear = 2 }
    public enum TextureWrapMode { Repeat = 0, Clamp = 1, Mirror = 2, MirrorOnce = 3 }

    public class Texture2D : Texture
    {
        public Texture2D(int width, int height) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, int mipCount, bool linear) { }
        public Texture2D(int width, int height, TextureFormat textureFormat, bool mipChain, bool linear) { }
        public TextureFormat format => throw new NotImplementedException();
        public int mipmapCount => throw new NotImplementedException();
        public bool isReadable => throw new NotImplementedException();
        public void Apply() => throw new NotImplementedException();
        public void Apply(bool updateMipmaps) => throw new NotImplementedException();
        public void Apply(bool updateMipmaps, bool makeNoLongerReadable) => throw new NotImplementedException();
        public Color GetPixel(int x, int y) => throw new NotImplementedException();
        public Color GetPixel(int x, int y, int mipLevel) => throw new NotImplementedException();
        public Color[] GetPixels() => throw new NotImplementedException();
        public Color32[] GetPixels32() => throw new NotImplementedException();
        public void SetPixel(int x, int y, Color color) => throw new NotImplementedException();
        public void SetPixels(Color[] colors) => throw new NotImplementedException();
        public void SetPixels32(Color32[] colors) => throw new NotImplementedException();
        public void SetPixels32(Color32[] colors, int miplevel) => throw new NotImplementedException();
        public bool Resize(int width, int height) => throw new NotImplementedException();
        public bool Resize(int width, int height, TextureFormat format, bool hasMipMap) => throw new NotImplementedException();
        public byte[] EncodeToPNG() => throw new NotImplementedException();
        public byte[] EncodeToJPG() => throw new NotImplementedException();
        public bool LoadImage(byte[] data) => throw new NotImplementedException();
        public bool LoadImage(byte[] data, bool markNonReadable) => throw new NotImplementedException();
        public void LoadRawTextureData(byte[] data) => throw new NotImplementedException();
        public void LoadRawTextureData<T>(T[] data) where T : struct => throw new NotImplementedException();
        public void LoadRawTextureData(IntPtr data, int size) => throw new NotImplementedException();
        public byte[] GetRawTextureData() => throw new NotImplementedException();
        public T[] GetRawTextureData<T>() where T : struct => throw new NotImplementedException();
        public static Texture2D whiteTexture => throw new NotImplementedException();
        public static Texture2D blackTexture => throw new NotImplementedException();
    }

    public enum TextureFormat { Alpha8 = 1, RGB24 = 3, RGBA32 = 4, ARGB32 = 5, RGBAHalf = 17, R8 = 63, RHalf = 66, RFloat = 76 }

    public class RenderTexture : Texture
    {
        public RenderTexture(int width, int height, int depth) { }
        public bool useMipMap { get; set; }
        public static RenderTexture GetTemporary(int width, int height) => throw new NotImplementedException();
        public static void ReleaseTemporary(RenderTexture temp) => throw new NotImplementedException();
        public static RenderTexture active { get; set; }
    }

    public class Sprite : Object
    {
        public Rect rect => throw new NotImplementedException();
        public Texture2D texture => throw new NotImplementedException();
        public Vector2 pivot => throw new NotImplementedException();
        public Bounds bounds => throw new NotImplementedException();
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot) => throw new NotImplementedException();
        public static Sprite Create(Texture2D texture, Rect rect, Vector2 pivot, float pixelsPerUnit) => throw new NotImplementedException();
    }

    public class Font : Object
    {
        public int fontSize => throw new NotImplementedException();
        public string[] fontNames => throw new NotImplementedException();
        public bool dynamic => throw new NotImplementedException();
        public static Font CreateDynamicFontFromOSFont(string fontname, int size) => throw new NotImplementedException();
    }

    public class Material : Object
    {
        public Material(Shader shader) { }
        public Material(Material material) { }
        public Shader shader { get; set; }
        public Color color { get; set; }
        public Texture mainTexture { get; set; }
        public Vector2 mainTextureOffset { get; set; }
        public Vector2 mainTextureScale { get; set; }
        public int renderQueue { get; set; }
        public void SetColor(string name, Color value) => throw new NotImplementedException();
        public void SetColor(int nameID, Color value) => throw new NotImplementedException();
        public void SetFloat(string name, float value) => throw new NotImplementedException();
        public void SetInt(string name, int value) => throw new NotImplementedException();
        public void SetTexture(string name, Texture value) => throw new NotImplementedException();
        public void SetVector(string name, Vector4 value) => throw new NotImplementedException();
        public void EnableKeyword(string keyword) => throw new NotImplementedException();
        public void DisableKeyword(string keyword) => throw new NotImplementedException();
        public void CopyPropertiesFromMaterial(Material mat) => throw new NotImplementedException();
    }

    public class Shader : Object
    {
        public static Shader Find(string name) => throw new NotImplementedException();
        public static int PropertyToID(string name) => throw new NotImplementedException();
        public int passCount => throw new NotImplementedException();
    }

    public class Mesh : Object
    {
        public Vector3[] vertices { get; set; }
        public int[] triangles { get; set; }
        public Vector3[] normals { get; set; }
        public Vector2[] uv { get; set; }
        public Color[] colors { get; set; }
        public int vertexCount => throw new NotImplementedException();
        public Bounds bounds { get; set; }
        public Rendering.IndexFormat indexFormat { get; set; }
        public void Clear() => throw new NotImplementedException();
        public void RecalculateNormals() => throw new NotImplementedException();
        public void RecalculateBounds() => throw new NotImplementedException();
        public void RecalculateTangents() => throw new NotImplementedException();
        public void SetVertices(Vector3[] inVertices) => throw new NotImplementedException();
        public void SetTriangles(int[] triangles, int submesh) => throw new NotImplementedException();
        public void SetUVs(int channel, List<Vector2> uvs) => throw new NotImplementedException();
        public void MarkDynamic() => throw new NotImplementedException();
        public void UploadMeshData(bool markNoLongerReadable) => throw new NotImplementedException();
    }

    public class MeshFilter : Component
    {
        public Mesh mesh { get; set; }
        public Mesh sharedMesh { get; set; }
    }

    public class Renderer : Component
    {
        public Material material { get; set; }
        public Material[] materials { get; set; }
        public Material sharedMaterial { get; set; }
        public Material[] sharedMaterials { get; set; }
        public Bounds bounds => throw new NotImplementedException();
        public bool enabled { get; set; }
        public ShadowCastingMode shadowCastingMode { get; set; }
        public bool receiveShadows { get; set; }
        public int sortingOrder { get; set; }
        public void SetPropertyBlock(MaterialPropertyBlock properties) => throw new NotImplementedException();
    }

    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }

    public class MaterialPropertyBlock
    {
        public void SetColor(string name, Color value) => throw new NotImplementedException();
        public void SetFloat(string name, float value) => throw new NotImplementedException();
        public void SetTexture(string name, Texture value) => throw new NotImplementedException();
    }

    public class MeshRenderer : Renderer { }

    public class Camera : Behaviour
    {
        public static Camera main => throw new NotImplementedException();
        public static Camera current => throw new NotImplementedException();
        public static int allCamerasCount => throw new NotImplementedException();
        public static Camera[] allCameras => throw new NotImplementedException();
        public float fieldOfView { get; set; }
        public float nearClipPlane { get; set; }
        public float farClipPlane { get; set; }
        public bool orthographic { get; set; }
        public float orthographicSize { get; set; }
        public Rect rect { get; set; }
        public Color backgroundColor { get; set; }
        public CameraClearFlags clearFlags { get; set; }
        public int cullingMask { get; set; }
        public int depth { get; set; }
        public RenderingPath renderingPath { get; set; }
        public bool useOcclusionCulling { get; set; }
        public bool allowHDR { get; set; }
        public bool allowMSAA { get; set; }
        public Matrix4x4 projectionMatrix { get; set; }
        public Vector3 WorldToScreenPoint(Vector3 position) => throw new NotImplementedException();
        public Vector3 ScreenToWorldPoint(Vector3 position) => throw new NotImplementedException();
        public Vector3 ViewportToWorldPoint(Vector3 position) => throw new NotImplementedException();
        public Vector3 WorldToViewportPoint(Vector3 position) => throw new NotImplementedException();
        public Ray ScreenPointToRay(Vector3 position) => throw new NotImplementedException();
        public Ray ViewportPointToRay(Vector3 position) => throw new NotImplementedException();
        public void Render() => throw new NotImplementedException();
        public RenderTexture targetTexture { get; set; }
    }

    public enum CameraClearFlags { Skybox = 1, Color = 2, SolidColor = 2, Depth = 3, Nothing = 4 }
    public enum RenderingPath { UsePlayerSettings = -1, VertexLit = 0, Forward = 1, DeferredLighting = 2, DeferredShading = 3 }

    public class Light : Behaviour
    {
        public LightType type { get; set; }
        public float intensity { get; set; }
        public Color color { get; set; }
        public float range { get; set; }
        public float spotAngle { get; set; }
        public LightShadows shadows { get; set; }
        public float shadowStrength { get; set; }
        public int cullingMask { get; set; }
    }

    public enum LightType { Spot = 0, Directional = 1, Point = 2, Area = 3, Rectangle = 3, Disc = 4 }
    public enum LightShadows { None = 0, Hard = 1, Soft = 2 }

    public class AudioListener : Behaviour { }
    public class AudioSource : Behaviour
    {
        public AudioClip clip { get; set; }
        public void Play() => throw new NotImplementedException();
        public void Stop() => throw new NotImplementedException();
        public bool loop { get; set; }
        public float volume { get; set; }
    }

    public class AudioClip : Object
    {
        public float length => throw new NotImplementedException();
    }

    public static class RenderSettings
    {
        public static Color ambientLight { get; set; }
        public static Rendering.AmbientMode ambientMode { get; set; }
        public static Color ambientSkyColor { get; set; }
        public static Color ambientEquatorColor { get; set; }
        public static Color ambientGroundColor { get; set; }
        public static float fogDensity { get; set; }
        public static bool fog { get; set; }
        public static Light sun { get; set; }
    }

    public static class ColorUtility
    {
        public static bool TryParseHtmlString(string htmlString, out Color color) => throw new NotImplementedException();
        public static string ToHtmlStringRGB(Color color) => throw new NotImplementedException();
        public static string ToHtmlStringRGBA(Color color) => throw new NotImplementedException();
    }

    public static class QualitySettings
    {
        public static string[] names => throw new NotImplementedException();
        public static void SetQualityLevel(int index, bool applyExpensiveChanges) => throw new NotImplementedException();
        public static void SetQualityLevel(int index) => throw new NotImplementedException();
        public static int GetQualityLevel() => throw new NotImplementedException();
        public static void IncreaseLevel(bool applyExpensiveChanges) => throw new NotImplementedException();
        public static void DecreaseLevel(bool applyExpensiveChanges) => throw new NotImplementedException();
        public static Rendering.RenderPipelineAsset renderPipeline { get; set; }
        public static int antiAliasing { get; set; }
        public static float shadowDistance { get; set; }
        public static int shadowCascades { get; set; }
        public static int masterTextureLimit { get; set; }
        public static int pixelLightCount { get; set; }
        public static ShadowQuality shadows { get; set; }
        public static AnisotropicFiltering anisotropicFiltering { get; set; }
        public static int vSyncCount { get; set; }
    }

    public enum ShadowQuality { Disable = 0, HardOnly = 1, All = 2 }
    public enum AnisotropicFiltering { Disable = 0, Enable = 1, ForceEnable = 2 }

    public static class GraphicsSettings
    {
        public static Rendering.RenderPipelineAsset defaultRenderPipeline { get; set; }
        public static Rendering.RenderPipelineAsset currentRenderPipeline => throw new NotImplementedException();
    }

    public static class LayerMask
    {
        public static int NameToLayer(string layerName) => throw new NotImplementedException();
        public static string LayerToName(int layer) => throw new NotImplementedException();
        public static int GetMask(params string[] layerNames) => throw new NotImplementedException();
    }

    public static class Random
    {
        public static float value => throw new NotImplementedException();
        public static float Range(float min, float max) => throw new NotImplementedException();
        public static int Range(int min, int max) => throw new NotImplementedException();
        public static void InitState(int seed) => throw new NotImplementedException();
    }

    public struct RaycastHit
    {
        public Vector3 point { get; set; }
        public Vector3 normal { get; set; }
        public float distance { get; set; }
        public Collider collider { get; set; }
        public Transform transform { get; set; }
        public Rigidbody rigidbody { get; set; }
        public int colliderInstanceID => throw new NotImplementedException();
    }

    public struct RaycastHit2D
    {
        public Vector2 point { get; set; }
        public float distance { get; set; }
        public Collider2D collider { get; set; }
    }

    public class Collider2D : Behaviour { public bool isTrigger { get; set; } }

    // ---- PhysicsModule (merged into this stub assembly; Unity ships it as a separate module,
    // but every assembly gets every engine module, so merging cannot mask a missing reference) ----

    public class Collider : Component
    {
        public bool enabled { get; set; }
        public bool isTrigger { get; set; }
        public Rigidbody attachedRigidbody => throw new NotImplementedException();
        public PhysicMaterial material { get; set; }
        public PhysicMaterial sharedMaterial { get; set; }
        public Bounds bounds => throw new NotImplementedException();
        public float contactOffset { get; set; }
        public Vector3 ClosestPoint(Vector3 position) => throw new NotImplementedException();
        public Vector3 ClosestPointOnBounds(Vector3 position) => throw new NotImplementedException();
        public bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance) => throw new NotImplementedException();
    }

    public class BoxCollider : Collider { public Vector3 center { get; set; } public Vector3 size { get; set; } }
    public class SphereCollider : Collider { public Vector3 center { get; set; } public float radius { get; set; } }
    public class CapsuleCollider : Collider { public Vector3 center { get; set; } public float radius { get; set; } public float height { get; set; } public int direction { get; set; } }
    public class MeshCollider : Collider { public Mesh sharedMesh { get; set; } public bool convex { get; set; } public bool inflateMesh { get; set; } public float skinWidth { get; set; } }
    public class TerrainCollider : Collider { public UnityEngine.Object terrainData { get; set; } }
    public class CharacterController : Collider
    {
        public bool isGrounded => throw new NotImplementedException();
        public CollisionFlags Move(Vector3 motion) => throw new NotImplementedException();
        public void SimpleMove(Vector3 speed) => throw new NotImplementedException();
        public float radius { get; set; }
        public float height { get; set; }
        public Vector3 velocity => throw new NotImplementedException();
        public float slopeLimit { get; set; }
        public float stepOffset { get; set; }
        public Vector3 center { get; set; }
    }

    [Flags]
    public enum CollisionFlags { None = 0, Sides = 1, Above = 2, Below = 4, CollidedSides = 1, CollidedAbove = 2, CollidedBelow = 4 }

    public class PhysicMaterial : Object
    {
        public float dynamicFriction { get; set; }
        public float staticFriction { get; set; }
        public float bounciness { get; set; }
        public PhysicMaterialCombine frictionCombine { get; set; }
        public PhysicMaterialCombine bounceCombine { get; set; }
    }

    public enum PhysicMaterialCombine { Average = 0, Multiply = 1, Minimum = 2, Maximum = 3 }

    public class Rigidbody : Component
    {
        // Unity 6 renamed these: `linearVelocity`/`linearDamping` are current, `velocity`/`drag`
        // are the obsolete aliases. Wayfinder targets 6000.3.26f1 and uses the current names.
        public Vector3 linearVelocity { get; set; }
        public float linearDamping { get; set; }
        public float angularDamping { get; set; }
        public Vector3 velocity { get; set; }
        public Vector3 angularVelocity { get; set; }
        public float mass { get; set; }
        public float drag { get; set; }
        public float angularDrag { get; set; }
        public bool useGravity { get; set; }
        public bool isKinematic { get; set; }
        public bool freezeRotation { get; set; }
        public RigidbodyConstraints constraints { get; set; }
        public CollisionDetectionMode collisionDetectionMode { get; set; }
        public RigidbodyInterpolation interpolation { get; set; }
        public Vector3 centerOfMass { get; set; }
        public Vector3 inertiaTensor { get; set; }
        public Quaternion inertiaTensorRotation { get; set; }
        public Vector3 worldCenterOfMass => throw new NotImplementedException();
        public bool detectCollisions { get; set; }
        public float sleepThreshold { get; set; }
        public int solverIterations { get; set; }
        public int solverVelocityIterations { get; set; }
        public float maxAngularVelocity { get; set; }
        public float maxDepenetrationVelocity { get; set; }
        public Vector3 position { get; set; }
        public Quaternion rotation { get; set; }
        public bool automaticCenterOfMass { get; set; }
        public bool automaticInertiaTensor { get; set; }
        public void AddForce(Vector3 force) => throw new NotImplementedException();
        public void AddForce(Vector3 force, ForceMode mode) => throw new NotImplementedException();
        public void AddForce(float x, float y, float z) => throw new NotImplementedException();
        public void AddRelativeForce(Vector3 force) => throw new NotImplementedException();
        public void AddRelativeForce(Vector3 force, ForceMode mode) => throw new NotImplementedException();
        public void AddTorque(Vector3 torque) => throw new NotImplementedException();
        public void AddTorque(Vector3 torque, ForceMode mode) => throw new NotImplementedException();
        public void AddRelativeTorque(Vector3 torque) => throw new NotImplementedException();
        public void AddForceAtPosition(Vector3 force, Vector3 position) => throw new NotImplementedException();
        public void AddForceAtPosition(Vector3 force, Vector3 position, ForceMode mode) => throw new NotImplementedException();
        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius) => throw new NotImplementedException();
        public Vector3 GetPointVelocity(Vector3 worldPoint) => throw new NotImplementedException();
        public Vector3 GetRelativePointVelocity(Vector3 relativePoint) => throw new NotImplementedException();
        public void MovePosition(Vector3 position) => throw new NotImplementedException();
        public void MoveRotation(Quaternion rot) => throw new NotImplementedException();
        public void Sleep() => throw new NotImplementedException();
        public void WakeUp() => throw new NotImplementedException();
        public bool IsSleeping() => throw new NotImplementedException();
        public void SetDensity(float density) => throw new NotImplementedException();
        public void ResetCenterOfMass() => throw new NotImplementedException();
        public void ResetInertiaTensor() => throw new NotImplementedException();
        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo) => throw new NotImplementedException();
        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo, float maxDistance) => throw new NotImplementedException();
    }

    public struct JointSpring
    {
        public float spring;
        public float damper;
        public float targetPosition;
    }

    public struct WheelFrictionCurve
    {
        public float extremumSlip;
        public float extremumValue;
        public float asymptoteSlip;
        public float asymptoteValue;
        public float stiffness;
    }

    public struct WheelHit
    {
        public Collider collider { get; set; }
        public Vector3 point { get; set; }
        public Vector3 normal { get; set; }
        public Vector3 forwardDir { get; set; }
        public Vector3 sidewaysDir { get; set; }
        public float force { get; set; }
        public float forwardSlip { get; set; }
        public float sidewaysSlip { get; set; }
    }

    public class WheelCollider : Collider
    {
        public Vector3 center { get; set; }
        public float radius { get; set; }
        public float suspensionDistance { get; set; }
        public JointSpring suspensionSpring { get; set; }
        public float sprungMass { get; set; }
        public float forceAppPointDistance { get; set; }
        public float mass { get; set; }
        public float wheelDampingRate { get; set; }
        public WheelFrictionCurve forwardFriction { get; set; }
        public WheelFrictionCurve sidewaysFriction { get; set; }
        public float motorTorque { get; set; }
        public float brakeTorque { get; set; }
        public float steerAngle { get; set; }
        public bool isGrounded => throw new NotImplementedException();
        public float rpm { get; set; }
        public void ConfigureVehicleSubsteps(float speedThreshold, int stepsBelowThreshold, int stepsAboveThreshold) => throw new NotImplementedException();
        public bool GetGroundHit(out WheelHit hit) => throw new NotImplementedException();
        public void GetWorldPose(out Vector3 pos, out Quaternion quat) => throw new NotImplementedException();
    }

    public static class Physics
    {
        public static Vector3 gravity { get; set; }
        public static float defaultContactOffset { get; set; }
        public static int defaultSolverIterations { get; set; }
        public static int defaultSolverVelocityIterations { get; set; }
        public static float sleepThreshold { get; set; }
        public static int defaultRaycastLayers => throw new NotImplementedException();
        public static int DefaultRaycastLayers => throw new NotImplementedException();
        public static int AllLayers => throw new NotImplementedException();
        public static int IgnoreRaycastLayer => throw new NotImplementedException();
        public static bool queriesHitTriggers { get; set; }
        public static bool queriesHitBackfaces { get; set; }
        public static float bounceThreshold { get; set; }
        public static bool Raycast(Vector3 origin, Vector3 direction) => throw new NotImplementedException();
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance) => throw new NotImplementedException();
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo) => throw new NotImplementedException();
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => throw new NotImplementedException();
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => throw new NotImplementedException();
        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw new NotImplementedException();
        public static bool Raycast(Ray ray) => throw new NotImplementedException();
        public static bool Raycast(Ray ray, out RaycastHit hitInfo) => throw new NotImplementedException();
        public static bool Raycast(Ray ray, float maxDistance) => throw new NotImplementedException();
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance) => throw new NotImplementedException();
        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask) => throw new NotImplementedException();
        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction) => throw new NotImplementedException();
        public static RaycastHit[] RaycastAll(Ray ray, float maxDistance) => throw new NotImplementedException();
        public static bool Linecast(Vector3 start, Vector3 end) => throw new NotImplementedException();
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo) => throw new NotImplementedException();
        public static bool Linecast(Vector3 start, Vector3 end, int layerMask) => throw new NotImplementedException();
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask) => throw new NotImplementedException();
        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw new NotImplementedException();
        public static bool Linecast(Vector3 start, Vector3 end, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw new NotImplementedException();
        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw new NotImplementedException();
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo) => throw new NotImplementedException();
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => throw new NotImplementedException();
        public static bool SphereCast(Ray ray, float radius) => throw new NotImplementedException();
        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo) => throw new NotImplementedException();
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw new NotImplementedException();
        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => throw new NotImplementedException();
        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => throw new NotImplementedException();
        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius) => throw new NotImplementedException();
        public static bool CheckSphere(Vector3 position, float radius) => throw new NotImplementedException();
        public static bool CheckSphere(Vector3 position, float radius, int layerMask) => throw new NotImplementedException();
        public static bool CheckBox(Vector3 center, Vector3 halfExtents) => throw new NotImplementedException();
        public static Collider[] OverlapSphere(Vector3 position, float radius) => throw new NotImplementedException();
        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents) => throw new NotImplementedException();
        public static bool ComputePenetration(Collider colliderA, Vector3 positionA, Quaternion rotationA, Collider colliderB, Vector3 positionB, Quaternion rotationB, out Vector3 direction, out float distance) => throw new NotImplementedException();
        public static void IgnoreCollision(Collider collider1, Collider collider2) => throw new NotImplementedException();
        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction) => throw new NotImplementedException();
        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction) => throw new NotImplementedException();
    }

    public enum QueryTriggerInteraction { UseGlobal = 0, Ignore = 1, Collide = 2 }
    public enum ForceMode { Force = 0, Acceleration = 5, Impulse = 1, VelocityChange = 2 }
    public enum RigidbodyInterpolation { None = 0, Interpolate = 1, Extrapolate = 2 }
    public enum CollisionDetectionMode { Discrete = 0, Continuous = 1, ContinuousDynamic = 2, ContinuousSpeculative = 3 }
    public enum RigidbodyConstraints { None = 0, FreezePositionX = 2, FreezePositionY = 4, FreezePositionZ = 8, FreezeRotationX = 16, FreezeRotationY = 32, FreezeRotationZ = 64, FreezePosition = 14, FreezeRotation = 112, FreezeAll = 126 }

    public class CanvasRenderer : Component { }

    public class Canvas : Behaviour
    {
        public RenderMode renderMode { get; set; }
        public Camera worldCamera { get; set; }
        public float scaleFactor { get; set; }
        public float planeDistance { get; set; }
        public int sortingOrder { get; set; }
        public bool overrideSorting { get; set; }
        public int targetDisplay { get; set; }
    }

    public enum RenderMode { ScreenSpaceOverlay = 0, ScreenSpaceCamera = 1, WorldSpace = 2 }

    public class RectTransform : Transform
    {
        public Vector2 anchoredPosition { get; set; }
        public Vector2 anchoredPosition3D { get; set; }
        public Vector2 anchorMin { get; set; }
        public Vector2 anchorMax { get; set; }
        public Vector2 offsetMin { get; set; }
        public Vector2 offsetMax { get; set; }
        public Vector2 pivot { get; set; }
        public Vector2 sizeDelta { get; set; }
        public Vector3 anchoredPosition3DUnclamped => throw new NotImplementedException();
        public Rect rect => throw new NotImplementedException();
        public void SetSizeWithCurrentAnchors(RectTransform.Axis axis, float size) => throw new NotImplementedException();
        public void SetInsetAndSizeFromParentEdge(RectTransform.Edge edge, float inset, float size) => throw new NotImplementedException();
        public void ForceUpdateRectTransforms() => throw new NotImplementedException();
        public void GetLocalCorners(Vector3[] fourCornersArray) => throw new NotImplementedException();
        public void GetWorldCorners(Vector3[] fourCornersArray) => throw new NotImplementedException();
        public enum Axis { Horizontal = 0, Vertical = 1 }
        public enum Edge { Left = 0, Right = 1, Top = 2, Bottom = 3 }
    }

    public static class RectTransformUtility
    {
        public static bool ScreenPointToLocalPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector2 localPoint) => throw new NotImplementedException();
        public static bool ScreenPointToWorldPointInRectangle(RectTransform rect, Vector2 screenPoint, Camera cam, out Vector3 worldPoint) => throw new NotImplementedException();
        public static Vector2 WorldToScreenPoint(Camera cam, Vector3 worldPoint) => throw new NotImplementedException();
        public static void FlipLayoutAxes(RectTransform rect, bool keepPositioning, bool recursive) => throw new NotImplementedException();
    }

    public class CanvasScaler : Behaviour
    {
        public CanvasScaler.ScaleMode uiScaleMode { get; set; }
        public Vector2 referenceResolution { get; set; }
        public CanvasScaler.ScreenMatchMode screenMatchMode { get; set; }
        public float matchWidthOrHeight { get; set; }
        public float scaleFactor { get; set; }
        public float dynamicPixelsPerUnit { get; set; }
        public enum ScaleMode { ConstantPixelSize = 0, ScaleWithScreenSize = 1, ConstantPhysicalSize = 2 }
        public enum ScreenMatchMode { MatchWidthOrHeight = 0, Expand = 1, Shrink = 2 }
    }

    public class Graphic : Behaviour
    {
        public bool raycastTarget { get; set; }
        public Color color { get; set; }
        public Material material { get; set; }
        public RectTransform rectTransform => throw new NotImplementedException();
        public float pixelsPerUnit => throw new NotImplementedException();
        public void SetAllDirty() => throw new NotImplementedException();
        public void SetVerticesDirty() => throw new NotImplementedException();
        public void SetLayoutDirty() => throw new NotImplementedException();
    }

    public class MaskableGraphic : Graphic { public bool maskable { get; set; } }
}

namespace UnityEngine.Rendering
{
    public enum AmbientMode { Skybox = 0, Trilight = 1, Flat = 3, Custom = 4 }
    public enum IndexFormat { UInt16 = 0, UInt32 = 1 }
    public enum ShadowCastingMode { Off = 0, On = 1, TwoSided = 2, ShadowsOnly = 3 }
    public enum CompareFunction { Disabled = 0, Never = 1, Less = 2, Equal = 3, LessEqual = 4, Greater = 5, NotEqual = 6, GreaterEqual = 7, Always = 8 }
    public enum CullMode { Off = 0, Front = 1, Back = 2 }

    public class RenderPipelineAsset : ScriptableObject
    {
        protected virtual UnityEngine.Rendering.RenderPipeline CreatePipeline() => throw new NotImplementedException();
    }

    public abstract class RenderPipeline
    {
        protected abstract void Render(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera[] cameras);
    }

    public class RenderPipelineManager
    {
        public static RenderPipelineAsset currentPipelineAsset => throw new NotImplementedException();
    }

    public struct ScriptableRenderContext { }
}

namespace UnityEngine.SceneManagement
{
    public struct Scene
    {
        public string name => throw new NotImplementedException();
        public string path => throw new NotImplementedException();
        public int buildIndex => throw new NotImplementedException();
        public bool isLoaded => throw new NotImplementedException();
        public bool IsValid() => throw new NotImplementedException();
        public UnityEngine.GameObject[] GetRootGameObjects() => throw new NotImplementedException();
    }

    public static class SceneManager
    {
        public static int sceneCount => throw new NotImplementedException();
        public static Scene GetActiveScene() => throw new NotImplementedException();
        public static Scene GetSceneAt(int index) => throw new NotImplementedException();
        public static Scene GetSceneByName(string name) => throw new NotImplementedException();
        public static void LoadScene(int sceneBuildIndex) => throw new NotImplementedException();
        public static void LoadScene(string sceneName) => throw new NotImplementedException();
        public static void LoadScene(int sceneBuildIndex, LoadSceneMode mode) => throw new NotImplementedException();
        public static void LoadScene(string sceneName, LoadSceneMode mode) => throw new NotImplementedException();
        public static UnityEngine.AsyncOperation LoadSceneAsync(int sceneBuildIndex) => throw new NotImplementedException();
        public static UnityEngine.AsyncOperation LoadSceneAsync(string sceneName) => throw new NotImplementedException();
        public static UnityEngine.AsyncOperation LoadSceneAsync(string sceneName, LoadSceneMode mode) => throw new NotImplementedException();
        public static UnityEngine.AsyncOperation UnloadSceneAsync(Scene scene) => throw new NotImplementedException();
        public static bool SetActiveScene(Scene scene) => throw new NotImplementedException();
        public static void MoveGameObjectToScene(UnityEngine.GameObject go, Scene scene) => throw new NotImplementedException();
    }

    public enum LoadSceneMode { Single = 0, Additive = 1 }
}

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<in T0>(T0 arg0);
    public delegate void UnityAction<in T0, in T1>(T0 arg0, T1 arg1);

    public class UnityEventBase { }

    public class UnityEvent : UnityEventBase
    {
        public void AddListener(UnityAction call) => throw new NotImplementedException();
        public void RemoveListener(UnityAction call) => throw new NotImplementedException();
        public void RemoveAllListeners() => throw new NotImplementedException();
        public void Invoke() => throw new NotImplementedException();
    }

    public class UnityEvent<T0> : UnityEventBase
    {
        public void AddListener(UnityAction<T0> call) => throw new NotImplementedException();
        public void RemoveListener(UnityAction<T0> call) => throw new NotImplementedException();
        public void RemoveAllListeners() => throw new NotImplementedException();
        public void Invoke(T0 arg0) => throw new NotImplementedException();
    }
}
