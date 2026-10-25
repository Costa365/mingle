"""Unit tests of verifier orchestration; real Docker/browser coverage runs separately."""

import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile
import unittest

ROOT = Path(__file__).resolve().parents[2]
DOCKER = r'''#!/usr/bin/env python3
import json, os, pathlib, sqlite3, sys
args = sys.argv[1:]
with open(os.environ['CALLS'], 'a') as log:
    log.write(json.dumps(args) + '\n')
if args[0] == 'cp':
    source, destination = args[1:]
    target = pathlib.Path(destination)
    if source.endswith('/keys'):
        target.mkdir()
        (target / 'fixture.xml').write_text('synthetic fixture, not a credential')
    else:
        with sqlite3.connect(target) as db:
            db.execute('PRAGMA journal_mode=WAL')
            db.execute('CREATE TABLE __EFMigrationsHistory (Id TEXT)')
            db.executemany('INSERT INTO __EFMigrationsHistory VALUES (?)', [('m0',), ('m1',), ('m2',), ('m3',)])
            db.execute('CREATE TABLE Installations (Id INTEGER, InstanceId TEXT, CreatedUtc TEXT)')
            db.execute("INSERT INTO Installations VALUES (1, 'fixture', 'fixture-date')")
    sys.exit(0)
operation = args[3]
if operation == 'start' and '--wait' in args:
    print('unknown flag: --wait', file=sys.stderr)
    sys.exit(64)  # Models the older hosted Compose CLI that exposed the regression.
if operation == 'up' and os.environ.get('FAIL_BUILD'):
    sys.exit(17)
if operation == 'ps':
    print('fixture-container')
if operation == 'logs':
    print('sanitized fixture diagnostic')
    if os.environ.get('FAIL_DIAGNOSTICS'):
        sys.exit(23)
if operation == 'down' and os.environ.get('FAIL_CLEANUP'):
    sys.exit(24)
'''


class ComposeVerifierTests(unittest.TestCase):
    def run_verifier(self, **flags):
        directory = tempfile.TemporaryDirectory()
        self.addCleanup(directory.cleanup)
        workspace = Path(directory.name)
        (workspace / 'scripts').mkdir()
        (workspace / 'bin').mkdir()
        shutil.copyfile(ROOT / 'scripts/verify-compose.sh', workspace / 'scripts/verify-compose.sh')
        (workspace / 'scripts/check-deployment.py').write_text("print('HTTP probe fixture')\n")
        for name, source in [('docker', DOCKER), ('npm', '#!/bin/sh\nexit 0\n')]:
            executable = workspace / 'bin' / name
            executable.write_text(source)
            executable.chmod(0o700)
        calls = workspace / 'calls.jsonl'
        environment = dict(os.environ, PATH=str(workspace / 'bin') + os.pathsep + os.environ['PATH'],
                           CALLS=str(calls), GITHUB_RUN_ID='unit-verifier', **flags)
        result = subprocess.run(['bash', 'scripts/verify-compose.sh'], cwd=workspace, env=environment,
                                capture_output=True, text=True, timeout=15)
        invocations = [json.loads(line) for line in calls.read_text().splitlines()]
        return workspace, result, invocations

    def test_older_compose_reaches_browser_checks_without_start_wait(self):
        workspace, result, calls = self.run_verifier()
        self.assertEqual(result.returncode, 0, result.stderr)
        self.assertIn('transport queries are excluded', result.stdout)
        self.assertTrue(any('--no-recreate' in call and '--wait' in call for call in calls))
        self.assertFalse(any('start' in call and '--wait' in call for call in calls))
        self.assertFalse((workspace / 'artifacts/verification/compose.log').exists())
        self.assert_scoped_cleanup(calls)

    def test_build_failure_retains_diagnostics_and_original_exit_status(self):
        workspace, result, calls = self.run_verifier(FAIL_BUILD='1')
        self.assertEqual(result.returncode, 17)
        self.assertIn('sanitized fixture diagnostic', (workspace / 'artifacts/verification/compose.log').read_text())
        self.assertTrue((workspace / 'artifacts/verification/compose-status.txt').exists())
        self.assert_scoped_cleanup(calls)

    def test_diagnostic_and_cleanup_failures_do_not_mask_the_build_failure(self):
        _, result, calls = self.run_verifier(FAIL_BUILD='1', FAIL_DIAGNOSTICS='1', FAIL_CLEANUP='1')
        self.assertEqual(result.returncode, 17)
        self.assert_scoped_cleanup(calls)

    def test_cleanup_failure_fails_an_otherwise_successful_run(self):
        _, result, calls = self.run_verifier(FAIL_CLEANUP='1')
        self.assertEqual(result.returncode, 24)
        self.assert_scoped_cleanup(calls)

    def assert_scoped_cleanup(self, calls):
        operations = [call for call in calls if call[0] == 'compose']
        self.assertTrue(all(call[1:3] == ['-p', 'teamgames-verification-unit-verifier'] for call in operations))
        self.assertEqual(operations[-1][3:], ['down', '--volumes', '--remove-orphans'])
