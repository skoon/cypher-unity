// Runs the web prototype's Crowd.js headless, under node with a stub engine context, and writes what it does as a golden fixture for the C# port
// (Assets/Tests/EditMode/Fixtures/crowd_golden_<seed>.txt). Read-only against the prototype.
//
//   node tools/gen-golden-crowd.mjs <path-to-cyberpunkish> [seed]
//
// Three scenarios on the same city and the same crowd. "free": the crowd as seeded, no traffic, 90 s. "kerbfree" and "kerbbusy": everyone is stood on a kerb-edge
// midpoint of one of the 25 blocks nearest the plaza, about to choose a way on, so crossings are chosen at once and often (in the seeded crowd they are rare); "kerbbusy"
// adds a stub traffic query that holds people at some kerbs, a pure function of where the crossing ends so the C# side can reproduce it. The player and the camera
// stand still at the plaza; the crowd steps at the prototype's 1/60 s.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-crowd.mjs <path-to-cyberpunkish> [seed]');
  process.exit(1);
}
const seed = Number(process.argv[3] ?? 20771);
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);
// the prototype's own copy of three, so its modules and this script share one instance
const THREE = await load('node_modules/three/build/three.module.js');

const { makeRng } = await load('src/engine/Assets.js');
const { City } = await load('src/world/City.js');
const { Crowd } = await load('src/ai/Crowd.js');

const city = new City({ seed });
city._layout(makeRng(seed));
const published = { cell: 62, blockSize: 46, streets: city.streets };

// A flat world for the kerb probe, as in gen-golden.mjs: the raised slab (y 0.185) within 25.5 m of a block centre, asphalt beyond.
const kerb = 25.5;
const flatWorld = {
  raycast(origin) {
    let best = Infinity;
    for (const s of city.streets) best = Math.min(best, Math.max(Math.abs(origin.x - s.cx), Math.abs(origin.z - s.cz)));
    return { point: { y: best <= kerb ? 0.185 : 0 } };
  },
};

// The traffic stub: a crossing is busy when this says so. A pure function of where the crossing ends and which way its traffic runs.
const busyTraffic = {
  asked: 0,
  held: 0,
  roadBusy(x, z, kind) {
    const busy = (Math.floor(x * 0.1) + Math.floor(z * 0.1) + kind) % 3 === 0;
    this.asked++;
    if (busy) this.held++;
    return busy;
  },
};

const STEP = 1 / 60;
const CHECKPOINTS = { free: [0, 30, 120, 360, 900, 2400, 5400], kerbfree: [0, 5, 30, 120, 360, 900], kerbbusy: [0, 5, 30, 120, 360, 900] };

function run(label, traffic, kerbStart) {
  const camera = new THREE.PerspectiveCamera(70, 16 / 9, 0.1, 520);
  camera.position.set(0, 1.7, 0);
  const player = { position: new THREE.Vector3(0, 0.185, 0) };
  const ctx = {
    scene: { add() {} },
    camera,
    player,
    state: { quality: 'medium' },
    sys: {
      physics: flatWorld,
      assets: { canvasTexture: () => new THREE.Texture() },
      traffic,
    },
    events: { emit() {}, on: () => () => {} },
    city: published,
    time: { alpha: 1, elapsed: 0 },
  };
  const crowd = new Crowd();
  crowd._build(ctx, published);

  if (kerbStart) {
    // Stand each person on a kerb-edge midpoint (node 1, 3, 5 or 7 of the block's ring), just arrived, about to pick the way on.
    const nav = crowd.nav;
    const near = nav.blocks.map((b, bi) => ({ b, bi })).filter(({ b }) => Math.abs(b.cx) < 100 && Math.abs(b.cz) < 100).map(({ bi }) => bi);
    crowd.peds.forEach((p, n) => {
      const bi = near[n % near.length];
      const k = [1, 3, 5, 7][n % 4];
      const node = bi * 8 + k;
      const next = bi * 8 + ((k + 1) % 8);
      p.node = node;
      p.prevNode = bi * 8 + ((k + 7) % 8);
      p.x = nav.nx[node]; p.z = nav.nz[node]; p.y = nav.walkY;
      // facing the road, so a crossing is a likely choice (walking along the ring, the prototype all but never picks one: see the C# notes)
      const kCross = nav.adjCross[node].findIndex((c) => c !== 0);
      const across = kCross >= 0 ? nav.adj[node][kCross] : next;
      p.heading = Math.atan2(nav.nx[across] - nav.nx[node], nav.nz[across] - nav.nz[node]);
      p.tx = p.x; p.tz = p.z; p.lat = 0; p.crossing = 0;
      p.state = 1; p.timer = 5; p.waitTimer = 0; p.speed = p.cruise;
      p.ox = p.x; p.oz = p.z; p.oy = p.y; p.oheading = p.heading; p.ophase = p.phase;
    });
  }

  const lines = [];
  const f = (v) => (typeof v === 'number' ? v : Number(v));
  if (label === 'free') {
    // The appearance table and the seeded start of the whole pool (400), before anything moves.
    for (const p of crowd._pool) {
      lines.push(`A ${p.i} ${p.scale} ${p.build} ${p.long ? 1 : 0} ${p.umbrella ? 1 : 0} ${p.hood ? 1 : 0} ${p.pack ? 1 : 0} ${p.trim ? 1 : 0} ${p.skin} ${p.coat} `
        + `${p.pants} ${p.trimColour} ${p.trimPower} ${p.umbrellaColour} ${p.cruise} ${p.hurried ? 1 : 0} ${p.lookSeed} ${p.phase}`);
    }
  }
  const dump = (step) => {
    for (const p of crowd.peds) {
      lines.push(`T ${label} ${step} ${p.i} ${f(p.x)} ${f(p.z)} ${f(p.y)} ${f(p.heading)} ${f(p.phase)} ${f(p.speed)} ${p.node} ${p.prevNode} ${p.state} ${p.crossing} `
        + `${f(p.timer)} ${f(p.waitTimer)} ${f(p.tx)} ${f(p.tz)} ${f(p.lat)}`);
    }
  };
  let step = 0;
  for (const target of CHECKPOINTS[label]) {
    while (step < target) { crowd.fixedUpdate(STEP, ctx); step++; }
    dump(step);
  }
  lines.unshift(`# ${label}: ${crowd.peds.length} active of ${crowd._pool.length}`);
  return lines;
}

// --- The rig: Crowd._pose's joint matrices for 60 synthetic states (the pool's appearances, made-up positions, headings, phases and speeds), so the C#
// port of the pose (which the shader is in turn checked against) can be compared matrix for matrix. Alpha 0.37 exercises the interpolation. ---
function poseGolden() {
  const camera = new THREE.PerspectiveCamera(70, 16 / 9, 0.1, 520);
  const ctx = {
    scene: { add() {} }, camera, player: { position: new THREE.Vector3() }, state: { quality: 'medium' },
    sys: { physics: flatWorld, assets: { canvasTexture: () => new THREE.Texture() } }, events: { emit() {}, on: () => () => {} },
    city: published, time: { alpha: 1, elapsed: 0 },
  };
  const crowd = new Crowd();
  crowd._build(ctx, published);
  const r = makeRng(555);
  const alpha = 0.37;
  const lines = [`# seed ${seed}; alpha ${alpha}`];
  const slotOf = (p) => ({
    torso: [p.long ? crowd.meshes.torsoLong : crowd.meshes.torsoShort, p.slots.torso],
    trim: [crowd.meshes.trim, p.slots.trim],
    head: [crowd.meshes.head, p.i],
    hood: [crowd.meshes.hood, p.slots.hood],
    armL: [crowd.meshes.armL, p.i],
    armR: [crowd.meshes.armR, p.i],
    umbrella: [crowd.meshes.umbrella, p.slots.umbrella],
    pack: [crowd.meshes.pack, p.slots.pack],
    thighL: [crowd.meshes.thighL, p.i],
    shinL: [crowd.meshes.shinL, p.i],
    thighR: [crowd.meshes.thighR, p.i],
    shinR: [crowd.meshes.shinR, p.i],
  });
  const present = (p, name) => (name === 'trim' ? p.trim : name === 'hood' ? p.hood : name === 'umbrella' ? p.umbrella : name === 'pack' ? p.pack : true);
  for (let k = 0; k < 60; k++) {
    const p = crowd._pool[k];
    p.ox = r() * 20 - 10; p.oz = r() * 20 - 10; p.oy = r() * 0.2;
    p.x = p.ox + (r() - 0.5) * 0.05; p.z = p.oz + (r() - 0.5) * 0.05; p.y = p.oy + (r() - 0.5) * 0.02;
    p.oheading = (r() * 2 - 1) * Math.PI * 1.2; p.heading = p.oheading + (r() - 0.5) * 0.5;
    p.ophase = r() * Math.PI * 2; p.phase = p.ophase + r() * 0.1;
    p.speed = [0, 0.3, 1.2, 2.5, 2.8][k % 5];
    p.lookYaw = (r() - 0.5) * 0.6;
    p.lod = k % 3 === 0 ? 0 : 1;
    const t = 12.3456 + k * 0.77;
    const cam = { x: r() * 6 - 3, y: 1.7, z: r() * 6 - 3 };
    const lookIn = p.lookYaw;
    crowd._pose(p, alpha, t, cam);
    lines.push(`P ${k} ${p.i} ${p.ox} ${p.oz} ${p.oy} ${p.x} ${p.z} ${p.y} ${p.oheading} ${p.heading} ${p.ophase} ${p.phase} ${p.speed} ${lookIn} ${p.lod === 0 ? 1 : 0} ${t} ${cam.x} ${cam.z}`);
    lines.push(`L ${k} ${p.lookYaw}`);
    for (const [name, [mesh, slot]] of Object.entries(slotOf(p))) {
      if (!present(p, name)) continue;
      lines.push(`M ${k} ${name} ${Array.from(mesh.slotMatrices.slice(slot * 16, slot * 16 + 16)).join(' ')}`);
    }
  }
  return lines;
}

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });
const all = [`# seed ${seed}; dt 1/60`, ...run('free', undefined, false), ...run('kerbfree', undefined, true), ...run('kerbbusy', busyTraffic, true)];
console.log(`the busy stub was asked ${busyTraffic.asked} times and held people back ${busyTraffic.held} times`);
writeFileSync(join(outDir, `crowd_golden_${seed}.txt`), `${all.join('\n')}\n`);
console.log(`wrote ${all.length} lines to ${outDir}`);
writeFileSync(join(outDir, `pose_golden_${seed}.txt`), `${poseGolden().join('\n')}\n`);
console.log('wrote the pose golden');
