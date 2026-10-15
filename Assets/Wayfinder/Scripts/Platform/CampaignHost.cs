using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Wayfinder.Campaign;
using Wayfinder.Core;
using Wayfinder.Persistence;

namespace Wayfinder.Platform
{
    /// <summary>
    /// Long-lived campaign owner (DontDestroyOnLoad, §15.3 composition root): loads the campaign
    /// content TextAssets that Setup Project copies into Resources/Content, owns the SaveService
    /// rooted in Application.persistentDataPath and the active <see cref="CampaignSession"/>,
    /// drives the autosave debounce with unscaled time and saves on Android pause/focus loss (§16.2).
    /// All gameplay rules live in the engine-free Wayfinder.Campaign assembly (headless-tested).
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class CampaignHost : MonoBehaviour
    {
        public const string ResourcesFolder = "Content";
        public const string SaveFolderName = "saves";

        static CampaignHost _instance;
        LifecycleHandler _lifecycle;

        public SaveService Saves { get; private set; }
        public CampaignContent Content { get; private set; }
        public CampaignSession Session { get; private set; }
        public string ContentError { get; private set; } = string.Empty;
        public bool Ready => Content != null && Saves != null;

        /// <summary>Lazily created singleton host; survives scene loads.</summary>
        public static CampaignHost Instance
        {
            get
            {
                if (_instance == null)
                {
                    var go = new GameObject("Campaign Host");
                    _instance = go.AddComponent<CampaignHost>();
                }
                return _instance;
            }
        }

        /// <summary>PlayMode tests: host whose saves live under <paramref name="saveRoot"/>.</summary>
        public static CampaignHost CreateWithSaveRoot(string saveRoot)
        {
            var host = Instance;
            host.Saves = new SaveService(saveRoot);
            host.Session = null;
            return host;
        }

        void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
            Saves = new SaveService(Path.Combine(Application.persistentDataPath, SaveFolderName));
            LoadContent();
            _lifecycle = gameObject.AddComponent<LifecycleHandler>();
            _lifecycle.OnSuspendRequested += SaveOnSuspend;
        }

        void LoadContent()
        {
            try
            {
                var files = new Dictionary<string, string>(StringComparer.Ordinal);
                foreach (var name in CampaignContent.RequiredFiles)
                {
                    var asset = Resources.Load<TextAsset>(ResourcesFolder + "/" + Path.GetFileNameWithoutExtension(name));
                    if (asset == null)
                        throw new InvalidDataException($"Resources/{ResourcesFolder}/{name} is missing — run Tools → Wayfinder → Setup Project.");
                    files[name] = asset.text;
                }
                Content = CampaignContent.FromJson(files);
                ContentError = string.Empty;
            }
            catch (Exception ex)
            {
                // A broken or missing catalog is an unexpected fault: surface it on the title screen.
                Content = null;
                ContentError = ex.Message;
                Debug.LogError("[Wayfinder] Campaign content failed to load: " + ex.Message);
            }
        }

        public SlotSummary Peek(int slot) => CampaignSession.PeekSlot(Saves, new SaveSlotId(slot));

        /// <summary>Starts a new campaign (UI must confirm before replacing an occupied slot, §17.5).</summary>
        public CampaignOpenResult StartNew(int slot, GameMode mode = GameMode.Expedition)
        {
            if (!Ready) return new CampaignOpenResult(null, CampaignOpenStatus.Failed, ContentError);
            CloseSession();
            var result = CampaignSession.StartNew(Saves, Content, new SaveSlotId(slot), mode);
            Session = result.Playable ? result.Session : null;
            return result;
        }

        public CampaignOpenResult Continue(int slot)
        {
            if (!Ready) return new CampaignOpenResult(null, CampaignOpenStatus.Failed, ContentError);
            CloseSession();
            var result = CampaignSession.Continue(Saves, Content, new SaveSlotId(slot));
            Session = result.Playable ? result.Session : null;
            return result;
        }

        /// <summary>Deletes a slot (UI confirms first). Closes the session if it belonged to that slot.</summary>
        public void DeleteSlot(int slot)
        {
            if (Session != null && Session.Slot.Index == slot) Session = null;
            Saves.DeleteSlot(new SaveSlotId(slot));
        }

        /// <summary>Saves anything pending and drops the session (back to title).</summary>
        public void CloseSession()
        {
            if (Session == null) return;
            Session.OnSuspend();
            Session = null;
        }

        void Update()
        {
            Session?.Tick(Time.unscaledDeltaTime);
        }

        void SaveOnSuspend() => Session?.OnSuspend();

        void OnApplicationQuit() => Session?.OnSuspend();

        void OnDestroy()
        {
            if (_lifecycle != null) _lifecycle.OnSuspendRequested -= SaveOnSuspend;
            if (_instance == this) _instance = null;
        }
    }
}
