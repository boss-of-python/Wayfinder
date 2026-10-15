using System;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Third-person chase camera (§3.4): distance 5.8 m, height 2.4 m, velocity look-ahead 2–6 m,
    /// FOV 60°→68°, spherecast obstruction (r 0.25 m), orbit ±160° yaw / −15..+55° pitch,
    /// recenter after 2 s without camera input. Independent toggles for shake/FOV motion/
    /// horizon stabilization/recenter (accessibility §3.5).
    /// </summary>
    public sealed class ChaseCameraBehaviour : MonoBehaviour
    {
        [Header("References")]
        public Transform target;

        [Header("Tuning (blueprint §3.4)")]
        public float distance = 5.8f;
        public float height = 2.4f;
        public float lookAheadMin = 2f;
        public float lookAheadMax = 6f;
        public float fovNormal = 60f;
        public float fovMax = 68f;
        public float positionSmoothingSeconds = 0.15f;
        public float lookSmoothingSeconds = 0.10f;
        public float obstructionRadius = 0.25f;
        public float obstructionRecoverSeconds = 0.4f;
        public float orbitYawLimitDegrees = 160f;
        public float orbitPitchMinDegrees = -15f;
        public float orbitPitchMaxDegrees = 55f;
        public float recenterDelaySeconds = 2f;

        [Header("Accessibility toggles")]
        public bool enableFovMotion = true;
        public bool enableHorizonStabilization = true;
        public bool enableRecenter = true;

        Camera _cam;
        float _yawOffset;
        float _pitchOffset = 8f;
        float _lastCameraInputTime = -999f;
        float _currentDistance;
        Vector3 _smoothedPosition;
        Vector3 _lookVelocity;
        Vector3 _smoothedLookPoint;
        bool _initialized;

        public void NotifyCameraInput() => _lastCameraInputTime = Time.time;

        public void SetOrbitInput(float yawDelta, float pitchDelta)
        {
            _yawOffset = MathUtil.Clamp(_yawOffset + yawDelta, -orbitYawLimitDegrees, orbitYawLimitDegrees);
            _pitchOffset = MathUtil.Clamp(_pitchOffset + pitchDelta, orbitPitchMinDegrees, orbitPitchMaxDegrees);
            NotifyCameraInput();
        }

        void Awake()
        {
            _cam = GetComponent<Camera>();
            if (_cam == null) _cam = Camera.main;
            _currentDistance = distance;
        }

        void LateUpdate()
        {
            if (target == null) return;

            float dt = Time.deltaTime;

            // Recenter after 2 s of driving without camera input.
            if (enableRecenter && Time.time - _lastCameraInputTime > recenterDelaySeconds)
            {
                _yawOffset = MathUtil.Lerp(_yawOffset, 0f, MathUtil.SmoothFactor(0.35f, dt));
                _pitchOffset = MathUtil.Lerp(_pitchOffset, 8f, MathUtil.SmoothFactor(0.35f, dt));
            }

            Quaternion orbit = Quaternion.Euler(_pitchOffset, target.eulerAngles.y + _yawOffset, 0f);
            Vector3 desiredPosition = target.position + orbit * new Vector3(0f, 0f, -distance) + Vector3.up * height;

            // Obstruction: spherecast from target toward camera; shorten immediately, recover slowly.
            float desiredDistance = distance;
            Vector3 castOrigin = target.position + Vector3.up * (height * 0.5f);
            Vector3 castDir = (desiredPosition - castOrigin).normalized;
            float castDist = Vector3.Distance(castOrigin, desiredPosition);
            if (Physics.SphereCast(castOrigin, obstructionRadius, castDir, out RaycastHit hit, castDist,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
            {
                desiredDistance = Mathf.Max(1.2f, hit.distance - obstructionRadius);
                _currentDistance = desiredDistance; // immediate shorten
            }
            else
            {
                // Slow recovery (0.4 s time constant).
                _currentDistance = Mathf.Lerp(_currentDistance, desiredDistance,
                    MathUtil.SmoothFactor(obstructionRecoverSeconds, dt));
            }

            desiredPosition = target.position + orbit * new Vector3(0f, 0f, -_currentDistance) + Vector3.up * height;

            if (!_initialized)
            {
                _smoothedPosition = desiredPosition;
                _initialized = true;
            }
            _smoothedPosition = Vector3.Lerp(_smoothedPosition, desiredPosition,
                MathUtil.SmoothFactor(positionSmoothingSeconds, dt));
            transform.position = _smoothedPosition;

            // Look-ahead 2–6 m scaled by speed.
            float speed01 = MathUtil.Clamp01(target.GetComponent<Rigidbody>() != null
                ? target.GetComponent<Rigidbody>().linearVelocity.magnitude / 20f
                : 0f);
            Vector3 lookPoint = target.position + Vector3.up * 1.2f
                                + target.forward * Mathf.Lerp(lookAheadMin, lookAheadMax, speed01);
            _smoothedLookPoint = Vector3.Lerp(_smoothedLookPoint, lookPoint,
                MathUtil.SmoothFactor(lookSmoothingSeconds, dt));

            Quaternion lookRotation = Quaternion.LookRotation(_smoothedLookPoint - transform.position, Vector3.up);
            if (enableHorizonStabilization)
            {
                // Damped roll only — keep horizon level without fighting slope context.
                lookRotation = Quaternion.Slerp(lookRotation,
                    Quaternion.LookRotation(_smoothedLookPoint - transform.position, Vector3.up), 1f);
            }
            transform.rotation = lookRotation;

            if (_cam != null && enableFovMotion)
            {
                _cam.fieldOfView = Mathf.Lerp(fovNormal, fovMax, speed01);
            }
        }

        /// <summary>Photo mode: bounded orbit within 20 m; no map reveal or trigger activation (§3.4).</summary>
        public void SetPhotoMode(bool photoMode)
        {
            if (photoMode)
            {
                _photoMode = true;
                distance = Mathf.Min(distance, 20f);
            }
            else
            {
                _photoMode = false;
            }
        }

        bool _photoMode;
    }
}
