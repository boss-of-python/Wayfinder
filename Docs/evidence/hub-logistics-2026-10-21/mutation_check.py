#!/usr/bin/env python3
"""Mutation check for Batch L (KI-028 §10.4): hub-to-hub community logistics transfer.

Each mutant breaks ONE rule of the batch in the production source, recompiles with
`Tools/Build/headless_cs.py compile`, runs the whole EditMode suite, and must be caught by at least
one failing test (or fail to compile). Sources are restored from a backup after every mutant, and the
script refuses to start on a dirty Assets/ tree and verifies it left the tree clean.

Not mutation-covered, deliberately: the two defensive `ConfigureCampStore` calls inside the transfer.
`ConfigureContainers()` already sizes every activated camp's store on load and on establishment, so
removing them changes no observable behaviour. They are belt-and-braces, and are recorded as such in
D-042 rather than claimed as tested.

Usage:  python3 Docs/evidence/hub-logistics-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CAMPAIGN = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign")
GARAGE = os.path.join(CAMPAIGN, "CampaignSession.Garage.cs")
SESSION = os.path.join(CAMPAIGN, "CampaignSession.cs")
VMS = os.path.join(CAMPAIGN, "CampaignViewModels.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")

# (id, what rule it breaks, file, exact source text, replacement)
MUTANTS = [
    # ------------------------------------------------------- §10.4 "no field access"
    ("L1", "transfers are allowed away from a hub (field access)", GARAGE,
     '''            var atHub = Garage.TryLogisticsTransfer(here);
            if (!atHub.Success) return atHub;''',
     "            var atHub = Garage.TryLogisticsTransfer(here);"),

    # ------------------------------------------------------- what may move
    ("L2", "protected ledger objects can be transferred", GARAGE,
     '''            if (def.ProtectedLedger)
                return CommandResult.Fail(FailureReason.ItemNotUsableHere, "protected objects never move (§10.4)");
''', ""),
    ("L3", "a non-positive quantity is accepted", GARAGE,
     '''            if (quantity <= 0)
                return CommandResult.Fail(FailureReason.InvalidTarget, "nothing to transfer");
''', ""),

    # ------------------------------------------------------- which hubs may exchange
    ("L4", "transferring to the hub you are already at is allowed", GARAGE,
     '''            if (string.Equals(other, here, StringComparison.Ordinal))
                return CommandResult.Fail(FailureReason.WrongState, "already at that hub");
''', ""),
    ("L5", "an unactivated hub can send or receive", GARAGE,
     '''            if (from == null || to == null)
                return CommandResult.Fail(FailureReason.InvalidTarget, "that hub is not activated (§10.4)");
''', ""),
    ("L6", "garage-to-garage 'transfer' is accepted", GARAGE,
     '''            if (from.Value.Equals(to.Value))
                return CommandResult.Fail(FailureReason.WrongState,
                    "every garage shares one unlimited store (§10.4), so there is nothing to move between two garages");
''', ""),

    # ------------------------------------------------------- store resolution (D-039)
    ("L7", "a camp's own store is replaced by the shared garage store", GARAGE,
     "            if (Garage.IsCampActivated(hubId)) return CampStorageFor(hubId);",
     "            if (Garage.IsCampActivated(hubId)) return GarageStorage;"),
    ("L8", "an unlocked garage addresses no store", GARAGE,
     "            if (Garage.IsGarageUnlocked(hubId)) return GarageStorage;",
     "            if (Garage.IsGarageUnlocked(hubId)) return null;"),
    ("L9", "pulling from another hub sends to it instead", GARAGE,
     '''        public CommandResult TransferFromHub(string sourceHubId, string itemId, int quantity) =>
            TransferBetweenHubs(sourceHubId, CurrentHubId, itemId, quantity);''',
     '''        public CommandResult TransferFromHub(string sourceHubId, string itemId, int quantity) =>
            TransferBetweenHubs(CurrentHubId, sourceHubId, itemId, quantity);'''),

    # ------------------------------------------------------- deterministic hub list
    ("L10", "the hub list puts camps before garages", GARAGE,
     '''            hubs.AddRange(camps);
            return hubs;''',
     '''            camps.AddRange(hubs);
            return camps;'''),
    ("L11", "camps are missing from the hub list", GARAGE,
     "            hubs.AddRange(camps);\n", ""),

    # ------------------------------------------------------- §13.2 notice
    ("L12", "the transfer notice is dropped", GARAGE,
     '''                if (result.Success)
                    Notice(CampaignNoticeKind.LogisticsTransferred,
                        $"Community logistics: {quantity} × {itemId} to {toHubId}", quantity, toHubId);
''', ""),
    ("L13", "the notice names the hub property left, not the one that received it", GARAGE,
     '''                        $"Community logistics: {quantity} × {itemId} to {toHubId}", quantity, toHubId);''',
     '''                        $"Community logistics: {quantity} × {itemId} to {toHubId}", quantity, fromHubId);'''),

    # ------------------------------------------------------- critical: verified save + rollback
    ("L14", "a failed verified save no longer rolls the transfer back (§17.4)", SESSION,
     '''                if (!save.Success)
                {
                    CampaignSnapshot.Apply(this, before);
                    return CommandResult.Fail(FailureReason.SaveIoFailure, save.Detail);
                }''',
     '''                if (!save.Success)
                {
                    return CommandResult.Fail(FailureReason.SaveIoFailure, save.Detail);
                }'''),

    # ------------------------------------------------------- garage panel (§10.4 "show capacity")
    ("L15", "every hub row claims it can exchange", VMS,
     "                row.CanExchange = !row.IsCurrent && !(shared && standingAtGarage);",
     "                row.CanExchange = true;"),
    ("L16", "the panel never shows a store's capacity", VMS,
     "                    CapacityKg = s.Inventory.CapacityOf(store.Value).massKg",
     "                    CapacityKg = 0f"),
    ("L17", "the shared garage store is presented as a per-hub store", VMS,
     "                    SharedStore = shared,",
     "                    SharedStore = false,"),
    ("L18", "a hub's stored stacks are not listed", VMS,
     '''                AddStacks(s, store.Value, row.Stacks);   // §10.4: "Camp storage holds 500 kg; show capacity"
''', ""),
]


def run(cmd):
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)


def dirty():
    return run(["git", "status", "--porcelain", "Assets"]).stdout.strip()


def main():
    if dirty():
        print("refusing to run: Assets/ is dirty\n" + dirty())
        return 2
    survived, caught = [], []
    for mid, what, path, old, new in MUTANTS:
        original = open(path, encoding="utf-8").read()
        if old not in original:
            print(f"PATTERN-MISSING {mid} {what}")
            survived.append(mid)
            continue
        try:
            open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
            compiled = run(["python3", "Tools/Build/headless_cs.py", "compile"])
            compile_ok = compiled.returncode == 0 and ": FAILED" not in compiled.stdout
            failed, total = [], None
            if compile_ok:
                run(["python3", "Tools/Build/headless_cs.py", "test"])
                root = ET.parse(RESULT_XML).getroot()
                total = f"{root.get('passed')}/{root.get('total')}"
                failed = [tc.get("name") for tc in root.iter("test-case") if tc.get("result") != "Passed"]
            if compile_ok and not failed:
                survived.append(mid)
                print(f"SURVIVED  {mid:4} {what}  (tests {total})")
            else:
                caught.append(mid)
                reason = "does not compile" if not compile_ok else ", ".join(failed[:3])
                print(f"CAUGHT    {mid:4} {what}  (tests {total}) by {reason}")
        finally:
            open(path, "w", encoding="utf-8").write(original)

    left = dirty()
    print(f"\n{len(caught)}/{len(MUTANTS)} mutants caught; survivors: {survived or 'none'}")
    print("tree clean after run" if not left else "TREE LEFT DIRTY:\n" + left)
    return 0 if not survived and not left else 1


if __name__ == "__main__":
    sys.exit(main())
