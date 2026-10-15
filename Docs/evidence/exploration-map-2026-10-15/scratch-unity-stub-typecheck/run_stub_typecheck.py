#!/usr/bin/env python3
"""SCRATCH check — NOT a Unity compile. Type-checks the campaign + exploration-map glue
(LifecycleHandler, CampaignHost, TitleFlowHost, VerticalSliceDriver, MapTextureBinding, MapGlyphs,
MinimapView, MapOverlayView) against the REAL compiled Wayfinder assemblies and the hand-written
UnityStubs.cs next to this file. Catches misnamed Wayfinder members, wrong argument types and name
ambiguities that the parse-only check misses; says nothing about real Unity API drift.
Run from the repo root after `python3 Tools/Build/headless_cs.py compile`."""
import os, re, sys, tempfile
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join("Tools", "Build"))
import headless_cs as h  # noqa: E402

tc = h.Toolchain()
refs = tc.framework_refs() + ["-r:" + os.path.join(tc.out, n + ".dll") for n in (
    "Wayfinder.Core", "Wayfinder.Simulation", "Wayfinder.World", "Wayfinder.Progression",
    "Wayfinder.Persistence", "Wayfinder.Campaign")]
P = "Assets/Wayfinder/Scripts/"
glue = [P + "Platform/LifecycleHandler.cs", P + "Platform/CampaignHost.cs", P + "Presentation/TitleFlowHost.cs",
        P + "Presentation/VerticalSliceDriver.cs", P + "Presentation/MapTextureBinding.cs", P + "Presentation/MapGlyphs.cs",
        P + "Presentation/MinimapView.cs", P + "Presentation/MapOverlayView.cs"]
stubs = os.path.join(HERE, "UnityStubs.cs")
out = os.path.join(tempfile.gettempdir(), "wayfinder-map-stubcheck.dll")


def compile_(files):
    res = tc.csc(["-target:library", "-langversion:9.0", "-warn:4", "-nowarn:0067,0649,0414,0169", "-out:" + out]
                 + refs + [stubs] + files, cwd=os.getcwd())
    return res.returncode, [l for l in res.stdout.splitlines() if ": error " in l], \
        [l for l in res.stdout.splitlines() if ": warning " in l]


code, errors, warnings = compile_(glue)
print(f"real glue vs real Wayfinder assemblies + stubs: exit {code}, {len(errors)} error(s), {len(warnings)} warning(s)")
for e in errors + warnings:
    print("  " + e)
controls = {
    "control A — Pose alias removed from the driver (expect CS0104)":
        (3, "using Pose = Wayfinder.Campaign.Pose;", ""),
    "control B — misnamed ExplorationMap member in the map screen (expect CS1061)":
        (7, "_presenter.Map.Raster", "_presenter.Map.Bitmap"),
    "control C — misnamed MapViewport member in the minimap (expect CS1061)":
        (6, "_viewport.UvFor(raster)", "_viewport.UvOf(raster)"),
}
ok = code == 0 and not warnings
for label, (index, old, new) in controls.items():
    text = open(glue[index], encoding="utf-8").read()
    assert old in text, f"control anchor missing: {old}"
    path = os.path.join(tempfile.gettempdir(), "wayfinder-map-stubcheck-control.cs")
    open(path, "w", encoding="utf-8").write(text.replace(old, new))
    c, errs, _ = compile_([g for i, g in enumerate(glue) if i != index] + [path])
    caught = c != 0 and errs
    ok &= bool(caught)
    print(f"{label}: {'caught' if caught else 'NOT CAUGHT'} — {re.sub(r'^.*?error ', 'error ', errs[0]) if errs else ''}")
print("STUB TYPE-CHECK:", "PASS (scratch; not Unity evidence)" if ok else "FAIL")
sys.exit(0 if ok else 1)
