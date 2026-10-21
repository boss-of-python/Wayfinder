using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Platform;
using Wayfinder.Presentation;
using Wayfinder.Simulation;
using Wayfinder.World;

namespace Wayfinder.Editor
{
    /// <summary>
    /// Deterministic scene generation (D-006, §14.2, §14.3): Bootstrap, Title, PhysicsTestGround,
    /// VerticalSlice, and WorldShell using all 30 verified 3D streaming <c>.obj</c> assets backed by
    /// persistent serialized Unity <see cref="Mesh"/> assets. Menu: Tools → Wayfinder → Build Scenes.
    /// </summary>
    public static class SceneBuilder
    {
        public const string ScenesDir = "Assets/Wayfinder/Scenes";

        [MenuItem("Tools/Wayfinder/Build Scenes")]
        public static void BuildAll()
        {
            Directory.CreateDirectory(ScenesDir);
            BuildBootstrap();
            BuildTitle();
            BuildPhysicsTestGround();
            BuildVerticalSlice();
            BuildWorldShell();
            EditorSceneManager.OpenScene($"{ScenesDir}/Bootstrap.unity");
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{ScenesDir}/Bootstrap.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/Title.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/PhysicsTestGround.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/VerticalSlice.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/WorldShell.unity", true)
            };
            AssetDatabase.SaveAssets();
            Debug.Log("[Wayfinder] Scenes built with 30 verified 3D streaming meshes and registered in EditorBuildSettings.");
        }

        static void NewScene(string name)
        {
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var lightGo = new GameObject("Directional Light");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;

            var cameraGo = new GameObject("Main Camera");
            cameraGo.tag = "MainCamera";
            var cam = cameraGo.AddComponent<Camera>();
            cam.fieldOfView = 60f; // §3.4 baseline
            cameraGo.AddComponent<AudioListener>();
            cameraGo.transform.position = new Vector3(0f, 6f, -10f);
        }

        static void Save(string name)
        {
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), $"{ScenesDir}/{name}.unity");
        }

        static void BuildBootstrap()
        {
            NewScene("Bootstrap");
            var go = new GameObject("Bootstrap");
            go.AddComponent<BootstrapHost>();
            Save("Bootstrap");
        }

        static void BuildTitle()
        {
            NewScene("Title");
            var go = new GameObject("TitleFlow");
            go.AddComponent<TitleFlowHost>();
            Save("Title");
        }

        /// <summary>
        /// PhysicsTestGround (§4.6, §14.2): asphalt braking lane, 10/20/30/35° ramps, 10/20/28° side
        /// slopes, 0.10/0.20/0.30 m ledges, articulation blocks, mud trench, water channel with 3D
        /// creek gauges &amp; bridge span, jump landing, home workshop bay, fuel depot, cargo pallets,
        /// 4-vehicle 3D fleet showcase, winch bollards, watchtower, cairns, and forest perimeter props.
        /// </summary>
        static void BuildPhysicsTestGround()
        {
            NewScene("PhysicsTestGround");
            var content = CampaignContent.FromDirectory(ProjectSetup.CanonicalContentDir);
            var catalog = content.StreamingAssets;
            var root = new GameObject("TestGround");
            Box(root.transform, "TestAreaGround", new Vector3(240f, 1f, 340f), new Vector3(50f, -.6f, 150f),
                new Color(.30f, .38f, .26f));

            // Flat asphalt braking lane 300 m.
            Box(root.transform, "BrakingLane", new Vector3(12f, 0.1f, 300f), new Vector3(0f, 0f, 150f),
                new Color(0.25f, 0.25f, 0.27f)).AddComponent<SurfaceLabel>().surface = SurfaceType.IntactAsphalt;

            // Ramps 10/20/30/35°.
            float[] rampAngles = { 10f, 20f, 30f, 35f };
            for (int i = 0; i < rampAngles.Length; i++)
            {
                var ramp = Box(root.transform, $"Ramp{rampAngles[i]}deg",
                    new Vector3(10f, 0.5f, 20f), new Vector3(25f + i * 20f, 0f, 60f), new Color(0.45f, 0.4f, 0.35f));
                ramp.transform.rotation = Quaternion.Euler(-rampAngles[i], 0f, 0f);
            }

            // Side slopes 10/20/28°.
            float[] sideAngles = { 10f, 20f, 28f };
            for (int i = 0; i < sideAngles.Length; i++)
            {
                var slope = Box(root.transform, $"SideSlope{sideAngles[i]}deg",
                    new Vector3(14f, 0.5f, 40f), new Vector3(25f + i * 22f, 0f, 140f), new Color(0.5f, 0.45f, 0.3f));
                slope.transform.rotation = Quaternion.Euler(0f, 0f, sideAngles[i]);
            }

            // Ledges 0.10 / 0.20 / 0.30 m.
            float[] ledges = { 0.10f, 0.20f, 0.30f };
            for (int i = 0; i < ledges.Length; i++)
            {
                Box(root.transform, $"Ledge{ledges[i]}m",
                    new Vector3(6f, ledges[i], 4f), new Vector3(25f + i * 10f, ledges[i] / 2f, 190f),
                    new Color(0.4f, 0.38f, 0.36f));
            }

            // Articulation blocks (offset wheel-height blocks).
            for (int i = 0; i < 4; i++)
            {
                Box(root.transform, $"Articulation{i}",
                    new Vector3(2.5f, 0.25f + (i % 2) * 0.15f, 2.5f),
                    new Vector3(25f + i * 3.5f, 0.2f, 215f), new Color(0.42f, 0.36f, 0.3f));
            }

            // Mud trench + 3D recovery bollard.
            var mud = Box(root.transform, "MudTrench", new Vector3(8f, 0.15f, 30f),
                new Vector3(60f, 0.02f, 60f), new Color(0.25f, 0.18f, 0.12f));
            mud.AddComponent<SurfaceLabel>().surface = SurfaceType.DeepMud;
            SpawnAsset(root.transform, StreamingAssetBindings.WinchBollardAssetId, "WinchAnchor_MudTrench",
                new Vector3(54f, 0f, 76f), new Color(.84f, .62f, .18f), addCollider: true, catalog: catalog)
                .AddComponent<WinchAnchor>();

            // Water channel with 3D creek depth gauges (§5.3) + timber bypass bridge span.
            var water = Box(root.transform, "WaterChannel", new Vector3(8f, 1.2f, 24f),
                new Vector3(80f, 0.2f, 60f), new Color(0.15f, 0.3f, 0.45f, 0.6f));
            var volume = water.AddComponent<WaterVolume>();
            volume.surfaceHeightY = .8f;
            water.GetComponent<Collider>().isTrigger = true;
            for (int i = 0; i < 3; i++)
            {
                SpawnAsset(root.transform, StreamingAssetBindings.WaterFordGaugeAssetId, $"DepthPole{i}",
                    new Vector3(76f + i * 0.6f, 0f, 48f + i * 2f), new Color(.88f, .76f, .18f),
                    addCollider: false, catalog: catalog);
            }
            SpawnAsset(root.transform, StreamingAssetBindings.BridgeSpanAssetId, "ProvingBridgeSpan",
                new Vector3(80f, 0f, 78f), new Color(.46f, .38f, .28f), addCollider: true, catalog: catalog);

            // Jump landing.
            Box(root.transform, "JumpRamp", new Vector3(8f, 0.5f, 12f), new Vector3(100f, 0.8f, 60f),
                new Color(0.45f, 0.4f, 0.35f)).transform.rotation = Quaternion.Euler(-18f, 0f, 0f);
            Box(root.transform, "JumpLanding", new Vector3(14f, 0.1f, 20f), new Vector3(100f, 0f, 95f),
                new Color(0.32f, 0.3f, 0.28f));

            // Proving workshop bay + fuel tank + cargo pallets + full 4-vehicle fleet showcase.
            SpawnAsset(root.transform, StreamingAssetBindings.HomeGarageAssetId, "ProvingGarageBay",
                new Vector3(-18f, 0f, 12f), new Color(.50f, .45f, .40f), addCollider: true, catalog: catalog);
            SpawnAsset(root.transform, StreamingAssetBindings.GarageFuelDepotAssetId, "ProvingFuelDepot",
                new Vector3(-26f, 0f, 14f), new Color(.56f, .58f, .60f), addCollider: true, catalog: catalog);
            SpawnAsset(root.transform, StreamingAssetBindings.CargoPalletStackAssetId, "ProvingCargoPallets",
                new Vector3(-10f, 0f, 10f), new Color(.62f, .49f, .28f), addCollider: true, catalog: catalog);
            SpawnFleetShowcase(root.transform, new Vector3(-24f, 0f, 28f), catalog);

            SpawnAsset(root.transform, StreamingAssetBindings.WatchtowerAssetId, "ProvingWatchtower",
                new Vector3(-24f, 0f, 90f), new Color(.48f, .38f, .28f), addCollider: true, catalog: catalog);
            SpawnAsset(root.transform, StreamingAssetBindings.TrailCairnAssetId, "ProvingCairn",
                new Vector3(-10f, 0f, 55f), new Color(.52f, .50f, .48f), addCollider: false, catalog: catalog);
            for (int i = 0; i < 6; i++)
            {
                SpawnAsset(root.transform, "env.tree.forest_pine", $"ProvingPine{i}",
                    new Vector3(-36f, 0f, 40f + i * 24f), new Color(.20f, .35f, .22f), addCollider: false, catalog: catalog);
                SpawnAsset(root.transform, "env.rock.granite_outcrop", $"ProvingGranite{i}",
                    new Vector3(132f, 0f, 40f + i * 24f), new Color(.42f, .42f, .44f), addCollider: true, catalog: catalog);
            }

            var vehicle = CreateScoutRig(root.transform, new Vector3(0f, 1f, 10f));
            var router = vehicle.gameObject.AddComponent<InputRouter>();
            var camera = Camera.main.gameObject.AddComponent<ChaseCameraBehaviour>();
            camera.target = vehicle.transform;
            var driver = new GameObject("Proving Ground Driver").AddComponent<ProvingGroundDriver>();
            driver.vehicle = vehicle;
            driver.input = router;
            driver.chaseCamera = camera;

            Save("PhysicsTestGround");
        }

        /// <summary>
        /// Vertical slice (§21.3, §14.2) generated from Content/vertical_slice.json and the canonical
        /// catalog in 1:1 WORLD coordinates, using verified 3D streaming meshes for the home garage,
        /// fleet apron, vista watchtowers, trail cairns, salvage caches, field camp shelters, relays,
        /// telemetry pods, bridge spans, creek gauges, winch bollards, and perimeter conifer forest.
        /// </summary>
        static void BuildVerticalSlice()
        {
            NewScene("VerticalSlice");
            var content = CampaignContent.FromDirectory(ProjectSetup.CanonicalContentDir);
            var catalog = content.StreamingAssets;
            var slice = content.Slice;
            var root = new GameObject("VerticalSliceGreybox").transform;
            float half = slice.AreaSizeMeters * .5f;
            float centerX = slice.AreaMinX + half;
            float centerZ = slice.AreaMinZ + half;
            Box(root, "Ground", new Vector3(slice.AreaSizeMeters, 1f, slice.AreaSizeMeters),
                new Vector3(centerX, -.5f, centerZ), new Color(.30f, .38f, .26f));

            var garage = content.Garages[slice.GarageId].Position;
            SpawnAsset(root, StreamingAssetBindings.HomeGarageAssetId, "Garage.Home",
                new Vector3(garage.X, 0f, garage.Z), new Color(.50f, .45f, .40f), addCollider: true, catalog: catalog);
            SpawnAsset(root, StreamingAssetBindings.GarageFuelDepotAssetId, "Garage.Home Fuel Depot",
                new Vector3(garage.X - 9f, 0f, garage.Z + 2f), new Color(.56f, .58f, .60f), addCollider: true, catalog: catalog);
            SpawnAsset(root, StreamingAssetBindings.CargoPalletStackAssetId, "Garage.Home Cargo Apron",
                new Vector3(garage.X + 8f, 0f, garage.Z - 2f), new Color(.62f, .49f, .28f), addCollider: true, catalog: catalog);
            SpawnFleetShowcase(root, new Vector3(garage.X - 18f, 0f, garage.Z + 12f), catalog);
            Marker(root, "Garage Arrival Pad", new Vector3(slice.ArrivalRadiusMeters * 2f, .04f, slice.ArrivalRadiusMeters * 2f),
                slice.ArrivalCenter, .02f, new Color(.55f, .55f, .30f));

            foreach (var l in slice.Landmarks)
            {
                var p = content.Landmarks[l.Id].Position;
                string lmAsset = StreamingAssetBindings.LandmarkAssetFor(l.Id, l.Vista, WorldBiome.Forest);
                // Solid BoxCollider on "Landmark <id>" preserves VerticalSliceDriver's LOS raycast check verbatim.
                SpawnAsset(root, lmAsset, "Landmark " + l.Id, new Vector3(p.X, 0f, p.Z),
                    l.Vista ? new Color(.85f, .75f, .35f) : new Color(.75f, .55f, .35f),
                    addCollider: true, catalog: catalog);
            }

            foreach (var id in slice.CacheIds)
            {
                var cp = content.Caches[id].Position;
                SpawnAsset(root, StreamingAssetBindings.SalvageCacheAssetId, "Cache " + id,
                    new Vector3(cp.X, 0f, cp.Z), new Color(.25f, .55f, .75f), addCollider: false, catalog: catalog);
            }

            foreach (var camp in slice.CampSockets)
            {
                Marker(root, "Camp Socket " + camp.Id, new Vector3(6f, .05f, 6f), camp.Position, .03f, new Color(.45f, .30f, .20f));
                SpawnAsset(root, StreamingAssetBindings.FieldCampAssetId, "Camp Shelter " + camp.Id,
                    new Vector3(camp.Position.X, 0f, camp.Position.Z), new Color(.58f, .46f, .32f),
                    addCollider: false, catalog: catalog);
            }

            foreach (var site in slice.InspectTargets)
            {
                string siteAsset = StreamingAssetBindings.WorldSiteAssetFor(site.Id, "inspect", WorldBiome.Forest);
                SpawnAsset(root, siteAsset, "Inspect " + site.Id,
                    new Vector3(site.Position.X, 0f, site.Position.Z), new Color(.85f, .76f, .24f),
                    addCollider: false, catalog: catalog);
            }

            foreach (var site in slice.QuestItemSites)
            {
                SpawnAsset(root, StreamingAssetBindings.SalvageCacheAssetId, "Mission Item " + site.Id,
                    new Vector3(site.Position.X, 0f, site.Position.Z), new Color(.95f, .85f, .20f),
                    addCollider: false, catalog: catalog);
            }

            foreach (var relay in slice.Relays)
            {
                SpawnAsset(root, StreamingAssetBindings.RelayTowerAssetId, "Relay " + relay.Id,
                    new Vector3(relay.Position.X, 0f, relay.Position.Z), new Color(.60f, .62f, .66f),
                    addCollider: true, catalog: catalog);
            }

            foreach (var route in slice.CheckpointRoutes)
                for (int i = 0; i < route.Checkpoints.Count; i++)
                {
                    var cp = route.Checkpoints[i];
                    foreach (float side in new[] { -route.RadiusMeters, route.RadiusMeters })
                        SpawnAsset(root, StreamingAssetBindings.TrailCairnAssetId, $"Checkpoint {route.Id}[{i}]",
                            new Vector3(cp.X + side, 0f, cp.Z), new Color(.95f, .55f, .15f),
                            addCollider: false, catalog: catalog);
                }

            foreach (var feature in slice.GreyboxFeatures)
            {
                var c = feature.Center;
                var size = feature.Size;
                switch (feature.Kind)
                {
                    case "water":
                        var water = Box(root, "Ford Water", new Vector3(size.X, size.Y, size.Z), new Vector3(c.X, size.Y * .5f, c.Z),
                            new Color(.15f, .3f, .45f, .6f));
                        water.AddComponent<WaterVolume>().surfaceHeightY = size.Y;
                        water.GetComponent<Collider>().isTrigger = true;
                        SpawnAsset(root, StreamingAssetBindings.WaterFordGaugeAssetId, "Ford Depth Gauge West",
                            new Vector3(c.X - size.X * .5f - 1.5f, 0f, c.Z), new Color(.88f, .76f, .18f),
                            addCollider: false, catalog: catalog);
                        SpawnAsset(root, StreamingAssetBindings.WaterFordGaugeAssetId, "Ford Depth Gauge East",
                            new Vector3(c.X + size.X * .5f + 1.5f, 0f, c.Z), new Color(.88f, .76f, .18f),
                            addCollider: false, catalog: catalog);
                        SpawnAsset(root, StreamingAssetBindings.BridgeSpanAssetId, "Ford Timber Bridge Span",
                            new Vector3(c.X - size.X * .5f - 9f, 0f, c.Z), new Color(.46f, .38f, .28f),
                            addCollider: true, catalog: catalog);
                        break;
                    case "mud":
                        Box(root, "Mud Patch", new Vector3(size.X, size.Y, size.Z), new Vector3(c.X, .02f, c.Z),
                            new Color(.25f, .18f, .12f)).AddComponent<SurfaceLabel>().surface = SurfaceType.DeepMud;
                        break;
                    case "hill":
                        float rise = size.Y, run = size.Z * .35f;
                        float angle = Mathf.Atan2(rise, run) * Mathf.Rad2Deg, slope = Mathf.Sqrt(rise * rise + run * run);
                        var hillColor = new Color(.40f, .45f, .30f);
                        Box(root, "Hill Up", new Vector3(size.X, .5f, slope), new Vector3(c.X, rise * .5f - .25f, c.Z - size.Z * .5f + run * .5f),
                            hillColor).transform.rotation = Quaternion.Euler(-angle, 0f, 0f);
                        Box(root, "Hill Top", new Vector3(size.X, rise, size.Z - 2f * run), new Vector3(c.X, rise * .5f, c.Z), hillColor);
                        Box(root, "Hill Down", new Vector3(size.X, .5f, slope), new Vector3(c.X, rise * .5f - .25f, c.Z + size.Z * .5f - run * .5f),
                            hillColor).transform.rotation = Quaternion.Euler(angle, 0f, 0f);
                        break;
                }
            }

            // Authoritative road-shoulder winch bollards around the ford, mud trench, and hill climb (§4.5, KI-028).
            var anchorPoints = new[]
            {
                new Vector3(985f, 0f, 2322f),
                new Vector3(985f, 0f, 2378f),
                new Vector3(1110f, 0f, 2060f),
                new Vector3(1110f, 0f, 2120f),
                new Vector3(792f, 0f, 2410f),
                new Vector3(828f, 0f, 2500f)
            };
            for (int i = 0; i < anchorPoints.Length; i++)
            {
                var post = SpawnAsset(root, StreamingAssetBindings.WinchBollardAssetId, $"Winch Anchor {i + 1}",
                    anchorPoints[i], new Color(.84f, .62f, .18f), addCollider: true, catalog: catalog);
                post.AddComponent<WinchAnchor>();
            }

            // Perimeter forest pines & granite outcrops framing the 1 km² basin.
            for (int i = 0; i < 20; i++)
            {
                float rad = i * (360f / 20f) * Mathf.Deg2Rad;
                SpawnAsset(root, "env.tree.forest_pine", $"SlicePine{i}",
                    new Vector3(centerX + Mathf.Cos(rad) * (half - 24f), 0f, centerZ + Mathf.Sin(rad) * (half - 24f)),
                    new Color(.20f, .35f, .22f), addCollider: false, catalog: catalog);
                if (i % 2 == 0)
                {
                    SpawnAsset(root, "env.rock.granite_outcrop", $"SliceGranite{i}",
                        new Vector3(centerX + Mathf.Cos(rad + .14f) * (half - 14f), 0f, centerZ + Mathf.Sin(rad + .14f) * (half - 14f)),
                        new Color(.42f, .42f, .44f), addCollider: true, catalog: catalog);
                }
            }

            var spawn = slice.GarageSpawn;
            var vehicle = CreateScoutRig(root, new Vector3(spawn.Position.X, spawn.Position.Y + .4f, spawn.Position.Z));
            vehicle.transform.rotation = Quaternion.Euler(0f, spawn.YawDegrees, 0f);
            var router = vehicle.gameObject.AddComponent<InputRouter>();
            Camera.main.farClipPlane = 1500f;
            var camera = Camera.main.gameObject.AddComponent<ChaseCameraBehaviour>();
            camera.target = vehicle.transform;
            var driver = new GameObject("Vertical Slice Driver").AddComponent<VerticalSliceDriver>();
            driver.vehicle = vehicle;
            driver.input = router;
            driver.chaseCamera = camera;
            Save("VerticalSlice");
        }

        /// <summary>
        /// Streaming mainland world shell (§6.4, §14.2, KI-037): initializes <see cref="WorldStreamer"/>
        /// over the baked continental heightfield, five-biome route network, scatter surface, active
        /// campaign vehicle rig, AND places verified 3D streaming meshes across all 5 garages, camps,
        /// landmarks, caches, relics, and regional biome overlooks.
        /// </summary>
        static void BuildWorldShell()
        {
            NewScene("WorldShell");
            var content = CampaignContent.FromDirectory(ProjectSetup.CanonicalContentDir);
            var catalog = content.StreamingAssets;
            var routes = MainlandRouteGraph.FromFile(
                Path.Combine(ProjectSetup.CanonicalContentDir, "routes.json"));
            var noise = new NoiseField();

            var root = new GameObject("WorldShell").transform;
            var streamer = root.gameObject.AddComponent<WorldStreamer>();
            streamer.TerrainMaterial = Material(new Color(.33f, .40f, .28f));
            streamer.PropMaterial = Material(new Color(.24f, .33f, .21f));
            streamer.WinchAnchorMaterial = Material(new Color(.84f, .62f, .18f));
            streamer.Initialize(routes, catalog: catalog);

            // Continental 3D POIs across all 5 regions (Home & Outpost Garages, Camps, Landmarks, Caches, Relics, Biome Landmarks)
            var poiRoot = new GameObject("ContinentalPOIs").transform;
            poiRoot.SetParent(root, false);

            foreach (var g in content.Garages.Values)
            {
                float gy = noise.BaseHeight(g.Position.X, g.Position.Z);
                Vector3 bayPos = new Vector3(g.Position.X - 14f, gy, g.Position.Z - 12f);
                SpawnAsset(poiRoot, StreamingAssetBindings.GarageBuildingFor(g.Id), "Garage " + g.Id,
                    bayPos, new Color(.50f, .45f, .40f), addCollider: true, catalog: catalog);
                SpawnAsset(poiRoot, StreamingAssetBindings.GarageFuelDepotAssetId, "FuelDepot " + g.Id,
                    bayPos + new Vector3(-9f, 0f, 2f), new Color(.56f, .58f, .60f), addCollider: true, catalog: catalog);
                SpawnAsset(poiRoot, StreamingAssetBindings.CargoPalletStackAssetId, "CargoApron " + g.Id,
                    bayPos + new Vector3(8f, 0f, -2f), new Color(.62f, .49f, .28f), addCollider: true, catalog: catalog);
            }

            foreach (var camp in content.Camps.Values)
            {
                float cy = noise.BaseHeight(camp.Position.X, camp.Position.Z);
                SpawnAsset(poiRoot, StreamingAssetBindings.FieldCampAssetId, "Camp " + camp.Id,
                    new Vector3(camp.Position.X, cy, camp.Position.Z), new Color(.58f, .46f, .32f),
                    addCollider: true, catalog: catalog);
            }

            foreach (var lm in content.Landmarks.Values)
            {
                float ly = noise.BaseHeight(lm.Position.X, lm.Position.Z);
                var biome = WorldHydrology.NearestBiome(lm.Position.X, lm.Position.Z);
                string lmAsset = StreamingAssetBindings.LandmarkAssetFor(lm.Id, lm.Vista, biome);
                SpawnAsset(poiRoot, lmAsset, "Landmark " + lm.Id,
                    new Vector3(lm.Position.X, ly, lm.Position.Z),
                    lm.Vista ? new Color(.85f, .75f, .35f) : new Color(.70f, .56f, .40f),
                    addCollider: true, catalog: catalog);
            }

            foreach (var cache in content.Caches.Values)
            {
                float cy = noise.BaseHeight(cache.Position.X, cache.Position.Z);
                SpawnAsset(poiRoot, StreamingAssetBindings.SalvageCacheAssetId, "Cache " + cache.Id,
                    new Vector3(cache.Position.X, cy, cache.Position.Z), new Color(.25f, .55f, .75f),
                    addCollider: false, catalog: catalog);
            }

            foreach (var relic in content.Relics.Values)
            {
                float ry = noise.BaseHeight(relic.Position.X, relic.Position.Z);
                SpawnAsset(poiRoot, StreamingAssetBindings.TelemetryPodAssetId, "Relic " + relic.Id,
                    new Vector3(relic.Position.X, ry, relic.Position.Z), new Color(.86f, .74f, .28f),
                    addCollider: false, catalog: catalog);
            }

            // Signature regional setpieces ensuring all 5 biome landmarks (observatory, overpass, pump station, hoodoo, cypress, larch, basalt) stand in WorldShell
            PlaceWorldSetpiece(poiRoot, StreamingAssetBindings.HighlandObservatoryAssetId, "HighlandObservatoryDome", 4920f, 5120f, new Color(.72f, .75f, .80f), noise, catalog);
            PlaceWorldSetpiece(poiRoot, StreamingAssetBindings.WetlandPumpStationAssetId, "WetlandPumpStation", 2140f, 4240f, new Color(.44f, .50f, .48f), noise, catalog);
            PlaceWorldSetpiece(poiRoot, StreamingAssetBindings.CityOverpassAssetId, "RuinedCityOverpass", 3920f, 3120f, new Color(.48f, .48f, .50f), noise, catalog);
            PlaceWorldSetpiece(poiRoot, "env.rock.canyon_hoodoo", "CanyonSandstoneHoodoo", 2920f, 1840f, new Color(.72f, .42f, .28f), noise, catalog);
            PlaceWorldSetpiece(poiRoot, "env.tree.wetland_cypress", "WetlandButtressedCypress", 2160f, 4260f, new Color(.22f, .36f, .24f), noise, catalog);
            PlaceWorldSetpiece(poiRoot, "env.tree.highland_larch", "HighlandWindsweptLarch", 4945f, 5135f, new Color(.24f, .38f, .26f), noise, catalog);
            PlaceWorldSetpiece(poiRoot, "env.rock.basalt_column_cluster", "HighlandBasaltColumns", 4960f, 5150f, new Color(.28f, .29f, .32f), noise, catalog);

            var home = content.Garages["garage.home"].Position;
            float groundY = noise.BaseHeight(home.X, home.Z);
            streamer.StepAt(home.X, home.Z);

            var vehicle = CreateScoutRig(root, new Vector3(home.X, groundY + 0.85f, home.Z));
            streamer.FollowTarget = vehicle.transform;
            var router = vehicle.gameObject.AddComponent<InputRouter>();
            Camera.main.farClipPlane = 2200f;
            var camera = Camera.main.gameObject.AddComponent<ChaseCameraBehaviour>();
            camera.target = vehicle.transform;
            var driver = new GameObject("Mainland Driver").AddComponent<VerticalSliceDriver>();
            driver.vehicle = vehicle;
            driver.input = router;
            driver.chaseCamera = camera;
            Save("WorldShell");
        }

        static void PlaceWorldSetpiece(
            Transform parent,
            string assetId,
            string name,
            float x,
            float z,
            Color color,
            NoiseField noise,
            IReadOnlyDictionary<string, StreamingAssetDefinition> catalog)
        {
            float y = noise.BaseHeight(x, z);
            SpawnAsset(parent, assetId, name, new Vector3(x, y, z), color, addCollider: true, catalog: catalog);
        }

        static void SpawnFleetShowcase(
            Transform parent,
            Vector3 origin,
            IReadOnlyDictionary<string, StreamingAssetDefinition> catalog)
        {
            var fleetRoot = new GameObject("ExpeditionFleetShowcase").transform;
            fleetRoot.SetParent(parent, false);
            fleetRoot.position = origin;

            var tireMat = Material(new Color(.11f, .12f, .13f));
            (string vehicleId, string label, Color color)[] fleet =
            {
                ("vehicle.mule.m2", "Fleet_Mule_M2", new Color(.55f, .48f, .30f)),
                ("vehicle.lynx.l3", "Fleet_Lynx_L3", new Color(.28f, .52f, .58f)),
                ("vehicle.atlas.a4", "Fleet_Atlas_A4", new Color(.64f, .30f, .24f))
            };

            Vector3[] wheelOffsets =
            {
                new Vector3(-.88f, .38f, 1.35f),
                new Vector3(.88f, .38f, 1.35f),
                new Vector3(-.88f, .38f, -1.35f),
                new Vector3(.88f, .38f, -1.35f)
            };

            for (int i = 0; i < fleet.Length; i++)
            {
                Vector3 bayOrigin = origin + new Vector3(i * 4.2f, 0f, 0f);
                SpawnAsset(fleetRoot, StreamingAssetBindings.ChassisForVehicle(fleet[i].vehicleId),
                    fleet[i].label + "_Chassis", bayOrigin, fleet[i].color, addCollider: true, catalog: catalog);
                for (int w = 0; w < wheelOffsets.Length; w++)
                {
                    var wheelGo = SpawnAsset(fleetRoot, StreamingAssetBindings.WheelForVehicle(fleet[i].vehicleId),
                        $"{fleet[i].label}_Wheel_{w}", bayOrigin + wheelOffsets[w],
                        new Color(.11f, .12f, .13f), addCollider: false, catalog: catalog);
                    wheelGo.GetComponent<Renderer>().sharedMaterial = tireMat;
                }
            }
        }

        static GameObject SpawnAsset(
            Transform parent,
            string assetId,
            string name,
            Vector3 position,
            Color color,
            bool addCollider,
            IReadOnlyDictionary<string, StreamingAssetDefinition> catalog = null)
        {
            var map = catalog ?? CampaignContent.FromDirectory(ProjectSetup.CanonicalContentDir).StreamingAssets;
            var def = map[assetId];
            var go = StreamingMeshRuntime.InstantiateModel(
                def,
                Material(color),
                parent,
                addBoxCollider: addCollider,
                meshResolver: ProjectSetup.LoadOrCreateStreamingMeshAsset);
            go.name = name;
            go.transform.position = position;
            return go;
        }

        /// <summary>Visual-only marker (no collider) standing on the ground at <paramref name="p"/>.</summary>
        static GameObject Marker(Transform parent, string name, Vector3 scale, WorldPoint p, float centerY, Color color)
        {
            var go = Box(parent, name, scale, new Vector3(p.X, centerY, p.Z), color);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static VehicleController CreateScoutRig(Transform parent, Vector3 position)
        {
            var catalog = CampaignContent.FromDirectory(ProjectSetup.CanonicalContentDir).StreamingAssets;
            var go = new GameObject("Scout S1");
            go.transform.SetParent(parent, false);
            go.transform.position = position;
            go.layer = 2; // Ignore Raycast: the chase camera must not collide with its own rig.
            var body = go.AddComponent<Rigidbody>();
            var spec = VehicleSpec.ScoutS1();
            body.mass = spec.DryMassKg + spec.FuelCapacityLiters * VehicleSpec.FuelDensityKgPerLiter;
            body.centerOfMass = new Vector3(0f, .1f, 0f);
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            var hull = go.AddComponent<BoxCollider>();
            hull.center = new Vector3(0f, .30f, 0f);
            hull.size = new Vector3(1.7f, .7f, 3.9f);

            var controller = go.AddComponent<VehicleController>();
            controller.spec = spec;
            var wheels = new WheelCollider[4];
            var visuals = new Transform[4];
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2;
                var wheelGo = new GameObject(new[] { "Front Left", "Front Right", "Rear Left", "Rear Right" }[i]);
                wheelGo.transform.SetParent(go.transform, false);
                wheelGo.layer = 2;
                wheelGo.transform.localPosition = new Vector3((i % 2 == 0 ? -1f : 1f) * spec.TrackM * .5f,
                    0f, (front ? 1f : -1f) * spec.WheelbaseM * .5f);
                var wheel = wheelGo.AddComponent<WheelCollider>();
                wheel.radius = spec.WheelRadiusM;
                wheel.mass = 20f;
                wheel.suspensionDistance = spec.SuspensionTravelM;
                wheel.suspensionSpring = new JointSpring
                {
                    spring = front ? spec.SpringRateFrontNm : spec.SpringRateRearNm,
                    damper = front ? spec.DamperFrontNsm : spec.DamperRearNsm,
                    targetPosition = .5f
                };
                wheel.sprungMass = body.mass * (front ? spec.FrontAxleLoadFraction : 1f - spec.FrontAxleLoadFraction) * .5f;
                wheels[i] = wheel;
                var pivot = new GameObject(wheelGo.name + " Visual").transform;
                pivot.gameObject.layer = 2;
                pivot.SetParent(go.transform, false);
                pivot.localPosition = wheelGo.transform.localPosition;
                visuals[i] = pivot;
            }
            controller.wheelFrontLeft = wheels[0]; controller.wheelFrontRight = wheels[1];
            controller.wheelRearLeft = wheels[2]; controller.wheelRearRight = wheels[3];
            controller.wheelVisualFrontLeft = visuals[0]; controller.wheelVisualFrontRight = visuals[1];
            controller.wheelVisualRearLeft = visuals[2]; controller.wheelVisualRearRight = visuals[3];

            StreamingMeshRuntime.ApplyVehicleVisuals(
                controller,
                "vehicle.scout.s1",
                showCargoStack: false,
                chassisMaterial: Material(new Color(.66f, .43f, .22f)),
                wheelMaterial: Material(new Color(.10f, .11f, .12f)),
                cargoMaterial: Material(new Color(.62f, .49f, .28f)),
                catalog: catalog,
                meshResolver: ProjectSetup.LoadOrCreateStreamingMeshAsset);

            return controller;
        }

        static Material Material(Color color)
        {
            const string dir = "Assets/Wayfinder/Art/Generated/Greybox";
            Directory.CreateDirectory(dir);
            if (!UnityEditor.AssetDatabase.IsValidFolder(dir)) UnityEditor.AssetDatabase.Refresh();
            string path = $"{dir}/Color_{ColorUtility.ToHtmlStringRGBA(color)}.mat";
            var material = UnityEditor.AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null) return material;
            var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
            if (shader == null) throw new System.InvalidOperationException("URP Simple Lit shader missing; resolve URP packages first.");
            material = new Material(shader) { color = color };
            UnityEditor.AssetDatabase.CreateAsset(material, path);
            return material;
        }

        static GameObject Box(Transform parent, string name, Vector3 scale, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localScale = scale;
            go.transform.position = position;
            go.GetComponent<Renderer>().sharedMaterial = Material(color);
            return go;
        }
    }
}
