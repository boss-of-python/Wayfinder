using System;
using System.Collections.Generic;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Rigidbody + four WheelCollider vehicle controller (§4.1, Appendix D physics outline).
    /// Owns actuation only — no UI, no permanent save writes (§16.1).
    /// Wheel buffers are reused; the stat resolver still allocates and needs M6 profiling.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        [Header("Wheel colliders (front-left, front-right, rear-left, rear-right)")]
        public WheelCollider wheelFrontLeft;
        public WheelCollider wheelFrontRight;
        public WheelCollider wheelRearLeft;
        public WheelCollider wheelRearRight;

        [Header("Wheel visual transforms (optional)")]
        public Transform wheelVisualFrontLeft;
        public Transform wheelVisualFrontRight;
        public Transform wheelVisualRearLeft;
        public Transform wheelVisualRearRight;

        [Header("Configuration")]
        public VehicleSpec spec;
        public TireSet tireSet = TireSet.AllTerrain;

        Rigidbody _rb;
        Collider _hull;
        float _groundHeightY;
        readonly Dictionary<Collider, WaterVolume> _waterContacts = new Dictionary<Collider, WaterVolume>();
        public float WaterDepthMeters { get; private set; }
        readonly Powertrain _powertrain = new Powertrain();
        readonly DamageModel _damage = new DamageModel();
        readonly MudSinkModel _mud = new MudSinkModel();
        readonly WaterCrossingModel _water = new WaterCrossingModel();
        readonly RolloverMonitor _rollover = new RolloverMonitor();
        readonly VehicleStatsResolver _resolver = new VehicleStatsResolver();

        float _steerCommand;
        float _throttleCommand;
        float _brakeCommand;
        bool _handbrake;
        bool _crawl;
        bool _engineOn = true;
        float _fuelLiters;
        float _cargoMassKg;

        // Reused buffers — zero steady-state allocation (§18.2).
        readonly bool[] _grounded = new bool[4];
        readonly float[] _slip = new float[4];
        readonly float[] _wheelTorque = new float[4];
        readonly SurfaceType[] _surfaces = new SurfaceType[4];

        public float FuelLiters => _fuelLiters;
        public float SpeedMetersPerSecond => _rb != null ? _rb.linearVelocity.magnitude : 0f;
        public float SpeedKph => SpeedMetersPerSecond * 3.6f;
        public DamageModel Damage => _damage;
        public MudSinkModel Mud => _mud;
        public WaterCrossingModel Water => _water;
        public RolloverMonitor Rollover => _rollover;
        public Powertrain PowertrainSystem => _powertrain;
        public VehicleState State { get; private set; } = VehicleState.Parked;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _hull = GetComponent<Collider>();
            if (spec == null) spec = VehicleSpec.ScoutS1();
            _fuelLiters = spec.FuelCapacityLiters;
            _powertrain.Reset(spec);
        }

        /// <summary>Latest normalized command from InputRouter (read per rendered frame, §3.2).</summary>
        public void SetDriveCommand(float steer, float throttle, float brake, bool handbrake, bool crawl)
        {
            _steerCommand = MathUtil.Clamp(steer, -1f, 1f);
            _throttleCommand = MathUtil.Clamp01(throttle);
            _brakeCommand = MathUtil.Clamp01(brake);
            _handbrake = handbrake;
            _crawl = crawl;
        }

        /// <summary>Losing focus/pause zeroes throttle and applies a parking hold (§3.2).</summary>
        public void ZeroInputsAndHold()
        {
            _throttleCommand = 0f;
            _brakeCommand = 1f;
            _steerCommand = 0f;
            _handbrake = true; // a parking hold must never become powered reverse
        }

        public void SetCargoMass(float kg) => _cargoMassKg = Math.Max(0f, kg);
        public void RefuelFully() => _fuelLiters = spec.FuelCapacityLiters;
        public void SetFuel(float liters) => _fuelLiters = MathUtil.Clamp(liters, 0f, spec.FuelCapacityLiters);
        public void SetSurfaceAtWheel(int index, SurfaceType surface) => _surfaces[index] = surface;
        public void SetEngineOn(bool on) => _engineOn = on;
        /// <summary>
        /// Water above intake for 2 s: engine stops and condition drops 10 once (§5.3). The water
        /// model's latch is deliberately NOT cleared here — it is cleared by
        /// <see cref="RestartAfterWaterRecovery"/> (winch-out/repair/rescue), so a vehicle sitting
        /// in deep water is never charged again every two seconds.
        /// </summary>
        public void ApplyWaterStall() { _damage.ApplyWaterStall(); SetEngineOn(false); }

        /// <summary>Recovery/repair path once the vehicle is out of the water: ends the incident and restarts the engine.</summary>
        public void RestartAfterWaterRecovery()
        {
            _water.ClearStall();
            _damage.EndWaterIncident();
            if (!_damage.EngineDisabled) SetEngineOn(true);
        }

        void FixedUpdate()
        {
            if (spec == null) return;
            float dt = Time.fixedDeltaTime;

            SampleGrounded();
            var stats = ResolveStats();

            bool reverseIndicated;
            float totalDriveTorque = _powertrain.Step(
                spec, stats,
                AverageWheelSpeedMs(),
                _throttleCommand, _brakeCommand, _handbrake, _crawl,
                dt, out reverseIndicated);

            for (int i = 0; i < 4; i++)
            {
                _slip[i] = FuelModel.SlipRatio(
                    _grounded[i] ? GetWheel(i).rpm * Mathf.PI / 30f : 0f,
                    spec.WheelRadiusM,
                    LongitudinalSpeedMs());
            }
            Powertrain.SplitAwdTorque(totalDriveTorque, _grounded, _slip, _wheelTorque, _powertrain.DiffLockEngaged);

            // Engine-off, disabled and empty-tank vehicles may brake/coast, never propel.
            if (!_engineOn || _fuelLiters <= 0f || _damage.EngineDisabled)
                Array.Clear(_wheelTorque, 0, _wheelTorque.Length);
            _rb.mass = spec.DryMassKg + _fuelLiters * VehicleSpec.FuelDensityKgPerLiter + _cargoMassKg;
            ApplyWheelForces(stats, reverseIndicated);

            // Hazards and fuel (simulation delta only).
            _mud.Step(_throttleCommand, LongitudinalSpeedMs(), CurrentMudDepthM(), dt);
            float intakeHeight = stats.SafeWadingDepthM;
            WaterDepthMeters = CurrentWaterDepthM();
            if (_water.Step(WaterDepthMeters, intakeHeight, dt, out bool stallEvent) && stallEvent)
                ApplyWaterStall();

            _damage.Tick(dt);
            _rollover.Step(CurrentBodyRollDegrees(), SpeedMetersPerSecond, CurrentTerrainSlopeDegrees(), dt);

            float slipSeverity = FuelModel.SlipSeverity(_slip);
            float load = FuelModel.EstimateLoad(
                _powertrain.LastCommandedTorque / Math.Max(1f, spec.PeakTorqueNm * spec.FinalDrive),
                CurrentTerrainSlopeRadians(), 0.035f);
            float driveDemand = _powertrain.InReverse ? _brakeCommand : _throttleCommand;
            float drain = FuelModel.LitersPerSecond(driveDemand, load, slipSeverity, _engineOn)
                          * stats.PoweredFuelDrainFactor
                          * (1f + _mud.SinkFraction);
            _fuelLiters = FuelModel.Integrate(_fuelLiters, stats.FuelCapacityLiters, drain, dt);

            UpdateState();
        }

        void ApplyWheelForces(EffectiveVehicleStats stats, bool reverseIndicated)
        {
            SteeringModel.AckermannAngles(_steerCommand, SpeedKph, spec.WheelbaseM, spec.TrackM,
                out float inner, out float outer);
            bool turningLeft = _steerCommand < 0f;

            // The brake pedal powers reverse after the stationary guard; throttle brakes in R.
            float serviceBrake = reverseIndicated ? _throttleCommand : _brakeCommand;
            // Per-wheel: steer (front), motor torque, brake torque scaled by surface grip.
            SetWheelDrive(wheelFrontLeft, _wheelTorque[0], serviceBrake, stats,
                turningLeft ? inner : outer);
            SetWheelDrive(wheelFrontRight, _wheelTorque[1], serviceBrake, stats,
                turningLeft ? outer : inner);
            SetWheelDrive(wheelRearLeft, _wheelTorque[2], serviceBrake, stats, 0f);
            SetWheelDrive(wheelRearRight, _wheelTorque[3], serviceBrake, stats, 0f);

            if (_handbrake && wheelRearLeft != null && wheelRearRight != null)
            {
                wheelRearLeft.brakeTorque = spec.BrakeTorqueRearNmPerWheel * 1.5f;
                wheelRearRight.brakeTorque = spec.BrakeTorqueRearNmPerWheel * 1.5f;
            }

            // Anti-roll (§4.5), both axles.
            ApplyAntiRoll(wheelFrontLeft, wheelFrontRight, stats);
            ApplyAntiRoll(wheelRearLeft, wheelRearRight, stats);
        }

        void SetWheelDrive(WheelCollider wheel, float motorTorque, float brakeCmd, EffectiveVehicleStats stats, float steerDegrees)
        {
            if (wheel == null) return;
            wheel.steerAngle = steerDegrees;
            wheel.motorTorque = motorTorque;

            float surfaceGrip = SurfaceTable.Get(_surfaces[WheelIndex(wheel)]).EffectiveGrip(false);
            float brakeScale = MathUtil.Clamp01(surfaceGrip / 0.9f + 0.1f); // ABS-ish assist envelope
            wheel.brakeTorque = brakeCmd * (wheel == wheelFrontLeft || wheel == wheelFrontRight
                ? spec.BrakeTorqueFrontNmPerWheel
                : spec.BrakeTorqueRearNmPerWheel) * brakeScale;

            // WheelCollider friction shaped toward the composed effective grip (§4.4 calibration note:
            // setting μ does NOT create an exact physical coefficient — M1 calibration required).
            var fwd = wheel.forwardFriction;
            fwd.stiffness = stats.EffectiveGrip * (1f + _mud.SinkFraction * -0.5f) * _mud.TractionMultiplier;
            wheel.forwardFriction = fwd;
            var side = wheel.sidewaysFriction;
            side.stiffness = stats.EffectiveGrip * stats.LateralGripFactor * _mud.TractionMultiplier;
            wheel.sidewaysFriction = side;
        }

        void ApplyAntiRoll(WheelCollider left, WheelCollider right, EffectiveVehicleStats stats)
        {
            if (left == null || right == null) return;
            bool groundedL = left.isGrounded;
            bool groundedR = right.isGrounded;
            float compL = SuspensionCompression(left);
            float compR = SuspensionCompression(right);
            float force = AntiRoll.Force(compL, compR, spec.AntiRollCouplingNm, groundedL, groundedR);
            if (Mathf.Approximately(force, 0f)) return;
            if (groundedL) _rb.AddForceAtPosition(left.transform.up * -force, left.transform.position);
            if (groundedR) _rb.AddForceAtPosition(right.transform.up * force, right.transform.position);
        }

        static float SuspensionCompression(WheelCollider wheel)
        {
            if (!wheel.GetGroundHit(out WheelHit hit)) return 0f;
            // WheelHit has a contact point, not a distance property. Measure in wheel space.
            float travel = Mathf.Max(0.001f, wheel.suspensionDistance);
            float extension = -wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius;
            return travel - Mathf.Clamp(extension, 0f, travel);
        }

        EffectiveVehicleStats ResolveStats()
        {
            var stats = _resolver.Resolve(
                spec, _fuelLiters, _cargoMassKg, tireSet, SurfaceType.PackedDirt, false,
                _damage.EngineCondition, _damage.SuspensionCondition, _damage.TiresCondition,
                true, true, true);
            stats.PunctureActive = _damage.TiresCondition < 30;
            return stats;
        }

        void SampleGrounded()
        {
            float groundSum = 0f;
            int groundedCount = 0;
            for (int i = 0; i < 4; i++)
            {
                var wheel = GetWheel(i);
                _grounded[i] = false;
                _surfaces[i] = SurfaceType.PackedDirt;
                if (wheel == null || !wheel.GetGroundHit(out WheelHit hit)) continue;
                _grounded[i] = true;
                groundSum += hit.point.y;
                groundedCount++;
                if (hit.collider != null && hit.collider.TryGetComponent<SurfaceLabel>(out var label))
                    _surfaces[i] = label.surface;
            }
            _groundHeightY = groundedCount > 0 ? groundSum / groundedCount
                : transform.position.y - spec.WheelRadiusM - spec.SuspensionTravelM * .5f;
        }

        WheelCollider GetWheel(int i) => i switch
        {
            0 => wheelFrontLeft, 1 => wheelFrontRight, 2 => wheelRearLeft, _ => wheelRearRight
        };

        int WheelIndex(WheelCollider w)
        {
            if (w == wheelFrontLeft) return 0;
            if (w == wheelFrontRight) return 1;
            if (w == wheelRearLeft) return 2;
            return 3;
        }

        float AverageWheelSpeedMs()
        {
            // rpm → rad/s → linear
            float sum = 0f;
            int n = 0;
            for (int i = 0; i < 4; i++)
            {
                if (!_grounded[i]) continue;
                sum += GetWheel(i).rpm * Mathf.PI / 30f * spec.WheelRadiusM;
                n++;
            }
            return n > 0 ? sum / n : LongitudinalSpeedMs();
        }

        float LongitudinalSpeedMs() => Vector3.Dot(_rb.linearVelocity, transform.forward);

        // Mud depth and full water-force integration still require authored depth/terrain data.
        float CurrentMudDepthM() => SurfaceTable.IsMud(_surfaces[0]) ? 0.10f : 0f;

        void OnTriggerEnter(Collider other)
        {
            if (other.TryGetComponent<WaterVolume>(out var volume)) _waterContacts[other] = volume;
        }

        void OnTriggerExit(Collider other) => _waterContacts.Remove(other);

        void OnDisable()
        {
            _waterContacts.Clear();
            WaterDepthMeters = 0f;
            ZeroInputsAndHold();
        }

        float CurrentWaterDepthM()
        {
            float depth = 0f;
            foreach (var contact in _waterContacts)
            {
                if (contact.Key == null || !contact.Key.enabled || contact.Value == null || !contact.Value.isActiveAndEnabled) continue;
                // Reject stale contacts after reset/teleport, even before the next trigger exit.
                if (_hull != null && !contact.Key.bounds.Intersects(_hull.bounds)) continue;
                depth = Mathf.Max(depth, contact.Value.surfaceHeightY - _groundHeightY);
            }
            return Mathf.Max(0f, depth);
        }
        float CurrentBodyRollDegrees() => Vector3.SignedAngle(transform.up, Vector3.up, transform.forward);
        float CurrentTerrainSlopeDegrees() => Vector3.Angle(transform.forward,
            Vector3.ProjectOnPlane(transform.forward, Vector3.up)) ;
        float CurrentTerrainSlopeRadians() => Mathf.Asin(Mathf.Clamp(transform.up.y - 1f, -1f, 0f) * -1f);

        void UpdateState()
        {
            if (_damage.EngineDisabled || _fuelLiters <= 0f)
                State = VehicleState.Disabled;
            else if (_engineOn == false && SpeedMetersPerSecond < 0.2f)
                State = VehicleState.Stalled;
            else if (SpeedMetersPerSecond > 0.5f)
                State = VehicleState.Driving;
            else
                State = VehicleState.Parked;
        }

        void LateUpdate()
        {
            // Cosmetic wheel visuals only (§18.2: nothing heavy here).
            SyncWheelVisual(wheelFrontLeft, wheelVisualFrontLeft);
            SyncWheelVisual(wheelFrontRight, wheelVisualFrontRight);
            SyncWheelVisual(wheelRearLeft, wheelVisualRearLeft);
            SyncWheelVisual(wheelRearRight, wheelVisualRearRight);
        }

        static void SyncWheelVisual(WheelCollider wheel, Transform visual)
        {
            if (wheel == null || visual == null) return;
            wheel.GetWorldPose(out Vector3 pos, out Quaternion rot);
            visual.SetPositionAndRotation(pos, rot);
        }
    }
}
