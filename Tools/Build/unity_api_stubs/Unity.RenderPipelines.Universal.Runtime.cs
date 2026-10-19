// ---------------------------------------------------------------------------------------------
// Unity.RenderPipelines.Universal.Runtime (STUB) — com.unity.render-pipelines.universal 17.3.0
//
// `UniversalRenderPipelineAsset.Create(ScriptableRendererData)` is public in URP 17 (verified
// against Unity-Technologies/Graphics, Packages/com.unity.render-pipelines.universal, 17.6.0).
// ---------------------------------------------------------------------------------------------

using System;

namespace UnityEngine.Rendering.Universal
{
    public abstract class ScriptableRendererData : UnityEngine.ScriptableObject
    {
        public bool postProcessData => throw new NotImplementedException();
        protected abstract UnityEngine.Rendering.Universal.ScriptableRenderer Create();
        internal virtual UnityEngine.Rendering.Universal.ScriptableRenderer GetRenderer() => throw new NotImplementedException();
    }

    public abstract class ScriptableRenderer
    {
        public abstract void Render(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera[] cameras);
    }

    public class UniversalRendererData : ScriptableRendererData
    {
        public bool postProcessEnabled { get; set; }
        public RenderingMode renderingMode { get; set; }
        public bool depthPrimingMode => throw new NotImplementedException();
        protected override ScriptableRenderer Create() => throw new NotImplementedException();
        public enum RenderingMode { Forward = 0, Deferred = 1, ForwardPlus = 2 }
    }

    public class UniversalRenderPipelineAsset : UnityEngine.Rendering.RenderPipelineAsset
    {
        public string assetVersion => throw new NotImplementedException();
        public string packageVersion => throw new NotImplementedException();
        public bool supportsTerrainHoles => throw new NotImplementedException();
        public bool supportsHDR { get; set; }
        public int msaaSampleCount { get; set; }
        public float renderScale { get; set; }
        public UpscalingFilterSelection upscalingFilter { get; set; }
        public float shadowDistance { get; set; }
        public int shadowCascadeCount { get; set; }
        public bool useSRPBatcher { get; set; }
        public bool supportsDynamicBatching { get; set; }
        public bool supportsMainLightShadows { get; set; }
        public bool supportsAdditionalLightShadows { get; set; }
        public bool supportsSoftShadows { get; set; }
        public bool supportsMixedLighting { get; set; }
        public bool useAdaptivePerformance { get; set; }
        public bool colorGradingMode => throw new NotImplementedException();
        public int colorGradingLutSize { get; set; }
        public bool useRenderGraph { get; set; }
        public static UniversalRenderPipelineAsset Create(ScriptableRendererData rendererData = null) =>
            throw new NotImplementedException();
        public enum UpscalingFilterSelection { Auto = 0, Linear = 1, Point = 2, FSR = 3, STP = 4, TAA = 5 }
    }

    public class UniversalRenderPipeline : UnityEngine.Rendering.RenderPipeline
    {
        public static UniversalRenderPipelineAsset asset => throw new NotImplementedException();
        protected override void Render(UnityEngine.Rendering.ScriptableRenderContext context, UnityEngine.Camera[] cameras) =>
            throw new NotImplementedException();
    }
}
