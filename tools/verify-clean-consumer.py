#!/usr/bin/env python3
"""Isolated source build, public API consumer and deterministic assembly comparison.
Uses only Python's standard library and the selected installed .NET SDK.
"""
import argparse
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import tempfile

parser = argparse.ArgumentParser()
parser.add_argument('--framework', choices=('net8.0', 'net10.0'), required=True)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--temp-root')
args = parser.parse_args()
repo = Path(__file__).resolve().parents[1]
# Framework identity is checked in the consumer, not inferred from dotnet's exit code.
consumer = r'''
using System.Runtime.InteropServices;
using SpecQR;
var expected = args[0];
if (AppContext.TargetFrameworkName != $".NETCoreApp,Version=v{expected[3..]}") throw new Exception("Wrong target framework");
if (Environment.Version.Major != int.Parse(expected[3..].Split('.')[0])) throw new Exception("Wrong runtime major");
var qr = QRCode.Generate("SpecQR C# consumer 漢字 0123456789", new QROptions { Eci = 26 });
if (qr.Size != qr.Version * 4 + 17 || qr.Codewords.Length == 0 || qr.ToPng()[0] != 137 || !qr.ToSvg().Contains("<svg")) throw new Exception("Generation failed");
var elements = GS1.ParseHumanReadable("(01)04912345678904(10)ABC%123");
var gs1 = QRCode.Generate(GS1.CreateElementString(elements), new QROptions { Gs1 = true });
if (!gs1.Planning.Segments.Any(s => s.Mode == QRMode.Byte)) throw new Exception("Literal percent not safe");
var set = QRCode.GenerateStructuredAppend(new string('A', 60), new QRStructuredAppendOptions { QrOptions = new QROptions { Version = 1, ErrorCorrectionLevel = ErrorCorrectionLevel.L } });
if (set.Total < 2 || set.Symbols.Any(s => s.Planning.StructuredAppend is null)) throw new Exception("SA failed");
Console.WriteLine($"PASS clean consumer target={AppContext.TargetFrameworkName} runtime={RuntimeInformation.FrameworkDescription} size={qr.Size} sa={set.Total}");
'''
env = os.environ.copy()
env.update(DOTNET_CLI_TELEMETRY_OPTOUT='1', DOTNET_GENERATE_ASPNET_CERTIFICATE='false', DOTNET_SKIP_FIRST_TIME_EXPERIENCE='1')

def run(command, cwd):
    result = subprocess.run(command, cwd=cwd, env=env, capture_output=True, text=True)
    if result.returncode:
        print(result.stdout)
        print(result.stderr)
        raise SystemExit(result.returncode)
    return result.stdout

with tempfile.TemporaryDirectory(prefix='specqr-consumer-', dir=args.temp_root) as directory:
    root = Path(directory)
    hashes = []
    for lane in ('a', 'b'):
        build = root / lane
        build.mkdir()
        for name in ('Directory.Build.props', 'NuGet.Config', 'global.json'):
            shutil.copyfile(repo / name, build / name)
        shutil.copytree(repo / 'src' / 'SpecQR', build / 'src' / 'SpecQR', ignore=shutil.ignore_patterns('bin', 'obj'))
        project = 'src/SpecQR/SpecQR.csproj'
        run([args.dotnet, 'restore', project, '--configfile', 'NuGet.Config'], build)
        run([args.dotnet, 'build', project, '-c', 'Release', '-f', args.framework, '--no-restore', '--disable-build-servers', '-p:UseSharedCompilation=false', '-m:1',
             '-p:ContinuousIntegrationBuild=true', '-p:IncludeSourceRevisionInInformationalVersion=false',
             f'-p:PathMap={build}=/_/specqr'], build)
        assembly = build / 'src' / 'SpecQR' / 'bin' / 'Release' / args.framework / 'SpecQR.dll'
        hashes.append(hashlib.sha256(assembly.read_bytes()).hexdigest())
        assets = json.loads((build / 'src' / 'SpecQR' / 'obj' / 'project.assets.json').read_text())
        if any(item.get('type') == 'package' for item in assets['libraries'].values()):
            raise SystemExit('Unexpected library NuGet dependency')
        if lane == 'a':
            app = build / 'Consumer'
            app.mkdir()
            (app / 'Consumer.csproj').write_text('<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType></PropertyGroup><ItemGroup><ProjectReference Include="../src/SpecQR/SpecQR.csproj" /></ItemGroup></Project>')
            (app / 'Program.cs').write_text(consumer)
            run([args.dotnet, 'build', 'Consumer', '-c', 'Release', '-f', args.framework, '--disable-build-servers', '-p:UseSharedCompilation=false', '-m:1'], build)
            print(run([args.dotnet, str(app / 'bin' / 'Release' / args.framework / 'Consumer.dll'), args.framework], build).strip())
    if hashes[0] != hashes[1]:
        raise SystemExit(f'Deterministic build mismatch: {hashes}')
    print(json.dumps({'status': 'pass', 'framework': args.framework, 'independentBuilds': 2, 'assemblySha256': hashes[0], 'runtimeNugetPackages': 0}, indent=2))
