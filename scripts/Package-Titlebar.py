"""Package the local 1.3.1 title-bar build without including user data."""

import argparse
from datetime import datetime, timezone
import difflib
import hashlib
import json
import re
from pathlib import Path
import subprocess
import zipfile


ROOT = Path(__file__).resolve().parent.parent
CHANGE_PATHS = [
    'Ui/Resources/Languages/en-us.xaml',
    'Ui/Resources/Languages/zh-cn.xaml',
    'Ui/Service/ConfigurationService.cs',
    'Ui/View/Host/TabWindowView.xaml',
    'Ui/View/Host/TabWindowView.xaml.cs',
    'Ui/View/Host/TabWindowViewModel.cs',
    'Ui/View/MainWindowView.xaml',
    'Ui/View/Settings/General/GeneralSettingViewModel.cs',
    'readme.md',
    'scripts/Build-Titlebar.ps1',
    'scripts/Titlebar-Build.targets',
    'scripts/Package-Titlebar.py',
    'scripts/titlebar-smoke/Program.cs',
    'scripts/titlebar-smoke/TitlebarButtonSmoke.csproj',
    'docs/titlebar-build.md',
]
PREFIX = '1Remote-1.3.1-titlebar-button'


def git(*args):
    return subprocess.run(
        ['git', '-c', f'safe.directory={ROOT.as_posix()}', '-C', str(ROOT), *args],
        check=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE,
    ).stdout


def sha(data):
    return hashlib.sha256(data).hexdigest()


def archive(path, entries):
    # Never replace an existing delivery silently.
    with zipfile.ZipFile(path, 'x', zipfile.ZIP_DEFLATED, compresslevel=6) as output:
        for name, data in sorted(entries.items()):
            output.writestr(name, data)
    with zipfile.ZipFile(path) as output:
        if output.testzip() is not None:
            raise ValueError(f'Invalid ZIP: {path}')
    print(f'{path.name}: {path.stat().st_size:,} bytes')


def create_patch():
    patch = git('diff', '--binary', '--no-ext-diff', '1.3.1', '--', *CHANGE_PATHS)
    tracked = set(git('ls-files', '-z').decode('utf-8').split('\0'))
    for name in CHANGE_PATHS:
        if name not in tracked:
            contents = (ROOT / name).read_text(encoding='utf-8-sig')
            patch += f'diff --git a/{name} b/{name}\nnew file mode 100644\n'.encode('utf-8')
            patch += ''.join(difflib.unified_diff(
                [], contents.splitlines(keepends=True), fromfile='/dev/null', tofile=f'b/{name}',
            )).encode('utf-8')
    return patch


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--previous-publish', type=Path)
    parser.add_argument('--previous-source', type=Path)
    parser.add_argument('--output', type=Path, help='Delivery directory; defaults to artifacts/dist.')
    parser.add_argument('--prefix', default=PREFIX, help='Filename prefix for the delivery.')
    args = parser.parse_args()
    if bool(args.previous_publish) != bool(args.previous_source):
        parser.error('Specify both --previous-publish and --previous-source for an update.')

    artifacts = ROOT / 'artifacts'
    publish = artifacts / '1Remote'
    out = args.output.resolve() if args.output else artifacts / 'dist'
    prefix = args.prefix
    if not prefix or '/' in prefix or '\\' in prefix:
        parser.error('--prefix must be a filename prefix without directory separators.')
    out.mkdir(parents=True, exist_ok=True)
    proof_path = artifacts / 'qa/verification.log'
    proof = proof_path.read_text(encoding='utf-8-sig')
    checks = re.findall(r'^SUCCESS: (\d+) checks passed$', proof, re.MULTILINE)
    if not checks or int(checks[-1]) <= 0:
        raise ValueError('Run Build-Titlebar.ps1 -Verify successfully before packaging.')
    files = {p.relative_to(publish).as_posix(): p.read_bytes()
             for p in publish.rglob('*') if p.is_file()}
    required = {'1Remote.exe', '1Remote.dll', 'coreclr.dll', 'hostfxr.dll',
                'PresentationFramework.dll', 'MSTSCLib.dll', 'AxMSTSCLib.dll'}
    if not required.issubset(files):
        raise ValueError('The self-contained publish is incomplete.')
    if any(name.lower() == '1remote.json' or '.db' in Path(name).suffix.lower()
           or '.db-' in name.lower() for name in files):
        raise ValueError('The publish directory contains user data.')

    patch_bytes = create_patch()
    info = {
        'upstreamTag': '1.3.1',
        'sourceCommit': git('rev-parse', 'HEAD').decode().strip(),
        'sourceBranch': git('branch', '--show-current').decode().strip(),
        'sourceDirty': bool(git('status', '--porcelain', '--untracked-files=all').strip()),
        'sourcePatchSha256': sha(patch_bytes),
        'submodules': git('submodule', 'status').decode().splitlines(),
        'builtAtUtc': datetime.now(timezone.utc).isoformat(),
        'target': 'net10.0-windows10.0.19041.0 / win-x64 / self-contained',
        'titlebarControl': 'Main window, after search box and before menu',
        'titlebarShortcut': None,
        'windowTitleControl': '1Remote suffix toggle in the main window, beside the session title bar switch',
        'smokeChecksPassed': int(checks[-1]),
        'liveRdpTested': False,
        'executableSha256': sha(files['1Remote.exe']),
        'assemblySha256': sha(files['1Remote.dll']),
    }
    readme = (ROOT / 'docs/titlebar-build.md').read_bytes()
    common = {'BUILD-INFO.json': (json.dumps(info, ensure_ascii=False, indent=2) + '\n').encode('utf-8'),
              '使用说明.md': readme, 'LICENSE': (ROOT / 'LICENSE').read_bytes()}
    deliveries = []
    if args.previous_publish:
        if (ROOT / 'Ui/Assert.cs').read_bytes() != (args.previous_source / 'Ui/Assert.cs').read_bytes():
            raise ValueError('Encryption parameter source differs from the previous custom build.')
        removed = [p.relative_to(args.previous_publish).as_posix()
                   for p in args.previous_publish.rglob('*')
                   if p.is_file() and p.relative_to(args.previous_publish).as_posix() not in files]
        if removed:
            raise ValueError(f'An incremental update would leave obsolete files: {removed}')
        changed = {name: data for name, data in files.items()
                   if not (args.previous_publish / name).is_file()
                   or sha(data) != sha((args.previous_publish / name).read_bytes())}
        update = out / f'{prefix}-update.zip'
        archive(update, {**changed, **common})
        deliveries.append(update)
        print('Updated program files:', ', '.join(sorted(changed)))

    profile = {'General': {'CurrentLanguageCode': 'zh-cn', 'TabWindowShowTitleBar': False,
                           'TabWindowShowWindowTitle': True,
                           'DoNotCheckNewVersion': True}}
    portable = {**files, **common,
                '1Remote.json': (json.dumps(profile, ensure_ascii=False, indent=2) + '\n').encode('utf-8'),
                'FORCE_INTO_PORTABLE_MODE': b'Portable profile\n'}
    full = out / f'{prefix}-win-x64.zip'
    archive(full, {'1Remote-titlebar/' + name: data for name, data in portable.items()})
    deliveries.append(full)

    patch_path = out / f'{prefix}.patch'
    with patch_path.open('xb') as patch_file:
        patch_file.write(patch_bytes)
    git('apply', '--reverse', '--check', str(patch_path))
    deliveries.append(patch_path)
    (out / 'BUILD-INFO.json').write_bytes(common['BUILD-INFO.json'])
    (out / '使用说明.md').write_bytes(readme)
    (out / 'SHA256.txt').write_text(
        ''.join(f'{sha(path.read_bytes())}  {path.name}\n' for path in deliveries), encoding='utf-8',
    )
    print('ZIP integrity, program hashes, patch reverse check, and data exclusions verified.')


if __name__ == '__main__':
    main()
