// Runs the web prototype's Traffic.js headless, under node with a stub engine context, and writes what it does as a golden fixture for the C# port
// (Assets/Tests/EditMode/Fixtures/traffic_golden_<seed>.txt). Read-only against the prototype.
//
//   node tools/gen-golden-traffic.mjs <path-to-cyberpunkish> [seed]
//
// Three scenarios on the same city, each from its own build with the player and the camera standing still: "medium" (the Medium tier's 52 vehicles and 20 aircraft, 60 s, player at
// the plaza), "ultra" (all 96 and 36, 60 s, player elsewhere) and "inroad" (all 96, the player standing in a lane so the vehicles have to stop for them). Nobody walks, so the
// prototype's behaviour is the whole of it: the C# port's extra rules for people on the road do nothing without people. The fleet table (type, speed, paint and under-glow picks) is
// written first, from the prototype's own meshes.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-traffic.mjs <path-to-cyberpunkish> [seed]');
  process.exit(1);
}
const seed = Number(process.argv[3] ?? 20771);
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);
// the prototype's own copy of three, so its modules and this script share one instance
const THREE = await load('node_modules/three/build/three.module.js');

const { makeRng } = await load('src/engine/Assets.js');
const { City } = await load('src/world/City.js');
const { Traffic } = await load('src/ai/Traffic.js');

const city = new City({ seed });
city._layout(makeRng(seed));
const published = { cell: 62, blockSize: 46, streets: city.streets };

// A flat world for the kerb probe, as in gen-golden.mjs: the raised slab (y 0.185) within 25.5 m of a block centre (the probe reads the edge as 25.4), asphalt beyond.
const kerb = 25.5;
const flatWorld = {
  raycast(origin) {
    let best = Infinity;
    for (const s of city.streets) best = Math.min(best, Math.max(Math.abs(origin.x - s.cx), Math.abs(origin.z - s.cz)));
    return { point: { y: best <= kerb ? 0.185 : 0 } };
  },
};

// Palettes, copied from Traffic.js: the paint and neon picks are recovered from the colours it gave each instance.
const PAINT = [0x1b1f2a, 0x2c3242, 0x101216, 0x3a2230, 0x1d3340, 0x40332a, 0x232323, 0x14232b, 0x2f2038, 0x35383e];
const NEON = [0x2ff0ff, 0xff2f9e, 0xa64bff, 0x36ff8a, 0xff7a1f, 0x2f6bff];
const paintColours = PAINT.map((h) => new THREE.Color(h).toArray());
const neonColours = NEON.map((h) => new THREE.Color(h).multiplyScalar(1.7).toArray());
const pick = (palette, got, what) => {
  const k = palette.findIndex((c) => c.every((v, n) => Math.abs(v - got[n]) < 1e-5));
  if (k < 0) throw new Error(`${what} ${got} is in no palette`);
  return k;
};

const STEP = 1 / 60;
const SCENARIOS = {
  medium: { quality: 'medium', player: [0, 0], checkpoints: [0, 60, 300, 900, 1800, 3600] },
  // 45 s: at 48 s a vehicle waiting to turn across traffic has overshot its waypoint and steers for a point exactly behind it, where the direction of the turn is decided by
  // the last bit of atan2 and the port cannot be expected to agree (see TrafficSimGoldenTests)
  ultra: { quality: 'ultra', player: [97, -64], checkpoints: [0, 600, 1800, 2700] },
  inroad: { quality: 'ultra', player: null, checkpoints: [0, 900, 1800] },
};

function run(label) {
  const sc = SCENARIOS[label];
  const camera = new THREE.PerspectiveCamera(70, 16 / 9, 0.1, 520);
  const ctx = {
    scene: { add() {} },
    camera,
    player: { position: new THREE.Vector3() },
    state: { quality: sc.quality },
    sys: { physics: flatWorld, assets: { canvasTexture: () => new THREE.Texture() } },
    events: { emit() {}, on: () => () => {} },
    city: published,
    time: { alpha: 1, elapsed: 0 },
  };
  // the player's place has to be known before the build, which seeds the traffic round them; the in-road scenario needs the nav's lane to find a lane, so read it first
  let [px, pz] = sc.player ?? [0, 0];
  if (!sc.player) {
    const probe = new Traffic();
    probe._build({ ...ctx, player: { position: new THREE.Vector3() } }, published);
    // the lane of the traffic running along +Z on the fourth north-south road
    px = probe.nav.xLines[3] - probe.lane;
    pz = 0;
    probe.dispose();
  }
  ctx.player.position.set(px, 0.185, pz);
  camera.position.set(px, 1.7, pz);

  const traffic = new Traffic();
  traffic._build(ctx, published);

  const lines = [`# ${label}: quality ${sc.quality}, player ${px} ${pz}, ${traffic.vehicles.length} live vehicles of ${traffic._vehiclePool.length}, ${traffic.flyers.length} live aircraft of ${traffic._flyerPool.length}`];
  // the fleet table, whole fleet, as it stands after the build
  for (const v of traffic._vehiclePool) {
    const paint = pick(paintColours, Array.from(traffic.meshes[`body${v.ti}`].slotColors.slice(v.slot * 3, v.slot * 3 + 3)), 'paint');
    const neon = pick(neonColours, Array.from(traffic.meshes.underglow.slotColors.slice(v.i * 3, v.i * 3 + 3)), 'neon');
    lines.push(`A ${label} ${v.i} ${v.ti} ${v.slot} ${v.cruise} ${v.spin} ${v.glow} ${v.lampTint} ${paint} ${neon}`);
  }
  for (const f of traffic._flyerPool) {
    const paint = pick(paintColours, Array.from(traffic.meshes[`air${f.ti}`].slotColors.slice(f.slot * 3, f.slot * 3 + 3)), 'aircraft paint');
    lines.push(`B ${label} ${f.i} ${f.ti} ${f.slot} ${paint}`);
  }

  const dump = (step) => {
    lines.push(`S ${label} ${step} ${traffic._cycle} ${traffic._cycleT}`);
    for (const v of traffic.vehicles) {
      lines.push(`V ${label} ${step} ${v.i} ${v.active ? 1 : 0} ${v.x} ${v.z} ${v.heading} ${v.speed} ${v.ix} ${v.iz} ${v.dx} ${v.dz} ${v.leg} ${v.turn} ${v.wpX} ${v.wpZ} ${v.spin}`);
    }
    for (const f of traffic.flyers) {
      lines.push(`F ${label} ${step} ${f.i} ${f.x} ${f.y} ${f.z} ${f.heading} ${f.speed} ${f.dirX} ${f.dirZ} ${f.bob} ${f.bank}`);
    }
  };
  let step = 0;
  let stopped = 0;
  for (const target of sc.checkpoints) {
    while (step < target) {
      traffic.fixedUpdate(STEP, ctx);
      step++;
      if (label === 'inroad') for (const v of traffic.vehicles) if (v.active && v.speed === 0) stopped++;
    }
    dump(step);
  }
  if (label === 'inroad') console.log(`inroad: vehicles stood still for ${stopped} vehicle-steps`);
  return lines;
}

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
const all = [`# seed ${seed}; dt 1/60`, ...run('medium'), ...run('ultra'), ...run('inroad')];
writeFileSync(join(outDir, `traffic_golden_${seed}.txt`), `${all.join('\n')}\n`);
console.log(`wrote ${all.length} lines to ${outDir}`);
