#!/usr/bin/env python3
"""Check the signed packages and ensure only the demo enables the agent."""
from pathlib import Path
import hashlib
import json
import os
import re
import subprocess
import zipfile

root = Path(__file__).resolve().parent.parent
tools = Path(os.environ['ANDROID_HOME']) / 'build-tools/36.0.0'
configuration = b'--debugger-agent=transport=dt_socket,address=127.0.0.1:56000,server=y,suspend=n,embedding=1'
results = []
for filename, package_id, is_target in [
    ('LocalDebug-android-arm64.apk', 'dev.localdebug.debugger', False),
    ('DebugTarget-android-arm64.apk', 'dev.localdebug.target', True),
]:
    package = root / 'artifacts' / filename
    signature = subprocess.run([str(tools/'apksigner'), 'verify', '--verbose', str(package)],
                               capture_output=True, text=True, check=True)
    assert 'Verified using v2 scheme (APK Signature Scheme v2): true' in signature.stdout
    assert 'Verified using v3 scheme (APK Signature Scheme v3): true' in signature.stdout
    badging = subprocess.run([str(tools/'aapt2'), 'dump', 'badging', str(package)],
                             capture_output=True, text=True, check=True).stdout
    for expected in [f"package: name='{package_id}'", "targetSdkVersion:'36'",
                     "uses-permission: name='android.permission.INTERNET'", "native-code: 'arm64-v8a'",
                     'application-debuggable']:
        assert expected in badging, f'{filename}: missing package attribute {expected}'
    manifest = subprocess.run([str(tools/'aapt2'), 'dump', 'xmltree', '--file', 'AndroidManifest.xml', str(package)],
                              capture_output=True, text=True, check=True).stdout
    assert re.search(r'android:minSdkVersion\([^\)]+\)=23\b', manifest), f'{filename}: incorrect minimum SDK'
    assert re.search(r'android:targetSdkVersion\([^\)]+\)=36\b', manifest), f'{filename}: incorrect target SDK'
    assert re.search(r'android:resizeableActivity\([^\)]+\)=true', manifest), f'{filename}: split screen not enabled'
    instrumentation = 'dev.localdebug.debugger.DeviceTests' in manifest
    assert instrumentation != is_target, f'{filename}: unexpected device-test instrumentation registration'
    with zipfile.ZipFile(package) as zipped:
        assert zipped.testzip() is None, f'{filename}: corrupt ZIP entry'
        assert 'lib/arm64-v8a/libmono-component-debugger.so' in zipped.namelist()
        configured = configuration in zipped.read('lib/arm64-v8a/libxamarin-app.so')
        assert configured == is_target, f'{filename}: unexpected compiled debug-agent settings'
    result = {'file': filename, 'package': package_id, 'bytes': package.stat().st_size,
              'sha256': hashlib.sha256(package.read_bytes()).hexdigest(),
              'apk_signature_v2': True, 'apk_signature_v3': True,
              'min_sdk': 23, 'target_sdk': 36, 'abi': 'arm64-v8a',
              'device_test_instrumentation': instrumentation,
              'target_debug_agent_enabled': configured}
    results.append(result)
    print('PASS:', filename, 'signature, manifest, ARM64 payload, and debug-agent configuration.')
(root/'artifacts/package-checks.json').write_text(json.dumps(results, indent=2)+'\n')
