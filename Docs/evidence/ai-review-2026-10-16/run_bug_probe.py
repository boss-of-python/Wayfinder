#!/usr/bin/env python3
"""Compile + run Docs/evidence/ai-review-2026-10-16/BugProbe.cs against the REAL Wayfinder assemblies.

The probe is deliberately kept outside Assets/ so `Tools/Build/headless_cs.py all` is unaffected.
It reuses the same pinned toolchain (Roslyn 3.9 / C# 9 / NUnitLite 3.13.3) that the repo already
bootstraps into ~/.cache/wayfinder-headless-cs.

    python3 Tools/Build/headless_cs.py bootstrap      # once, if the cache is cold
    python3 Docs/evidence/ai-review-2026-10-16/run_bug_probe.py

Exit code 0 = every characterization assertion held (i.e. every reported defect is still present).
"""
import os
import shutil
import subprocess
import sys
import tempfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", ".."))
HERE = os.path.dirname(os.path.abspath(__file__))
sys.path.insert(0, os.path.join(ROOT, "Tools", "Build"))
import headless_cs as h  # noqa: E402

PROBE = os.path.join(HERE, "BugProbe.cs")
ASSEMBLIES = ("Wayfinder.Core", "Wayfinder.Simulation", "Wayfinder.World",
              "Wayfinder.Progression", "Wayfinder.Persistence", "Wayfinder.Campaign")


def main() -> int:
    tc = h.Toolchain()
    tc.ensure_bootstrapped()

    work = tempfile.mkdtemp(prefix="wayfinder-bug-probe-")
    out = os.path.join(work, "BugProbe.dll")
    refs = tc.framework_refs()
    refs += ["-r:" + os.path.join(tc.out, name + ".dll") for name in ASSEMBLIES]
    refs += ["-r:" + os.path.join(tc.lib, "nunit.framework.dll")]

    res = tc.csc(["-target:library", "-langversion:9.0", "-warn:4", "-nowarn:0067,0649,0414,0169",
                  "-out:" + out] + refs + [PROBE], cwd=ROOT)
    errors = [l for l in (res.stdout or "").splitlines() if ": error " in l]
    warnings = [l for l in (res.stdout or "").splitlines() if ": warning " in l]
    print(f"probe compile: exit {res.returncode}, {len(errors)} error(s), {len(warnings)} warning(s)")
    for line in errors + warnings:
        print("  " + line)
    if res.returncode != 0:
        return 2

    for name in ASSEMBLIES:
        shutil.copyfile(os.path.join(tc.out, name + ".dll"), os.path.join(work, name + ".dll"))
    for name in ("nunit.framework.dll", "nunitlite.dll", "Newtonsoft.Json.dll"):
        shutil.copyfile(os.path.join(tc.lib, name), os.path.join(work, name))

    xml = os.path.join(work, "BugProbe.xml")
    env = tc.env()
    env["WAYFINDER_CONTENT_DIR"] = os.path.join("Assets", "Wayfinder", "Content")
    cmd = [tc.dotnet, os.path.join(tc.lib, "nunitlite-host.dll"), out,
           "--result=" + xml, "--labels=Off", "--workers=1"]
    run = subprocess.run(cmd, cwd=ROOT, env=env, stdout=subprocess.PIPE, stderr=subprocess.STDOUT, text=True)
    print(run.stdout)
    with open(xml, encoding="utf-8") as fh:
        head = fh.read(400)
    print("result xml head:", head.splitlines()[1] if "\n" in head else head)
    print("probe workdir:", work)
    return run.returncode


if __name__ == "__main__":
    sys.exit(main())
