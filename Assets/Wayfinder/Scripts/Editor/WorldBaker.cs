using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Wayfinder.Core;
using Wayfinder.World;

namespace Wayfinder.Editor
{
    /// <summary>
    /// World bake pipeline (§7.2): macro heightfield → hydrology/road carve hooks → POI
    /// foundations → surface pass → chunk mesh bake → seam validation → report.
    /// Deterministic from seed 481516 / generator version 1 (§7.1).
    /// Menu: Tools → Wayfinder → Bake World (Greybox).
    /// </summary>
    public static class WorldBaker
    {
        public const string BakeOutputDir = "Assets/Wayfinder/Art/Generated/World";

        [MenuItem("Tools/Wayfinder/Bake World (Greybox)")]
        public static void Bake()
        {
            Directory.CreateDirectory(BakeOutputDir);
            Directory.CreateDirectory("Logs");

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            var noise = new NoiseField(WorldConstants.CanonicalWorldSeed, WorldConstants.GeneratorVersion);
            var field = new HeightField(noise);
            field.Bake();

            int chunks = WorldConstants.ChunksPerSide;
            int seamFailures = 0;
            float worstSeam = 0f;
            var report = new StringBuilder();
            report.AppendLine("{");
            report.AppendLine($"  \"worldSeed\": {WorldConstants.CanonicalWorldSeed},");
            report.AppendLine($"  \"generatorVersion\": {WorldConstants.GeneratorVersion},");
            report.AppendLine($"  \"chunksPerSide\": {chunks},");
            report.AppendLine($"  \"terrainTileResolution\": {WorldConstants.TerrainTileResolution},");

            // Seam validation (§7.4): shared borders must agree within 1 mm after quantization.
            for (int cz = 0; cz < chunks; cz++)
            for (int cx = 0; cx < chunks; cx++)
            {
                if (!SeamCheck.BordersMatch(field, cx, cz, out float maxDiff))
                {
                    seamFailures++;
                    report.AppendLine($"  \"seamError\": \"chunk {cx},{cz} maxDiff={maxDiff}\",");
                }
                worstSeam = Mathf.Max(worstSeam, maxDiff);

                BakeChunkMesh(field, cx, cz);
            }

            report.AppendLine($"  \"seamFailures\": {seamFailures},");
            report.AppendLine($"  \"worstSeamDifferenceMeters\": {worstSeam:0.######},");
            report.AppendLine($"  \"elapsedMs\": {stopwatch.ElapsedMilliseconds}");
            report.AppendLine("}");

            File.WriteAllText("Logs/world_bake_report.json", report.ToString());
            Debug.Log($"[Wayfinder] World bake finished in {stopwatch.ElapsedMilliseconds} ms. " +
                      $"Seam failures: {seamFailures}. Report: Logs/world_bake_report.json");
            if (seamFailures > 0)
                Debug.LogError("[Wayfinder] Seam validation FAILED — a world with seam errors cannot ship (§7.4).");
            AssetDatabase.Refresh();
        }

        /// <summary>
        /// Emits one greybox chunk mesh (256 m, 129×129 grid from the shared height function —
        /// border samples read the same source so seams quantize equal). Final art dressing is M5.
        /// </summary>
        static void BakeChunkMesh(HeightField field, int chunkX, int chunkZ)
        {
            int res = WorldConstants.TerrainTileResolution;
            float spacing = WorldConstants.TerrainSampleSpacingMeters;
            float originX = chunkX * WorldConstants.ChunkSizeMeters;
            float originZ = chunkZ * WorldConstants.ChunkSizeMeters;

            var vertices = new Vector3[res * res];
            for (int z = 0; z < res; z++)
            for (int x = 0; x < res; x++)
            {
                float wx = originX + x * spacing;
                float wz = originZ + z * spacing;
                vertices[z * res + x] = new Vector3(wx, field.Sample(wx, wz), wz);
            }

            var triangles = new int[(res - 1) * (res - 1) * 6];
            int t = 0;
            for (int z = 0; z < res - 1; z++)
            for (int x = 0; x < res - 1; x++)
            {
                int i = z * res + x;
                triangles[t++] = i;
                triangles[t++] = i + res;
                triangles[t++] = i + 1;
                triangles[t++] = i + 1;
                triangles[t++] = i + res;
                triangles[t++] = i + res + 1;
            }

            var mesh = new Mesh { name = $"chunk_{chunkX}_{chunkZ}" };
            if (vertices.Length > 65535) mesh.indexFormat = UnityEngine.Rendering.IndexFormat.UInt32;
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            string path = $"{BakeOutputDir}/chunk_{chunkX}_{chunkZ}.asset";
            var existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(mesh, path);
        }
    }
}
