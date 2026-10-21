using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Platform;
using Wayfinder.Progression;
using Wayfinder.Simulation;
// UnityEngine also defines a Pose struct; the alias makes every use below resolve to the campaign type.
using Pose = Wayfinder.Campaign.Pose;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Vertical-slice scene binding (§21.3): renders the campaign HUD/garage/summary and forwards
    /// vehicle telemetry + touch/keyboard input to the engine-free <see cref="SliceFieldController"/>.
    /// It contains no gameplay rules of its own — pickups, discovery, checkpoints, arrival,
    /// banking, rescue and saving are decided in Wayfinder.Campaign (headless-tested).
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class VerticalSliceDriver : MonoBehaviour
    {
        public VehicleController vehicle;
        public InputRouter input;
        public ChaseCameraBehaviour chaseCamera;
        public int researchRowsShown = 6;

        CampaignHost _host;
        CampaignSession _session;
        SliceFieldController _field;
        LifecycleHandler _lifecycle;
        readonly HudModel _hud = new HudModel();
        readonly Queue<string> _toasts = new Queue<string>();

        RectTransform _root, _drivePanel, _overlay;
        Text _objective, _status, _prompt, _toast;
        Button _pauseButton, _interactButton, _rescueButton;
        bool _paused, _left, _right, _gas, _brake, _interactTouch, _rescueArmed;
        string _armedResearchId = string.Empty;
        string _armedLogisticsHub = string.Empty;
        float _nextHud, _toastTimer;
        MapPresenter _mapPresenter;
        MinimapView _minimap;
        Button _suppliesButton;
        readonly SuppliesModel _supplies = new SuppliesModel();
        string _armedSupply = string.Empty, _supplyMessage = string.Empty, _armedSellKey = string.Empty;
        MapOverlayView _mapView;
        bool _mapPausedField;
        int _mapClosedFrame = -1;

        void Start()
        {
            _root = PrototypeUi.CreateRoot("Slice UI");
            _host = CampaignHost.Instance;
            _session = _host.Session;
            if (vehicle == null || input == null || _session == null)
            {
                PrototypeUi.Label(_root, _session == null
                        ? "No campaign loaded — start or continue a save slot from the title screen."
                        : "Scene references missing — rebuild scenes with Tools → Wayfinder → Build Scenes.",
                    new Vector2(.1f, .45f), new Vector2(.9f, .65f));
                PrototypeUi.Button(_root, "Title", new Vector2(.4f, .3f), new Vector2(.6f, .4f)).onClick.AddListener(GoToTitle);
                if (vehicle != null) vehicle.ZeroInputsAndHold();
                enabled = false;
                return;
            }
            _lifecycle = gameObject.AddComponent<LifecycleHandler>();
            _lifecycle.OnSuspendRequested += OnSuspend;
            _field = new SliceFieldController(_session) { LineOfSight = HasLineOfSight };
            BuildDriveUi();
            _mapPresenter = new MapPresenter(_session);
            _minimap = MinimapView.Create(_drivePanel, _mapPresenter, new Vector2(.80f, .44f), new Vector2(.98f, .77f));
            _minimap.Clicked += OpenMap;
            PlaceVehicle(_session.ResumePose);
            ApplySessionToVehicle();
            if (_session.Expedition.IsActive) ShowDrive();
            else ShowGarage();
        }

        // ------------------------------------------------------------------ frame loop

        void Update()
        {
            bool pausePressed = (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ||
                                (Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame);
            // The open map reads its own close keys (inputs are suppressed); ignore keys in the frame it closed.
            bool mapKey = input.MapPressed ||
                          (Keyboard.current != null && Keyboard.current.mKey.wasPressedThisFrame) ||
                          (Gamepad.current != null && Gamepad.current.selectButton.wasPressedThisFrame);
            bool mapJustClosed = Time.frameCount == _mapClosedFrame;
            if (pausePressed && _session.Expedition.IsActive && _mapView == null && !mapJustClosed) SetPaused(!_paused);
            if (mapKey && _mapView == null && !mapJustClosed && !_paused) OpenMap();

            if (_session.Expedition.IsActive && !_paused && _overlay == null && _mapView == null)
                HandleFrame(_field.Update(BuildFieldInput(Time.deltaTime)));
            _mapPresenter?.Tick(Time.unscaledDeltaTime); // 2 Hz reveals land here; budgeted redraw + upload

            if (_session.PendingNoticeCount > 0)
                foreach (var notice in _session.DrainNotices()) _toasts.Enqueue(notice.Text);
            _toastTimer -= Time.unscaledDeltaTime;
            if (_toastTimer <= 0f && _toast != null)
            {
                _toast.text = _toasts.Count > 0 ? _toasts.Dequeue() : string.Empty;
                _toastTimer = _toast.text.Length > 0 ? 3f : 0f;
            }
            if (Time.unscaledTime >= _nextHud && _objective != null)
            {
                _nextHud = Time.unscaledTime + .1f;
                HudModel.Build(_session, _hud);
                _objective.text = _hud.ObjectiveLine;
                _status.text = $"{_hud.FuelLine}{(_hud.FuelWarning ? " ⚠" : "")}\n{_hud.CargoLine}{(_hud.CargoWarning ? " ⚠" : "")}\n" +
                               $"{_hud.SalvageLine}\n{_hud.SaveLine} · {vehicle.SpeedKph:0} km/h";
            }
        }

        void LateUpdate()
        {
            if (_session == null || vehicle == null || input == null) return;
            if (_paused || _overlay != null || _mapView != null || !_session.Expedition.IsActive)
            {
                vehicle.ZeroInputsAndHold();
                return;
            }
            var command = input.Current;
            vehicle.SetDriveCommand(command.Steer, command.Throttle, command.Brake, command.Handbrake, command.Crawl);
            if (chaseCamera != null)
            {
                var delta = input.CameraLookDelta;
                if (delta.sqrMagnitude > .0001f) chaseCamera.SetOrbitInput(delta.x, -delta.y);
            }
        }

        FieldInput BuildFieldInput(float dt)
        {
            var t = vehicle.transform;
            var damage = vehicle.Damage;
            return new FieldInput
            {
                Pose = new Pose(t.position.x, t.position.y, t.position.z, t.eulerAngles.y),
                SpeedKph = vehicle.SpeedKph,
                DeltaSeconds = dt,
                FuelLiters = vehicle.FuelLiters,
                EngineCondition = damage.EngineCondition,
                SuspensionCondition = damage.SuspensionCondition,
                TiresCondition = damage.TiresCondition,
                BodyCondition = damage.BodyCondition,
                // Real measurements. Until 2026-10-19 Grounded was `State != Airborne` while the
                // controller never assigned Airborne (always true), RollDegrees was a ZXY euler
                // component (≈ true roll only near yaw 0/180°), and mud depth/clearance/tap were
                // constants, so deep-mud hazards, the safe-pose gate and the §3.5 tap alternative
                // were all inert (DEF-015).
                Grounded = vehicle.AnyWheelGrounded,
                RollDegrees = vehicle.CurrentBodyRollDegrees(),
                InWater = vehicle.WaterDepthMeters > .05f,
                InDeepMud = vehicle.MudDepthMeters > 0f,
                ClearanceMeters = vehicle.GroundClearanceMeters,
                InteractPressed = _interactTouch || input.InteractHeld,
                TapToInteract = input != null && input.InteractTapped
            };
        }

        void HandleFrame(FieldFrame frame)
        {
            if (frame.Prompt.Visible)
                _prompt.text = frame.Prompt.Available
                    ? frame.Prompt.Label + (frame.HoldProgress01 > 0f ? $"  {frame.HoldProgress01 * 100f:0}%" : string.Empty)
                    : frame.Prompt.BlockedReason;
            else if (frame.ObservingLandmarkId.Length > 0)
                _prompt.text = $"Observing {_session.Content.LandmarkName(frame.ObservingLandmarkId)}… " +
                               $"{Mathf.Min(frame.ObservationSeconds, 2f):0.0}/2.0 s (stay below 15 km/h)";
            else _prompt.text = string.Empty;
            _interactButton.interactable = frame.Prompt.Available;

            if (frame.InteractionCommitted && !frame.InteractionResult.Success)
                _toasts.Enqueue(frame.InteractionResult.Detail);
            if (frame.InteractionCommitted) vehicle.SetCargoMass(_session.Inventory.TotalMassKg(CampaignSession.Cargo));
            if (frame.ArrivalTriggered && frame.Extraction != null) ShowSummary(frame.Extraction);
        }

        bool HasLineOfSight(WorldPoint from, WorldPoint to)
        {
            var a = new Vector3(from.X, from.Y + 1.5f, from.Z);
            var b = new Vector3(to.X, to.Y + 2f, to.Z);
            if (!Physics.Linecast(a, b, out var hit, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore)) return true;
            return hit.collider != null && hit.collider.name.StartsWith("Landmark ", StringComparison.Ordinal);
        }

        // ------------------------------------------------------------------ vehicle sync

        void PlaceVehicle(Pose pose)
        {
            var position = new Vector3(pose.Position.X, Mathf.Max(.6f, pose.Position.Y + .3f), pose.Position.Z);
            var rotation = Quaternion.Euler(0f, pose.YawDegrees, 0f);
            vehicle.transform.SetPositionAndRotation(position, rotation);
            var body = vehicle.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.position = position;
                body.rotation = rotation;
                body.linearVelocity = Vector3.zero;   // restored with zero velocity (§17.4)
                body.angularVelocity = Vector3.zero;
            }
            vehicle.ZeroInputsAndHold();
        }

        /// <summary>Session → vehicle after load/extraction: resolver, tire set, resolved rig
        /// geometry (ApplyEffectiveStats, KI-025/D-044), fuel, condition and cargo mass.</summary>
        void ApplySessionToVehicle()
        {
            // Researched modules reach the physics rig through its own resolver (KI-025, D-030); the
            // tank clamp follows the resolved capacity, so the spec is no longer widened at runtime.
            _session.ConfigureResolver(vehicle.StatsResolver);
            // The mounted tire set drives the surface grip lookup (§10.2), and the resolver composes
            // only the modules fitted into their slots, so the garage shows what the rig will do.
            vehicle.tireSet = _session.SelectedTireSet;
            vehicle.SetFuel(_session.FuelLiters);
            vehicle.Damage.EngineCondition = _session.Damage.EngineCondition;
            vehicle.Damage.SuspensionCondition = _session.Damage.SuspensionCondition;
            vehicle.Damage.TiresCondition = _session.Damage.TiresCondition;
            vehicle.Damage.BodyCondition = _session.Damage.BodyCondition;
            vehicle.SetCargoMass(_session.Inventory.TotalMassKg(CampaignSession.Cargo));
            if (_session.Damage.EngineCondition > 0) vehicle.RestartAfterWaterRecovery();
        }

        // ------------------------------------------------------------------ UI: driving

        void BuildDriveUi()
        {
            _drivePanel = PrototypeUi.Rect("Drive", _root, Vector2.zero, Vector2.one);
            _objective = PrototypeUi.Label(_drivePanel, "", new Vector2(.02f, .88f), new Vector2(.70f, .99f), 20);
            _objective.alignment = TextAnchor.UpperLeft;
            _status = PrototypeUi.Label(_drivePanel, "", new Vector2(.02f, .66f), new Vector2(.45f, .88f), 16);
            _status.alignment = TextAnchor.UpperLeft;
            _toast = PrototypeUi.Label(_drivePanel, "", new Vector2(.25f, .56f), new Vector2(.75f, .66f), 20);
            _prompt = PrototypeUi.Label(_drivePanel, "", new Vector2(.25f, .27f), new Vector2(.75f, .36f), 20);
            _pauseButton = PrototypeUi.Button(_drivePanel, "Pause", new Vector2(.84f, .89f), new Vector2(.98f, .98f));
            _pauseButton.onClick.AddListener(() => SetPaused(!_paused));
            _rescueButton = PrototypeUi.Button(_drivePanel, "Rescue", new Vector2(.84f, .79f), new Vector2(.98f, .88f));
            _rescueButton.onClick.AddListener(OnRescue);
            _suppliesButton = PrototypeUi.Button(_drivePanel, "Supplies", new Vector2(.02f, .56f), new Vector2(.16f, .64f));
            _suppliesButton.onClick.AddListener(() =>
            {
                _armedSupply = _supplyMessage = string.Empty;
                ShowSupplies();
            });

            _interactButton = PrototypeUi.Button(_drivePanel, "HOLD\nINTERACT", new Vector2(.42f, .14f), new Vector2(.58f, .26f));
            _interactButton.navigation = new Navigation { mode = Navigation.Mode.None };
            var interactHold = _interactButton.gameObject.AddComponent<TouchHoldButton>();
            interactHold.Changed += value => _interactTouch = value && !_paused;

            Hold("LEFT", new Vector2(.02f, .04f), new Vector2(.18f, .24f), value => _left = value);
            Hold("RIGHT", new Vector2(.20f, .04f), new Vector2(.36f, .24f), value => _right = value);
            Hold("BRAKE", new Vector2(.64f, .04f), new Vector2(.80f, .24f), value => _brake = value);
            Hold("GAS", new Vector2(.82f, .04f), new Vector2(.98f, .24f), value => _gas = value);
        }

        void Hold(string label, Vector2 min, Vector2 max, Action<bool> assign)
        {
            var button = PrototypeUi.Button(_drivePanel, label, min, max);
            button.navigation = new Navigation { mode = Navigation.Mode.None };
            var hold = button.gameObject.AddComponent<TouchHoldButton>();
            hold.Changed += value =>
            {
                assign(!_paused && value);
                input.SetTouchSteer((_right ? 1f : 0f) - (_left ? 1f : 0f));
                input.SetTouchPedal(_gas ? 1f : 0f, _brake ? 1f : 0f);
            };
        }

        void ShowDrive()
        {
            CloseOverlay();
            _drivePanel.gameObject.SetActive(true);
            _field.ResetForExpedition();
        }

        void OnRescue()
        {
            if (!_session.Expedition.IsActive) return;
            if (!_rescueArmed)
            {
                _rescueArmed = true;
                _rescueButton.GetComponentInChildren<Text>().text = _session.RescueLossApplies ? "Confirm\n(−25% loose)" : "Confirm\n(free)";
                return;
            }
            _rescueArmed = false;
            _rescueButton.GetComponentInChildren<Text>().text = "Rescue";
            var summary = _session.FullRescue();
            if (summary.Success) ShowSummary(summary);
            else _toasts.Enqueue(SummaryText.Describe(summary, _session));
        }

        void OnSuspend()
        {
            if (_mapView != null) _mapView.Close();
            SetPaused(true);
        }

        // ------------------------------------------------------------------ field supplies (§5.5, §5.6, §10.3)

        /// <summary>
        /// Supplies overlay. While it is open the field controller is not ticked and LateUpdate holds
        /// the vehicle, so "use while stopped" is reached by simply opening it. Rules and text come
        /// from <see cref="CampaignSession"/> / <see cref="SuppliesModel"/>.
        /// </summary>
        void ShowSupplies()
        {
            if (!_session.Expedition.IsActive || _paused || _mapView != null) return;
            float speed = vehicle.SpeedKph;
            bool inWater = vehicle.WaterDepthMeters > .05f;
            SuppliesModel.Build(_session, speed, inWater, _supplies);
            var overlay = OpenOverlay("Supplies");
            PrototypeUi.Label(overlay, speed > CampaignSession.SupplyMaxSpeedKph ? "Field supplies — stopping the vehicle… (Refresh)"
                : "Field supplies", new Vector2(.1f, .85f), new Vector2(.9f, .95f), 24);

            SupplyLine(overlay, _supplies.FuelCanLine, .72f);
            var fuel = PrototypeUi.Button(overlay, _armedSupply == CampaignSession.FuelCanId ? "Confirm (some wasted)" : "Use fuel can",
                new Vector2(.68f, .72f), new Vector2(.9f, .8f));
            fuel.interactable = _supplies.FuelCanUsable;
            fuel.onClick.AddListener(() =>
            {
                if (_supplies.FuelCanNeedsConfirmation && _armedSupply != CampaignSession.FuelCanId)
                {
                    _armedSupply = CampaignSession.FuelCanId; // §10.3: confirm before wasting part of a can
                    ShowSupplies();
                    return;
                }
                _armedSupply = string.Empty;
                UseSupply(_session.UseFuelCan(acceptWaste: true));
            });

            SupplyLine(overlay, _supplies.RepairLine, .60f);
            float x = .1f;
            foreach (var row in _supplies.Subsystems)
            {
                var zone = row.Zone;
                var repair = PrototypeUi.Button(overlay, $"{row.Name} {row.Condition}", new Vector2(x, .50f), new Vector2(x + .19f, .58f));
                repair.interactable = row.CanRepair;
                repair.onClick.AddListener(() => UseSupply(_session.UseRepairKit(zone, vehicle.SpeedKph, vehicle.WaterDepthMeters > .05f)));
                x += .2f;
            }

            SupplyLine(overlay, _supplies.TirePatchLine, .38f);
            var patch = PrototypeUi.Button(overlay, "Patch tire", new Vector2(.68f, .38f), new Vector2(.9f, .46f));
            patch.interactable = _supplies.TirePatchUsable;
            patch.onClick.AddListener(() => UseSupply(_session.UseTirePatch(vehicle.SpeedKph)));

            PrototypeUi.Label(overlay, _supplyMessage, new Vector2(.1f, .30f), new Vector2(.9f, .37f), 16);
            PrototypeUi.Button(overlay, "Refresh", new Vector2(.1f, .18f), new Vector2(.35f, .28f)).onClick.AddListener(ShowSupplies);
            PrototypeUi.Button(overlay, "Back to driving", new Vector2(.65f, .18f), new Vector2(.9f, .28f)).onClick.AddListener(CloseOverlay);
        }

        static void SupplyLine(RectTransform overlay, string text, float y)
        {
            var label = PrototypeUi.Label(overlay, text, new Vector2(.1f, y), new Vector2(.66f, y + .08f), 18);
            label.alignment = TextAnchor.MiddleLeft;
        }

        void UseSupply(CommandResult result)
        {
            _supplyMessage = result.Success ? "Done." : (string.IsNullOrEmpty(result.Detail) ? result.Reason.ToString() : result.Detail);
            if (result.Success)
            {
                ApplySessionToVehicle(); // the session is authoritative: push fuel/conditions back to the rig
                foreach (var notice in _session.DrainNotices()) _supplyMessage = notice.Text;
            }
            ShowSupplies();
        }

        // ------------------------------------------------------------------ map (§13.2 minimap, §13.4 map screen)

        /// <summary>Opens the full map. During an expedition it freezes the simulation (AppState MapPaused, §9).</summary>
        void OpenMap()
        {
            if (_mapView != null || _paused || _mapPresenter == null) return;
            _mapPausedField = _session.Expedition.IsActive && _overlay == null;
            if (_mapPausedField)
            {
                _left = _right = _gas = _brake = _interactTouch = false;
                foreach (var hold in _root.GetComponentsInChildren<TouchHoldButton>()) hold.Release();
                input.SuppressInputs(true);
                vehicle.ZeroInputsAndHold();
                Time.timeScale = 0f;
            }
            _mapPresenter.RebuildMarkersNow();
            _mapView = MapOverlayView.Create(_root, _mapPresenter, _session, OnMapClosed, message => _toasts.Enqueue(message));
        }

        void OnMapClosed()
        {
            _mapView = null;
            _mapClosedFrame = Time.frameCount;
            if (!_mapPausedField) return;
            _mapPausedField = false;
            input.SuppressInputs(false);
            Time.timeScale = 1f;
        }

        public void SetPaused(bool paused)
        {
            if (!paused && _lifecycle != null && _lifecycle.IsSuspended) return; // explicit tap-to-resume only
            _paused = paused;
            _left = _right = _gas = _brake = _interactTouch = false;
            foreach (var hold in _root.GetComponentsInChildren<TouchHoldButton>()) hold.Release();
            input.SuppressInputs(paused);
            vehicle.ZeroInputsAndHold();
            Time.timeScale = paused ? 0f : 1f;
            _pauseButton.GetComponentInChildren<Text>().text = paused ? "Resume" : "Pause";
            if (paused)
            {
                var overlay = OpenOverlay("Paused");
                PrototypeUi.Label(overlay, "Paused — the simulation is frozen; autosave continues.", new Vector2(.1f, .6f), new Vector2(.9f, .8f));
                PrototypeUi.Button(overlay, "Resume", new Vector2(.35f, .42f), new Vector2(.65f, .54f)).onClick.AddListener(() => SetPaused(false));
                PrototypeUi.Button(overlay, "Save & quit to title", new Vector2(.35f, .26f), new Vector2(.65f, .38f)).onClick.AddListener(GoToTitle);
            }
            else CloseOverlay();
        }

        // ------------------------------------------------------------------ UI: overlays

        RectTransform OpenOverlay(string name)
        {
            CloseOverlay();
            _overlay = PrototypeUi.Rect(name, _root, Vector2.zero, Vector2.one);
            var background = _overlay.gameObject.AddComponent<Image>();
            background.color = new Color(.03f, .06f, .07f, .92f);
            return _overlay;
        }

        void CloseOverlay()
        {
            if (_overlay != null) Destroy(_overlay.gameObject);
            _overlay = null;
        }

        void ShowSummary(ExpeditionSummary summary)
        {
            vehicle.ZeroInputsAndHold();
            PlaceVehicle(_session.ResumePose);
            ApplySessionToVehicle();
            var overlay = OpenOverlay("Expedition Summary");
            var text = PrototypeUi.Label(overlay, SummaryText.Describe(summary, _session), new Vector2(.08f, .25f), new Vector2(.92f, .92f), 20);
            text.alignment = TextAnchor.UpperLeft;
            PrototypeUi.Button(overlay, "Continue to garage", new Vector2(.35f, .08f), new Vector2(.65f, .2f)).onClick.AddListener(ShowGarage);
        }

        void ShowGarage()
        {
            var overlay = OpenOverlay("Garage");
            var model = GarageModel.Build(_session, researchRowsShown);
            PrototypeUi.Label(overlay, $"Home Garage · {model.Wallet} Salvage · {model.CapacityLine}", new Vector2(.02f, .92f), new Vector2(.98f, .99f), 20);

            // Missions (left top)
            float y = .90f;
            foreach (var mission in model.Missions)
            {
                string objectives = string.Join("  ", mission.Objectives);
                var label = PrototypeUi.Label(overlay, $"{mission.Code} {mission.Name} — {mission.State}\n{objectives}",
                    new Vector2(.02f, y - .07f), new Vector2(.40f, y), 14);
                label.alignment = TextAnchor.UpperLeft;
                if (mission.CanAccept)
                {
                    string code = mission.Code;
                    PrototypeUi.Button(overlay, "Accept", new Vector2(.41f, y - .06f), new Vector2(.49f, y - .01f))
                        .onClick.AddListener(() => Act(_session.AcceptMission(code)));
                }
                y -= .075f;
            }

            // Research (left bottom) — one confirm, no drag purchase (§13.3). Buying a module fits it;
            // the button then becomes the §10.2 slot control. Fitting is free and reversible, so unlike
            // a purchase it needs no second tap (§10.4 "vehicle change").
            y = .58f;
            foreach (var row in model.Research)
            {
                bool owned = row.State == GarageModel.ResearchState.Researched;
                string slotText = row.Slot == ModuleSlot.None ? "unlock" : row.SlotName;
                var label = PrototypeUi.Label(overlay, $"T{row.Tier} {row.Name} · {slotText} · {row.Cost} · {row.Result}",
                    new Vector2(.02f, y - .06f), new Vector2(.40f, y), 14);
                label.alignment = TextAnchor.MiddleLeft;
                string id = row.UpgradeId;
                string caption = !owned
                    ? (row.State == GarageModel.ResearchState.NeedsMission ? "Needs " + row.RequiredMission
                        : row.State == GarageModel.ResearchState.NeedsPreviousTier ? "Locked"
                        : row.State == GarageModel.ResearchState.NeedsSalvage ? "Need Salvage"
                        : _armedResearchId == id ? $"Confirm {row.Cost}?" : "Research")
                    : row.Slot == ModuleSlot.None ? "Owned"
                    : row.IsFitted ? "Fitted ✓"
                    : row.CanFit ? "Fit" : "Slots full";
                var button = PrototypeUi.Button(overlay, caption, new Vector2(.41f, y - .055f), new Vector2(.49f, y - .005f));
                button.interactable = owned ? row.CanFit || row.CanUnfit : row.CanPurchase;
                if (owned)
                    button.onClick.AddListener(() => Act(row.IsFitted ? _session.UnequipModule(id) : _session.EquipModule(id)));
                else
                    button.onClick.AddListener(() =>
                    {
                        if (_armedResearchId != id) { _armedResearchId = id; ShowGarage(); return; }
                        _armedResearchId = string.Empty;
                        Act(_session.ResearchUpgrade(id));
                    });
                y -= .065f;
            }

            // Tire sets (§10.2): one set mounted at a time; unlocks come from research and survive
            // fitting anything else, so a locked row says which tier unlocks it.
            y -= .005f;
            PrototypeUi.Label(overlay, $"Tire set — mounted: {model.SelectedTireSet}",
                new Vector2(.02f, y - .04f), new Vector2(.40f, y), 14);
            y -= .045f;
            foreach (var tire in model.TireSets)
            {
                var set = (TireSet)Enum.Parse(typeof(TireSet), tire.TireSetId);
                var tireLabel = PrototypeUi.Label(overlay,
                    tire.Unlocked ? tire.Name : $"{tire.Name} — needs {tire.UnlockName}",
                    new Vector2(.02f, y - .045f), new Vector2(.30f, y), 14);
                tireLabel.alignment = TextAnchor.MiddleLeft;
                var tireButton = PrototypeUi.Button(overlay, tire.Mounted ? "Mounted ✓" : "Mount",
                    new Vector2(.31f, y - .04f), new Vector2(.40f, y - .005f));
                tireButton.interactable = tire.CanMount;
                tireButton.onClick.AddListener(() => Act(_session.SelectTireSet(set)));
                y -= .05f;
            }

            // Cargo and storage (right)
            y = .90f;
            PrototypeUi.Label(overlay, "Cargo (loaded)", new Vector2(.52f, y - .04f), new Vector2(.98f, y), 16);
            y -= .045f;
            foreach (var stack in model.Cargo)
            {
                PrototypeUi.Label(overlay, $"{stack.Name} ×{stack.Quantity} ({stack.MassKg:0.#} kg)", new Vector2(.52f, y - .045f), new Vector2(.74f, y), 14);
                string item = stack.ItemId;
                int qty = stack.Quantity;
                SellButton(overlay, stack, fromCargo: true, y);
                PrototypeUi.Button(overlay, "Unload", new Vector2(.85f, y - .045f), new Vector2(.98f, y - .005f))
                    .onClick.AddListener(() => Act(_session.UnloadCargo(item, qty)));
                y -= .05f;
            }
            y -= .02f;
            PrototypeUi.Label(overlay, "Garage storage", new Vector2(.52f, y - .04f), new Vector2(.98f, y), 16);
            y -= .045f;
            foreach (var stack in model.Storage)
            {
                PrototypeUi.Label(overlay, $"{stack.Name} ×{stack.Quantity}", new Vector2(.52f, y - .045f), new Vector2(.74f, y), 14);
                string item = stack.ItemId;
                int qty = stack.Quantity;
                SellButton(overlay, stack, fromCargo: false, y);
                PrototypeUi.Button(overlay, "Load", new Vector2(.85f, y - .045f), new Vector2(.98f, y - .005f))
                    .onClick.AddListener(() => Act(_session.LoadCargo(item, qty)));
                y -= .05f;
                if (y < .2f) break;
            }

            // Community logistics (§10.4): pick a partner hub, then send from the store you are
            // standing at or pull from theirs. Free and instant, and only while at a hub.
            y -= .02f;
            if (y > .22f)
            {
                GarageModel.LogisticsRow here = null;
                foreach (var h in model.Logistics) if (h.IsCurrent) { here = h; break; }

                PrototypeUi.Label(overlay, $"Community logistics — at {DisplayNames.Hub(model.CurrentHubId)}",
                    new Vector2(.52f, y - .04f), new Vector2(.98f, y), 16);
                y -= .045f;
                foreach (var hub in model.Logistics)
                {
                    if (y < .22f) break;
                    string hubId = hub.HubId;
                    bool armed = _armedLogisticsHub == hubId;
                    string detail = hub.SharedStore ? "shared, unlimited" : $"{hub.MassKg:0}/{hub.CapacityKg:0} kg";
                    var hubLabel = PrototypeUi.Label(overlay, (hub.IsCurrent ? "• " : "") + hub.Name + " — " + detail,
                        new Vector2(.52f, y - .045f), new Vector2(.74f, y), 14);
                    hubLabel.alignment = TextAnchor.MiddleLeft;
                    var choose = PrototypeUi.Button(overlay, armed ? "Partner ✓" : "Choose",
                        new Vector2(.75f, y - .045f), new Vector2(.84f, y - .005f));
                    choose.interactable = !hub.IsCurrent;
                    choose.onClick.AddListener(() =>
                    {
                        _armedLogisticsHub = armed ? string.Empty : hubId;
                        ShowGarage();
                    });
                    y -= .05f;
                    if (!armed || !hub.CanExchange || here == null) continue;

                    foreach (var mine in here.Stacks)
                    {
                        if (y < .22f) break;
                        string item = mine.ItemId;
                        int qty = mine.Quantity;
                        PrototypeUi.Label(overlay, $"send {mine.Name} ×{qty}", new Vector2(.56f, y - .04f), new Vector2(.78f, y), 13);
                        PrototypeUi.Button(overlay, "Send", new Vector2(.79f, y - .04f), new Vector2(.88f, y - .005f))
                            .onClick.AddListener(() => Act(_session.TransferToHub(hubId, item, qty)));
                        y -= .045f;
                    }
                    foreach (var theirs in hub.Stacks)
                    {
                        if (y < .22f) break;
                        string item = theirs.ItemId;
                        int qty = theirs.Quantity;
                        PrototypeUi.Label(overlay, $"pull {theirs.Name} ×{qty}", new Vector2(.56f, y - .04f), new Vector2(.78f, y), 13);
                        PrototypeUi.Button(overlay, "Pull", new Vector2(.79f, y - .04f), new Vector2(.88f, y - .005f))
                            .onClick.AddListener(() => Act(_session.TransferFromHub(hubId, item, qty)));
                        y -= .045f;
                    }
                }
            }

            PrototypeUi.Button(overlay, "Craft camp kit (80 + materials)", new Vector2(.52f, .10f), new Vector2(.74f, .18f))
                .onClick.AddListener(() => Act(_session.Craft("recipe.camp_kit")));
            PrototypeUi.Button(overlay, "Depart", new Vector2(.76f, .10f), new Vector2(.98f, .18f)).onClick.AddListener(OnDepart);
            PrototypeUi.Button(overlay, "Map (M)", new Vector2(.52f, .02f), new Vector2(.74f, .08f)).onClick.AddListener(OpenMap);
            PrototypeUi.Button(overlay, "Save & quit to title", new Vector2(.02f, .02f), new Vector2(.24f, .08f)).onClick.AddListener(GoToTitle);
        }

        /// <summary>§10.4 sell-back (40 %, rounded down): first tap arms, second tap sells one unit (critical save).</summary>
        void SellButton(RectTransform overlay, GarageModel.StackRow stack, bool fromCargo, float y)
        {
            string key = (fromCargo ? "cargo:" : "storage:") + stack.ItemId;
            string item = stack.ItemId;
            var sell = PrototypeUi.Button(overlay, !stack.Sellable ? "—" : _armedSellKey == key ? "Confirm?" : $"Sell +{stack.SellPriceEach}",
                new Vector2(.75f, y - .045f), new Vector2(.84f, y - .005f));
            sell.interactable = stack.Sellable;
            sell.onClick.AddListener(() =>
            {
                if (_armedSellKey != key)
                {
                    _armedSellKey = key;
                    ShowGarage();
                    return;
                }
                _armedSellKey = string.Empty;
                Act(_session.SellItems(item, 1, fromCargo));
            });
        }

        void Act(CommandResult result)
        {
            if (!result.Success) _toasts.Enqueue(string.IsNullOrEmpty(result.Detail) ? result.Reason.ToString() : result.Detail);
            ApplySessionToVehicle();
            ShowGarage();
        }

        void OnDepart()
        {
            var result = _session.Depart();
            if (!result.Success)
            {
                _toasts.Enqueue(result.Detail);
                return;
            }
            PlaceVehicle(_session.ResumePose);
            ApplySessionToVehicle();
            ShowDrive();
        }

        void GoToTitle()
        {
            Time.timeScale = 1f;
            if (_host != null) _host.CloseSession();
            SceneManager.LoadScene("Title");
        }

        void OnDestroy()
        {
            if (_lifecycle != null) _lifecycle.OnSuspendRequested -= OnSuspend;
            _mapPresenter?.Dispose();
            Time.timeScale = 1f;
        }
    }
}
