using System;
using System.Collections.Generic;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>
    /// Rigidbody + four WheelCollider vehicle controller (§4.1, Appendix C, Appendix D physics outline).
    /// Owns actuation only — no UI, no permanent save writes (§16.1).
    /// Researched suspension travel, clearance, dampers, and winch capability enter through
    /// <see cref="ApplyEffectiveStats"/> (KI-025, D-044), and Appendix C winch recovery runs through
    /// the pure <see cref="WinchController"/> state machine.
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

        [Header("Winch rig (Appendix C, optional)")]
        public Transform winchFairlead;
        public LineRenderer winchCableRenderer;

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
        readonly WinchController _winch = new WinchController();
        readonly List<WinchAnchorCandidate> _winchCandidates = new List<WinchAnchorCandidate>(16);

        /// <summary>Stats resolved by the last FixedUpdate (exposed for the HUD and the safe-pose gate).</summary>
        EffectiveVehicleStats _stats;
        WheelRigSettings _rigSettings;
        bool _rigApplied;
        readonly Vector3[] _baseWheelCenters = new Vector3[4];
        readonly bool[] _centerCaptured = new bool[4];

        float _steerCommand;
        float _throttleCommand;
        float _brakeCommand;
        bool _handbrake;
        bool _crawl;
        bool _winchReelHeld;
        bool _engineOn = true;
        float _fuelLiters;
        float _cargoMassKg;

        // Reused buffers — zero steady-state allocation (§18.2).
        readonly bool[] _grounded = new bool[4];
        readonly float[] _slip = new float[4];
        readonly float[] _wheelTorque = new float[4];
        readonly SurfaceType[] _surfaces = new SurfaceType[4];

        public bool SurfaceWet { get; set; }

        public float FuelLiters => _fuelLiters;
        public float SpeedMetersPerSecond => _rb != null ? _rb.linearVelocity.magnitude : 0f;
        public float SpeedKph => SpeedMetersPerSecond * 3.6f;
        public DamageModel Damage => _damage;
        public MudSinkModel Mud => _mud;
        public WaterCrossingModel Water => _water;
        public RolloverMonitor Rollover => _rollover;
        public Powertrain PowertrainSystem => _powertrain;
        public WinchController Winch => _winch;
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

        public void SetWinchReelHeld(bool held) => _winchReelHeld = held;

        public Vector3 FairleadWorldPosition =>
            winchFairlead != null
                ? winchFairlead.position
                : transform.TransformPoint(new Vector3(0f, 0.45f, spec != null ? spec.WheelbaseM * 0.55f : 1.4f));

        /// <summary>Appendix C: toggles or engages winch aiming below 1.0 m/s.</summary>
        public bool EngageWinchAim(out WinchRejectReason reason)
        {
            bool ok = _winch.TryEngageAim(SpeedMetersPerSecond, out reason);
            if (ok) RefreshWinchCandidates();
            return ok;
        }

        public bool AttachSelectedWinchAnchor(out WinchRejectReason reason)
        {
            RefreshWinchCandidates();
            Vector3 fp = FairleadWorldPosition;
            return _winch.TryAttachSelected(fp.x, fp.y, fp.z, out reason);
        }

        /// <summary>Appendix C §7: instant winch detach on cancel, reset, rescue, disable, or suspend.</summary>
        public void DetachWinch()
        {
            _winchReelHeld = false;
            _winch.Detach();
            if (winchCableRenderer != null) winchCableRenderer.enabled = false;
        }

        /// <summary>Switches active vehicle specification safely at a garage (§10.1).</summary>
        public void SwitchVehicleSpec(VehicleSpec nextSpec)
        {
            if (nextSpec == null) throw new ArgumentNullException(nameof(nextSpec));
            DetachWinch();
            spec = nextSpec;
            _rigApplied = false;
            _powertrain.Reset(nextSpec);
            ApplyEffectiveStats(ResolveStats());
        }

        /// <summary>Losing focus/pause zeroes throttle, applies a parking hold, and detaches active winch (§3.2, Appendix C).</summary>
        public void ZeroInputsAndHold()
        {
            _throttleCommand = 0f;
            _brakeCommand = 1f;
            _steerCommand = 0f;
            _handbrake = true;
            _winchReelHeld = false;
        }

        public void SetCargoMass(float kg) => _cargoMassKg = Math.Max(0f, kg);
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
        /// spring/damper rates, wheel radius, clearance wheel-center offset, and winch capability to each
        /// WheelCollider whenever <see cref="WheelRigMath.RequiresGeometryUpdate"/> trips (§18.2).
        /// </summary>
        public void ApplyEffectiveStats(EffectiveVehicleStats stats)
        {
            if (stats == null || spec == null) return;
            _stats = stats;
            var next = WheelRigMath.FromStats(spec, stats);
            if (!_rigApplied || WheelRigMath.RequiresGeometryUpdate(_rigSettings, next))
            {
                for (int i = 0; i < 4; i++) ApplyWheelGeometry(GetWheel(i), i, next);
                _winch.ConfigureCapability(next.WinchReachM, next.WinchForceCapN);
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
        public void SetEngineOn(bool on)
        {
            _engineOn = on;
            if (!on) DetachWinch();
        }

        public void ApplyWaterStall() { _damage.ApplyWaterStall(); SetEngineOn(false); }

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

            if (_winch.State == WinchState.Aiming)
                RefreshWinchCandidates();

            bool holdBrakes = _handbrake || _winch.HoldBrakes;
            float effectiveThrottle = _winch.HoldBrakes ? 0f : _throttleCommand;
            float effectiveBrake = _winch.HoldBrakes ? 1f : _brakeCommand;

            bool reverseIndicated;
            float totalDriveTorque = _powertrain.Step(
                spec, stats,
                AverageWheelSpeedMs(),
                effectiveThrottle, effectiveBrake, holdBrakes, _crawl,
                dt, out reverseIndicated);

            for (int i = 0; i < 4; i++)
            {
                _slip[i] = FuelModel.SlipRatio(
                    _grounded[i] ? GetWheel(i).rpm * Mathf.PI / 30f : 0f,
                    spec.WheelRadiusM,
                    LongitudinalSpeedMs());
            }
            Powertrain.SplitAwdTorque(totalDriveTorque, _grounded, _slip, _wheelTorque, _powertrain.DiffLockEngaged);

            if (!_engineOn || _fuelLiters <= 0f || _damage.EngineDisabled)
                Array.Clear(_wheelTorque, 0, _wheelTorque.Length);
            _rb.mass = spec.DryMassKg + _fuelLiters * VehicleSpec.FuelDensityKgPerLiter + _cargoMassKg;
            ApplyWheelForces(stats, reverseIndicated, effectiveThrottle, effectiveBrake, holdBrakes);
            StepWinchPhysics(dt);

            float mudDepthM = CurrentMudDepthM();
            MudDepthMeters = mudDepthM;
            _mud.Step(effectiveThrottle, LongitudinalSpeedMs(), mudDepthM, dt);
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
            float driveDemand = _powertrain.InReverse ? effectiveBrake : effectiveThrottle;
            float drain = FuelModel.LitersPerSecond(driveDemand, load, slipSeverity, _engineOn)
                          * stats.PoweredFuelDrainFactor
                          * (1f + _mud.SinkFraction);
            _fuelLiters = FuelModel.Integrate(_fuelLiters, stats.FuelCapacityLiters, drain, dt);

            UpdateState();
            if (State == VehicleState.Disabled)
                DetachWinch();
        }

        void RefreshWinchCandidates()
        {
            _winchCandidates.Clear();
            Vector3 fp = FairleadWorldPosition;
            var anchors = WinchAnchor.ActiveAnchors;
            for (int i = 0; i < anchors.Count; i++)
            {
                var anchor = anchors[i];
                if (anchor == null || anchor.transform.IsChildOf(transform)) continue;
                bool isDynamic = anchor.GetComponentInParent<Rigidbody>() != null;
                Vector3 ap = anchor.WorldAttachPoint;
                bool los = true;
                if (Physics.Linecast(fp, ap, out RaycastHit hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != null && !hit.collider.transform.IsChildOf(anchor.transform) &&
                        !hit.collider.transform.IsChildOf(transform))
                    {
                        los = false;
                    }
                }
                _winchCandidates.Add(new WinchAnchorCandidate(anchor.AnchorId, ap.x, ap.y, ap.z, !isDynamic, los));
            }
            Camera cam = Camera.main;
            Vector3 camFwd = cam != null ? cam.transform.forward : transform.forward;
            Vector3 vehFwd = transform.forward;
            _winch.UpdateAimCandidates(_winchCandidates, fp.x, fp.y, fp.z,
                camFwd.x, camFwd.y, camFwd.z, vehFwd.x, vehFwd.y, vehFwd.z);
        }

        void StepWinchPhysics(float dt)
        {
            Vector3 fp = FairleadWorldPosition;
            float sepVel = 0f;
            if (_rb != null && (_winch.State == WinchState.Attached || _winch.State == WinchState.OverloadPaused))
            {
                var (ax, ay, az) = _winch.AttachedAnchorPosition;
                Vector3 toAnchor = new Vector3(ax - fp.x, ay - fp.y, az - fp.z);
                if (toAnchor.sqrMagnitude > 1e-6f)
                    sepVel = -Vector3.Dot(_rb.GetPointVelocity(fp), toAnchor.normalized);
            }
            var step = _winch.Step(fp.x, fp.y, fp.z, _winchReelHeld, dt, sepVel);
            if (_rb != null && step.PullForceN > 0f)
                _rb.AddForceAtPosition(new Vector3(step.PullFx, step.PullFy, step.PullFz), fp, ForceMode.Force);
        }

        void ApplyWheelForces(EffectiveVehicleStats stats, bool reverseIndicated,
            float throttleCmd, float brakeCmd, bool handbrakeCmd)
        {
            SteeringModel.AckermannAngles(_steerCommand, SpeedKph, spec.WheelbaseM, spec.TrackM,
                out float inner, out float outer);
            bool turningLeft = _steerCommand < 0f;

            float serviceBrake = reverseIndicated ? throttleCmd : brakeCmd;
            SetWheelDrive(wheelFrontLeft, _wheelTorque[0], serviceBrake, stats,
                turningLeft ? inner : outer);
            SetWheelDrive(wheelFrontRight, _wheelTorque[1], serviceBrake, stats,
                turningLeft ? outer : inner);
            SetWheelDrive(wheelRearLeft, _wheelTorque[2], serviceBrake, stats, 0f);
            SetWheelDrive(wheelRearRight, _wheelTorque[3], serviceBrake, stats, 0f);

            if (handbrakeCmd && wheelRearLeft != null && wheelRearRight != null)
            {
                wheelRearLeft.brakeTorque = spec.BrakeTorqueRearNmPerWheel * 1.5f;
                wheelRearRight.brakeTorque = spec.BrakeTorqueRearNmPerWheel * 1.5f;
            }

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
            // deep mud and asphalt no longer share one packed-dirt coefficient (DEF-005).
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
            float travel = Mathf.Max(0.001f, wheel.suspensionDistance);
            float extension = -wheel.transform.InverseTransformPoint(hit.point).y - wheel.radius;
            return travel - Mathf.Clamp(extension, 0f, travel);
        }

        EffectiveVehicleStats ResolveStats()
        {
            var stats = _resolver.Resolve(
                spec, _fuelLiters, _cargoMassKg, tireSet, DominantSurface(), SurfaceWet,
                _damage.EngineCondition, _damage.SuspensionCondition, _damage.TiresCondition,
                false, false, false);
            stats.PunctureActive = _damage.HasPuncture;
            _stats = stats;
            return stats;
        }

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

        void OnApplicationPause(bool paused)
        {
            if (paused)
            {
                DetachWinch();
                ZeroInputsAndHold();
            }
        }

        void OnDisable()
        {
            _waterContacts.Clear();
            WaterDepthMeters = 0f;
            DetachWinch();
            ZeroInputsAndHold();
        }

        float CurrentWaterDepthM()
        {
            float depth = 0f;
            foreach (var contact in _waterContacts)
            {
                if (contact.Key == null || !contact.Key.enabled || contact.Value == null || !contact.Value.isActiveAndEnabled) continue;
                if (_hull != null && !contact.Key.bounds.Intersects(_hull.bounds)) continue;
                depth = Mathf.Max(depth, contact.Value.surfaceHeightY - _groundHeightY);
            }
            return Mathf.Max(0f, depth);
        }

        public float CurrentBodyRollDegrees() => Vector3.SignedAngle(transform.up, Vector3.up, transform.forward);

        float CurrentTerrainSlopeDegrees()
        {
            Vector3 lateral = Vector3.ProjectOnPlane(transform.up, transform.forward);
            return Vector3.Angle(Vector3.up, lateral);
        }

        float CurrentTerrainSlopeRadians() => Mathf.Acos(Mathf.Clamp(transform.up.y, -1f, 1f));

        public float GroundClearanceMeters => _stats != null ? _stats.GroundClearanceM : 0f;

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
                State = VehicleState.Airborne;
            else if (SpeedMetersPerSecond > 0.5f)
                State = VehicleState.Driving;
            else
                State = VehicleState.Parked;
        }

        void LateUpdate()
        {
            SyncWheelVisual(wheelFrontLeft, wheelVisualFrontLeft);
            SyncWheelVisual(wheelFrontRight, wheelVisualFrontRight);
            SyncWheelVisual(wheelRearLeft, wheelVisualRearLeft);
            SyncWheelVisual(wheelRearRight, wheelVisualRearRight);

            if (winchCableRenderer != null)
            {
                bool active = _winch.State == WinchState.Attached || _winch.State == WinchState.OverloadPaused;
                winchCableRenderer.enabled = active;
                if (active)
                {
                    var (ax, ay, az) = _winch.AttachedAnchorPosition;
                    winchCableRenderer.positionCount = 2;
                    winchCableRenderer.SetPosition(0, FairleadWorldPosition);
                    winchCableRenderer.SetPosition(1, new Vector3(ax, ay, az));
                }
            }
        }

        static void SyncWheelVisual(WheelCollider wheel, Transform visual)
        {
            if (wheel == null || visual == null) return;
            wheel.GetWorldPose(out Vector3 pos, out Quaternion rot);
            visual.SetPositionAndRotation(pos, rot);
        }
    }
}
