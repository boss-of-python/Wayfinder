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
        /// <summary>Mud depth under the wheels this tick, 0 when no grounded wheel is in mud (DEF-015).</summary>
        public float MudDepthMeters { get; private set; }
        readonly Powertrain _powertrain = new Powertrain();
        readonly DamageModel _damage = new DamageModel();
        readonly MudSinkModel _mud = new MudSinkModel();
        readonly WaterCrossingModel _water = new WaterCrossingModel();
        readonly RolloverMonitor _rollover = new RolloverMonitor();
        readonly VehicleStatsResolver _resolver = new VehicleStatsResolver();
        /// <summary>Stats resolved by the last FixedUpdate (exposed for the HUD and the safe-pose gate).</summary>
        EffectiveVehicleStats _stats;

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
        readonly Vector3[] _baseWheelCenters = new Vector3[4];
        readonly bool[] _centerCaptured = new bool[4];
        WheelRigSettings _rigSettings;
        bool _rigApplied;

        /// <summary>
        /// Wet-surface flag for grip composition (§4.4). Until the weather system drives this it is
        /// false; it exists so the per-wheel grip path does not hardcode "dry" (DEF-005).
        /// </summary>
        public bool SurfaceWet { get; set; }

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
        /// <summary>
        /// This rig's stat resolver. The campaign configures it with the researched modules
        /// (<c>CampaignSession.ConfigureResolver</c>), so every resolved stat used below — torque,
        /// grip, wading depth, fuel drain, tank size — includes research (KI-025, D-030).
        /// </summary>
        public VehicleStatsResolver StatsResolver => _resolver;
        /// <summary>Rig settings currently live on the WheelColliders (KI-025, D-044).</summary>
        public WheelRigSettings RigSettings => _rigSettings;
        /// <summary>True once a winch module is fitted and applied (tiers 3/15).</summary>
        public bool HasWinch => _rigSettings.HasWinch;
        /// <summary>Fitted winch anchor reach (m); 0 means no winch.</summary>
        public float WinchReachMeters => _rigSettings.WinchReachM;
        /// <summary>Fitted winch pull cap (N); 0 means no winch.</summary>
        public float WinchForceCapNewtons => _rigSettings.WinchForceCapN;
        /// <summary>True when the fitted winch can anchor at the given distance.</summary>
        public bool WinchCanReach(float distanceMeters) => _rigSettings.WinchCanReach(distanceMeters);

        /// <summary>
        /// The seam researched stats enter the physics rig through (KI-025, D-044). Translates the
        /// stats with the engine-free <see cref="WheelRigMath"/> and writes suspension travel,
        /// spring/damper rates, wheel radius and the clearance wheel-center offset to each
        /// WheelCollider — but only when <see cref="WheelRigMath.RequiresGeometryUpdate"/> says a
        /// loadout or condition change actually moved a geometry-bearing value, so calling this
        /// every FixedUpdate rewrites nothing in steady state (§18.2). Wheel centers are captured
        /// lazily per wheel because scene builders assign the colliders after Awake runs.
        /// Center of mass is deliberately untouched: the blueprint requires it to be solved
        /// against the imported chassis datum in a calibration scene (§4.2), not assigned blindly.
        /// Rigidbody mass follows the resolved total here so a parked loadout change applies
        /// immediately; FixedUpdate still recomputes it per tick from live fuel/cargo.
        /// </summary>
        public void ApplyEffectiveStats(EffectiveVehicleStats stats)
        {
            if (stats == null || spec == null) return;
            _stats = stats;
            var next = WheelRigMath.FromStats(spec, stats);
            if (!_rigApplied || WheelRigMath.RequiresGeometryUpdate(_rigSettings, next))
            {
                for (int i = 0; i < 4; i++) ApplyWheelGeometry(GetWheel(i), i, next);
            }
            _rigSettings = next;
            _rigApplied = true;
            if (_rb != null) _rb.mass = stats.TotalMassKg;
        }

        void ApplyWheelGeometry(WheelCollider wheel, int index, WheelRigSettings settings)
        {
            if (wheel == null) return;
            if (!_centerCaptured[index])
            {
                _baseWheelCenters[index] = wheel.center;
                _centerCaptured[index] = true;
            }
            bool front = index == 0 || index == 1;
            wheel.suspensionDistance = settings.SuspensionDistanceM;
            wheel.radius = settings.WheelRadiusM;
            var spring = wheel.suspensionSpring;
            spring.spring = front ? settings.SpringRateFrontNm : settings.SpringRateRearNm;
            spring.damper = front ? settings.DamperFrontNsm : settings.DamperRearNsm;
            spring.targetPosition = settings.TargetPosition;
            wheel.suspensionSpring = spring;
            var center = _baseWheelCenters[index];
            center.y += settings.WheelCenterYOffsetM;
            wheel.center = center;
        }

        /// <summary>Tank capacity after research (not the base spec), used for every fuel clamp.</summary>
        public float FuelCapacityLiters => spec == null ? 0f : ResolveStats().FuelCapacityLiters;
        public void RefuelFully() => _fuelLiters = FuelCapacityLiters;
        public void SetFuel(float liters) => _fuelLiters = MathUtil.Clamp(liters, 0f, FuelCapacityLiters);
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
            ApplyEffectiveStats(stats);

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
            float mudDepthM = CurrentMudDepthM();
            MudDepthMeters = mudDepthM;
            _mud.Step(_throttleCommand, LongitudinalSpeedMs(), mudDepthM, dt);
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

            int index = WheelIndex(wheel);
            float surfaceGrip = SurfaceTable.Get(_surfaces[index]).EffectiveGrip(SurfaceWet);
            float brakeScale = WheelRigMath.BrakeScale(surfaceGrip); // ABS-ish assist envelope
            wheel.brakeTorque = brakeCmd * (wheel == wheelFrontLeft || wheel == wheelFrontRight
                ? spec.BrakeTorqueFrontNmPerWheel
                : spec.BrakeTorqueRearNmPerWheel) * brakeScale;

            // WheelCollider friction shaped toward the composed effective grip (§4.4 calibration note:
            // setting μ does NOT create an exact physical coefficient — M1 calibration required).
            // Per-wheel: each wheel resolves grip for the surface it is actually standing on, so ice,
            // deep mud and asphalt no longer share one packed-dirt coefficient (DEF-005). Before
            // 2026-10-19 the whole vehicle was resolved for `PackedDirt, wet:false` and the per-wheel
            // surface only scaled brake torque. The stiffness formulas themselves live in the
            // engine-free WheelRigMath so they execute in headless tests (KI-025, D-044).
            float grip = _resolver.GripFor(tireSet, _surfaces[index], SurfaceWet, stats.TireGripFactor, out float lateral);
            var fwd = wheel.forwardFriction;
            fwd.stiffness = WheelRigMath.ForwardFrictionStiffness(grip, _mud.SinkFraction, _mud.TractionMultiplier);
            wheel.forwardFriction = fwd;
            var side = wheel.sidewaysFriction;
            side.stiffness = WheelRigMath.SidewaysFrictionStiffness(grip, lateral, _mud.TractionMultiplier);
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
            // Tire-set availability is derived from research inside the resolver (§11.1 tiers 8 and
            // 18). Until 2026-10-19 the three flags were passed as constants — `true,true,true` here
            // and `false,false,false` from the campaign — so research never gated them (DEF-004).
            var stats = _resolver.Resolve(
                spec, _fuelLiters, _cargoMassKg, tireSet, DominantSurface(), SurfaceWet,
                _damage.EngineCondition, _damage.SuspensionCondition, _damage.TiresCondition,
                false, false, false);
            stats.PunctureActive = _damage.HasPuncture;
            _stats = stats;
            return stats;
        }

        /// <summary>
        /// The surface most of the grounded wheels are on. Used only for the whole-vehicle stats
        /// (torque, fuel, wading); per-wheel traction uses each wheel's own surface (DEF-005).
        /// </summary>
        SurfaceType DominantSurface()
        {
            int best = 0;
            SurfaceType winner = SurfaceType.PackedDirt;
            for (int i = 0; i < 4; i++)
            {
                if (!_grounded[i]) continue;
                int count = 0;
                for (int j = 0; j < 4; j++)
                    if (_grounded[j] && _surfaces[j] == _surfaces[i]) count++;
                if (count > best) { best = count; winner = _surfaces[i]; }
            }
            return winner;
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

        /// <summary>
        /// Deepest mud any grounded wheel is standing in, from the mud model's authored depth
        /// constants (§5.2). Before 2026-10-19 this read wheel 0 only and returned a literal 0.10 m,
        /// so deep mud and shallow mud — the two surfaces that decide whether the sink model runs —
        /// were indistinguishable (DEF-015).
        /// </summary>
        float CurrentMudDepthM()
        {
            float depth = 0f;
            for (int i = 0; i < 4; i++)
            {
                if (!_grounded[i] || !SurfaceTable.IsMud(_surfaces[i])) continue;
                depth = Mathf.Max(depth, _surfaces[i] == SurfaceType.DeepMud
                    ? MudSinkModel.ShallowDepthM + (MudSinkModel.BogDepthM - MudSinkModel.ShallowDepthM) * 0.5f
                    : MudSinkModel.ShallowDepthM);
            }
            return depth;
        }

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
        /// <summary>Body roll about the forward axis (+ = leaning right). Used by the HUD and the rollover monitor.</summary>
        public float CurrentBodyRollDegrees() => Vector3.SignedAngle(transform.up, Vector3.up, transform.forward);

        /// <summary>
        /// Cross-slope (side-slope) of the ground under the vehicle, in degrees: the angle between
        /// world up and the body's up vector projected onto the vehicle's roll plane. This is the axis
        /// that actually causes a rollover. Until 2026-10-19 the value handed to the rollover monitor
        /// was the *pitch* of the forward vector, so the §5.4 22° side-slope limit was measuring the
        /// wrong axis (DEF-006).
        /// </summary>
        float CurrentTerrainSlopeDegrees()
        {
            Vector3 lateral = Vector3.ProjectOnPlane(transform.up, transform.forward);
            return Vector3.Angle(Vector3.up, lateral);
        }

        /// <summary>
        /// Terrain incline from the body's up vector: acos(up.y). Until 2026-10-19 this used
        /// <c>asin(1 - up.y)</c>, which is not the tilt angle — a 45° climb reported 17°, so grade
        /// fuel load was under-counted by roughly 2.6× (DEF-006).
        /// </summary>
        float CurrentTerrainSlopeRadians() => Mathf.Acos(Mathf.Clamp(transform.up.y, -1f, 1f));

        /// <summary>Resolved clearance under the chassis, including research (§5.6 safe-pose gate).</summary>
        public float GroundClearanceMeters => _stats != null ? _stats.GroundClearanceM : 0f;
        /// <summary>True while at least one wheel has ground contact.</summary>
        public bool AnyWheelGrounded
        {
            get
            {
                for (int i = 0; i < 4; i++) if (_grounded[i]) return true;
                return false;
            }
        }

        void UpdateState()
        {
            if (_damage.EngineDisabled || _fuelLiters <= 0f)
                State = VehicleState.Disabled;
            else if (_engineOn == false && SpeedMetersPerSecond < 0.2f)
                State = VehicleState.Stalled;
            else if (!AnyWheelGrounded)
                State = VehicleState.Airborne; // airborne is a real measurement now, not a constant (DEF-015)
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
