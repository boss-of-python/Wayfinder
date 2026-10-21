#!/usr/bin/env python3
"""Mutation check for Batch J (KI-028): safe-hub travel (§10.4) + per-camp storage (§12.2, schema 2).

Each mutant breaks ONE rule of the batch in the production source, recompiles with
`Tools/Build/headless_cs.py compile`, runs the three affected fixtures, and must be caught by at
least one failing test (or fail to compile). Sources are restored from a backup after every mutant,
and the script refuses to start on a dirty tree and verifies it left the tree clean.

Usage:  python3 Docs/evidence/hub-travel-camp-storage-2026-10-21/mutation_check.py
Exit:   0 when every mutant is caught, 1 otherwise.
"""
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", "..", ".."))
CAMPAIGN = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Campaign")
GARAGE = os.path.join(CAMPAIGN, "CampaignSession.Garage.cs")
FIELD = os.path.join(CAMPAIGN, "CampaignSession.Field.cs")
SESSION = os.path.join(CAMPAIGN, "CampaignSession.cs")
SUPPLIES = os.path.join(CAMPAIGN, "CampaignSession.Supplies.cs")
SNAPSHOT = os.path.join(CAMPAIGN, "CampaignSnapshot.cs")
SAVESERVICE = os.path.join(ROOT, "Assets/Wayfinder/Scripts/Persistence/SaveService.cs")
RESULT_XML = os.path.join(ROOT, "Logs/headless/EditMode.headless.xml")
FIXTURES = ["--test=Wayfinder.Tests.CampaignHubTravelTests",
            "--test=Wayfinder.Tests.CampaignCampStorageTests",
            "--test=Wayfinder.Tests.PersistenceTests"]

# (id, what rule it breaks, file, exact source text, replacement)
MUTANTS = [
    # ------------------------------------------------------- hub travel (§10.4, D-039)
    ("A1", "content service gate dropped", GARAGE,
     "if (!Content.OffersHubTravel(origin) || !Content.OffersHubTravel(destinationHubId))", "if (false)"),
    ("A2", "water / deep-mud refusal dropped", GARAGE, "if (inWater || inDeepMud)", "if (false)"),
    ("A3", "extraction target re-based mid-expedition", GARAGE,
     "if (!onExpedition) Expedition.LastGarageId = destinationHubId;", "Expedition.LastGarageId = destinationHubId;"),
    ("A4", "garage-only service gates revert to InGarage (Garage.cs)", GARAGE, "!AtGarage", "!InGarage"),
    ("A5", "selling gate reverts to InGarage (Supplies.cs)", SUPPLIES, "!AtGarage", "!InGarage"),
    ("A6", "camp activation ignored when locating the hub", GARAGE,
     "if (!Garage.IsCampActivated(socket.Id)) continue;", ""),
    ("A7", "camp socket radius ignored", GARAGE,
     "if (distance <= socket.RadiusMeters && distance < bestDistance)",
     "if (distance <= socket.RadiusMeters * 1000f && distance < bestDistance)"),
    ("A8", "HubTravelled notice dropped", GARAGE,
     'Notice(CampaignNoticeKind.HubTravelled, $"Arrived at {destinationHubId}", 0, destinationHubId);', ""),
    ("A9", "safe pose not re-based on arrival", GARAGE, "Expedition.LastSafePose = destination;", ""),
    ("A10", "progression activation gate dropped", GARAGE,
     "var gate = Garage.TryHubTravel(origin, destinationHubId);", "var gate = CommandResult.Ok();"),
    ("A11", "travel allowed from the open field (origin falls back to LastGarageId)", GARAGE,
     "string origin = CurrentHubId;",
     "string origin = string.IsNullOrEmpty(CurrentHubId) ? Expedition.LastGarageId : CurrentHubId;"),
    ("A12", "'you are not at a hub' refusal dropped", GARAGE,
     '''            if (string.IsNullOrEmpty(origin))
                return CommandResult.Fail(FailureReason.WrongState,
                    "hub travel is only available at an activated garage or camp");''', ""),
    ("A13", "non-hub refusal reports InvalidTarget instead of WrongState", GARAGE,
     '''                return CommandResult.Fail(FailureReason.WrongState,
                    "hub travel is only available at an activated garage or camp");''',
     '''                return CommandResult.Fail(FailureReason.InvalidTarget,
                    "hub travel is only available at an activated garage or camp");'''),
    ("A14", "SpawnFor ignores camp sockets", SESSION,
     "var socket = Content.Slice.FindSite(Content.Slice.CampSockets, hubId);\n"
     "            if (socket != null) return new Pose(socket.Position, 0f);", ""),
    ("A15", "hub travel committed without the verified save", GARAGE,
     "return RunCritical(() =>\n            {\n                Expedition.Pose = destination;",
     "Expedition.Pose = destination;\n            return RunCritical(() =>\n            {\n                "),

    # ------------------------------------------------------- per-camp storage (§12.2, D-040)
    ("B1", "per-camp store id collapses back to the pooled v1 id", SESSION,
     'new InventoryId(CampStoragePrefix + (campId ?? string.Empty));', 'new InventoryId("campStorage");'),
    ("B2", "store capacity ignores content and uses the shared constant", SESSION,
     '''            Content.Camps.TryGetValue(campId ?? string.Empty, out var camp) && camp.StorageCapacityKg > 0f
                ? camp.StorageCapacityKg
                : CampService.CampStorageCapacityKg;''',
     "            CampService.CampStorageCapacityKg;"),
    ("B3", "EstablishCamp does not configure the new camp's store", FIELD,
     "                    ConfigureCampStore(campId);   // the new camp's own store exists immediately (§12.2)\n", ""),
    ("B4", "deposit targets the pooled store", FIELD,
     "new BankCargo(NextTransactionId(), new ContentId(campId), Cargo, CampStorageFor(campId)));",
     'new BankCargo(NextTransactionId(), new ContentId(campId), Cargo, new InventoryId("campStorage")));'),
    ("B5", "migration always falls back to garage storage", SAVESERVICE,
     "string campId = FirstActivatedCamp(p.activatedCampIds);", "string campId = null;"),
    ("B6", "migration appends instead of merging stacks", SAVESERVICE,
     "                if (match != null) match.quantity += incoming.quantity;\n                else merged.Add(incoming);",
     "                merged.Add(incoming);"),
    ("B7", "migration leaves the pooled rows in place (double-apply)", SAVESERVICE,
     "p.campStorage = new List<ItemStackDto>();", "p.campStorage = p.campStorage ?? new List<ItemStackDto>();"),
    ("B8", "migration keeps rows that carry nothing", SAVESERVICE,
     "if (d == null || string.IsNullOrEmpty(d.itemId) || d.quantity <= 0) continue;", "if (d == null) continue;"),
    ("B9", "migration does not recompute the payload checksum", SAVESERVICE,
     "envelope.payloadChecksum = CanonicalJson.Sha256Hex(CanonicalJson.Serialize(envelope.payload));", ""),
    ("B10", "restore ignores the per-camp stores", SNAPSHOT,
     '''            foreach (var store in p.campStores ?? new List<CampStoreDto>())
            {
                if (store == null || string.IsNullOrEmpty(store.campId)) continue;
                var container = CampaignSession.CampStorageFor(store.campId);
                s.ConfigureCampStore(store.campId);
                s.Inventory.Restore(container, FromDtos(store.items), s.Inventory.CapacityOf(container));
            }''', ""),
    ("B11", "restore drops pooled rows instead of adopting them", SNAPSHOT,
     "            AdoptLegacyCampStorage(s, p);", ""),
    ("B12", "capture mirrors camp stores back into the pooled field", SNAPSHOT,
     "                campStorage = new List<ItemStackDto>(),   // v1 pooled store, superseded in schema 2 (D-039)",
     "                campStorage = LegacyMirror(s),"),
]

# B12 also needs the mirroring helper to exist.
EXTRA_SETUP = {"B12": (SNAPSHOT, "        /// <summary>One store per activated camp",
                       "        static List<ItemStackDto> LegacyMirror(CampaignSession s)\n"
                       "        {\n"
                       "            var list = new List<ItemStackDto>();\n"
                       "            foreach (var campId in s.ActivatedCampsInOrder())\n"
                       "                list.AddRange(Stacks(s, CampaignSession.CampStorageFor(campId)));\n"
                       "            return list;\n"
                       "        }\n\n"
                       "        /// <summary>One store per activated camp")}


def run(cmd):
    return subprocess.run(cmd, cwd=ROOT, capture_output=True, text=True)


def dirty():
    out = run(["git", "status", "--porcelain", "Assets"]).stdout.strip()
    return out


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
        backups = [(path, original)]
        try:
            open(path, "w", encoding="utf-8").write(original.replace(old, new, 1))
            if mid in EXTRA_SETUP:
                p2, o2, n2 = EXTRA_SETUP[mid]
                src2 = open(p2, encoding="utf-8").read()
                if p2 != path:
                    backups.append((p2, src2))
                if o2 not in src2:
                    print(f"PATTERN-MISSING {mid} (extra setup)")
                    survived.append(mid)
                    continue
                open(p2, "w", encoding="utf-8").write(src2.replace(o2, n2, 1))

            compiled = run(["python3", "Tools/Build/headless_cs.py", "compile"])
            compile_ok = compiled.returncode == 0 and ": FAILED" not in compiled.stdout
            failed, total = [], None
            if compile_ok:
                run(["python3", "Tools/Build/headless_cs.py", "test", "--"] + FIXTURES)
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
            for target, text in backups:
                open(target, "w", encoding="utf-8").write(text)

    left = dirty()
    print(f"\n{len(caught)}/{len(MUTANTS)} mutants caught; survivors: {survived or 'none'}")
    print("tree clean after run" if not left else "TREE LEFT DIRTY:\n" + left)
    return 0 if not survived and not left else 1


if __name__ == "__main__":
    sys.exit(main())
