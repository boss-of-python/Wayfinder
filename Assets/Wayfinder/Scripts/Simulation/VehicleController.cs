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
        /// <summary>Per-wheel §4.4 cross-fade, so a material boundary is never a one-tick jolt (SYS-SIM-001).</summary>
        readonly SurfaceTransition[] _surfaceFade = new SurfaceTransition[4];
        readonly IdleAutoStopModel _idleAutoStop = new IdleAutoStopModel();

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
        /// <summary>§5.1 auto-stop: after 15 s parked at zero throttle the engine stops itself.</summary>
        public IdleAutoStopModel IdleAutoStop => _idleAutoStop;
        public VehicleState State { get; private set; } = VehicleState.Parked;

        void Awake()
        {
            _rb = GetComponent<Rigidbody>();
            _hull = GetComponent<Collider>();
            if (spec == null) spec = VehicleSpec.ScoutS1();
            _fuelLiters = spec.FuelCapacityLiters;
            _powertrain.Reset(spec);
            // Wheels start on the neutral authored surface, not on enum value 0 (asphalt), so the
            // first cross-fade starts from a defined state (SYS-SIM-001).
            for (int i = 0; i < 4; i++)
            {
                _surfaces[i] = SurfaceType.PackedDirt;
                _surfaceFade[i].SnapTo(SurfaceType.PackedDirt);
            }
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
        /// <summary>Tank capacity after research (not the base spec), used for every fuel clamp.</summary>
        public float FuelCapacityLiters => spec == null ? 0f : ResolveStats().FuelCapacityLiters;
        public void RefuelFully() => _fuelLiters = FuelCapacityLiters;
        public void SetFuel(float liters) => _fuelLiters = MathUtil.Clamp(liters, 0f, FuelCapacityLiters);
        /// <summary>
        /// Test/host seam for the per-wheel surface. Bounds-checked and sanitised: an index or a
        /// label outside the table used to write an arbitrary slot or poison the friction path
        /// (SYS-SIM-001).
        /// </summary>
        public void SetSurfaceAtWheel(int index, SurfaceType surface)
        {
            if (index < 0 || index >= _surfaces.Length) return;
            _surfaces[index] = SurfaceTable.Sanitize(surface);
            _surfaceFade[index].SnapTo(_surfaces[index]);
        }

        public void SetEngineOn(bool on)
        {
            _engineOn = on;
            // An explicit start supersedes an auto-stop countdown; the model must not keep a stale
            // latch and immediately "wake" from a pedal the driver never pressed.
            if (on) _idleAutoStop.Clear();
        }
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

            // A zero/NaN fixed step (paused time scale, a corrupted project setting) must not reach
            // the friction and fuel maths: it produced infinities in the slip ratio and a zero-length
            // "impulse" spike at surface boundaries (SYS-SIM-001).
            if (dt <= 0f || float.IsNaN(dt)) dt = 1f / 60f;

            StepIdleAutoStop(dt);
            SampleGrounded();
            StepSurfaceTransitions(dt);
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

        /// <summary>
        /// §5.1 idle auto-stop. Fifteen seconds parked at zero throttle stops the engine, so the
        /// parked drain becomes zero instead of the 0.003 L/s base rate (SYS-SIM-002). Any pedal
        /// restarts it unless the engine is dead or the tank is empty.
        /// </summary>
        void StepIdleAutoStop(float dt)
        {
            bool stopRequested = _idleAutoStop.Step(
                _throttleCommand, _brakeCommand, SpeedMetersPerSecond, _engineOn, dt, out bool restartRequested);

            if (stopRequested)
            {
                if (_engineOn) SetEngineOn(false);
                return;
            }

            if (restartRequested && !_engineOn && !_damage.EngineDisabled && _fuelLiters > 0f)
                SetEngineOn(true);
        }

        /// <summary>
        /// Feeds each wheel's observed surface into its §4.4 cross-fade. Water is the safety-critical
        /// exception: while the hull is in water the coefficients snap instead of blending. The water
        /// depth read here is the previous tick's — one tick of lag on a snap is harmless, whereas
        /// blending a wet-to-dry traction change is exactly what §4.4 forbids.
        /// </summary>
        void StepSurfaceTransitions(float dt)
        {
            bool immediate = WaterDepthMeters > 0.01f;
            for (int i = 0; i < 4; i++)
                _surfaceFade[i].Step(_surfaces[i], dt, immediate);
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
            SurfaceType surface = SurfaceTable.Sanitize(_surfaces[index]);
            float surfaceGrip = SurfaceTable.Get(surface).EffectiveGrip(SurfaceWet);
            float brakeScale = MathUtil.Clamp01(surfaceGrip / 0.9f + 0.1f); // ABS-ish assist envelope
            wheel.brakeTorque = brakeCmd * (wheel == wheelFrontLeft || wheel == wheelFrontRight
                ? spec.BrakeTorqueFrontNmPerWheel
                : spec.BrakeTorqueRearNmPerWheel) * brakeScale;

            // WheelCollider friction shaped toward the composed effective grip (§4.4 calibration note:
            // setting μ does NOT create an exact physical coefficient — M1 calibration required).
            // Per-wheel: each wheel resolves grip for the surface it is actually standing on, so ice,
            // deep mud and asphalt no longer share one packed-dirt coefficient (DEF-005). Before
            // 2026-10-19 the whole vehicle was resolved for `PackedDirt, wet:false` and the per-wheel
            // surface only scaled brake torque.
            float grip = _resolver.GripFor(tireSet, surface, SurfaceWet, stats.TireGripFactor, out float lateral);

            // §4.4 cross-fade: scale the fully composed grip by the blended/raw coefficient ratio, so
            // tire-set, research and condition bonuses are all carried through the fade instead of
            // being re-implemented here. `rawGrip` is 0 only for a zero-grip table entry.
            if (_surfaceFade[index].IsInitialised && surfaceGrip > 1e-4f)
                grip *= MathUtil.Clamp(_surfaceFade[index].EffectiveGrip(SurfaceWet) / surfaceGrip, 0f, 1f);

            // Every stiffness the solver sees is bounded: NaN → 0, and nothing above the documented
            // envelope (SYS-SIM-001 — an out-of-range stiffness is what makes a seam read as a jolt).
            var fwd = wheel.forwardFriction;
            fwd.stiffness = SurfaceTable.ClampFrictionStiffness(
                grip * (1f + _mud.SinkFraction * -0.5f) * _mud.TractionMultiplier);
            wheel.forwardFriction = fwd;
            var side = wheel.sidewaysFriction;
            side.stiffness = SurfaceTable.ClampFrictionStiffness(grip * lateral * _mud.TractionMultiplier);
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
                if (wheel == null || !wheel.GetGroundHit(out WheelHit hit)) continue;
                _grounded[i] = true;
                groundSum += hit.point.y;
                groundedCount++;
                // Keep the last known surface while airborne: resetting to packed dirt here made a
                // jump-blip restart the cross-fade from the wrong coefficient (SYS-SIM-001). A label
                // whose enum value is not in the table is sanitised instead of indexed blindly.
                if (hit.collider != null && hit.collider.TryGetComponent<SurfaceLabel>(out var label) && label != null)
                    _surfaces[i] = SurfaceTable.Sanitize(label.surface);
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
