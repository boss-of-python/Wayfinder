using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;
using Wayfinder.Progression;

namespace Wayfinder.Tests
{
    /// <summary>
    /// KI-046: proves that items.json, recipes.json, vehicles.json, and missions.json are parsed
    /// directly into the runtime services — editing JSON mutates live gameplay rules without
    /// changing C# code.
    /// </summary>
    public class SingleSourceCatalogTests
    {
        static string LocateContentDir()
        {
            var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
            while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "Assets", "Wayfinder", "Content")))
                dir = dir.Parent;
            return Path.Combine(dir.FullName, "Assets", "Wayfinder", "Content");
        }

        [TearDown]
        public void TearDown()
        {
            // Restore canonical on-disk definitions so no subsequent test observes mutated globals.
            CampaignContent.FromDirectory(LocateContentDir());
        }

        [Test]
        public void EditingCanonicalJsonCatalogs_ChangesLiveItemsRecipesVehiclesAndMissions_KI046()
        {
            string contentDir = LocateContentDir();
            var files = new Dictionary<string, string>(System.StringComparer.Ordinal);
            foreach (var p in Directory.GetFiles(contentDir, "*.json"))
                files[Path.GetFileName(p)] = File.ReadAllText(p);

            // 1. Mutate items.json: change tire_patch purchase price from 22 -> 30 and maxStack 4 -> 6.
            var items = JArray.Parse(files["items.json"]);
            foreach (JObject item in items)
                if (item.Value<string>("id") == "tire_patch")
                {
                    item["purchasePriceSalvage"] = 30;
                    item["maxStack"] = 6;
                }
            files["items.json"] = items.ToString();

            // 2. Mutate recipes.json: change recipe.tire_patch salvageFee from 10 -> 17.
            var recipes = JArray.Parse(files["recipes.json"]);
            foreach (JObject recipe in recipes)
                if (recipe.Value<string>("id") == "recipe.tire_patch")
                    recipe["salvageFee"] = 17;
            files["recipes.json"] = recipes.ToString();

            // 3. Mutate vehicles.json: change vehicle.scout.s1 fuelCapacityLiters from 24 -> 29.
            var vehicles = JArray.Parse(files["vehicles.json"]);
            foreach (JObject v in vehicles)
                if (v.Value<string>("id") == "vehicle.scout.s1")
                    v["fuelCapacityLiters"] = 29;
            files["vehicles.json"] = vehicles.ToString();

            // 4. Mutate missions.json: change Q01 reward salvage from 150 -> 275.
            var missions = JArray.Parse(files["missions.json"]);
            foreach (JObject m in missions)
                if (m.Value<string>("displayCode") == "Q01")
                    m["reward"]["salvage"] = 275;
            files["missions.json"] = missions.ToString();

            var loaded = CampaignContent.FromJson(files);
            Assert.AreEqual(30, ItemCatalog.Get("tire_patch").PurchasePriceSalvage);
            Assert.AreEqual(6, ItemCatalog.Get("tire_patch").MaxStack);
            Assert.AreEqual(17, CraftingService.GetRecipe(new ContentId("recipe.tire_patch")).SalvageFee);
            Assert.AreEqual(29f, loaded.VehicleSpecById("vehicle.scout.s1").FuelCapacityLiters, 1e-4f);

            string root = CampaignTestKit.NewTempRoot();
            try
            {
                var session = CampaignSession.StartNew(new SaveService(root), loaded, new SaveSlotId(0)).Session;
                Assert.AreEqual(29f, session.FuelCapacityLiters, 1e-4f, "session tank comes from vehicles.json");
                Assert.AreEqual(275, session.Missions.Definitions["Q01"].RewardSalvage, "mission reward comes from missions.json");
            }
            finally
            {
                CampaignTestKit.DeleteRoot(root);
            }
        }
    }
}
