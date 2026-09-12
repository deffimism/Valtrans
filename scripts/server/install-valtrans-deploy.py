#!/usr/bin/python3
"""One-time administrator bootstrap; run only after verifying this file's hash."""
import hashlib
import os
from pathlib import Path
import pwd
import stat
import subprocess
import sys
import tempfile

HELPER_SHA256 = 'd17635ebc4df6866cacabefcb869ea7adc377fb2876270fc0a328ac5ff1698d0'
ADMIN_DIRECTORY = Path('/DATA/.valtrans-admin')
HELPER = ADMIN_DIRECTORY / 'valtrans-deploy'
RULE = Path('/etc/sudoers.d/valtrans-deploy')
RULE_TEXT = b'deffimism ALL=(root) NOPASSWD: /DATA/.valtrans-admin/valtrans-deploy ""\n'
APPDATA = Path('/media/HDD-Raid1/AppData')
ENV_FILE = APPDATA / 'Valtrans/server/.env'
INBOX = Path('/DATA/valtrans-deploy-inbox')


def install(path, data, mode):
    fd, temporary = tempfile.mkstemp(prefix='.valtrans-', dir=path.parent)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(data)
        os.chmod(temporary, mode)
        os.replace(temporary, path)
    finally:
        if os.path.exists(temporary):
            os.unlink(temporary)


def main():
    if os.geteuid() != 0 or len(sys.argv) != 2:
        raise SystemExit('Requires sudo and the uploaded helper file path.')
    os.umask(0o077)
    helper = Path(sys.argv[1]).read_bytes()
    if hashlib.sha256(helper).hexdigest() != HELPER_SHA256:
        raise SystemExit('Helper SHA256 mismatch; nothing changed')
    account = pwd.getpwnam('deffimism')
    for path in (Path('/DATA'), Path('/etc/sudoers.d')):
        info = path.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0 or info.st_mode & 0o022:
            raise SystemExit('Unsafe administrative directory: ' + str(path))
        if os.statvfs(path).f_flag & os.ST_RDONLY:
            raise SystemExit('Read-only administrative filesystem: ' + str(path))
    if ADMIN_DIRECTORY.is_symlink() or (ADMIN_DIRECTORY.exists() and
            (not ADMIN_DIRECTORY.is_dir() or ADMIN_DIRECTORY.stat().st_uid != 0 or
             ADMIN_DIRECTORY.stat().st_mode & 0o022)):
        raise SystemExit('Unsafe existing administrator directory')
    for path in (APPDATA, ENV_FILE.parent.parent, ENV_FILE.parent):
        info = path.lstat()
        if not stat.S_ISDIR(info.st_mode) or info.st_uid != 0:
            raise SystemExit('Expected root-owned directory: ' + str(path))
    info = ENV_FILE.lstat()
    if not stat.S_ISREG(info.st_mode) or info.st_uid != 0 or info.st_nlink != 1:
        raise SystemExit('Expected root-owned regular .env, not a link')
    for path, expected in ((HELPER, helper), (RULE, RULE_TEXT)):
        if path.is_symlink() or (path.exists() and path.read_bytes() != expected):
            raise SystemExit('Existing configuration differs; refusing overwrite: ' + str(path))
    if INBOX.is_symlink() or (INBOX.exists() and (not INBOX.is_dir() or INBOX.stat().st_uid != account.pw_uid)):
        raise SystemExit('Unexpected existing inbox; nothing changed')
    subprocess.run(['/usr/sbin/visudo', '-c'], check=True)
    # ZimaOS has a read-only system root. /etc is an overlay and /DATA persists.
    # Dot-prefixed candidates are ignored by sudo's includedir until validated.
    fd, candidate = tempfile.mkstemp(prefix='.valtrans-check-', dir=RULE.parent)
    try:
        with os.fdopen(fd, 'wb') as stream:
            stream.write(RULE_TEXT)
        subprocess.run(['/usr/sbin/visudo', '-cf', candidate], check=True)
    finally:
        os.unlink(candidate)
    # User explicitly approved these two permission changes. No recursive chmod.
    os.chmod(APPDATA, stat.S_IMODE(APPDATA.stat().st_mode) | stat.S_ISVTX)
    os.chmod(ENV_FILE, 0o600)
    INBOX.mkdir(mode=0o750, exist_ok=True)
    os.chown(INBOX, account.pw_uid, account.pw_gid)
    os.chmod(INBOX, 0o750)
    ADMIN_DIRECTORY.mkdir(mode=0o755, exist_ok=True)
    install(HELPER, helper, 0o755)
    install(RULE, RULE_TEXT, 0o440)
    subprocess.run(['/usr/sbin/visudo', '-c'], check=True)
    print('Installed: Valtrans-only, no-argument deployment permission. No service restarted.')


if __name__ == '__main__':
    main()
