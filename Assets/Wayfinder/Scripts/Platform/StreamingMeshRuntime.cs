using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using Wayfinder.Campaign;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Shared Unity runtime &amp; editor mesh loader and vehicle/world model binder for the 30 verified
    /// 3D streaming assets (§14.2, §14.3). Lives in <c>Wayfinder.Platform</c> so <see cref="WorldStreamer"/>,
    /// <c>Wayfinder.Presentation</c>, and <c>Wayfinder.Editor</c> all share one single cached mesh pipeline
    /// with zero duplicate geometry in memory and zero circular assembly dependencies.
    /// </summary>
    public static class StreamingMeshRuntime
    {
        public const string BakedMeshesResourcesSubfolder = StreamingAssetCatalog.ResourcesSubfolder + "/Meshes";

        static readonly Dictionary<string, Mesh> Cache = new Dictionary<string, Mesh>(StringComparer.Ordinal);
        static CampaignContent _cachedContent;

        public static void ClearCache() => Cache.Clear();

        /// <summary>Resolves the canonical streaming asset catalog at runtime (cached after first load).</summary>
        public static IReadOnlyDictionary<string, StreamingAssetDefinition> LoadDefaultCatalog()
        {
            if (_cachedContent != null)
                return _cachedContent.StreamingAssets;

            var files = new Dictionary<string, string>(StringComparer.Ordinal);
            bool loadedFromResources = true;
            foreach (string name in CampaignContent.RequiredFiles)
            {
                var asset = Resources.Load<TextAsset>(CampaignHost.ResourcesFolder + "/" + Path.GetFileNameWithoutExtension(name));
                if (asset == null)
                {
                    loadedFromResources = false;
                    break;
                }
                files[name] = asset.text;
            }

            _cachedContent = loadedFromResources
                ? CampaignContent.FromJson(files)
                : CampaignContent.FromDirectory("Assets/Wayfinder/Content");
            return _cachedContent.StreamingAssets;
        }

        /// <summary>Converts parsed <see cref="ObjMeshData"/> directly into a left-handed CW Unity <see cref="Mesh"/>.</summary>
        public static Mesh ToUnityMesh(string assetId, ObjMeshData data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            var flat = data.ToFlattenedBuffers(clockwiseWinding: true);
            int vertexCount = flat.Positions.Length;

            var vertices = new Vector3[vertexCount];
            var normals = new Vector3[vertexCount];
            var uvs = new Vector2[vertexCount];

            for (int i = 0; i < vertexCount; i++)
            {
                var p = flat.Positions[i];
                var n = flat.Normals[i];
                var t = flat.Uvs[i];
                vertices[i] = new Vector3(p.x, p.y, p.z);
                normals[i] = new Vector3(n.x, n.y, n.z);
                uvs[i] = new Vector2(t.u, t.v);
            }

            var mesh = new Mesh
            {
                name = string.IsNullOrEmpty(assetId) ? flat.ObjectName : assetId,
                indexFormat = vertexCount > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16
            };
            mesh.vertices = vertices;
            mesh.normals = normals;
            mesh.uv = uvs;
            mesh.triangles = flat.Triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>Loads verified OBJ text for <paramref name="def"/> from Resources or StreamingAssets.</summary>
        public static string LoadObjText(StreamingAssetDefinition def, string streamingRootOverride = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));

            string resourceKey = StreamingAssetCatalog.ResourceStemFor(def);
            if (string.IsNullOrEmpty(streamingRootOverride))
            {
                var resourceAsset = Resources.Load<TextAsset>(resourceKey);
                if (resourceAsset != null && !string.IsNullOrEmpty(resourceAsset.text))
                    return resourceAsset.text;
            }

            string root = !string.IsNullOrEmpty(streamingRootOverride)
                ? streamingRootOverride
                : Application.streamingAssetsPath;
            string diskPath = Path.Combine(root, def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            if (File.Exists(diskPath))
                return File.ReadAllText(diskPath);

            throw new FileNotFoundException(
                $"3D streaming asset '{def.Id}' not found at Resources/{resourceKey} or '{diskPath}'.",
                diskPath);
        }

        /// <summary>
        /// Loads, verifies (if parsed at runtime), and caches the Unity <see cref="Mesh"/> for <paramref name="def"/>.
        /// Prefers pre-baked native Unity <see cref="Mesh"/> assets in <c>Resources/StreamingModels/Meshes/</c>
        /// when present so runtime loads cost zero CPU parsing on Android ARM64.
        /// </summary>
        public static Mesh LoadMesh(StreamingAssetDefinition def, string streamingRootOverride = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (Cache.TryGetValue(def.Id, out var cached) && cached != null)
                return cached;

            if (string.IsNullOrEmpty(streamingRootOverride))
            {
                string stem = Path.GetFileNameWithoutExtension(def.RelativePath);
                var prebaked = Resources.Load<Mesh>(BakedMeshesResourcesSubfolder + "/" + stem);
                if (prebaked != null)
                {
                    Cache[def.Id] = prebaked;
                    return prebaked;
                }
            }

            string text = LoadObjText(def, streamingRootOverride);
            var parsed = StreamingAssetCatalog.VerifyAssetContent(def, text);

            var mesh = ToUnityMesh(def.Id, parsed);
            Cache[def.Id] = mesh;
            return mesh;
        }

        /// <summary>Loads a verified <see cref="Mesh"/> by canonical <paramref name="assetId"/>.</summary>
        public static Mesh LoadMeshById(
            string assetId,
            IReadOnlyDictionary<string, StreamingAssetDefinition> catalog = null,
            Func<StreamingAssetDefinition, Mesh> meshResolver = null)
        {
            var map = catalog ?? LoadDefaultCatalog();
            if (!map.TryGetValue(assetId, out var def))
                throw new KeyNotFoundException($"Unknown 3D streaming asset id '{assetId}'.");
            return meshResolver != null ? meshResolver(def) : LoadMesh(def);
        }

        /// <summary>
        /// Instantiates a GameObject bearing a verified 3D streaming <see cref="Mesh"/>, URP material,
        /// and optional bounding <see cref="BoxCollider"/> derived directly from the asset's verified bounds.
        /// </summary>
        public static GameObject InstantiateModel(
            StreamingAssetDefinition def,
            Material material,
            Transform parent = null,
            bool addBoxCollider = false,
            bool triggerCollider = false,
            Func<StreamingAssetDefinition, Mesh> meshResolver = null)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            Mesh mesh = meshResolver != null ? meshResolver(def) : LoadMesh(def);
            var go = new GameObject(def.Id);
            if (parent != null)
                go.transform.SetParent(parent, worldPositionStays: false);

            var filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;

            if (addBoxCollider || triggerCollider)
                AttachBoundsCollider(go, def, triggerCollider);

            return go;
        }

        /// <summary>Attaches a <see cref="BoxCollider"/> sized directly to <paramref name="def"/>'s verified AABB.</summary>
        public static BoxCollider AttachBoundsCollider(GameObject go, StreamingAssetDefinition def, bool isTrigger = false)
        {
            var col = go.GetComponent<BoxCollider>();
            if (col == null) col = go.AddComponent<BoxCollider>();
            Vector3 min = new Vector3(def.BoundsMin.x, def.BoundsMin.y, def.BoundsMin.z);
            Vector3 max = new Vector3(def.BoundsMax.x, def.BoundsMax.y, def.BoundsMax.z);
            col.center = (min + max) * 0.5f;
            Vector3 size = max - min;
            col.size = new Vector3(
                Mathf.Max(0.25f, size.x),
                Mathf.Max(0.25f, size.y),
                Mathf.Max(0.25f, size.z));
            col.isTrigger = isTrigger;
            return col;
        }

        /// <summary>
        /// Attaches or updates the live 3D chassis mesh, all 4 axle-centered spinning/steering 3D wheel meshes,
        /// and the removable roof/bed cargo pallet stack for any of the 4 playable vehicles
        /// (<c>vehicle.scout.s1</c>, <c>vehicle.mule.m2</c>, <c>vehicle.lynx.l3</c>, <c>vehicle.atlas.a4</c>).
        /// </summary>
        public static void ApplyVehicleVisuals(
            VehicleController vehicle,
            string vehicleId,
            bool showCargoStack,
            Material chassisMaterial = null,
            Material wheelMaterial = null,
            Material cargoMaterial = null,
            IReadOnlyDictionary<string, StreamingAssetDefinition> catalog = null,
            Func<StreamingAssetDefinition, Mesh> meshResolver = null)
        {
            if (vehicle == null) return;
            var map = catalog ?? LoadDefaultCatalog();

            string chassisId = StreamingAssetBindings.ChassisForVehicle(vehicleId);
            string wheelId = StreamingAssetBindings.WheelForVehicle(vehicleId);

            Mesh chassisMesh = LoadMeshById(chassisId, map, meshResolver);
            Mesh wheelMesh = LoadMeshById(wheelId, map, meshResolver);
            Mesh cargoMesh = LoadMeshById(StreamingAssetBindings.CargoPalletStackAssetId, map, meshResolver);

            var chassisTf = vehicle.transform.Find("Chassis Mesh");
            if (chassisTf == null)
            {
                var go = new GameObject("Chassis Mesh") { layer = vehicle.gameObject.layer };
                go.transform.SetParent(vehicle.transform, worldPositionStays: false);
                chassisTf = go.transform;
            }
            chassisTf.localPosition = new Vector3(0f, -0.35f, 0f);
            chassisTf.localRotation = Quaternion.identity;
            chassisTf.localScale = Vector3.one;
            EnsureMeshVisual(chassisTf.gameObject, chassisMesh, chassisMaterial);

            BindWheelVisual(vehicle.wheelVisualFrontLeft, wheelMesh, wheelMaterial, vehicle.gameObject.layer);
            BindWheelVisual(vehicle.wheelVisualFrontRight, wheelMesh, wheelMaterial, vehicle.gameObject.layer);
            BindWheelVisual(vehicle.wheelVisualRearLeft, wheelMesh, wheelMaterial, vehicle.gameObject.layer);
            BindWheelVisual(vehicle.wheelVisualRearRight, wheelMesh, wheelMaterial, vehicle.gameObject.layer);

            var cargoTf = vehicle.transform.Find("Cargo Load Mesh");
            if (cargoTf == null)
            {
                var go = new GameObject("Cargo Load Mesh") { layer = vehicle.gameObject.layer };
                go.transform.SetParent(vehicle.transform, worldPositionStays: false);
                cargoTf = go.transform;
            }
            var (cx, cy, cz) = StreamingAssetBindings.CargoMountOffsetForVehicle(vehicleId);
            cargoTf.localPosition = new Vector3(cx, cy, cz);
            cargoTf.localRotation = Quaternion.identity;
            cargoTf.localScale = new Vector3(0.72f, 0.72f, 0.72f);
            EnsureMeshVisual(cargoTf.gameObject, cargoMesh, cargoMaterial != null ? cargoMaterial : chassisMaterial);
            cargoTf.gameObject.SetActive(showCargoStack);
        }

        static void BindWheelVisual(Transform wheelRoot, Mesh wheelMesh, Material wheelMaterial, int layer)
        {
            if (wheelRoot == null) return;
            var meshTf = wheelRoot.Find("Wheel Mesh");
            if (meshTf == null)
            {
                var go = new GameObject("Wheel Mesh") { layer = layer };
                go.transform.SetParent(wheelRoot, worldPositionStays: false);
                meshTf = go.transform;
            }
            meshTf.localPosition = Vector3.zero;
            meshTf.localRotation = Quaternion.identity;
            meshTf.localScale = Vector3.one;
            EnsureMeshVisual(meshTf.gameObject, wheelMesh, wheelMaterial);
        }

        static void EnsureMeshVisual(GameObject go, Mesh mesh, Material material)
        {
            var filter = go.GetComponent<MeshFilter>();
            if (filter == null) filter = go.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;

            var renderer = go.GetComponent<MeshRenderer>();
            if (renderer == null) renderer = go.AddComponent<MeshRenderer>();
            if (material != null)
                renderer.sharedMaterial = material;
            else if (renderer.sharedMaterial == null)
                renderer.sharedMaterial = DefaultRuntimeMaterial();
        }

        static Material _fallbackMaterial;
        static Material DefaultRuntimeMaterial()
        {
            if (_fallbackMaterial != null) return _fallbackMaterial;
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit") ?? Shader.Find("Standard");
            if (shader != null)
                _fallbackMaterial = new Material(shader) { name = "StreamingAssetFallback" };
            return _fallbackMaterial;
        }
    }
}
