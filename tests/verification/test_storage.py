from contextlib import closing
import importlib.util
import json
from pathlib import Path
import sqlite3
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('storage', Path(__file__).resolve().parents[2] / 'scripts/storage.py')
storage = importlib.util.module_from_spec(spec)
spec.loader.exec_module(storage)


class StorageTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.addCleanup(self.temp.cleanup)
        self.directory = Path(self.temp.name)
        data = self.directory / 'data'
        (data / 'keys').mkdir(parents=True)
        (data / 'keys/key.xml').write_text('synthetic key fixture')
        with closing(sqlite3.connect(data / 'teamgames.db')) as db:
            db.execute('CREATE TABLE __EFMigrationsHistory (Id TEXT)')
            db.execute("INSERT INTO __EFMigrationsHistory VALUES ('fixture')")
            db.commit()
        (self.directory / 'manifest.json').write_text(json.dumps({'format': 1, 'sha256': storage.files(data)}))

    def test_complete_backup_passes_and_changed_data_is_rejected(self):
        storage.validate(self.directory)
        (self.directory / 'data/keys/key.xml').write_text('changed')
        with self.assertRaises(ValueError): storage.validate(self.directory)

    def test_extra_files_and_symlinks_are_rejected(self):
        (self.directory / 'data/extra').write_text('unexpected')
        with self.assertRaises(ValueError): storage.validate(self.directory)
        (self.directory / 'data/extra').unlink()
        (self.directory / 'data/link').symlink_to('/etc/passwd')
        with self.assertRaises(ValueError): storage.validate(self.directory)

    def test_missing_keys_are_rejected(self):
        (self.directory / 'data/keys/key.xml').unlink()
        with self.assertRaises(ValueError): storage.integrity(self.directory / 'data')

    @patch.object(storage, 'run')
    def test_restore_requires_exact_confirmation_before_any_docker_call(self, run):
        with self.assertRaises(ValueError): storage.operate('restore', 'mingle', self.directory, 'other')
        run.assert_not_called()

    @patch.object(storage, 'run')
    def test_existing_backup_is_never_overwritten(self, run):
        with self.assertRaises(ValueError): storage.operate('backup', 'mingle', self.directory)
        run.assert_not_called()

    def test_corrupt_database_is_rejected(self):
        (self.directory / 'data/teamgames.db').write_bytes(b'not a SQLite database')
        with self.assertRaises(sqlite3.DatabaseError): storage.integrity(self.directory / 'data')

    @patch.object(storage, 'run')
    def test_corrupt_restore_never_stops_or_modifies_the_application(self, run):
        (self.directory / 'data/keys/key.xml').write_text('changed')
        with self.assertRaises(ValueError): storage.operate('restore', 'mingle', self.directory, 'mingle')
        run.assert_not_called()

    @patch.object(storage, 'run')
    def test_restore_mounts_only_the_selected_volume_and_restarts_the_existing_api(self, run):
        info = {'Mounts': [{'Destination': '/data', 'Type': 'volume', 'Name': 'mingle-test_app-data'}], 'Image': 'sha256:synthetic'}
        def response(*args):
            if args[-4:] == ('ps', '--all', '--quiet', 'api'): return 'synthetic-api'
            if args == ('docker', 'inspect', 'synthetic-api'): return json.dumps([info])
            if '{{.State.Health.Status}}' in args: return 'healthy'
            return ''
        run.side_effect = response
        storage.operate('restore', 'mingle-test', self.directory, 'mingle-test')
        maintenance = next(call.args for call in run.call_args_list if call.args[:3] == ('docker', 'run', '--rm'))
        self.assertIn('type=volume,source=mingle-test_app-data,target=/data', maintenance)
        self.assertIn('none', maintenance)
        self.assertIn(('docker', 'compose', '-p', 'mingle-test', '-f', 'compose.yaml', 'start', 'api'), [call.args for call in run.call_args_list])
