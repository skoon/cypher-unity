// Generates golden fixtures from the NIGHT SECTOR web prototype so the C# port
// can be checked against it number for number. Read-only against the prototype.
//
//   node tools/gen-golden.mjs <path-to-cyberpunkish> [seed]
//
// Writes Assets/Tests/EditMode/Fixtures/{rng_golden.txt,noise_golden.txt,city_golden_<seed>.txt,nav_golden_<seed>.txt}.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden.mjs <path-to-cyberpunkish> [seed]');
  process.exit(1);
}
const seed = Number(process.argv[3] ?? 20771);
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);

const { makeRng, perlin2, fbm2 } = await load('src/engine/Assets.js');
const { City } = await load('src/world/City.js');
const { buildNav } = await load('src/ai/CityNav.js');

const outDir = join(dirname(fileURLToPath(import.meta.url)), '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(outDir, { recursive: true });

// --- RNG: first 16 values for a few seeds (including a seed >= 2^31). ---
const rngLines = [];
for (const s of [1, 1337, 20771, 4294967295]) {
  const r = makeRng(s);
  const vals = Array.from({ length: 16 }, () => r());
  rngLines.push(`${s} ${vals.join(' ')}`);
}
writeFileSync(join(outDir, 'rng_golden.txt'), `# seed v0 v1 ... v15\n${rngLines.join('\n')}\n`);

// --- Noise: perlin2 and fbm2 at points covering negatives, fractions and lattice wrap (>256). ---
const noisePoints = [
  [0, 0], [0.37, 0.81], [5.5, 3.25], [-2.7, 4.1], [-13.9, -0.2], [100.123, 77.77],
  [255.9, 0.1], [256.3, 257.4], [511.5, -300.25], [0.001, 9.999], [42.42, 42.42], [-1000.5, 1000.5],
];
const noiseLines = ['# P x y perlin2 | F x y octaves fbm2 | W x y wet-roughness byte (512) | C x y concrete-roughness byte (256)'];
for (const [x, y] of noisePoints) {
  noiseLines.push(`P ${x} ${y} ${perlin2(x, y)}`);
  for (const octaves of [1, 3, 5]) noiseLines.push(`F ${x} ${y} ${octaves} ${fbm2(x, y, octaves)}`);
}
// Roughness fields: the prototype's two pure data-texture callbacks, stored through a Uint8Array.
const u8 = new Uint8Array(1);
const store = (v) => { u8[0] = v; return u8[0]; };
const wetRough = (x, y) => {
  const puddle = fbm2(x / 50, y / 50, 4) * 0.5 + 0.5;
  const grain = fbm2(x / 5, y / 5, 3) * 0.5 + 0.5;
  const wet = Math.pow(Math.max(0, puddle - 0.42) / 0.58, 0.65);
  const a = 70 + grain * 95;
  return store(a + (8 - a) * wet);
};
const concRough = (x, y) => store(165 + (fbm2(x / 18, y / 18, 4) * 0.5 + 0.5) * 78);
for (const [x, y] of [[0, 0], [10, 10], [123, 45], [200, 255], [255, 255], [300, 100], [511, 511], [77, 400]]) {
  noiseLines.push(`W ${x} ${y} ${wetRough(x, y)}`);
}
for (const [x, y] of [[0, 0], [10, 10], [123, 45], [200, 255], [255, 255], [64, 200]]) {
  noiseLines.push(`C ${x} ${y} ${concRough(x, y)}`);
}
writeFileSync(join(outDir, 'noise_golden.txt'), `${noiseLines.join('\n')}\n`);

// --- City layout: lots, alleys, buildings, volumes, spire/mast. ---
const city = new City({ seed });
city._layout(makeRng(seed));

const lines = [`# seed ${seed}`];
lines.push(`counts buildings=${city.buildings.length} lots=${city.lots.length} `
  + `alleys=${city.alleys.length} volumes=${city.volumes.length} streets=${city.streets.length}`);
for (const s of city.streets) lines.push(`S ${s.gx} ${s.gz} ${s.cx} ${s.cz} ${s.isPlaza ? 1 : 0}`);
for (const l of city.lots) lines.push(`L ${l.gx} ${l.gz} ${l.x0} ${l.z0} ${l.x1} ${l.z1} ${l.vacant ? 1 : 0}`);
for (const a of city.alleys) lines.push(`A ${a.axis} ${a.x0} ${a.z0} ${a.x1} ${a.z1}`);
city.buildings.forEach((b) => {
  lines.push(`B ${b.type} ${b.x} ${b.z} ${b.w} ${b.d} ${b.h} ${b.floors} ${b.volumes.length} `
    + `${b.rowBase} ${b.colBase} ${b.uo} ${b.tint.join(',')} `
    + `${b.spire ? [b.spire.x, b.spire.z, b.spire.y, b.spire.r, b.spire.h].join(',') : '-'} `
    + `${b.mast ? [b.mast.x, b.mast.z, b.mast.y, b.mast.h].join(',') : '-'}`);
});
city.volumes.forEach((v) => {
  lines.push(`V ${v.building} ${v.y0} ${v.y1} ${v.poly.length} ${v.poly.map((p) => `${p[0]} ${p[1]}`).join(' ')}`);
});
writeFileSync(join(outDir, `city_golden_${seed}.txt`), `${lines.join('\n')}\n`);
// --- Navigation: the pedestrian graph and road lattice of the same city. The prototype probes the kerb with physics raycasts, so a flat
// world stands in for it: the raised slab (y 0.185) within 25.5 m of a block centre (Chebyshev), asphalt beyond. The probe steps by 0.3 m from the
// block edge, so it reports the outer kerb as 25.4 m, which is what the C# test feeds its generic build. ---
const kerb = 25.5;
const flatWorld = {
  raycast(origin) {
    let best = Infinity;
    for (const s of city.streets) best = Math.min(best, Math.max(Math.abs(origin.x - s.cx), Math.abs(origin.z - s.cz)));
    return { point: { y: best <= kerb ? 0.185 : 0 } };
  },
};
const nav = buildNav({ cell: 62, blockSize: 46, streets: city.streets }, flatWorld);
const navLines = [`# seed ${seed}`];
navLines.push(`M ${nav.cell} ${nav.blockHalf} ${nav.walkOuter} ${nav.walkY} ${nav.roadY} ${nav.asphaltHalf} ${nav.ringR} ${nav.walkBand} ${nav.nodeCount}`);
navLines.push(`X ${nav.xLines.join(' ')}`);
navLines.push(`Z ${nav.zLines.join(' ')}`);
for (let i = 0; i < nav.nodeCount; i++) {
  navLines.push(`N ${i} ${nav.nx[i]} ${nav.nz[i]} ${nav.nBlock[i]} ${nav.adj[i].map((j, e) => `${j}:${nav.adjCross[i][e]}`).join(',')}`);
}
// ground height and kerb distance at a spread of points, and the band search with a fixed draw sequence
const probes = [[0, 0], [25.4, 0], [25.6, 0], [31, 31], [-217, -217], [140.3, -77.7], [-12.5, 200.1], [300, 300], [-31, 10], [93, 93]];
for (const [x, z] of probes) navLines.push(`G ${x} ${z} ${nav.kerbDistance(x, z)} ${nav.groundY(x, z)}`);
const draws = makeRng(99);
for (const [px, pz, lo, hi] of [[0, 0, 60, 120], [140, -80, 20, 70], [-200, 200, 0, 40], [5000, 5000, 0, 1]]) {
  navLines.push(`B ${px} ${pz} ${lo} ${hi} ${nav.nodeInBand(draws, px, pz, lo, hi)}`);
}
writeFileSync(join(outDir, `nav_golden_${seed}.txt`), `${navLines.join('\n')}\n`);

console.log(`wrote fixtures to ${outDir}: ${city.buildings.length} buildings, ${city.volumes.length} volumes`);
