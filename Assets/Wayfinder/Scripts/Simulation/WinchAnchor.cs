using System.Collections.Generic;
using UnityEngine;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Static world winch anchor marker (Appendix C: sturdy trees, boulders, recovery posts,
    /// utility poles — never dynamic vehicles). Self-registers while enabled so field queries
    /// remain zero-allocation and require no scene-wide FindObjectsByType calls per frame.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class WinchAnchor : MonoBehaviour
    {
        static readonly List<WinchAnchor> Registry = new List<WinchAnchor>(128);

        [SerializeField] string anchorId = "";
        [SerializeField] Vector3 attachOffset = new Vector3(0f, 0.8f, 0f);

        public string AnchorId => string.IsNullOrEmpty(anchorId) ? gameObject.name : anchorId;
        public Vector3 WorldAttachPoint => transform.TransformPoint(attachOffset);
        public static IReadOnlyList<WinchAnchor> ActiveAnchors => Registry;

        public void Configure(string id, Vector3 localOffset)
        {
            anchorId = id ?? string.Empty;
            attachOffset = localOffset;
        }

        void OnEnable()
        {
            if (!Registry.Contains(this))
                Registry.Add(this);
        }

        void OnDisable() => Registry.Remove(this);
    }
}
