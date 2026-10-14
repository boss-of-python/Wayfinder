using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Wayfinder.Tests
{
    /// <summary>
    /// Content catalog audits (CONTENT-01, UPG-01, QUEST-01, QA case 7/13) run inside Unity
    /// against the canonical JSON. Headless twin: Tools/Validation/validate_content.py.
    /// </summary>
    public class ContentCatalogTests
    {
        /// <summary>
        /// Inside Unity the catalog lives under Application.dataPath. Under the headless NUnit
        /// runner (Tools/Build/headless_cs.py) there is no UnityEngine, so we honour
        /// WAYFINDER_CONTENT_DIR or walk up from the working directory to find Assets/Wayfinder/Content.
        /// </summary>
        static string ContentDir => _contentDir ??= LocateContentDir();
        static string _contentDir;

        static string LocateContentDir()
        {
#if UNITY_5_3_OR_NEWER
            return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, "Wayfinder", "Content"));
#else
            string overrideDir = Environment.GetEnvironmentVariable("WAYFINDER_CONTENT_DIR");
            if (!string.IsNullOrEmpty(overrideDir) && Directory.Exists(overrideDir))
                return Path.GetFullPath(overrideDir);

            foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
            {
                var dir = new DirectoryInfo(start);
                while (dir != null)
                {
                    string candidate = Path.Combine(dir.FullName, "Assets", "Wayfinder", "Content");
                    if (Directory.Exists(candidate)) return candidate;
                    dir = dir.Parent;
                }
            }
            throw new DirectoryNotFoundException(
                "Assets/Wayfinder/Content not found; set WAYFINDER_CONTENT_DIR or run from the repository.");
#endif
        }

        static JArray Load(string name) =>
            JArray.Parse(File.ReadAllText(Path.Combine(ContentDir, name)));

        [Test]
        public void Counts_MatchReleaseScope()
        {
            Assert.AreEqual(100, Load("landmarks.json").Count);
            Assert.AreEqual(200, Load("caches.json").Count);
            Assert.AreEqual(20, Load("missions.json").Count);
            var contracts = Load("contracts.json");
            Assert.AreEqual(30, contracts.Count(t => t.Value<bool?>("oneTime") ?? true));
            Assert.AreEqual(5, contracts.Count(t => !(t.Value<bool?>("oneTime") ?? true)));
            Assert.AreEqual(20, Load("upgrades.json").Count);
            Assert.AreEqual(4, Load("vehicles.json").Count);
            Assert.AreEqual(10, Load("camps.json").Count);
            Assert.AreEqual(5, Load("garages.json").Count);
            Assert.AreEqual(25, Load("relics.json").Count);
            Assert.AreEqual(11, Load("surfaces.json").Count);
        }

        [Test]
        public void PerBiomeSplits_AreExact()
        {
            foreach (var biome in new[] { "forest", "canyon", "city", "wetlands", "highlands" })
            {
                Assert.AreEqual(20, Load("landmarks.json").Count(t => t.Value<string>("biome") == biome), biome);
                Assert.AreEqual(40, Load("caches.json").Count(t => t.Value<string>("biome") == biome), biome);
                Assert.AreEqual(6, Load("contracts.json").Count(t => t.Value<string>("biome") == biome), biome);
                Assert.AreEqual(2, Load("camps.json").Count(t => t.Value<string>("biome") == biome), biome);
            }
        }

        [Test]
        public void UpgradeTable_MatchesCanonicalCostsAndGates_QA7()
        {
            int[] expectedCosts = { 200, 300, 450, 550, 700, 850, 1000, 1150, 1300, 1450,
                1600, 1750, 1900, 2100, 2300, 2500, 2700, 2900, 3100, 3400 };
            string[] expectedGates = { "Q01","Q02","Q02","Q03","Q04","Q04","Q05","Q06","Q07","Q08",
                "Q09","Q10","Q11","Q12","Q13","Q14","Q15","Q16","Q17","Q19" };
            var upgrades = Load("upgrades.json");
            int total = 0;
            for (int i = 0; i < 20; i++)
            {
                var u = upgrades[i];
                Assert.AreEqual(i + 1, u.Value<int>("tier"));
                Assert.AreEqual(expectedCosts[i], u.Value<int>("costSalvage"), u.Value<string>("id"));
                Assert.AreEqual(expectedGates[i], u.Value<string>("requiredMission"), u.Value<string>("id"));
                total += u.Value<int>("costSalvage");
            }
            Assert.AreEqual(32200, total);
        }

        [Test]
        public void LandmarkAndCacheIds_AreUnique_QA13()
        {
            var ids = Load("landmarks.json").Select(t => t.Value<string>("id"))
                .Concat(Load("caches.json").Select(t => t.Value<string>("id")))
                .ToList();
            Assert.AreEqual(ids.Count, ids.Distinct().Count(), "all landmark/cache IDs must be unique");
        }

        [Test]
        public void MissionRewards_MatchSection9_2()
        {
            int[] expected = { 150, 200, 300, 500, 300, 400, 500, 700, 400, 500,
                650, 850, 500, 650, 800, 1000, 650, 800, 1000, 1500 };
            var missions = Load("missions.json");
            for (int i = 0; i < 20; i++)
            {
                var m = missions.First(t => t.Value<string>("displayCode") == $"Q{i + 1:00}");
                Assert.AreEqual(expected[i], m["reward"].Value<int>("salvage"));
            }
        }

        [Test]
        public void NoDuplicateIds_AcrossAllContent()
        {
            var all = new HashSet<string>();
            foreach (var file in new[] { "landmarks.json", "caches.json", "missions.json", "contracts.json",
                "upgrades.json", "vehicles.json", "items.json", "camps.json", "garages.json", "relics.json",
                "recipes.json", "surfaces.json", "biomes.json" })
            {
                foreach (var token in Load(file))
                {
                    string id = token.Value<string>("id");
                    Assert.IsTrue(all.Add(id), $"duplicate id {id}");
                }
            }
        }
    }
}
