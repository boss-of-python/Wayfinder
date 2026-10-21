using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Wayfinder.World
{
    /// <summary>
    /// Metadata and verification contract for one required 3D streaming mesh asset (§14.2, §14.3, LEGAL-01).
    /// Stored in <c>Assets/Wayfinder/Content/streaming_assets.json</c> and backed by a deterministic,
    /// text-based Wavefront <c>.obj</c> file under <c>Assets/StreamingAssets/Models/</c>.
    /// </summary>
    public sealed class StreamingAssetDefinition
    {
        public string Id;
        public string Name;
        public string Category;
        public string RelativePath;
        public string Format;
        public bool Required;
        public string PivotConvention;
        public int VertexCount;
        public int UvCount;
        public int NormalCount;
        public int TriangleCount;
        public int MaxTrianglesLod0;
        public (float x, float y, float z) BoundsMin;
        public (float x, float y, float z) BoundsMax;
        public string Sha256;
        public string License;
        public string Generator;
    }

    /// <summary>One vertex/uv/normal index triplet (1-based in Wavefront OBJ, stored 0-based here).</summary>
    public readonly struct ObjFaceVertex
    {
        public readonly int VertexIndex;
        public readonly int UvIndex;
        public readonly int NormalIndex;

        public ObjFaceVertex(int vertexIndex, int uvIndex, int normalIndex)
        {
            VertexIndex = vertexIndex;
            UvIndex = uvIndex;
            NormalIndex = normalIndex;
        }
    }

    /// <summary>One triangulated face of a Wavefront OBJ mesh.</summary>
    public readonly struct ObjTriangle
    {
        public readonly ObjFaceVertex A;
        public readonly ObjFaceVertex B;
        public readonly ObjFaceVertex C;

        public ObjTriangle(ObjFaceVertex a, ObjFaceVertex b, ObjFaceVertex c)
        {
            A = a;
            B = b;
            C = c;
        }
    }

    /// <summary>Engine-free parsed Wavefront OBJ 3D mesh data.</summary>
    public sealed class ObjMeshData
    {
        public string ObjectName = string.Empty;
        public readonly List<(float x, float y, float z)> Vertices = new List<(float x, float y, float z)>();
        public readonly List<(float u, float v)> Uvs = new List<(float u, float v)>();
        public readonly List<(float x, float y, float z)> Normals = new List<(float x, float y, float z)>();
        public readonly List<ObjTriangle> Triangles = new List<ObjTriangle>();
        public (float x, float y, float z) BoundsMin;
        public (float x, float y, float z) BoundsMax;
        public string Sha256Hex = string.Empty;
    }

    /// <summary>
    /// Pure C# parser and build/runtime verifier for the 10 required 3D streaming assets (§14.2, §14.3).
    /// No UnityEngine dependency — executes in headless tests, CI validation, and Unity editor setup.
    /// </summary>
    public static class StreamingAssetCatalog
    {
        public const int RequiredAssetCount = 10;
        public const string DefaultStreamingAssetsRoot = "Assets/StreamingAssets";

        public static readonly string[] RequiredAssetIds =
        {
            "veh.scout.s1.chassis",
            "veh.scout.s1.wheel",
            "env.hub.home_garage",
            "env.hub.field_camp",
            "env.landmark.watchtower",
            "env.prop.relay_tower",
            "env.prop.salvage_cache",
            "env.prop.creek_gauge",
            "env.tree.forest_pine",
            "env.rock.granite_outcrop"
        };

        public static readonly string[] AllowedCategories =
        {
            "vehicle", "vehicle_wheel", "building", "camp", "landmark", "prop", "tree", "rock"
        };

        /// <summary>Computes lowercase hex SHA-256 over LF-normalized UTF-8 text.</summary>
        public static string ComputeSha256Hex(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));
            string normalized = text.Replace("\r\n", "\n");
            byte[] bytes = Encoding.UTF8.GetBytes(normalized);
            using (var sha = SHA256.Create())
            {
                byte[] digest = sha.ComputeHash(bytes);
                var sb = new StringBuilder(digest.Length * 2);
                for (int i = 0; i < digest.Length; i++)
                    sb.Append(digest[i].ToString("x2", CultureInfo.InvariantCulture));
                return sb.ToString();
            }
        }

        /// <summary>Parses a Wavefront .obj mesh and validates its topology, normals, UVs, and bounds.</summary>
        public static ObjMeshData ParseObj(string objText, string assetLabel = "obj")
        {
            if (string.IsNullOrWhiteSpace(objText))
                throw new InvalidDataException($"{assetLabel}: OBJ content is empty");

            string normalized = objText.Replace("\r\n", "\n");
            var mesh = new ObjMeshData
            {
                Sha256Hex = ComputeSha256Hex(normalized)
            };

            float minX = float.PositiveInfinity, minY = float.PositiveInfinity, minZ = float.PositiveInfinity;
            float maxX = float.NegativeInfinity, maxY = float.NegativeInfinity, maxZ = float.NegativeInfinity;

            string[] lines = normalized.Split('\n');
            for (int lineIdx = 0; lineIdx < lines.Length; lineIdx++)
            {
                string line = lines[lineIdx].Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;

                switch (parts[0])
                {
                    case "o":
                        if (parts.Length < 2)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: 'o' missing object name");
                        mesh.ObjectName = parts[1];
                        break;

                    case "v":
                        if (parts.Length != 4)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: 'v' requires 3 coordinates");
                        float vx = ParseFloat(parts[1], assetLabel, lineIdx + 1);
                        float vy = ParseFloat(parts[2], assetLabel, lineIdx + 1);
                        float vz = ParseFloat(parts[3], assetLabel, lineIdx + 1);
                        mesh.Vertices.Add((vx, vy, vz));
                        if (vx < minX) minX = vx;
                        if (vy < minY) minY = vy;
                        if (vz < minZ) minZ = vz;
                        if (vx > maxX) maxX = vx;
                        if (vy > maxY) maxY = vy;
                        if (vz > maxZ) maxZ = vz;
                        break;

                    case "vt":
                        if (parts.Length != 3)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: 'vt' requires 2 UV coordinates");
                        float u = ParseFloat(parts[1], assetLabel, lineIdx + 1);
                        float v = ParseFloat(parts[2], assetLabel, lineIdx + 1);
                        mesh.Uvs.Add((u, v));
                        break;

                    case "vn":
                        if (parts.Length != 4)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: 'vn' requires 3 normal components");
                        float nx = ParseFloat(parts[1], assetLabel, lineIdx + 1);
                        float ny = ParseFloat(parts[2], assetLabel, lineIdx + 1);
                        float nz = ParseFloat(parts[3], assetLabel, lineIdx + 1);
                        float nLen = (float)Math.Sqrt(nx * nx + ny * ny + nz * nz);
                        if (nLen < 0.9f || nLen > 1.1f)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: normal is not unit length ({nLen:0.###})");
                        mesh.Normals.Add((nx, ny, nz));
                        break;

                    case "f":
                        if (parts.Length != 4)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: faces must be triangulated (expected 3 vertices, got {parts.Length - 1})");
                        var fv0 = ParseFaceVertex(parts[1], mesh, assetLabel, lineIdx + 1);
                        var fv1 = ParseFaceVertex(parts[2], mesh, assetLabel, lineIdx + 1);
                        var fv2 = ParseFaceVertex(parts[3], mesh, assetLabel, lineIdx + 1);
                        if (fv0.VertexIndex == fv1.VertexIndex || fv1.VertexIndex == fv2.VertexIndex || fv0.VertexIndex == fv2.VertexIndex)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: degenerate triangle with duplicate vertex index");

                        var p0 = mesh.Vertices[fv0.VertexIndex];
                        var p1 = mesh.Vertices[fv1.VertexIndex];
                        var p2 = mesh.Vertices[fv2.VertexIndex];
                        float ux = p1.x - p0.x, uy = p1.y - p0.y, uz = p1.z - p0.z;
                        float wx = p2.x - p0.x, wy = p2.y - p0.y, wz = p2.z - p0.z;
                        float cx = uy * wz - uz * wy;
                        float cy = uz * wx - ux * wz;
                        float cz = ux * wy - uy * wx;
                        float doubleArea = (float)Math.Sqrt(cx * cx + cy * cy + cz * cz);
                        if (doubleArea < 1e-6f)
                            throw new InvalidDataException($"{assetLabel}:{lineIdx + 1}: collinear/zero-area triangle");

                        mesh.Triangles.Add(new ObjTriangle(fv0, fv1, fv2));
                        break;
                }
            }

            if (mesh.Vertices.Count == 0 || mesh.Triangles.Count == 0)
                throw new InvalidDataException($"{assetLabel}: mesh must contain vertices and triangles");

            mesh.BoundsMin = (minX, minY, minZ);
            mesh.BoundsMax = (maxX, maxY, maxZ);
            return mesh;
        }

        /// <summary>Verifies a raw UTF-8 OBJ byte buffer against its manifest definition (§14.2, §14.3, LEGAL-01).</summary>
        public static ObjMeshData VerifyAssetContent(StreamingAssetDefinition def, byte[] rawBytes)
        {
            if (rawBytes == null) throw new ArgumentNullException(nameof(rawBytes));
            return VerifyAssetContent(def, Encoding.UTF8.GetString(rawBytes));
        }

        /// <summary>Verifies a parsed OBJ mesh against its manifest definition (§14.2, §14.3, LEGAL-01).</summary>
        public static ObjMeshData VerifyAssetContent(StreamingAssetDefinition def, string objText)
        {
            if (def == null) throw new ArgumentNullException(nameof(def));
            if (string.IsNullOrEmpty(def.Id))
                throw new InvalidDataException("streaming asset definition has empty id");
            if (!def.Required)
                throw new InvalidDataException($"{def.Id}: required 3D streaming asset must have required=true");
            if (def.Format != "obj")
                throw new InvalidDataException($"{def.Id}: unsupported streaming mesh format '{def.Format}' (expected 'obj')");
            if (string.IsNullOrEmpty(def.RelativePath) || !def.RelativePath.StartsWith("Models/", StringComparison.Ordinal) ||
                !def.RelativePath.EndsWith(".obj", StringComparison.Ordinal) || def.RelativePath.Contains(".."))
                throw new InvalidDataException($"{def.Id}: unsafe or non-canonical relativePath '{def.RelativePath}'");
            if (Array.IndexOf(AllowedCategories, def.Category) < 0)
                throw new InvalidDataException($"{def.Id}: unknown category '{def.Category}'");
            if (def.License != "Project-owned generated")
                throw new InvalidDataException($"{def.Id}: license '{def.License}' violates LEGAL-01 ('Project-owned generated' required)");

            var mesh = ParseObj(objText, def.Id);

            if (!string.Equals(mesh.ObjectName, def.Id, StringComparison.Ordinal))
                throw new InvalidDataException($"{def.Id}: OBJ object name '{mesh.ObjectName}' does not match asset id '{def.Id}'");
            if (!string.Equals(mesh.Sha256Hex, def.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException($"{def.Id}: SHA-256 mismatch (manifest {def.Sha256}, actual {mesh.Sha256Hex})");
            if (mesh.Vertices.Count != def.VertexCount)
                throw new InvalidDataException($"{def.Id}: vertexCount mismatch (manifest {def.VertexCount}, actual {mesh.Vertices.Count})");
            if (mesh.Uvs.Count != def.UvCount)
                throw new InvalidDataException($"{def.Id}: uvCount mismatch (manifest {def.UvCount}, actual {mesh.Uvs.Count})");
            if (mesh.Normals.Count != def.NormalCount)
                throw new InvalidDataException($"{def.Id}: normalCount mismatch (manifest {def.NormalCount}, actual {mesh.Normals.Count})");
            if (mesh.Triangles.Count != def.TriangleCount)
                throw new InvalidDataException($"{def.Id}: triangleCount mismatch (manifest {def.TriangleCount}, actual {mesh.Triangles.Count})");
            if (def.MaxTrianglesLod0 <= 0 || mesh.Triangles.Count > def.MaxTrianglesLod0)
                throw new InvalidDataException($"{def.Id}: triangle count {mesh.Triangles.Count} exceeds §14.2 budget {def.MaxTrianglesLod0}");

            CheckClose(def.Id, "boundsMin.x", mesh.BoundsMin.x, def.BoundsMin.x);
            CheckClose(def.Id, "boundsMin.y", mesh.BoundsMin.y, def.BoundsMin.y);
            CheckClose(def.Id, "boundsMin.z", mesh.BoundsMin.z, def.BoundsMin.z);
            CheckClose(def.Id, "boundsMax.x", mesh.BoundsMax.x, def.BoundsMax.x);
            CheckClose(def.Id, "boundsMax.y", mesh.BoundsMax.y, def.BoundsMax.y);
            CheckClose(def.Id, "boundsMax.z", mesh.BoundsMax.z, def.BoundsMax.z);

            // §14.3 pivot conventions: ground datum sits on Y = 0; wheel sits centered on axle (Y/Z symmetric).
            if (def.PivotConvention == "ground_datum_y0")
            {
                if (Math.Abs(mesh.BoundsMin.y) > 1e-3f)
                    throw new InvalidDataException($"{def.Id}: ground_datum_y0 requires boundsMin.y == 0 (got {mesh.BoundsMin.y})");
            }
            else if (def.PivotConvention == "axle_center")
            {
                if (Math.Abs(mesh.BoundsMin.y + mesh.BoundsMax.y) > 1e-3f ||
                    Math.Abs(mesh.BoundsMin.z + mesh.BoundsMax.z) > 1e-3f)
                    throw new InvalidDataException($"{def.Id}: axle_center requires Y/Z bounds centered on 0");
            }
            else
            {
                throw new InvalidDataException($"{def.Id}: unknown pivotConvention '{def.PivotConvention}'");
            }

            return mesh;
        }

        /// <summary>
        /// Verifies that all 10 required 3D streaming assets are present in the catalog and that every
        /// backing <c>.obj</c> file exists under <paramref name="streamingAssetsRoot"/> and passes
        /// full geometric + SHA-256 verification.
        /// </summary>
        public static void VerifyAllOnDisk(IReadOnlyDictionary<string, StreamingAssetDefinition> assets,
            string streamingAssetsRoot = DefaultStreamingAssetsRoot)
        {
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (string.IsNullOrEmpty(streamingAssetsRoot))
                throw new ArgumentException("streamingAssetsRoot is required", nameof(streamingAssetsRoot));

            if (assets.Count != RequiredAssetCount)
                throw new InvalidDataException(
                    $"expected exactly {RequiredAssetCount} required 3D streaming assets, found {assets.Count}");

            foreach (string requiredId in RequiredAssetIds)
            {
                if (!assets.TryGetValue(requiredId, out var def) || def == null)
                    throw new InvalidDataException($"missing required 3D streaming asset '{requiredId}'");

                string fullPath = Path.Combine(streamingAssetsRoot,
                    def.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException(
                        $"required 3D streaming asset file missing on disk: {fullPath} ({requiredId})", fullPath);

                string objText = File.ReadAllText(fullPath, Encoding.UTF8);
                VerifyAssetContent(def, objText);
            }
        }

        static float ParseFloat(string token, string label, int line)
        {
            if (!float.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out float value) ||
                float.IsNaN(value) || float.IsInfinity(value))
                throw new InvalidDataException($"{label}:{line}: invalid float '{token}'");
            return value;
        }

        static ObjFaceVertex ParseFaceVertex(string token, ObjMeshData mesh, string label, int line)
        {
            string[] indices = token.Split('/');
            if (indices.Length != 3)
                throw new InvalidDataException($"{label}:{line}: face vertex '{token}' must be v/vt/vn");
            int vi = ParseIndex(indices[0], mesh.Vertices.Count, "vertex", label, line);
            int ti = ParseIndex(indices[1], mesh.Uvs.Count, "uv", label, line);
            int ni = ParseIndex(indices[2], mesh.Normals.Count, "normal", label, line);
            return new ObjFaceVertex(vi, ti, ni);
        }

        static int ParseIndex(string token, int count, string kind, string label, int line)
        {
            if (!int.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out int idx) ||
                idx < 1 || idx > count)
                throw new InvalidDataException($"{label}:{line}: {kind} index '{token}' out of range 1..{count}");
            return idx - 1;
        }

        static void CheckClose(string id, string field, float actual, float expected)
        {
            if (Math.Abs(actual - expected) > 1e-3f)
                throw new InvalidDataException($"{id}: {field} mismatch (manifest {expected}, actual {actual})");
        }
    }
}
