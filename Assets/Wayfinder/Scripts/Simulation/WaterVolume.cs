using UnityEngine;

namespace Wayfinder.Simulation
{
    public sealed class WaterVolume : MonoBehaviour
    {
        public float surfaceHeightY;
        public Vector3 flowDirection = Vector3.forward;
        public float currentStrength;
        public bool highState;
    }
}
