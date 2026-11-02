#!/usr/bin/env python3
"""Compile every platform branch against each Unity 2022.3+ installation (Unity 6 included)."""
import argparse
from pathlib import Path
import plistlib
import re
import subprocess

MINIMUM = (2022, 3)  # package.json "unity"
PLATFORMS = ['UNITY_EDITOR', 'UNITY_ANDROID', 'UNITY_IOS']
HUB_ROOTS = [Path('/Applications/Unity/Hub/Editor'), Path.home() / 'Applications/Unity/Hub/Editor']

parser = argparse.ArgumentParser()
parser.add_argument('--unity', type=Path, action='append',
                    help='Path to Unity.app/Contents; repeat for several editors. '
                         'Default: every Unity Hub editor that is 2022.3 or newer.')
args = parser.parse_args()
root = Path(__file__).resolve().parents[1]


def version(contents):
    info = contents / 'Info.plist'
    if not info.exists():
        raise SystemExit('Not a Unity.app/Contents directory: ' + str(contents))
    raw = plistlib.loads(info.read_bytes())['CFBundleVersion']
    match = re.match(r'(\d+)\.(\d+)', raw)
    if not match:
        raise SystemExit('Unknown Unity version ' + raw + ' in ' + str(contents))
    return raw, (int(match[1]), int(match[2]))


editors = args.unity or sorted(p for hub in HUB_ROOTS for p in hub.glob('*/Unity.app/Contents'))
selected = []
for contents in editors:
    raw, number = version(contents)
    if number < MINIMUM:
        if args.unity:
            raise SystemExit('Unity ' + raw + ' is older than the package minimum 2022.3')
        print('Skipping Unity ' + raw + ': older than 2022.3')
        continue
    selected.append((raw, contents))
if not selected:
    raise SystemExit('No Unity 2022.3+ installation found; pass --unity /path/to/Unity.app/Contents')

for raw, unity in selected:
    # The Roslyn compiler and reference assemblies Unity itself uses for scripts.
    dotnet = unity / 'NetCoreRuntime/dotnet'
    csc = unity / 'DotNetSdkRoslyn/csc.dll'
    refs = sorted((unity / 'NetStandard/ref/2.1.0').glob('*.dll'))
    engine = sorted((unity / 'Managed/UnityEngine').glob('*.dll'))
    if not dotnet.exists() or not csc.exists() or not refs or not engine:
        raise SystemExit('Unity ' + raw + ' compiler or reference assemblies are missing')
    out = root / '.validation' / raw
    out.mkdir(parents=True, exist_ok=True)
    for platform in PLATFORMS:
        player = [p for p in engine if platform == 'UNITY_EDITOR' or not p.name.startswith('UnityEditor')]
        response = ['-nologo', '-target:library', '-langversion:9.0', '-warnaserror+', '-nostdlib+',
                    '-define:' + platform, '-out:' + str(out / (platform + '.dll'))]
        response += ['-r:' + str(p) for p in refs + player]
        response += [str(root / 'Runtime/Cluefolk.cs')]
        path = out / (platform + '.rsp')
        path.write_text('\n'.join('"' + line + '"' for line in response))
        subprocess.run([str(dotnet), 'exec', str(csc), '-noconfig', '@' + str(path)], check=True)
        print('Unity ' + raw + ' ' + platform + ': compiled with warnings as errors')
print('Verified against Unity ' + ', '.join(raw for raw, _ in selected))
