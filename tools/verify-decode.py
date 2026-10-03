#!/usr/bin/env python3
"""Test-only independent ZXing-C++ decoding. No decoder enters the .NET runtime.

Build tests/SpecQR.Tests in Release first. Installs only SHA-256-pinned binary
decoder wheels into --dependency-dir; standard-library Python handles pixels/PNG.
"""
import argparse
import base64
import hashlib
import importlib
import importlib.metadata
import json
import os
from pathlib import Path
import string
import struct
import subprocess
import sys
import zlib

ROOT = Path(__file__).resolve().parents[1]
parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--dotnet', default='dotnet')
parser.add_argument('--framework', choices=['net8.0', 'net10.0'], default='net10.0')
parser.add_argument('--dependency-dir', type=Path, default=ROOT.parent / '.tools' / 'zxing-cpp')
parser.add_argument('--report', type=Path)
parser.add_argument('--no-install', action='store_true')
args = parser.parse_args()
requirements = ROOT / 'tools' / 'zxing-cpp-requirements.txt'
requirement_hash = hashlib.sha256(requirements.read_bytes()).hexdigest()
dependency_dir = args.dependency_dir.resolve()
receipt = dependency_dir / '.verified-requirements-sha256'
if not receipt.exists() or receipt.read_text().strip() != requirement_hash:
    if args.no_install:
        raise SystemExit('Decoder is not installed and hash-verified. No tests were run.')
    dependency_dir.mkdir(parents=True, exist_ok=True)
    subprocess.run([sys.executable, '-m', 'pip', 'install', '--target', str(dependency_dir), '--upgrade',
                    '--only-binary=:all:', '--no-deps', '--require-hashes', '-r', str(requirements)], check=True)
    receipt.write_text(requirement_hash + '\n')
sys.path.insert(0, str(dependency_dir))
zxingcpp = importlib.import_module('zxingcpp')
assert importlib.metadata.version('zxing-cpp') == '3.1.1'
assembly = ROOT / 'tests' / 'SpecQR.Tests' / 'bin' / 'Release' / args.framework / 'SpecQR.Tests.dll'
runtime_library = assembly.with_name('SpecQR.dll')
assert assembly.exists(), 'Build the requested test target in Release first.'


def generate(requests):
    process = subprocess.run([args.dotnet, str(assembly), '--json-lines'],
                             input='\n'.join(map(json.dumps, requests)) + '\n',
                             capture_output=True, text=True, check=True)
    results = [json.loads(line) for line in process.stdout.splitlines()]
    assert len(results) == len(requests), 'Test protocol response count differs.'
    for request, result in zip(requests, results):
        assert 'error' not in result, (request, result)
    return results


def decode(matrix):
    width = len(matrix) + 8
    pixels = bytearray([255]) * (width * width)
    for y, row in enumerate(matrix):
        for x, value in enumerate(row):
            pixels[(y + 4) * width + x + 4] = 0 if value == '1' else 255
    scaled = bytearray()
    for y in range(width):
        row = bytearray(value for value in pixels[y * width:(y + 1) * width] for _ in range(3))
        scaled.extend(row * 3)
    result = zxingcpp.read_barcode(memoryview(scaled).cast('B', shape=(width * 3, width * 3)), text_mode=zxingcpp.TextMode.Plain)
    assert result is not None and result.valid, 'ZXing-C++ did not decode a symbol.'
    return result


def decode_png(encoded):
    png = base64.b64decode(encoded)
    assert png[:8] == bytes([137, 80, 78, 71, 13, 10, 26, 10])
    offset = 8
    compressed = bytearray()
    width = height = 0
    while offset < len(png):
        length = struct.unpack('>I', png[offset:offset + 4])[0]
        kind = png[offset + 4:offset + 8]
        data = png[offset + 8:offset + 8 + length]
        expected_crc = struct.unpack('>I', png[offset + 8 + length:offset + 12 + length])[0]
        assert zlib.crc32(kind + data) == expected_crc
        if kind == b'IHDR':
            width, height, depth, color, compression, filtering, interlace = struct.unpack('>IIBBBBB', data)
            assert (depth, color, compression, filtering, interlace) == (8, 6, 0, 0, 0)
        elif kind == b'IDAT':
            compressed.extend(data)
        offset += 12 + length
    raw = zlib.decompress(compressed)
    assert len(raw) == height * (width * 4 + 1)
    luminance = bytearray()
    for y in range(height):
        row = raw[y * (width * 4 + 1):(y + 1) * (width * 4 + 1)]
        assert row[0] == 0
        luminance.extend(row[1::4])
    result = zxingcpp.read_barcode(memoryview(luminance).cast('B', shape=(height, width)), text_mode=zxingcpp.TextMode.Plain)
    assert result is not None and result.valid, 'PNG pixels did not decode.'
    return result


identity = generate([{'command': 'identity'}])[0]
expected_major = args.framework.removeprefix('net').split('.')[0]
assert identity['framework'] == f'.NETCoreApp,Version=v{expected_major}.0', identity
assert identity['runtime'].split('.')[0] == expected_major, identity
assert identity['testAssembly'] == 'SpecQR.Tests' and identity['library'] == 'SpecQR', identity

cases = []
indicators = [f'{i:02}' for i in range(100)] + list(string.ascii_uppercase + string.ascii_lowercase)
for ordinal, indicator in enumerate(indicators):
    options = {'version': 3, 'errorCorrectionLevel': ['L', 'M', 'Q', 'H'][ordinal % 4], 'maskPattern': ordinal % 8}
    for data in [{'mode': 'alphanumeric', 'text': 'ABC%123%%XYZ'}, {'mode': 'byte', 'bytes': [0, 255, 128, 29, 37, 65, 0]}]:
        for route in ['manual', 'options']:
            if route == 'manual':
                request = {'segments': [{'mode': 'fnc1-second', 'applicationIndicator': indicator}, data], 'options': options}
                expected = b'ABC\x1d123%XYZ' if data['mode'] == 'alphanumeric' else bytes(data['bytes'])
            else:
                request = {key: value for key, value in data.items() if key != 'mode'}
                request['options'] = {**options, 'fnc1Second': indicator}
                expected = data['text'].encode() if data['mode'] == 'alphanumeric' else bytes(data['bytes'])
            cases.append((request, indicator.encode() + expected, ']Q5'))
fnc1_count = len(cases)
for text in ['10ABC%DEF', '10ABC%%DEF', '10ABC%DEF\x1d21SER%%IAL', '10ABC\x1d21SERIAL']:
    for optimize in [True, False]:
        for mode in ['auto', 'byte']:
            for controls, prefix, identifier in [({'gs1': True}, b'', ']Q3'), ({'fnc1Second': 'A'}, b'A', ']Q5')]:
                cases.append(({'text': text, 'options': {**controls, 'mode': mode, 'optimizeSegments': optimize}}, prefix + text.encode(), identifier))
percent_count = len(cases) - fnc1_count
for level in ['L', 'M', 'Q', 'H']:
    for mode, text in [('numeric', '012345678901234567890'), ('alphanumeric', 'HELLO WORLD 2026'), ('byte', '日本語 😀 café'), ('kanji', '漢字茗荷')]:
        for mask in range(8):
            cases.append(({'text': text, 'options': {'mode': mode, 'errorCorrectionLevel': level, 'maskPattern': mask}}, text.encode('shift_jis' if mode == 'kanji' else 'utf-8'), None))
for assignment in [0, 127, 128, 16383, 16384, 999999]:
    cases.append(({'segments': [{'mode': 'eci', 'assignmentNumber': assignment}, {'mode': 'byte', 'bytes': [65, 0, 255]}]}, b'A\0\xff', None))
for version in [1, 7, 10, 27, 40]:
    cases.append(({'bytes': [0, 1, 127, 128, 254, 255], 'options': {'version': version, 'maskPattern': version % 8}}, bytes([0, 1, 127, 128, 254, 255]), None))

for start in range(0, len(cases), 100):
    batch = cases[start:start + 100]
    for (request, expected_bytes, identifier), generated in zip(batch, generate([case[0] for case in batch])):
        decoded = decode(generated['matrix'])
        assert decoded.bytes == expected_bytes, (request, decoded.bytes, expected_bytes)
        if identifier:
            assert decoded.symbology_identifier == identifier, (request, decoded.symbology_identifier)
        options = request.get('options', {})
        if 'version' in options:
            assert decoded.extra['Version'] == str(options['version'])
        if 'maskPattern' in options:
            assert decoded.extra['DataMask'] == options['maskPattern']
        if 'errorCorrectionLevel' in options:
            assert decoded.ec_level == options['errorCorrectionLevel']

png_requests = [{'text': text, 'png': True, 'options': {'eci': 26}} for text in ['Portable PNG', '日本語 😀 café', 'A' * 1000]]
for request, result in zip(png_requests, generate(png_requests)):
    assert decode_png(result['png']).bytes == request['text'].encode()

# Independently decode each SA member; matrix fixtures verify the exact header bits.
sa_requests = [
    {'command': 'structured-append', 'text': 'Structured append payload ' * 6, 'options': {'version': 2, 'mode': 'byte'}},
    {'command': 'structured-append', 'bytes': list(range(100)), 'options': {'version': 2}},
]
sa_symbols = 0
for request, result in zip(sa_requests, generate(sa_requests)):
    decoded_parts = [decode(symbol['matrix']) for symbol in result['symbols']]
    expected = request['text'].encode() if 'text' in request else bytes(request['bytes'])
    assert b''.join(part.bytes for part in decoded_parts) == expected
    assert len(decoded_parts) == result['total']
    sa_symbols += len(decoded_parts)

report = {'status': 'passed', 'decoder': 'ZXing-C++ 3.1.1', 'framework': args.framework, 'executedIdentity': identity,
          'librarySha256': hashlib.sha256(runtime_library.read_bytes()).hexdigest(),
          'testAssemblySha256': hashlib.sha256(assembly.read_bytes()).hexdigest(),
          'requirementsSha256': requirement_hash, 'python': sys.version.split()[0],
          'applicationIndicators': len(indicators), 'fnc1SecondSymbols': fnc1_count,
          'highLevelPercentAndSeparatorCases': percent_count, 'decodedSymbols': len(cases),
          'portablePngImages': len(png_requests), 'structuredAppendSymbols': sa_symbols,
          'skipped': 0, 'limitations': ['Synthetic matrices and PNG pixels; no physical-camera or print claim.',
          'ZXing-C++ prepends FNC1-second application indicators to decoded bytes.',
          'Structured Append headers also receive exact matrix fixture checks; this decoder check compares member payloads.']}
if args.report:
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2) + '\n')
print(json.dumps(report, indent=2))
