// Shared helpers for the audio goldens that come from a real Web Audio engine: Chromium, run headless with Playwright (taken from the prototype's own node_modules, read-only).
import { createRequire } from 'node:module';

const require = createRequire('D:/source/cyberpunkish/package.json');
const { chromium } = require('playwright');

/** Opens a blank page in headless Chromium; the caller closes the browser. */
export async function openBlankPage() {
  const browser = await chromium.launch({ headless: true });
  const page = await browser.newPage();
  await page.goto('about:blank');
  return { browser, page };
}

/**
 * Renders synthesis scenarios (see gen-golden-synth.mjs for the description) in an OfflineAudioContext each and returns the left and right channels.
 * The page-side code is plain Web Audio, written once here so the scenario is the only thing the two engines are given.
 */
export async function renderScenarios(page, scenarios, beds) {
  return page.evaluate(async ({ scenarios, beds }) => {
    const wave = (name) => {
      const n = 40;
      const real = new Float32Array(n);
      const imag = new Float32Array(n);
      for (let h = 1; h < n; h++) {
        const roll = Math.exp(-h / 16);
        if (name === 'softsaw') imag[h] = (1 / h) * roll;
        else if (name === 'hollow') imag[h] = h % 2 === 1 ? (1 / h) * roll : 0;
        else if (name === 'bell') imag[h] = h <= 7 ? [0, 1, 0.42, 0.28, 0.14, 0.09, 0.05, 0.03][h] : 0;
      }
      return { real, imag };
    };
    const NATIVE = ['sine', 'triangle', 'sawtooth'];

    const results = {};
    for (const scn of scenarios) {
      const ac = new OfflineAudioContext(2, scn.frames, scn.rate);
      const nodes = { out: ac.destination };
      const param = (id, name) => {
        const n = nodes[id];
        const p = n[name === 'rate' ? 'playbackRate' : name === 'q' ? 'Q' : name];
        if (!p) throw new Error(`no param ${id}.${name}`);
        return p;
      };
      for (const d of scn.nodes) {
        let n;
        if (d.type === 'osc') {
          n = ac.createOscillator();
          if (NATIVE.includes(d.wave)) n.type = d.wave;
          else {
            const w = wave(d.wave);
            n.setPeriodicWave(ac.createPeriodicWave(w.real, w.imag, { disableNormalization: false }));
          }
          n.frequency.value = d.freq;
          if (d.detune) n.detune.value = d.detune;
          n.start(d.start ?? 0);
          if (d.stop != null) n.stop(d.stop);
        } else if (d.type === 'gain') {
          n = ac.createGain();
          n.gain.value = d.value;
        } else if (d.type === 'biquad') {
          n = ac.createBiquadFilter();
          n.type = d.filter;
          n.frequency.value = d.freq;
          n.Q.value = d.q;
          n.gain.value = d.gain ?? 0;
        } else if (d.type === 'delay') {
          n = ac.createDelay(d.max ?? 1);
          n.delayTime.value = d.time;
        } else if (d.type === 'pan') {
          n = ac.createStereoPanner();
          n.pan.value = d.value;
        } else if (d.type === 'comp') {
          n = ac.createDynamicsCompressor();
          n.threshold.value = d.threshold;
          n.knee.value = d.knee;
          n.ratio.value = d.ratio;
          n.attack.value = d.attack;
          n.release.value = d.release;
        } else if (d.type === 'const') {
          n = ac.createConstantSource();
          n.offset.value = d.offset;
          n.start(0);
        } else if (d.type === 'src') {
          n = ac.createBufferSource();
          let data;
          if (d.buffer === 'impulse') { data = new Float32Array(scn.frames); data[0] = 1; }
          else if (d.buffer === 'ramp') { data = new Float32Array(scn.frames); for (let i = 0; i < data.length; i++) data[i] = i / data.length; }
          else data = Float32Array.from(beds[`${d.buffer}@${scn.rate}`]);
          const buf = ac.createBuffer(1, data.length, scn.rate);
          buf.copyToChannel(data, 0);
          n.buffer = buf;
          n.loop = d.loop ?? false;
          n.playbackRate.value = d.rate ?? 1;
          if (d.duration != null) n.start(d.start ?? 0, d.offset ?? 0, d.duration);
          else n.start(d.start ?? 0, d.offset ?? 0);
        } else throw new Error(`unknown node type ${d.type}`);
        nodes[d.id] = n;
      }
      for (const [a, b] of scn.connect) {
        if (b.includes('.')) {
          const [id, name] = b.split('.');
          nodes[a].connect(param(id, name));
        } else nodes[a].connect(nodes[b]);
      }
      for (const a of scn.auto ?? []) {
        const p = param(a.node, a.param);
        for (const op of a.ops) {
          const [kind, ...args] = op;
          if (kind === 'set') p.setValueAtTime(args[0], args[1]);
          else if (kind === 'lin') p.linearRampToValueAtTime(args[0], args[1]);
          else if (kind === 'exp') p.exponentialRampToValueAtTime(args[0], args[1]);
          else if (kind === 'target') p.setTargetAtTime(args[0], args[1], args[2]);
          else if (kind === 'cancel') p.cancelScheduledValues(args[0]);
          else if (kind === 'value') p.value = args[0];
          else throw new Error(`unknown op ${kind}`);
        }
      }
      const buf = await ac.startRendering();
      results[scn.name] = { left: Array.from(buf.getChannelData(0)), right: Array.from(buf.getChannelData(1)) };
    }
    return results;
  }, { scenarios, beds });
}
