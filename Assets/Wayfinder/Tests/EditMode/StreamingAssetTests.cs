using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Wayfinder.Campaign;
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
        public void CanonicalCatalogHasExactlyTenRequired3DStreamingAssets()
        {
            Assert.IsTrue(CampaignContent.RequiredFiles.Contains("streaming_assets.json"));
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
            }
        }

        [Test]
        public void AllTenStreamingAssetsExistOnDiskAndMatchSha256AndGeometry()
        {
            StreamingAssetCatalog.VerifyAllOnDisk(Content.StreamingAssets, StreamingAssetsDir);

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

            // Delete one required 3D streaming asset
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

            // Provide only 9 assets in streaming_assets.json
            var subset = Content.StreamingAssets.Values.Take(9).ToList();
            files["streaming_assets.json"] = Newtonsoft.Json.JsonConvert.SerializeObject(subset);

            var ex = Assert.Throws<InvalidDataException>(() => CampaignContent.FromJson(files));
            StringAssert.Contains("streaming_assets.json", ex.Message);
        }
    }
}
