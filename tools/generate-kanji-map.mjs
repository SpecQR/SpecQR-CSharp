// Development-only generator. Node's built-in WHATWG Shift_JIS decoder builds
// the same first-code-point mapping used by the user-owned SpecQR and Swift ports.
// The .NET library uses only the emitted static mapping; Node is not a runtime dependency.
import { readFileSync, writeFileSync } from 'node:fs';
import { fileURLToPath } from 'node:url';
import { createHash } from 'node:crypto';

const decoder = new TextDecoder('shift_jis', { fatal: true });
const codes = new Map();
for (const [start, end] of [[0x81, 0x9f], [0xe0, 0xeb]]) {
  for (let lead = start; lead <= end; lead++) {
    for (let trail = 0x40; trail <= 0xfc; trail++) {
      if (trail === 0x7f) continue;
      try {
        const text = decoder.decode(Uint8Array.of(lead, trail));
        const scalars = Array.from(text);
        if (scalars.length === 1 && text !== '\ufffd' && !codes.has(text))
          codes.set(text, lead << 8 | trail);
      } catch { /* Invalid byte pairs are outside the mapping. */ }
    }
  }
}
const bytes = Buffer.alloc(codes.size * 4);
let offset = 0;
for (const [scalar, code] of codes) {
  bytes.writeUInt16BE(scalar.codePointAt(0), offset);
  bytes.writeUInt16BE(code, offset + 2);
  offset += 4;
}
const packed = bytes.toString('base64');
const target = fileURLToPath(new URL('../src/SpecQR/KanjiMap.cs', import.meta.url));
const previous = readFileSync(target, 'utf8');
const markerStart = '    private const string Packed =\n';
const markerEnd = '\n    private static readonly IReadOnlyDictionary<int, int> Values';
const start = previous.indexOf(markerStart) + markerStart.length;
const end = previous.indexOf(markerEnd);
if (start < markerStart.length || end <= start) throw new Error('Kanji map markers not found');
const lines = packed.match(/.{1,120}/g).map((line, i, all) => `        "${line}"${i + 1 === all.length ? ';' : ' +'}`).join('\n');
const generated = previous.slice(0, start) + lines + previous.slice(end);
if (process.argv.includes('--check')) {
  if (generated !== previous) throw new Error('Generated Kanji map differs from the checked-in mapping');
} else writeFileSync(target, generated);
console.log(JSON.stringify({ mappings: codes.size, bytes: bytes.length, sha256: createHash('sha256').update(bytes).digest('hex'), checked: process.argv.includes('--check') }));
