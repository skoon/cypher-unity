// Can this machine run Chromium with Web Audio? (The reference renders for the audio goldens come from it.)
import { createRequire } from 'node:module';
const require = createRequire('D:/source/cyberpunkish/package.json');
const { chromium } = require('playwright');

const browser = await chromium.launch({ headless: true });
const page = await browser.newPage();
await page.goto('about:blank');
const result = await page.evaluate(async () => {
  const ac = new OfflineAudioContext(1, 48000, 48000);
  const o = ac.createOscillator();
  o.type = 'sawtooth'; o.frequency.value = 440;
  o.connect(ac.destination); o.start(0);
  const buf = await ac.startRendering();
  const d = buf.getChannelData(0);
  return { ua: navigator.userAgent, n: d.length, first: Array.from(d.slice(0, 6)), peak: Math.max(...d) };
});
console.log(JSON.stringify(result));
await browser.close();
