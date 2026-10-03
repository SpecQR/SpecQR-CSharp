// Development only. Invokes the owner's pinned JavaScript implementation; never shipped in the runtime.
import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { gzipSync } from 'node:zlib';
import { createHash } from 'node:crypto';
import { createRequire } from 'node:module';
import assert from 'node:assert/strict';

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const upstream = path.resolve(process.argv[2] ?? '../baselines/SpecQR');
const expectedCommit = '15ad15e5c770ea0e39072f8f88b2733018f02ffd';
const commit = execFileSync('git', ['-C', upstream, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
assert.equal(commit, expectedCommit);
execFileSync('git', ['-C', upstream, 'diff', '--quiet', 'HEAD', '--', 'src', 'package.json']);
const load = relative => import(pathToFileURL(path.join(upstream, relative)).href);
const api = await load('src/index.js');
const { normalizeOptions } = await load('src/options.js');
const { selectPlanForInput, selectPlanForManualSegments } = await load('src/internal/planning.js');
const { normalizeManualSegments, encodeSegments } = await load('src/encoding/modes.js');
const { buildResultArtifact } = await load('src/internal/build.js');
const require = createRequire(path.join(path.resolve(process.env.SPECQR_DEV_NODE_MODULES ?? upstream), 'package.json'));
const imported = require('nayuki-qr-code-generator');
const nayuki = imported.default ?? imported;
assert.equal(require('nayuki-qr-code-generator/package.json').version, '1.8.0');
const eccMap = { L: nayuki.QrCode.Ecc.LOW, M: nayuki.QrCode.Ecc.MEDIUM, Q: nayuki.QrCode.Ecc.QUARTILE, H: nayuki.QrCode.Ecc.HIGH };
const cases = [], estimates = [], capacities = [];
let independentMatrices = 0;
const jsSegments = segments => segments.map(s => ['numeric','alphanumeric','byte','kanji'].includes(s.mode) ? {mode:s.mode,data:s.bytes ?? s.text} : s);
function referenceSegments(r) {
  return (r.segments ?? (r.bytes ? [{mode:'byte',bytes:r.bytes}] : [{mode:r.options.mode,text:r.text}])).map(s => {
    switch(s.mode) {
      case 'numeric': return nayuki.QrSegment.makeNumeric(s.text);
      case 'alphanumeric': return nayuki.QrSegment.makeAlphanumeric(s.text);
      case 'byte': return nayuki.QrSegment.makeBytes(s.bytes ?? Array.from(new TextEncoder().encode(s.text)));
      case 'eci': return nayuki.QrSegment.makeEci(s.assignmentNumber);
      default: return null;
    }
  });
}
function add(r, correctedOptions = null) {
  const opts = normalizeOptions({...r.options,...correctedOptions,output:'matrix',diagnostics:true});
  const input = r.bytes ? Uint8Array.from(r.bytes) : r.text ?? '';
  const segments = r.segments ? normalizeManualSegments(jsSegments(r.segments)) : null;
  const plan = segments ? selectPlanForManualSegments(segments,opts) : selectPlanForInput(input,opts);
  const built = buildResultArtifact(plan,opts);
  const result = segments ? api.generateSegments(jsSegments(r.segments),opts) : api.generate(input,opts);
  const matrix = result.matrix.map(row=>row.map(Number).join(''));
  let independent = false;
  if (opts.version !== 'auto' && opts.maskPattern !== 'auto' && !opts.gs1 && opts.fnc1Second === false && opts.eci === false) {
    const ref = referenceSegments(r);
    if(ref.every(Boolean)) {
      const q = nayuki.QrCode.encodeSegments(ref,eccMap[opts.errorCorrectionLevel],opts.version,opts.version,opts.maskPattern,false);
      assert.deepEqual(matrix,Array.from({length:q.size},(_,y)=>Array.from({length:q.size},(_,x)=>q.getModule(x,y)?'1':'0').join('')));
      independentMatrices++; independent = true;
    }
  }
  cases.push({ ...r, independent, expected:{ matrix, dataCodewords:Buffer.from(encodeSegments(plan.segments,plan.version,plan.errorCorrectionLevel)).toString('base64'), codewords:Buffer.from(built.interleaved.codewords).toString('base64'), diagnostics:result.diagnostics } });
}
const kinds = [
  {text:'123456789',options:{mode:'numeric'}}, {text:'HELLO:1',options:{mode:'alphanumeric'}},
  {text:'aé',options:{mode:'byte'}}, {bytes:[0,1,127,128,254,255]},
  {segments:[{mode:'numeric',text:'123'},{mode:'byte',bytes:[97]}]},
  {segments:[{mode:'eci',assignmentNumber:26},{mode:'byte',text:'雪'}]},
  {text:'漢字',options:{mode:'kanji'}}, {segments:[{mode:'fnc1-second',applicationIndicator:'A'},{mode:'alphanumeric',text:'ABC'}]}
];
for(let version=1;version<=40;version++) for(const [ei,errorCorrectionLevel] of ['L','M','Q','H'].entries()) for(let maskPattern=0;maskPattern<8;maskPattern++) {
  const kind=kinds[(version+ei+maskPattern)%kinds.length];
  add({...kind,id:`fixed-v${version}-${errorCorrectionLevel}-m${maskPattern}`,category:'fixed',options:{...kind.options,version,errorCorrectionLevel,maskPattern}});
}
for(const text of ['', 'A','HELLO WORLD','https://example.com/a?q=1','123456789012345678901234567890','日本語漢字かなカナ','abc12345678901234567890XYZ','😀e\u0301é','𝄞𐐷0\u0000\u007f','12A34B56C78D90','x'.repeat(300),'1'.repeat(1000)])
  for(const errorCorrectionLevel of ['L','M','Q','H']) for(const optimizeSegments of [true,false]) add({id:`auto-${cases.length}`,category:'auto',text,options:{errorCorrectionLevel,optimizeSegments}});
for(const version of [1,2,9,10,26,27,40]) for(const errorCorrectionLevel of ['L','M','Q','H']) add({id:`boost-${cases.length}`,category:'boost',text:'A1',options:{version,errorCorrectionLevel,boostErrorCorrection:true}});
for(const assignmentNumber of [0,127,128,16383,16384,999999]) add({id:`eci-${assignmentNumber}`,category:'eci',segments:[{mode:'eci',assignmentNumber},{mode:'byte',text:'ECI'}]});
for(const indicator of ['00','99','A','z']) add({id:`fnc1-${indicator}`,category:'fnc1',text:'ABC',options:{fnc1Second:indicator}});
for(const text of ['0109501101530003','010950110153000310LOT123\u001d17271231']) add({id:`gs1-${cases.length}`,category:'gs1',text,options:{gs1:true}});
for(const headers of [{gs1:true},{fnc1Second:'A'}]) for(const optimizeSegments of [true,false]) for(const text of ['10ABC%DEF','10ABC%%DEF','10LOT%\u001d21SER%IAL']) add({id:`safety-${cases.length}`,category:'intentional-percent-safety',text,options:{...headers,optimizeSegments}},{mode:'byte'});
// Deterministic mixed-Unicode fuzz independently fixes inputs before requesting the oracle.
let seed=0x5eec0de;
const random=()=>{seed^=seed<<13;seed^=seed>>>17;seed^=seed<<5;return seed>>>0;};
const alphabet=Array.from('ABCXYZ0123456789abcé漢字😀%:');
for(let i=0;i<160;i++) {let text='';for(let j=random()%60;j>0;j--)text+=alphabet[random()%alphabet.length];add({id:`fuzz-${i}`,category:'seeded-fuzz',text,options:{errorCorrectionLevel:['L','M','Q','H'][i%4],optimizeSegments:i%2===0}});}
for(let version=1;version<=40;version++) for(const errorCorrectionLevel of ['L','M','Q','H']) for(const mode of ['numeric','alphanumeric','byte','kanji']) {
  const options={version,errorCorrectionLevel,mode}; const expected=api.getCapacity(options); capacities.push({options,expected});
  // Boundary planning across every version and mode; generation at selected count-width transitions.
  const max=expected.maxCharacters??expected.maxBytes;
  for(const count of [max-1,max,max+1]) {const text={numeric:'1',alphanumeric:'A',byte:'a',kanji:'漢'}[mode].repeat(count);estimates.push({options,count,character:{numeric:'1',alphanumeric:'A',byte:'a',kanji:'漢'}[mode],expected:api.estimate(text,options)});}
}
const fixture={schemaVersion:1,sourceCommit:commit,independentOracle:'nayuki-qr-code-generator@1.8.0',cases,capacities,estimates};
const bytes=gzipSync(Buffer.from(JSON.stringify(fixture)),{level:9});
const destination=path.join(root,'tests/SpecQR.Tests/Fixtures/core.json.gz'); fs.writeFileSync(destination,bytes);
const copied = ['gs1-upstream.json','structured-append-differential.json'].map(name=>({file:name,sha256:createHash('sha256').update(fs.readFileSync(path.join(root,'tests/SpecQR.Tests/Fixtures',name))).digest('hex')}));
const manifest={schemaVersion:1,source:{url:'https://github.com/SpecQR/SpecQR',commit,version:'3.0.0-rc.2'},swift:{url:'https://github.com/SpecQR/SpecQR-Swift',commit:'0ef9613fe8f1ecd687da76ce797b4ef896afa477'},lab:{url:'https://github.com/SpecQR/SpecQR-Conformance-Lab',commit:'72ad78c979327e3e261526ea3c0a164efa4ab390'},independentOracle:'nayuki-qr-code-generator@1.8.0',independentMatrices,matrixCases:cases.length,capacities:capacities.length,estimates:estimates.length,files:[{file:'core.json.gz',sha256:createHash('sha256').update(bytes).digest('hex')},...copied],intentionalDifferences:['High-level FNC1/GS1 literal percent uses byte mode; expected matrices are the pinned JS explicitly-byte output.']};
fs.writeFileSync(path.join(root,'tests/SpecQR.Tests/Fixtures/manifest.json'),JSON.stringify(manifest,null,2)+'\n');
console.log(JSON.stringify({matrixCases:cases.length,independentMatrices,capacities:capacities.length,estimates:estimates.length,compressedBytes:bytes.length},null,2));
