"""Runner contract tests using mocks/files, not evidence of a Unity run."""
import importlib.util
import json
from pathlib import Path
import subprocess
import tempfile
import unittest
from unittest.mock import patch
import zipfile

SPEC = importlib.util.spec_from_file_location("unity_pipeline", Path(__file__).with_name("unity_pipeline.py"))
pipeline = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(pipeline)


class UnityPipelineTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.root = Path(self.temp.name)
        (self.root / "Packages").mkdir()
        (self.root / "Packages/manifest.json").write_text(json.dumps({"dependencies": {}}))
        self.unity = self.root / "Unity"
        self.unity.touch()

    def test_explicit_missing_editor_fails(self):
        with self.assertRaises(pipeline.PipelineError):
            pipeline.find_unity(str(self.root / "missing"))

    def test_explicit_editor_found(self):
        self.assertEqual(self.unity, pipeline.find_unity(str(self.unity)))

    def test_reads_actual_editor_version(self):
        with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], 0, "6000.3.2f1\n", "")):
            self.assertEqual("6000.3.2f1", pipeline.editor_version(self.unity))

    def test_rejects_other_editor_or_failed_version_query(self):
        for output, code in [("6000.2.1f1", 0), ("6000.3.1b1", 0), ("6000.3.2f1", 1)]:
            with self.subTest(output=output, code=code):
                with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], code, output, "")):
                    with self.assertRaises(pipeline.PipelineError):
                        pipeline.editor_version(self.unity)

    def test_doctor_check_does_not_pin(self):
        pipeline.check_project(self.root, "6000.3.2f1")
        self.assertFalse((self.root / "ProjectSettings/ProjectVersion.txt").exists())

    def test_setup_pins_without_invented_revision_hash(self):
        pipeline.check_project(self.root, "6000.3.2f1", pin=True)
        self.assertEqual("m_EditorVersion: 6000.3.2f1\n", (self.root / "ProjectSettings/ProjectVersion.txt").read_text())

    def test_never_silently_overwrites_pin(self):
        pipeline.check_project(self.root, "6000.3.2f1", pin=True)
        with self.assertRaises(pipeline.PipelineError):
            pipeline.check_project(self.root, "6000.3.3f1", pin=True)

    def test_missing_and_invalid_manifest_fail(self):
        path = self.root / "Packages/manifest.json"
        for content in ["not json", None]:
            if content is None:
                path.unlink()
            else:
                path.write_text(content)
            with self.assertRaises(pipeline.PipelineError):
                pipeline.check_project(self.root, "6000.3.2f1", pin=True)
        self.assertFalse((self.root / "ProjectSettings/ProjectVersion.txt").exists())

    def test_test_command_has_no_premature_quit(self):
        log = self.root / "EditMode.log"
        command = pipeline.command(self.unity, self.root, log, test_platform="EditMode")
        self.assertNotIn("-quit", command)
        self.assertNotIn("-executeMethod", command)
        self.assertIn("-runTests", command)
        self.assertIn(str(log.with_suffix(".xml")), command)
        self.assertEqual("Android", command[command.index("-buildTarget") + 1])

    def test_build_command_has_method_and_no_secrets(self):
        command = pipeline.command(self.unity, self.root, self.root / "build.log", method=pipeline.METHODS["android-release"])
        self.assertIn("-quit", command)
        self.assertIn("Wayfinder.Editor.BuildCommands.BuildAndroidRelease", command)
        self.assertNotIn("-runTests", command)
        self.assertNotIn("WAYFINDER_KEYSTORE_PASS", " ".join(command))

    def test_result_gate_rejects_missing_empty_failed_and_corrupt(self):
        path = self.root / "result.xml"
        for xml in [None, "broken", '<test-run result="Passed" total="0" failed="0"/>', '<test-run result="Failed" total="3" failed="1"/>', '<test-run result="Passed" total="3" failed="1"/>']:
            with self.subTest(xml=xml):
                if xml is not None:
                    path.write_text(xml)
                with self.assertRaises(pipeline.PipelineError):
                    pipeline.verify_tests(path)

    def test_result_gate_accepts_nonzero_pass(self):
        path = self.root / "result.xml"
        path.write_text('<test-run result="Passed" total="3" passed="3" failed="0"/>')
        pipeline.verify_tests(path)

    def test_bundle_and_apk_have_distinct_manifests(self):
        path = self.root / "build.aab"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("base/manifest/AndroidManifest.xml", "binary manifest placeholder")
        pipeline.verify_artifact(path, bundle=True)
        with self.assertRaises(pipeline.PipelineError):
            pipeline.verify_artifact(path, bundle=False)

    def test_invalid_artifact_rejected(self):
        path = self.root / "build.apk"
        path.write_text("not an apk")
        with self.assertRaises(pipeline.PipelineError):
            pipeline.verify_artifact(path, bundle=False)

    def test_nonzero_exit_stops_pipeline(self):
        with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], 1)):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "android-dev", 10)

    def test_no_artifact_on_zero_exit_is_failure(self):
        with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "android-dev", 10)

    def test_stale_artifact_on_zero_exit_is_failure(self):
        path = self.root / pipeline.ARTIFACTS["android-dev"]
        path.parent.mkdir(parents=True)
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("AndroidManifest.xml", "placeholder")
        with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "android-dev", 10)

    def test_stale_test_xml_cannot_report_pass(self):
        path = self.root / "Logs/unity-pipeline/EditMode.xml"
        path.parent.mkdir(parents=True)
        path.write_text('<test-run result="Passed" total="3" failed="0"/>')
        with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "EditMode", 10)
        self.assertFalse(path.exists())

    def test_timeout_fails(self):
        with patch.object(pipeline.subprocess, "run", side_effect=subprocess.TimeoutExpired("Unity", 10)):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "setup", 10)

    def test_setup_requires_fresh_completion_marker(self):
        bootstrap = self.root / "Assets/Wayfinder/Scenes/Bootstrap.unity"
        bootstrap.parent.mkdir(parents=True)
        bootstrap.touch()
        with patch.object(pipeline.subprocess, "run", return_value=subprocess.CompletedProcess([], 0)):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "setup", 10)

    def test_setup_requires_generated_outputs_and_package_lock(self):
        def fake_unity(*args, **kwargs):
            path = self.root / "Logs/unity-pipeline/setup-complete.json"
            path.write_text('{"editorVersion":"6000.3.2f1"}')
            return subprocess.CompletedProcess([], 0)
        with patch.object(pipeline.subprocess, "run", side_effect=fake_unity):
            with self.assertRaises(pipeline.PipelineError):
                pipeline.run_step(self.unity, self.root, "setup", 10)

    def test_complete_setup_outputs_are_accepted(self):
        def fake_unity(*args, **kwargs):
            for relative in ("Logs/unity-pipeline/setup-complete.json", "Assets/Wayfinder/Scenes/Bootstrap.unity", "Assets/Wayfinder/Resources/WayfinderControls.json", "Packages/packages-lock.json"):
                path = self.root / relative
                path.parent.mkdir(parents=True, exist_ok=True)
                path.write_text("{}")
            return subprocess.CompletedProcess([], 0)
        with patch.object(pipeline.subprocess, "run", side_effect=fake_unity):
            pipeline.run_step(self.unity, self.root, "setup", 10)

    def test_new_bundle_is_accepted(self):
        def fake_unity(*args, **kwargs):
            path = self.root / pipeline.ARTIFACTS["android-release"]
            path.parent.mkdir(parents=True, exist_ok=True)
            with zipfile.ZipFile(path, "w") as archive:
                archive.writestr("base/manifest/AndroidManifest.xml", "placeholder")
            return subprocess.CompletedProcess([], 0)
        with patch.object(pipeline.subprocess, "run", side_effect=fake_unity):
            pipeline.run_step(self.unity, self.root, "android-release", 10)


if __name__ == "__main__":
    unittest.main()
