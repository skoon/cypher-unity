// Runs the web prototype's own score (Music.js) in Chromium, with its graph's play() and the automation of its pad, duck and delay recorded, and writes what it asks for as a golden fixture for
// the C# score (Assets/Tests/EditMode/Fixtures/audio_music_golden.txt). Nothing is rendered: the score is a scheduler, and what matters is which notes it asks for, when, how loud, through
// which path, and what it does to the pad and the sidechain. The intensity follows a script that visits every arrangement, with a bump and its decay. Read-only against the prototype.
//
//   node tools/audio-ref/gen-golden-music.mjs <path-to-cyberpunkish>
import http from 'node:http';
import { readFile } from 'node:fs/promises';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname, extname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { openBlankPage } from './chromium.mjs';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/audio-ref/gen-golden-music.mjs <path-to-cyberpunkish>');
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

const SEEDS = [4242, 99];
const SECONDS = 200;

const { browser, page } = await openBlankPage();
await page.goto(`http://127.0.0.1:${port}/blank.html`);
const runs = await page.evaluate(async ({ seeds, seconds }) => {
  const { AudioGraph } = await import('/proto/src/audio/Graph.js');
  const { Music } = await import('/proto/src/audio/Music.js');
  const { DspCache } = await import('/proto/src/audio/Dsp.js');
  const rate = 48000;
  const out = [];
  for (const seed of seeds) {
    const ac = new OfflineAudioContext(2, 128, rate);
    const dsp = new DspCache();
    const graph = new AudioGraph(ac, dsp, { seed: 20771, offline: true });
    const music = new Music(graph, { seed });
    const header = { progression: music.progression.join(','), arpShape: music.arpShape.join(',') };
    music.start(0.01);
    header.nextStepTime = music.nextStepTime;
    const rec = [];
    const wrap = (param, label) => {
      for (const m of ['setTargetAtTime', 'setValueAtTime', 'cancelScheduledValues']) {
        const original = param[m].bind(param);
        param[m] = (...a) => { rec.push([label, m, ...a]); return original(...a); };
      }
    };
    wrap(music.duck.gain, 'duck');
    wrap(music.padGain.gain, 'padGain');
    wrap(music.padFilter.frequency, 'padFilter');
    wrap(music.delayWet.gain, 'delayWet');
    music.padOscs.forEach((o, i) => wrap(o.frequency, `pad${i}`));
    graph.play = (name, opts) => {
      const dest = opts.out === music.duck ? 'duck' : opts.out === music.delayIn ? 'delay' : 'bus';
      rec.push(['play', name, opts.time, opts.volume, dest, opts.params ?? null]);
      return null;
    };
    const script = (t) => {
      if (t < 20) return 0.12;
      if (t < 40) return 0.5;
      if (t < 70) return 0.95;
      if (t < 100) return 0.12;
      if (t < 130) return 0.7;
      if (t < 160) return 0.3;
      return 0.55;
    };
    let bumped = false;
    const intensities = [];
    for (let t = 0; t < seconds; t += 0.1) {
      music.setIntensity(script(t));
      if (!bumped && t >= 70) { music.bump(0.35); bumped = true; }
      music.tick(0.1);
      music.advance(t, 0.25);
      if (Math.abs(t * 10 - Math.round(t * 10)) < 1e-6 && Math.round(t * 10) % 50 === 0) intensities.push(music.intensity);
    }
    out.push({ seed, header, rec, intensities });
  }
  return out;
}, { seeds: SEEDS, seconds: SECONDS });
await browser.close();
server.close();

const n = (v) => (typeof v === 'number' ? v.toPrecision(17) : String(v));
const lines = ['# audio score goldens: the prototype\'s own Music.js scheduler in Chromium (tools/audio-ref/gen-golden-music.mjs).'];
for (const run of runs) {
  lines.push(`SCN ${JSON.stringify({ seed: run.seed, seconds: SECONDS, ...run.header })}`);
  for (const i of run.intensities) lines.push(`I ${run.seed} ${n(i)}`);
  for (const r of run.rec) {
    if (r[0] === 'play') {
      const p = r[5] ?? {};
      lines.push(`N ${run.seed} ${n(r[2])} ${r[1]} ${n(r[3])} ${r[4]} ${p.freq !== undefined ? n(p.freq) : '-'} ${p.dur !== undefined ? n(p.dur) : '-'} ${p.cutoff !== undefined ? n(p.cutoff) : '-'} ${p.pitch !== undefined ? n(p.pitch) : '-'} ${p.freqs ? p.freqs.map(n).join(',') : '-'}`);
    } else {
      lines.push(`A ${run.seed} ${r.map(n).join(' ')}`);
    }
  }
}
const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'audio_music_golden.txt'), lines.join('\n') + '\n');
console.log(`wrote ${runs.length} runs, ${lines.length} lines`);
