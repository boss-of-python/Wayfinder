using UnityEngine;
using Wayfinder.Core;
using Wayfinder.Simulation;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Composition root placeholder (§15.3): wires explicit services at boot. No God-object
    /// GameManager — small explicit services. Expanded through M2.
    /// </summary>
    public sealed class BootstrapHost : MonoBehaviour
    {
        public LifecycleHandler lifecycle;

        void Awake()
        {
            // Boot → validate data → Title (AppStateMachine transitions are explicit commands).
            Debug.Log("[Wayfinder] Boot: validating content catalog…");
            if (lifecycle == null) lifecycle = gameObject.AddComponent<LifecycleHandler>();
        }

        void Start()
        {
            Debug.Log("[Wayfinder] Boot complete → Title.");
        }
    }

    /// <summary>Title flow host: New Expedition / Continue / Settings (§2.1). No account, no network.</summary>
    public sealed class TitleFlowHost : MonoBehaviour
    {
        void Start() => Debug.Log("[Wayfinder] Title: New Expedition / Continue / Settings.");
    }
}

namespace Wayfinder.Simulation
{
    /// <summary>Surface label component: explicit gameplay surface on a mesh (§4.4).</summary>
    public sealed class SurfaceLabel : MonoBehaviour
    {
        public SurfaceType surface = SurfaceType.PackedDirt;
    }

    /// <summary>
    /// Authored water volume (§5.3): surface height, depth samples, flow direction and current
    /// strength. Water level may only use prevalidated low/high states (§6.2 D).
    /// </summary>
    public sealed class WaterVolume : MonoBehaviour
    {
        public float surfaceHeightY;
        public Vector3 flowDirection = Vector3.forward;
        public float currentStrength;
        public bool highState;
    }
}
