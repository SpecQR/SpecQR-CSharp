#!/usr/bin/env python3
"""Optional exhaustive internal differential check; Python/Node/.NET BCL only.

python3 tools/matrix-check/run.py --baseline /path/to/SpecQR --dotnet /path/to/dotnet
The baseline must be a clean checkout at the pinned commit. No network requests,
third-party packages, or alternative QR runtime implementations are installed.
"""

import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys


PINNED_JS_COMMIT = "15ad15e5c770ea0e39072f8f88b2733018f02ffd"
ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
SOURCES = ["src/SpecQR/QRTables.cs", "src/SpecQR/ReedSolomon.cs", "src/SpecQR/MatrixBuilder.cs"]
HARNESS = ["tools/matrix-check/MatrixCheck.csproj", "tools/matrix-check/Program.cs",
           "tools/matrix-check/reference.mjs", "tools/matrix-check/run.py"]


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def run(command, cwd=ROOT):
    env = dict(os.environ)
    env.update(DOTNET_CLI_TELEMETRY_OPTOUT="1", DOTNET_SKIP_FIRST_TIME_EXPERIENCE="1",
               DOTNET_GENERATE_ASPNET_CERTIFICATE="false", DOTNET_NOLOGO="1")
    result = subprocess.run(command, cwd=cwd, env=env, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
    if result.returncode:
        sys.stderr.buffer.write(result.stdout)
        sys.stderr.buffer.write(result.stderr)
        raise RuntimeError(f"{Path(command[0]).name} failed with exit status {result.returncode}")
    return result


def executable(value):
    found = shutil.which(value)
    if found is None:
        raise ValueError(f"Required executable was not found: {value}")
    return str(Path(found).resolve())


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--node", default="node")
    parser.add_argument("--frameworks", nargs="+", choices=["net8.0", "net10.0"], default=["net8.0", "net10.0"])
    parser.add_argument("--output", type=Path, default=ROOT / "artifacts" / "matrix-check.json")
    args = parser.parse_args()
    baseline = args.baseline.resolve(strict=True)
    dotnet, node = executable(args.dotnet), executable(args.node)
    baseline_revision = run(["git", "rev-parse", "HEAD"], baseline).stdout.decode().strip()
    if baseline_revision != PINNED_JS_COMMIT:
        raise ValueError(f"Expected SpecQR JS commit {PINNED_JS_COMMIT}; found {baseline_revision}")
    if run(["git", "status", "--porcelain"], baseline).stdout.strip():
        raise ValueError("The SpecQR JS baseline must be a clean checkout")

    source_hashes = {name: sha256((ROOT / name).read_bytes()) for name in SOURCES}
    harness_hashes = {name: sha256((ROOT / name).read_bytes()) for name in HARNESS}
    print("Generating the pinned JavaScript comparison output...", flush=True)
    expected = run([node, str(HERE / "reference.mjs"), str(baseline)]).stdout
    manifest = {
        "status": "passed",
        "baselineRepository": "https://github.com/SpecQR/SpecQR",
        "baselineCommit": baseline_revision,
        "sourceSha256": source_hashes,
        "harnessSha256": harness_hashes,
        "sdkVersion": run([dotnet, "--version"]).stdout.decode().strip(),
        "nodeVersion": run([node, "--version"]).stdout.decode().strip(),
        "comparisonOutputSha256": sha256(expected),
        "comparisonOutputLines": len(expected.splitlines()),
        "testedTargets": [],
        "scope": "All versions 1–40, four ECC levels, all eight fixed masks and automatic selection; zero, 255, and deterministic arbitrary full-capacity data streams. Exact interleaved codewords, matrix modules, mask selection, and all penalty values are compared by SHA-256 or literal output. All 65,536 GF products and generator/remainder degrees 1–255 are also compared."
    }
    for framework in dict.fromkeys(args.frameworks):
        print(f"Building and checking {framework}...", flush=True)
        run([dotnet, "build", str(HERE / "MatrixCheck.csproj"), "--framework", framework,
             "--configuration", "Release", "--verbosity", "quiet"])
        result = run([dotnet, str(HERE / "bin" / "Release" / framework / "MatrixCheck.dll")])
        if result.stdout != expected:
            expected_lines, actual_lines = expected.splitlines(), result.stdout.splitlines()
            difference = next((i + 1 for i, (a, b) in enumerate(zip(expected_lines, actual_lines)) if a != b),
                              min(len(expected_lines), len(actual_lines)) + 1)
            raise RuntimeError(f"{framework} differs from the baseline at output line {difference}")
        metadata = [line[7:] for line in result.stderr.decode().splitlines() if line.startswith("RESULT ")]
        if len(metadata) != 1:
            raise RuntimeError("The harness did not report one actual runtime identity")
        target = json.loads(metadata[0])
        expected_target = ".NETCoreApp,Version=v" + framework.removeprefix("net")
        if target["targetFramework"] != expected_target:
            raise RuntimeError(f"Requested {framework} but the executed harness reports {target['targetFramework']}")
        runtime_major = framework.removeprefix("net").split(".")[0]
        if not target["runtime"].startswith(f".NET {runtime_major}."):
            raise RuntimeError(f"The requested target ran under an unexpected runtime: {target['runtime']}")
        target.update(status="passed", outputSha256=sha256(result.stdout))
        manifest["testedTargets"].append(target)
        print(f"PASS {framework}: {target['matrices']} exact matrices; {target['runtime']}; {target['architecture']}", flush=True)
    if any(sha256((ROOT / name).read_bytes()) != digest for name, digest in source_hashes.items()):
        raise RuntimeError("Runtime source files changed during verification; rerun against a fixed revision")
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(manifest, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"PASS comparison SHA-256 {manifest['comparisonOutputSha256']}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, RuntimeError) as error:
        sys.exit(f"FAIL: {error}")
