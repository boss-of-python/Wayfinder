using System;
using System.Collections.Generic;
using UnityEngine;
using Wayfinder.Core;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Runtime world chunk streamer (§6.4, §18.3, KI-037). Hosts <see cref="ChunkStreamPlanner"/>
    /// inside a Unity scene, executing its state transitions cleanly (<c>Request → DataReady →
    /// Activate → Active → Deactivate → DataReady/Unloaded</c>), building 3D heightfield chunk
    /// meshes + colliders, placing deterministic biome scatter props, and attaching road-shoulder
    /// <see cref="WinchAnchor"/> components with zero handle leaks.
    /// </summary>
    public sealed class WorldStreamer : MonoBehaviour
    {
        public Transform FollowTarget;
        public Material TerrainMaterial;
        public Material PropMaterial;
        public Material WinchAnchorMaterial;
        public int ActiveSubdivisions = 16;

        public ChunkStreamPlanner Planner { get; private set; }
        public MainlandRouteGraph Routes { get; private set; }
        public WorldScatterSurface ScatterSurface { get; private set; }

        readonly NoiseField _noise = new NoiseField();
        readonly Dictionary<(int x, int z), GameObject> _activeObjects = new Dictionary<(int, int), GameObject>();

        public int LiveChunkObjectCount => _activeObjects.Count;

        public void Initialize(MainlandRouteGraph routes = null, IEnumerable<ReservedSocket> reservedSockets = null)
        {
            Planner = new ChunkStreamPlanner();
            Routes = routes ?? new MainlandRouteGraph();
            ScatterSurface = new WorldScatterSurface(Routes, reservedSockets, _noise);
        }

        void Awake()
        {
            if (Planner == null) Initialize();
        }

        void Update()
        {
            if (FollowTarget == null)
            {
                var vc = FindFirstObjectByType<VehicleController>();
                if (vc != null) FollowTarget = vc.transform;
            }
            if (FollowTarget != null)
            {
                Vector3 v = Vector3.zero;
                var rb = FollowTarget.GetComponent<Rigidbody>();
                if (rb != null) v = rb.linearVelocity;
                StepAt(FollowTarget.position.x, FollowTarget.position.z, v.x, v.z);
            }
        }

        public void StepAt(float worldX, float worldZ, float velX = 0f, float velZ = 0f)
        {
            if (Planner == null) Initialize();
            var commands = Planner.Step(worldX, worldZ, velX, velZ);
            foreach (var cmd in commands)
            {
                switch (cmd.Kind)
                {
                    case StreamCommandKind.Request:
                        Planner.MarkDataReady(cmd.ChunkX, cmd.ChunkZ);
                        break;
                    case StreamCommandKind.Activate:
                        SpawnChunk(cmd.ChunkX, cmd.ChunkZ);
                        Planner.MarkActive(cmd.ChunkX, cmd.ChunkZ);
                        break;
                    case StreamCommandKind.Deactivate:
                        DespawnChunk(cmd.ChunkX, cmd.ChunkZ);
                        Planner.MarkDeactivated(cmd.ChunkX, cmd.ChunkZ, out _);
                        break;
                    case StreamCommandKind.Cancel:
                        DespawnChunk(cmd.ChunkX, cmd.ChunkZ);
                        break;
                }
            }
        }

        void SpawnChunk(int cx, int cz)
        {
            DespawnChunk(cx, cz);
            float originX = cx * WorldConstants.ChunkSizeMeters;
            float originZ = cz * WorldConstants.ChunkSizeMeters;

            var root = new GameObject($"Chunk_{cx}_{cz}");
            root.transform.SetParent(transform, false);
            root.transform.position = new Vector3(originX, 0f, originZ);

            Mesh mesh = BuildChunkMesh(_noise, cx, cz, Mathf.Clamp(ActiveSubdivisions, 8, 64));
            var mf = root.AddComponent<MeshFilter>();
            mf.sharedMesh = mesh;
            var mr = root.AddComponent<MeshRenderer>();
            if (TerrainMaterial != null) mr.sharedMaterial = TerrainMaterial;
            var mc = root.AddComponent<MeshCollider>();
            mc.sharedMesh = mesh;

            var surfaceLabel = root.AddComponent<SurfaceLabel>();
            var biome = WorldHydrology.NearestBiome(originX + 128f, originZ + 128f);
            surfaceLabel.surface = biome switch
            {
                WorldBiome.Wetlands => SurfaceType.ShallowMud,
                WorldBiome.Canyon => SurfaceType.Gravel,
                WorldBiome.City => SurfaceType.BrokenAsphalt,
                _ => SurfaceType.PackedDirt
            };

            var anchors = ScatterSurface.PlaceWinchAnchorsForChunk(cx, cz);
            for (int i = 0; i < anchors.Count; i++)
            {
                var a = anchors[i];
                var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                go.name = $"WinchAnchor_{cx}_{cz}_{i}";
                go.transform.SetParent(root.transform, false);
                go.transform.position = new Vector3(a.X, a.Y + 1.25f, a.Z);
                go.transform.localScale = new Vector3(0.55f, 1.25f, 0.55f);
                go.AddComponent<WinchAnchor>();
                if (WinchAnchorMaterial != null)
                    go.GetComponent<Renderer>().sharedMaterial = WinchAnchorMaterial;
            }

            foreach (var rules in WorldScatterSurface.DefaultBiomeRules(biome))
            {
                var points = ScatterPlacer.PlaceChunk(cx, cz, rules, ScatterSurface);
                for (int i = 0; i < points.Count; i++)
                {
                    var p = points[i];
                    var prop = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                    prop.name = $"{rules.SpeciesId}_{p.CandidateIndex}";
                    prop.transform.SetParent(root.transform, false);
                    prop.transform.position = new Vector3(p.X, p.Y + 1.4f * p.Scale, p.Z);
                    prop.transform.rotation = Quaternion.Euler(0f, p.YawDegrees, 0f);
                    prop.transform.localScale = new Vector3(1.05f * p.Scale, 1.6f * p.Scale, 1.05f * p.Scale);
                    if (PropMaterial != null)
                        prop.GetComponent<Renderer>().sharedMaterial = PropMaterial;
                }
            }

            _activeObjects[(cx, cz)] = root;
        }

        public static Mesh BuildChunkMesh(NoiseField noise, int chunkX, int chunkZ, int quadsPerSide)
        {
            int vertsPerSide = quadsPerSide + 1;
            float step = (float)WorldConstants.ChunkSizeMeters / quadsPerSide;
            float originX = chunkX * WorldConstants.ChunkSizeMeters;
            float originZ = chunkZ * WorldConstants.ChunkSizeMeters;

            var verts = new Vector3[vertsPerSide * vertsPerSide];
            var uvs = new Vector2[vertsPerSide * vertsPerSide];
            var tris = new int[quadsPerSide * quadsPerSide * 6];

            for (int z = 0; z < vertsPerSide; z++)
            for (int x = 0; x < vertsPerSide; x++)
            {
                int idx = z * vertsPerSide + x;
                float lx = x * step, lz = z * step;
                float wy = HeightField.Quantize(noise.BaseHeight(originX + lx, originZ + lz));
                verts[idx] = new Vector3(lx, wy, lz);
                uvs[idx] = new Vector2((float)x / quadsPerSide, (float)z / quadsPerSide);
            }

            int t = 0;
            for (int z = 0; z < quadsPerSide; z++)
            for (int x = 0; x < quadsPerSide; x++)
            {
                int i0 = z * vertsPerSide + x;
                int i1 = i0 + 1;
                int i2 = i0 + vertsPerSide;
                int i3 = i2 + 1;
                tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
            }

            var mesh = new Mesh { name = $"chunk_{chunkX}_{chunkZ}" };
            mesh.vertices = verts;
            mesh.uv = uvs;
            mesh.triangles = tris;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        void DespawnChunk(int cx, int cz)
        {
            if (_activeObjects.TryGetValue((cx, cz), out var go))
            {
                if (go != null) DestroyImmediate(go);
                _activeObjects.Remove((cx, cz));
            }
        }

        void OnDestroy()
        {
            var keys = new List<(int x, int z)>(_activeObjects.Keys);
            foreach (var k in keys) DespawnChunk(k.x, k.z);
        }
    }
}
