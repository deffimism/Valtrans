"""Read-only archive/security tests. No Docker or privileged operations."""
import hashlib
import importlib.util
import io
import json
from pathlib import Path
import tarfile
import tempfile
import unittest
from unittest.mock import patch

spec = importlib.util.spec_from_file_location('deploy', Path(__file__).with_name('valtrans-deploy.py'))
deploy = importlib.util.module_from_spec(spec)
spec.loader.exec_module(deploy)


def fixture(extra=None, alter_manifest=None):
    files = {'site/index.html': b'v0.5.0-beta', 'site/terms.html': b'terms',
             'site/privacy.html': b'privacy', 'server/server.js': b'server',
             'server/package.json': b'{}', 'server/Dockerfile': b'FROM node',
             'server/compose.yaml': b'services: {}'}
    manifest = {'version': '0.5.0-beta', 'files': [
        {'name': name, 'sha256': hashlib.sha256(data).hexdigest()} for name, data in files.items()]}
    if alter_manifest:
        alter_manifest(manifest)
    files['support-build-manifest.json'] = json.dumps(manifest).encode()
    output = io.BytesIO()
    with tarfile.open(fileobj=output, mode='w:gz') as archive:
        for name, data in files.items():
            info = tarfile.TarInfo('./' + name)
            info.size = len(data)
            archive.addfile(info, io.BytesIO(data))
        if extra:
            archive.addfile(extra, io.BytesIO(b''))
    raw = output.getvalue()
    return raw, hashlib.sha256(raw).hexdigest()


class PackageTests(unittest.TestCase):
    def test_valid(self):
        version, files = deploy.validated_package(*fixture())
        self.assertEqual('0.5.0-beta', version)
        self.assertIn('site/privacy.html', files)

    def test_hash_mismatch(self):
        raw, _ = fixture()
        with self.assertRaises(ValueError):
            deploy.validated_package(raw, '0' * 64)

    def test_reject_unsafe_entries(self):
        for name in ('../outside', '/etc/sudoers', 'site/../../outside',
                     'site\\oops.js', 'server/.env', 'server/data/events.json',
                     'scripts/server/valtrans-deploy.py', 'site/a.exe', 'site/index.html'):
            with self.subTest(name=name), self.assertRaises(ValueError):
                deploy.validated_package(*fixture(tarfile.TarInfo(name)))

    def test_reject_links_and_devices(self):
        for kind in (tarfile.SYMTYPE, tarfile.LNKTYPE, tarfile.CHRTYPE, tarfile.FIFOTYPE):
            info = tarfile.TarInfo('site/linked.js')
            info.type = kind
            info.linkname = '/etc/passwd'
            with self.subTest(kind=kind), self.assertRaises(ValueError):
                deploy.validated_package(*fixture(info))

    def test_bad_manifest(self):
        changes = [lambda m: m.update(version='../../root'),
                   lambda m: m['files'].pop(),
                   lambda m: m['files'].append(m['files'][0]),
                   lambda m: m['files'][0].update(sha256='0' * 64)]
        for change in changes:
            with self.subTest(change=change), self.assertRaises(ValueError):
                deploy.validated_package(*fixture(alter_manifest=change))

    def test_real_package_when_provided(self):
        package = Path(__file__).with_name('valtrans-support-release.tar.gz')
        if not package.exists():
            self.skipTest('No real package uploaded')
        raw = package.read_bytes()
        deploy.validated_package(raw, hashlib.sha256(raw).hexdigest())


class TransactionTests(unittest.TestCase):
    """Real temporary files, mocked privilege checks/Docker/HTTP. No service changes."""
    def exercise(self, failure):
        raw, checksum = fixture()
        _, files = deploy.validated_package(raw, checksum)
        with tempfile.TemporaryDirectory(prefix='valtrans-rollback-test-') as temporary:
            root = Path(temporary) / 'project'
            inbox = Path(temporary) / 'inbox'
            inbox.mkdir()
            (root / 'site').mkdir(parents=True)
            (root / 'server/.docker-client').mkdir(parents=True)
            (inbox / deploy.ARCHIVE).write_bytes(raw)
            (inbox / (deploy.ARCHIVE + '.sha256')).write_text(checksum)
            originals = {'site/index.html': b'previous homepage', 'server/server.js': b'previous code',
                'server/.env': b'KEEP_FAKE_SECRET=unchanged', 'server/data/events.json': b'[]'}
            for name, data in originals.items():
                (root / name).parent.mkdir(parents=True, exist_ok=True)
                (root / name).write_bytes(data)
            for name in deploy.IMMUTABLE:
                (root / name).write_bytes(files[name])
            old_image = 'sha256:' + 'a' * 64

            def docker(*args, **kwargs):
                if args[:2] == ('inspect', '--format'):
                    if args[2] == '{{.Image}}':
                        return old_image
                    return 'server' if '.project' in args[2] else 'valtrans-support'
                if args[0] == 'build' and failure == 'build':
                    raise RuntimeError('simulated build failure')
                return ''

            old_mask = deploy.os.umask(0o077)
            try:
                with patch.multiple(deploy, ROOT=root, INBOX=inbox), \
                     patch.object(deploy, 'trusted_path'), \
                     patch.object(deploy.os, 'geteuid', return_value=0), \
                     patch.object(deploy.sys, 'argv', ['test-helper']), \
                     patch.object(deploy, 'docker', side_effect=docker) as docker_calls, \
                     patch.object(deploy, 'compose') as compose, \
                     patch.object(deploy, 'verify_service', side_effect=
                         [RuntimeError('simulated unhealthy release'), None] if failure == 'health' else None) as verify:
                    if failure:
                        with self.assertRaisesRegex(RuntimeError, 'simulated'):
                            deploy.main()
                        self.assertEqual(originals['site/index.html'], (root / 'site/index.html').read_bytes())
                        self.assertEqual(originals['server/server.js'], (root / 'server/server.js').read_bytes())
                        self.assertFalse((root / 'site/privacy.html').exists())
                        docker_calls.assert_any_call('image', 'tag', old_image, 'valtrans/support:local')
                        verify.assert_called_with()
                    else:
                        deploy.main()
                        self.assertEqual(files['site/index.html'], (root / 'site/index.html').read_bytes())
                        verify.assert_called_once_with('0.5.0-beta')
                    self.assertEqual(2 if failure == 'health' else 1, compose.call_count)
                    for name in ('server/.env', 'server/data/events.json'):
                        self.assertEqual(originals[name], (root / name).read_bytes())
                    backups = list((root / '.deployments').glob('release-*'))
                    self.assertEqual(1, len(backups))
                    self.assertFalse((backups[0] / 'build/server/.env').exists())
                    self.assertEqual(originals['site/index.html'], (backups[0] / 'site/index.html').read_bytes())
            finally:
                deploy.os.umask(old_mask)

    def test_success(self):
        self.exercise(None)

    def test_build_failure_restores_files_and_image(self):
        self.exercise('build')

    def test_health_failure_restores_files_and_image(self):
        self.exercise('health')


if __name__ == '__main__':
    unittest.main()
