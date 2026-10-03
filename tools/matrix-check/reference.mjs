// Optional development oracle: imports only the caller-supplied SpecQR JS checkout.
import {createHash} from 'node:crypto';
import {resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
if (process.argv.length !== 3) throw new Error('Usage: node reference.mjs BASELINE_CHECKOUT');
const load = relative => import(pathToFileURL(resolve(process.argv[2], relative)).href);
const {getSize, getRawCodewordCount, getDataCodewordCount, getAlignmentPatternPositions} = await load('src/core/tables.js');
const {interleaveCodewords} = await load('src/core/codewords.js');
const {buildMatrix} = await load('src/core/matrix.js');
const {multiply} = await load('src/core/galois-field.js');
const {createGeneratorPolynomial, computeRemainder} = await load('src/core/reed-solomon.js');
const hash=bytes=>createHash('sha256').update(Buffer.from(bytes)).digest('hex');
for(let version=1;version<=40;version++) {
  console.log(`table ${version} ${getSize(version)} ${getRawCodewordCount(version)} ${getAlignmentPatternPositions(version).join(',')}`);
  for(const [ordinal,level] of ['L','M','Q','H'].entries()) {
    const capacity=getDataCodewordCount(version,level);
    for(let seed=0;seed<3;seed++) {
      const data=Array.from({length:capacity},(_,index)=> seed===0?0:seed===1?255:((index*149+version*43+ordinal*89+seed*67)^(index>>(seed+1)))&255);
      const codewords=interleaveCodewords(data,version,level).codewords;
      const wordHash=hash(codewords);
      for(let mask=-1;mask<8;mask++) {
        const result=buildMatrix(codewords,version,level,mask<0?'auto':mask);
        console.log(`case ${version} ${level} ${seed} ${mask} ${capacity} ${wordHash} ${hash(result.matrix.flat().map(Number))} ${result.maskPattern} ${result.penalty} ${result.maskPenalties.map(p=>p.penalty).join(',')}`);
      }
    }
  }
}
const products=[];
for(let left=0;left<256;left++)for(let right=0;right<256;right++)products.push(multiply(left,right));
console.log(`gf ${hash(products)}`);
for(let degree=1;degree<=255;degree++) {
 const generator=createGeneratorPolynomial(degree), input=Array.from({length:300},(_,n)=>(n*61+degree)&255);
 console.log(`rs ${degree} ${hash(generator)} ${hash(computeRemainder(input,generator))}`);
}
