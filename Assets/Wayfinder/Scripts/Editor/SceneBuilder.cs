using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Wayfinder.Platform;
using Wayfinder.Simulation;

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

            // Flat asphalt braking lane 300 m.
            Box(root.transform, "BrakingLane", new Vector3(12f, 0.1f, 300f), new Vector3(0f, 0f, 150f),
                new Color(0.25f, 0.25f, 0.27f));

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
                new Vector3(80f, -0.4f, 60f), new Color(0.15f, 0.3f, 0.45f, 0.6f));
            water.AddComponent<WaterVolume>();
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

            // Vehicle spawn (Scout S1 rig placeholder — M1 replaces mesh, keeps systems).
            var vehicle = GameObject.CreatePrimitive(PrimitiveType.Cube);
            vehicle.name = "ScoutS1_Spawn";
            vehicle.transform.SetParent(root.transform);
            vehicle.transform.position = new Vector3(0f, 1.2f, 0f);
            vehicle.transform.localScale = new Vector3(1.8f, 1.2f, 4.2f);
            var rb = vehicle.AddComponent<Rigidbody>();
            rb.mass = 1350f;
            vehicle.AddComponent<VehicleController>();

            Save("PhysicsTestGround");
        }

        /// <summary>Vertical slice greybox (§21.3): 1024 m forest test area placeholder ground.</summary>
        static void BuildVerticalSlice()
        {
            NewScene("VerticalSlice");
            var root = new GameObject("VerticalSliceGreybox");
            Box(root.transform, "Ground", new Vector3(1024f, 1f, 1024f), new Vector3(512f, -0.5f, 512f),
                new Color(0.30f, 0.38f, 0.26f));

            var garage = Box(root.transform, "GarageHome", new Vector3(12f, 5f, 10f),
                new Vector3(1024f + 0f, 2.5f, 2304f - 2304f + 512f), new Color(0.5f, 0.45f, 0.4f));
            garage.name = "Garage.Home";
            Save("VerticalSlice");
        }

        static GameObject Box(Transform parent, string name, Vector3 scale, Vector3 position, Color color)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent);
            go.transform.localScale = scale;
            go.transform.position = position;
            var renderer = go.GetComponent<Renderer>();
            if (renderer != null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Simple Lit");
                if (shader == null) shader = Shader.Find("Standard");
                var mat = new Material(shader) { color = color };
                renderer.sharedMaterial = mat;
            }
            return go;
        }
    }
}
