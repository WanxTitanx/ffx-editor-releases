#!/usr/bin/env python3
"""Fetch the existing, hash-pinned Windows dependencies; never run an installer.

The Editor's source, viewers and build dependencies are already in Git. This
optional step supplies the offline WebView2 installer and the legacy FSBank tool
from the exact previously released package. Downloads stay outside Git.
"""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import tempfile
import urllib.request
import zipfile

ROOT = Path(__file__).resolve().parents[1]
BASE = 'https://github.com/WanxTitanx/ffx-editor-releases/releases/download/'
WEBVIEW = ('prerequisites/MicrosoftEdgeWebView2RuntimeInstallerX64.exe',
           '82b2d8a7013e0c0ea15d48ff4742ee3778ba16bd8b7b4a47876645b3e48d4016')
WINDOWS = ('v2.245.1.0/Spira-Reforge-Studio-v2.245.1.0-win-x64.zip',
           'bff54748046383d34e314f4982a6884343773b26c78f723b49ce1585ec25a563')

def digest(path):
    with path.open('rb') as stream:
        return hashlib.file_digest(stream, 'sha256').hexdigest()

def download(relative_url, expected):
    cache = ROOT / '.downloads'
    cache.mkdir(exist_ok=True)
    target = cache / relative_url.rsplit('/', 1)[1]
    if target.is_file() and digest(target) == expected:
        return target
    request = urllib.request.Request(BASE + relative_url, headers={'User-Agent': 'Spira-Reforge-Studio-source-bootstrap'})
    descriptor, temporary = tempfile.mkstemp(dir=cache, suffix='.part')
    try:
        with os.fdopen(descriptor, 'wb') as output, urllib.request.urlopen(request, timeout=90) as response:
            shutil.copyfileobj(response, output)
        if digest(Path(temporary)) != expected:
            raise ValueError('Downloaded dependency failed SHA-256 verification')
        os.replace(temporary, target)
    finally:
        Path(temporary).unlink(missing_ok=True)
    return target

def windows_dependencies():
    installer = download(*WEBVIEW)
    destination = ROOT / 'ExternalLibs/WindowsPrerequisites/WebView2' / installer.name
    destination.parent.mkdir(parents=True, exist_ok=True)
    shutil.copyfile(installer, destination)
    package = download(*WINDOWS)
    count = 0
    with zipfile.ZipFile(package) as archive:
        for member in archive.infolist():
            # Only the immediate files of this one pinned legacy tool are imported.
            # Do not extract arbitrary archive paths or replace editor source.
            prefix = 'tools/fsbankcl/'
            if member.is_dir() or not member.filename.startswith(prefix):
                continue
            name = member.filename[len(prefix):]
            if not name or '/' in name or '\\' in name or ':' in name or name in {'.', '..'}:
                raise ValueError('Unexpected FSBank archive member')
            target = ROOT / prefix / name
            target.parent.mkdir(parents=True, exist_ok=True)
            with archive.open(member) as source, target.open('wb') as output:
                shutil.copyfileobj(source, output)
            count += 1
    if count == 0:
        raise ValueError('The pinned package contains no FSBank dependency')
    print(f'Verified WebView2 installer and {count} legacy audio dependency files. No installer was executed.')

if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--windows', action='store_true', help='Prepare optional Windows runtime/package dependencies')
    args = parser.parse_args()
    if args.windows:
        windows_dependencies()
    else:
        print('Core build dependencies are included. Use --windows for offline Windows package dependencies.')
