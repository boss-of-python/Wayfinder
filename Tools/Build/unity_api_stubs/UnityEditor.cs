// ---------------------------------------------------------------------------------------------
// UnityEditor (STUB) — editor-only APIs used by Wayfinder.Editor and Tests.Editor
//
// See UnityEngine.CoreModule.cs for what these stubs can and cannot prove.
// ---------------------------------------------------------------------------------------------

using System;
using System.Collections.Generic;

namespace UnityEditor
{
    public class MonoScript : UnityEngine.Object
    {
        public static MonoScript FromMonoBehaviour(UnityEngine.MonoBehaviour behaviour) => throw new NotImplementedException();
        public static MonoScript FromScriptableObject(UnityEngine.ScriptableObject scriptableObject) => throw new NotImplementedException();
        public Type GetClass() => throw new NotImplementedException();
        public string GetScriptPath() => throw new NotImplementedException();
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class MenuItem : Attribute
    {
        public MenuItem(string itemName) { }
        public MenuItem(string itemName, bool isValidateFunction) { }
        public MenuItem(string itemName, bool isValidateFunction, int priority) { }
    }

    [AttributeUsage(AttributeTargets.Method)]
    public class InitializeOnLoadMethodAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class InitializeOnLoadAttribute : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public class DidReloadScripts : Attribute { }

    [AttributeUsage(AttributeTargets.Class)]
    public class CustomEditor : Attribute
    {
        public CustomEditor(Type inspectedType) { }
        public CustomEditor(Type inspectedType, bool editorForChildClasses) { }
    }

    [AttributeUsage(AttributeTargets.Class)]
    public class CanEditMultipleObjects : Attribute { }

    public static class EditorApplication
    {
        public static bool isPlaying { get; set; }
        public static bool isPaused { get; set; }
        public static bool isPlayingOrWillChangePlaymode => throw new NotImplementedException();
        public static bool isCompiling => throw new NotImplementedException();
        public static bool isUpdating => throw new NotImplementedException();
        public static bool isRemoteConnected => throw new NotImplementedException();
        public static string applicationPath => throw new NotImplementedException();
        public static string applicationContentsPath => throw new NotImplementedException();
        public static event Action update;
        public static event Action playModeStateChanged;
        public static event Action delayCall;
        public static void Exit(int returnValue) => throw new NotImplementedException();
        public static void Step() => throw new NotImplementedException();
        public static void LockReloadAssemblies() => throw new NotImplementedException();
        public static void UnlockReloadAssemblies() => throw new NotImplementedException();
    }

    public static class EditorUtility
    {
        public static void DisplayDialog(string title, string message, string ok) => throw new NotImplementedException();
        public static bool DisplayDialog(string title, string message, string ok, string cancel) => throw new NotImplementedException();
        public static void SetDirty(UnityEngine.Object target) => throw new NotImplementedException();
        public static void ClearDirty(UnityEngine.Object target) => throw new NotImplementedException();
        public static bool IsDirty(UnityEngine.Object target) => throw new NotImplementedException();
        public static string OpenFolderPanel(string title, string folder, string defaultName) => throw new NotImplementedException();
        public static string SaveFilePanel(string title, string directory, string defaultName, string extension) => throw new NotImplementedException();
        public static void FocusProjectWindow() => throw new NotImplementedException();
    }

    public static class AssetDatabase
    {
        public static void Refresh() => throw new NotImplementedException();
        public static void ImportAsset(string path) => throw new NotImplementedException();
        public static void ImportAsset(string path, ImportAssetOptions options) => throw new NotImplementedException();
        public static T LoadAssetAtPath<T>(string assetPath) where T : UnityEngine.Object => throw new NotImplementedException();
        public static UnityEngine.Object LoadAssetAtPath(string assetPath, Type type) => throw new NotImplementedException();
        public static UnityEngine.Object LoadMainAssetAtPath(string assetPath) => throw new NotImplementedException();
        public static UnityEngine.Object[] LoadAllAssetsAtPath(string assetPath) => throw new NotImplementedException();
        public static string[] GetAllAssetPaths() => throw new NotImplementedException();
        public static string[] FindAssets(string filter) => throw new NotImplementedException();
        public static string GUIDToAssetPath(string guid) => throw new NotImplementedException();
        public static string AssetPathToGUID(string path) => throw new NotImplementedException();
        public static string GetAssetPath(UnityEngine.Object assetObject) => throw new NotImplementedException();
        public static void CreateAsset(UnityEngine.Object asset, string path) => throw new NotImplementedException();
        public static void CreateFolder(string parentFolder, string newFolderName) => throw new NotImplementedException();
        public static bool DeleteAsset(string path) => throw new NotImplementedException();
        public static bool IsValidFolder(string path) => throw new NotImplementedException();
        public static void SaveAssets() => throw new NotImplementedException();
        public static void SaveAssetIfDirty(UnityEngine.Object assetObject) => throw new NotImplementedException();
        public static bool IsMainAsset(UnityEngine.Object obj) => throw new NotImplementedException();
        public static string GetAssetDependencyHash(string path) => throw new NotImplementedException();
    }

    [Flags]
    public enum ImportAssetOptions
    {
        Default = 0,
        ForceUpdate = 1,
        ForceSynchronousImport = 8,
        ImportRecursive = 256,
        DontDownloadFromCacheServer = 1024,
        ForceUncompressedImport = 4096,
    }

    public class SerializedProperty
    {
        public string propertyPath => throw new NotImplementedException();
        public string name => throw new NotImplementedException();
        public string displayName => throw new NotImplementedException();
        public SerializedPropertyType propertyType => throw new NotImplementedException();
        public int intValue { get; set; }
        public long longValue { get; set; }
        public bool boolValue { get; set; }
        public float floatValue { get; set; }
        public double doubleValue { get; set; }
        public string stringValue { get; set; }
        public UnityEngine.Color colorValue { get; set; }
        public UnityEngine.Vector3 vector3Value { get; set; }
        public UnityEngine.Vector2 vector2Value { get; set; }
        public UnityEngine.Object objectReferenceValue { get; set; }
        public int enumValueIndex { get; set; }
        public int arraySize { get; set; }
        public bool hasChildren => throw new NotImplementedException();
        public bool isArray => throw new NotImplementedException();
        public SerializedProperty GetArrayElementAtIndex(int index) => throw new NotImplementedException();
        public SerializedProperty FindPropertyRelative(string relativePropertyPath) => throw new NotImplementedException();
        public bool Next(bool enterChildren) => throw new NotImplementedException();
        public bool NextVisible(bool enterChildren) => throw new NotImplementedException();
        public void ClearArray() => throw new NotImplementedException();
        public void DeleteCommand() => throw new NotImplementedException();
        public void InsertArrayElementAtIndex(int index) => throw new NotImplementedException();
        public SerializedProperty Copy() => throw new NotImplementedException();
    }

    public enum SerializedPropertyType
    {
        Generic = -1, Integer = 0, Boolean = 1, Float = 2, String = 3, Color = 4, ObjectReference = 5,
        LayerMask = 6, Enum = 7, Vector2 = 8, Vector3 = 9, Vector4 = 10, Rect = 11, ArraySize = 12,
        Character = 13, AnimationCurve = 14, Bounds = 15, Gradient = 16, Quaternion = 17, ExposedReference = 18,
    }

    public class SerializedObject
    {
        public SerializedObject(UnityEngine.Object obj) { }
        public SerializedObject(UnityEngine.Object[] objs) { }
        public UnityEngine.Object targetObject => throw new NotImplementedException();
        public UnityEngine.Object[] targetObjects => throw new NotImplementedException();
        public SerializedProperty FindProperty(string propertyPath) => throw new NotImplementedException();
        public SerializedProperty GetIterator() => throw new NotImplementedException();
        public bool ApplyModifiedProperties() => throw new NotImplementedException();
        public bool ApplyModifiedPropertiesWithoutUndo() => throw new NotImplementedException();
        public void Update() => throw new NotImplementedException();
        public void UpdateIfRequiredOrScript() => throw new NotImplementedException();
        public void SetIsDifferentCacheDirty() => throw new NotImplementedException();
        public void Dispose() => throw new NotImplementedException();
    }

    public class SerializedPropertyHolder { }

    public static class EditorUserBuildSettings
    {
        public static bool buildAppBundle { get; set; }
        public static bool development { get; set; }
        public static bool allowDebugging { get; set; }
        public static bool connectProfiler { get; set; }
        public static bool buildScriptsOnly { get; set; }
        public static bool exportAsGoogleAndroidProject { get; set; }
        public static bool waitForPlayerConnection { get; set; }
        public static BuildTargetGroup selectedBuildTargetGroup => throw new NotImplementedException();
        public static BuildTarget activeBuildTarget => throw new NotImplementedException();
        public static string androidBuildSystem => throw new NotImplementedException();
        public static bool SwitchActiveBuildTarget(BuildTargetGroup targetGroup, BuildTarget target) => throw new NotImplementedException();
    }

    public static class BuildPipeline
    {
        public static Build.Reporting.BuildReport BuildPlayer(BuildPlayerOptions buildPlayerOptions) => throw new NotImplementedException();
        public static Build.Reporting.BuildReport BuildPlayer(string[] levels, string locationPathName, BuildTarget target, BuildOptions options) => throw new NotImplementedException();
        public static bool IsBuildTargetSupported(BuildTargetGroup targetGroup, BuildTarget target) => throw new NotImplementedException();
        public static bool IsBuildTargetSupported(BuildTarget target) => throw new NotImplementedException();
        public static string GetBuildTargetName(BuildTarget target) => throw new NotImplementedException();
        public static string GetBuildTargetGroupFullName(BuildTargetGroup targetGroup) => throw new NotImplementedException();
    }

    public struct BuildPlayerOptions
    {
        public string[] scenes { get; set; }
        public string locationPathName { get; set; }
        public string assetBundleManifestPath { get; set; }
        public BuildTargetGroup targetGroup { get; set; }
        public BuildTarget target { get; set; }
        public BuildOptions options { get; set; }
        public string[] extraScriptingDefines { get; set; }
    }

    [Flags]
    public enum BuildOptions
    {
        None = 0,
        Development = 1,
        AutoRunPlayer = 4,
        ShowBuiltPlayer = 8,
        BuildAdditionalStreamedScenes = 16,
        AcceptExternalModificationsToPlayer = 32,
        ConnectWithProfiler = 256,
        AllowDebugging = 512,
        SymlinkSources = 1024,
        UncompressedAssetBundle = 2048,
        ConnectToHost = 4096,
        EnableHeadlessMode = 16384,
        BuildScriptsOnly = 32768,
        Il2CPP = 65536,
        ForceEnableAssertions = 262144,
        CompressWithLz4 = 524288,
        StrictMode = 1048576,
        IncludeTestAssemblies = 16777216,
        NoUniqueIdentifier = 33554432,
        WaitForPlayerConnection = 134217728,
    }

    public enum BuildTarget
    {
        NoTarget = -2, Android = 13, iOSSimulatorSDK = -1, iOS = 9, StandaloneLinux64 = 24,
        StandaloneOSX = 2, StandaloneWindows = 5, StandaloneWindows64 = 19, WebGL = 20, Unknown = -1,
    }

    public enum BuildTargetGroup
    {
        Unknown = 0, Standalone = 1, WebGL = 13, iOS = 4, PS4 = 11, PS5 = 21,
        Android = 7, WindowsStandalone = 203, LinuxStandalone = 202, OSXStandalone = 201,
    }

    public class EditorBuildSettingsScene
    {
        public EditorBuildSettingsScene() { }
        public EditorBuildSettingsScene(string path, bool enabled) { }
        public EditorBuildSettingsScene(Guid guid, bool enabled) { }
        public bool enabled { get; set; }
        public string path { get; set; }
        public Guid guid => throw new NotImplementedException();
    }

    public static class EditorBuildSettings
    {
        public static EditorBuildSettingsScene[] scenes { get; set; }
        public static bool AddConfigObject(string name, UnityEngine.Object obj, bool overwriteIfExists) => throw new NotImplementedException();
        public static UnityEngine.Object GetConfigObject(string name) => throw new NotImplementedException();
        public static bool RemoveConfigObject(string name) => throw new NotImplementedException();
    }

    public static class PlayerSettings
    {
        public static string productName { get; set; }
        public static string companyName { get; set; }
        public static string bundleVersion { get; set; }
        public static UnityEngine.ColorSpace colorSpace { get; set; }
        public static UnityEngine.UIOrientation defaultInterfaceOrientation { get; set; }
        public static bool allowedAutorotateToLandscapeLeft { get; set; }
        public static bool allowedAutorotateToLandscapeRight { get; set; }
        public static bool allowedAutorotateToPortrait { get; set; }
        public static bool allowedAutorotateToPortraitUpsideDown { get; set; }
        public static bool use32BitDisplayBuffer { get; set; }
        public static bool runInBackground { get; set; }
        public static UnityEngine.Rendering.GraphicsDeviceType[] GetGraphicsAPIs(BuildTarget platform) => throw new NotImplementedException();
        public static void SetGraphicsAPIs(BuildTarget platform, UnityEngine.Rendering.GraphicsDeviceType[] apis) => throw new NotImplementedException();
        public static void SetScriptingBackend(Build.NamedBuildTarget target, ScriptingImplementation backend) => throw new NotImplementedException();
        public static ScriptingImplementation GetScriptingBackend(Build.NamedBuildTarget target) => throw new NotImplementedException();
        public static void SetIl2CppCompilerConfiguration(Build.NamedBuildTarget target, Build.Il2CppCompilerConfiguration configuration) => throw new NotImplementedException();
        public static string GetApplicationIdentifier(Build.NamedBuildTarget target) => throw new NotImplementedException();
        public static void SetApplicationIdentifier(Build.NamedBuildTarget target, string identifier) => throw new NotImplementedException();
        public static string[] GetScriptingDefineSymbols(Build.NamedBuildTarget target) => throw new NotImplementedException();
        public static void SetScriptingDefineSymbols(Build.NamedBuildTarget target, string[] defines) => throw new NotImplementedException();
        public static int AndroidBundleVersionCode => throw new NotImplementedException();

        public static class Android
        {
            public static AndroidArchitecture targetArchitectures { get; set; }
            public static bool useCustomKeystore { get; set; }
            public static string keystoreName { get; set; }
            public static string keystorePass { get; set; }
            public static string keyaliasName { get; set; }
            public static string keyaliasPass { get; set; }
            public static bool buildApkPerCpuArchitecture { get; set; }
            public static AndroidSdkVersions minSdkVersion { get; set; }
            public static AndroidSdkVersions targetSdkVersion { get; set; }
            public static int bundleVersionCode { get; set; }
            public static string targetSdkVersionString => throw new NotImplementedException();
            public static bool forceInternetPermission { get; set; }
            public static bool forceSDCardPermission { get; set; }
            public static AndroidPreferredInstallLocation preferredInstallLocation { get; set; }
            public static bool useLowAccuracyLocation { get; set; }
            public static bool androidIsGame { get; set; }
            public static AndroidGamePadSupportLevel gamePadSupportLevel { get; set; }
            public static bool disableDepthAndStencilBuffers { get; set; }
            public static bool startInFullscreen { get; set; }
            public static bool renderOutsideSafeArea { get; set; }
            public static bool useSwappy { get; set; }
            public static bool optimizedFramePacing { get; set; }
            public static bool blitType => throw new NotImplementedException();
        }

        public static class iOS
        {
            public static string buildNumber { get; set; }
            public static string targetOSVersionString { get; set; }
            public static iOSDeviceRequirement targetDevice { get; set; }
            public static bool requiresFullScreen { get; set; }
            public static bool allowHTTPDownload { get; set; }
            public static string cameraUsageDescription { get; set; }
            public static string locationUsageDescription { get; set; }
        }
    }

    public enum AndroidArchitecture { None = 0, ARMv7 = 1, ARM64 = 2, X86 = 4, X86_64 = 8, All = 15 }
    public enum AndroidSdkVersions { AndroidApiLevelAuto = 0, AndroidApiLevel22 = 22, AndroidApiLevel23 = 23, AndroidApiLevel24 = 24, AndroidApiLevel25 = 25, AndroidApiLevel26 = 26, AndroidApiLevel27 = 27, AndroidApiLevel28 = 28, AndroidApiLevel29 = 29, AndroidApiLevel30 = 30, AndroidApiLevel31 = 31, AndroidApiLevel32 = 32, AndroidApiLevel33 = 33, AndroidApiLevel34 = 34, AndroidApiLevel35 = 35, AndroidApiLevel36 = 36 }
    public enum AndroidPreferredInstallLocation { Auto = 0, PreferExternal = 1, ForceInternal = 2 }
    public enum AndroidGamePadSupportLevel { WorksWithStandardGamePad = 0, SupportsGamePad = 1, RequiresGamePad = 2 }
    public enum iOSDeviceRequirement { iPhoneOnly = 0, iPadOnly = 1, iPhoneAndiPad = 2 }
    public enum ScriptingImplementation { Mono2x = 0, WinRTDotNET = 1, IL2CPP = 2, IL2CPPSupport = 3 }
    public enum PlayerSettingsArchitecture { None = 0, ARM64 = 1, X64 = 2 }
}

namespace UnityEngine.Rendering
{
    public enum GraphicsDeviceType
    {
        OpenGL2 = 0, Direct3D9 = 1, Direct3D11 = 2, PlayStation3 = 3, Null = 4, Xbox360 = 6, OpenGLES2 = 8,
        OpenGLES3 = 11, PlayStationVita = 12, PlayStation4 = 13, XboxOne = 14, PlayStationMobile = 15,
        Metal = 16, OpenGLCore = 17, Direct3D12 = 18, NintendoSwitch = 19, Vulkan = 21, PlayStation5 = 23,
    }
}

namespace UnityEditor.Build
{
    public struct NamedBuildTarget : IEquatable<NamedBuildTarget>
    {
        public static NamedBuildTarget Active => throw new NotImplementedException();
        public static NamedBuildTarget Android => throw new NotImplementedException();
        public static NamedBuildTarget iOS => throw new NotImplementedException();
        public static NamedBuildTarget Standalone => throw new NotImplementedException();
        public static NamedBuildTarget Server => throw new NotImplementedException();
        public static NamedBuildTarget WebGL => throw new NotImplementedException();
        public static NamedBuildTarget WindowsStoreApps => throw new NotImplementedException();
        public static NamedBuildTarget EmbeddedLinux => throw new NotImplementedException();
        public static NamedBuildTarget PS4 => throw new NotImplementedException();
        public static NamedBuildTarget PS5 => throw new NotImplementedException();
        public static NamedBuildTarget XboxOne => throw new NotImplementedException();
        public static NamedBuildTarget XboxSeriesX => throw new NotImplementedException();
        public static NamedBuildTarget Switch => throw new NotImplementedException();
        public static NamedBuildTarget QNX => throw new NotImplementedException();
        public static NamedBuildTarget unknownPlatform => throw new NotImplementedException();
        public string TargetName => throw new NotImplementedException();
        public UnityEditor.BuildTargetGroup ToBuildTargetGroup() => throw new NotImplementedException();
        public UnityEditor.BuildTarget ToBuildTarget() => throw new NotImplementedException();
        public static NamedBuildTarget FromBuildTargetGroup(UnityEditor.BuildTargetGroup targetGroup) => throw new NotImplementedException();
        public bool Equals(NamedBuildTarget other) => throw new NotImplementedException();
    }

    public enum Il2CppCompilerConfiguration { Debug = 0, Release = 1, Master = 2 }

    public class BuildFailedException : Exception
    {
        public BuildFailedException(string message) : base(message) { }
        public BuildFailedException(string message, Exception innerException) : base(message, innerException) { }
        public BuildFailedException(Exception innerException) : base(string.Empty, innerException) { }
    }

    public interface IProcessSceneWithReport { void OnProcessScene(UnityEngine.SceneManagement.Scene scene, Reporting.BuildReport report); }
    public interface IPreprocessBuildWithReport { int callbackOrder { get; } void OnPreprocessBuild(Reporting.BuildReport report); }
    public interface IPostprocessBuildWithReport { int callbackOrder { get; } void OnPostprocessBuild(Reporting.BuildReport report); }

    [AttributeUsage(AttributeTargets.Class)]
    public class BuildPlayerProcessor : Attribute { }
}

namespace UnityEditor.Build.Reporting
{
    public class BuildReport : UnityEngine.ScriptableObject
    {
        public BuildSummary summary => throw new NotImplementedException();
        public BuildFile[] files => throw new NotImplementedException();
        public BuildStep[] steps => throw new NotImplementedException();
        public BuildOptions options => throw new NotImplementedException();
        public UnityEngine.Object[] packedAssets => throw new NotImplementedException();
        public string name { get; set; }
    }

    public struct BuildSummary
    {
        public BuildResult result { get; set; }
        public ulong totalSize { get; set; }
        public int totalErrors { get; set; }
        public int totalWarnings { get; set; }
        public ulong buildStartedAt { get; set; }
        public ulong buildEndedAt { get; set; }
        public TimeSpan totalTime => throw new NotImplementedException();
        public TimeSpan duration => throw new NotImplementedException();
        public string outputPath { get; set; }
        public string platform => throw new NotImplementedException();
        public Guid guid => throw new NotImplementedException();
        public BuildTarget platformGroup => throw new NotImplementedException();
        public UnityEngine.RuntimePlatform platformGroup2 => throw new NotImplementedException();
        public BuildOptions options { get; set; }
    }

    public enum BuildResult { Unknown = 0, Succeeded = 1, Failed = 2, Cancelled = 3 }

    public class BuildFile
    {
        public string path => throw new NotImplementedException();
        public string id => throw new NotImplementedException();
        public ulong size => throw new NotImplementedException();
        public BuildFileRole role => throw new NotImplementedException();
    }

    public enum BuildFileRole { Scene = 0, AssetBundle = 1, Resource = 2, Other = 3 }

    public class BuildStep
    {
        public string name => throw new NotImplementedException();
        public TimeSpan duration => throw new NotImplementedException();
        public string[] messages => throw new NotImplementedException();
        public int depth => throw new NotImplementedException();
    }
}

namespace UnityEditor.SceneManagement
{
    public enum NewSceneSetup { EmptyScene = 0, DefaultGameObjects = 1 }
    public enum NewSceneMode { Single = 0, Additive = 1 }
    public enum OpenSceneMode { Single = 0, Additive = 1, AdditiveWithoutLoading = 2 }

    public class SceneSetup
    {
        public string path { get; set; }
        public bool isLoaded { get; set; }
        public bool isActive { get; set; }
    }

    public static class EditorSceneManager
    {
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup) => throw new NotImplementedException();
        public static UnityEngine.SceneManagement.Scene NewScene(NewSceneSetup setup, NewSceneMode mode) => throw new NotImplementedException();
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene) => throw new NotImplementedException();
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath) => throw new NotImplementedException();
        public static bool SaveScene(UnityEngine.SceneManagement.Scene scene, string dstScenePath, bool saveAsCopy) => throw new NotImplementedException();
        public static bool SaveOpenScenes() => throw new NotImplementedException();
        public static bool SaveCurrentModifiedScenesIfUserWantsTo() => throw new NotImplementedException();
        public static bool SaveModifiedScenesIfUserWantsTo(UnityEngine.SceneManagement.Scene[] scenes) => throw new NotImplementedException();
        public static UnityEngine.SceneManagement.Scene GetActiveScene() => throw new NotImplementedException();
        public static bool SetActiveScene(UnityEngine.SceneManagement.Scene scene) => throw new NotImplementedException();
        public static UnityEngine.SceneManagement.Scene OpenScene(string scenePath) => throw new NotImplementedException();
        public static UnityEngine.SceneManagement.Scene OpenScene(string scenePath, OpenSceneMode mode) => throw new NotImplementedException();
        public static UnityEngine.SceneManagement.Scene CloseScene(UnityEngine.SceneManagement.Scene scene, bool removeScene) => throw new NotImplementedException();
        public static UnityEngine.SceneManagement.Scene[] GetAllScenes() => throw new NotImplementedException();
        public static SceneSetup[] GetSceneManagerSetup() => throw new NotImplementedException();
        public static void RestoreSceneManagerSetup(SceneSetup[] setup) => throw new NotImplementedException();
        public static void MarkSceneDirty(UnityEngine.SceneManagement.Scene scene) => throw new NotImplementedException();
        public static bool EnsureUntitledSceneHasBeenSaved(string dialogContent) => throw new NotImplementedException();
    }

    public static class PrefabStageUtility { }
}
