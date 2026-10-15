// Bundles src/ into one self-contained HTML file (code, models, sounds and font inlined), the format ad
// networks expect for a playable. Usage: node build.mjs  ->  dist/planes-playable.html
import { build } from 'esbuild';
import { readFileSync, readdirSync, writeFileSync, mkdirSync } from 'node:fs';

const b64 = path => readFileSync(path).toString('base64');
const folder = (dir, ext) => Object.fromEntries(readdirSync(dir).filter(f => f.endsWith(ext)).map(f => [f.slice(0, -ext.length), b64(`${dir}/${f}`)]));

const { outputFiles } = await build({ entryPoints: ['src/main.js'], bundle: true, minify: true, format: 'iife', write: false, target: 'es2019' });
const assets = JSON.stringify({ models: folder('models', '.glb'), audio: folder('audio', '.mp3') });
// Function replacements: the minified code contains "$&"-style sequences a string replacement would expand.
const html = readFileSync('src/template.html', 'utf8')
  .replace('__FONT__', () => b64('LilitaOne-Regular.ttf'))
  .replace('__ASSETS__', () => assets)
  .replace('__BUNDLE__', () => outputFiles[0].text.replace(/<\/script/g, '<\\/script'));

mkdirSync('dist', { recursive: true });
writeFileSync('dist/planes-playable.html', html);
const mb = Buffer.byteLength(html) / 1024 / 1024;
console.log(`dist/planes-playable.html: ${mb.toFixed(2)} MB`);
if (mb > 5) throw new Error('Over the 5 MB limit most ad networks enforce');
