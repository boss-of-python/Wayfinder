using System;
using System.Collections.Generic;
using UnityEngine;
using Wayfinder.Core;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Accessibility settings (§3.5): 100–150% UI scale, high contrast, color-independent hazard
    /// symbols, subtitle size/background, separate audio sliders, hold/toggle options, left-handed
    /// layout, haptics toggle, reduced camera motion, unlimited pause. Tilt is never required.
    /// </summary>
    [Serializable]
    public sealed class AccessibilitySettings
    {
        public float uiScale = 1.0f;                 // 1.0 – 1.5
        public bool highContrast;
        public bool colorIndependentHazardSymbols = true;
        public bool subtitles = true;
        public float subtitleScale = 1.0f;
        public bool subtitleBackground = true;
        public float masterVolume = 1f, musicVolume = 0.8f, sfxVolume = 1f;
        public bool holdToInteract = true;           // false → tap alternative
        public bool leftHandedLayout;
        public bool hapticsEnabled = true;
        public bool reducedCameraMotion;
        public bool reducedFx;

        public void Validate()
        {
            uiScale = MathUtil.Clamp(uiScale, 1.0f, 1.5f);
            subtitleScale = MathUtil.Clamp(subtitleScale, 1.0f, 1.5f);
            masterVolume = MathUtil.Clamp01(masterVolume);
            musicVolume = MathUtil.Clamp01(musicVolume);
            sfxVolume = MathUtil.Clamp01(sfxVolume);
        }
    }

    /// <summary>
    /// HUD view model (§13.2). Warning priority: imminent rollover/deep water → fuel insufficient
    /// → subsystem damage → cargo full → objective update → cosmetic. Minor notifications queue;
    /// never overlapping audio/text. Nonessential info hides after inactivity; controls never hide.
    /// </summary>
    public sealed class HudViewModel
    {
        public float FuelFraction;
        public float FuelLiters;
        public float FuelCapacityLiters;
        public bool ShowConditionWarning;
        public string ConditionWarningKey = "";
        public float SpeedKph;
        public string GearLabel = "D";
        public string ObjectiveLineKey = "";
        public string InteractionPromptKey = "";
        public bool InteractionAvailable;
        public float PayloadFraction;
        public bool PayloadWarning;
        public string FuelRangeHint = "ui.fuel_range.estimate"; // always labelled "estimate" (§5.1)

        readonly Queue<string> _notificationQueue = new Queue<string>();
        public string CurrentNotification { get; private set; } = "";
        float _notificationTimer;

        public void EnqueueNotification(string localizationKey)
        {
            if (string.IsNullOrEmpty(localizationKey)) return;
            _notificationQueue.Enqueue(localizationKey);
        }

        public void Tick(float deltaSeconds)
        {
            _notificationTimer -= deltaSeconds;
            if (_notificationTimer <= 0f)
            {
                CurrentNotification = _notificationQueue.Count > 0 ? _notificationQueue.Dequeue() : "";
                _notificationTimer = string.IsNullOrEmpty(CurrentNotification) ? 0f : 3.5f;
            }
        }

        /// <summary>Warning priority ordering (§13.2). Lower value = higher priority.</summary>
        public static int WarningPriority(GameEvent.WarningKind kind) => kind switch
        {
            GameEvent.WarningKind.RolloverRisk => 0,
            GameEvent.WarningKind.DeepWaterAhead => 1,
            GameEvent.WarningKind.IntakeLimitNear => 2,
            GameEvent.WarningKind.EngineStalled => 3,
            GameEvent.WarningKind.FuelReturnRangeInsufficient => 4,
            GameEvent.WarningKind.FuelFifteenPercent => 5,
            GameEvent.WarningKind.FuelThirtyPercent => 6,
            GameEvent.WarningKind.SubsystemDamaged => 7,
            GameEvent.WarningKind.CargoFull => 8,
            GameEvent.WarningKind.CargoNearFull => 9,
            GameEvent.WarningKind.ObjectiveUpdated => 10,
            _ => 11
        };
    }

    /// <summary>
    /// Screen hierarchy (§13.1): Title → Save Slots → Garage/Continue Expedition.
    /// Garage tabs: Overview | Vehicle | Research | Cargo/Craft | Map/Contracts | Journal | Cosmetics.
    /// All modal actions support cancel/back (Android back, controller back, visible touch).
    /// </summary>
    public sealed class ScreenFlowController
    {
        public enum Screen
        {
            Title, SaveSlots, GarageOverview, GarageVehicle, GarageResearch, GarageCargoCraft,
            GarageMapContracts, GarageJournal, GarageCosmetics,
            DrivingHud, MapOverlay, JournalOverlay, PauseMenu, RecoveryMenu, CampMenu,
            ExpeditionSummary, Settings, Credits, TapToResume
        }

        readonly Stack<Screen> _stack = new Stack<Screen>();
        readonly PauseTokenRegistry _pauseTokens;

        public ScreenFlowController(PauseTokenRegistry pauseTokens) =>
            _pauseTokens = pauseTokens ?? throw new ArgumentNullException(nameof(pauseTokens));

        public Screen Current => _stack.Count > 0 ? _stack.Peek() : Screen.Title;

        IDisposable _currentPauseToken;

        /// <summary>Opening a modal overlay acquires a pause token; closing releases only its own (§2.5).</summary>
        public void PushModal(Screen screen, bool pauseSimulation)
        {
            _stack.Push(screen);
            if (pauseSimulation)
                _currentPauseToken = _pauseTokens.Acquire();
        }

        /// <summary>Android system back / controller back / visible cancel all route here.</summary>
        public bool TryCancel()
        {
            if (_stack.Count <= 1) return false; // root: no-op (title); system back must not exit app mid-game silently
            _stack.Pop();
            _currentPauseToken?.Dispose();
            _currentPauseToken = null;
            return true;
        }

        public void Replace(Screen screen)
        {
            _stack.Clear();
            _currentPauseToken?.Dispose();
            _currentPauseToken = null;
            _stack.Push(screen);
        }
    }

    /// <summary>
    /// Credits + finale presentation flow (§2.3, FINISH-01). Story completion → atlas review →
    /// credits → free exploration with all remaining objectives available. "Game over" is not a state.
    /// </summary>
    public sealed class CreditsController
    {
        public bool CreditsShown;
        public event Action OnAtlasReviewRequested;
        public event Action OnCreditsFinished;

        public void BeginFinaleSequence()
        {
            OnAtlasReviewRequested?.Invoke();
            CreditsShown = true;
        }

        public void FinishCredits() => OnCreditsFinished?.Invoke();
    }

    /// <summary>Garage research screen model (§13.3): exact change per tier, one confirm, no drag purchase.</summary>
    public sealed class ResearchScreenModel
    {
        public struct Row
        {
            public int Tier;
            public string NameKey;
            public int CostSalvage;
            public string RequiredMission;
            public string ResultDescriptionKey;
            public bool Researched;
            public bool Affordable;
            public bool Locked;
        }

        public readonly List<Row> Rows = new List<Row>(20);

        public void Rebuild(Progression.ResearchService research, Progression.EconomyService economy,
            Func<string, bool> missionCompleted)
        {
            Rows.Clear();
            foreach (var tier in research.Tiers)
            {
                Rows.Add(new Row
                {
                    Tier = tier.Tier,
                    NameKey = tier.NameKey,
                    CostSalvage = tier.CostSalvage,
                    RequiredMission = tier.RequiredMissionCode,
                    ResultDescriptionKey = tier.ResultDescriptionKey,
                    Researched = research.IsResearched(tier.Id.Value),
                    Affordable = economy.BankedSalvage >= tier.CostSalvage,
                    Locked = !string.IsNullOrEmpty(tier.RequiredMissionCode) &&
                             !(missionCompleted?.Invoke(tier.RequiredMissionCode) ?? false)
                });
            }
        }
    }
}
