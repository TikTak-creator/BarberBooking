#!/usr/bin/env python3
"""Offline Linux host backup; authenticated before extraction, no existing database replacement."""
import getpass
import hashlib
import io
import os
from pathlib import Path
import shutil
import sys
import tarfile
import tempfile
from cryptography.hazmat.primitives.ciphers.aead import AESGCM

MAGIC = b'BLADEZ-LINUX-1\n'
LIMIT = 512 * 1024 * 1024

def main():
    os.umask(0o077)
    action, name = sys.argv[1:]
    password = sys.stdin.buffer.read(512)
    if len(password.decode()) < 12:
        raise ValueError('Backup password must contain at least 12 characters.')
    if action == 'create':
        files = [p for p in Path('private').rglob('*') if p.is_file() and not p.is_symlink()]
        if sum(p.stat().st_size for p in files) > LIMIT:
            raise ValueError('The included backup tool supports up to 512 MiB of source data.')
        buffer = io.BytesIO()
        with tarfile.open(fileobj=buffer, mode='w:gz') as archive:
            archive.add('.env')
            for path in files:
                if path.suffix not in ('.sock', '.pid', '.err') and 'data-protection' not in path.parts:
                    archive.add(path, recursive=False)
        salt, nonce = os.urandom(16), os.urandom(12)
        key = hashlib.pbkdf2_hmac('sha512', password, salt, 600_000, 32)
        data = MAGIC + salt + nonce + AESGCM(key).encrypt(nonce, buffer.getvalue(), MAGIC)
        # Exclusive creation: never overwrite a known-good backup.
        with open(name, 'xb') as output:
            output.write(data)
    elif action == 'restore':
        if Path('private/mysql/mysql').exists() or Path('private/appdata/master.key').exists():
            raise ValueError('Use a fresh, empty host package for restore.')
        source = Path(name)
        if not len(MAGIC) + 44 <= source.stat().st_size <= LIMIT + 16_000_000:
            raise ValueError('Invalid backup size.')
        data = source.read_bytes()
        if not data.startswith(MAGIC):
            raise ValueError('Unknown backup format.')
        offset = len(MAGIC)
        salt, nonce = data[offset:offset+16], data[offset+16:offset+28]
        key = hashlib.pbkdf2_hmac('sha512', password, salt, 600_000, 32)
        clear = AESGCM(key).decrypt(nonce, data[offset+28:], MAGIC)
        with tempfile.TemporaryDirectory(prefix='bladez-restore-', dir='.') as temporary:
            with tarfile.open(fileobj=io.BytesIO(clear), mode='r:gz') as archive:
                entries = archive.getmembers()
                if sum(e.size for e in entries) > LIMIT or len(entries) > 100_000:
                    raise ValueError('Expanded backup too large.')
                for entry in entries:
                    parts = Path(entry.name).parts
                    if entry.name != '.env' and (not parts or parts[0] != 'private'):
                        raise ValueError('Unexpected backup path.')
                    if '..' in parts or entry.issym() or entry.islnk() or not entry.isfile():
                        raise ValueError('Unsafe backup entry.')
                archive.extractall(temporary, members=entries, filter='data')
            staging = Path(temporary)
            if not (staging/'private/appdata/master.key').is_file() or not (staging/'private/mysql/mysql').is_dir():
                raise ValueError('Incomplete backup.')
            if Path('private').exists():
                if any(Path('private').iterdir()):
                    raise ValueError('The private directory must be empty.')
                Path('private').rmdir()
            shutil.move(str(staging/'private'), 'private')
            shutil.move(str(staging/'.env'), '.env')
        for path in Path('private').rglob('*'):
            os.chmod(path, 0o700 if path.is_dir() else 0o600)
            if any(part in ('mysql', 'db-certs', 'init') for part in path.parts) or path.name in ('root-password', 'root-client.cnf'):
                os.chown(path, 999, 999)
            elif 'appdata' in path.parts or path.name in ('app-connection', 'migration-connection', 'bootstrap.json'):
                os.chown(path, 1654, 1654)
        os.chmod('private/ca.pem', 0o644)
        os.chmod('private', 0o700)
    else:
        raise ValueError('Unknown backup action.')

if __name__ == '__main__':
    try:
        main()
    except Exception as error:
        print('Backup operation failed (' + type(error).__name__ + '). Check password, file integrity, available space and empty destination.', file=sys.stderr)
        sys.exit(1)
