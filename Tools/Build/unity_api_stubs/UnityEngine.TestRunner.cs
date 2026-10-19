// ---------------------------------------------------------------------------------------------
// UnityEngine.TestRunner / UnityEditor.TestRunner (STUB) — com.unity.test-framework 1.5.1
// ---------------------------------------------------------------------------------------------

using System;

namespace UnityEngine.TestTools
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class UnityTestAttribute : NUnit.Framework.NUnitAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class UnitySetUpAttribute : NUnit.Framework.NUnitAttribute { }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class UnityTearDownAttribute : NUnit.Framework.NUnitAttribute { }

    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false)]
    public class UnityPlatformAttribute : NUnit.Framework.NUnitAttribute
    {
        public UnityPlatformAttribute() { }
        public UnityPlatformAttribute(params UnityEngine.RuntimePlatform[] include) { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class PrebuildSetupAttribute : NUnit.Framework.NUnitAttribute
    {
        public PrebuildSetupAttribute(Type setupType) { }
        public PrebuildSetupAttribute(string setupTypeName) { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class PostBuildCleanupAttribute : NUnit.Framework.NUnitAttribute
    {
        public PostBuildCleanupAttribute(Type cleanupType) { }
        public PostBuildCleanupAttribute(string cleanupTypeName) { }
    }

    public static class LogAssert
    {
        public static bool ignoreFailingMessages { get; set; }
        public static void NoUnexpectedReceived() => throw new NotImplementedException();
        public static void Expect(UnityEngine.LogType type, string message) => throw new NotImplementedException();
        public static void Ignore(UnityEngine.LogType type, string message) => throw new NotImplementedException();
    }
}

namespace UnityEditor.TestTools
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequiresPlayModeAttribute : NUnit.Framework.NUnitAttribute
    {
        public RequiresPlayModeAttribute() { }
        public RequiresPlayModeAttribute(bool enable) { }
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, AllowMultiple = true)]
    public class RequiresPlatformAttribute : NUnit.Framework.NUnitAttribute
    {
        public RequiresPlatformAttribute(params UnityEditor.BuildTarget[] platforms) { }
    }
}
