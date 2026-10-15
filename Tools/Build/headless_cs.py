#!/usr/bin/env python3
"""Headless C# compile + NUnit test runner for Wayfinder's engine-independent assemblies.

WHY THIS EXISTS (D-014, KI-001/KI-003)
--------------------------------------
The implementation environment has no Unity editor and no .NET SDK, and its network egress is
limited to github.com / api.github.com / codeload.github.com / registry.npmjs.org / pypi.org /
files.pythonhosted.org. This script assembles a *real* C# 9 toolchain from those hosts only:

  * .NET Core 3.1.23 runtime      — PyPI wheel `dotnetcore2==3.1.23` (Microsoft-published)
  * Roslyn 3.9.0 (csc, C# 9.0)    — github.com/mono/roslyn-binaries (Microsoft.Net.Compilers 3.9.0,
                                     netstandard2.0 compiler assemblies + csc.exe host)
  * OpenSSL 1.1 shared libs        — only if the host OS lacks libssl.so.1.1 (needed by the 3.1
                                     runtime's crypto shim); taken from the psycopg2-binary 2.9.5 wheel
  * Newtonsoft.Json 13.0.3         — compiled from the tagged GitHub source (same major as Unity's
                                     com.unity.nuget.newtonsoft-json 3.x package)
  * NUnit 3.13.3 + NUnitLite       — compiled from the tagged GitHub source (Unity Test Framework
                                     embeds NUnit 3.5; the attribute/assert API used here is common)
  * .NET Standard 2.1 reference    — built from github.com/dotnet/standard (release/3.0,
    assembly                         src/netstandard/ref) so the game assemblies can be type-checked
                                     against exactly the API profile Unity 6 targets

It then compiles the assemblies that have **no UnityEngine dependency** (Core, World,
Progression, Persistence, Campaign and the pure part of Simulation) exactly as their .asmdef graph
declares, compiles the EditMode test assembly against them, and runs it with NUnitLite,
writing a standard NUnit3 XML result.

WHAT THIS IS — AND IS NOT
-------------------------
  + Real compilation by the real Roslyn compiler at `-langversion:9.0` (Unity 6 = C# 9).
  + Real execution of the EditMode NUnit suites that do not touch UnityEngine.
  + API-profile check: the same assemblies compiled against ONLY the .NET Standard 2.1 reference
    assembly (no .NET Core-only API can slip through).
  + Syntax-only (parser) verification of UnityEngine/UnityEditor-dependent files.
  - NOT a Unity build. Files that `using UnityEngine/UnityEditor/Unity.*` are NOT type-checked
    here; the Unity Test Runner on an owner machine remains the authoritative evidence for them.
  - Tests execute on the .NET Core 3.1 BCL (a superset of .NET Standard 2.1); the `profile`
    step is what guarantees no API outside .NET Standard 2.1 is used by the runtime assemblies.
  - NUnit 3.13.3 / Newtonsoft 13.0.3 built here are not the exact binaries Unity ships
    (UTF embeds NUnit 3.5; com.unity.nuget.newtonsoft-json 3.2.1). Only the common surface is used.

USAGE
-----
  python3 Tools/Build/headless_cs.py bootstrap          # download + build the toolchain (~1 min)
  python3 Tools/Build/headless_cs.py compile            # compile Wayfinder pure assemblies
  python3 Tools/Build/headless_cs.py test               # run EditMode tests (NUnitLite)
  python3 Tools/Build/headless_cs.py test -- --where "class =~ PersistenceTests"   # NUnit filter
  python3 Tools/Build/headless_cs.py profile            # compile pure assemblies vs .NET Standard 2.1 ref only
  python3 Tools/Build/headless_cs.py syntax             # parse-check Unity-dependent files (C# 9)
  python3 Tools/Build/headless_cs.py all                # bootstrap (if needed) + compile + test + profile + syntax

Environment:
  WAYFINDER_HEADLESS_CS_HOME  toolchain cache dir (default ~/.cache/wayfinder-headless-cs)
  GITHUB_TOKEN / GH_TOKEN     optional, raises api.github.com rate limits for the Roslyn download
Exit code is non-zero on any compile error, test failure, profile violation or syntax error.
"""

from __future__ import annotations

import argparse
import hashlib
import io
import json
import os
import platform
import shutil
import subprocess
import sys
import tarfile
import time
import urllib.request
import zipfile

ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
ASSETS = os.path.join(ROOT, "Assets", "Wayfinder")
HOME = os.environ.get("WAYFINDER_HEADLESS_CS_HOME") or os.path.join(
    os.path.expanduser("~"), ".cache", "wayfinder-headless-cs")
LOGS = os.path.join(ROOT, "Logs", "headless")

# --------------------------------------------------------------------------------------------
# Pinned artifacts (provenance recorded in Docs/DECISIONS.md D-014 and Docs/ASSET_MANIFEST.csv)
# --------------------------------------------------------------------------------------------
DOTNET_RUNTIME_WHEEL = {
    "url": "https://files.pythonhosted.org/packages/d9/04/ddd3fc5c374870e5e3d386b18512e8c0c7557aca9fbc1d9f286bf9f4844d/dotnetcore2-3.1.23-py3-none-manylinux1_x86_64.whl",
    "sha256": "5f076ddc39da0c685e7de20ecb91ee81185928918ec86fbeb3bffc55dd867ab5",
    "runtime_version": "3.1.23",
}
OPENSSL11_WHEEL = {  # only used when the OS has no libssl.so.1.1 / libcrypto.so.1.1
    "url": "https://files.pythonhosted.org/packages/36/af/a9f06e2469e943364b2383b45b3209b40350c105281948df62153394b4a9/psycopg2_binary-2.9.5-cp311-cp311-manylinux_2_17_x86_64.manylinux2014_x86_64.whl",
    "sha256": "bcda1c84a1c533c528356da5490d464a139b6e84eb77cc0b432e38c5c6dd7882",
}
ROSLYN = {
    "repo": "mono/roslyn-binaries",
    "commit": "1c6482470cd219dcc7503259a20f26a1723f20ec",
    "path": "Microsoft.Net.Compilers/3.9.0",
    "version": "3.9.0",
    # file -> (sha256, assemblyVersion or None for non-assemblies)
    "files": {
        "Microsoft.CodeAnalysis.dll": ("44297d9a467210a10a8c66573297956cee1f3ab4290d126eb53cf3aed0de1d97", "3.9.0.0"),
        "Microsoft.CodeAnalysis.CSharp.dll": ("58fc067e6cfb52480ce558eed5c55b6806e602ea235acd0fd0634dd771c9b715", "3.9.0.0"),
        "System.Collections.Immutable.dll": ("646d3b52a4898078f46534727bdb06ff23b72523441458b9f49ecc315bf3ef5c", "5.0.0.0"),
        "System.Reflection.Metadata.dll": ("46451e1168dd11d450aa9b6119f17cec9a70928a40ac3c752abf61ce809cba6f", "5.0.0.0"),
        "System.Memory.dll": ("8e76318e8b06692abf7dab1169d27d15557f7f0a34d36af6463eff0fe21213c7", "4.0.1.1"),
        "System.Buffers.dll": ("accccfbe45d9f08ffeed9916e37b33e98c65be012cfff6e7fa7b67210ce1fefb", "4.0.3.0"),
        "System.Numerics.Vectors.dll": ("1d3ef8698281e7cf7371d1554afef5872b39f96c26da772210a33da041ba1183", "4.1.4.0"),
        "System.Runtime.CompilerServices.Unsafe.dll": ("3cadcb6b8a7335141c7c357a1d77af1ff49b59b872df494f5025580191d1c0d5", "5.0.0.0"),
        "System.Text.Encoding.CodePages.dll": ("e1091b3af6cbc71bba27cb5ab0c9bc204f1af128fd3681fe60f6ec4a9d3b5345", "4.1.1.0"),
        "System.Threading.Tasks.Extensions.dll": ("4f81ffd0dc7204db75afc35ea4291769b07c440592f28894260eea76626a23c6", "4.2.0.1"),
        "csc.exe": ("81e98ade50f3e4127237128211778bd6ebe0c3998c9cc2f5eb44f3196a0297f8", "3.9.0.0"),
        "csc.rsp": ("c9862ddf2732a7d06ec2b779061d2a3e4861bdcbdee61ca74cf9c04201cc90a3", None),
        "version.txt": ("07332f6810639c13c53ccc9bbe9b3093d73d997994e8146e8f05a04ab39cd0d7", None),
    },
}
NEWTONSOFT = {
    "repo": "JamesNK/Newtonsoft.Json", "tag": "13.0.3",
    "commit": "0a2e291c0d9c0c7675d445703e51750363a549ef",
    # sha256 over (relative path + sha256 of content) of every extracted file — tarball bytes
    # from codeload are not guaranteed byte-stable, the tree is.
    "tree_sha256": "2c4062e0ca493f31e4d8f08adf83b3a2e27e171ca14dda63fa0610ed7c35c1fc",
    "defines": (
        "NETSTANDARD2_0;HAVE_ADO_NET;HAVE_APP_DOMAIN;HAVE_ASYNC;HAVE_BIG_INTEGER;HAVE_BINARY_FORMATTER;"
        "HAVE_BINARY_SERIALIZATION;HAVE_BINARY_EXCEPTION_SERIALIZATION;HAVE_CHAR_TO_LOWER_WITH_CULTURE;"
        "HAVE_CHAR_TO_STRING_WITH_CULTURE;HAVE_COM_ATTRIBUTES;HAVE_COMPONENT_MODEL;HAVE_CONCURRENT_COLLECTIONS;"
        "HAVE_COVARIANT_GENERICS;HAVE_DATA_CONTRACTS;HAVE_DATE_TIME_OFFSET;HAVE_DB_NULL_TYPE_CODE;HAVE_DYNAMIC;"
        "HAVE_EMPTY_TYPES;HAVE_ENTITY_FRAMEWORK;HAVE_EXPRESSIONS;HAVE_FAST_REVERSE;HAVE_FSHARP_TYPES;"
        "HAVE_FULL_REFLECTION;HAVE_GUID_TRY_PARSE;HAVE_HASH_SET;HAVE_ICLONEABLE;HAVE_ICONVERTIBLE;"
        "HAVE_IGNORE_DATA_MEMBER_ATTRIBUTE;HAVE_INOTIFY_COLLECTION_CHANGED;HAVE_INOTIFY_PROPERTY_CHANGING;"
        "HAVE_ISET;HAVE_LINQ;HAVE_MEMORY_BARRIER;HAVE_METHOD_IMPL_ATTRIBUTE;HAVE_NON_SERIALIZED_ATTRIBUTE;"
        "HAVE_READ_ONLY_COLLECTIONS;HAVE_SECURITY_SAFE_CRITICAL_ATTRIBUTE;HAVE_SERIALIZATION_BINDER_BIND_TO_NAME;"
        "HAVE_STREAM_READER_WRITER_CLOSE;HAVE_STRING_JOIN_WITH_ENUMERABLE;HAVE_TIME_SPAN_PARSE_WITH_CULTURE;"
        "HAVE_TIME_SPAN_TO_STRING_WITH_CULTURE;HAVE_TIME_ZONE_INFO;HAVE_TRACE_WRITER;HAVE_TYPE_DESCRIPTOR;"
        "HAVE_UNICODE_SURROGATE_DETECTION;HAVE_VARIANT_TYPE_PARAMETERS;HAVE_VERSION_TRY_PARSE;HAVE_XLINQ;"
        "HAVE_XML_DOCUMENT;HAVE_XML_DOCUMENT_TYPE;HAVE_CONCURRENT_DICTIONARY;HAVE_REGEX_TIMEOUTS"),
}
NETSTANDARD_REF = {  # .NET Standard 2.1 reference-assembly source (release/3.0 branch head)
    "repo": "dotnet/standard", "tag": "release/3.0",
    "commit": "908070fe0491d2b20761c32ebc375829c6c8c0cf",
    "tree_sha256": "6e12b73c1ba2e54690813652f054ebbe6140b016a4b739edb6220a7c458955b7",
}
NUNIT = {
    "repo": "nunit/nunit", "tag": "v3.13.3",
    "commit": "af8ca8bc779072b4fa5432e8620f06ca796d13a3",
    "tree_sha256": "1bd3a5ebb766b15c9bb218fa1009284c13f405e164db9b2ec285f345e05d0051",
    "defines": "NETSTANDARD;NETSTANDARD2_0;TASK_PARALLEL_LIBRARY_API",
}

UNITY_USING_MARKERS = ("using UnityEngine", "using UnityEditor", "using Unity.", "using TMPro")

# Wayfinder assemblies in dependency order: (name, source dir, asmdef references, extra refs)
ASSEMBLIES = [
    ("Wayfinder.Core", "Scripts/Core", [], []),
    ("Wayfinder.Simulation", "Scripts/Simulation", ["Wayfinder.Core"], []),
    ("Wayfinder.World", "Scripts/World", ["Wayfinder.Core"], []),
    ("Wayfinder.Progression", "Scripts/Progression", ["Wayfinder.Core", "Wayfinder.Simulation", "Wayfinder.World"], []),
    ("Wayfinder.Persistence", "Scripts/Persistence",
     ["Wayfinder.Core", "Wayfinder.Progression", "Wayfinder.Simulation", "Wayfinder.World"], ["Newtonsoft.Json"]),
    ("Wayfinder.Campaign", "Scripts/Campaign",
     ["Wayfinder.Core", "Wayfinder.Simulation", "Wayfinder.World", "Wayfinder.Progression", "Wayfinder.Persistence"],
     ["Newtonsoft.Json"]),
    ("Wayfinder.Tests.EditMode", "Tests/EditMode",
     ["Wayfinder.Core", "Wayfinder.Simulation", "Wayfinder.World", "Wayfinder.Progression", "Wayfinder.Persistence",
      "Wayfinder.Campaign"],
     ["Newtonsoft.Json", "nunit.framework"]),
]
# Assemblies whose every file needs UnityEngine/UnityEditor — syntax-checked only.
UNITY_ONLY_DIRS = ["Scripts/Platform", "Scripts/Presentation", "Scripts/Editor", "Tests/PlayMode", "Tests/Editor"]

NUNITLITE_HOST_CS = r'''
using System;
using System.IO;
using System.Linq;
using System.Reflection;
// Loads a test assembly (resolving sibling dependencies from its folder) and runs NUnitLite on it.
static class Host
{
    static int Main(string[] args)
    {
        if (args.Length < 1) { Console.Error.WriteLine("usage: nunitlite-host <tests.dll> [nunitlite options]"); return 2; }
        string asmPath = Path.GetFullPath(args[0]);
        string dir = Path.GetDirectoryName(asmPath);
        AppDomain.CurrentDomain.AssemblyResolve += (s, e) =>
        {
            string candidate = Path.Combine(dir, new AssemblyName(e.Name).Name + ".dll");
            return File.Exists(candidate) ? Assembly.LoadFrom(candidate) : null;
        };
        var asm = Assembly.LoadFrom(asmPath);
        return new NUnitLite.AutoRun(asm).Execute(args.Skip(1).ToArray());
    }
}
'''

SYNTAXCHECK_CS = r'''
using System;
using System.IO;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
// Parser-only check (no semantic analysis): reports C# 9 syntax errors for the given files,
// once with Unity preprocessor symbols defined and once without, so both #if branches are parsed.
static class SyntaxCheck
{
    static int Main(string[] args)
    {
        string[][] symbolSets =
        {
            new[] { "UNITY_EDITOR", "UNITY_5_3_OR_NEWER", "UNITY_2017_1_OR_NEWER", "UNITY_6000_0_OR_NEWER", "UNITY_INCLUDE_TESTS", "UNITY_ANDROID" },
            new string[0]
        };
        int errors = 0;
        foreach (var path in args)
        {
            string text = File.ReadAllText(path);
            foreach (var symbols in symbolSets)
            {
                var options = new CSharpParseOptions(LanguageVersion.CSharp9, preprocessorSymbols: symbols);
                var tree = CSharpSyntaxTree.ParseText(text, options, path);
                foreach (var d in tree.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error))
                {
                    Console.WriteLine(d.ToString());
                    errors++;
                }
            }
        }
        Console.WriteLine($"syntax-check: {args.Length} file(s), {errors} syntax error(s)");
        return errors == 0 ? 0 : 1;
    }
}
'''


# --------------------------------------------------------------------------------------------
# helpers
# --------------------------------------------------------------------------------------------
def log(msg: str) -> None:
    print(f"[headless-cs] {msg}", flush=True)


def sha256_file(path: str) -> str:
    h = hashlib.sha256()
    with open(path, "rb") as fh:
        for chunk in iter(lambda: fh.read(1 << 20), b""):
            h.update(chunk)
    return h.hexdigest()


def tree_sha256(root: str) -> str:
    h = hashlib.sha256()
    for dp, dn, fn in os.walk(root):
        dn.sort()
        for f in sorted(fn):
            p = os.path.join(dp, f)
            h.update(os.path.relpath(p, root).replace(os.sep, "/").encode())
            h.update(b"\0")
            h.update(bytes.fromhex(sha256_file(p)))
    return h.hexdigest()


def download(url: str, dest: str, sha256: str | None = None, headers: dict | None = None) -> str:
    if os.path.exists(dest) and (sha256 is None or sha256_file(dest) == sha256):
        return dest
    os.makedirs(os.path.dirname(dest), exist_ok=True)
    log(f"download {url}")
    req = urllib.request.Request(url, headers=headers or {})
    last_error = None
    for attempt in range(3):
        try:
            with urllib.request.urlopen(req, timeout=300) as resp, open(dest + ".part", "wb") as out:
                shutil.copyfileobj(resp, out)
            break
        except Exception as ex:  # noqa: BLE001 — retry on any transport error
            last_error = ex
            time.sleep(2 * (attempt + 1))
    else:
        raise RuntimeError(f"failed to download {url}: {last_error}")
    if sha256 is not None:
        actual = sha256_file(dest + ".part")
        if actual != sha256:
            os.remove(dest + ".part")
            raise RuntimeError(f"sha256 mismatch for {url}\n  expected {sha256}\n  actual   {actual}")
    os.replace(dest + ".part", dest)
    return dest


def github_headers() -> dict:
    headers = {"Accept": "application/vnd.github.raw", "User-Agent": "wayfinder-headless-cs"}
    token = os.environ.get("GITHUB_TOKEN") or os.environ.get("GH_TOKEN")
    if token:
        headers["Authorization"] = f"Bearer {token}"
    return headers


def run(cmd: list[str], cwd: str | None = None, env: dict | None = None, capture: bool = False) -> subprocess.CompletedProcess:
    return subprocess.run(cmd, cwd=cwd, env=env, text=True,
                          stdout=subprocess.PIPE if capture else None,
                          stderr=subprocess.STDOUT if capture else None, check=False)


class Toolchain:
    def __init__(self) -> None:
        self.home = HOME
        self.downloads = os.path.join(HOME, "downloads")
        self.runtime_root = os.path.join(HOME, "dotnet")           # contains bin/dotnet + shared/
        self.roslyn = os.path.join(HOME, "roslyn-3.9.0")
        self.lib = os.path.join(HOME, "lib")                       # Newtonsoft, nunit, hosts
        self.ssl = os.path.join(HOME, "openssl11")
        self.src = os.path.join(HOME, "src")
        self.out = os.path.join(HOME, "out")                       # Wayfinder build output
        self.stamp = os.path.join(HOME, "BOOTSTRAP_OK")

    # ---- paths ----
    @property
    def dotnet(self) -> str:
        return os.path.join(self.runtime_root, "bin", "dotnet")

    @property
    def runtime_dir(self) -> str:
        return os.path.join(self.runtime_root, "bin", "shared", "Microsoft.NETCore.App",
                            DOTNET_RUNTIME_WHEEL["runtime_version"])

    def env(self) -> dict:
        env = dict(os.environ)
        env["DOTNET_ROLL_FORWARD"] = "Major"
        env["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] = "1"
        env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
        env["DOTNET_NOLOGO"] = "1"
        if os.path.isdir(self.ssl):
            env["LD_LIBRARY_PATH"] = self.ssl + (":" + env["LD_LIBRARY_PATH"] if env.get("LD_LIBRARY_PATH") else "")
        return env

    def framework_refs(self) -> list[str]:
        refs = []
        for name in sorted(os.listdir(self.runtime_dir)):
            if not name.endswith(".dll"):
                continue
            if name.startswith("System.") or name in ("netstandard.dll", "mscorlib.dll", "Microsoft.CSharp.dll",
                                                     "Microsoft.Win32.Primitives.dll", "Microsoft.Win32.Registry.dll",
                                                     "Microsoft.VisualBasic.dll"):
                if name.endswith(".Native.dll") or ".Native." in name:
                    continue
                refs.append("-r:" + os.path.join(self.runtime_dir, name))
        return refs

    def csc(self, args: list[str], cwd: str | None = None) -> subprocess.CompletedProcess:
        cmd = [self.dotnet, os.path.join(self.roslyn, "csc.exe"), "-nologo", "-nostdlib", "-noconfig",
               "-utf8output"] + args
        return run(cmd, cwd=cwd, env=self.env(), capture=True)

    # ---- bootstrap steps ----
    def ensure_runtime(self) -> None:
        if os.path.exists(self.dotnet):
            return
        whl = download(DOTNET_RUNTIME_WHEEL["url"], os.path.join(self.downloads, "dotnetcore2-3.1.23.whl"),
                       DOTNET_RUNTIME_WHEEL["sha256"])
        log("extract .NET Core 3.1.23 runtime")
        with zipfile.ZipFile(whl) as z:
            for member in z.namelist():
                if member.startswith("dotnetcore2/bin/"):
                    target = os.path.join(self.runtime_root, member[len("dotnetcore2/"):])
                    if member.endswith("/"):
                        os.makedirs(target, exist_ok=True)
                        continue
                    os.makedirs(os.path.dirname(target), exist_ok=True)
                    with z.open(member) as src, open(target, "wb") as dst:
                        shutil.copyfileobj(src, dst)
        os.chmod(self.dotnet, 0o755)

    def ensure_openssl11(self) -> None:
        if os.path.isdir(self.ssl):
            return
        # Does the OS already provide libssl 1.1?
        probe = run(["/sbin/ldconfig", "-p"], capture=True) if os.path.exists("/sbin/ldconfig") else None
        if probe and probe.returncode == 0 and "libssl.so.1.1" in probe.stdout and "libcrypto.so.1.1" in probe.stdout:
            log("system provides libssl.so.1.1 — skipping bundled OpenSSL 1.1")
            os.makedirs(self.ssl + ".system", exist_ok=True)
            return
        whl = download(OPENSSL11_WHEEL["url"], os.path.join(self.downloads, "psycopg2_binary-2.9.5.whl"),
                       OPENSSL11_WHEEL["sha256"])
        log("extract OpenSSL 1.1 shared libraries (for the .NET Core 3.1 crypto shim)")
        os.makedirs(self.ssl, exist_ok=True)
        with zipfile.ZipFile(whl) as z:
            for member in z.namelist():
                base = os.path.basename(member)
                if member.startswith("psycopg2_binary.libs/") and (base.startswith("libssl-") or base.startswith("libcrypto-")):
                    with z.open(member) as src, open(os.path.join(self.ssl, base), "wb") as dst:
                        shutil.copyfileobj(src, dst)
                    link = os.path.join(self.ssl, "libssl.so.1.1" if base.startswith("libssl-") else "libcrypto.so.1.1")
                    if os.path.lexists(link):
                        os.remove(link)
                    os.symlink(base, link)

    def ensure_roslyn(self) -> None:
        if os.path.exists(os.path.join(self.roslyn, "csc.deps.json")):
            return
        os.makedirs(self.roslyn, exist_ok=True)
        for name, (sha, _) in ROSLYN["files"].items():
            url = (f"https://api.github.com/repos/{ROSLYN['repo']}/contents/{ROSLYN['path']}/{name}"
                   f"?ref={ROSLYN['commit']}")
            download(url, os.path.join(self.roslyn, name), sha, headers=github_headers())
        # runtimeconfig: run the (net472) csc.exe host and netstandard2.0 compiler on .NET Core 3.1
        with open(os.path.join(self.roslyn, "csc.runtimeconfig.json"), "w") as fh:
            json.dump({"runtimeOptions": {"tfm": "netcoreapp3.1",
                                          "framework": {"name": "Microsoft.NETCore.App", "version": "3.1.0"}}}, fh)
        # deps.json so app-local System.Collections.Immutable 5.0 etc. win over the framework's 1.2.x
        self.write_deps_json(self.roslyn, "csc", {n: v for n, (_, v) in ROSLYN["files"].items() if v})

    @staticmethod
    def write_deps_json(directory: str, app: str, assemblies: dict) -> None:
        target = ".NETCoreApp,Version=v3.1"
        targets = {}
        libraries = {}
        for name, version in assemblies.items():
            key = f"{os.path.splitext(name)[0]}/{version}"
            targets[key] = {"runtime": {name: {"assemblyVersion": version, "fileVersion": version}}}
            libraries[key] = {"type": "project", "serviceable": False, "sha512": ""}
        deps = {"runtimeTarget": {"name": target, "signature": ""},
                "targets": {target: targets}, "libraries": libraries}
        with open(os.path.join(directory, app + ".deps.json"), "w") as fh:
            json.dump(deps, fh, indent=2)

    def fetch_source(self, spec: dict, folder: str) -> str:
        dest = os.path.join(self.src, folder)
        if os.path.isdir(dest):
            return dest
        tgz = download(f"https://codeload.github.com/{spec['repo']}/tar.gz/{spec['commit']}",
                       os.path.join(self.downloads, folder + ".tar.gz"))
        log(f"extract {folder}")
        tmp = dest + ".tmp"
        shutil.rmtree(tmp, ignore_errors=True)
        with tarfile.open(tgz) as tf:
            tf.extractall(tmp)
        inner = [os.path.join(tmp, d) for d in os.listdir(tmp)]
        if len(inner) != 1:
            raise RuntimeError(f"unexpected archive layout for {folder}")
        actual = tree_sha256(inner[0])
        if actual != spec["tree_sha256"]:
            raise RuntimeError(f"source tree hash mismatch for {spec['repo']}@{spec['commit']}\n"
                               f"  expected {spec['tree_sha256']}\n  actual   {actual}")
        os.rename(inner[0], dest)
        shutil.rmtree(tmp, ignore_errors=True)
        return dest

    def ensure_newtonsoft(self) -> None:
        out = os.path.join(self.lib, "Newtonsoft.Json.dll")
        if os.path.exists(out):
            return
        src = self.fetch_source(NEWTONSOFT, "Newtonsoft.Json-13.0.3")
        os.makedirs(self.lib, exist_ok=True)
        log("compile Newtonsoft.Json 13.0.3 (netstandard2.0 defines)")
        proj = os.path.join(src, "Src", "Newtonsoft.Json")
        # Not strong-name signed: the upstream signed build swaps its InternalsVisibleTo list via a
        # SIGNED define; an unsigned build is what the Unity package consumer sees anyway.
        res = self.csc(["-target:library", "-langversion:9.0", "-nullable:enable", "-optimize+", "-deterministic",
                        "-define:" + NEWTONSOFT["defines"], "-nowarn:1591,1573,1570,1574,1712,8632,0436,8714",
                        "-out:" + out] + self.framework_refs() + ["-recurse:*.cs"], cwd=proj)
        self._check(res, "Newtonsoft.Json")

    def ensure_nunit(self) -> None:
        fw = os.path.join(self.lib, "nunit.framework.dll")
        lite = os.path.join(self.lib, "nunitlite.dll")
        host = os.path.join(self.lib, "nunitlite-host.dll")
        if all(os.path.exists(p) for p in (fw, lite, host)):
            return
        src = self.fetch_source(NUNIT, "nunit-3.13.3")
        os.makedirs(self.lib, exist_ok=True)
        common = [os.path.join(src, "src", "CommonAssemblyInfo.cs"),
                  os.path.join(src, "src", "NUnitFramework", "FrameworkVersion.cs")]
        key = "-keyfile:" + os.path.join(src, "src", "nunit.snk")
        warn = "-nowarn:1591,1573,1570,1574,1712,0618,0612"
        log("compile nunit.framework 3.13.3 (netstandard2.0 defines)")
        fwdir = os.path.join(src, "src", "NUnitFramework", "framework")
        res = self.csc(["-target:library", "-langversion:8.0", "-define:" + NUNIT["defines"], key, warn,
                        "-out:" + fw] + self.framework_refs() + ["-recurse:*.cs"] + common, cwd=fwdir)
        self._check(res, "nunit.framework")
        log("compile nunitlite 3.13.3")
        litedir = os.path.join(src, "src", "NUnitFramework", "nunitlite")
        res = self.csc(["-target:library", "-langversion:8.0", "-define:" + NUNIT["defines"], key, warn,
                        "-out:" + lite, "-r:" + fw] + self.framework_refs() + ["-recurse:*.cs"] + common, cwd=litedir)
        self._check(res, "nunitlite")
        log("compile nunitlite-host")
        host_cs = os.path.join(self.lib, "nunitlite-host.cs")
        with open(host_cs, "w") as fh:
            fh.write(NUNITLITE_HOST_CS)
        res = self.csc(["-target:exe", "-langversion:9.0", "-out:" + host, "-r:" + fw, "-r:" + lite]
                       + self.framework_refs() + [host_cs])
        self._check(res, "nunitlite-host")
        with open(os.path.join(self.lib, "nunitlite-host.runtimeconfig.json"), "w") as fh:
            json.dump({"runtimeOptions": {"tfm": "netcoreapp3.1",
                                          "framework": {"name": "Microsoft.NETCore.App", "version": "3.1.0"}}}, fh)

    @property
    def netstandard_ref(self) -> str:
        return os.path.join(self.lib, "netstandard21-ref.dll")

    def ensure_netstandard_ref(self) -> None:
        if os.path.exists(self.netstandard_ref):
            return
        src = self.fetch_source(NETSTANDARD_REF, "dotnet-standard-release-3.0")
        refdir = os.path.join(src, "src", "netstandard", "ref")
        with open(os.path.join(refdir, "netstandard.csproj")) as fh:
            import re
            files = re.findall(r'Compile Include="([^"]+)"', fh.read())
        os.makedirs(self.lib, exist_ok=True)
        log(f"compile .NET Standard 2.1 reference assembly from dotnet/standard ({len(files)} files)")
        res = self.csc(["-target:library", "-unsafe", "-runtimemetadataversion:v4.0.30319",
                        "-nowarn:0618,0619,0809,0169,0649,1591,3001,3002,3003,3005,3009,3021,0414,0067,0108,0109,0612,0659,0660,0661,8632",
                        "-out:" + self.netstandard_ref] + files, cwd=refdir)
        self._check(res, "netstandard2.1 reference assembly")

    def ensure_syntaxcheck(self) -> None:
        exe = os.path.join(self.roslyn, "syntaxcheck.dll")
        if os.path.exists(exe):
            return
        log("compile syntaxcheck (Roslyn parser host)")
        cs = os.path.join(self.roslyn, "syntaxcheck.cs")
        with open(cs, "w") as fh:
            fh.write(SYNTAXCHECK_CS)
        refs = ["-r:" + os.path.join(self.roslyn, n) for n, (_, v) in ROSLYN["files"].items()
                if v and n != "csc.exe"]
        res = self.csc(["-target:exe", "-langversion:9.0", "-out:" + exe] + refs + self.framework_refs() + [cs])
        self._check(res, "syntaxcheck")
        shutil.copyfile(os.path.join(self.roslyn, "csc.runtimeconfig.json"),
                        os.path.join(self.roslyn, "syntaxcheck.runtimeconfig.json"))
        assemblies = {n: v for n, (_, v) in ROSLYN["files"].items() if v and n != "csc.exe"}
        assemblies["syntaxcheck.dll"] = "1.0.0.0"
        self.write_deps_json(self.roslyn, "syntaxcheck", assemblies)

    @staticmethod
    def _check(res: subprocess.CompletedProcess, what: str) -> None:
        errors = [l for l in (res.stdout or "").splitlines() if "error " in l]
        if res.returncode != 0 or errors:
            print(res.stdout)
            raise RuntimeError(f"compiling {what} failed (exit {res.returncode})")

    def bootstrap(self) -> None:
        if platform.system() != "Linux" or platform.machine() not in ("x86_64", "AMD64"):
            raise SystemExit("headless_cs bootstrap currently supports Linux x86_64 only "
                             "(the pinned runtime wheel is manylinux x86_64).")
        os.makedirs(self.home, exist_ok=True)
        self.ensure_runtime()
        self.ensure_openssl11()
        self.ensure_roslyn()
        res = self.csc(["-version"])
        if res.returncode != 0:
            print(res.stdout)
            raise RuntimeError("csc.exe does not run on the bootstrapped runtime")
        log(f"csc version {res.stdout.strip()}")
        self.ensure_newtonsoft()
        self.ensure_nunit()
        self.ensure_netstandard_ref()
        self.ensure_syntaxcheck()
        with open(self.stamp, "w") as fh:
            fh.write(time.strftime("%Y-%m-%dT%H:%M:%SZ", time.gmtime()) + "\n")
        log(f"toolchain ready in {self.home}")

    def ensure_bootstrapped(self) -> None:
        if not os.path.exists(self.stamp):
            self.bootstrap()


# --------------------------------------------------------------------------------------------
# Wayfinder build / test
# --------------------------------------------------------------------------------------------
def is_unity_dependent(path: str) -> bool:
    with open(path, encoding="utf-8-sig") as fh:
        for line in fh:
            stripped = line.strip()
            if stripped.startswith("using ") and stripped.startswith(UNITY_USING_MARKERS):
                return True
    return False


def collect_sources(directory: str) -> tuple[list[str], list[str]]:
    included, excluded = [], []
    for dp, _, fn in os.walk(directory):
        for f in sorted(fn):
            if not f.endswith(".cs"):
                continue
            p = os.path.join(dp, f)
            (excluded if is_unity_dependent(p) else included).append(p)
    return sorted(included), sorted(excluded)


def rel(path: str) -> str:
    return os.path.relpath(path, ROOT).replace(os.sep, "/")


def compile_wayfinder(tc: Toolchain) -> bool:
    tc.ensure_bootstrapped()
    os.makedirs(tc.out, exist_ok=True)
    os.makedirs(LOGS, exist_ok=True)
    ok = True
    report = []
    for name, subdir, asm_refs, extra in ASSEMBLIES:
        srcdir = os.path.join(ASSETS, subdir)
        included, excluded = collect_sources(srcdir)
        refs = tc.framework_refs()
        refs += ["-r:" + os.path.join(tc.out, r + ".dll") for r in asm_refs]
        refs += ["-r:" + os.path.join(tc.lib, e + ".dll") for e in extra]
        defines = ["WAYFINDER_HEADLESS"]
        if name.startswith("Wayfinder.Tests"):
            defines.append("UNITY_INCLUDE_TESTS")
        out = os.path.join(tc.out, name + ".dll")
        args = ["-target:library", "-langversion:9.0", "-deterministic", "-debug:portable", "-warn:4",
                "-define:" + ";".join(defines), "-out:" + out] + refs + included
        res = tc.csc(args, cwd=ROOT)
        lines = (res.stdout or "").splitlines()
        errors = [l for l in lines if ": error " in l or l.startswith("error ")]
        warnings = [l for l in lines if ": warning " in l]
        status = "OK" if res.returncode == 0 and not errors else "FAILED"
        if status == "FAILED":
            ok = False
        log(f"{name}: {status} — {len(included)} file(s) compiled, {len(excluded)} Unity-dependent file(s) excluded, "
            f"{len(errors)} error(s), {len(warnings)} warning(s)")
        for e in excluded:
            log(f"    excluded (UnityEngine): {rel(e)}")
        for l in errors + warnings:
            print("    " + l)
        report.append({"assembly": name, "status": status, "compiled": [rel(p) for p in included],
                       "excluded_unity_dependent": [rel(p) for p in excluded],
                       "errors": errors, "warnings": warnings})
        if status == "FAILED":
            break
    with open(os.path.join(LOGS, "compile-report.json"), "w") as fh:
        json.dump(report, fh, indent=2)
    log(f"compile report: {rel(os.path.join(LOGS, 'compile-report.json'))}")
    return ok


def run_tests(tc: Toolchain, extra_args: list[str]) -> bool:
    tc.ensure_bootstrapped()
    tests = os.path.join(tc.out, "Wayfinder.Tests.EditMode.dll")
    if not os.path.exists(tests):
        raise SystemExit("compile first: python3 Tools/Build/headless_cs.py compile")
    for dep in ("nunit.framework.dll", "nunitlite.dll", "Newtonsoft.Json.dll"):
        shutil.copyfile(os.path.join(tc.lib, dep), os.path.join(tc.out, dep))
    os.makedirs(LOGS, exist_ok=True)
    result_xml = os.path.join(LOGS, "EditMode.headless.xml")
    env = tc.env()
    env["WAYFINDER_CONTENT_DIR"] = os.path.join(ASSETS, "Content")
    cmd = [tc.dotnet, os.path.join(tc.lib, "nunitlite-host.dll"), tests,
           "--result=" + result_xml, "--labels=Off", "--workers=1"] + extra_args
    log("run EditMode suites with NUnitLite 3.13.3: " + " ".join(rel(c) if c.startswith(ROOT) else c for c in cmd[2:]))
    res = run(cmd, cwd=ROOT, env=env)
    log(f"NUnit3 result XML: {rel(result_xml)}")
    return res.returncode == 0


def profile_check(tc: Toolchain) -> bool:
    """Compile the pure assemblies against ONLY the .NET Standard 2.1 reference assembly.

    Unity 6's scripting backend exposes the .NET Standard 2.1 API profile; compiling against the
    .NET Core 3.1 implementation assemblies (as `compile` does, so the tests can execute) would
    let a .NET-Core-only API through. Newtonsoft.Json is rebuilt against the same reference so the
    type identities unify. No code is executed here."""
    tc.ensure_bootstrapped()
    outdir = os.path.join(tc.out, "netstandard2.1")
    os.makedirs(outdir, exist_ok=True)
    ns = tc.netstandard_ref
    newtonsoft = os.path.join(outdir, "Newtonsoft.Json.dll")
    if not os.path.exists(newtonsoft):
        proj = os.path.join(tc.src, "Newtonsoft.Json-13.0.3", "Src", "Newtonsoft.Json")
        res = tc.csc(["-target:library", "-langversion:9.0", "-nullable:enable", "-optimize+", "-deterministic",
                      "-define:" + NEWTONSOFT["defines"], "-nowarn:1591,1573,1570,1574,1712,8632,0436,8714",
                      "-out:" + newtonsoft, "-r:" + ns, "-recurse:*.cs"], cwd=proj)
        if res.returncode != 0:
            print(res.stdout)
            log("Newtonsoft.Json does not compile against netstandard2.1 ref — toolchain problem")
            return False
    ok = True
    for name, subdir, asm_refs, extra in ASSEMBLIES:
        if name.startswith("Wayfinder.Tests"):
            continue  # nunit.framework is not part of the shipped profile check
        included, _ = collect_sources(os.path.join(ASSETS, subdir))
        refs = ["-r:" + ns] + ["-r:" + os.path.join(outdir, r + ".dll") for r in asm_refs]
        refs += ["-r:" + os.path.join(outdir, e + ".dll") for e in extra]
        res = tc.csc(["-target:library", "-langversion:9.0", "-deterministic", "-warn:4",
                      "-define:WAYFINDER_HEADLESS", "-out:" + os.path.join(outdir, name + ".dll")] + refs + included,
                     cwd=ROOT)
        lines = (res.stdout or "").splitlines()
        errors = [l for l in lines if ": error " in l or l.startswith("error ")]
        status = "OK" if res.returncode == 0 and not errors else "FAILED"
        log(f"{name} vs .NET Standard 2.1 reference only: {status} ({len(included)} file(s))")
        for l in errors:
            print("    " + l)
        if status == "FAILED":
            ok = False
            break
    return ok


def syntax_check(tc: Toolchain) -> bool:
    tc.ensure_bootstrapped()
    files = []
    for name, subdir, _, _ in ASSEMBLIES:
        _, excluded = collect_sources(os.path.join(ASSETS, subdir))
        files += excluded
    for subdir in UNITY_ONLY_DIRS:
        inc, exc = collect_sources(os.path.join(ASSETS, subdir))
        files += inc + exc
    files = sorted(set(files))
    log(f"syntax-check (Roslyn 3.9 parser, C# 9) of {len(files)} Unity-dependent file(s) — not type-checked")
    for f in files:
        log(f"    {rel(f)}")
    res = run([tc.dotnet, os.path.join(tc.roslyn, "syntaxcheck.dll")] + files, cwd=ROOT, env=tc.env())
    return res.returncode == 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("command", choices=["bootstrap", "compile", "test", "profile", "syntax", "all"])
    parser.add_argument("nunit_args", nargs=argparse.REMAINDER,
                        help="extra NUnitLite options for 'test', after '--' (e.g. -- --where \"class =~ Persistence\")")
    args = parser.parse_args()
    if args.nunit_args and args.nunit_args[0] == "--":
        args.nunit_args = args.nunit_args[1:]
    tc = Toolchain()
    try:
        if args.command == "bootstrap":
            tc.bootstrap()
            return 0
        if args.command == "compile":
            return 0 if compile_wayfinder(tc) else 1
        if args.command == "test":
            return 0 if run_tests(tc, args.nunit_args) else 1
        if args.command == "profile":
            return 0 if profile_check(tc) else 1
        if args.command == "syntax":
            return 0 if syntax_check(tc) else 1
        if args.command == "all":
            tc.ensure_bootstrapped()
            compiled = compile_wayfinder(tc)
            tested = compiled and run_tests(tc, args.nunit_args)
            profiled = profile_check(tc)
            parsed = syntax_check(tc)
            log(f"SUMMARY compile={'OK' if compiled else 'FAILED'} tests={'OK' if tested else 'FAILED'} "
                f"netstandard2.1-profile={'OK' if profiled else 'FAILED'} syntax={'OK' if parsed else 'FAILED'}")
            return 0 if (compiled and tested and profiled and parsed) else 1
    except RuntimeError as ex:
        log(f"ERROR: {ex}")
        return 2
    return 0


if __name__ == "__main__":
    sys.exit(main())
