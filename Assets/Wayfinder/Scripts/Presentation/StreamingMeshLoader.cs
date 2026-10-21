using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using Wayfinder.Campaign;
using Wayfinder.Platform;
using Wayfinder.World;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Runtime and Editor loader that converts verified Wavefront <c>.obj</c> 3D streaming assets
    /// (<see cref="StreamingAssetCatalog"/>) into ready-to-render <see cref="Mesh"/> and
    /// <see cref="GameObject"/> instances (§14.2, §14.3).
    ///
    /// On Android/iOS player builds (where <c>Application.streamingAssetsPath</c> is inside a compressed
    /// archive), loads the <see cref="TextAsset"/> mirrored into <c>Resources/StreamingModels/</c> by
    /// <c>ProjectSetup.VerifyStreamingAssets()</c>. In the Editor or desktop standalone, falls back to
    /// reading <c>Assets/StreamingAssets/Models/*.obj</c> directly from disk.
    /// </summary>
    public static class StreamingMeshLoader
    {
        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>(StringComparer.Ordinal);

        /// <summary>Clears the runtime mesh cache (e.g. between scene rebuilds or tests).</summary>
        public static void ClearCache() => Cache.Clear();

        /// <summary>
        /// Converts engine-free <see cref="ObjMeshData"/> into a <see cref="Mesh"/> with positions,
        /// UVs, outward normals, and left-handed clockwise triangle winding.
        /// </summary>
        public static Mesh ToUnityMesh(ObjMeshData meshData)
        {
            if (meshData == null) throw new ArgumentNullException(nameof(meshData));
            FlattenedMeshBuffers flat = meshData.ToFlattenedBuffers(clockwiseWinding: true);

            var vertices = new Vector3[flat.Positions.Length];
            var uvs = new Vector2[flat.Uvs.Length];
            var normals = new Vector3[flat.Normals.Length];

            for (int i = 0; i < flat.Positions.Length; i++)
            {
                var p = flat.Positions[i];
                var uv = flat.Uvs[i];
                var n = flat.Normals[i];
                vertices[i] = new Vector3(p.x, p.y, p.z);
                uvs[i] = new Vector2(uv.u, uv.v);
                normals[i] = new Vector3(n.x, n.y, n.z);
            }

            var mesh = new Mesh { name = meshData.ObjectName };
            if (vertices.Length > 65535)
                mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.uv = uvs;
            mesh.normals = normals;
            mesh.triangles = flat.Triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>
        /// Resolves the raw UTF-8 <c>.obj</c> text for <paramref name="def"/> from either the built
        /// <c>Resources/StreamingModels/</c> mirror (Android/iOS safe) or <paramref name="streamingAssetsRoot"/> on disk.
        /// </summary>
        public static string LoadObjText(StreamingAssetDefinition def, string streamingAssetsRoot = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            string resourceStem = StreamingAssetCatalog.ResourceStemFor(def);
            var textAsset = Resources.Load<TextAsset>(resourceStem);
            if (textAsset != null && !string.IsNullOrEmpty(textAsset.text))
                return textAsset.text;

            string root = !string.IsNullOrEmpty(streamingAssetsRoot)
                ? streamingAssetsRoot
                : StreamingAssetCatalog.DefaultStreamingAssetsRoot;
            string fullPath = Path.Combine(root, def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(fullPath))
                return File.ReadAllText(fullPath, Encoding.UTF8);

            throw new FileNotFoundException(
                $"3D streaming asset '{def.Id}' not found in Resources ('{resourceStem}') or on disk ('{fullPath}')",
                fullPath);
        }

        /// <summary>
        /// Loads, SHA-256-verifies, caches, and returns the <see cref="Mesh"/> for <paramref name="assetId"/>.
        /// </summary>
        public static Mesh LoadMesh(string assetId, CampaignContent content = null, string streamingAssetsRoot = null)
        {
            if (string.IsNullOrEmpty(assetId))
                throw new ArgumentException("assetId is required", nameof(assetId));

            if (Cache.TryGetValue(assetId, out var cached) && cached != null)
                return cached;

            CampaignContent resolvedContent = content ?? CampaignHost.Instance?.Content;
            if (resolvedContent == null)
                throw new InvalidOperationException("CampaignContent is required to resolve 3D streaming asset metadata.");

            if (!resolvedContent.StreamingAssets.TryGetValue(assetId, out var def) || def == null)
                throw new KeyNotFoundException($"3D streaming asset '{assetId}' is not registered in streaming_assets.json.");

            string objText = LoadObjText(def, streamingAssetsRoot);
            ObjMeshData verified = StreamingAssetCatalog.VerifyAssetContent(def, objText);
            Mesh mesh = ToUnityMesh(verified);
            Cache[assetId] = mesh;
            return mesh;
        }

        /// <summary>
        /// Instantiates a <see cref="GameObject"/> with <see cref="MeshFilter"/> and <see cref="MeshRenderer"/>
        /// populated with the verified 3D streaming asset <paramref name="assetId"/>.
        /// </summary>
        public static GameObject InstantiateModel(
            string assetId,
            Transform parent,
            Vector3 position,
            Quaternion rotation,
            Material material = null,
            CampaignContent content = null,
            string streamingAssetsRoot = null)
        {
            Mesh mesh = LoadMesh(assetId, content, streamingAssetsRoot);
            var go = new GameObject(assetId);
            if (parent != null) go.transform.SetParent(parent, worldPositionStays: false);
            go.transform.position = position;
            go.transform.rotation = rotation;

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;

            return go;
        }
    }
}
