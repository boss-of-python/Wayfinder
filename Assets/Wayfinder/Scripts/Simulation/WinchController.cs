using System;
using System.Collections.Generic;
using Wayfinder.Core;

namespace Wayfinder.Simulation
{
    /// <summary>Appendix C winch lifecycle states.</summary>
    public enum WinchState
    {
        Idle,
        Aiming,
        Attached,
        OverloadPaused
    }

    /// <summary>Typed reason why a winch action was rejected.</summary>
    public enum WinchRejectReason
    {
        None,
        NotEquipped,
        MovingTooFast,
        NotAiming,
        NoValidAnchor,
        AnchorOutOfReach,
        AnchorObscured,
        AnchorDynamic
    }

    /// <summary>Candidate world anchor supplied by the host scene to <see cref="WinchController"/>.</summary>
    public readonly struct WinchAnchorCandidate
    {
        public readonly string Id;
        public readonly float X, Y, Z;
        /// <summary>False for dynamic vehicles or self-colliders (Appendix C: never dynamic vehicles).</summary>
        public readonly bool IsStaticAnchor;
        /// <summary>True when a straight line from fairlead to anchor is unobstructed.</summary>
        public readonly bool HasLineOfSight;

        public WinchAnchorCandidate(string id, float x, float y, float z, bool isStaticAnchor = true,
            bool hasLineOfSight = true)
        {
            Id = id ?? string.Empty;
            X = x;
            Y = y;
            Z = z;
            IsStaticAnchor = isStaticAnchor;
            HasLineOfSight = hasLineOfSight;
        }
    }

    /// <summary>Per-step output consumed by <see cref="VehicleController"/> and HUD.</summary>
    public readonly struct WinchStepOutput
    {
        public readonly WinchState State;
        public readonly bool HoldBrakes;
        public readonly string SelectedAnchorId;
        public readonly string AttachedAnchorId;
        public readonly float TargetCableLengthM;
        public readonly float CurrentSeparationM;
        public readonly float PullForceN;
        public readonly float PullFx, PullFy, PullFz;
        public readonly string WarningText;

        public WinchStepOutput(WinchState state, bool holdBrakes, string selectedAnchorId,
            string attachedAnchorId, float targetCableLengthM, float currentSeparationM,
            float pullForceN, float pullFx, float pullFy, float pullFz, string warningText)
        {
            State = state;
            HoldBrakes = holdBrakes;
            SelectedAnchorId = selectedAnchorId ?? string.Empty;
            AttachedAnchorId = attachedAnchorId ?? string.Empty;
            TargetCableLengthM = targetCableLengthM;
            CurrentSeparationM = currentSeparationM;
            PullForceN = pullForceN;
            PullFx = pullFx;
            PullFy = pullFy;
            PullFz = pullFz;
            WarningText = warningText ?? string.Empty;
        }
    }

    /// <summary>
    /// Pure, engine-free Appendix C winch state machine sharing constants and damped tension math
    /// with <see cref="WinchMath"/>:
    /// <list type="number">
    ///   <item>Engage only below <see cref="WinchMath.MaxEngageSpeedMs"/> (1.0 m/s) with brake hold while aiming.</item>
    ///   <item>Query static unobscured anchors within 12 m (Field Winch 1) or 20 m (Field Winch 2),
    ///         scored deterministically via <see cref="WinchMath.ScoreAnchor"/>.</item>
    ///   <item>Attach only on explicit player action (never automatic collision attach), setting
    ///         initial cable length to current separation.</item>
    ///   <item>Reel shortens target cable length at &lt;= <see cref="WinchMath.ReelSpeedMs"/> (0.8 m/s).</item>
    ///   <item>Tension acts strictly along fairlead-to-anchor line, capped at 8 kN / 12 kN, never pushing.</item>
    ///   <item>Continuous cap saturation for &gt;= 2.0 s pauses reeling with clear warning text.</item>
    ///   <item>Instant detach with zero residual force on cancel, module removal, reset, rescue,
    ///         vehicle disable, or app suspend.</item>
    /// </list>
    /// </summary>
    public sealed class WinchController
    {
        public const float MinCableLengthM = 0.5f;
        public const string OverloadWarning = "Winch paused: line tension high";

        readonly WinchMath _math = new WinchMath();
        readonly List<RankedAnchor> _validAnchors = new List<RankedAnchor>(16);

        float _reachM;
        float _forceCapN;
        string _selectedId = string.Empty;
        string _attachedId = string.Empty;
        float _attachedX, _attachedY, _attachedZ;
        float _targetCableLengthM;
        float _currentSeparationM;
        float _lastForceN;
        float _pullFx, _pullFy, _pullFz;

        struct RankedAnchor
        {
            public WinchAnchorCandidate Candidate;
            public float DistanceM;
            public float Score;
        }

        public WinchState State { get; private set; } = WinchState.Idle;
        public bool HoldBrakes => State == WinchState.Aiming;
        public string SelectedAnchorId => _selectedId;
        public string AttachedAnchorId => _attachedId;
        public float TargetCableLengthM => _targetCableLengthM;
        public float CurrentSeparationM => _currentSeparationM;
        public float CurrentPullForceN => _lastForceN;
        public (float x, float y, float z) CurrentPullVector => (_pullFx, _pullFy, _pullFz);
        public (float x, float y, float z) AttachedAnchorPosition => (_attachedX, _attachedY, _attachedZ);
        public string WarningText => State == WinchState.OverloadPaused ? OverloadWarning : string.Empty;
        public int ValidAnchorCount => _validAnchors.Count;

        public void ConfigureCapability(float reachM, float forceCapN)
        {
            _reachM = Math.Max(0f, reachM);
            _forceCapN = Math.Max(0f, forceCapN);
            if (_reachM <= 0f || _forceCapN <= 0f)
                Detach();
        }

        /// <summary>Appendix C §1: engage aim mode only below 1.0 m/s when a winch is fitted.</summary>
        public bool TryEngageAim(float speedMps, out WinchRejectReason reason)
        {
            if (_reachM <= 0f || _forceCapN <= 0f)
            {
                reason = WinchRejectReason.NotEquipped;
                return false;
            }
            if (Math.Abs(speedMps) >= WinchMath.MaxEngageSpeedMs)
            {
                reason = WinchRejectReason.MovingTooFast;
                return false;
            }
            Detach();
            State = WinchState.Aiming;
            reason = WinchRejectReason.None;
            return true;
        }

        /// <summary>
        /// Appendix C §2: refreshes candidate anchors in reach, excluding dynamic objects and
        /// obscured lines, ranked by camera-forward alignment, vehicle heading, and distance.
        /// </summary>
        public int UpdateAimCandidates(
            IReadOnlyList<WinchAnchorCandidate> candidates,
            float fairleadX, float fairleadY, float fairleadZ,
            float camDirX, float camDirY, float camDirZ,
            float vehDirX, float vehDirY, float vehDirZ)
        {
            _validAnchors.Clear();
            if (State != WinchState.Aiming || candidates == null || _reachM <= 0f)
            {
                _selectedId = string.Empty;
                return 0;
            }

            Normalize(ref camDirX, ref camDirY, ref camDirZ);
            Normalize(ref vehDirX, ref vehDirY, ref vehDirZ);

            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (string.IsNullOrEmpty(c.Id) || !c.IsStaticAnchor)
                    continue;
                float dx = c.X - fairleadX, dy = c.Y - fairleadY, dz = c.Z - fairleadZ;
                float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
                float ux = dx, uy = dy, uz = dz;
                Normalize(ref ux, ref uy, ref uz);
                float camDot = ux * camDirX + uy * camDirY + uz * camDirZ;
                float vehDot = ux * vehDirX + uy * vehDirY + uz * vehDirZ;
                float usefulness = MathUtil.Clamp01(0.67f * (camDot * 0.5f + 0.5f) + 0.33f * (vehDot * 0.5f + 0.5f));
                float score = WinchMath.ScoreAnchor(dist, _reachM, usefulness, c.HasLineOfSight);
                if (float.IsNegativeInfinity(score)) continue;
                _validAnchors.Add(new RankedAnchor { Candidate = c, DistanceM = dist, Score = score });
            }

            _validAnchors.Sort((a, b) =>
            {
                int cmp = b.Score.CompareTo(a.Score);
                return cmp != 0 ? cmp : string.CompareOrdinal(a.Candidate.Id, b.Candidate.Id);
            });

            if (_validAnchors.Count == 0)
            {
                _selectedId = string.Empty;
                return 0;
            }

            bool keptSelection = false;
            for (int i = 0; i < _validAnchors.Count; i++)
                if (_validAnchors[i].Candidate.Id == _selectedId) { keptSelection = true; break; }
            if (!keptSelection)
                _selectedId = _validAnchors[0].Candidate.Id;
            return _validAnchors.Count;
        }

        public bool SelectAnchor(string anchorId)
        {
            if (State != WinchState.Aiming || string.IsNullOrEmpty(anchorId)) return false;
            for (int i = 0; i < _validAnchors.Count; i++)
            {
                if (_validAnchors[i].Candidate.Id == anchorId)
                {
                    _selectedId = anchorId;
                    return true;
                }
            }
            return false;
        }

        public string CycleAnchor(int step = 1)
        {
            if (State != WinchState.Aiming || _validAnchors.Count == 0) return string.Empty;
            int idx = 0;
            for (int i = 0; i < _validAnchors.Count; i++)
                if (_validAnchors[i].Candidate.Id == _selectedId) { idx = i; break; }
            int n = _validAnchors.Count;
            int next = ((idx + step) % n + n) % n;
            _selectedId = _validAnchors[next].Candidate.Id;
            return _selectedId;
        }

        /// <summary>Appendix C §3: explicit player attach at current separation (never auto-triggered).</summary>
        public bool TryAttachSelected(float fairleadX, float fairleadY, float fairleadZ, out WinchRejectReason reason)
        {
            if (State != WinchState.Aiming)
            {
                reason = WinchRejectReason.NotAiming;
                return false;
            }
            if (_reachM <= 0f || _forceCapN <= 0f)
            {
                reason = WinchRejectReason.NotEquipped;
                return false;
            }
            RankedAnchor? chosen = null;
            for (int i = 0; i < _validAnchors.Count; i++)
                if (_validAnchors[i].Candidate.Id == _selectedId) { chosen = _validAnchors[i]; break; }
            if (chosen == null)
            {
                reason = WinchRejectReason.NoValidAnchor;
                return false;
            }
            return TryAttachCandidate(chosen.Value.Candidate, fairleadX, fairleadY, fairleadZ, out reason);
        }

        /// <summary>Direct explicit attach against a specific candidate (validates static, LOS, and reach).</summary>
        public bool TryAttachCandidate(WinchAnchorCandidate candidate,
            float fairleadX, float fairleadY, float fairleadZ, out WinchRejectReason reason)
        {
            if (State != WinchState.Aiming)
            {
                reason = WinchRejectReason.NotAiming;
                return false;
            }
            if (_reachM <= 0f || _forceCapN <= 0f)
            {
                reason = WinchRejectReason.NotEquipped;
                return false;
            }
            if (!candidate.IsStaticAnchor)
            {
                reason = WinchRejectReason.AnchorDynamic;
                return false;
            }
            if (!candidate.HasLineOfSight)
            {
                reason = WinchRejectReason.AnchorObscured;
                return false;
            }
            float dx = candidate.X - fairleadX, dy = candidate.Y - fairleadY, dz = candidate.Z - fairleadZ;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (dist > _reachM)
            {
                reason = WinchRejectReason.AnchorOutOfReach;
                return false;
            }

            _attachedId = candidate.Id;
            _attachedX = candidate.X;
            _attachedY = candidate.Y;
            _attachedZ = candidate.Z;
            _currentSeparationM = dist;
            _targetCableLengthM = MathUtil.Clamp(dist, MinCableLengthM, _reachM);
            _math.Reset();
            _lastForceN = 0f;
            _pullFx = _pullFy = _pullFz = 0f;
            State = WinchState.Attached;
            reason = WinchRejectReason.None;
            return true;
        }

        /// <summary>
        /// Appendix C §4–§6: advances reeling and computes bounded line tension along fairlead→anchor.
        /// </summary>
        public WinchStepOutput Step(float fairleadX, float fairleadY, float fairleadZ,
            bool reelHeld, float deltaSeconds, float cableSeparationVelocityMs = 0f)
        {
            if (_reachM <= 0f || _forceCapN <= 0f)
                Detach();

            float dt = Math.Max(0f, deltaSeconds);
            if (State == WinchState.Idle || State == WinchState.Aiming)
            {
                _lastForceN = 0f;
                _pullFx = _pullFy = _pullFz = 0f;
                return CurrentOutput();
            }

            if (State == WinchState.Attached && reelHeld && dt > 0f)
                _targetCableLengthM = WinchMath.Reel(_targetCableLengthM, 1f, dt);

            float dx = _attachedX - fairleadX;
            float dy = _attachedY - fairleadY;
            float dz = _attachedZ - fairleadZ;
            float dist = (float)Math.Sqrt(dx * dx + dy * dy + dz * dz);
            _currentSeparationM = dist;

            float force = _math.ComputePullForce(
                dist, _targetCableLengthM, cableSeparationVelocityMs, _forceCapN, dt, out bool paused);
            if (paused)
            {
                State = WinchState.OverloadPaused;
            }
            else if (State == WinchState.OverloadPaused && _math.ExcessTensionSeconds <= 0f)
            {
                State = WinchState.Attached;
            }

            _lastForceN = force;
            if (dist > 1e-4f && force > 0f)
            {
                float inv = force / dist;
                _pullFx = dx * inv;
                _pullFy = dy * inv;
                _pullFz = dz * inv;
            }
            else
            {
                _pullFx = _pullFy = _pullFz = 0f;
            }

            return CurrentOutput();
        }

        /// <summary>Appendix C §7: instant detach with zero residual force.</summary>
        public void Detach()
        {
            State = WinchState.Idle;
            _validAnchors.Clear();
            _selectedId = string.Empty;
            _attachedId = string.Empty;
            _attachedX = _attachedY = _attachedZ = 0f;
            _targetCableLengthM = 0f;
            _currentSeparationM = 0f;
            _lastForceN = 0f;
            _pullFx = _pullFy = _pullFz = 0f;
            _math.Reset();
        }

        WinchStepOutput CurrentOutput() => new WinchStepOutput(
            State, HoldBrakes, _selectedId, _attachedId, _targetCableLengthM,
            _currentSeparationM, _lastForceN, _pullFx, _pullFy, _pullFz, WarningText);

        static void Normalize(ref float x, ref float y, ref float z)
        {
            float len = (float)Math.Sqrt(x * x + y * y + z * z);
            if (len > 1e-5f) { x /= len; y /= len; z /= len; }
            else { x = 0f; y = 0f; z = 1f; }
        }
    }
}
