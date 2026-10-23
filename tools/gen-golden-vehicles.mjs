// Runs the web prototype's Traffic.js headless, as gen-golden-traffic.mjs does, and writes what it builds as a golden fixture for the C# port's vehicle geometry and placement
// (Assets/Tests/EditMode/Fixtures/vehicles_golden_<seed>.txt). Read-only against the prototype.
//
//   node tools/gen-golden-vehicles.mjs <path-to-cyberpunkish> [seed]
//
// Two parts. The geometry: every mesh Traffic builds, as triangles (three vertices of x y z, and for the glowing meshes the colour times the vertex alpha, which is all that adds to the
// picture), plus the signal heads. The placement: after 600 steps at the Medium tier, with everything taken to be in view, the matrix the prototype's lateUpdate gives every part of every
// vehicle and aircraft, at an alpha of 0.37 and a game time of 12.3456 s.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-vehicles.mjs <path-to-cyberpunkish> [seed]');
  process.exit(1);
}
const seed = Number(process.argv[3] ?? 20771);
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);
const THREE = await load('node_modules/three/build/three.module.js');

const { makeRng } = await load('src/engine/Assets.js');
const { City } = await load('src/world/City.js');
const { Traffic } = await load('src/ai/Traffic.js');

const city = new City({ seed });
city._layout(makeRng(seed));
const published = { cell: 62, blockSize: 46, streets: city.streets };
const kerb = 25.5;
const flatWorld = {
  raycast(origin) {
    let best = Infinity;
    for (const s of city.streets) best = Math.min(best, Math.max(Math.abs(origin.x - s.cx), Math.abs(origin.z - s.cz)));
    return { point: { y: best <= kerb ? 0.185 : 0 } };
  },
};

const camera = new THREE.PerspectiveCamera(70, 16 / 9, 0.1, 520);
const ctx = {
  scene: { add() {} },
  camera,
  player: { position: new THREE.Vector3(0, 0.185, 0) },
  state: { quality: 'medium' },
  sys: { physics: flatWorld, assets: { canvasTexture: () => new THREE.Texture() } },
  events: { emit() {}, on: () => () => {} },
  city: published,
  time: { alpha: 0.37, elapsed: 12.3456 },
};
const traffic = new Traffic();
traffic._build(ctx, published);

const lines = [`# seed ${seed}`];

// --- geometry ---
function triangles(name, geometry, { first = Infinity } = {}) {
  const pos = geometry.attributes.position;
  const col = geometry.attributes.color;
  const index = geometry.index ? geometry.index.array : null;
  const count = (index ? index.length : pos.count) / 3;
  const shown = Math.min(count, first);
  lines.push(`G ${name} ${count} ${shown}`);
  for (let t = 0; t < shown; t++) {
    const row = [];
    for (let k = 0; k < 3; k++) {
      const i = index ? index[t * 3 + k] : t * 3 + k;
      row.push(pos.getX(i), pos.getY(i), pos.getZ(i));
    }
    if (col) {
      for (let k = 0; k < 3; k++) {
        const i = index ? index[t * 3 + k] : t * 3 + k;
        const a = col.itemSize === 4 ? col.getW(i) : 1;
        row.push(col.getX(i) * a, col.getY(i) * a, col.getZ(i) * a);
      }
    }
    lines.push(`T ${name} ${row.join(' ')}`);
  }
}

for (const name of ['body0', 'body1', 'body2', 'body3', 'air0', 'air1', 'wheels', 'rig', 'underglow', 'beams', 'airRig', 'shadow', 'lamps']) {
  triangles(name, traffic.meshes[name].geometry);
}
// the masts are 256 heads of 36 triangles: the first three heads are enough to check the parts and their order
triangles('masts', traffic.masts.geometry, { first: 108 });
for (const h of traffic.signalHeads) lines.push(`H ${h.px} ${h.pz} ${h.yaw} ${h.axis}`);

// --- placement ---
const STEP = 1 / 60;
for (let step = 0; step < 600; step++) traffic.fixedUpdate(STEP, ctx);
traffic._view = { update() {}, visible: () => true };
traffic.lateUpdate(STEP, ctx);
const m = traffic.meshes;
const row = (mesh, slot) => Array.from(mesh.slotMatrices.slice(slot * 16, slot * 16 + 16)).join(' ');
for (const v of traffic.vehicles) {
  if (!v.active) continue;
  lines.push(`M body ${v.i} ${row(m[`body${v.ti}`], v.slot)}`);
  for (let k = 0; k < 4; k++) lines.push(`M wheel ${v.i} ${k} ${row(m.wheels, v.i * 4 + k)}`);
  for (const name of ['rig', 'underglow', 'beams', 'shadow']) lines.push(`M ${name} ${v.i} ${row(m[name], v.i)}`);
}
for (const f of traffic.flyers) {
  lines.push(`M air ${f.i} ${row(m[`air${f.ti}`], f.slot)}`);
  lines.push(`M airRig ${f.i} ${row(m.airRig, f.i)}`);
}

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
writeFileSync(join(outDir, `vehicles_golden_${seed}.txt`), `${lines.join('\n')}\n`);
console.log(`wrote ${lines.length} lines to ${outDir}`);
