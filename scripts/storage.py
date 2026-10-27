#!/usr/bin/env python3
"""Stopped-volume backups/restores; supports this application's named /data volume."""
from contextlib import closing
import argparse
import hashlib
import json
import os
from pathlib import Path
import re
import sqlite3
import subprocess
import time


def run(*args):
    return subprocess.run(args, check=True, text=True, capture_output=True, cwd=Path(__file__).resolve().parents[1]).stdout.strip()


def files(directory):
    result = {}
    for path in directory.rglob('*'):
        if path.is_symlink():
            raise ValueError('Symlinks are not accepted in storage backups')
        if path.is_file():
            digest = hashlib.sha256()
            with path.open('rb') as stream:
                for block in iter(lambda: stream.read(1024 * 1024), b''):
                    digest.update(block)
            result[path.relative_to(directory).as_posix()] = digest.hexdigest()
    return result


def integrity(directory):
    if not (directory / 'teamgames.db').is_file() or not list((directory / 'keys').glob('*.xml')):
        raise ValueError('The backup must contain the database and Data Protection keys')
    with closing(sqlite3.connect((directory / 'teamgames.db').as_uri() + '?mode=ro', uri=True)) as db:
        if db.execute('PRAGMA integrity_check').fetchone()[0] != 'ok':
            raise ValueError('SQLite integrity validation failed')
        if db.execute('PRAGMA foreign_key_check').fetchone() is not None:
            raise ValueError('SQLite foreign-key validation failed')
        return db.execute('SELECT count(*) FROM __EFMigrationsHistory').fetchone()[0]


def validate(directory):
    manifest = json.loads((directory / 'manifest.json').read_text())
    if manifest.get('format') != 1 or files(directory / 'data') != manifest.get('sha256'):
        raise ValueError('Incomplete or changed backup; restore refused')
    integrity(directory / 'data')


def container(project):
    result = run('docker', 'compose', '-p', project, '-f', 'compose.yaml', 'ps', '--all', '--quiet', 'api')
    if not result or '\n' in result:
        raise ValueError('Expected exactly one existing API container')
    return result


def start(project, identity):
    run('docker', 'compose', '-p', project, '-f', 'compose.yaml', 'start', 'api')
    deadline = time.monotonic() + 90
    while time.monotonic() < deadline:
        if run('docker', 'inspect', '--format', '{{.State.Health.Status}}', identity) == 'healthy':
            return
        time.sleep(.5)
    raise RuntimeError('API failed readiness after storage operation')


def operate(action, project, directory, confirmation=None):
    if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,62}', project):
        raise ValueError('Invalid Compose project name')
    if action == 'restore':
        if confirmation != project:
            raise ValueError('Restore requires --confirm with the exact project name')
        validate(directory)  # Validate completely before stopping or modifying the app.
    elif directory.exists():
        raise ValueError('Choose a new backup directory; existing paths are never overwritten')
    identity = container(project)
    info = json.loads(run('docker', 'inspect', identity))[0]
    mounts = [m for m in info['Mounts'] if m['Destination'] == '/data']
    if len(mounts) != 1 or mounts[0]['Type'] != 'volume':
        raise ValueError('Only the configured named /data volume is supported')
    try:
        run('docker', 'compose', '-p', project, '-f', 'compose.yaml', 'stop', 'api')
        if action == 'backup':
            directory.mkdir(parents=True, mode=0o700)
            os.chmod(directory, 0o700)
            (directory / 'data').mkdir(mode=0o700)
            run('docker', 'cp', identity + ':/data/.', str(directory / 'data'))
            migrations = integrity(directory / 'data')
            manifest = {'format': 1, 'migrations': migrations, 'sha256': files(directory / 'data')}
            (directory / 'manifest.json').write_text(json.dumps(manifest, indent=2) + '\n')
            validate(directory)
        else:
            # This maintenance container mounts only the selected application's volume.
            run('docker', 'run', '--rm', '--network', 'none', '--security-opt', 'no-new-privileges:true', '--user', 'root', '--entrypoint', '/bin/sh',
                '--mount', f'type=volume,source={mounts[0]["Name"]},target=/data',
                '--mount', f'type=bind,source={directory / "data"},target=/restore,readonly',
                info['Image'], '-ec',
                'find /data -mindepth 1 -maxdepth 1 -exec rm -rf -- {} +; cp -a /restore/. /data/; chown -R app:app /data; chmod 700 /data /data/keys')
    finally:
        start(project, identity)
    print(f'{action.capitalize()} completed; database integrity and API readiness passed')


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('action', choices=['backup', 'restore'])
    parser.add_argument('--project', required=True)
    parser.add_argument('--directory', type=Path, required=True)
    parser.add_argument('--confirm')
    args = parser.parse_args()
    try:
        operate(args.action, args.project, args.directory.resolve(), args.confirm)
    except (ValueError, RuntimeError, OSError, sqlite3.Error, subprocess.CalledProcessError):
        # Subprocess output can contain private paths/configuration: never echo it.
        parser.exit(1, 'Storage operation failed; check the project, backup integrity, permissions and container health\n')


if __name__ == '__main__':
    main()
