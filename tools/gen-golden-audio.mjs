// Runs the web prototype's Dsp.js and Analysis.js headless, under node, and writes what they produce as a golden fixture for the C# port
// (Assets/Tests/EditMode/Fixtures/audio_noise_golden.txt). Read-only against the prototype.
//
//   node tools/gen-golden-audio.mjs <path-to-cyberpunkish>
//
// Each noise bed is long (four seconds), so the fixture holds its length, an FNV-1a hash of the bytes of its single-precision samples (the hash matches only if every
// sample is bit for bit the same), and a few samples to read by eye. The analysis helper is run on three synthetic buffers.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-audio.mjs <path-to-cyberpunkish>');
  process.exit(1);
}
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);
const { makeNoise, DspCache, makeImpulseResponse, REVERB_SPACES, hash2 } = await load('src/audio/Dsp.js');
const { makeRng } = await load('src/engine/Assets.js');
const { Audio } = await load('src/audio/Audio.js');
const { analyzeBuffer, stereoWidth, decayTime } = await load('src/audio/Analysis.js');

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });

const fnv = (f32) => {
  const bytes = new Uint8Array(f32.buffer, f32.byteOffset, f32.byteLength);
  let h = 0x811c9dc5;
  for (let i = 0; i < bytes.length; i++) {
    h ^= bytes[i];
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h.toString(16).padStart(8, '0');
};
const num = (v) => (Number.isInteger(v) ? String(v) : v.toPrecision(17));

const lines = ['# audio goldens from src/audio/Dsp.js and Analysis.js'];

// the noise beds, as the prototype's DspCache makes them (seed 7717)
for (const rate of [48000, 44100]) {
  const cache = new DspCache();
  for (const kind of ['white', 'pink', 'brown', 'velvet']) {
    const b = cache.noise(kind, rate);
    const probe = [0, 1, 2, 3, 100, 1000, 2879, 2880, 50000, b.length - 1].map((i) => num(b[i]));
    lines.push(`N ${kind} ${rate} ${b.length} ${fnv(b)} ${probe.join(' ')}`);
  }
}
// another seed, to be sure the seed is read
{
  const b = makeNoise('pink', 48000, 12345);
  lines.push(`S pink 48000 12345 ${b.length} ${fnv(b)}`);
}

// analysis of synthetic buffers: a fake AudioBuffer is all it reads
const fake = (channels, sampleRate) => ({
  length: channels[0].length,
  numberOfChannels: channels.length,
  sampleRate,
  duration: channels[0].length / sampleRate,
  getChannelData: (c) => channels[c],
});
{
  const sr = 48000;
  const n = sr; // one second
  const sine = new Float32Array(n);
  for (let i = 0; i < n; i++) sine[i] = 0.5 * Math.sin((2 * Math.PI * 1000 * i) / sr);
  const noise = makeNoise('white', sr, 5).subarray(0, n);
  const burst = new Float32Array(n);
  for (let i = 0; i < 4800; i++) burst[i + 20000] = noise[i] * (1 - i / 4800) * 0.8;
  const stereoA = new Float32Array(n);
  const stereoB = new Float32Array(n);
  for (let i = 0; i < n; i++) { stereoA[i] = 0.3 * Math.sin((2 * Math.PI * 300 * i) / sr); stereoB[i] = 0.3 * Math.sin((2 * Math.PI * 300 * i) / sr + 1); }
  const cases = [['sine', fake([sine], sr)], ['noise', fake([noise], sr)], ['burst', fake([burst], sr)], ['stereo', fake([stereoA, stereoB], sr)]];
  for (const [name, buf] of cases) {
    const a = analyzeBuffer(buf);
    const b = a.bands;
    lines.push(`A ${name} ${num(a.peak)} ${num(a.rms)} ${num(a.peakDb)} ${num(a.rmsDb)} ${num(a.centroid)} ${num(b.sub)} ${num(b.low)} ${num(b.mid)} ${num(b.high)} ${num(b.air)} ${num(a.nonSilentRatio)} ${num(a.durationS)} ${a.channels} ${num(stereoWidth(buf))}`);
  }
  // the fixtures' input buffers, so the C# side builds the same ones
  lines.push(`I sine ${sr} 1000 0.5`);
  lines.push(`I noise ${sr} 5`);
  lines.push(`I burst ${sr} 5 20000 4800 0.8`);
  lines.push(`I stereo ${sr} 300 0.3 1`);
}

// RT60 estimate on one of the reverb impulse responses
{
  const sr = 48000;
  const ir = makeImpulseResponse(sr, REVERB_SPACES.street);
  const buf = fake([ir.left], sr);
  lines.push(`R street ${ir.left.length} ${num(decayTime(buf, 40))} ${fnv(ir.left)} ${fnv(ir.right)}`);
}

// the footstep surface hash
for (const [x, y] of [[0, 0], [1, 0], [0, 1], [-1, -1], [7, -13], [-25, 40], [100, 100], [-1000, 999], [123456, -654321], [2147483647, -2147483648], [3, 3], [12, 8]]) {
  lines.push(`H ${x} ${y} ${num(hash2(x, y))}`);
}

// the reverb-space probe: the prototype's own _updateSpace run over scripted rays
{
  const rng = makeRng(31337);
  const queue = [];
  const phys = {
    world: true,
    raycast(origin, dir, max) {
      const d = queue.shift();
      return d !== null && d < max ? { distance: d } : null;
    },
  };
  const audio = new Audio();
  audio.graph = { space: 'street', setReverbSpace(name) { this.space = name; } };
  const ctx = { sys: { physics: phys }, player: { collider: null }, events: { emit() {} } };
  let now = 0;
  const kinds = ['canyon', 'open', 'room', 'plaza', 'street', 'mixed', 'alley', 'doorway'];
  for (let n = 0; n < 90; n++) {
    // a few probes in a row of one kind, so the hysteresis has something to do
    const kind = kinds[Math.floor(n / 4) % kinds.length];
    const dist = [];
    for (let i = 0; i < 12; i++) {
      let d;
      if (kind === 'canyon' || kind === 'alley') d = 2 + rng() * 6;
      else if (kind === 'room') d = 2 + rng() * 9;
      else if (kind === 'plaza') d = rng() < 0.7 ? 40 : 20 + rng() * 15;
      else if (kind === 'open') d = rng() < 0.15 ? 40 : 14 + rng() * 12;
      else if (kind === 'street') d = 8 + rng() * 14;
      else if (kind === 'doorway') d = i < 3 ? 40 : 3 + rng() * 5;
      else d = rng() < 0.5 ? 40 : 4 + rng() * 30;
      dist.push(Math.round(d * 100) / 100);
    }
    let ceiling;
    if (kind === 'room') ceiling = 2.5 + rng() * 5;
    else if (kind === 'doorway') ceiling = 3 + rng() * 5;
    else if (kind === 'canyon') ceiling = 10 + rng() * 15;
    else ceiling = rng() < 0.3 ? 12 + rng() * 12 : 40;
    ceiling = Math.round(ceiling * 100) / 100;
    for (const d of dist) queue.push(d >= 40 ? null : d);
    queue.push(ceiling >= 25 ? null : ceiling);
    // now and then the space is forced and released again
    const override = n === 30 ? 'plaza' : n === 55 ? 'auto' : null;
    if (override) audio.setReverbSpace(override, 0);
    audio._updateSpace(ctx, now);
    lines.push(`P ${n} ${dist.map(num).join(' ')} ${num(ceiling)} ${override ?? '-'} ${audio.graph.space} ${num(audio._indoor)}`);
    now += 0.5;
  }
}

writeFileSync(join(outDir, 'audio_noise_golden.txt'), lines.join('\n') + '\n');
console.log(`wrote ${lines.length} lines`);
