#!/usr/bin/python3
"""Root-owned, no-argument Valtrans site deployer. Never install from an archive.

Dockerfile and compose.yaml are deliberately immutable through this helper.
Changing container privileges, host mounts or port configuration requires the
administrator to review and install those files separately.
"""
import fcntl
import hashlib
import io
import json
import os
from pathlib import Path, PurePosixPath
import re
import shutil
import stat
import subprocess
import sys
import tarfile
import tempfile
import time
import urllib.request

ROOT = Path('/media/HDD-Raid1/AppData/Valtrans')
INBOX = Path('/DATA/valtrans-deploy-inbox')
ARCHIVE = 'valtrans-support-release.tar.gz'
MAX_BYTES = 64 * 1024 * 1024
COMPOSE_PROJECT = None
FIXED = {'README.md', 'server/README.md', 'server/server.js',
         'server/package.json', 'server/Dockerfile', 'server/compose.yaml',
         'scripts/sync-support-site.sh', 'support-build-manifest.json'}
IMMUTABLE = {'server/Dockerfile', 'server/compose.yaml'}
DOCUMENTATION = {'README.md', 'server/README.md', 'scripts/sync-support-site.sh'}
SITE_EXTENSIONS = {'.html', '.css', '.js', '.png', '.ico', '.svg', '.woff2', '.woff'}


def regular_bytes(path, limit):
    fd = os.open(path, os.O_RDONLY | os.O_NOFOLLOW | os.O_NONBLOCK)
    with os.fdopen(fd, 'rb') as stream:
        info = os.fstat(stream.fileno())
        if not stat.S_ISREG(info.st_mode) or info.st_size > limit:
            raise ValueError('Input must be a bounded regular file')
        data = stream.read(limit + 1)
        if len(data) > limit:
            raise ValueError('Input too large')
        return data


def allowed_file(name):
    return name in FIXED or (name.startswith('site/') and
                            PurePosixPath(name).suffix.lower() in SITE_EXTENSIONS)


def validated_package(raw, expected_hash):
    if not re.fullmatch('[a-fA-F0-9]{64}', expected_hash):
        raise ValueError('Invalid SHA256')
    if hashlib.sha256(raw).hexdigest() != expected_hash.lower():
        raise ValueError('Archive SHA256 mismatch')
    files = {}
    total = 0
    with tarfile.open(fileobj=io.BytesIO(raw), mode='r:gz') as archive:
        for count, member in enumerate(archive):
            if count > 1024:
                raise ValueError('Too many archive entries')
            name = member.name.removeprefix('./').rstrip('/')
            if name in ('', '.') and member.isdir():
                continue
            path = PurePosixPath(name)
            if (path.is_absolute() or '\\' in name or ':' in name or
                    any(part in ('', '.', '..') for part in name.split('/'))):
                raise ValueError('Unsafe archive path')
            if member.isdir():
                if name not in ('server', 'scripts', 'site') and not name.startswith('site/'):
                    raise ValueError('Unexpected directory')
                continue
            if not member.isfile() or not allowed_file(name) or name in files:
                raise ValueError('Unexpected, duplicate or non-regular archive entry')
            total += member.size
            if total > MAX_BYTES or member.size < 0:
                raise ValueError('Expanded archive too large')
            files[name] = archive.extractfile(member).read()
    manifest = json.loads(files['support-build-manifest.json'].decode('utf-8-sig'))
    version = manifest['version']
    if not isinstance(version, str) or not re.fullmatch(r'\d+\.\d+\.\d+(?:-[A-Za-z0-9.-]+)?', version):
        raise ValueError('Invalid release version')
    entries = manifest['files']
    names = [entry['name'] for entry in entries]
    if len(names) != len(set(names)) or set(names) != set(files) - {'support-build-manifest.json'}:
        raise ValueError('Manifest file set mismatch')
    for entry in entries:
        if hashlib.sha256(files[entry['name']]).hexdigest() != entry['sha256'].lower():
            raise ValueError('Manifest file hash mismatch')
    required = {'site/index.html', 'site/terms.html', 'site/privacy.html',
                'server/server.js', 'server/package.json'} | IMMUTABLE
    if not required <= files.keys() or ('v' + version).encode() not in files['site/index.html']:
        raise ValueError('Required pages or matching version missing')
    return version, files


def trusted_path(path):
    """Reject symlinks and non-root-writable ancestors below the fixed root."""
    if not path.is_relative_to(ROOT):
        raise ValueError('Outside deployment root')
    # A root-owned leaf can still be renamed through a world-writable parent.
    for ancestor in ROOT.parents:
        info = ancestor.lstat()
        if (not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or
                (info.st_mode & 0o022 and not info.st_mode & stat.S_ISVTX)):
            raise ValueError('Unsafe deployment ancestor: ' + str(ancestor))
    current = ROOT
    for component in (None, *path.relative_to(ROOT).parts):
        if component is not None:
            current = current / component
        if current.is_symlink():
            raise ValueError('Symlink in deployment target')
        if current.exists():
            info = current.stat()
            if info.st_uid != 0 or info.st_mode & 0o022:
                raise ValueError('Deployment target must be root-owned and not group/world writable: ' + str(current))


def atomic_write(path, data):
    trusted_path(path)
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o755)
    fd, temporary = tempfile.mkstemp(prefix='.valtrans-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
            stream.flush()
            os.fsync(stream.fileno())
        os.chmod(temporary, 0o644)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def docker(*args, capture=False):
    # Do not inherit user-controlled PATH, Docker endpoint, plugin or compose settings.
    environment = {'PATH': '/usr/sbin:/usr/bin:/sbin:/bin', 'HOME': '/root',
                   'DOCKER_CONFIG': str(ROOT / 'server/.docker-client')}
    return subprocess.run(['/usr/bin/docker', *args], cwd=ROOT / 'server',
                          env=environment, check=True, text=True,
                          stdout=subprocess.PIPE if capture else None,
                          timeout=600).stdout


def compose(*args):
    if COMPOSE_PROJECT is None:
        raise ValueError('Existing Compose project has not been verified')
    docker('compose', '--project-name', COMPOSE_PROJECT, '--env-file', str(ROOT / 'server/.env'),
           '-f', str(ROOT / 'server/compose.yaml'), *args)


def verify_service(version=None):
    opener = urllib.request.build_opener(urllib.request.ProxyHandler({}))
    for attempt in range(20):
        try:
            for route in ('/health', '/', '/terms', '/privacy'):
                with opener.open('http://192.168.0.19:13020' + route, timeout=3) as response:
                    body = response.read(MAX_BYTES)
                    if response.status != 200 or (route == '/' and version and
                                                  ('v' + version).encode() not in body):
                        raise ValueError('Service version/status mismatch')
            return
        except (OSError, ValueError):
            if attempt == 19:
                raise
            time.sleep(1)


def main():
    global COMPOSE_PROJECT
    if os.geteuid() != 0 or len(sys.argv) != 1:
        raise SystemExit('Run the installed helper using sudo, without arguments.')
    os.umask(0o077)
    trusted_path(ROOT)
    for name in ('server', 'server/.docker-client', 'server/.env', *IMMUTABLE):
        trusted_path(ROOT / name)
    for path in (ROOT / 'server/.docker-client').rglob('*'):
        trusted_path(path)
    state = ROOT / '.deployments'
    trusted_path(state)
    state.mkdir(mode=0o700, exist_ok=True)
    lock_fd = os.open(state / 'lock', os.O_CREAT | os.O_RDWR | os.O_NOFOLLOW, 0o600)
    with os.fdopen(lock_fd, 'w'):
        fcntl.flock(lock_fd, fcntl.LOCK_EX | fcntl.LOCK_NB)
        raw = regular_bytes(INBOX / ARCHIVE, MAX_BYTES)
        checksum = regular_bytes(INBOX / (ARCHIVE + '.sha256'), 512).decode('ascii').split()[0]
        version, files = validated_package(raw, checksum)
        for name, data in files.items():
            if name in DOCUMENTATION:
                continue
            trusted_path(ROOT / name)
            if name in IMMUTABLE and regular_bytes(ROOT / name, MAX_BYTES) != data:
                raise ValueError(name + ' changed; administrator review required')
        # Refuse to replace a service without a verified rollback image.
        old_image = docker('inspect', '--format', '{{.Image}}', 'valtrans-support', capture=True).strip()
        if not re.fullmatch(r'sha256:[a-f0-9]{64}', old_image):
            raise ValueError('No rollback image available')
        COMPOSE_PROJECT = docker('inspect', '--format',
            '{{index .Config.Labels "com.docker.compose.project"}}', 'valtrans-support', capture=True).strip()
        service = docker('inspect', '--format',
            '{{index .Config.Labels "com.docker.compose.service"}}', 'valtrans-support', capture=True).strip()
        if not re.fullmatch(r'[a-z0-9][a-z0-9_-]{0,62}', COMPOSE_PROJECT) or service != 'valtrans-support':
            raise ValueError('Unexpected existing Compose service/project')
        previous = {name: regular_bytes(ROOT / name, MAX_BYTES) if (ROOT / name).exists() else None
                    for name in files if name not in IMMUTABLE | DOCUMENTATION}
        backup = Path(tempfile.mkdtemp(prefix='release-', dir=state))
        for name, data in previous.items():
            if data is not None:
                atomic_write(backup / name, data)
        atomic_write(backup / 'rollback.json', json.dumps({'image': old_image,
                     'newVersion': version, 'archiveSha256': checksum,
                     'newFiles': [name for name, data in previous.items() if data is None]}).encode())
        docker('image', 'tag', old_image, 'valtrans/support:rollback-' + backup.name)
        # Build only verified package files: stale/user-writable legacy assets,
        # .env and payment data must never enter the Docker build context.
        staging = backup / 'build'
        for name, data in files.items():
            if name.startswith('site/') or name in ('server/server.js', 'server/package.json', 'server/Dockerfile'):
                atomic_write(staging / name, data)
        try:
            docker('build', '--tag', 'valtrans/support:local', '--file',
                   str(staging / 'server/Dockerfile'), str(staging))
            for name in previous:
                atomic_write(ROOT / name, files[name])
            compose('up', '-d', '--no-build', '--no-deps', '--force-recreate', 'valtrans-support')
            verify_service(version)
        except BaseException:
            print('Deployment failed; restoring previous files and image.', flush=True)
            for name, data in previous.items():
                if data is None:
                    (ROOT / name).unlink(missing_ok=True)
                else:
                    atomic_write(ROOT / name, data)
            docker('image', 'tag', old_image, 'valtrans/support:local')
            compose('up', '-d', '--no-build', '--no-deps', '--force-recreate', 'valtrans-support')
            verify_service()
            raise
        print(json.dumps({'status': 'deployed', 'version': version,
                          'sha256': checksum, 'backup': str(backup)}))


if __name__ == '__main__':
    main()
