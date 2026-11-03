#!/usr/bin/env python3
"""Compile and run pure C# security regression tests with Unity's toolchain."""

import argparse
import json
from pathlib import Path
import plistlib
import re
import subprocess
import tempfile

MINIMUM = (2022, 3)
HUB_ROOTS = [Path('/Applications/Unity/Hub/Editor'),
             Path.home() / 'Applications/Unity/Hub/Editor']

parser = argparse.ArgumentParser()
parser.add_argument('--unity', type=Path,
                    help='Path to Unity.app/Contents. Defaults to the newest '
                         'installed Unity 2022.3 or later.')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]
package_version = json.loads((root / 'package.json').read_text())['version']
manifest_values = dict(
    line.split('=', 1) for line in
    (root / 'Native~/Cluefolk.xcframework.integrity').read_text().splitlines()
    if line)


def version(contents):
    info = contents / 'Info.plist'
    if not info.exists():
        raise SystemExit('Not a Unity.app/Contents directory: ' + str(contents))
    raw = plistlib.loads(info.read_bytes())['CFBundleVersion']
    match = re.match(r'(\d+)\.(\d+)', raw)
    if not match:
        raise SystemExit('Unknown Unity version ' + raw)
    return raw, (int(match[1]), int(match[2]))


if args.unity:
    unity = args.unity
    raw, number = version(unity)
    if number < MINIMUM:
        raise SystemExit('Unity ' + raw + ' is older than 2022.3')
else:
    choices = []
    for hub in HUB_ROOTS:
        for contents in hub.glob('*/Unity.app/Contents'):
            raw, number = version(contents)
            if number >= MINIMUM:
                choices.append((number, raw, contents))
    if not choices:
        raise SystemExit('No Unity 2022.3+ installation found; pass --unity')
    _, raw, unity = max(choices)

dotnet = unity / 'NetCoreRuntime/dotnet'
csc = unity / 'DotNetSdkRoslyn/csc.dll'
refs = sorted((unity / 'NetStandard/ref/2.1.0').glob('*.dll'))
runtimes = sorted((unity / 'NetCoreRuntime/shared/Microsoft.NETCore.App').iterdir())
if not dotnet.exists() or not csc.exists() or not refs or not runtimes:
    raise SystemExit('Unity compiler, references or .NET runtime are missing')

out = root / '.validation' / 'security-tests'
out.mkdir(parents=True, exist_ok=True)
assembly = out / 'SecurityTests.dll'
sources = [
    root / 'Runtime/AppearanceValidation.cs',
    root / 'Runtime/IdentityProviderRunner.cs',
    root / 'Runtime/Cluefolk.cs',
    root / 'Editor/GradleDependency.cs',
    root / 'Editor/ArtifactIntegrity.cs',
    root / 'Tools~/SecurityTests.cs',
]
command = [str(dotnet), 'exec', str(csc), '-noconfig', '-nologo',
           '-target:exe', '-langversion:9.0', '-warnaserror+', '-nostdlib+',
           '-out:' + str(assembly)]
command += ['-r:' + str(path) for path in refs]
command += [str(path) for path in sources]
subprocess.run(command, check=True)

runtime_version = runtimes[-1].name
(out / 'SecurityTests.runtimeconfig.json').write_text(json.dumps({
    'runtimeOptions': {
        'tfm': 'net6.0',
        'framework': {
            'name': 'Microsoft.NETCore.App',
            'version': runtime_version,
        },
    },
}))
subprocess.run([str(dotnet), str(assembly),
                str(root / 'Native~/Cluefolk.xcframework'),
                str(root / 'Native~/Cluefolk.xcframework.integrity'),
                package_version], check=True)

integrity = root / 'Tools~/ios-framework-integrity.py'
subprocess.run(['python3', str(integrity), 'verify',
                str(root / 'Native~/Cluefolk.xcframework'),
                str(root / 'Native~/Cluefolk.xcframework.integrity'),
                package_version, manifest_values['widget']], check=True)


def fail(command, message):
    result = subprocess.run(command, cwd=root, text=True,
                            stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if result.returncode == 0 or message not in result.stdout:
        raise SystemExit('Expected release-tool failure containing ' +
                         repr(message) + ', got:\n' + result.stdout)


# Exercise all pre-build fail-closed branches without invoking Xcode or
# changing the bundled framework.
with tempfile.TemporaryDirectory(prefix='cluefolk-ios-source-') as temporary:
    source = Path(temporary)
    cluefolk = source / 'Sources/Cluefolk'
    scripts = source / 'Scripts'
    cluefolk.mkdir(parents=True)
    scripts.mkdir()
    sdk = cluefolk / 'Cluefolk.swift'
    widget = cluefolk / 'WidgetAssets.swift'
    sdk.write_text('public static let sdkVersion = "999.0.0"\n')
    widget.write_text('// Generated from widget build 0123456789abcdef.\n'
                      'enum WidgetAssets {\n'
                      '  static let version = "0123456789abcdef"\n}\n')
    (scripts / 'build-xcframework.sh').write_text('#!/bin/sh\nexit 99\n')
    subprocess.run(['git', 'init', '-q'], cwd=source, check=True)
    subprocess.run(['git', 'config', 'user.name', 'Cluefolk Tests'],
                   cwd=source, check=True)
    subprocess.run(['git', 'config', 'user.email', 'tests@cluefolk.invalid'],
                   cwd=source, check=True)
    subprocess.run(['git', 'add', '.'], cwd=source, check=True)
    subprocess.run(['git', 'commit', '-qm', 'fixture'], cwd=source, check=True)
    update = [str(root / 'Tools~/update-ios-framework.sh'), str(source),
              '0123456789abcdef']
    fail(update, 'does not match Unity package')

    sdk.write_text('public static let sdkVersion = "' + package_version + '"\n')
    subprocess.run(['git', 'add', '.'], cwd=source, check=True)
    subprocess.run(['git', 'commit', '-qm', 'matching version'],
                   cwd=source, check=True)
    fail(update[:-1] + ['fedcba9876543210'],
         'does not match expected fedcba9876543210')
    fail(update, 'must be exactly at tag ' + package_version)

    sdk.write_text(sdk.read_text() + '// dirty\n')
    fail(update, 'checkout is dirty')

print('Security tests compiled and ran with Unity ' + raw)
