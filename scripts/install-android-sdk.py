#!/usr/bin/env python3
"""Install build prerequisites from Google's HTTPS repository, checking its hashes."""
from pathlib import Path
import hashlib
import json
import os
import shutil
import stat
import subprocess
import tempfile
import urllib.parse
import xml.etree.ElementTree as ET
import zipfile

sdk = Path(os.environ.get('ANDROID_HOME', '/workspace/.cloud-setup/android-sdk'))
cache = sdk.parent / 'android-sdk-downloads'
cache.mkdir(parents=True, exist_ok=True)
metadata = cache / 'repository2-3.xml'


def download(url, destination):
    subprocess.run(['curl', '--fail', '--show-error', '--silent', '--location',
                    '--retry', '2', '--max-time', '600', url, '-o', str(destination)], check=True)


download('https://dl.google.com/android/repository/repository2-3.xml', metadata)
repository = ET.parse(metadata).getroot()
wanted = {
    'platforms;android-36': sdk / 'platforms/android-36',
    'build-tools;36.0.0': sdk / 'build-tools/36.0.0',
    'platform-tools': sdk / 'platform-tools',
    'cmdline-tools;latest': sdk / 'cmdline-tools/latest',
}
installed = []
for package_id, destination in wanted.items():
    if (destination / 'source.properties').is_file():
        print('Retaining installed SDK component:', package_id, flush=True)
        continue
    if destination.exists():
        raise SystemExit(f'Refusing to overwrite an unrecognized SDK directory: {destination}')
    package = next(p for p in repository.iter('remotePackage') if p.get('path') == package_id)
    archive = next(a for a in package.iter('archive') if a.findtext('host-os') in [None, 'linux'])
    complete = archive.find('complete')
    relative = complete.findtext('url')
    checksum = complete.find('checksum')
    algorithm = checksum.get('type', 'sha1')
    expected = checksum.text.strip().lower()
    url = urllib.parse.urljoin('https://dl.google.com/android/repository/', relative)
    archive_path = cache / Path(relative).name
    print('Downloading SDK component:', package_id, flush=True)
    if not archive_path.exists():
        download(url, archive_path)
    with archive_path.open('rb') as stream:
        actual = hashlib.file_digest(stream, algorithm).hexdigest()
    if actual != expected:
        raise SystemExit(f'Official Android SDK checksum mismatch: {archive_path}')
    with tempfile.TemporaryDirectory(dir=cache) as staging:
        staging = Path(staging)
        with zipfile.ZipFile(archive_path) as zipped:
            for entry in zipped.infolist():
                path = staging / entry.filename
                if not path.resolve().is_relative_to(staging.resolve()):
                    raise SystemExit('Invalid SDK archive path')
                zipped.extract(entry, staging)
                permissions = stat.S_IMODE(entry.external_attr >> 16)
                if permissions and not entry.is_dir():
                    path.chmod(permissions)
        roots = list(staging.iterdir())
        if len(roots) != 1 or not roots[0].is_dir():
            raise SystemExit('Unexpected SDK archive layout')
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.move(str(roots[0]), str(destination))
    installed.append({'package': package_id, 'url': url, 'algorithm': algorithm, 'checksum': expected})
    print('Installed verified SDK component:', destination, flush=True)

# This is the noninteractive equivalent of InstallAndroidDependencies with
# AcceptAndroidSdkLicenses=true. Keep existing license acceptances.
licenses = sdk / 'licenses'
licenses.mkdir(parents=True, exist_ok=True)
for license in repository.findall('license'):
    if license.get('id') == 'android-sdk-license':
        digest = hashlib.sha1(license.text.strip().encode()).hexdigest()
        target = licenses / 'android-sdk-license'
        accepted = target.read_text().splitlines() if target.exists() else []
        if digest not in accepted:
            with target.open('a') as output:
                output.write(digest + '\n')
if installed:
    (cache / 'installed-components.json').write_text(json.dumps(installed, indent=2) + '\n')
