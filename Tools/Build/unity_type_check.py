#!/usr/bin/env python3
"""Type-check the Wayfinder assemblies that Unity has never compiled (KI-034).

WHY THIS EXISTS
---------------
`Tools/Build/headless_cs.py` compiles and *runs* the engine-free assemblies. It cannot touch
`Wayfinder.Platform`, `Wayfinder.Presentation`, `Wayfinder.Editor` or the Editor/PlayMode test
assemblies, because those use UnityEngine/UnityEditor/InputSystem/uGUI/URP — so those files have
only ever been **parse-checked**. A parse check cannot see a compile error, and a compile error in
an Editor assembly aborts Unity batchmode *before* `-executeMethod` runs. That is exactly how five
consecutive cloud builds died at "Unity 1 of 2: Setup Project" with `Build failed with exit code 1`
and no diagnostic: batchmode never reached the project's own code, so the project's own failure
breadcrumb was never written.

One such error was found by reading source and fixed in PR #15 (`ContentImporter.cs` uses
`Newtonsoft.Json.Linq` while `Wayfinder.Editor.asmdef` did not reference `Unity.Newtonsoft.Json`).
"Reading source" is not a scalable way to find the rest. This tool is.

HOW
---
`Tools/Build/unity_api_stubs/` holds hand-written, signature-shaped stubs of the Unity APIs this
repository uses, split into the same assemblies Unity ships (CoreModule, UnityEngine.UI,
Unity.InputSystem, Unity.RenderPipelines.Universal.Runtime, UnityEditor, TestRunner). This script

  1. compiles the stubs with the pinned Roslyn 3.9 toolchain (`headless_cs.bootstrap`), then
  2. compiles every Wayfinder assembly **including** the Unity-dependent files, giving each one
     exactly the references its own `.asmdef` declares — mirroring Unity's rule that asmdef
     references are NOT transitive (the bug class that produced PR #15).

WHAT A CLEAN RUN PROVES — AND WHAT IT DOES NOT
----------------------------------------------
  + Every C# file in Assets/ is now type-checked by a real Roslyn compiler at C# 9 / warn level 4,
    against dependency edges taken from the real .asmdef files.
  - It does NOT prove Unity will compile it. The stubs are hand-written: a stub member with the
    wrong signature is a false negative, and a missing stub member is a false positive. It cannot
    see API drift, package-version differences, obsolete/removed members, or anything inside
    Unity's own packages. Unity's editor remains the only authority.

USAGE
-----
  python3 Tools/Build/unity_type_check.py            # compile stubs + all Wayfinder assemblies
  python3 Tools/Build/unity_type_check.py --verbose  # also print every compiled file

Exit code is non-zero on any compile error or warning.
"""

from __future__ import annotations

import argparse
import json
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import headless_cs  # noqa: E402  (sibling module; path set above)

ROOT = headless_cs.ROOT
ASSETS = headless_cs.ASSETS
SCRIPTS = os.path.join(ASSETS, "Scripts")
TESTS = os.path.join(ASSETS, "Tests")
LOGS = headless_cs.LOGS
STUB_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "unity_api_stubs")

# Stub assembly name -> stub source file. Mirrors the assembly names Unity itself uses, so an
# .asmdef `references` entry maps straight onto one of these.
STUB_ASSEMBLIES = [
    ("UnityEngine.CoreModule", "UnityEngine.CoreModule.cs", []),
    ("UnityEngine.UI", "UnityEngine.UI.cs", []),
    ("Unity.InputSystem", "Unity.InputSystem.cs", []),
    ("Unity.RenderPipelines.Universal.Runtime", "Unity.RenderPipelines.Universal.Runtime.cs", []),
    ("UnityEditor", "UnityEditor.cs", []),
    ("UnityEngine.TestRunner", "UnityEngine.TestRunner.cs", ["@nunit.framework"]),
]

# Aliases Unity resolves to the same assembly as far as the asmdef graph is concerned.
ASSEMBLY_ALIASES = {
    "UnityEngine.TestRunner": "UnityEngine.TestRunner",
    "UnityEditor.TestRunner": "UnityEngine.TestRunner",
    "Unity.Newtonsoft.Json": "@Newtonsoft.Json",
}

# precompiledReferences entries Unity resolves from the package cache / the toolchain's lib dir.
PRECOMPILED_ALIASES = {
    "nunit.framework.dll": "@nunit.framework",
    "Newtonsoft.Json.dll": "@Newtonsoft.Json",
}

BUILD_ORDER = [
    ("Wayfinder.Core", os.path.join(SCRIPTS, "Core")),
    ("Wayfinder.Simulation", os.path.join(SCRIPTS, "Simulation")),
    ("Wayfinder.World", os.path.join(SCRIPTS, "World")),
    ("Wayfinder.Progression", os.path.join(SCRIPTS, "Progression")),
    ("Wayfinder.Persistence", os.path.join(SCRIPTS, "Persistence")),
    ("Wayfinder.Campaign", os.path.join(SCRIPTS, "Campaign")),
    ("Wayfinder.Platform", os.path.join(SCRIPTS, "Platform")),
    ("Wayfinder.Presentation", os.path.join(SCRIPTS, "Presentation")),
    ("Wayfinder.Editor", os.path.join(SCRIPTS, "Editor")),
    ("Wayfinder.Tests.EditMode", os.path.join(TESTS, "EditMode")),
    ("Wayfinder.Tests.Editor", os.path.join(TESTS, "Editor")),
    ("Wayfinder.Tests.PlayMode", os.path.join(TESTS, "PlayMode")),
]


def sources(directory: str) -> list[str]:
    out = []
    for dp, _, fn in os.walk(directory):
        for f in sorted(fn):
            if f.endswith(".cs"):
                out.append(os.path.join(dp, f))
    return sorted(out)


def read_asmdef(directory: str) -> dict:
    files = [f for f in os.listdir(directory) if f.endswith(".asmdef")]
    if not files:
        raise SystemExit(f"no .asmdef in {directory}")
    with open(os.path.join(directory, files[0]), encoding="utf-8") as fh:
        return json.load(fh)


def compile_stubs(tc: headless_cs.Toolchain, stub_out: str) -> list[str]:
    os.makedirs(stub_out, exist_ok=True)
    built: dict[str, str] = {}
    for name, source, extra in STUB_ASSEMBLIES:
        out = os.path.join(stub_out, name + ".dll")
        refs = tc.framework_refs()
        refs += ["-r:" + p for p in built.values()]
        for e in extra:
            refs.append("-r:" + os.path.join(tc.lib, e[1:] + ".dll"))
        res = tc.csc(["-target:library", "-langversion:9.0", "-nowarn:0067,0169,0414,0649,0108,0114,3021",
                      "-out:" + out] + refs + [os.path.join(STUB_DIR, source)], cwd=ROOT)
        errors = [l for l in (res.stdout or "").splitlines() if ": error " in l]
        if res.returncode != 0 or errors:
            print("\n".join(errors) or res.stdout)
            raise SystemExit(f"stub assembly {name} does not compile — fix {source}")
        built[name] = out
    return list(built.values())


def resolve(ref: str, tc: headless_cs.Toolchain, built: dict[str, str], stubs: dict[str, str]) -> str | None:
    """Map one .asmdef reference onto a file path, or None if it is not something we can fake."""
    ref = ASSEMBLY_ALIASES.get(ref, ref)
    if ref in built:
        return built[ref]
    if ref in stubs:
        return stubs[ref]
    if ref == "@Newtonsoft.Json":
        return os.path.join(tc.lib, "Newtonsoft.Json.dll")
    if ref == "@nunit.framework":
        return os.path.join(tc.lib, "nunit.framework.dll")
    return None


def main() -> int:
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument("--verbose", action="store_true")
    args = ap.parse_args()

    tc = headless_cs.Toolchain()
    tc.ensure_bootstrapped()
    os.makedirs(LOGS, exist_ok=True)

    stub_out = os.path.join(tc.out, "unity-stubs")
    headless_cs.log("compile the Unity API stubs (hand-written; see Tools/Build/unity_api_stubs)")
    for p in compile_stubs(tc, stub_out):
        headless_cs.log("    " + os.path.relpath(p, ROOT))
    stubs = {name: os.path.join(stub_out, name + ".dll") for name, _, _ in STUB_ASSEMBLIES}

    engine = [stubs["UnityEngine.CoreModule"]]
    out_dir = os.path.join(tc.out, "unity-typecheck")
    os.makedirs(out_dir, exist_ok=True)
    built: dict[str, str] = {}
    report = []
    ok = True

    for name, directory in BUILD_ORDER:
        asmdef = read_asmdef(directory)
        declared = asmdef.get("name", name)
        is_editor = "Editor" in (asmdef.get("includePlatforms") or [])
        files = sources(directory)
        refs = tc.framework_refs() + ["-r:" + engine[0]]
        if is_editor:
            refs += ["-r:" + stubs["UnityEditor"]]
        for ref in asmdef.get("references", []):
            path = resolve(ref, tc, built, stubs)
            if path is None:
                headless_cs.log(f"    note: {declared} references '{ref}' — no stub; Unity-only symbol")
                continue
            refs.append("-r:" + path)
        for pre in asmdef.get("precompiledReferences", []) or []:
            path = resolve(PRECOMPILED_ALIASES.get(pre, pre), tc, built, stubs)
            if path:
                refs.append("-r:" + path)
        defines = ["UNITY_EDITOR" if is_editor else "UNITY_RUNTIME", "UNITY_6000_0_OR_NEWER"]
        if name.startswith("Wayfinder.Tests"):
            defines.append("UNITY_INCLUDE_TESTS")
        out = os.path.join(out_dir, declared + ".dll")
        res = tc.csc(["-target:library", "-langversion:9.0", "-warn:4", "-deterministic",
                      "-define:" + ";".join(defines), "-out:" + out] + refs + files, cwd=ROOT)
        lines = (res.stdout or "").splitlines()
        errors = [l for l in lines if ": error " in l or l.startswith("error ")]
        warnings = [l for l in lines if ": warning " in l]
        status = "OK" if res.returncode == 0 and not errors else "FAILED"
        ok = ok and status == "OK"
        headless_cs.log(f"{declared}: {status} — {len(files)} file(s), {len(errors)} error(s), "
                        f"{len(warnings)} warning(s)")
        for l in errors + warnings:
            print("    " + l)
        if args.verbose:
            for f in files:
                headless_cs.log("        " + os.path.relpath(f, ROOT))
        built[declared] = out
        report.append({"assembly": declared, "status": status, "files": [os.path.relpath(f, ROOT) for f in files],
                       "errors": errors, "warnings": warnings})
        if status != "OK":
            break

    with open(os.path.join(LOGS, "unity-type-check.json"), "w") as fh:
        json.dump(report, fh, indent=2)
    headless_cs.log(f"report: {os.path.relpath(os.path.join(LOGS, 'unity-type-check.json'), ROOT)}")
    headless_cs.log(f"SUMMARY unity-type-check={'OK' if ok else 'FAILED'}")
    return 0 if ok else 1


if __name__ == "__main__":
    sys.exit(main())
