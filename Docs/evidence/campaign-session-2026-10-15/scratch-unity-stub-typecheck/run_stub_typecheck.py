#!/usr/bin/env python3
"""SCRATCH check — NOT a Unity compile. Type-checks the new campaign glue (CampaignHost,
TitleFlowHost, VerticalSliceDriver + the real LifecycleHandler) against the REAL compiled
Wayfinder.Campaign/Core/Simulation/World/Progression/Persistence assemblies and the hand-written
UnityStubs.cs next to this file. Purpose: catch misnamed campaign members, wrong argument types
and name ambiguities (e.g. UnityEngine.Pose vs Wayfinder.Campaign.Pose) that a parse-only check
misses. Stubs mirror only the members used; a pass says nothing about real Unity API drift.
Run from the repo root after `python3 Tools/Build/headless_cs.py compile`."""
import os, re, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join("Tools", "Build"))
import headless_cs as h  # noqa: E402

tc = h.Toolchain()
refs = tc.framework_refs() + ["-r:" + os.path.join(tc.out, n + ".dll") for n in (
    "Wayfinder.Core", "Wayfinder.Simulation", "Wayfinder.World", "Wayfinder.Progression",
    "Wayfinder.Persistence", "Wayfinder.Campaign")]
glue = ["Assets/Wayfinder/Scripts/Platform/LifecycleHandler.cs", "Assets/Wayfinder/Scripts/Platform/CampaignHost.cs",
        "Assets/Wayfinder/Scripts/Presentation/TitleFlowHost.cs", "Assets/Wayfinder/Scripts/Presentation/VerticalSliceDriver.cs"]
stubs = os.path.join(HERE, "UnityStubs.cs")
out = os.path.join(tempfile.gettempdir(), "wayfinder-stubcheck.dll")

def compile_(files):
    res = tc.csc(["-target:library", "-langversion:9.0", "-warn:4", "-nowarn:0067,0649,0414,0169", "-out:" + out]
                 + refs + [stubs] + files, cwd=os.getcwd())
    return res.returncode, [l for l in res.stdout.splitlines() if ": error " in l]

code, errors = compile_(glue)
print(f"real glue vs real campaign assemblies + stubs: exit {code}, {len(errors)} error(s)")
for e in errors: print("  " + e)
driver = open(glue[3], encoding="utf-8").read()
controls = {
    "control A — Pose alias removed (expect CS0104)": driver.replace("using Pose = Wayfinder.Campaign.Pose;", ""),
    "control B — misnamed session member (expect CS1061)": driver.replace("_session.FullRescue()", "_session.FullRescueNow()"),
}
ok = code == 0
for label, text in controls.items():
    path = os.path.join(tempfile.gettempdir(), "wayfinder-stubcheck-control.cs")
    open(path, "w", encoding="utf-8").write(text)
    c, errs = compile_(glue[:3] + [path])
    caught = c != 0 and errs
    ok &= bool(caught)
    print(f"{label}: {'caught' if caught else 'NOT CAUGHT'} — {re.sub(r'^.*?error ', 'error ', errs[0]) if errs else ''}")
print("STUB TYPE-CHECK:", "PASS (scratch; not Unity evidence)" if ok else "FAIL")
sys.exit(0 if ok else 1)
