using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Wayfinder.Campaign;
using Wayfinder.Platform;

namespace Wayfinder.Presentation
{
    /// <summary>
    /// Title → Save Slots (§13.1). Three slots with New / Continue / Delete; replacing or deleting
    /// an occupied slot needs a second confirming tap (§17.5). New games are saved before the
    /// slice scene loads. The proving ground remains available as a separate test rig.
    /// Status: implemented, uncompiled/unverified in Unity (syntax-checked only).
    /// </summary>
    public sealed class TitleFlowHost : MonoBehaviour
    {
        public const string SliceScene = "VerticalSlice";
        public const string ProvingGroundScene = "PhysicsTestGround";

        CampaignHost _host;
        RectTransform _root;
        RectTransform _slotPanel;
        Text _status;
        int _pendingConfirmSlot = -1;
        string _pendingConfirmAction = string.Empty;

        void Start()
        {
            Time.timeScale = 1f;
            _host = CampaignHost.Instance;
            _root = PrototypeUi.CreateRoot("Title UI");
            PrototypeUi.Label(_root, "WAYFINDER\nThe Quiet Frontier", new Vector2(.1f, .78f), new Vector2(.9f, .97f), 40);
            _status = PrototypeUi.Label(_root, "", new Vector2(.05f, .70f), new Vector2(.95f, .78f), 18);
            PrototypeUi.Button(_root, "Open proving ground", new Vector2(.30f, .10f), new Vector2(.70f, .19f))
                .onClick.AddListener(() => SceneManager.LoadScene(ProvingGroundScene));
            PrototypeUi.Label(_root, "Offline • No ads • No purchases • Greybox vertical slice", new Vector2(.1f, .01f),
                new Vector2(.9f, .09f), 16);
            if (!_host.Ready)
            {
                _status.text = "Campaign content unavailable: " + _host.ContentError;
                return;
            }
            RebuildSlots();
        }

        void RebuildSlots()
        {
            if (_slotPanel != null) Destroy(_slotPanel.gameObject);
            _slotPanel = PrototypeUi.Rect("Save Slots", _root, new Vector2(.05f, .21f), new Vector2(.95f, .69f));
            for (int slot = 0; slot < 3; slot++)
            {
                float top = 1f - slot / 3f, bottom = top - 1f / 3f + .02f;
                var row = PrototypeUi.Rect("Slot " + slot, _slotPanel, new Vector2(0f, bottom), new Vector2(1f, top));
                var card = _host.Peek(slot);
                string text = card.HasSave
                    ? $"Slot {slot + 1} · {card.BankedSalvage} Salvage · {card.CompletedMissions} missions · {card.LandmarksDiscovered} landmarks" +
                      (card.ExpeditionActive ? " · expedition in progress" : "")
                    : $"Slot {slot + 1} · empty";
                PrototypeUi.Label(row, text, new Vector2(0f, 0f), new Vector2(.52f, 1f), 18);
                int captured = slot;
                bool confirmNew = _pendingConfirmSlot == slot && _pendingConfirmAction == "new";
                bool confirmDelete = _pendingConfirmSlot == slot && _pendingConfirmAction == "delete";
                PrototypeUi.Button(row, confirmNew ? "Replace save?" : "New", new Vector2(.53f, .1f), new Vector2(.68f, .9f))
                    .onClick.AddListener(() => OnNew(captured, card.HasSave));
                var cont = PrototypeUi.Button(row, "Continue", new Vector2(.69f, .1f), new Vector2(.84f, .9f));
                cont.interactable = card.HasSave;
                cont.onClick.AddListener(() => OnContinue(captured));
                var del = PrototypeUi.Button(row, confirmDelete ? "Delete?" : "Delete", new Vector2(.85f, .1f), new Vector2(1f, .9f));
                del.interactable = card.HasSave;
                del.onClick.AddListener(() => OnDelete(captured));
            }
        }

        void OnNew(int slot, bool occupied)
        {
            if (occupied && !(_pendingConfirmSlot == slot && _pendingConfirmAction == "new"))
            {
                Arm(slot, "new", $"Slot {slot + 1} has a save. Tap \"Replace save?\" to overwrite it.");
                return;
            }
            Disarm();
            var result = _host.StartNew(slot);
            if (result.Playable) SceneManager.LoadScene(SliceScene);
            else _status.text = "Could not create the save: " + result.Detail + " — nothing was overwritten in memory; try again.";
        }

        void OnContinue(int slot)
        {
            Disarm();
            var result = _host.Continue(slot);
            if (!result.Playable)
            {
                _status.text = result.Status == CampaignOpenStatus.SchemaTooNew
                    ? "This save was made by a newer version; it is kept read-only."
                    : "Could not load: " + result.Detail;
                RebuildSlots();
                return;
            }
            if (result.Status == CampaignOpenStatus.LoadedBackup)
                Debug.LogWarning("[Wayfinder] Loaded backup save: " + result.Detail);
            SceneManager.LoadScene(SliceScene);
        }

        void OnDelete(int slot)
        {
            if (!(_pendingConfirmSlot == slot && _pendingConfirmAction == "delete"))
            {
                Arm(slot, "delete", $"Tap \"Delete?\" again to permanently delete slot {slot + 1}.");
                return;
            }
            Disarm();
            _host.DeleteSlot(slot);
            _status.text = $"Slot {slot + 1} deleted.";
            RebuildSlots();
        }

        void Arm(int slot, string action, string message)
        {
            _pendingConfirmSlot = slot;
            _pendingConfirmAction = action;
            _status.text = message;
            RebuildSlots();
        }

        void Disarm()
        {
            _pendingConfirmSlot = -1;
            _pendingConfirmAction = string.Empty;
        }
    }
}
