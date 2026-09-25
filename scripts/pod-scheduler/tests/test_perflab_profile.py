import json
import os
from pathlib import Path
import re
import shutil
import subprocess
import sys
import textwrap
import unittest
import uuid


_HERE = Path(__file__).resolve().parent
_REPO = _HERE.parents[2]
_PROFILE = _REPO / "build" / "perflab.profile.yml"


def _profile():
    text = _PROFILE.read_text(encoding="utf-8")
    variables = dict(re.findall(r"^  (\w+): ([^\n]+)$", text, re.MULTILINE))
    variables = {name: value.strip('"') for name, value in variables.items()}
    commands = []
    for block in text.split("    - condition: ")[1:]:
        condition, body = block.split("\n", 1)
        script_type = re.search(r"scriptType: (\w+)", body).group(1)
        script = body.split("      script: ", 1)[1]
        if script.startswith("|\n"):
            script = textwrap.dedent(script[2:])
        else:
            script = script.rstrip("\n")
        commands.append({
            "condition": json.loads(condition),
            "scriptType": script_type,
            "script": script,
        })
    return text, variables, commands


def _render(script, variables, script_type):
    replacement = "''" if script_type == "powershell" else "'\\''"
    expected_filter = (
        '''replace: "'", "''"'''
        if script_type == "powershell"
        else '''replace: "'", "'\\\\''"'''
    )

    def expand(match):
        expression = match.group(1)
        name, filter_expression = expression.split(" | ", 1)
        if filter_expression != expected_filter:
            raise AssertionError(f"Unexpected shell quoting: {expression}")
        return variables[name].replace("'", replacement)

    return re.sub(r"{{ (.*?) }}", expand, script)


class TestPerfLabProfile(unittest.TestCase):
    def setUp(self):
        self.text, self.variables, self.commands = _profile()
        self.root = _REPO / "artifacts" / ("perflab-profile-test-" + uuid.uuid4().hex)
        self.attempt = self.root / "attempt"
        self.scripts = self.root / "scripts"
        self.attempt.mkdir(parents=True)
        self.scripts.mkdir()
        self.addCleanup(shutil.rmtree, self.root)
        self.fixture = {"Jobs": {"application": {"Results": {"requests": 123}}}}
        (self.attempt / "crank-results.json").write_text(json.dumps(self.fixture))
        self.variables.update({
            "perfLabCounterPolicy": "policy ' $(echo injected) `echo injected`; & file.json",
            "perfLabStorageAccount": "customaccount",
            "perfLabContainer": "customcontainer",
            "perfLabResultsQueue": "customqueue",
            "crankVersionEnvironmentVariable": "CUSTOM_VERSION",
            "perfLabTenantIdEnvironmentVariable": "CUSTOM_TENANT",
            "perfLabClientIdEnvironmentVariable": "CUSTOM_CLIENT",
            "perfLabCertificateBase64EnvironmentVariable": "CUSTOM_CERT",
            "perfLabCertificatePasswordEnvironmentVariable": "CUSTOM_PASSWORD",
        })

    def test_only_application_gets_one_hook_and_publication_defaults_off(self):
        self.assertIn(
            "profiles:\n  perflab:\n    agents:\n      application:\n"
            "        afterJob:\n          - perflab-export\n",
            self.text,
        )
        self.assertEqual("false", self.variables["perfLabPublication"])
        self.assertEqual(1, self.text.count("afterJob:"))
        self.assertEqual(4, len(self.commands))
        self.assertEqual(2, self.text.count("continueOnError: false"))
        self.assertNotIn("job.operatingSystem", self.text)
        for command in self.commands:
            self.assertIn("job.environment.platform", command["condition"])
            self.assertIn("result.returnCode", command["condition"])
            self.assertIn("result.jobResults.jobs.Count", command["condition"])
        for command in self.commands[:2]:
            self.assertIn(
                "'{{ perfLabPublication }}' != 'true' || "
                "result.returnCode != 0 || result.jobResults.jobs.Count == 0",
                command["condition"],
            )
        for command in self.commands[2:]:
            self.assertIn(
                "'{{ perfLabPublication }}' == 'true' && "
                "result.returnCode == 0 && result.jobResults.jobs.Count > 0",
                command["condition"],
            )

    def _run_shell_cases(self, script_type, executable):
        if not executable:
            self.skipTest(f"{script_type} unavailable")
        environment = os.environ.copy()
        environment.update({
            "PERFLAB_TEST_PYTHON": sys.executable,
            "PERFLAB_TEST_EXIT": "0",
        })
        if script_type == "powershell":
            fake = self.scripts / "fake exporter.ps1"
            fake.write_text(
                "$ErrorActionPreference = 'Stop'\n"
                "$fixture = Get-Content 'crank-results.json' -Raw | ConvertFrom-Json\n"
                "@{ arguments = @($args); fixture = $fixture } | "
                "ConvertTo-Json -Depth 10 | Set-Content 'invocation.json'\n"
                "Write-Host 'fake export invoked'\n"
                "exit ([int]$env:PERFLAB_TEST_EXIT)\n",
                encoding="utf-8",
            )
            shell_arguments = [
                executable, "-NoProfile", "-ExecutionPolicy", "Bypass", "-File",
            ]
            extension = ".ps1"
        else:
            fake_python = self.scripts / "fake_exporter.py"
            fake_python.write_text(
                "import json, os, sys\n"
                "from pathlib import Path\n"
                "Path('invocation.json').write_text(json.dumps({\n"
                "    'arguments': sys.argv[1:],\n"
                "    'fixture': json.loads(Path('crank-results.json').read_text())\n"
                "}))\n"
                "print('fake export invoked')\n"
                "sys.exit(int(os.environ['PERFLAB_TEST_EXIT']))\n"
            )
            fake = self.scripts / "fake exporter.sh"
            fake.write_text(
                '#!/bin/sh\nexec "$PERFLAB_TEST_PYTHON" '
                '"$PERFLAB_TEST_EXPORTER" "$@"\n',
                newline="\n",
            )
            fake.chmod(0o755)
            environment["PERFLAB_TEST_EXPORTER"] = str(fake_python)
            shell_arguments = [executable]
            extension = ".sh"
        environment["CRANK_AZDO_POST_PROCESS_EXECUTABLE"] = str(fake)
        command = next(
            command for command in self.commands[2:]
            if command["scriptType"] == script_type
        )
        script = self.scripts / ("export" + extension)
        script.write_text(
            _render(command["script"], self.variables, script_type),
            encoding="utf-8", newline="\n",
        )
        for exit_code in (0, 23):
            with self.subTest(exit_code=exit_code):
                environment["PERFLAB_TEST_EXIT"] = str(exit_code)
                result = subprocess.run(
                    shell_arguments + [str(script)], cwd=self.attempt,
                    env=environment, capture_output=True, text=True, timeout=30,
                )
                self.assertEqual(exit_code, result.returncode, result.stderr)
                self.assertIn("fake export invoked", result.stdout)
                invocation = json.loads(
                    (self.attempt / "invocation.json").read_text(encoding="utf-8-sig")
                )
                self.assertEqual(self.fixture, invocation["fixture"])
                arguments = invocation["arguments"]
                self.assertEqual("upload", arguments[0])
                self.assertEqual(27, len(arguments))
                options = dict(zip(arguments[1::2], arguments[2::2]))
                self.assertEqual("crank-results.json", options["--crank-json"])
                self.assertEqual("crank", options["--identity-source"])
                self.assertEqual("certificate", options["--storage-authentication"])
                for option, variable in [
                    ("counter-policy", "perfLabCounterPolicy"),
                    ("storage-account", "perfLabStorageAccount"),
                    ("container", "perfLabContainer"),
                    ("queue", "perfLabResultsQueue"),
                    ("crank-version-environment-variable", "crankVersionEnvironmentVariable"),
                    ("tenant-id-environment-variable", "perfLabTenantIdEnvironmentVariable"),
                    ("client-id-environment-variable", "perfLabClientIdEnvironmentVariable"),
                    ("certificate-base64-environment-variable",
                     "perfLabCertificateBase64EnvironmentVariable"),
                    ("certificate-password-environment-variable",
                     "perfLabCertificatePasswordEnvironmentVariable"),
                ]:
                    self.assertEqual(self.variables[variable], options["--" + option])
        (self.attempt / "invocation.json").unlink()
        environment.pop("CRANK_AZDO_POST_PROCESS_EXECUTABLE")
        result = subprocess.run(
            shell_arguments + [str(script)], cwd=self.attempt,
            env=environment, capture_output=True, text=True, timeout=30,
        )
        self.assertNotEqual(0, result.returncode)
        self.assertFalse((self.attempt / "invocation.json").exists())

        skip = next(
            command for command in self.commands[:2]
            if command["scriptType"] == script_type
        )
        script.write_text(skip["script"], encoding="utf-8", newline="\n")
        result = subprocess.run(
            shell_arguments + [str(script)], cwd=self.attempt,
            env=environment, capture_output=True, text=True, timeout=30,
        )
        self.assertEqual(0, result.returncode, result.stderr)
        self.assertIn("Skipping PerfLab export", result.stdout)
        self.assertFalse((self.attempt / "invocation.json").exists())

    def test_powershell_export_success_failure_skip_and_quoting(self):
        self._run_shell_cases("powershell", shutil.which("powershell"))

    def test_bash_export_success_failure_skip_and_quoting(self):
        bash = shutil.which("bash")
        if os.name == "nt":
            git_bash = Path(os.environ.get("ProgramFiles", "")) / "Git" / "bin" / "bash.exe"
            bash = str(git_bash) if git_bash.exists() else None
        self._run_shell_cases("bash", bash)

    @unittest.skipUnless(
        os.environ.get("CRANK_CONTROLLER_DLL") and shutil.which("pwsh"),
        "Set CRANK_CONTROLLER_DLL to validate with a locally built Controller",
    )
    def test_actual_controller_rendering_and_jint_guards(self):
        variables = self.root / "variables.json"
        variables.write_text(json.dumps(self.variables))
        output = self.root / "rendered.json"
        result = subprocess.run(
            [
                shutil.which("pwsh"), "-NoProfile", "-File",
                str(_HERE / "verify_perflab_controller.ps1"),
                "-ControllerPath", os.environ["CRANK_CONTROLLER_DLL"],
                "-OutputPath", str(output), "-VariablesPath", str(variables),
            ],
            cwd=_REPO, capture_output=True, text=True, timeout=60,
        )
        self.assertEqual(0, result.returncode, result.stdout + result.stderr)
        rendered = json.loads(output.read_text(encoding="utf-8-sig"))
        for enabled in ("false", "true"):
            self.assertEqual(4, len(rendered[enabled]))
            for source, actual in zip(self.commands, rendered[enabled]):
                self.assertEqual(
                    _render(source["script"], self.variables, source["scriptType"]),
                    actual["Script"],
                )
                self.assertFalse(actual["ContinueOnError"])
                self.assertNotIn("{{", actual["Condition"])


if __name__ == "__main__":
    unittest.main()
