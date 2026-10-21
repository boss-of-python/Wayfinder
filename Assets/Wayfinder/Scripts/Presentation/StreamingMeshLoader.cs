using UnityEngine;
using Wayfinder.Campaign;
using Wayfinder.Platform;
using Wayfinder.World;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Presentation-layer facade over <see cref="StreamingMeshRuntime"/> for loading verified 3D streaming
    /// assets (<c>.obj</c> / pre-baked <see cref="Mesh"/>) in UI previews, drivers, and world scenes (§14.2, §14.3).
    /// </summary>
    public static class StreamingMeshLoader
    {
        public static void ClearCache() => StreamingMeshRuntime.ClearCache();

        public static Mesh ToUnityMesh(string assetId, ObjMeshData data) =>
            StreamingMeshRuntime.ToUnityMesh(assetId, data);

        public static string LoadObjText(StreamingAssetDefinition def, string streamingRootOverride = null) =>
            StreamingMeshRuntime.LoadObjText(def, streamingRootOverride);

        public static Mesh LoadMesh(StreamingAssetDefinition def, string streamingRootOverride = null) =>
            StreamingMeshRuntime.LoadMesh(def, streamingRootOverride);

        public static Mesh LoadMesh(string assetId, CampaignContent content = null, string streamingRootOverride = null)
        {
            var catalog = content != null ? content.StreamingAssets : StreamingMeshRuntime.LoadDefaultCatalog();
            return StreamingMeshRuntime.LoadMesh(catalog[assetId], streamingRootOverride);
        }

        public static GameObject InstantiateModel(
            StreamingAssetDefinition def,
            Material material,
            Transform parent = null,
            bool addBoxCollider = false) =>
            StreamingMeshRuntime.InstantiateModel(def, material, parent, addBoxCollider);
    }
}
