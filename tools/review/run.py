#!/usr/bin/env python3
"""Run the reviewer and integrated suites with actual target identity checks.

Only Python's standard library and installed .NET 8/10 toolchains are required.
The resulting report contains repository-relative source hashes, never local paths.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--output', type=Path, default=Path('artifacts/review-evidence.json'))
args = parser.parse_args()
root = Path(__file__).resolve().parents[2]
dotnet = str(Path(shutil.which(args.dotnet) or args.dotnet).resolve())
env = dict(os.environ, DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1',
           DOTNET_GENERATE_ASPNET_CERTIFICATE='false')

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

source_files = sorted((root / 'src/SpecQR').glob('*.cs')) + [root / name for name in
    ('src/SpecQR/SpecQR.csproj', 'Directory.Build.props', 'NuGet.Config', 'global.json')]
source_hashes = {str(path.relative_to(root)): digest(path) for path in sorted(source_files)}
review_hashes = {str(path.relative_to(root)): digest(path) for path in sorted((root / 'tools/review').glob('*')) if path.is_file()}
report = {'status': 'passed', 'sourceSha256': source_hashes, 'reviewHarnessSha256': review_hashes,
          'sourceSetSha256': hashlib.sha256(json.dumps(source_hashes, sort_keys=True, separators=(',', ':')).encode()).hexdigest(),
          'targets': [], 'unrunByReviewer': ['Windows execution', 'Linux execution', 'Physical camera/print testing']}

def run(arguments, must_succeed=True):
    result = subprocess.run([dotnet, *arguments], cwd=root, env=env, capture_output=True, text=True)
    if must_succeed and result.returncode != 0:
        raise RuntimeError(result.stdout + result.stderr)
    return result

for project in ('tools/review/Review.csproj', 'tests/SpecQR.Tests/SpecQR.Tests.csproj'):
    run(['build', project, '-c', 'Release', '--disable-build-servers', '-p:UseSharedCompilation=false', '-m:1'])
for framework in ('net8.0', 'net10.0'):
    reviewer = root / 'tools/review/bin/Release' / framework / 'Review.dll'
    main = root / 'tests/SpecQR.Tests/bin/Release' / framework / 'SpecQR.Tests.dll'
    reviewed = [json.loads(line) for line in run([str(reviewer), '--expected-framework', framework]).stdout.splitlines()]
    integrated = [json.loads(line) for line in run([str(main), '--expected-framework', framework]).stdout.splitlines()]
    wrong = 'net10.0' if framework == 'net8.0' else 'net8.0'
    rejected = run([str(reviewer), '--expected-framework', wrong], must_succeed=False)
    if rejected.returncode == 0 or 'Framework identity mismatch' not in rejected.stderr:
        raise RuntimeError('Reviewer framework mismatch check did not fail as intended.')
    if reviewed[-1]['status'] != 'passed' or integrated[-1]['status'] != 'passed':
        raise RuntimeError('Suite status did not confirm passing checks.')
    report['targets'].append({'framework': framework, 'reviewer': reviewed,
        'integrated': integrated, 'mismatchedFrameworkRejected': True})
for filename, sha in source_hashes.items():
    if digest(root / filename) != sha:
        raise RuntimeError('Runtime source changed during review verification; run again.')
args.output.parent.mkdir(parents=True, exist_ok=True)
args.output.write_text(json.dumps(report, ensure_ascii=False, indent=2) + '\n', encoding='utf-8')
print(json.dumps({'status': 'passed', 'targets': [item['framework'] for item in report['targets']],
    'sourceSetSha256': report['sourceSetSha256']}, indent=2))
