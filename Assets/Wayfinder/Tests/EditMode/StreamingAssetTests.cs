using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.World;
using static Wayfinder.Tests.CampaignTestKit;

namespace Wayfinder.Tests
{
    public class StreamingAssetTests
    {
        string _tempRoot;

        [SetUp]
        public void SetUp() => _tempRoot = NewTempRoot();

        [TearDown]
        public void TearDown() => DeleteRoot(_tempRoot);

        [Test]
        public void CanonicalCatalogHasExactlyThirtyRequired3DStreamingAssets()
        {
            Assert.IsTrue(CampaignContent.RequiredFiles.Contains("streaming_assets.json"));
            Assert.AreEqual(30, StreamingAssetCatalog.RequiredAssetCount);
            Assert.AreEqual(StreamingAssetCatalog.RequiredAssetCount, Content.StreamingAssets.Count);
            foreach (string requiredId in StreamingAssetCatalog.RequiredAssetIds)
            {
                Assert.IsTrue(Content.StreamingAssets.TryGetValue(requiredId, out var def),
                    $"Missing required 3D streaming asset '{requiredId}'");
                Assert.IsTrue(def.Required, $"Streaming asset '{requiredId}' must be marked required");
                Assert.AreEqual("obj", def.Format);
                Assert.AreEqual("Project-owned generated", def.License);
                Assert.Greater(def.VertexCount, 0);
                Assert.Greater(def.TriangleCount, 0);
                Assert.LessOrEqual(def.TriangleCount, def.MaxTrianglesLod0);
                StringAssert.StartsWith("StreamingModels/", StreamingAssetCatalog.ResourceStemFor(def));
            }
        }

        [Test]
        public void AllThirtyStreamingAssetsExistOnDiskAndProduceValidFlattenedMeshBuffers()
        {
            var loaded = StreamingAssetCatalog.LoadAllAndVerifyFromDisk(Content.StreamingAssets, StreamingAssetsDir);
            Assert.AreEqual(StreamingAssetCatalog.RequiredAssetCount, loaded.Count);

            foreach (var def in Content.StreamingAssets.Values)
            {
                string fullPath = Path.Combine(StreamingAssetsDir,
                    def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                byte[] rawBytes = File.ReadAllBytes(fullPath);

                // Ensure LF line endings (no CRLF) for cross-platform Git safety
                for (int i = 0; i < rawBytes.Length - 1; i++)
                {
                    if (rawBytes[i] == (byte)'\r' && rawBytes[i + 1] == (byte)'\n')
                        Assert.Fail($"Streaming asset '{def.Id}' contains CRLF line endings at byte {i}");
                }

                ObjMeshData mesh = StreamingAssetCatalog.VerifyAssetContent(def, rawBytes);
                Assert.AreEqual(def.VertexCount, mesh.Vertices.Count);
                Assert.AreEqual(def.UvCount, mesh.Uvs.Count);
                Assert.AreEqual(def.NormalCount, mesh.Normals.Count);
                Assert.AreEqual(def.TriangleCount, mesh.Triangles.Count);

                // Verify flattened GPU/collider buffers (both CW Unity winding and CCW OBJ winding)
                FlattenedMeshBuffers flatCw = mesh.ToFlattenedBuffers(clockwiseWinding: true);
                FlattenedMeshBuffers flatCcw = mesh.ToFlattenedBuffers(clockwiseWinding: false);
                Assert.AreEqual(def.Id, flatCw.ObjectName);
                Assert.AreEqual(def.TriangleCount * 3, flatCw.Triangles.Length);
                Assert.AreEqual(flatCw.Positions.Length, flatCw.Uvs.Length);
                Assert.AreEqual(flatCw.Positions.Length, flatCw.Normals.Length);
                Assert.Greater(flatCw.Positions.Length, 0);

                for (int t = 0; t < def.TriangleCount; t++)
                {
                    int b = t * 3;
                    Assert.AreEqual(flatCcw.Triangles[b], flatCw.Triangles[b]);
                    Assert.AreEqual(flatCcw.Triangles[b + 1], flatCw.Triangles[b + 2]);
                    Assert.AreEqual(flatCcw.Triangles[b + 2], flatCw.Triangles[b + 1]);
                    for (int k = 0; k < 3; k++)
                    {
                        int vIdx = flatCw.Triangles[b + k];
                        Assert.GreaterOrEqual(vIdx, 0);
                        Assert.Less(vIdx, flatCw.Positions.Length);
                    }
                }
            }
        }

        [Test]
        public void MissingStreamingAssetFileOnDiskThrowsFileNotFoundException()
        {
            string modelsDir = Path.Combine(_tempRoot, "Models");
            Directory.CreateDirectory(modelsDir);
            foreach (var def in Content.StreamingAssets.Values)
            {
                string src = Path.Combine(StreamingAssetsDir, def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                string dst = Path.Combine(_tempRoot, def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                File.Copy(src, dst, overwrite: true);
            }

            string victimPath = Path.Combine(_tempRoot,
                Content.StreamingAssets["veh.scout.s1.chassis"].RelativePath.Replace('/', Path.DirectorySeparatorChar));
            File.Delete(victimPath);

            Assert.Throws<FileNotFoundException>(() =>
                StreamingAssetCatalog.VerifyAllOnDisk(Content.StreamingAssets, _tempRoot));
        }

        [Test]
        public void TamperedStreamingAssetBytesThrowSha256Mismatch()
        {
            var def = Content.StreamingAssets["env.landmark.watchtower"];
            string fullPath = Path.Combine(StreamingAssetsDir,
                def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
            byte[] rawBytes = File.ReadAllBytes(fullPath);
            byte[] tampered = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(rawBytes) + "\n# tampered\n");

            var ex = Assert.Throws<InvalidDataException>(() =>
                StreamingAssetCatalog.VerifyAssetContent(def, tampered));
            StringAssert.Contains("SHA-256 mismatch", ex.Message);
        }

        [Test]
        public void MissingRequiredStreamingAssetInManifestThrowsInvalidDataException()
        {
            var files = new Dictionary<string, string>();
            foreach (string name in CampaignContent.RequiredFiles)
                files[name] = File.ReadAllText(Path.Combine(ContentDir, name));

            var subset = Content.StreamingAssets.Values.Take(StreamingAssetCatalog.RequiredAssetCount - 1).ToList();
            files["streaming_assets.json"] = Newtonsoft.Json.JsonConvert.SerializeObject(subset);

            var ex = Assert.Throws<InvalidDataException>(() => CampaignContent.FromJson(files));
            StringAssert.Contains("streaming_assets.json", ex.Message);
        }

        [Test]
        public void AllThirtyRequiredAssetsAreActivelyBoundAcrossVehiclesWorldAndBiomes()
        {
            var bound = StreamingAssetBindings.CollectAllBoundAssetIds();
            Assert.AreEqual(StreamingAssetCatalog.RequiredAssetCount, bound.Count,
                $"Expected all {StreamingAssetCatalog.RequiredAssetCount} required 3D streaming assets to be bound, got {bound.Count}: " +
                string.Join(", ", StreamingAssetCatalog.RequiredAssetIds.Except(bound)));

            foreach (string requiredId in StreamingAssetCatalog.RequiredAssetIds)
            {
                Assert.IsTrue(bound.Contains(requiredId),
                    $"Required 3D asset '{requiredId}' is not bound in StreamingAssetBindings");
                Assert.IsTrue(Content.StreamingAssets.ContainsKey(requiredId),
                    $"Required 3D asset '{requiredId}' missing from Content.StreamingAssets");
            }
        }

        [Test]
        public void EveryPlayableVehicleHasDistinctChassisAndAxleCenteredWheelAssets()
        {
            var chassisSet = new HashSet<string>();
            var wheelSet = new HashSet<string>();
            foreach (string vehicleId in StreamingAssetBindings.AllVehicleIds)
            {
                string chassisId = StreamingAssetBindings.ChassisForVehicle(vehicleId);
                string wheelId = StreamingAssetBindings.WheelForVehicle(vehicleId);
                Assert.IsTrue(chassisSet.Add(chassisId), $"Duplicate chassis binding '{chassisId}' for '{vehicleId}'");
                Assert.IsTrue(wheelSet.Add(wheelId), $"Duplicate wheel binding '{wheelId}' for '{vehicleId}'");

                var chassisDef = Content.StreamingAssets[chassisId];
                var wheelDef = Content.StreamingAssets[wheelId];
                Assert.AreEqual("vehicle", chassisDef.Category);
                Assert.AreEqual("vehicle_wheel", wheelDef.Category);
                Assert.AreEqual("axle_center", wheelDef.PivotConvention);
                Assert.AreEqual("ground_datum_y0", chassisDef.PivotConvention);
            }
            Assert.AreEqual(4, chassisSet.Count);
            Assert.AreEqual(4, wheelSet.Count);
        }

        [Test]
        public void AllCampaignWorldEntitiesResolveToVerifiedStreamingAssets()
        {
            foreach (var garage in Content.Garages.Values)
                Assert.IsTrue(Content.StreamingAssets.ContainsKey(StreamingAssetBindings.GarageBuildingFor(garage.Id)),
                    $"Garage '{garage.Id}' failed to resolve to a verified 3D asset");

            foreach (var lm in Content.Landmarks.Values)
            {
                var biome = WorldHydrology.NearestBiome(lm.Position.X, lm.Position.Z);
                string assetId = StreamingAssetBindings.LandmarkAssetFor(lm.Id, lm.Vista, biome);
                Assert.IsTrue(Content.StreamingAssets.ContainsKey(assetId),
                    $"Landmark '{lm.Id}' resolved to unknown 3D asset '{assetId}'");
            }

            foreach (var site in Content.Slice.InspectTargets)
            {
                string assetId = StreamingAssetBindings.WorldSiteAssetFor(site.Id, "inspect", WorldBiome.Forest);
                Assert.IsTrue(Content.StreamingAssets.ContainsKey(assetId),
                    $"Inspect site '{site.Id}' resolved to unknown 3D asset '{assetId}'");
            }

            foreach (WorldBiome biome in Enum.GetValues(typeof(WorldBiome)))
            {
                foreach (var rule in WorldScatterSurface.DefaultBiomeRules(biome))
                {
                    string assetId = StreamingAssetBindings.ScatterSpeciesAssetFor(rule.SpeciesId);
                    Assert.IsTrue(Content.StreamingAssets.ContainsKey(assetId),
                        $"Biome scatter rule '{rule.SpeciesId}' ({biome}) resolved to unknown 3D asset '{assetId}'");
                }
            }
        }
    }
}
