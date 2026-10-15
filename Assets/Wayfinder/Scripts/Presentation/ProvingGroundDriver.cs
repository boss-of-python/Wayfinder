using System;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Wayfinder.Platform;
using Wayfinder.Simulation;

namespace Wayfinder.Presentation
{
    /// <summary>Prototype bridge: explicit input → vehicle/camera, touch controls and safe pause.</summary>
    public sealed class ProvingGroundDriver : MonoBehaviour
    {
        public VehicleController vehicle;
        public InputRouter input;
        public ChaseCameraBehaviour chaseCamera;
        LifecycleHandler _lifecycle;
        RectTransform _root;
        Text _status;
        Button _pauseButton;
        bool _paused, _left, _right, _gas, _brake;
        Vector3 _spawn;
        Quaternion _rotation;
        float _nextHudUpdate;

        void Start()
        {
            if (vehicle == null || input == null || chaseCamera == null)
            {
                Debug.LogError("[Wayfinder] Proving-ground references missing. Regenerate scenes with Setup Project.");
                enabled = false;
                return;
            }
            _spawn = vehicle.transform.position;
            _rotation = vehicle.transform.rotation;
            _lifecycle = gameObject.AddComponent<LifecycleHandler>();
            _lifecycle.OnSuspendRequested += Suspend;
            _root = PrototypeUi.CreateRoot("Driving UI");
            _status = PrototypeUi.Label(_root, "Driving prototype", new Vector2(.12f, .83f), new Vector2(.72f, .98f));
            _pauseButton = PrototypeUi.Button(_root, "Pause", new Vector2(.79f, .85f), new Vector2(.97f, .97f));
            _pauseButton.onClick.AddListener(() => SetPaused(!_paused));
            PrototypeUi.Button(_root, "Reset vehicle", new Vector2(.39f, .03f), new Vector2(.61f, .13f))
                .onClick.AddListener(ResetVehicle);
            var fpsButton = PrototypeUi.Button(_root, $"Target: {Application.targetFrameRate} FPS",
                new Vector2(.39f, .15f), new Vector2(.61f, .25f));
            fpsButton.onClick.AddListener(() =>
            {
                QualityTierService.Apply(Application.targetFrameRate == 30 ? QualityTier.Balanced : QualityTier.BatteryLow);
                fpsButton.GetComponentInChildren<Text>().text = $"Target: {Application.targetFrameRate} FPS";
            });
            PrototypeUi.Button(_root, "Title", new Vector2(.02f, .85f), new Vector2(.12f, .97f))
                .onClick.AddListener(() => { Time.timeScale = 1f; SceneManager.LoadScene("Title"); });
            Hold("LEFT", new Vector2(.02f, .04f), new Vector2(.18f, .24f), value => _left = value);
            Hold("RIGHT", new Vector2(.20f, .04f), new Vector2(.36f, .24f), value => _right = value);
            Hold("BRAKE", new Vector2(.64f, .04f), new Vector2(.80f, .24f), value => _brake = value);
            Hold("GAS", new Vector2(.82f, .04f), new Vector2(.98f, .24f), value => _gas = value);
            PrototypeUi.Label(_root, "WASD / arrows • Space: handbrake • Esc: pause\nTest rig only — no campaign rewards or saved progress",
                new Vector2(.10f, .68f), new Vector2(.90f, .82f), 18);
            if (_lifecycle.IsSuspended) SetPaused(true);
        }

        void Hold(string label, Vector2 min, Vector2 max, Action<bool> assign)
        {
            var button = PrototypeUi.Button(_root, label, min, max);
            // Driving controls must never become keyboard/gamepad navigation targets.
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var hold = button.gameObject.AddComponent<TouchHoldButton>();
            hold.Changed += value =>
            {
                assign(!_paused && value);
                input.SetTouchSteer((_right ? 1f : 0f) - (_left ? 1f : 0f));
                input.SetTouchPedal(_gas ? 1f : 0f, _brake ? 1f : 0f);
            };
        }

        void Update()
        {
            if (input == null || vehicle == null) return;
            if ((Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame))
                SetPaused(!_paused);
            if (Time.unscaledTime >= _nextHudUpdate && _status != null)
            {
                _nextHudUpdate = Time.unscaledTime + .1f;
                _status.text = _paused ? "Paused — tap Resume" : $"{vehicle.SpeedKph:0} km/h  •  {vehicle.FuelLiters:0.0} L  •  {(vehicle.PowertrainSystem.InReverse ? "R" : "D")}";
            }
        }

        void LateUpdate()
        {
            if (vehicle == null || input == null) return;
            if (_paused) { vehicle.ZeroInputsAndHold(); return; }
            var command = input.Current;
            vehicle.SetDriveCommand(command.Steer, command.Throttle, command.Brake, command.Handbrake, command.Crawl);
            if (chaseCamera != null)
            {
                var delta = input.CameraLookDelta;
                if (delta.sqrMagnitude > .0001f) chaseCamera.SetOrbitInput(delta.x, -delta.y);
            }
        }

        void Suspend() => SetPaused(true);

        public void SetPaused(bool paused)
        {
            if (!paused && _lifecycle != null && _lifecycle.IsSuspended) return;
            _paused = paused;
            _left = _right = _gas = _brake = false;
            if (_root != null)
                foreach (var hold in _root.GetComponentsInChildren<TouchHoldButton>()) hold.Release();
            input.SuppressInputs(paused);
            if (paused) vehicle.ZeroInputsAndHold();
            Time.timeScale = paused ? 0f : 1f;
            if (_pauseButton != null) _pauseButton.GetComponentInChildren<Text>().text = paused ? "Resume" : "Pause";
        }

        public void ResetVehicle()
        {
            var body = vehicle.GetComponent<Rigidbody>();
            body.position = _spawn;
            body.rotation = _rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            vehicle.Damage.RepairFully();
            vehicle.RestartAfterWaterRecovery();
            vehicle.RefuelFully();
            vehicle.ZeroInputsAndHold();
        }

        void OnDestroy()
        {
            if (_lifecycle != null) _lifecycle.OnSuspendRequested -= Suspend;
            Time.timeScale = 1f;
        }
    }
}
