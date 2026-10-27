#!/usr/bin/env python3
"""Create source and APK bundles without local caches, binaries, or signing keys."""
from pathlib import Path
import hashlib
import zipfile

root = Path(__file__).resolve().parent.parent
artifacts = root / 'artifacts'
artifacts.mkdir(exist_ok=True)
source = artifacts / 'local-debugger-source.zip'
excluded = {'bin', 'obj', 'artifacts', '.git', '.local', '__pycache__', 'TestResults', 'device-results'}
with zipfile.ZipFile(source, 'w', zipfile.ZIP_DEFLATED, compresslevel=6) as output:
    for path in sorted(root.rglob('*')):
        relative = path.relative_to(root)
        if not path.is_file() or any(part in excluded for part in relative.parts):
            continue
        if path.suffix in ['.keystore', '.jks', '.p12', '.pfx', '.user', '.pyc']:
            continue
        if path.name == 'core' or path.name.startswith('core.'):
            continue
        if path.name == '.env' or (path.name.startswith('.env.') and path.name != '.env.example'):
            continue
        output.write(path, Path(root.name) / relative)

bundle = artifacts / 'local-debugger-arm64.zip'
with zipfile.ZipFile(bundle, 'w', zipfile.ZIP_STORED) as output:
    for name in ['LocalDebug-android-arm64.apk', 'DebugTarget-android-arm64.apk', 'package-checks.json']:
        output.write(artifacts / name, name)
    for name in ['README.md', 'CHECKS.md', 'RELEASE_NOTES.md', 'LICENSE', 'THIRD_PARTY_NOTICES.txt']:
        output.write(root / name, name)
    output.write(root/'scripts/Test-Fold4.ps1', 'Test-Fold4.ps1')
    output.write(root/'DEVICE_TEST_HANDOFF.md', 'DEVICE_TEST_HANDOFF.md')

deliverables = [artifacts/'LocalDebug-android-arm64.apk', artifacts/'DebugTarget-android-arm64.apk', source, bundle]
lines = []
for path in deliverables:
    with path.open('rb') as stream:
        digest = hashlib.file_digest(stream, 'sha256').hexdigest()
    lines.append(f'{digest}  {path.name}')
    with zipfile.ZipFile(path) as archive:
        assert archive.testzip() is None, f'Corrupt deliverable: {path}'
    print(f'{path.name}: {path.stat().st_size:,} bytes')
(artifacts/'SHA256SUMS').write_text('\n'.join(lines)+'\n')

with zipfile.ZipFile(source) as archive:
    assert all(not set(Path(name).parts).intersection(excluded) for name in archive.namelist())
    assert all(not name.endswith('.keystore') for name in archive.namelist())
    print('Source archive:',len(archive.namelist()),'files; no build outputs, caches, or signing keys.')
