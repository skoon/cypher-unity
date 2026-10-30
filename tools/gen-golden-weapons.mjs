// Runs the web prototype's player/WeaponModels.js headless (it only builds geometry, no WebGL) and writes every part of the three guns as a golden fixture for the
// C# port's model builders (Assets/Tests/EditMode/Fixtures/weapons_golden.txt). Read-only against the prototype.
//
//   node tools/gen-golden-weapons.mjs <path-to-cyberpunkish>
//
// Per gun: its name and anchors; per part: name, parent, pivot, and every vertex (the merged, non-indexed geometry, recentred on the pivot) as
// position, normal, linear colour, and metal/rough/emissive/channel, to 6 decimals.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-weapons.mjs <path-to-cyberpunkish>');
  process.exit(1);
}
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);
const { WEAPON_BUILDERS } = await load('src/player/WeaponModels.js');

const f = (v) => (Math.abs(v) < 5e-7 ? 0 : v).toFixed(6);
const lines = [];
for (const id of ['wasp', 'magnat', 'voltline']) {
  const def = WEAPON_BUILDERS[id]();
  lines.push(`gun ${def.id} ${def.name.replace(/ /g, '_')}`);
  for (const [k, p] of Object.entries(def.anchors)) lines.push(`anchor ${k} ${p.map(f).join(' ')}`);
  for (const part of def.parts) {
    const g = part.geo;
    const pos = g.attributes.position, nrm = g.attributes.normal, col = g.attributes.color, mre = g.attributes.aMRE;
    lines.push(`part ${part.name} ${part.parent ?? '-'} ${part.pivot.map(f).join(' ')} ${pos.count}`);
    for (let i = 0; i < pos.count; i++) {
      lines.push([
        pos.getX(i), pos.getY(i), pos.getZ(i), nrm.getX(i), nrm.getY(i), nrm.getZ(i),
        col.getX(i), col.getY(i), col.getZ(i), mre.getX(i), mre.getY(i), mre.getZ(i), mre.getW(i),
      ].map(f).join(' '));
    }
  }
}

const here = dirname(fileURLToPath(import.meta.url));
const out = join(here, '..', 'Assets', 'Tests', 'EditMode', 'Fixtures', 'weapons_golden.txt');
mkdirSync(dirname(out), { recursive: true });
writeFileSync(out, lines.join('\n') + '\n');
console.log(`wrote ${out}: ${lines.length} lines`);
