using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Editor
{
    /// <summary>
    /// Content import + catalog hash (§17.1): JSON under Assets/Wayfinder/Content is the
    /// canonical, auditable source. Import validates IDs and emits a deterministic catalog hash.
    /// Menu: Tools → Wayfinder → Import Content JSON / Validate Content.
    /// </summary>
    public static class ContentImporter
    {
        public const string ContentDir = "Assets/Wayfinder/Content";

        [MenuItem("Tools/Wayfinder/Import Content JSON")]
        public static void Import()
        {
            var report = ContentValidators.ValidateAll();
            Debug.Log(report.ToString());
            if (!report.Ok)
            {
                Debug.LogError("[Wayfinder] Content validation FAILED — import aborted. Fix errors first.");
                return;
            }
            string hash = ComputeCatalogHash();
            Debug.Log($"[Wayfinder] Content imported. Catalog hash: {hash}");
            Directory.CreateDirectory("Logs");
            File.WriteAllText("Logs/catalog_hash.txt", hash);
        }

        /// <summary>Deterministic catalog hash over all content JSON (sorted file names + content).</summary>
        public static string ComputeCatalogHash()
        {
            if (!Directory.Exists(ContentDir)) return "missing-content-dir";
            var files = Directory.GetFiles(ContentDir, "*.json", SearchOption.AllDirectories)
                .OrderBy(f => f.Replace('\\', '/'), StringComparer.Ordinal);
            var sb = new StringBuilder();
            foreach (string file in files)
            {
                sb.Append(Path.GetFileName(file).Replace('\\', '/'));
                sb.Append('\n');
                sb.Append(File.ReadAllText(file).Replace("\r\n", "\n"));
                sb.Append('\n');
            }
            return StableHash.Hash64(sb.ToString()).ToString("x16");
        }
    }

    /// <summary>
    /// Build-time validation (§16.6): duplicate IDs, missing localization keys, circular
    /// prerequisites, negative costs, invalid recipes, unreachable mandatory objectives,
    /// content-count mismatches, story gates referring to nonexistent missions.
    /// CI must fail on errors (warnings allowed).
    /// </summary>
    public sealed class ValidationReport
    {
        public readonly List<string> Errors = new List<string>();
        public readonly List<string> Warnings = new List<string>();
        public bool Ok => Errors.Count == 0;

        public void Error(string msg) => Errors.Add(msg);
        public void Warning(string msg) => Warnings.Add(msg);

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"[Wayfinder] Validation: {Errors.Count} error(s), {Warnings.Count} warning(s)");
            foreach (var e in Errors) sb.AppendLine("  ERROR: " + e);
            foreach (var w in Warnings) sb.AppendLine("  warn:  " + w);
            return sb.ToString();
        }
    }

    public static class ContentValidators
    {
        [MenuItem("Tools/Wayfinder/Validate Content")]
        public static void ValidateMenu()
        {
            var report = ValidateAll();
            Debug.Log(report.ToString());
        }

        public static ValidationReport ValidateAll()
        {
            var report = new ValidationReport();
            string contentDir = Path.Combine(Application.dataPath, "Wayfinder/Content");
            if (!Directory.Exists(contentDir))
            {
                report.Error($"content directory missing: {contentDir}");
                return report;
            }

            var ids = new HashSet<string>(StringComparer.Ordinal);
            void CheckUnique(string kind, string id)
            {
                if (string.IsNullOrEmpty(id)) { report.Error($"{kind}: empty id"); return; }
                if (!ids.Add(id)) report.Error($"duplicate id '{id}' ({kind})");
            }

            var jsonFiles = Directory.GetFiles(contentDir, "*.json", SearchOption.AllDirectories);
            foreach (string file in jsonFiles)
            {
                string text = File.ReadAllText(file);
                try
                {
                    Newtonsoft.Json.Linq.JObject.Parse(text);
                }
                catch (Exception ex)
                {
                    report.Error($"{Path.GetFileName(file)}: invalid JSON — {ex.Message}");
                }
            }

            // Canonical table checks (mirror of Tools/Validation/validate_content.py):
            // the headless Python validator is authoritative for CI in this repo's state; these
            // editor checks keep the same rules available inside Unity.
            ValidateUpgradeTable(report, contentDir, ids, CheckUnique);
            ValidateMissionTable(report, contentDir, ids, CheckUnique);
            ValidateCounts(report, contentDir);
            return report;
        }

        static void ValidateUpgradeTable(ValidationReport report, string contentDir,
            HashSet<string> ids, Action<string, string> checkUnique)
        {
            string path = Path.Combine(contentDir, "upgrades.json");
            if (!File.Exists(path)) { report.Error("upgrades.json missing"); return; }

            var root = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
            int expectedTier = 1;
            int totalCost = 0;
            foreach (var token in root)
            {
                string id = token.Value<string>("id");
                checkUnique("upgrade", id);
                int tier = token.Value<int>("tier");
                int cost = token.Value<int>("costSalvage");
                if (tier != expectedTier) report.Error($"upgrade {id}: tier {tier} out of sequence (expected {expectedTier})");
                if (cost <= 0) report.Error($"upgrade {id}: non-positive cost");
                totalCost += cost;
                string mission = token.Value<string>("requiredMission");
                if (!string.IsNullOrEmpty(mission) && !MissionCodeExists(contentDir, mission))
                    report.Error($"upgrade {id}: story gate '{mission}' does not exist");
                expectedTier++;
            }
            if (totalCost != 32200)
                report.Error($"upgrade total cost {totalCost} != 32200 (§11.2)");
            if (expectedTier != 21)
                report.Error($"expected 20 upgrade tiers, found {expectedTier - 1}");
        }

        static bool MissionCodeExists(string contentDir, string code)
        {
            string path = Path.Combine(contentDir, "missions.json");
            if (!File.Exists(path)) return false;
            var root = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
            return root.Any(t => t.Value<string>("displayCode") == code);
        }

        static void ValidateMissionTable(ValidationReport report, string contentDir,
            HashSet<string> ids, Action<string, string> checkUnique)
        {
            string path = Path.Combine(contentDir, "missions.json");
            if (!File.Exists(path)) { report.Error("missions.json missing"); return; }
            var root = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
            var codes = new HashSet<string>(StringComparer.Ordinal);
            foreach (var token in root)
            {
                string id = token.Value<string>("id");
                checkUnique("mission", id);
                string code = token.Value<string>("displayCode");
                codes.Add(code);
                int reward = token.Value<int>("rewardSalvage");
                if (reward < 0) report.Error($"mission {code}: negative reward");
                if (token["prerequisites"] is Newtonsoft.Json.Linq.JArray pre)
                {
                    foreach (var p in pre)
                    {
                        string preId = p.Value<string>();
                        if (!preId.StartsWith("mission."))
                            report.Error($"mission {code}: bad prerequisite id {preId}");
                    }
                }
            }
            for (int q = 1; q <= 20; q++)
            {
                if (!codes.Contains($"Q{q:00}"))
                    report.Error($"mission Q{q:00} missing");
            }
        }

        static void ValidateCounts(ValidationReport report, string contentDir)
        {
            void Count(string file, string field, string value, int expected, string label)
            {
                string path = Path.Combine(contentDir, file);
                if (!File.Exists(path)) { report.Error($"{file} missing"); return; }
                var root = Newtonsoft.Json.Linq.JArray.Parse(File.ReadAllText(path));
                int n = root.Count(t => value == null || t.Value<string>(field) == value);
                if (n != expected) report.Error($"{label}: expected {expected}, found {n}");
            }

            // Content-count audit (CONTENT-01): exact release-scope counts.
            Count("landmarks.json", "biome", "forest", 20, "landmarks.forest");
            Count("landmarks.json", "biome", "canyon", 20, "landmarks.canyon");
            Count("landmarks.json", "biome", "city", 20, "landmarks.city");
            Count("landmarks.json", "biome", "wetlands", 20, "landmarks.wetlands");
            Count("landmarks.json", "biome", "highlands", 20, "landmarks.highlands");
            Count("caches.json", "biome", "forest", 40, "caches.forest");
            Count("caches.json", "biome", "canyon", 40, "caches.canyon");
            Count("caches.json", "biome", "city", 40, "caches.city");
            Count("caches.json", "biome", "wetlands", 40, "caches.wetlands");
            Count("caches.json", "biome", "highlands", 40, "caches.highlands");
        }
    }
}
