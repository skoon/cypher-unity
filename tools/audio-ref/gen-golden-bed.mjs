// Plays the web prototype's own city bed (Ambience.js) in Chromium's Web Audio and writes what it makes as a golden fixture for the C# bed
// (Assets/Tests/EditMode/Fixtures/audio_bed_golden.txt). The prototype's modules are served read-only from its folder (see gen-golden-recipes.mjs). The positional sources (the neon tube and
// the air-conditioning) are plain gains here, heard from the middle, and the random one-shots are off (an empty bank), so what is rendered is the continuous bed, taken from the ambience bus's
// input; the scheduler is pumped every 0.1 s exactly as the prototype's own measurement does.
//
//   node tools/audio-ref/gen-golden-bed.mjs <path-to-cyberpunkish>
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { openBlankPage } from './chromium.mjs';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/audio-ref/gen-golden-bed.mjs <path-to-cyberpunkish>');
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

const SEED = 91117;
const SECONDS = 8;
// each scenario: the state over time, as [time, state] steps
const scenarios = [
  { name: 'rain-and-wind', steps: [[0, { rain: 0.85, wind: 0.35, indoor: 0, density: 1 }]] },
  { name: 'dry-indoors-quiet', steps: [[0, { rain: 0, wind: 0, indoor: 0.8, density: 0.5 }]] },
  { name: 'gale-and-crowd', steps: [[0, { rain: 0.3, wind: 1, indoor: 0, density: 2 }]] },
  { name: 'weather-turns', steps: [[0, { rain: 0, wind: 0.1, indoor: 0, density: 1 }], [3.0, { rain: 1, wind: 0.8, indoor: 0, density: 1 }], [5.5, { rain: 0.2, wind: 0.2, indoor: 1, density: 1 }]] },
  // events only (nothing rendered): five minutes of the scheduler, to see every clock fire many times, with the rain stopping and starting
  { name: 'events-long', eventsOnly: true, seconds: 300, steps: [[0, { rain: 0.85, wind: 0.35, indoor: 0, density: 1 }], [100, { rain: 0, wind: 0.2, indoor: 0, density: 1.5 }], [200, { rain: 0.5, wind: 0.2, indoor: 0.3, density: 0.4 }]] },
];

const { browser, page } = await openBlankPage();
await page.goto(`http://127.0.0.1:${port}/blank.html`);
const results = await page.evaluate(async ({ scenarios, seed, seconds: defaultSeconds }) => {
  const { AudioGraph } = await import('/proto/src/audio/Graph.js');
  const { Ambience } = await import('/proto/src/audio/Ambience.js');
  const { DspCache } = await import('/proto/src/audio/Dsp.js');
  const rate = 48000;
  // the positional sources are plain gains, heard from the middle
  OfflineAudioContext.prototype.createPanner = function createPanner() {
    const g = this.createGain();
    const p = { setValueAtTime() {}, setTargetAtTime() {}, value: 0 };
    g.positionX = p; g.positionY = p; g.positionZ = p;
    return g;
  };
  const out = [];
  for (const scn of scenarios) {
    const seconds = scn.seconds ?? defaultSeconds;
    const ac = new OfflineAudioContext(2, scn.eventsOnly ? 128 : Math.ceil(seconds * rate), rate);
    const dsp = new DspCache();
    dsp.warm(rate);
    const graph = new AudioGraph(ac, dsp, { seed: 20771, offline: true });
    graph.bank = Object.create(null); // no one-shots
    const events = [];
    graph.play = (name, opts) => { events.push({ name, t: opts.time, p: opts.position, v: opts.volume, pitch: opts.pitch ?? null, wet: opts.params?.wet ?? null }); return null; };
    graph.listenerPos = { x: 0, y: 1.7, z: 0 };
    graph.masterOut.disconnect();
    graph.buses.ambience.input.connect(ac.destination);
    const ambience = new Ambience(graph, { seed });
    ambience.eventScale = 3;
    ambience.start(0.01);
    let step = 0;
    for (let t = 0; t < seconds; t += 0.1) {
      while (step + 1 < scn.steps.length && scn.steps[step + 1][0] <= t + 1e-9) step++;
      ambience.advance(t, 0.25, scn.steps[step][1]);
    }
    if (scn.eventsOnly) { out.push({ name: scn.name, left: [], right: [], events, eventsOnly: true }); continue; }
    const buf = await ac.startRendering();
    out.push({ name: scn.name, left: Array.from(buf.getChannelData(0)), right: Array.from(buf.getChannelData(1)), events });
  }
  return out;
}, { scenarios, seed: SEED, seconds: SECONDS });
await browser.close();
server.close();

const num = (v) => {
  const x = Math.fround(v);
  return x === 0 ? '0' : x.toPrecision(7);
};
const WINDOW = 4800;
const lines = ['# audio bed goldens: the prototype\'s own Ambience.js played in Chromium (tools/audio-ref/gen-golden-bed.mjs).'];
for (const r of results) {
  const scn = scenarios.find((s) => s.name === r.name);
  lines.push(`SCN ${JSON.stringify({ name: r.name, seed: SEED, seconds: scn.seconds ?? SECONDS, steps: scn.steps, eventsOnly: !!scn.eventsOnly })}`);
  if (!r.eventsOnly) {
  for (const [label, ch] of [['L', r.left], ['R', r.right]]) {
    const rms = [];
    for (let w = 0; w + WINDOW <= ch.length; w += WINDOW) {
      let s = 0;
      for (let i = w; i < w + WINDOW; i++) s += ch[i] * ch[i];
      rms.push(num(Math.sqrt(s / WINDOW)));
    }
    lines.push(`W ${r.name} ${label} ${rms.join(' ')}`);
  }
  }
  for (const e of r.events) {
    lines.push(`E ${r.name} ${e.t.toPrecision(17)} ${e.name} ${e.p.x.toPrecision(17)} ${e.p.y.toPrecision(17)} ${e.p.z.toPrecision(17)} ${e.v.toPrecision(17)} ${e.pitch === null ? '-' : e.pitch.toPrecision(17)} ${e.wet === null ? '-' : e.wet.toPrecision(17)}`);
  }
  if (!r.eventsOnly) {
  // sample-level probes: every 8th sample over 40 ms near 2 s and near 7 s
  for (const at of [96000, 336000]) {
    for (const [label, ch] of [['L', r.left], ['R', r.right]]) {
      const v = [];
      for (let i = at; i < at + 1920; i += 8) v.push(num(ch[i]));
      lines.push(`S ${r.name} ${label} ${at} ${v.join(' ')}`);
    }
  }
  }
}
const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'audio_bed_golden.txt'), lines.join('\n') + '\n');
console.log(`wrote ${results.length} beds`);
