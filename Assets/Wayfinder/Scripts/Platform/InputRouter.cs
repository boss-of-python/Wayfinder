using System;
using UnityEngine;
using UnityEngine.InputSystem;
using Wayfinder.Core;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Unified player commands and device switching (§16.1). Reads device/UI input per rendered
    /// frame and retains the latest command for fixed-step physics (§3.2).
    /// Never owns vehicle physics.
    ///
    /// Touch button steering ramps 0→full in 0.30 s and returns in 0.18 s; stick dead zone 0.08;
    /// throttle targets in 0.20 s, brake in 0.12 s (accessibility settings may lengthen ramps).
    /// Losing focus/pause zeroes throttle and applies a safe parking hold — never leaves
    /// throttle latched (QA case 15).
    /// </summary>
    public sealed class InputRouter : MonoBehaviour
    {
        [Header("Accessibility (driven by settings)")]
        public float steerRampSeconds = 0.30f;
        public float steerReturnSeconds = 0.18f;
        public float throttleRampSeconds = 0.20f;
        public float brakeRampSeconds = 0.12f;
        public bool tiltSteeringEnabled;
        public bool leftHandedLayout;
        public bool holdToInteractAlternativeTap = true;

        public struct DriveCommand
        {
            public float Steer;     // [-1, 1]
            public float Throttle;  // [0, 1]
            public float Brake;     // [0, 1]
            public bool Handbrake;
            public bool Crawl;
            public bool ReverseIndicated;
        }

        public DriveCommand Current { get; private set; }
        public bool InteractHeld { get; private set; }
        public bool InteractTapped { get; private set; }
        public bool MapPressed { get; private set; }
        public bool PausePressed { get; private set; }
        public bool RecoveryPressed { get; private set; }
        public bool WinchModePressed { get; private set; }
        public float WinchReel { get; private set; }
        public Vector2 CameraLookDelta { get; private set; }
        public bool CameraRecenterPressed { get; private set; }

        InputActionAsset _actions;
        InputActionMap _driving;
        bool _inputsSuppressed;
        float _touchSteer, _touchThrottle, _touchBrake;

        [Header("Optional TextAsset (defaults to Resources/WayfinderControls)")]
        public TextAsset controlsJson;

        void Awake()
        {
            string json = controlsJson != null ? controlsJson.text : LoadFromResources();
            if (string.IsNullOrEmpty(json))
            {
                Debug.LogError("WayfinderControls.inputactions missing; input will be inert.");
                return;
            }
            _actions = InputActionAsset.FromJson(json);
            _driving = _actions.FindActionMap("Driving", throwIfNotFound: false);
            _driving?.Enable();
        }

        static string LoadFromResources()
        {
            var asset = Resources.Load<TextAsset>("WayfinderControls");
            return asset != null ? asset.text : null;
        }

        void OnDestroy()
        {
            _actions?.Disable();
            if (_actions != null) Destroy(_actions);
            _actions = null;
        }

        void Update()
        {
            if (_driving == null) return;

            float dt = Time.deltaTime;
            if (_inputsSuppressed)
            {
                Current = default; // explicit zero — never latch (QA case 15)
                ClearTransientFlags();
                return;
            }

            // Device axes first (keyboard/gamepad/joystick); touch buttons override via ramps.
            float stickSteer = _driving.FindAction("Steer").ReadValue<float>();
            float throttle = Mathf.Clamp01(_driving.FindAction("Throttle").ReadValue<float>());
            float brake = Mathf.Clamp01(_driving.FindAction("Brake").ReadValue<float>());

            bool leftBtn = _driving.FindAction("SteerLeftButton").IsPressed();
            bool rightBtn = _driving.FindAction("SteerRightButton").IsPressed();
            float buttonTarget = (leftBtn ? -1f : 0f) + (rightBtn ? 1f : 0f);

            if (Mathf.Abs(_touchSteer) > 0.001f) buttonTarget = _touchSteer;
            throttle = Mathf.Max(throttle, _touchThrottle);
            brake = Mathf.Max(brake, _touchBrake);

            var cmd = Current;
            if (Mathf.Abs(stickSteer) > 0.001f)
            {
                cmd.Steer = Mathf.Abs(stickSteer) < 0.08f ? 0f : stickSteer; // stick dead zone
            }
            else
            {
                float rate = Mathf.Abs(buttonTarget) > Mathf.Abs(cmd.Steer)
                    ? 1f / Mathf.Max(0.01f, steerRampSeconds)
                    : 1f / Mathf.Max(0.01f, steerReturnSeconds);
                cmd.Steer = Mathf.MoveTowards(cmd.Steer, buttonTarget, rate * dt);
            }

            if (tiltSteeringEnabled)
                cmd.Steer = ApplyTiltSteer(cmd.Steer);

            cmd.Throttle = Mathf.MoveTowards(cmd.Throttle, throttle, dt / Mathf.Max(0.01f, throttleRampSeconds));
            cmd.Brake = Mathf.MoveTowards(cmd.Brake, brake, dt / Mathf.Max(0.01f, brakeRampSeconds));
            cmd.Handbrake = _driving.FindAction("Handbrake").IsPressed();
            if (_driving.FindAction("CrawlToggle").WasPressedThisFrame())
                cmd.Crawl = !cmd.Crawl; // toggle, not latch (§3.2)
            Current = cmd;

            InteractHeld = _driving.FindAction("Interact").IsPressed();
            // Edge only, and gated by the accessibility setting that offers a tap as an alternative
            // to holding (§3.5). Until 2026-10-19 the expression OR-ed in `IsPressed()`, so the flag
            // stayed true on every frame of a hold and any consumer treating it as an event fired
            // once per frame (DEF-014).
            InteractTapped = holdToInteractAlternativeTap
                             && _driving.FindAction("InteractTap").WasPressedThisFrame();
            MapPressed = _driving.FindAction("Map").WasPressedThisFrame();
            PausePressed = _driving.FindAction("Pause").WasPressedThisFrame();
            RecoveryPressed = _driving.FindAction("Recovery").WasPressedThisFrame();
            WinchModePressed = _driving.FindAction("WinchMode").WasPressedThisFrame();
            WinchReel = Mathf.Clamp01(_driving.FindAction("WinchReel").ReadValue<float>());
            CameraLookDelta = _driving.FindAction("CameraLook").ReadValue<Vector2>();
            CameraRecenterPressed = _driving.FindAction("CameraRecenter").WasPressedThisFrame();
        }

        float _tiltFiltered;

        /// <summary>
        /// Tilt steering (§3.1): calibrate zero at current comfortable device angle,
        /// ±22° full steering, 2° dead zone, low-pass τ = 0.10 s. Touch pedals remain.
        /// Tilt is NEVER required (accessibility).
        /// </summary>
        float ApplyTiltSteer(float fallback)
        {
            if (!SystemInfo.supportsGyroscope && Accelerometer.current == null) return fallback;
            Vector3 acc = Accelerometer.current != null
                ? Accelerometer.current.acceleration.ReadValue()
                : UnityEngine.InputSystem.Gyroscope.current != null
                    ? UnityEngine.InputSystem.Gyroscope.current.angularVelocity.ReadValue()
                    : Vector3.zero;

            float raw = Mathf.Atan2(acc.x, Mathf.Max(0.01f, -acc.y)) * Mathf.Rad2Deg;
            float deadzoned = Mathf.Abs(raw) < 2f ? 0f : raw;
            float target = Mathf.Clamp(deadzoned / 22f, -1f, 1f);
            _tiltFiltered = Mathf.Lerp(_tiltFiltered, target, MathUtil.SmoothFactor(0.10f, Time.deltaTime));
            return Mathf.Abs(_tiltFiltered) > 0.01f ? _tiltFiltered : fallback;
        }

        /// <summary>Focus loss / pause: stop input immediately, zero throttle (§19.1).</summary>
        public void SuppressInputs(bool suppressed)
        {
            _inputsSuppressed = suppressed;
            if (suppressed)
            {
                Current = default;
                _touchSteer = _touchThrottle = _touchBrake = 0f;
                InteractHeld = false;
                WinchReel = 0f;
                ClearTransientFlags();
            }
        }

        /// <summary>Called by UI touch pedals (pointer-captured per control; camera drag never steals pedal touches, §3.2).</summary>
        public void SetTouchPedal(float throttle01, float brake01)
        {
            if (_inputsSuppressed) return;
            _touchThrottle = Mathf.Clamp01(throttle01);
            _touchBrake = Mathf.Clamp01(brake01);
        }

        public void SetTouchSteer(float steerMinus1To1)
        {
            if (_inputsSuppressed) return;
            _touchSteer = Mathf.Clamp(steerMinus1To1, -1f, 1f);
        }

        public void ClearTransientFlags()
        {
            InteractTapped = false;
            MapPressed = false;
            PausePressed = false;
            RecoveryPressed = false;
            WinchModePressed = false;
            CameraRecenterPressed = false;
            CameraLookDelta = Vector2.zero;
        }
    }
}
