// Renders small synthesis scenarios in Chromium's Web Audio (an OfflineAudioContext, headless via Playwright) and writes what comes out as a golden fixture for the C# synthesis engine
// (Assets/Tests/EditMode/Fixtures/audio_synth_golden.txt). The scenario is a plain description (nodes, connections, parameter automation) that this script and the C# test
// each build, so the two engines are given exactly the same thing. Read-only against the prototype.
//
//   node tools/audio-ref/gen-golden-synth.mjs <path-to-cyberpunkish>
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';
import { openBlankPage, renderScenarios } from './chromium.mjs';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/audio-ref/gen-golden-synth.mjs <path-to-cyberpunkish>');
  process.exit(1);
}
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);
const { DspCache } = await load('src/audio/Dsp.js');

const RATE = 48000;
const scenarios = [];
const add = (s) => scenarios.push({ rate: RATE, frames: 1024, stride: 1, channels: 1, connect: [], auto: [], ...s });

/* ---- oscillators ---- */
const osc = (name, wave, freq, extra = {}, scn = {}) => add({
  name, nodes: [{ id: 'o', type: 'osc', wave, freq, ...extra }], connect: [['o', 'out']], ...scn,
});
osc('osc-sine-440', 'sine', 440);
osc('osc-sine-50', 'sine', 50);
osc('osc-triangle-440', 'triangle', 440);
osc('osc-triangle-5000', 'triangle', 5000);
osc('osc-saw-110', 'sawtooth', 110);
osc('osc-saw-440', 'sawtooth', 440);
osc('osc-saw-3000', 'sawtooth', 3000);
osc('osc-saw-11000', 'sawtooth', 11000);
osc('osc-softsaw-100', 'softsaw', 100);
osc('osc-softsaw-440', 'softsaw', 440);
osc('osc-softsaw-3000', 'softsaw', 3000);
osc('osc-softsaw-9000', 'softsaw', 9000);
osc('osc-hollow-440', 'hollow', 440);
osc('osc-bell-233', 'bell', 233.08);
osc('osc-bell-1200', 'bell', 1200);
osc('osc-sine-detune', 'sine', 440, { detune: 1200 });
osc('osc-sine-fractional-start', 'sine', 440, { start: 0.00001, stop: 0.01 });
osc('osc-sine-start-later', 'sine', 440, { start: 0.0123 }, { frames: 1536 });
osc('osc-sine-stop', 'sine', 1000, { start: 0.001, stop: 0.0102 });
add({
  name: 'osc-frequency-linear',
  frames: 1536,
  nodes: [{ id: 'o', type: 'osc', wave: 'sine', freq: 200 }],
  connect: [['o', 'out']],
  auto: [{ node: 'o', param: 'frequency', ops: [['set', 200, 0], ['lin', 2000, 0.02]] }],
});
add({
  name: 'osc-frequency-exponential',
  frames: 1536,
  nodes: [{ id: 'o', type: 'osc', wave: 'sine', freq: 200 }],
  connect: [['o', 'out']],
  auto: [{ node: 'o', param: 'frequency', ops: [['set', 200, 0], ['exp', 3200, 0.025]] }],
});
add({
  name: 'osc-frequency-modulated',
  frames: 2048,
  nodes: [
    { id: 'o', type: 'osc', wave: 'sine', freq: 300 },
    { id: 'lfo', type: 'osc', wave: 'sine', freq: 90 },
    { id: 'depth', type: 'gain', value: 120 },
  ],
  connect: [['o', 'out'], ['lfo', 'depth'], ['depth', 'o.frequency']],
});

/* ---- buffer sources ---- */
const src = (name, buffer, extra = {}, scn = {}) => add({
  name, nodes: [{ id: 's', type: 'src', buffer, loop: true, ...extra }], connect: [['s', 'out']], ...scn,
});
src('src-white', 'white');
src('src-white-rate075-offset', 'white', { rate: 0.75, offset: 0.3 });
src('src-pink-rate137-offset', 'pink', { rate: 1.37, offset: 1.9 });
src('src-brown-wraps-the-loop', 'brown', { offset: 3.9335 }, { frames: 1536 });
src('src-velvet-duration', 'velvet', { duration: 0.01, offset: 0.1 }, { frames: 1024 });
src('src-white-starts-later', 'white', { start: 0.0071, offset: 0.2 });
src('src-white-rate-automated', 'white', { offset: 0.5 }, {
  auto: [{ node: 's', param: 'rate', ops: [['set', 0.7, 0], ['lin', 1.4, 0.015]] }],
});

/* ---- biquads, as impulse responses ---- */
const bq = (name, filter, freq, q, gain = 0, frames = 512) => add({
  name,
  frames,
  nodes: [{ id: 's', type: 'src', buffer: 'impulse', loop: false }, { id: 'f', type: 'biquad', filter, freq, q, gain }],
  connect: [['s', 'f'], ['f', 'out']],
});
bq('biquad-lowpass-1000-0', 'lowpass', 1000, 0);
bq('biquad-lowpass-1000-6', 'lowpass', 1000, 6);
bq('biquad-lowpass-5000-12', 'lowpass', 5000, 12);
bq('biquad-lowpass-300-0.7', 'lowpass', 300, 0.7);
bq('biquad-lowpass-165-1.6', 'lowpass', 165, 1.6);
bq('biquad-lowpass-20000-0.4', 'lowpass', 20000, 0.4);
bq('biquad-lowpass-above-nyquist', 'lowpass', 30000, 1);
bq('biquad-lowpass-zero', 'lowpass', 0, 1);
bq('biquad-lowpass-negative-q', 'lowpass', 2000, -6);
bq('biquad-highpass-1000-0', 'highpass', 1000, 0);
bq('biquad-highpass-200-6', 'highpass', 200, 6);
bq('biquad-highpass-2600-0.7', 'highpass', 2600, 0.7);
bq('biquad-highpass-above-nyquist', 'highpass', 30000, 1);
bq('biquad-bandpass-1000-1', 'bandpass', 1000, 1);
bq('biquad-bandpass-2500-6', 'bandpass', 2500, 6);
bq('biquad-bandpass-500-0.5', 'bandpass', 500, 0.5);
bq('biquad-bandpass-1500-9', 'bandpass', 1500, 9);
bq('biquad-bandpass-q-zero', 'bandpass', 800, 0);
bq('biquad-peaking-2600-1.1-3', 'peaking', 2600, 1.1, 3);
bq('biquad-peaking-6200-1.2-4', 'peaking', 6200, 1.2, 4);
bq('biquad-peaking-1000-3-minus9', 'peaking', 1000, 3, -9);
bq('biquad-peaking-q-zero', 'peaking', 1000, 0, 6);

/* ---- biquads that move ---- */
for (const [filter, f0, f1, q] of [['lowpass', 400, 3000, 2], ['bandpass', 300, 4000, 5], ['highpass', 200, 2500, 1]]) {
  add({
    name: `biquad-sweep-${filter}`,
    frames: 2400,
    nodes: [{ id: 's', type: 'src', buffer: 'white', loop: true, offset: 0.25 }, { id: 'f', type: 'biquad', filter, freq: f0, q }],
    connect: [['s', 'f'], ['f', 'out']],
    auto: [{ node: 'f', param: 'frequency', ops: [['set', f0, 0], ['exp', f1, 0.04]] }],
  });
}
add({
  name: 'biquad-modulated-by-lfo',
  frames: 2400,
  nodes: [
    { id: 's', type: 'src', buffer: 'pink', loop: true, offset: 0.4 },
    { id: 'f', type: 'biquad', filter: 'lowpass', freq: 800, q: 1.6 },
    { id: 'lfo', type: 'osc', wave: 'sine', freq: 120 },
    { id: 'depth', type: 'gain', value: 500 },
  ],
  connect: [['s', 'f'], ['f', 'out'], ['lfo', 'depth'], ['depth', 'f.frequency']],
});

/* ---- parameter schedules, seen as a constant source's output ---- */
const par = (name, ops, extra = {}) => add({
  name,
  frames: 4800,
  stride: 16,
  nodes: [{ id: 'c', type: 'const', offset: 1 }],
  connect: [['c', 'out']],
  auto: [{ node: 'c', param: 'offset', ops }],
  ...extra,
});
par('param-linear', [['set', 0.2, 0], ['lin', 1, 0.02]]);
par('param-linear-down-then-hold', [['set', 1, 0.005], ['lin', -0.5, 0.03]]);
par('param-exponential-up', [['set', 0.001, 0], ['exp', 1, 0.05]]);
par('param-exponential-down', [['set', 1, 0], ['exp', 0.001, 0.04]]);
par('param-exponential-from-zero-holds', [['set', 0, 0], ['exp', 1, 0.03]]);
par('param-exponential-across-zero-holds', [['set', -1, 0], ['exp', 1, 0.03]]);
par('param-target', [['set', 0, 0], ['target', 1, 0.01, 0.01]]);
par('param-target-then-target', [['set', 0, 0], ['target', 1, 0.01, 0.01], ['target', 0.2, 0.05, 0.02]]);
par('param-target-zero-time-constant', [['set', 0.3, 0], ['target', 0.8, 0.02, 0]]);
par('param-set-then-set', [['set', 0.1, 0.01], ['set', 0.9, 0.03]]);
par('param-same-time-replaces', [['set', 0.1, 0.01], ['set', 0.7, 0.01], ['exp', 0.01, 0.06]]);
par('param-cancel', [['set', 0.5, 0], ['lin', 1, 0.04], ['set', 0.2, 0.06], ['cancel', 0.03]]);
par('param-cancel-then-set', [['set', 0.5, 0], ['target', 1, 0.005, 0.01], ['cancel', 0.03], ['set', 0.1, 0.03], ['target', 0.9, 0.04, 0.005]]);
// the envelopes the sound recipes use (Graph.js ad / ahr / sweep)
par('param-envelope-ad', [['cancel', 0.01], ['set', 0, 0.01], ['lin', 0.5, 0.0125], ['exp', 0.0003, 0.06], ['set', 0, 0.062]]);
par('param-envelope-ad-instant-attack', [['cancel', 0.01], ['set', 0, 0.01], ['set', 0.4, 0.01], ['exp', 0.00024, 0.05], ['set', 0, 0.052]]);
par('param-envelope-ahr', [['cancel', 0.005], ['set', 0, 0.005], ['lin', 0.3, 0.02], ['set', 0.3, 0.035], ['lin', 0, 0.06]]);
par('param-envelope-sweep', [['cancel', 0.004], ['set', 380, 0.004], ['exp', 5200, 0.07]]);
par('param-value-then-events', [['value', 0.4], ['set', 0.8, 0.02], ['lin', 0.2, 0.04]]);
add({
  name: 'param-modulated',
  frames: 2400,
  stride: 4,
  nodes: [
    { id: 'c', type: 'const', offset: 1 },
    { id: 'g', type: 'gain', value: 0.5 },
    { id: 'lfo', type: 'osc', wave: 'sine', freq: 50 },
    { id: 'depth', type: 'gain', value: 0.25 },
  ],
  connect: [['c', 'g'], ['g', 'out'], ['lfo', 'depth'], ['depth', 'g.gain']],
  auto: [{ node: 'g', param: 'gain', ops: [['set', 0.5, 0], ['lin', 0.9, 0.03]] }],
});
add({
  name: 'gain-chain',
  nodes: [{ id: 'o', type: 'osc', wave: 'sine', freq: 440 }, { id: 'a', type: 'gain', value: 0.3 }, { id: 'b', type: 'gain', value: 2 }],
  connect: [['o', 'a'], ['a', 'b'], ['b', 'out']],
});
add({
  name: 'gain-sum-of-two-oscillators',
  nodes: [{ id: 'o', type: 'osc', wave: 'sine', freq: 300 }, { id: 'p', type: 'osc', wave: 'sine', freq: 700 }, { id: 'g', type: 'gain', value: 0.25 }],
  connect: [['o', 'g'], ['p', 'g'], ['g', 'out']],
});

/* ---- delays ---- */
add({
  name: 'delay-impulse',
  frames: 2048,
  nodes: [{ id: 's', type: 'src', buffer: 'impulse', loop: false }, { id: 'd', type: 'delay', time: 0.01, max: 1 }],
  connect: [['s', 'd'], ['d', 'out']],
});
add({
  name: 'delay-fractional',
  frames: 2048,
  nodes: [{ id: 's', type: 'src', buffer: 'impulse', loop: false }, { id: 'd', type: 'delay', time: 0.0100123, max: 1 }],
  connect: [['s', 'd'], ['d', 'out']],
});
add({
  name: 'delay-with-feedback',
  frames: 4800,
  stride: 2,
  nodes: [
    { id: 's', type: 'src', buffer: 'impulse', loop: false },
    { id: 'd', type: 'delay', time: 0.01, max: 1 },
    { id: 'fb', type: 'gain', value: 0.5 },
  ],
  connect: [['s', 'd'], ['d', 'fb'], ['fb', 'd'], ['d', 'out']],
});
add({
  name: 'delay-with-damped-feedback',
  frames: 4800,
  stride: 2,
  nodes: [
    { id: 's', type: 'src', buffer: 'impulse', loop: false },
    { id: 'd', type: 'delay', time: 0.063, max: 1.5 },
    { id: 'damp', type: 'biquad', filter: 'lowpass', freq: 4200, q: 0.7 },
    { id: 'fb', type: 'gain', value: 0.34 },
    { id: 'wet', type: 'gain', value: 0.26 },
  ],
  connect: [['s', 'd'], ['d', 'damp'], ['damp', 'fb'], ['fb', 'd'], ['d', 'wet'], ['wet', 'out'], ['s', 'out']],
  stride: 2,
  frames: 9600,
});

/* ---- panning ---- */
for (const [label, value] of [['left', -1], ['quarter-left', -0.5], ['centre', 0], ['quarter-right', 0.3], ['right', 1]]) {
  add({
    name: `pan-${label}`,
    channels: 2,
    frames: 256,
    nodes: [{ id: 'c', type: 'const', offset: 0.8 }, { id: 'p', type: 'pan', value }],
    connect: [['c', 'p'], ['p', 'out']],
  });
}
add({
  name: 'pan-automated',
  channels: 2,
  frames: 2400,
  stride: 8,
  nodes: [{ id: 'c', type: 'const', offset: 0.8 }, { id: 'p', type: 'pan', value: -1 }],
  connect: [['c', 'p'], ['p', 'out']],
  auto: [{ node: 'p', param: 'pan', ops: [['set', -1, 0], ['lin', 1, 0.04]] }],
});
add({
  name: 'mono-into-both-speakers',
  channels: 2,
  frames: 256,
  nodes: [{ id: 'c', type: 'const', offset: 0.8 }],
  connect: [['c', 'out']],
});

/* ---- long noise through a sweeping band-pass, as a car's tyres are heard ---- */
add({
  name: 'long-noise-rate',
  frames: 144000,
  stride: 16,
  nodes: [{ id: 's', type: 'src', buffer: 'white', loop: true, rate: 1.0713, offset: 1.2345, start: 0.05, duration: 3.02 }],
  connect: [['s', 'out']],
});
add({
  name: 'long-noise-bandpass-sweep',
  frames: 144000,
  stride: 16,
  nodes: [
    { id: 's', type: 'src', buffer: 'white', loop: true, rate: 1.0713, offset: 1.2345, start: 0.05, duration: 3.02 },
    { id: 'f', type: 'biquad', filter: 'bandpass', freq: 1400, q: 0.9 },
  ],
  connect: [['s', 'f'], ['f', 'out']],
  auto: [{ node: 'f', param: 'frequency', ops: [['cancel', 0.05], ['set', 900, 0.05], ['exp', 2400, 1.55]] }],
});
add({
  name: 'long-noise-bandpass-sweep-envelope',
  frames: 144000,
  stride: 16,
  nodes: [
    { id: 's', type: 'src', buffer: 'white', loop: true, rate: 1.0713, offset: 1.2345, start: 0.05, duration: 3.02 },
    { id: 'f', type: 'biquad', filter: 'bandpass', freq: 1400, q: 0.9 },
    { id: 'g', type: 'gain', value: 0 },
  ],
  connect: [['s', 'f'], ['f', 'g'], ['g', 'out']],
  auto: [
    { node: 'f', param: 'frequency', ops: [['cancel', 0.05], ['set', 900, 0.05], ['exp', 2400, 1.55]] },
    { node: 'g', param: 'gain', ops: [['cancel', 0.05], ['set', 0, 0.05], ['lin', 0.3, 1.4], ['set', 0.3, 1.55], ['lin', 0, 3.05]] },
  ],
});

/* ---- the compressor: noise whose level steps up and down ---- */
const steps = [['set', 0.01, 0], ['set', 0.1, 0.25], ['set', 0.5, 0.5], ['set', 0.9, 0.75], ['set', 0.05, 1.0], ['set', 0.9, 1.2], ['set', 0.3, 1.4]];
const compScenario = (name, comps, extra = {}) => add({
  name,
  frames: 76800,
  stride: 16,
  nodes: [
    { id: 's', type: 'src', buffer: 'white', loop: true, offset: 0.3 },
    { id: 'lvl', type: 'gain', value: 1 },
    ...comps.map((c, i) => ({ id: `c${i}`, type: 'comp', ...c })),
  ],
  connect: [['s', 'lvl'], ['lvl', 'c0'], ...comps.slice(1).map((_, i) => [`c${i}`, `c${i + 1}`]), [`c${comps.length - 1}`, 'out']],
  auto: [{ node: 'lvl', param: 'gain', ops: steps }],
  ...extra,
});
const GLUE = { threshold: -20, knee: 22, ratio: 3, attack: 0.012, release: 0.28 };
const LIMITER = { threshold: -2.5, knee: 0, ratio: 20, attack: 0.0016, release: 0.09 };
const DEFAULT = { threshold: -24, knee: 30, ratio: 12, attack: 0.003, release: 0.25 };
compScenario('comp-default-steps', [DEFAULT]);
compScenario('comp-glue-steps', [GLUE]);
compScenario('comp-limiter-steps', [LIMITER]);
compScenario('comp-glue-then-limiter', [GLUE, LIMITER]);
add({
  name: 'comp-stereo-linked',
  channels: 2,
  frames: 76800,
  stride: 16,
  nodes: [
    { id: 's', type: 'src', buffer: 'white', loop: true, offset: 0.3 },
    { id: 't', type: 'src', buffer: 'pink', loop: true, offset: 1.1 },
    { id: 'lvl', type: 'gain', value: 1 },
    { id: 'quiet', type: 'gain', value: 0.05 },
    { id: 'l', type: 'pan', value: -1 },
    { id: 'r', type: 'pan', value: 1 },
    { id: 'sum', type: 'gain', value: 1 },
    { id: 'c0', type: 'comp', ...GLUE },
  ],
  connect: [['s', 'lvl'], ['lvl', 'l'], ['t', 'quiet'], ['quiet', 'r'], ['l', 'sum'], ['r', 'sum'], ['sum', 'c0'], ['c0', 'out']],
  auto: [{ node: 'lvl', param: 'gain', ops: steps }],
});

/* ---- run ---- */
const cache = new DspCache();
const beds = {};
for (const s of scenarios) {
  for (const n of s.nodes) {
    if (n.type === 'src' && !['impulse', 'ramp'].includes(n.buffer)) beds[`${n.buffer}@${s.rate}`] = Array.from(cache.noise(n.buffer, s.rate));
  }
}
const { browser, page } = await openBlankPage();
const results = await renderScenarios(page, scenarios, beds);
await browser.close();

const num = (v) => {
  const x = Math.fround(v);
  return x === 0 ? '0' : x.toPrecision(9);
};
const lines = ['# audio synthesis goldens from Chromium Web Audio (tools/audio-ref/gen-golden-synth.mjs)'];
for (const s of scenarios) {
  const r = results[s.name];
  const pick = (a) => a.filter((_, i) => i % s.stride === 0).map(num);
  lines.push(`SCN ${JSON.stringify(s)}`);
  lines.push(`OUT ${s.name} ${s.channels} ${pick(r.left).join(' ')}${s.channels === 2 ? ` | ${pick(r.right).join(' ')}` : ''}`);
}
const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, 'audio_synth_golden.txt'), lines.join('\n') + '\n');
console.log(`wrote ${scenarios.length} scenarios`);
