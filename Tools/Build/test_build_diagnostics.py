"""Contract tests for the cloud-build diagnostics helpers.

These run the real shell scripts against fixtures, including the verbatim log lines from the
failed cloud build of 2026-10-15 (run 38754979851, Unity 6000.3.26f1): that run aborted with
"Build failed with exit code 1" and the only cause in 1,996 log lines was a compile error
inside Unity's own Input System package. The scripts must turn that into an annotation that is
readable from the GitHub API, because Unity's exit code is not descriptive.

Stdlib only (no PyYAML): the workflow checks below are structural text assertions.
"""
from pathlib import Path
import subprocess
import tempfile
import unittest

TOOLS = Path(__file__).resolve().parent
REPORT = TOOLS / "report_unity_failure.sh"
SECRETS = TOOLS / "check_unity_secrets.sh"
WORKFLOW = TOOLS.parent.parent / ".github/workflows/android-cloud-build.yml"

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


class CheckUnitySecretsTests(unittest.TestCase):
    def env_with(self, **values):
        env = {"PATH": "/usr/bin:/bin"}
        env.update(values)
        return env

    def test_all_present_passes(self):
        result = run(SECRETS, env=self.env_with(UNITY_LICENSE="lic", UNITY_EMAIL="a@b.c", UNITY_PASSWORD="pw"))
        self.assertEqual(0, result.returncode)
        self.assertIn("All three Unity license secrets are present", result.stdout)

    def test_missing_secret_names_it_and_fails(self):
        result = run(SECRETS, env=self.env_with(UNITY_EMAIL="a@b.c", UNITY_PASSWORD="pw"))
        self.assertEqual(1, result.returncode)
        self.assertIn("::error title=Unity secrets missing::Missing repository secret(s): UNITY_LICENSE",
                      result.stdout)

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

    def test_secret_check_step_passes_the_three_secrets_and_nothing_else(self):
        step = self.step_block("Check the Unity license secrets exist (values are never printed)")
        for secret in ["UNITY_LICENSE", "UNITY_EMAIL", "UNITY_PASSWORD"]:
            self.assertIn(f"{secret}: ${{{{ secrets.{secret} }}}}", step)
        self.assertIn("bash Tools/Build/check_unity_secrets.sh", step)
        # It must not echo them into a later step's environment or the log.
        self.assertNotIn("echo \"$UNITY", step)

    def test_preflight_runs_before_expensive_runner_setup(self):
        checkout = self.text.index("- name: Check out the code")
        secret_check = self.text.index("- name: Check the Unity license secrets exist")
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

    def test_each_unity_step_writes_its_full_log_for_diagnostics_and_artifact_upload(self):
        setup = self.step_block('"Unity 1 of 2: Setup Project (scenes, URP, player settings)"')
        build = self.step_block('"Unity 2 of 2: Build the development APK"')
        self.assertIn('customParameters: "-logFile /github/workspace/Logs/Unity-Setup.log"', setup)
        self.assertIn('customParameters: "-logFile /github/workspace/Logs/Unity-Android-Dev.log"', build)
        artifact = self.step_block("Upload Unity logs and the resolved package lock (for offline diagnosis)")
        self.assertIn("Logs/", artifact)

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
