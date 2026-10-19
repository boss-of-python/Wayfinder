"""Contract tests for the cloud-build diagnostics helpers.

These run the real shell scripts against fixtures, including the verbatim log lines from the
failed cloud build of 2026-10-15 (run 38754979851, Unity 6000.3.26f1): that run aborted with
"Build failed with exit code 1" and the only cause in 1,996 log lines was a compile error
inside Unity's own Input System package. The scripts must turn that into an annotation that is
readable from the GitHub API, because Unity's exit code is not descriptive.

Run 38875757162 (2026-10-16) then failed a second way: the diagnostics said *nothing at all*.
game-ci starts Unity with `-logfile /dev/stdout`, so the editor log is the job log; the
workspace only held Unity's auxiliary worker logs, which match none of the patterns. The whole
8-minute build produced one annotation, "Build failed with exit code 1". A later rerun (38880573866,
attempt 2) exposed a related gap: when only worker logs survived, the "editor log missing" error
suppressed the fallback log tail. The tests below pin the fixes: the reporter distinguishes missing
log context from a diagnosed cause and still publishes a labeled worker-log tail, while a second
job downloads the finished job log (Tools/Build/fetch_job_log.sh) and annotates from it.

Stdlib only (no PyYAML): the workflow checks below are structural text assertions.
"""
from pathlib import Path
import stat
import subprocess
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parent
REPORT = TOOLS / "report_unity_failure.sh"
SECRETS = TOOLS / "check_unity_secrets.sh"
FETCH_JOB_LOG = TOOLS / "fetch_job_log.sh"
WORKFLOW = TOOLS.parent.parent / ".github/workflows/android-cloud-build.yml"

# The job whose log carries Unity's editor output, and the job that reads it.
BUILD_JOB_NAME = "Build Wayfinder-dev.apk"

# Verbatim worker-log noise of the kind that left run 38875757162 undiagnosable: these files
# exist in Logs/ after any import, so "a log exists" never meant "the cause is readable".
WORKER_LOG = ("Using pre-set license\n"
              "Cmd: initializeCompiler\n"
              "Cmd: ImportAsset\n"
              "  path: Assets/Wayfinder/Content/Input/WayfinderControls.inputactions\n")

# Verbatim from the 2026-10-15 failure, with the GitHub Actions timestamp prefix.
REAL_ERROR = ("2026-10-15T14:56:44.8497357Z Library/PackageCache/com.unity.inputsystem@920b46832575/"
              "InputSystem/Editor/InputSystemPluginControl.cs(47,25): error CS0117: 'BuildTarget' does "
              "not contain a definition for 'ReservedCFE'")
REAL_WARNING = ("2026-10-15T14:56:44.8537247Z Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs"
                "(148,14): warning CS0414: The field 'ChaseCameraBehaviour._photoMode' is assigned but "
                "its value is never used")
REAL_ABORT = ("2026-10-15T14:56:44.9611658Z Aborting batchmode due to failure:\n"
              "2026-10-15T14:56:44.9611762Z Scripts have compiler errors.")
REAL_GAMECI = ("2026-10-15T14:56:51.7174866Z [ERROR] Error: Unity aborted before completing: "
               "Scripts have compiler errors.")
REAL_CSC = ("2026-10-15T14:56:40.0000000Z ## CmdLine: \"/opt/unity/Editor/Data/NetCoreRuntime/dotnet\" exec "
            "\"/opt/unity/Editor/Data/DotNetSdkRoslyn/csc.dll\" /nostdlib /noconfig /shared "
            "\"@Library/Bee/artifacts/1300b0aE.dag/Wayfinder.Core.rsp\"")


def run(script, workspace=None, env=None, logs=None):
    args = ["bash", str(script)]
    if workspace is not None:
        args.append(str(workspace))
    if logs:
        args.extend(str(log) for log in logs)
    return subprocess.run(args, capture_output=True, text=True, env=env or {})


class ReportUnityFailureTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.summary = self.root / "summary.md"
        self.env = {"GITHUB_STEP_SUMMARY": str(self.summary), "PATH": "/usr/bin:/bin"}

    def write_log(self, text, relative="Builds/Logs/Editor.log"):
        path = self.root / relative
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(text)
        return path

    def test_reports_the_real_2026_10_15_compile_error_as_an_annotation(self):
        self.write_log(REAL_ERROR + "\n")
        result = run(REPORT, self.root, self.env)
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("::error file=Library/PackageCache/com.unity.inputsystem@920b46832575/"
                      "InputSystem/Editor/InputSystemPluginControl.cs,line=47,col=25,"
                      "title=Unity compile error::error CS0117: 'BuildTarget' does not contain a "
                      "definition for 'ReservedCFE'", result.stdout)
        self.assertIn("compile errors (unique): **1**", self.summary.read_text())

    def test_timestamp_prefix_is_stripped_from_the_reported_path(self):
        self.write_log(REAL_ERROR + "\n")
        result = run(REPORT, self.root, self.env)
        self.assertNotIn("2026-10-15T", result.stdout)
        self.assertIn("::error file=Library/", result.stdout)

    def test_duplicate_error_lines_are_reported_once(self):
        # Unity printed the same error twice in the real run (two compilation passes).
        self.write_log(REAL_ERROR + "\n" + REAL_ERROR + "\n")
        result = run(REPORT, self.root, self.env)
        self.assertEqual(1, result.stdout.count("::error file="))
        self.assertIn("compile errors (unique): **1**", self.summary.read_text())

    def test_warnings_in_wayfinder_code_are_reported(self):
        self.write_log(REAL_WARNING + "\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::warning file=Assets/Wayfinder/Scripts/Simulation/ChaseCameraBehaviour.cs,"
                      "line=148,title=Unity warning::warning CS0414:", result.stdout)
        self.assertIn("warnings in Assets/Wayfinder (unique): **1**", self.summary.read_text())

    def test_warnings_outside_wayfinder_are_ignored(self):
        # A vendored asset under Assets/ but outside Assets/Wayfinder is not our warning budget.
        self.write_log("Assets/Plugins/SomeVendor/Bar.cs(1,2): warning CS0168: The variable is unused\n"
                       "Library/PackageCache/com.unity.foo@abc/Baz.cs(3,4): warning CS0168: unused\n")
        result = run(REPORT, self.root, self.env)
        self.assertNotIn("::warning", result.stdout)
        self.assertIn("warnings in Assets/Wayfinder (unique): **0**", self.summary.read_text())

    def test_batchmode_abort_reason_is_reported(self):
        self.write_log(REAL_ABORT + "\n" + REAL_GAMECI + "\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity aborted batchmode::Scripts have compiler errors.", result.stdout)
        self.assertIn("::error title=Unity aborted::Unity aborted before completing: Scripts have compiler errors.",
                      result.stdout)

    def test_build_method_exceptions_are_reported(self):
        self.write_log("BuildFailedException: Missing build scenes. Run Tools → Wayfinder → Setup Project first.\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity exception::BuildFailedException: Missing build scenes.", result.stdout)

    def test_compiled_wayfinder_assemblies_are_listed(self):
        assemblies = ["Wayfinder.Core", "Wayfinder.Campaign", "Wayfinder.Simulation"]
        text = "".join(
            f"Csc Library/Bee/artifacts/1300b0aE.dag/{name}.dll (+pdb)\n" for name in assemblies
        ) + REAL_CSC + "\n"
        self.write_log(text)
        result = run(REPORT, self.root, self.env)
        for name in assemblies:
            self.assertIn(f"::notice title=Unity compiled::{name}", result.stdout)
        self.assertIn("Wayfinder assemblies Unity compiled:", self.summary.read_text())

    def test_missing_log_is_a_notice_not_a_failure(self):
        result = run(REPORT, self.root, self.env)
        self.assertEqual(0, result.returncode)
        self.assertIn("::notice title=Unity diagnostics::No Editor.log", result.stdout)
        self.assertIn("No Unity log survived", self.summary.read_text())

    def test_clean_log_exits_zero_without_error_annotations(self):
        self.write_log("Refreshing native access tokens\nApplication quit\n")
        result = run(REPORT, self.root, self.env)
        self.assertEqual(0, result.returncode)
        self.assertNotIn("::error", result.stdout)
        self.assertIn("No compile error or batchmode abort found", self.summary.read_text())

    def test_annotation_cap_is_ten_plus_a_count(self):
        text = "".join(f"Assets/Wayfinder/F{i}.cs({i},1): error CS1002: ; expected\n" for i in range(1, 13))
        self.write_log(text)
        result = run(REPORT, self.root, self.env)
        self.assertEqual(10, result.stdout.count("::error file="))
        self.assertIn("::error title=Unity compile errors::12 unique compile errors; 10 reported here",
                      result.stdout)
        self.assertIn("compile errors (unique): **12**", self.summary.read_text())

    def test_apk_artefact_is_reported_with_its_size(self):
        apk = self.root / "Builds/Android/Wayfinder-dev.apk"
        apk.parent.mkdir(parents=True, exist_ok=True)
        apk.write_bytes(b"x" * 4096)
        self.write_log("Build succeeded\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::notice title=Unity artefact::Builds/Android/Wayfinder-dev.apk, 4096 bytes",
                      result.stdout)

    def test_explicit_log_argument_is_used(self):
        log = self.write_log(REAL_ERROR + "\n", relative="somewhere/else.log")
        result = run(REPORT, self.root, self.env, logs=[log])
        self.assertIn("::error file=Library/PackageCache/com.unity.inputsystem", result.stdout)

    def test_gameci_custom_editor_log_path_is_scanned(self):
        self.write_log(REAL_ERROR + "\n", relative="Logs/Unity-Setup.log")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error file=Library/PackageCache/com.unity.inputsystem", result.stdout)
        self.assertIn("Logs/Unity-Setup.log", self.summary.read_text())

    def test_overlapping_globs_list_each_log_once(self):
        # Builds/Logs/Editor.log matches both the explicit path and Builds/Logs/*.log.
        self.write_log(REAL_ERROR + "\n")
        run(REPORT, self.root, self.env)
        inspected = [line for line in self.summary.read_text().splitlines()
                     if line.startswith("Logs inspected:")]
        self.assertEqual(1, len(inspected))
        self.assertEqual(1, inspected[0].count("Editor.log"))

    def test_unresolvable_package_version_is_reported(self):
        # The most likely failure after a pin change: the registry has no such version.
        self.write_log("Cannot perform upm operation: unable to find package "
                       "com.unity.inputsystem@1.20.1 [https://packages.unity.com]\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity package resolution::Cannot perform upm operation: "
                      "unable to find package com.unity.inputsystem@1.20.1", result.stdout)
        self.assertIn("Package resolution failures:", self.summary.read_text())

    def test_resolved_packages_are_listed_against_the_manifest(self):
        (self.root / "Packages").mkdir(parents=True, exist_ok=True)
        (self.root / "Packages/manifest.json").write_text(
            '{"dependencies": {"com.unity.inputsystem": "1.20.1", '
            '"com.unity.modules.ai": "1.0.0", "com.unity.absent": "9.9.9"}}')
        self.write_log("Loading Library/PackageCache/com.unity.inputsystem@920b46832575/x.cs\n"
                       "Loading Library/PackageCache/com.unity.modules.ai/y.cs\n")
        run(REPORT, self.root, self.env)
        summary = self.summary.read_text()
        self.assertIn("Pinned packages seen in Library/PackageCache:", summary)
        self.assertIn("  - com.unity.inputsystem", summary)
        self.assertIn("  - com.unity.modules.ai", summary)
        self.assertNotIn("com.unity.absent", summary)

    def test_never_exits_nonzero_even_with_a_broken_log_path(self):
        result = run(REPORT, self.root, self.env, logs=[self.root / "does-not-exist.log"])
        self.assertEqual(0, result.returncode)

    # ---- regressions for run 38875757162: diagnostics that said nothing at all ----------

    def failing_env(self, **extra):
        env = dict(self.env)
        env["UNITY_SETUP_OUTCOME"] = "failure"
        env.update(extra)
        return env

    def test_every_run_says_which_logs_it_inspected(self):
        self.write_log(WORKER_LOG, relative="Logs/AssetImportWorker0.log")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::notice title=Unity diagnostics::Inspected Logs/AssetImportWorker0.log (",
                      result.stdout)
        self.assertIn("bytes); editor log recognised: no.", result.stdout)

    def test_worker_logs_only_include_a_tail_when_the_editor_log_is_missing(self):
        # The exact 2026-10-16 shape: Unity ran and failed, Logs/ holds only worker logs.
        # A missing-editor-log annotation is not itself the cause; include surviving context
        # when the separate Actions API download also fails.
        self.write_log(WORKER_LOG + "Fallback context marker: worker-tail-visible\n",
                       relative="Logs/AssetImportWorker0.log")
        self.write_log("Cmd: compileSnippet\n", relative="Logs/shadercompiler-UnityShaderCompiler-0.log")
        result = run(REPORT, self.root, self.failing_env())
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("::error title=Unity editor log missing::", result.stdout)
        self.assertIn("::error title=Unity failure not explained::", result.stdout)
        self.assertIn("::notice title=Unity workspace log tail::", result.stdout)
        self.assertIn("worker-tail-visible", result.stdout)
        self.assertIn("-logfile /dev/stdout", result.stdout)
        self.assertIn("AssetImportWorker0.log", result.stdout)

    def test_a_succeeding_run_with_only_worker_logs_invents_no_failure(self):
        self.write_log(WORKER_LOG, relative="Logs/AssetImportWorker0.log")
        result = run(REPORT, self.root, self.env)
        self.assertNotIn("::error", result.stdout)

    def test_failed_step_without_any_log_is_an_error_not_only_a_notice(self):
        result = run(REPORT, self.root, self.failing_env())
        self.assertIn("::notice title=Unity diagnostics::No Editor.log", result.stdout)
        self.assertIn("::error title=Unity log unavailable::", result.stdout)

    def test_unexplained_failure_publishes_a_log_tail_annotation(self):
        self.write_log("Unity Editor version:    6000.3.26f1 (de8167e7440c)\n"
                       "Something unrecognised went wrong at 100% load\n"
                       "Build failed, with exit code 1\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity failure not explained::", result.stdout)
        tail = [line for line in result.stdout.splitlines()
                if line.startswith("::notice title=Unity log tail::")]
        self.assertEqual(1, len(tail), result.stdout)
        # One-line workflow command: newlines and percent signs must be encoded.
        self.assertIn("%0A", tail[0])
        self.assertIn("100%25 load", tail[0])
        self.assertIn("Failure with no recognised cause", self.summary.read_text())

    def test_recognised_failure_does_not_add_a_tail_annotation(self):
        self.write_log(REAL_ERROR + "\nBuild failed, with exit code 1\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error file=Library/PackageCache/com.unity.inputsystem", result.stdout)
        self.assertNotIn("::notice title=Unity log tail::", result.stdout)
        self.assertNotIn("::error title=Unity failure not explained::", result.stdout)

    def test_editor_entry_point_breadcrumb_is_reported(self):
        # Written by Assets/Wayfinder/Scripts/Editor/EditorFailureLog.cs before it rethrows.
        self.write_log(
            "2026-10-16T08:53:20.1Z [Wayfinder] Editor method failed: "
            "Wayfinder.Editor.ProjectSetup.Setup - System.IO.FileNotFoundException: "
            "Could not find file 'Assets/Wayfinder/Content/Input/WayfinderControls.inputactions'.\n"
            "  at System.IO.File.Copy (System.String sourceFileName)\n",
            relative="Logs/wayfinder-editor-failure.log")
        result = run(REPORT, self.root, self.failing_env())
        self.assertIn("::error title=Wayfinder editor method failed::[Wayfinder] Editor method "
                      "failed: Wayfinder.Editor.ProjectSetup.Setup - "
                      "System.IO.FileNotFoundException:", result.stdout)
        self.assertNotIn("2026-10-16T08:53:20.1Z", result.stdout)
        # The cause is known, so the missing editor log is context, not the headline.
        self.assertIn("::notice title=Unity editor log missing::", result.stdout)

    def test_unresolvable_build_method_is_reported(self):
        self.write_log("Aborting batchmode due to failure:\n"
                       "executeMethod method Wayfinder.Editor.ProjectSetup.Setup in class "
                       "Wayfinder.Editor.ProjectSetup could not be found.\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity build method not found::executeMethod method "
                      "Wayfinder.Editor.ProjectSetup.Setup", result.stdout)

    def test_any_dotted_exception_type_is_reported(self):
        self.write_log("System.NullReferenceException: Object reference not set to an instance\n")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity exception::System.NullReferenceException: "
                      "Object reference not set to an instance", result.stdout)

    # ---- hard stops: no exception is thrown, so no breadcrumb and no compile error exists ----

    def test_native_crash_is_reported_as_the_cause(self):
        # Run 39141401948 failed in Setup Project with no breadcrumb in the workspace, which is
        # exactly what a process kill looks like: EditorFailureLog's catch never ran.
        self.write_log(
            "Refresh completed\n"
            "Crash!!!\n"
            "Received signal SIGSEGV\n"
            "  at 0x00007f8b1c2d3e4f in UnityEditor.AssetDatabase:Refresh\n")
        result = run(REPORT, self.root, self.failing_env())
        self.assertIn("::error title=Unity hard stop::Crash!!!", result.stdout)
        self.assertIn("::error title=Unity hard stop::Received signal SIGSEGV", result.stdout)
        # A cause was found, so the generic "not explained" error must not compete with it.
        self.assertNotIn("::error title=Unity failure not explained::", result.stdout)

    def test_container_oom_kill_is_reported_as_the_cause(self):
        # The Unity Android image plus shader compilation can exhaust a standard runner; the
        # kernel's own line is the only evidence that survives in the job log.
        self.write_log("Cmd: compileSnippet\n"
                       "unity-editor invoked oom-killer: gfp_mask=0x100cca(GFP_HIGHUSER_MOVABLE)\n"
                       "Killed process 4242 (unity-editor) total-vm:18675132kB anon-rss:9830400kB\n")
        result = run(REPORT, self.root, self.failing_env())
        self.assertIn("::error title=Unity hard stop::Killed process 4242 (unity-editor)", result.stdout)
        self.assertIn("::error title=Unity hard stop::unity-editor invoked oom-killer", result.stdout)
        self.assertNotIn("::error title=Unity failure not explained::", result.stdout)

    def test_a_crash_alone_is_evidence_of_failure_without_the_workflow_outcomes(self):
        # The in-job reporter is told the step outcomes, but a hard stop must also be recognised
        # from the log alone: a killed editor is a failure whatever the workflow says.
        self.write_log("Crash!!!\nReceived signal SIGSEGV\n", relative="Logs/AssetImportWorker0.log")
        result = run(REPORT, self.root, self.env)
        self.assertIn("::error title=Unity hard stop::Crash!!!", result.stdout)
        self.assertIn("::notice title=Wayfinder breadcrumb absent::", result.stdout)

    def test_benign_signals_and_memory_retry_lines_are_not_reported_as_a_crash(self):
        # Unity logs these on a normal shutdown; a false "hard stop" would be worse than none.
        self.write_log("Received signal SIGUSR1\n"
                       "Received signal SIGINT\n"
                       "Assertion failed: out of memory while pooling\n")
        result = run(REPORT, self.root, self.failing_env())
        self.assertNotIn("::error title=Unity hard stop::", result.stdout)

    def test_a_failure_with_no_breadcrumb_says_our_own_code_did_not_throw(self):
        # The exact shape of run 39141401948: Unity failed, Logs/ held only shader-compiler logs,
        # and nothing said whether ProjectSetup.Setup had thrown. The absence is itself evidence.
        self.write_log("Cmd: compileSnippet\n", relative="Logs/shadercompiler-UnityShaderCompiler-0.log")
        result = run(REPORT, self.root, self.failing_env())
        self.assertIn("::notice title=Wayfinder breadcrumb absent::", result.stdout)
        self.assertIn("no C# exception was thrown inside the build method", result.stdout)

    def test_a_present_breadcrumb_suppresses_the_absence_notice(self):
        self.write_log(
            "[Wayfinder] Editor method failed: Wayfinder.Editor.ProjectSetup.Setup - "
            "UnityEditor.Build.BuildFailedException: Unity QualitySettings asset not available.\n",
            relative="Logs/wayfinder-editor-failure.log")
        result = run(REPORT, self.root, self.failing_env())
        self.assertIn("::error title=Wayfinder editor method failed::", result.stdout)
        self.assertIn("BuildFailedException: Unity QualitySettings asset not available.", result.stdout)
        self.assertNotIn("::notice title=Wayfinder breadcrumb absent::", result.stdout)

    def test_the_diagnose_job_workspace_is_not_told_a_breadcrumb_is_missing(self):
        # That job checks the code out fresh and never runs Unity, so it has no Logs/ at all;
        # reporting an "absent breadcrumb" there would be a statement about nothing.
        result = run(REPORT, self.root, dict(self.env, UNITY_JOB_RESULT="failure"),
                     logs=[self.root / "error_logs/unity-job-log.txt"])
        self.assertNotIn("::notice title=Wayfinder breadcrumb absent::", result.stdout)

    def test_a_successful_run_does_not_claim_a_breadcrumb_is_missing(self):
        (self.root / "Logs").mkdir(parents=True, exist_ok=True)
        result = run(REPORT, self.root, self.env)
        self.assertNotIn("::notice title=Wayfinder breadcrumb absent::", result.stdout)

    def test_downloaded_job_log_is_accepted_as_the_editor_log(self):
        # What the diagnose job passes: the finished job's plain-text log, timestamps and all.
        log = self.write_log(
            "2026-10-16T08:48:30.0Z Unity Editor version:    6000.3.26f1 (de8167e7440c)\n"
            + REAL_ERROR + "\n"
            + REAL_ABORT + "\n",
            relative="Logs/unity-job-log.txt")
        env = dict(self.env)
        env["UNITY_JOB_RESULT"] = "failure"
        result = run(REPORT, self.root, env, logs=[log])
        self.assertIn("editor log recognised: yes", result.stdout)
        self.assertIn("::error file=Library/PackageCache/com.unity.inputsystem", result.stdout)
        self.assertIn("::error title=Unity aborted batchmode::Scripts have compiler errors.",
                      result.stdout)
        self.assertNotIn("::error title=Unity editor log missing::", result.stdout)


class ReporterMissingLogMessageTests(unittest.TestCase):
    def test_missing_explicit_log_does_not_blame_the_same_job(self):
        with tempfile.TemporaryDirectory() as tmp:
            env = {"PATH": "/usr/bin:/bin", "UNITY_BUILD_OUTCOME": "failure",
                   "GITHUB_STEP_SUMMARY": str(Path(tmp) / "summary.md")}
            result = subprocess.run(["bash", str(REPORT), tmp, "Logs/unity-job-log.txt"],
                                    capture_output=True, text=True, env=env, cwd=tmp)
            self.assertEqual(0, result.returncode)
            self.assertIn("::error title=Unity editor log missing::", result.stdout)
            self.assertIn("fetch_job_log.sh downloads it", result.stdout)
            self.assertNotIn("Explain the Unity result", result.stdout)


class FetchJobLogTests(unittest.TestCase):
    """fetch_job_log.sh orchestration, with `gh` replaced by a recording stub.

    The stub answers the two API calls the script makes; it does not re-implement gh's --jq, so
    these tests cover which call is made, the retry/exit contract and the output file - not
    GitHub's JSON shape.
    """

    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        self.calls = self.root / "gh-calls.txt"
        self.job_log = self.root / "remote-job-log.txt"
        self.job_log.write_text("2026-10-16T08:48:30.0Z Unity Editor version: 6000.3.26f1\n"
                                "2026-10-16T08:53:30.0Z Build failed, with exit code 1\n")
        self.gh = self.root / "fake-gh"
        self.write_gh(job_id="4242")
        self.env = {
            "PATH": "/usr/bin:/bin",
            "WAYFINDER_GH": str(self.gh),
            "GITHUB_REPOSITORY": "boss-of-python/Wayfinder",
            "GITHUB_RUN_ID": "38875757162",
            "GITHUB_RUN_ATTEMPT": "1",
            "GH_CALLS": str(self.calls),
            "GH_JOB_LOG": str(self.job_log),
        }

    def write_gh(self, job_id="4242", supports_escape_flag=True):
        # `gh api --help` is the capability probe: gh >= 2.63 refuses to print a response that
        # contains terminal escape sequences unless it is asked to (run 39141401948, twelve
        # attempts, no log). The stub answers it the way the real CLI does.
        help_flags = "--allow-escape-sequences" if supports_escape_flag else "--paginate"
        self.gh.write_text(f"""#!/usr/bin/env bash
printf '%s\\n' "$*" >> "$GH_CALLS"
case "$*" in
  *"--help"*) cat <<'HELP'
Flags:
      {help_flags}
HELP
    ;;
  *"/jobs?per_page=100"*)
    case "$*" in
      *'select(.name == "{BUILD_JOB_NAME}")'*) printf '%s\\n' "{job_id}" ;;
      *) exit 0 ;;
    esac ;;
  *"/actions/jobs/{job_id}/logs"*) cat "$GH_JOB_LOG" ;;
  *) exit 1 ;;
esac
""")
        self.gh.chmod(self.gh.stat().st_mode | stat.S_IEXEC)

    def fetch(self, env=None, attempts="2", pause="0"):
        return subprocess.run(
            ["bash", str(FETCH_JOB_LOG), BUILD_JOB_NAME, str(self.root / "Logs/job.txt"),
             attempts, pause],
            capture_output=True, text=True, env=env or self.env)

    def test_downloads_the_named_job_log(self):
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        written = (self.root / "Logs/job.txt").read_text()
        self.assertIn("Build failed, with exit code 1", written)
        self.assertIn("::notice title=Unity job log::", result.stdout)
        self.assertIn("2 lines", result.stdout)

    def test_asks_for_this_run_attempt_only(self):
        env = dict(self.env, GITHUB_RUN_ATTEMPT="3")
        self.fetch(env=env)
        calls = self.calls.read_text()
        self.assertIn("runs/38875757162/attempts/3/jobs", calls)
        self.assertIn(f'select(.name == "{BUILD_JOB_NAME}")', calls)

    def test_unknown_job_name_fails_softly_with_a_notice(self):
        self.write_gh(job_id="")  # stub finds no job with that name
        result = self.fetch()
        self.assertEqual(1, result.returncode)
        self.assertIn("::notice title=Unity job log::No job named", result.stdout)
        self.assertFalse((self.root / "Logs/job.txt").exists())

    def test_empty_log_response_leaves_no_empty_file(self):
        self.job_log.write_text("")
        result = self.fetch()
        self.assertEqual(1, result.returncode)
        self.assertIn("Could not download the log", result.stdout)
        self.assertFalse((self.root / "Logs/job.txt").exists())

    def test_download_failure_reports_gh_error_without_signature(self):
        # Run 38880573866: the job id was found but the log redirect failed, and the only notice
        # said "could not download". The real error must reach the annotation, signature removed.
        self.gh.write_text(self.gh.read_text().replace(
            '*"/actions/jobs/4242/logs"*) cat "$GH_JOB_LOG" ;;',
            '*"/actions/jobs/4242/logs"*) echo \'Get "https://blob.example/job-logs.txt?sig=SECRET&se=1": EOF\' >&2; exit 1 ;;'))
        result = self.fetch(attempts="3")
        self.assertEqual(1, result.returncode)
        self.assertIn("after 3 attempts", result.stdout)
        self.assertIn("EOF", result.stdout)
        self.assertIn("?<redacted>", result.stdout)
        self.assertNotIn("SECRET", result.stdout)
        self.assertFalse((self.root / "Logs/job.txt").exists())
        self.assertEqual(3, self.calls.read_text().count("/actions/jobs/4242/logs"))

    # ---- regressions for run 39141401948: gh refused to print a log with escape sequences ----

    def test_asks_for_escape_sequences_when_the_installed_gh_supports_them(self):
        # The runner's notice, verbatim: "the response contains terminal escape sequences; pass
        # --allow-escape-sequences to output it anyway." Without the flag, all 12 attempts failed.
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("--allow-escape-sequences repos/boss-of-python/Wayfinder/actions/jobs/4242/logs",
                      self.calls.read_text())

    def test_capability_is_probed_not_assumed(self):
        self.fetch()
        self.assertIn("api --help", self.calls.read_text())

    def test_older_gh_without_the_flag_is_not_given_an_unknown_flag(self):
        # An unsupported flag is a hard error ("unknown flag"), so it must not be passed blindly:
        # the sanitiser below makes the download usable either way.
        self.write_gh(supports_escape_flag=False)
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        calls = self.calls.read_text()
        self.assertNotIn("--allow-escape-sequences repos/", calls)
        self.assertIn("has no --allow-escape-sequences flag", result.stdout)

    def test_escape_sequences_and_carriage_returns_are_stripped_from_the_saved_log(self):
        # A Unity job log is coloured by game-ci/Docker and full of \r progress redraws. Left in
        # place they split the very lines the reporter greps for, and GitHub's workflow-command
        # parser rejects ESC in annotation text.
        self.job_log.write_bytes(
            b"2026-10-18T15:52:01.1Z \033[31mRefreshing native access tokens\033[0m\r\n"
            b"2026-10-18T15:58:30.2Z \033[1mAssets/Wayfinder/Scripts/Editor/SceneBuilder.cs"
            b"(88,13): error CS0103: The name 'Bake' does not exist\033[0m\n"
            b"2026-10-18T15:58:39.9Z \033[KAborting batchmode due to failure:\n")
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        written = (self.root / "Logs/job.txt").read_bytes()
        self.assertNotIn(b"\033", written)
        self.assertNotIn(b"\r", written)
        text = written.decode()
        self.assertIn("Assets/Wayfinder/Scripts/Editor/SceneBuilder.cs(88,13): error CS0103: "
                      "The name 'Bake' does not exist", text)
        # ESC[K (erase to end of line) is immediately followed by real text here. A sanitiser
        # whose intermediate-byte class is too wide deletes that text with the sequence, which
        # is how "Aborting batchmode due to failure:" became "orting batchmode due to failure:".
        self.assertIn("Aborting batchmode due to failure:", text)
        # The reporter must be able to annotate straight from the sanitised file.
        report = run(REPORT, self.root, dict(self.env, UNITY_JOB_RESULT="failure"),
                     logs=[self.root / "Logs/job.txt"])
        self.assertIn("::error file=Assets/Wayfinder/Scripts/Editor/SceneBuilder.cs,line=88,col=13",
                      report.stdout)

    def test_a_tail_excerpt_is_saved_next_to_the_log_for_the_uploaded_artefact(self):
        # Annotations carry a few hundred characters; the artefact must carry the end of Unity's
        # own output, which is where the cause of a hard stop usually is.
        self.job_log.write_text("".join(f"line {i}\n" for i in range(1, 901))
                                + "Crash!!! Out of memory\n")
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        excerpt = (self.root / "Logs/job.txt.excerpt").read_text()
        self.assertIn("Crash!!! Out of memory", excerpt)
        self.assertIn("Last 400 lines of job 4242", excerpt)
        self.assertNotIn("line 1\n", excerpt)
        # The verbatim download is not left beside the artefact: a failing Unity job log runs to
        # tens of MB, and only the byte delta it reported is worth keeping.
        self.assertFalse((self.root / "Logs/job.txt.raw").exists())
        self.assertIn("bytes downloaded for diagnosis", result.stdout)

    def test_the_byte_delta_removed_by_sanitising_is_reported(self):
        # If the sanitiser ever ate real log content, the delta is the evidence that says so.
        self.job_log.write_bytes(b"\033[31mred\033[0m plain line\n")
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("Stripped 9 bytes of escape sequences/carriage returns from 24 downloaded.",
                      result.stdout)
        self.assertEqual("red plain line\n", (self.root / "Logs/job.txt").read_text())

    def test_a_log_with_no_escape_sequences_says_so(self):
        result = self.fetch()
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        self.assertIn("no escape sequences to strip", result.stdout)

    def test_a_download_of_only_control_characters_is_not_reported_as_success(self):
        self.job_log.write_bytes(b"\033[0m\r\033[K")
        result = self.fetch(attempts="2")
        self.assertEqual(1, result.returncode)
        self.assertIn("Could not download the log", result.stdout)
        self.assertIn("only control characters", result.stdout)
        self.assertFalse((self.root / "Logs/job.txt").exists())
        self.assertFalse((self.root / "Logs/job.txt.raw").exists())

    def test_an_escape_sequence_refusal_does_not_blame_the_token(self):
        # Runs 39139527909/39141218150/39141401948 printed "Needs actions: read" over exactly this
        # refusal, and the workflow had already granted that permission. The hint must name the
        # thing that actually went wrong, or the next reader investigates a non-problem.
        self.gh.write_text(f"""#!/usr/bin/env bash
case "$*" in
  *"--help"*) printf 'Flags:\\n      --allow-escape-sequences\\n' ;;
  *"/jobs?per_page=100"*) printf '%s\\n' "4242" ;;
  *) echo 'the response contains terminal escape sequences; pass --allow-escape-sequences to output it anyway' >&2; exit 1 ;;
esac
""")
        self.gh.chmod(self.gh.stat().st_mode | stat.S_IEXEC)
        result = self.fetch(attempts="2")
        self.assertEqual(1, result.returncode)
        self.assertNotIn("Needs actions: read", result.stdout)
        self.assertIn("gh refused the body because it contains terminal escape sequences",
                      result.stdout)
        self.assertIn("gh --version", result.stdout)

    def test_a_permission_error_is_still_reported_as_a_permission_problem(self):
        self.gh.write_text("""#!/usr/bin/env bash
case "$*" in
  *"--help"*) printf 'Flags:\\n      --allow-escape-sequences\\n' ;;
  *"/jobs?per_page=100"*) printf '%s\\n' "4242" ;;
  *) echo 'gh: HTTP 403: resource not accessible by integration' >&2; exit 1 ;;
esac
""")
        self.gh.chmod(self.gh.stat().st_mode | stat.S_IEXEC)
        result = self.fetch(attempts="2")
        self.assertEqual(1, result.returncode)
        self.assertIn("Needs `actions: read` on the workflow token", result.stdout)

    def test_an_unrecognised_download_error_points_at_the_actions_tab(self):
        result = self.fetch(attempts="2")  # default stub succeeds; use the failing-redirect stub
        self.assertEqual(0, result.returncode)
        self.gh.write_text(self.gh.read_text().replace(
            '*"/actions/jobs/4242/logs"*) cat "$GH_JOB_LOG" ;;',
            '*"/actions/jobs/4242/logs"*) echo \'dial tcp: i/o timeout\' >&2; exit 1 ;;'))
        result = self.fetch(attempts="2")
        self.assertEqual(1, result.returncode)
        self.assertIn("Open the run in the Actions tab to read the Unity log there.", result.stdout)

    def test_workflow_gives_the_log_a_longer_retry_window(self):
        diagnose = WORKFLOW.read_text()
        self.assertIn("fetch_job_log.sh \"Build Wayfinder-dev.apk\" error_logs/unity-job-log.txt 12 10",
                      diagnose)

    def test_missing_run_context_is_reported_not_crashed(self):
        env = {k: v for k, v in self.env.items() if k != "GITHUB_RUN_ID"}
        result = self.fetch(env=env)
        self.assertEqual(1, result.returncode)
        self.assertIn("cannot download the build job log", result.stdout)

    def test_usage_error_is_distinct(self):
        result = subprocess.run(["bash", str(FETCH_JOB_LOG)], capture_output=True, text=True,
                                env=self.env)
        self.assertEqual(2, result.returncode)
        self.assertIn("usage:", result.stderr)


class CheckUnitySecretsTests(unittest.TestCase):
    def env_with(self, **values):
        env = {"PATH": "/usr/bin:/bin"}
        env.update(values)
        return env

    def test_all_present_passes(self):
        result = run(SECRETS, env=self.env_with(UNITY_LICENSE="lic", UNITY_EMAIL="a@b.c", UNITY_PASSWORD="pw"))
        self.assertEqual(0, result.returncode)
        self.assertIn("Unity license-file authentication is configured", result.stdout)

    def test_account_credentials_pass_without_license_file(self):
        # This reproduces the successful cloud activation in run 38754979851: UNITY_LICENSE was blank.
        result = run(SECRETS, env=self.env_with(UNITY_EMAIL="a@b.c", UNITY_PASSWORD="pw"))
        self.assertEqual(0, result.returncode)
        self.assertIn("Unity account credentials are configured for Personal-license activation", result.stdout)

    def test_license_file_passes_without_account_credentials(self):
        result = run(SECRETS, env=self.env_with(UNITY_LICENSE="license-file-content"))
        self.assertEqual(0, result.returncode)
        self.assertIn("Unity license-file authentication is configured", result.stdout)

    def test_missing_supported_auth_method_fails_with_actionable_message(self):
        result = run(SECRETS, env=self.env_with(UNITY_EMAIL="a@b.c"))
        self.assertEqual(1, result.returncode)
        self.assertIn("Provide either UNITY_LICENSE or both UNITY_EMAIL and UNITY_PASSWORD", result.stdout)
        self.assertIn("Missing account secret(s): UNITY_PASSWORD", result.stdout)

    def test_padded_secret_warns_but_passes(self):
        result = run(SECRETS, env=self.env_with(UNITY_LICENSE=" lic\n", UNITY_EMAIL="a@b.c", UNITY_PASSWORD="pw"))
        self.assertEqual(0, result.returncode)
        self.assertIn("::warning title=Unity secrets::Leading/trailing whitespace in: UNITY_LICENSE", result.stdout)

    def test_never_prints_a_secret_value(self):
        result = run(SECRETS, env=self.env_with(UNITY_LICENSE="SUPER-SECRET-LICENSE",
                                                UNITY_EMAIL="owner@example.com",
                                                UNITY_PASSWORD="SUPER-SECRET-PASSWORD"))
        combined = result.stdout + result.stderr
        self.assertNotIn("SUPER-SECRET-LICENSE", combined)
        self.assertNotIn("SUPER-SECRET-PASSWORD", combined)

    def test_padded_value_is_not_echoed_in_the_warning(self):
        result = run(SECRETS, env=self.env_with(UNITY_LICENSE="  SUPER-SECRET-LICENSE  ",
                                                UNITY_EMAIL="a@b.c", UNITY_PASSWORD="pw"))
        self.assertNotIn("SUPER-SECRET-LICENSE", result.stdout)


class EditorEntryPointTests(unittest.TestCase):
    """The -executeMethod entry points must leave a readable reason behind (D-035).

    Unity's editor log lives on the build container's stdout, so an exception thrown by
    ProjectSetup.Setup leaves nothing in the workspace. EditorFailureLog writes one grep-able
    line that report_unity_failure.sh turns into an annotation - these assertions keep the C#
    marker and the shell pattern from drifting apart. They are structural (no Unity here).
    """

    EDITOR = TOOLS.parent.parent / "Assets/Wayfinder/Scripts/Editor"

    def test_cloud_build_entry_points_record_failures_before_rethrowing(self):
        setup = (self.EDITOR / "ProjectSetup.cs").read_text()
        build = (self.EDITOR / "BuildCommands.cs").read_text()
        self.assertIn('EditorFailureLog.Run("Wayfinder.Editor.ProjectSetup.Setup", RunSetup)', setup)
        self.assertIn('EditorFailureLog.Run("Wayfinder.Editor.BuildCommands.BuildAndroidDev"', build)

    def test_the_breadcrumb_marker_is_exactly_what_the_reporter_greps_for(self):
        source = (self.EDITOR / "EditorFailureLog.cs").read_text()
        self.assertIn('Marker = "[Wayfinder] Editor method failed: "', source)
        self.assertIn('LogPath = LogDirectory + "/wayfinder-editor-failure.log"', source)
        # The same text, escaped for grep, is what the reporter looks for.
        self.assertIn(r"\[Wayfinder\] Editor method failed: ", REPORT.read_text())

    def test_the_breadcrumb_rethrows_so_unity_still_fails_the_build(self):
        source = (self.EDITOR / "EditorFailureLog.cs").read_text()
        self.assertIn("Record(method, error);\n                throw;", source)


class CloudBuildWorkflowTests(unittest.TestCase):
    def setUp(self):
        self.text = WORKFLOW.read_text()

    def test_only_one_cloud_build_workflow_can_be_dispatched(self):
        workflows = sorted((TOOLS.parent.parent / ".github/workflows").glob("android-cloud-build*.yml"))
        self.assertEqual([WORKFLOW], workflows,
                         "duplicate manual workflows with the same display name make it easy to "
                         "dispatch a stale build without diagnostics")

    def step_block(self, name):
        """The YAML for exactly one step: from its name up to (not including) the next step."""
        start = self.text.index(f"- name: {name}")
        rest = self.text[start + len(f"- name: {name}"):]
        end = rest.index("- name: ")
        return self.text[start:start + len(f"- name: {name}") + end]

    def test_diagnostics_run_after_unity_attempts_and_cannot_change_the_verdict(self):
        step = self.step_block("Report the real Unity failure as annotations")
        self.assertIn(
            "if: ${{ always() && (steps.unity_setup.outcome == 'success' || "
            "steps.unity_setup.outcome == 'failure' || steps.unity_setup.outcome == 'cancelled' || "
            "steps.unity_build.outcome == 'success' || steps.unity_build.outcome == 'failure' || "
            "steps.unity_build.outcome == 'cancelled') }}",
            step,
        )
        self.assertIn("continue-on-error: true", step)
        self.assertIn("bash Tools/Build/report_unity_failure.sh", step)
        setup = self.step_block('"Unity 1 of 2: Setup Project (scenes, URP, player settings)"')
        build = self.step_block('"Unity 2 of 2: Build the development APK"')
        self.assertIn("id: unity_setup", setup)
        self.assertIn("id: unity_build", build)

    def test_log_upload_step_also_always_runs_and_cannot_change_the_verdict(self):
        step = self.step_block("Upload Unity logs and the resolved package lock (for offline diagnosis)")
        self.assertIn("if: always()", step)
        self.assertIn("continue-on-error: true", step)

    def test_auth_preflight_passes_all_auth_options_without_printing_values(self):
        step = self.step_block("Check Unity authentication secrets exist (values are never printed)")
        for secret in ["UNITY_LICENSE", "UNITY_EMAIL", "UNITY_PASSWORD"]:
            self.assertIn(f"{secret}: ${{{{ secrets.{secret} }}}}", step)
        self.assertIn("bash Tools/Build/check_unity_secrets.sh", step)
        # It must not echo them into a later step's environment or the log.
        self.assertNotIn("echo \"$UNITY", step)

    def test_preflight_runs_before_expensive_runner_setup(self):
        checkout = self.text.index("- name: Check out the code")
        secret_check = self.text.index("- name: Check Unity authentication secrets exist")
        pin = self.text.index("- name: Pin the Unity version for this build only")
        disk_cleanup = self.text.index("- name: Free disk space")
        cache = self.text.index("- name: Reuse Unity's import cache from earlier runs")
        unity_setup = self.text.index("- name: \"Unity 1 of 2")
        self.assertLess(checkout, secret_check)
        self.assertLess(secret_check, pin)
        self.assertLess(pin, disk_cleanup)
        self.assertLess(disk_cleanup, cache)
        self.assertLess(cache, unity_setup)

    def test_both_unity_steps_still_target_the_verified_editor_and_methods(self):
        self.assertIn("UNITY_VERSION: 6000.3.26f1", self.text)
        self.assertIn("buildMethod: Wayfinder.Editor.ProjectSetup.Setup", self.text)
        self.assertIn("buildMethod: Wayfinder.Editor.BuildCommands.BuildAndroidDev", self.text)

    def test_cold_cache_and_unity_log_directories_are_prepared_before_cache(self):
        prepare = self.text.index("- name: Prepare Unity cache and diagnostic log folders")
        cache = self.text.index("- name: Reuse Unity's import cache from earlier runs")
        self.assertLess(prepare, cache)
        self.assertIn("mkdir -p Library Logs Builds/Logs", self.text[prepare:cache])
        self.assertIn("hashFiles('Assets/**', 'Packages/**', 'ProjectSettings/**')", self.text)

    def test_error_log_directory_and_file_are_created_before_preflight(self):
        prepare = self.step_block("Pre-create APK error log")
        secrets = self.text.index("- name: Check Unity authentication secrets exist")
        self.assertLess(self.text.index("- name: Pre-create APK error log"), secrets)
        self.assertIn("mkdir -p error_logs", prepare)
        self.assertIn("error_logs/apk-build-errors.log", prepare)
        self.assertIn("GITHUB_RUN_ID", prepare)
        self.assertIn("GITHUB_SHA", prepare)
        reporter = self.step_block("Report the real Unity failure as annotations")
        self.assertIn("tee -a error_logs/apk-build-errors.log", reporter)
        root = TOOLS.parent.parent
        self.assertTrue((root / "error_logs/.gitkeep").exists())
        ignore = (root / ".gitignore").read_text()
        self.assertIn("/error_logs/*", ignore)
        self.assertIn("!/error_logs/.gitkeep", ignore)

    def job_block(self, name):
        """The YAML for exactly one job: from its key up to (not including) the next job key."""
        start = self.text.index(f"\n  {name}:\n")
        rest = self.text[start + 1:]
        ends = [rest.index(f"\n  {other}:\n") for other in ("apk", "diagnose")
                if f"\n  {other}:\n" in rest]
        return rest[:min(ends)] if ends else rest

    def test_no_dead_logfile_override_is_left_in_the_unity_steps(self):
        # game-ci's build.sh runs `unity-editor -logfile /dev/stdout ... $CUSTOM_PARAMETERS`;
        # run 38875757162 proved the override produces no readable editor log, so keeping it
        # only advertises a diagnostic path that does not exist.
        settings = "\n".join(line.split("#")[0] for line in self.text.splitlines())
        self.assertNotIn("customParameters:", settings)
        self.assertNotIn("-logFile /github/workspace", settings)

    def test_the_unity_log_upload_still_collects_the_workspace_logs(self):
        artifact = self.step_block("Upload Unity logs and the resolved package lock (for offline diagnosis)")
        self.assertIn("Logs/", artifact)
        self.assertIn("Builds/Logs/", artifact)
        self.assertIn("error_logs/", artifact)

    def test_in_job_diagnostics_are_told_the_unity_step_outcomes(self):
        # Without the outcomes, a failure whose log says nothing looks exactly like a success.
        step = self.step_block("Report the real Unity failure as annotations")
        self.assertIn("UNITY_SETUP_OUTCOME: ${{ steps.unity_setup.outcome }}", step)
        self.assertIn("UNITY_BUILD_OUTCOME: ${{ steps.unity_build.outcome }}", step)

    def test_a_second_job_reads_the_finished_build_job_log_and_annotates_it(self):
        diagnose = self.job_block("diagnose")
        self.assertIn("needs: apk", diagnose)
        self.assertIn("if: always()", diagnose)
        self.assertIn("actions: read", diagnose)  # GET .../actions/jobs/{id}/logs
        self.assertIn("GH_TOKEN: ${{ github.token }}", diagnose)
        self.assertIn(f'bash Tools/Build/fetch_job_log.sh "{BUILD_JOB_NAME}" error_logs/unity-job-log.txt',
                      diagnose)
        self.assertIn('bash Tools/Build/report_unity_failure.sh "$PWD" error_logs/unity-job-log.txt',
                      diagnose)
        self.assertIn("tee -a error_logs/apk-build-errors.log", diagnose)
        self.assertIn("error_logs/job-log-download.txt", diagnose)
        self.assertIn("grep -Ein", diagnose)
        self.assertIn("name: Wayfinder-APK-error-logs", diagnose)
        self.assertIn("path: error_logs/", diagnose)
        # The excerpt tail lives next to the fetched log, so it rides up in the same artefact:
        # annotations carry a few hundred characters, the artefact carries the end of Unity's log.
        self.assertIn("error_logs/unity-job-log.txt.excerpt", diagnose)
        self.assertIn("UNITY_JOB_RESULT: ${{ needs.apk.result }}", diagnose)
        # The job-level guard prevents checkout/setup/upload failures in this reporting-only job
        # from failing the workflow; its four failure-prone steps also keep running independently.
        self.assertIn("    continue-on-error: true\n    runs-on: ubuntu-24.04", diagnose)
        self.assertEqual(4, diagnose.count("        continue-on-error: true"))

    def test_the_error_line_grep_covers_a_hard_stop_and_stays_bounded(self):
        step = self.step_block("Append Unity error lines to the APK error log")
        # A crash or a kill leaves no compile error and no exception, so the raw grep has to
        # carry those alternatives or the downloadable report says nothing about them.
        for pattern in ["Crash!!!", "Native stacktrace", "Fatal Error!", "Out of memory",
                        "Killed process", "No space left on device", "Licensing::Module"]:
            self.assertIn(pattern, step)
        # A failed Unity run emits tens of thousands of shader-compiler lines, and this text is
        # also tee'd into the job summary, so it is capped per line and in total.
        self.assertIn("| head -400 | cut -c1-400", step)

    def test_the_report_file_carries_the_end_of_the_unity_log(self):
        step = self.step_block("Append Unity error lines to the APK error log")
        self.assertIn("Last 400 lines of the Unity job log", step)
        self.assertIn("cat error_logs/unity-job-log.txt.excerpt", step)

    def test_the_diagnosis_job_downloads_the_job_it_is_named_after(self):
        # A rename of the build job would silently make the diagnosis download nothing.
        self.assertIn(f"name: {BUILD_JOB_NAME}", self.job_block("apk"))

    def test_runner_image_is_pinned_so_the_ubuntu_26_migration_cannot_surprise_a_build(self):
        # Annotation on run 38875757162: "The ubuntu-latest label will migrate to Ubuntu 26
        # beginning October 19, 2026."
        runners = [line.strip() for line in self.text.splitlines() if line.strip().startswith("runs-on:")]
        self.assertEqual(["runs-on: ubuntu-24.04", "runs-on: ubuntu-24.04"], runners)

    def test_action_pins_are_unchanged(self):
        for pin in ["jlumbroso/free-disk-space@ceedf095f4ec1a097402bc6bd80831f2e1a6fde6",
                    "actions/checkout@3d3c42e5aac5ba805825da76410c181273ba90b1",
                    "actions/cache@55cc8345863c7cc4c66a329aec7e433d2d1c52a9",
                    "game-ci/unity-builder@eb1b9fba120c6e62c9fb7a7a81d6c107ce004c45",
                    "actions/upload-artifact@cf430e030ddbb5b0abf93d22962f4752f3646cd9"]:
            self.assertIn(pin, self.text)

    def test_resolved_package_lock_is_uploaded_so_it_can_be_committed(self):
        step = self.step_block("Upload Unity logs and the resolved package lock (for offline diagnosis)")
        self.assertIn("Packages/packages-lock.json", step)

    def test_apk_upload_still_fails_closed(self):
        index = self.text.index("- name: Upload the APK")
        self.assertIn("if-no-files-found: error", self.text[index:index + 400])

    def test_manual_trigger_and_read_only_permissions(self):
        self.assertIn("workflow_dispatch", self.text)
        self.assertIn("permissions:\n  contents: read", self.text)

    def test_input_system_pin_is_the_verified_compatible_one(self):
        manifest = (TOOLS.parent.parent / "Packages/manifest.json").read_text()
        # 1.12.0/1.13.0 reference BuildTarget.ReservedCFE, which Unity 6000.3.26f1 removed;
        # the fix landed in 1.13.1 (verified against the upstream package source).
        self.assertIn('"com.unity.inputsystem": "1.20.1"', manifest)
        self.assertNotIn('"com.unity.inputsystem": "1.12.0"', manifest)

    def test_project_version_pin_is_committed_and_matches_the_workflow(self):
        version = (TOOLS.parent.parent / "ProjectSettings/ProjectVersion.txt").read_text()
        self.assertIn("m_EditorVersion: 6000.3.26f1", version)
        self.assertIn("m_EditorVersionWithRevision: 6000.3.26f1 (de8167e7440c)", version)


if __name__ == "__main__":
    unittest.main()
