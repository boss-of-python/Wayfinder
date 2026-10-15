using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wayfinder.Campaign;
using Wayfinder.Platform;
using Wayfinder.Core;
using Wayfinder.Simulation;
using Wayfinder.Presentation;

namespace Wayfinder.Editor
{
    /// <summary>
    /// Deterministic scene generation (D-006): Bootstrap, Title, PhysicsTestGround (with the full
    /// §4.6 obstacle set), and VerticalSlice greybox. Menu: Tools → Wayfinder → Build Scenes.
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
            EditorSceneManager.OpenScene($"{ScenesDir}/Bootstrap.unity");
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene($"{ScenesDir}/Bootstrap.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/Title.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/PhysicsTestGround.unity", true),
                new EditorBuildSettingsScene($"{ScenesDir}/VerticalSlice.unity", true)
            };
            Debug.Log("[Wayfinder] Scenes built and registered in EditorBuildSettings.");
        }

        static void NewScene(string name)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
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
            // Bootstrap host: composition root wires explicit services at boot (§15.3).
            // App flow: Boot → validate data → Title (AppStateMachine).
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
        /// PhysicsTestGround (§4.6): asphalt braking lane, 10/20/30/35° ramps, 10/20/28° side
        /// slopes, 0.10/0.20/0.30 m ledges, articulation blocks, mud trench, water channel,
        /// jump landing. Unloaded/loaded tests run against this rig in M1.
        /// </summary>
        static void BuildPhysicsTestGround()
        {
            NewScene("PhysicsTestGround");
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

            // Mud trench (surface label shallow/deep mud).
            var mud = Box(root.transform, "MudTrench", new Vector3(8f, 0.15f, 30f),
                new Vector3(60f, 0.02f, 60f), new Color(0.25f, 0.18f, 0.12f));
            mud.AddComponent<SurfaceLabel>().surface = SurfaceType.DeepMud;

            // Water channel (water volume; depth poles beside it).
            var water = Box(root.transform, "WaterChannel", new Vector3(8f, 1.2f, 24f),
                new Vector3(80f, 0.2f, 60f), new Color(0.15f, 0.3f, 0.45f, 0.6f));
            var volume = water.AddComponent<WaterVolume>();
            volume.surfaceHeightY = .8f;
            water.GetComponent<Collider>().isTrigger = true;
            for (int i = 0; i < 3; i++)
            {
                Box(root.transform, $"DepthPole{i}", new Vector3(0.1f, 1.2f, 0.1f),
                    new Vector3(76f + i * 0.6f, 0.6f, 48f + i * 2f), Color.yellow); // visible depth poles (§5.3)
            }

            // Jump landing.
            Box(root.transform, "JumpRamp", new Vector3(8f, 0.5f, 12f), new Vector3(100f, 0.8f, 60f),
                new Color(0.45f, 0.4f, 0.35f)).transform.rotation = Quaternion.Euler(-18f, 0f, 0f);
            Box(root.transform, "JumpLanding", new Vector3(14f, 0.1f, 20f), new Vector3(100f, 0f, 95f),
                new Color(0.32f, 0.3f, 0.28f));

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
        /// Vertical slice greybox (§21.3) generated from Content/vertical_slice.json and the canonical
        /// catalog, in WORLD coordinates so content positions map 1:1: 1024 m ground, home garage and
        /// arrival pad, six landmark markers, twelve caches, two camp sockets, the creek gauge, ford
        /// checkpoints over shallow water, relay mast, fuse site, mud and a hill. The Scout starts at the
        /// garage spawn with the slice driver wired. Markers are visual only — interaction is
        /// distance-based in Wayfinder.Campaign, so decorative markers carry no colliders.
        /// </summary>
        static void BuildVerticalSlice()
        {
            NewScene("VerticalSlice");
            var content = CampaignContent.FromDirectory("Assets/Wayfinder/Content");
            var slice = content.Slice;
            var root = new GameObject("VerticalSliceGreybox").transform;
            float half = slice.AreaSizeMeters * .5f;
            Box(root, "Ground", new Vector3(slice.AreaSizeMeters, 1f, slice.AreaSizeMeters),
                new Vector3(slice.AreaMinX + half, -.5f, slice.AreaMinZ + half), new Color(.30f, .38f, .26f));

            var garage = content.Garages[slice.GarageId].Position;
            Box(root, "Garage.Home", new Vector3(12f, 5f, 10f), new Vector3(garage.X, 2.5f, garage.Z), new Color(.5f, .45f, .4f));
            Marker(root, "Garage Arrival Pad", new Vector3(slice.ArrivalRadiusMeters * 2f, .04f, slice.ArrivalRadiusMeters * 2f),
                slice.ArrivalCenter, .02f, new Color(.55f, .55f, .30f));

            foreach (var l in slice.Landmarks)
            {
                var p = content.Landmarks[l.Id].Position;
                // Solid marker; VerticalSliceDriver's line-of-sight test treats "Landmark …" hits as visible.
                Box(root, "Landmark " + l.Id, new Vector3(3f, 12f, 3f), new Vector3(p.X, 6f, p.Z),
                    l.Vista ? new Color(.85f, .75f, .35f) : new Color(.75f, .55f, .35f));
            }
            foreach (var id in slice.CacheIds)
                Marker(root, "Cache " + id, new Vector3(1.2f, .8f, 1.2f), content.Caches[id].Position, .4f, new Color(.25f, .55f, .75f));
            foreach (var camp in slice.CampSockets)
            {
                Marker(root, "Camp Socket " + camp.Id, new Vector3(6f, .05f, 6f), camp.Position, .03f, new Color(.45f, .30f, .20f));
                Marker(root, "Camp Post " + camp.Id, new Vector3(.3f, 2.5f, .3f), camp.Position, 1.25f, new Color(.85f, .45f, .20f));
            }
            foreach (var site in slice.InspectTargets)
                Marker(root, "Inspect " + site.Id, new Vector3(.3f, 3f, .3f), site.Position, 1.5f, Color.yellow);
            foreach (var site in slice.QuestItemSites)
                Marker(root, "Mission Item " + site.Id, new Vector3(.8f, .6f, .8f), site.Position, .3f, new Color(.95f, .85f, .2f));
            foreach (var relay in slice.Relays)
                Box(root, "Relay " + relay.Id, new Vector3(1f, 18f, 1f), new Vector3(relay.Position.X, 9f, relay.Position.Z),
                    new Color(.6f, .6f, .65f));
            foreach (var route in slice.CheckpointRoutes)
                for (int i = 0; i < route.Checkpoints.Count; i++)
                {
                    var cp = route.Checkpoints[i];
                    foreach (float side in new[] { -route.RadiusMeters, route.RadiusMeters })
                        Marker(root, $"Checkpoint {route.Id}[{i}]", new Vector3(.25f, 2f, .25f),
                            new WorldPoint(cp.X + side, 0f, cp.Z), 1f, new Color(.95f, .55f, .15f));
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

        /// <summary>Visual-only marker (no collider) standing on the ground at <paramref name="p"/>.</summary>
        static GameObject Marker(Transform parent, string name, Vector3 scale, WorldPoint p, float centerY, Color color)
        {
            var go = Box(parent, name, scale, new Vector3(p.X, centerY, p.Z), color);
            Object.DestroyImmediate(go.GetComponent<Collider>());
            return go;
        }

        public static VehicleController CreateScoutRig(Transform parent, Vector3 position)
        {
            var go = new GameObject("Scout S1 Greybox");
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
            var mesh = Box(go.transform, "Body Greybox", hull.size, position + hull.center, new Color(.66f, .43f, .22f));
            Object.DestroyImmediate(mesh.GetComponent<Collider>());
            mesh.layer = 2;
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
                pivot.SetParent(go.transform, false);
                pivot.localPosition = wheelGo.transform.localPosition;
                var tire = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                tire.name = "Tire Greybox";
                tire.layer = 2;
                tire.transform.SetParent(pivot, false);
                tire.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                tire.transform.localScale = new Vector3(spec.WheelRadiusM * 2f, .13f, spec.WheelRadiusM * 2f);
                Object.DestroyImmediate(tire.GetComponent<Collider>());
                tire.GetComponent<Renderer>().sharedMaterial = Material(new Color(.10f, .11f, .12f));
                visuals[i] = pivot;
            }
            controller.wheelFrontLeft = wheels[0]; controller.wheelFrontRight = wheels[1];
            controller.wheelRearLeft = wheels[2]; controller.wheelRearRight = wheels[3];
            controller.wheelVisualFrontLeft = visuals[0]; controller.wheelVisualFrontRight = visuals[1];
            controller.wheelVisualRearLeft = visuals[2]; controller.wheelVisualRearRight = visuals[3];
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
