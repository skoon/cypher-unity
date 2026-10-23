// Plays the web prototype's own sound bank (Bank.js through Graph.js) in Chromium's Web Audio and writes what it makes as a golden fixture for the C# recipes
// (Assets/Tests/EditMode/Fixtures/audio_recipes_golden.txt). The prototype's modules are served read-only from its folder, with its engine module (which pulls in three.js) replaced
// by the one function the audio uses, the seeded random number generator. Each sound is played alone, straight to the output (no pan law, no buses, no reverb return).
//
//   node tools/audio-ref/gen-golden-recipes.mjs <path-to-cyberpunkish>
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { openBlankPage } from './chromium.mjs';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/audio-ref/gen-golden-recipes.mjs <path-to-cyberpunkish>');
  process.exit(1);
}

const STUB_ASSETS = `
export function makeRng(seed = 1337) {
  let a = seed >>> 0;
  return function rng() {
    a |= 0; a = (a + 0x6d2b79f5) | 0;
    let t = Math.imul(a ^ (a >>> 15), 1 | a);
    t = (t + Math.imul(t ^ (t >>> 7), 61 | t)) ^ t;
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}
`;

const server = http.createServer(async (req, res) => {
  try {
    const url = new URL(req.url, 'http://x');
    if (url.pathname === '/blank.html') { res.writeHead(200, { 'content-type': 'text/html' }); res.end('<!doctype html><title>x</title>'); return; }
    if (url.pathname === '/proto/src/engine/Assets.js') { res.writeHead(200, { 'content-type': 'text/javascript' }); res.end(STUB_ASSETS); return; }
    if (url.pathname.startsWith('/proto/')) {
      const file = join(protoRoot, url.pathname.slice('/proto/'.length));
      if (!file.startsWith(protoRoot)) { res.writeHead(403); res.end(); return; }
      const body = await readFile(file);
      res.writeHead(200, { 'content-type': extname(file) === '.js' ? 'text/javascript' : 'application/octet-stream' });
      res.end(body);
      return;
    }
    res.writeHead(404); res.end();
  } catch (e) { res.writeHead(500); res.end(String(e)); }
});
await new Promise((r) => server.listen(0, '127.0.0.1', r));
const port = server.address().port;

const SEEDS = [20771, 4242];
const cases = [];
const add = (name, seconds, params = {}, opts = {}) => cases.push({ name, seconds, params, opts });

// footsteps on every surface, a wet one, a sprint, a crouch
add('foot.concrete', 2, { wet: 0.8 });
add('foot.concrete', 2, { sprint: true });
add('foot.asphalt', 2, { wet: 0.5, crouch: true });
add('foot.metal', 2);
add('foot.wood', 2);
add('foot.gravel', 2, { sprint: true });
add('foot.carpet', 2);
add('foot.water', 2);
add('player.jump', 2);
add('player.land', 2, { impact: 0.9, surface: 'concrete' });
add('player.land', 2, { impact: 0.3, surface: 'metal' });
add('player.cloth', 2);
add('player.breath', 2, { effort: 0.8 });
add('player.breath', 2, { effort: 0.2, inhale: false });
for (const ui of ['hover', 'tick', 'select', 'confirm', 'back', 'open', 'close', 'error', 'alert', 'notify']) add(`ui.${ui}`, 3);
add('amb.siren', 10);
add('amb.rumbleimpact', 5);
add('amb.drip', 1.5);
add('amb.voice', 2.5);
add('amb.neonbuzz', 2);
add('amb.carpass', 6, { wet: 0.5 });
add('amb.carpass', 6);
add('mus.kick', 2, { pitch: 1 });
add('mus.snare', 2);
add('mus.clap', 2);
add('mus.hat', 1, { pitch: 1.05 });
add('mus.openhat', 1, { pitch: 1 });
add('mus.rim', 1);
add('mus.noise', 2);
add('mus.bass', 2, { freq: 55, dur: 0.5, cutoff: 700 });
add('mus.arp', 2, { freq: 440, dur: 0.3 });
add('mus.stab', 2, { dur: 0.8, freqs: [220, 262, 330], cutoff: 1800 });
add('mus.lead', 2, { freq: 440, dur: 0.6 });

const { browser, page } = await openBlankPage();
await page.goto(`http://127.0.0.1:${port}/blank.html`);
const results = await page.evaluate(async ({ cases, seeds }) => {
  const { AudioGraph } = await import('/proto/src/audio/Graph.js');
  const { buildBank } = await import('/proto/src/audio/Bank.js');
  const { DspCache } = await import('/proto/src/audio/Dsp.js');
  const bank = buildBank();
  const dsp = new DspCache();
  const rate = 48000;
  const out = [];
  for (const c of cases) {
    for (const seed of seeds) {
      const frames = Math.ceil(c.seconds * rate);
      const ac = new OfflineAudioContext(2, frames, rate);
      dsp.warm(rate);
      const graph = new AudioGraph(ac, dsp, { seed, offline: true });
      graph.bank = bank;
      graph.masterOut.disconnect(); // the reverb return and the buses go nowhere: only the dry voice is heard
      const acquire = graph._acquire.bind(graph);
      graph._acquire = (...a) => { const v = acquire(...a); if (v) v.occl.frequency.value = rate / 2; return v; };
      const v = graph.play(c.name, { time: 0.05, out: ac.destination, params: c.params, ...c.opts });
      if (!v) throw new Error(`${c.name} was culled`);
      const buf = await ac.startRendering();
      const left = buf.getChannelData(0);
      // undo the centre pan law the voice's stereo panner applies
      out.push({ name: c.name, params: c.params, seed, samples: Array.from(left, (x) => x / Math.SQRT1_2), reported: v.endTime });
    }
  }
  return out;
}, { cases, seeds: SEEDS });
await browser.close();
server.close();

const num = (v) => {
  const x = Math.fround(v);
  return x === 0 ? '0' : x.toPrecision(7);
};
const WINDOW = 480;
const START = Math.round(0.05 * 48000);
const lines = ['# audio recipe goldens: the prototype\'s own Bank.js played in Chromium (tools/audio-ref/gen-golden-recipes.mjs). R name seed params endFrame windowRms... | firstSamples...'];
for (const r of results) {
  let last = 0;
  for (let i = r.samples.length - 1; i >= 0; i--) if (Math.abs(r.samples[i]) > 1e-7) { last = i; break; }
  const end = Math.min(r.samples.length, Math.ceil((last + 1) / WINDOW) * WINDOW);
  const rms = [];
  for (let w = 0; w < end; w += WINDOW) {
    let s = 0;
    for (let i = w; i < w + WINDOW && i < r.samples.length; i++) s += r.samples[i] * r.samples[i];
    rms.push(num(Math.sqrt(s / WINDOW)));
  }
  const head = r.samples.slice(START, START + 256).map(num);
  lines.push(`R ${r.name} ${r.seed} ${JSON.stringify(r.params)} ${end} ${rms.join(' ')} | ${head.join(' ')}`);
}
const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'audio_recipes_golden.txt'), lines.join('\n') + '\n');
console.log(`wrote ${results.length} sounds`);
