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
        /// <summary>
        /// How far the camera rolls with the vehicle when horizon stabilisation is OFF (§3.5):
        /// 0 = always level (identical to stabilisation), 1 = fully follows the chassis up vector.
        /// </summary>
        [Range(0f, 1f)] public float vehicleRollBlend = 0.75f;

        Camera _cam;
        float _yawOffset;
        float _pitchOffset = 8f;
        float _lastCameraInputTime = -999f;
        float _currentDistance;
        Vector3 _smoothedPosition;
        Vector3 _lookVelocity;
        Vector3 _smoothedLookPoint;
        bool _lookInitialized;
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
            Vector3 desiredPosition = target.position + orbit * new Vector3(0f, 0f, -EffectiveDistance) + Vector3.up * height;

            // Obstruction: spherecast from target toward camera; shorten immediately, recover slowly.
            float desiredDistance = EffectiveDistance;
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
            // Initialised on the first frame: starting at the world origin made the camera swivel
            // from (0,0,0) at start-up (DEF-013).
            if (!_lookInitialized)
            {
                _smoothedLookPoint = lookPoint;
                _lookVelocity = Vector3.zero;
                _lookInitialized = true;
            }
            _smoothedLookPoint = Vector3.SmoothDamp(_smoothedLookPoint, lookPoint, ref _lookVelocity,
                lookSmoothingSeconds, Mathf.Infinity, dt);

            // DEF-013: until 2026-10-19 this built the stabilised and unstabilised rotations from the
            // identical expression and slerped with t = 1f, so `enableHorizonStabilization` was a
            // no-op and the camera was always horizon-locked — one of the four §3.5 motion
            // accessibility toggles simply did not exist.
            Vector3 lookDirection = _smoothedLookPoint - transform.position;
            Vector3 lookUp = Vector3.up;                     // stabilised: the horizon stays level.
            if (!enableHorizonStabilization && vehicleRollBlend > 0f)
            {
                // Unstabilised: follow the vehicle's own up so the horizon tilts with the slope.
                // Guarded so a flipped or near-vertical chassis can never invert the view.
                Vector3 vehicleUp = target.up.y > 0.2f ? target.up : Vector3.up;
                lookUp = Vector3.Lerp(Vector3.up, vehicleUp, MathUtil.Clamp01(vehicleRollBlend)).normalized;
            }
            transform.rotation = Quaternion.LookRotation(lookDirection, lookUp);

            if (_cam != null && enableFovMotion)
            {
                _cam.fieldOfView = Mathf.Lerp(fovNormal, fovMax, speed01);
            }
        }

        /// <summary>Photo mode orbit bound (§3.4): never farther than 20 m from the vehicle.</summary>
        public const float PhotoModeMaxDistance = 20f;

        bool _photoMode;

        /// <summary>True while photo mode bounds the orbit (§3.4). Read by the camera update every frame.</summary>
        public bool PhotoMode => _photoMode;

        /// <summary>
        /// Photo mode: bounded orbit within 20 m; no map reveal or trigger activation (§3.4).
        /// The bound is applied per frame instead of mutating the authored <see cref="distance"/>,
        /// so leaving photo mode restores the tuned distance exactly.
        /// </summary>
        public void SetPhotoMode(bool photoMode)
        {
            if (_photoMode == photoMode) return;
            _photoMode = photoMode;
            NotifyCameraInput();
        }

        /// <summary>Authored distance, clamped to the photo-mode bound while photo mode is active.</summary>
        float EffectiveDistance => _photoMode ? Mathf.Min(distance, PhotoModeMaxDistance) : distance;
    }
}
