#!/usr/bin/env python3
"""Independent development-only ZXing Java metadata and correction verification.

Requires a Release net10.0 SpecQR.Tests build, Python 3.9+, and Java 17+.
The sole downloaded dependency is the SHA-256-pinned official ZXing core JAR.
No decoder dependency or source enters the C# library. --prepare-only verifies
the C# fixture generation but explicitly does not claim independent decoding.
"""

import argparse
import base64
import hashlib
import json
from pathlib import Path
import re
import struct
import subprocess
import sys
import urllib.request
import zlib


ROOT = Path(__file__).resolve().parents[1]
JAR_VERSION = "3.5.4"
JAR_SHA256 = "71de5d89341b5fcf5dd89da7f44e84d825d0e084cdf3ec77c9abe26b0f0ceb13"
JAR_URL = f"https://repo.maven.apache.org/maven2/com/google/zxing/core/{JAR_VERSION}/core-{JAR_VERSION}.jar"
ADAPTER = ROOT / "tools" / "zxing-java" / "DecodeSymbols.java"
PNG_SCALE = 3


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()


def b64(data):
    return base64.b64encode(bytes(data)).decode("ascii")


def unb64(data):
    return base64.b64decode(data, validate=True)


def parity(data):
    value = 0
    for byte in data:
        value ^= byte
    return value


def verify_png_pixels(png, matrix, context, scale=PNG_SCALE, color_type=6):
    """Check the actual C# PNG pixels before an independent detector sees them."""
    require(png.startswith(b"\x89PNG\r\n\x1a\n"), context + " PNG signature mismatch")
    position, compressed, dimensions = 8, bytearray(), None
    while position < len(png):
        length = struct.unpack(">I", png[position:position + 4])[0]
        kind = png[position + 4:position + 8]
        data = png[position + 8:position + 8 + length]
        crc = struct.unpack(">I", png[position + 8 + length:position + 12 + length])[0]
        require(zlib.crc32(kind + data) == crc, context + " PNG CRC mismatch")
        if kind == b"IHDR":
            width, height, depth, color, compression, filtering, interlace = struct.unpack(">IIBBBBB", data)
            require((depth, color, compression, filtering, interlace) == (8, color_type, 0, 0, 0),
                    context + " unexpected PNG format")
            dimensions = width, height
        elif kind == b"IDAT":
            compressed.extend(data)
        position += length + 12
    size = len(matrix)
    dimension = (size + 8) * scale
    require(dimensions == (dimension, dimension), context + " PNG scale/margin mismatch")
    raw = zlib.decompress(compressed)
    bytes_per_pixel = 4 if color_type == 6 else 1
    require(len(raw) == dimension * (dimension * bytes_per_pixel + 1), context + " PNG scanline count mismatch")
    white, black = (bytes([255, 255, 255, 255]), bytes([0, 0, 0, 255])) if color_type == 6 else (b"\xff", b"\x00")
    blank = white * dimension
    expected_rows = [blank] * 4 + [white * (4 * scale) +
        b"".join((black if value == "1" else white) * scale for value in row) +
        white * (4 * scale) for row in matrix] + [blank] * 4
    for y in range(dimension):
        row = raw[y * (dimension * bytes_per_pixel + 1):(y + 1) * (dimension * bytes_per_pixel + 1)]
        require(row[0] == 0 and row[1:] == expected_rows[y // scale], context + " PNG module pixels mismatch")


def independent_grayscale_png(matrix, scale):
    """Independent test renderer: grayscale PNG, using only Python struct/zlib."""
    dimension = (len(matrix) + 8) * scale
    raw = bytearray()
    for y in range(dimension):
        raw.append(0)
        for x in range(dimension):
            row, column = y // scale - 4, x // scale - 4
            dark = 0 <= row < len(matrix) and 0 <= column < len(matrix) and matrix[row][column] == "1"
            raw.append(0 if dark else 255)

    def chunk(kind, data):
        return struct.pack(">I", len(data)) + kind + data + struct.pack(">I", zlib.crc32(kind + data))

    header = struct.pack(">IIBBBBB", dimension, dimension, 8, 0, 0, 0, 0)
    return b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", header) + chunk(b"IDAT", zlib.compress(raw)) + chunk(b"IEND", b"")


def verify_decoded(index, record, expected, context):
    require(record.get("ordinal") == index, context + " ordinal mismatch")
    if expected.get("error"):
        require(record.get("error") is not None, context + " was not rejected")
        return
    require("error" not in record, f"{context}: independent decoder rejected symbol: {record.get('error')}")
    if "text" in expected:
        require(unb64(record["textBase64"]).decode("utf-8") == expected["text"], context + " text mismatch")
    if "bytes" in expected:
        require(b"".join(unb64(s) for s in record["byteSegments"]) == bytes(expected["bytes"]), context + " byte payload mismatch")
    if "byteSegments" in expected:
        require(record["byteSegments"] == expected["byteSegments"], context + " byte-segment boundaries mismatch")
    for actual_key, expected_key in [("rawBytesBase64", "rawBytes"), ("sequence", "sequence"),
            ("parity", "parity"), ("ecc", "ecc"), ("symbologyIdentifier", "identifier")]:
        require(record[actual_key] == expected[expected_key], f"{context} {actual_key}: expected {expected[expected_key]}, got {record[actual_key]}")
    if "errorsCorrected" in expected:
        require(record["errorsCorrected"] == expected["errorsCorrected"], context + " corrected-error count mismatch")


def run(command, input_text=None):
    result = subprocess.run(command, input=input_text, capture_output=True, text=True, encoding="utf-8", cwd=ROOT)
    if result.returncode:
        sys.stderr.write(result.stderr)
        sys.stderr.write(result.stdout[:4000])
        raise RuntimeError(f"{Path(command[0]).name} exited with status {result.returncode}")
    return result.stdout


def cases():
    result = []

    def add(identifier, request, expected):
        result.append((identifier, {**request, "png": True, "pngScale": PNG_SCALE}, expected))

    for ordinal, level in enumerate(["L", "M", "Q", "H"]):
        for mask in range(8):
            options = {"version": 4, "errorCorrectionLevel": level, "maskPattern": mask}
            for mode, text in [("numeric", "012345678901234567890123456789"),
                               ("alphanumeric", "SPECQR / 12345 %"),
                               ("kanji", "漢字東京大阪日本語")]:
                add(f"{mode}-{ordinal}-{mask}", {"text": text, "options": {**options, "mode": mode}}, {"text": text})
            text = "e\u0301🙂漢字 café"
            add(f"eci-utf8-{ordinal}-{mask}", {"text": text, "options": {**options, "mode": "byte", "eci": 26}},
                {"text": text, "bytes": list(text.encode()), "identifier": "]Q2"})
            data = [0, 255, 128, 127, 13, 10, 29, 0, 254, 65]
            add(f"binary-{ordinal}-{mask}", {"bytes": data, "options": options}, {"bytes": data})
            add(f"fnc1-alpha-{ordinal}-{mask}", {"segments": [{"mode": "fnc1"},
                {"mode": "alphanumeric", "text": "10LOT%21SER%%IAL"}], "options": options},
                {"text": "10LOT\x1d21SER%IAL", "identifier": "]Q3"})
            text = "10LOT\x1d21SER%IAL"
            add(f"fnc1-byte-{ordinal}-{mask}", {"text": text, "options": {**options, "mode": "byte", "gs1": True}},
                {"text": text, "identifier": "]Q3"})
    for assignment, data, text in [(3, [99, 97, 102, 233], "café"),
                                    (20, [138, 191, 142, 154], "漢字"),
                                    (170, [65, 83, 67, 73, 73], "ASCII")]:
        add(f"eci-{assignment}", {"segments": [{"mode": "eci", "assignmentNumber": assignment},
            {"mode": "byte", "bytes": data}]}, {"text": text, "bytes": data, "identifier": "]Q2"})
    add("mixed-byte-boundaries", {"segments": [{"mode": "eci", "assignmentNumber": 26},
        {"mode": "byte", "text": "café"}, {"mode": "numeric", "text": "12345"},
        {"mode": "byte", "text": "🙂"}, {"mode": "kanji", "text": "漢字"}]},
        {"text": "café12345🙂漢字", "byteSegments": [b64("café".encode()), b64("🙂".encode())], "identifier": "]Q2"})

    # All 135 legal one-based (index,total) pairs, with distinct nontrivial parity.
    for total in range(2, 17):
        for index in range(1, total + 1):
            data = [0, index, total, 255, 128]
            checksum = (total * 17 + index * 31) & 255
            add(f"sa-{index}-{total}", {"bytes": data, "options": {"version": 2, "maskPattern": index % 8,
                "errorCorrectionLevel": ["L", "M", "Q", "H"][total % 4],
                "structuredAppend": {"index": index, "total": total, "parity": checksum}}},
                {"bytes": data, "sequence": (index - 1) * 16 + total - 1, "parity": checksum})
    for ordinal, text in enumerate(["10ABC%DEF", "10ABC%%DEF", "10ABC%DEF\x1d21SER%%IAL", "10ABC\x1d21SERIAL"]):
        for optimize in [True, False]:
            add(f"high-gs1-{ordinal}-{optimize}", {"text": text, "options": {"gs1": True, "optimizeSegments": optimize}},
                {"text": text, "identifier": "]Q3"})
    return result


def high_level_cases():
    result = []

    def text_case(identifier, text, version, level, mode):
        result.append((identifier, {"command": "structured-append", "text": text, "png": True, "pngScale": PNG_SCALE,
            "options": {"version": version, "errorCorrectionLevel": level, "mode": mode}}, {"text": text}))

    text_case("high-numeric", "0123456789" * 13, 1, "M", "numeric")
    text_case("high-alphanumeric", "SPECQR / 12345 " * 10, 2, "Q", "alphanumeric")
    text_case("high-unicode", "e\u0301🙂漢字" * 12, 2, "L", "byte")
    for identifier, data, version, level in [("high-binary-all-bytes", list(range(256)), 2, "M"),
                                            ("high-sixteen-symbols", list(range(240)), 1, "L")]:
        result.append((identifier, {"command": "structured-append", "bytes": data, "png": True, "pngScale": PNG_SCALE,
            "options": {"version": version, "errorCorrectionLevel": level}}, {"bytes": data}))
    segments = [{"mode": "numeric", "text": "012345678901234567890123456789"},
                {"mode": "byte", "text": "café🙂café🙂café🙂"},
                {"mode": "alphanumeric", "text": "SPECQR / 12345 "},
                {"mode": "kanji", "text": "漢字東京大阪日本語"}]
    result.append(("high-manual-mixed", {"command": "structured-append", "segments": segments, "png": True, "pngScale": PNG_SCALE,
        "options": {"version": 2, "errorCorrectionLevel": "M"}}, {"text": "".join(s["text"] for s in segments)}))
    manual_data = list(range(150))
    result.append(("high-manual-binary", {"command": "structured-append",
        "segments": [{"mode": "byte", "bytes": manual_data}], "png": True, "pngScale": PNG_SCALE,
        "options": {"version": 2, "errorCorrectionLevel": "M"}}, {"bytes": manual_data}))
    return result


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--dotnet", default="dotnet")
    parser.add_argument("--framework", choices=["net10.0"], default="net10.0")
    parser.add_argument("--java", default="java")
    parser.add_argument("--dependency-dir", type=Path, default=ROOT / "artifacts" / "zxing-java")
    parser.add_argument("--report", type=Path, default=ROOT / "artifacts" / "zxing-java-report.json")
    parser.add_argument("--no-download", action="store_true")
    parser.add_argument("--prepare-only", action="store_true")
    args = parser.parse_args()
    assembly = ROOT / "tests" / "SpecQR.Tests" / "bin" / "Release" / "net10.0" / "SpecQR.Tests.dll"
    library = assembly.with_name("SpecQR.dll")
    require(assembly.is_file() and library.is_file(), "Build tests/SpecQR.Tests in Release for net10.0 first.")
    assembly_hash, library_hash = digest(assembly), digest(library)
    work = args.dependency_dir.resolve()
    work.mkdir(parents=True, exist_ok=True)

    def generate(requests):
        output = run([args.dotnet, str(assembly), "--json-lines"],
                     "\n".join(json.dumps(r, ensure_ascii=False) for r in requests) + "\n")
        values = [json.loads(line) for line in output.splitlines()]
        require(len(values) == len(requests), "C# JSON-lines response count differs.")
        for request, value in zip(requests, values):
            require("error" not in value, f"C# generation failed for {request}: {value}")
        return values

    identity = generate([{"command": "identity"}])[0]
    require(identity.get("framework") == ".NETCoreApp,Version=v10.0" and
            identity.get("runtime", "").split(".")[0] == "10" and
            identity.get("testAssembly") == "SpecQR.Tests" and identity.get("library") == "SpecQR",
            f"The actual executed test/library identity is incorrect or stale: {identity}")
    java_version = None
    jar = work / f"core-{JAR_VERSION}.jar"
    if not args.prepare_only:
        java_version = run([args.java, "--version"]).splitlines()[0]
        java_major = re.search(r"(?:openjdk|java) (\d+)", java_version)
        require(java_major is not None and int(java_major.group(1)) >= 17,
                f"Java 17 or newer is required; found {java_version}")
        if not jar.exists():
            require(not args.no_download, "The pinned ZXing JAR is missing; no decoder tests ran.")
            with urllib.request.urlopen(JAR_URL, timeout=60) as response:
                data = response.read(4 * 1024 * 1024 + 1)
            require(len(data) <= 4 * 1024 * 1024, "The Maven artifact exceeds the expected resource budget.")
            require(hashlib.sha256(data).hexdigest() == JAR_SHA256, "Downloaded ZXing artifact SHA-256 mismatch.")
            jar.write_bytes(data)
        require(digest(jar) == JAR_SHA256, "ZXing artifact SHA-256 mismatch.")

    tests, groups = cases(), high_level_cases()
    inputs, checks, group_checks = [], [], []

    def enqueue(kind, value, expected, context):
        inputs.append(f"{kind}\t{value}")
        checks.append({"expected": expected, "context": context})

    def add_symbol(identifier, symbol, expected):
        # byte[] is encoded as base64 by System.Text.Json; require the real field.
        raw = symbol["dataCodewords"]
        require(isinstance(raw, str) and len(unb64(raw)) > 0, f"{identifier}: missing complete data codewords")
        expected = {"sequence": None, "parity": None, "identifier": "]Q1", **expected,
                    "rawBytes": raw, "ecc": symbol["errorCorrectionLevel"]}
        enqueue("matrix", ",".join(symbol["matrix"]), expected, identifier + " matrix")
        png = unb64(symbol["png"])
        verify_png_pixels(png, symbol["matrix"], identifier)
        png_path = work / (identifier + ".png")
        png_path.write_bytes(png)
        enqueue("png", str(png_path), expected, identifier + " PNG detection")

    print("Generating scalar, Structured Append, and damaged symbol fixtures...", flush=True)
    generated = generate([test[1] for test in tests])
    for (identifier, _, expected), symbol in zip(tests, generated):
        add_symbol(identifier, symbol, expected)
    # Retain the original default-scale regression and an independently rendered
    # control. This diagnostic never substitutes for any of the strict PNG tests.
    default_index = next(i for i, test in enumerate(tests) if test[0] == "alphanumeric-0-0")
    default_symbol = generate([{**tests[default_index][1], "pngScale": 8}])[0]
    require(default_symbol["matrix"] == generated[default_index]["matrix"], "Default PNG diagnostic matrix differs")
    default_png = unb64(default_symbol["png"])
    verify_png_pixels(default_png, default_symbol["matrix"], "default-scale8 C#", scale=8)
    control_png = independent_grayscale_png(default_symbol["matrix"], 8)
    verify_png_pixels(control_png, default_symbol["matrix"], "default-scale8 independent control", scale=8, color_type=0)
    diagnostic_directory = (args.report.parent / "default-png-diagnostic").resolve()
    diagnostic_directory.mkdir(parents=True, exist_ok=True)
    default_path, control_path = diagnostic_directory / "csharp-scale8.png", diagnostic_directory / "independent-scale8.png"
    default_path.write_bytes(default_png)
    control_path.write_bytes(control_png)
    default_diagnostic = {
        "case": "alphanumeric-0-0", "text": "SPECQR / 12345 %", "version": 4, "ecc": "L", "mask": 0,
        "pixelsPerModule": 8, "quietZoneModules": 4, "width": 328, "height": 328,
        "csharpPngSha256": hashlib.sha256(default_png).hexdigest(),
        "independentGrayscalePngSha256": hashlib.sha256(control_png).hexdigest(),
        "matrixSha256": hashlib.sha256("".join(default_symbol["matrix"]).encode("ascii")).hexdigest(),
        "csharpPixelsMatchMatrix": True,
        "independentPixelsMatchMatrix": True,
        "controlRenderer": "Independent Python standard-library grayscale PNG from the same module matrix",
        "status": "prepared-not-decoded", "javaOutcomesMatch": None,
        "countsAsStrictDecodeSuccess": False
    }
    high_level = generate([group[1] for group in groups])
    for (identifier, _, expected), group in zip(groups, high_level):
        source = bytes(expected["bytes"]) if "bytes" in expected else expected["text"].encode("utf-8")
        require(group["parity"] == parity(source), identifier + " source parity mismatch")
        require(len(group["symbols"]) == group["total"] and 2 <= group["total"] <= 16,
                identifier + " symbol count mismatch")
        if identifier == "high-sixteen-symbols":
            require(group["total"] == 16, "The maximum-count fixture did not generate 16 symbols")
        indices = []
        for index, symbol in enumerate(group["symbols"]):
            indices.append(len(checks))
            add_symbol(f"{identifier}-{index + 1}", symbol,
                       {"sequence": index * 16 + group["total"] - 1, "parity": group["parity"]})
        group_checks.append((identifier, indices, source, "bytes" in expected, group["total"]))

    damage_requests = [{"text": "ECC", "options": {"version": 1, "errorCorrectionLevel": level,
        "maskPattern": mask, "mode": "byte"}} for level in ["L", "M", "Q", "H"] for mask in range(8)]
    damaged = generate(damage_requests)
    for index, symbol in enumerate(damaged):
        matrix = [list(row) for row in symbol["matrix"]]
        # For version 1 these three modules belong to distinct data codewords.
        for row in [20, 16, 12]:
            matrix[row][20] = "0" if matrix[row][20] == "1" else "1"
        enqueue("matrix", ",".join("".join(row) for row in matrix),
                {"text": "ECC", "rawBytes": symbol["dataCodewords"], "ecc": symbol["errorCorrectionLevel"],
                 "sequence": None, "parity": None, "identifier": "]Q1", "errorsCorrected": 3},
                f"three-codeword-damage-{index}")
    enqueue("matrix", ",".join(["0" * 21] * 21), {"error": True}, "invalid-all-white-matrix")
    input_file = work / "inputs.tsv"
    diagnostic_inputs = [f"png\t{default_path}", f"png\t{control_path}"]
    input_file.write_text("\n".join(inputs + diagnostic_inputs) + "\n", encoding="utf-8")
    report = {
        "status": "prepared-not-decoded" if args.prepare_only else "passed",
        "decoder": f"ZXing Java core {JAR_VERSION}", "decoderArtifactUrl": JAR_URL,
        "decoderSha256": None if args.prepare_only else digest(jar), "expectedDecoderSha256": JAR_SHA256,
        "framework": "net10.0", "executedIdentity": identity, "java": java_version,
        "librarySha256": library_hash, "testAssemblySha256": assembly_hash,
        "adapterSha256": digest(ADAPTER), "verificationScriptSha256": digest(Path(__file__)),
        "lowLevelSymbols": len(tests), "structuredAppendHeaders": 0 if args.prepare_only else 135,
        "preparedStructuredAppendHeaders": 135,
        "highLevelSets": len(groups), "highLevelSymbols": sum(g["total"] for g in high_level),
        "pngDetectionDecodes": 0 if args.prepare_only else sum(c["context"].endswith("PNG detection") for c in checks),
        "undamagedMatrixDecodes": 0 if args.prepare_only else sum(c["context"].endswith(" matrix") for c in checks),
        "preparedDecoderInputs": len(checks),
        "defaultScaleDiagnosticInputs": 2,
        "defaultScaleDiagnostic": default_diagnostic,
        "portablePngPixelChecks": sum(c["context"].endswith("PNG detection") for c in checks),
        "pngPixelsPerModule": PNG_SCALE, "pngQuietZoneModules": 4, "pureBarcodeHint": False,
        "damagedSymbolsCorrected": 0 if args.prepare_only else len(damaged),
        "errorsPerDamagedSymbol": 3, "invalidSymbolsRejected": 0 if args.prepare_only else 1,
        "completeCorrectedDataCodewordComparisons": 0 if args.prepare_only else len(checks) - 1,
        "skippedDecodes": len(checks) if args.prepare_only else 0, "assertionsPassed": not args.prepare_only,
        "limitations": [
            "ZXing Java 3.5.4 does not consume the FNC1-second application indicator; that mode is verified separately using ZXing-C++.",
            "Decoder-supported ECI assignments tested here are 3, 20, 26, and 170.",
            "PNG detection uses explicit scale 3, matching the established Swift decoder suite. ZXing Java can miss some otherwise valid scale-8 PNGs; no PURE_BARCODE shortcut or matrix fallback is used.",
            "Synthetic PNG detection and three controlled data-codeword errors do not establish physical-camera, print, or general damage tolerance."
        ]
    }
    failures = []
    if not args.prepare_only:
        output = run([args.java, "-Djava.awt.headless=true", "--class-path", str(jar), str(ADAPTER), str(input_file)])
        # Preserve every decoder result even when a later assertion fails.
        (work / "decoded.jsonl").write_text(output, encoding="utf-8")
        records = [json.loads(line) for line in output.splitlines()]
        require(len(records) == len(checks) + 2, "Java decoder result count differs")
        decoded, diagnostic_records = records[:len(checks)], records[len(checks):]
        passed_indices = set()
        for index, record in enumerate(decoded):
            expected, context = checks[index]["expected"], checks[index]["context"]
            try:
                verify_decoded(index, record, expected, context)
                passed_indices.add(index)
            except (KeyError, ValueError, RuntimeError) as error:
                failures.append({"context": context, "error": str(error)})
        for identifier, indices, source, binary, total in group_checks:
            for kind_offset in [0, 1]:
                context = identifier + (" PNG reconstruction" if kind_offset else " matrix reconstruction")
                try:
                    require(all(index + kind_offset in passed_indices for index in indices), context + " has failed member decodes")
                    parts = sorted((decoded[index + kind_offset] for index in reversed(indices)), key=lambda p: p["sequence"] >> 4)
                    # Reconstruct exclusively from independent decoder metadata.
                    require([part["sequence"] >> 4 for part in parts] == list(range(total)), context + " decoded indices mismatch")
                    require(all((part["sequence"] & 15) + 1 == total for part in parts), context + " decoded totals mismatch")
                    data = (b"".join(unb64(segment) for part in parts for segment in part["byteSegments"]) if binary
                            else b"".join(unb64(part["textBase64"]) for part in parts))
                    require(data == source, context + " out-of-order reconstruction mismatch")
                    require(all(part["parity"] == parity(data) for part in parts), context + " decoder-derived parity mismatch")
                except (KeyError, ValueError, RuntimeError) as error:
                    failures.append({"context": context, "error": str(error)})
        normalized = [{key: value for key, value in record.items() if key != "ordinal"} for record in diagnostic_records]
        matching = normalized[0] == normalized[1]
        default_diagnostic.update(javaOutcomesMatch=matching, java=java_version,
            csharpJavaResult=normalized[0], independentJavaResult=normalized[1],
            status="matching-rejection" if matching and "error" in normalized[0] else "matching-decode" if matching else "mismatch")
        default_expected = {"text": "SPECQR / 12345 %", "rawBytes": default_symbol["dataCodewords"],
                            "ecc": "L", "sequence": None, "parity": None, "identifier": "]Q1"}
        try:
            require(matching, "Java outcomes differ between default C# PNG and independent same-pixel PNG")
            for offset, record in enumerate(diagnostic_records):
                require(record.get("ordinal") == len(checks) + offset, "Default PNG diagnostic ordinal mismatch")
                if "error" not in record:
                    verify_decoded(len(checks) + offset, record, default_expected, "default PNG diagnostic")
        except (KeyError, ValueError, RuntimeError) as error:
            failures.append({"context": "default-scale8 independent control", "error": str(error)})
        report.update(
            status="failed" if failures else "passed", assertionsPassed=not failures,
            failedChecks=len(failures), failures=failures[:10], verifiedStrictChecks=len(passed_indices),
            decodedOutputSha256=hashlib.sha256(output.encode("utf-8")).hexdigest(),
            defaultScalePngDecodeFailures=sum("error" in record for record in diagnostic_records),
            defaultScalePngDecodeSuccesses=sum("error" not in record for record in diagnostic_records),
            pngDetectionDecodes=sum(checks[i]["context"].endswith("PNG detection") for i in passed_indices),
            undamagedMatrixDecodes=sum(checks[i]["context"].endswith(" matrix") for i in passed_indices),
            structuredAppendHeaders=sum(checks[i]["context"].startswith("sa-") and checks[i]["context"].endswith(" matrix") for i in passed_indices),
            damagedSymbolsCorrected=sum(checks[i]["context"].startswith("three-codeword-damage-") for i in passed_indices),
            invalidSymbolsRejected=sum(checks[i]["expected"].get("error", False) for i in passed_indices),
            completeCorrectedDataCodewordComparisons=sum("rawBytes" in checks[i]["expected"] for i in passed_indices)
        )
    (diagnostic_directory / "diagnostic.json").write_text(json.dumps(default_diagnostic, indent=2) + "\n", encoding="utf-8")
    require(digest(assembly) == assembly_hash and digest(library) == library_hash,
            "The test/library assemblies changed during verification; rebuild and rerun")
    args.report.parent.mkdir(parents=True, exist_ok=True)
    args.report.write_text(json.dumps(report, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2, ensure_ascii=False))
    require(not failures, f"{len(failures)} independent Java checks failed; first failures: {failures[:10]}")


if __name__ == "__main__":
    try:
        main()
    except (OSError, ValueError, KeyError, RuntimeError) as error:
        sys.exit(f"FAIL: {error}")
