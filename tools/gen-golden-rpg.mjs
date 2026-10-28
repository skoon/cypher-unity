// Runs the web prototype's RPG rules (src/rpg) headless under node and writes what they do as a golden fixture for the C# port
// (Assets/Tests/EditMode/Fixtures/rpg_character_golden.jsonl). Read-only against the prototype.
//
//   node tools/gen-golden-rpg.mjs <path-to-cyberpunkish>
//
// One JSON object per line, each a "suite": the XP curves, derived stats, and seeded random scripts of operations on a StatBlock, a PerkTree, a CyberwareRig,
// the loot roller, an Inventory and the Economy (with a stub host), conditions against a fixed view, and save migration. Every operation is written with its
// arguments and with what came back and the state after it, so the C# test replays the arguments and compares. Results that hold an item, a perk or a piece of
// chrome are reduced to its uid or id; an operation that throws is written as { throws: <error name> }.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-rpg.mjs <path-to-cyberpunkish>');
  process.exit(1);
}
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);

const { makeRng } = await load('src/engine/Assets.js');
const {
  ATTRIBUTES, StatBlock, ModifierSet, applyModifiers, baseDerived, xpToNext, totalXpForLevel, levelFromXp, levelUpAwards, credToNext, credFromXp,
  DERIVED_KEYS, LEVEL_CAP, CRED_CAP,
} = await load('src/rpg/Stats.js');
const { PerkTree, PERKS } = await load('src/rpg/Perks.js');
const { CyberwareRig, CYBERWARE } = await load('src/rpg/Cyberware.js');
const { LootGenerator, makeCyberwareItem, ITEM_TYPES, EQUIP_SLOTS, RARITY_KEYS, ITEM_TEMPLATES } = await load('src/rpg/Items.js');
const { Inventory } = await load('src/rpg/Inventory.js');
const { Economy, VENDORS } = await load('src/rpg/Economy.js');
const { checkCondition, describeCondition } = await load('src/rpg/Conditions.js');
const { migrate, SaveManager, MemoryStorage } = await load('src/rpg/Save.js');

const lines = [];
const out = (o) => lines.push(JSON.stringify(o));

/** Items to uids, perks and chrome to ids, so results compare by identity, not by every field. */
function norm(v) {
  if (v === null || v === undefined) return v ?? null;
  if (Array.isArray(v)) return v.map(norm);
  if (typeof v !== 'object') return typeof v === 'number' && !Number.isFinite(v) ? null : v;
  if ('uid' in v && 'templateId' in v) return v.uid;
  if ('maxRank' in v && 'tree' in v) return v.id;
  if ('corp' in v && 'capacity' in v && 'slot' in v) return v.id;
  const o = {};
  for (const [k, x] of Object.entries(v)) if (x !== undefined) o[k] = norm(x);
  return o;
}
const plain = (v) => JSON.parse(JSON.stringify(v));

function attempt(fn) {
  try {
    return { result: norm(fn()) };
  } catch (err) {
    return { throws: err.name };
  }
}

const pickOf = (rng, arr) => arr[Math.floor(rng() * arr.length)];
const intIn = (rng, a, b) => a + Math.floor(rng() * (b - a + 1));

// ---------------------------------------------------------------- curves and derived stats

{
  const xp = [];
  const total = [];
  for (let l = 1; l <= LEVEL_CAP; l++) { xp.push(xpToNext(l)); total.push(totalXpForLevel(l)); }
  const cred = [];
  for (let l = 1; l <= CRED_CAP; l++) cred.push(credToNext(l));
  const rng = makeRng(5);
  const samples = [0, 1, 274, 275, 276, 1000, 123456, 999999999];
  for (let i = 0; i < 40; i++) samples.push(Math.floor(rng() * 400000));
  out({
    suite: 'curves',
    xpToNext: norm(xp), totalXpForLevel: total, credToNext: norm(cred),
    levelFromXp: samples.map((x) => [x, ...Object.values(norm(levelFromXp(x)))]),
    credFromXp: samples.map((x) => [x, ...Object.values(norm(credFromXp(x)))]),
    awards: Array.from({ length: 30 }, (_, i) => Object.values(levelUpAwards(i + 1))),
  });
}

const STATS_FOR_MODS = [...DERIVED_KEYS];
function randomMods(rng, n) {
  const mods = [];
  for (let i = 0; i < n; i++) {
    const stat = pickOf(rng, STATS_FOR_MODS);
    const op = rng() < 0.75 ? 'add' : 'mul';
    const value = op === 'mul' ? Math.round((rng() * 0.8 - 0.2) * 1000) / 1000 : Math.round((rng() * 60 - 15) * 100) / 100;
    mods.push({ stat, op, value });
  }
  return mods;
}
function randomAttrs(rng, lo = 3, hi = 20) {
  return Object.fromEntries(ATTRIBUTES.map((a) => [a, intIn(rng, lo, hi)]));
}

{
  const rng = makeRng(77);
  for (let i = 0; i < 60; i++) {
    const attrs = randomAttrs(rng);
    const level = intIn(rng, 1, 50);
    const mods = randomMods(rng, intIn(rng, 0, 6));
    out({ suite: 'derived', attrs, level, mods, base: baseDerived(attrs, level), applied: applyModifiers(baseDerived(attrs, level), mods) });
  }
}

// ---------------------------------------------------------------- the stat block

for (const seed of [1, 2, 3, 4, 5, 6]) {
  const rng = makeRng(seed * 101);
  const attrs = randomAttrs(rng, 3, 12);
  const stats = new StatBlock({ attributes: attrs });
  const ops = [];
  const state = () => ({
    serialized: stats.serialize(), level: stats.level, xpInto: stats.xpInto, xpToNext: norm(stats.xpToNext), streetCred: stats.streetCred,
    credInto: stats.credInto, credToNext: norm(stats.credToNext), derived: stats.derived, sources: stats.modifiers.sources(),
  });
  for (let i = 0; i < 50; i++) {
    const r = rng();
    let op;
    if (r < 0.3) {
      const amount = rng() < 0.1 ? -intIn(rng, 0, 100) : Math.round(rng() * 4000 * 10) / 10;
      op = { op: 'addXp', amount, ...attempt(() => stats.addXp(amount)) };
    } else if (r < 0.45) {
      const amount = rng() < 0.1 ? 0 : Math.round(rng() * 600 * 10) / 10;
      op = { op: 'addCredXp', amount, ...attempt(() => stats.addCredXp(amount)) };
    } else if (r < 0.65) {
      const attr = rng() < 0.05 ? 'charisma' : pickOf(rng, ATTRIBUTES);
      op = { op: 'spend', attr, ...attempt(() => stats.spendAttributePoint(attr)) };
    } else if (r < 0.72) {
      const n = intIn(rng, 1, 6);
      stats.attributePoints += n;
      op = { op: 'grant', n };
    } else if (r < 0.92) {
      const source = pickOf(rng, ['perk:a', 'cyber:b', 'buff:c', 'item:d']);
      const mods = rng() < 0.15 ? [] : randomMods(rng, intIn(rng, 1, 3));
      if (rng() < 0.3) mods.push({ stat: 'xpRate', op: 'add', value: Math.round(rng() * 50) / 100 });
      op = { op: 'setMods', source, mods, ...attempt(() => { stats.modifiers.set(source, mods); stats.invalidate(); }) };
    } else {
      const source = pickOf(rng, ['perk:a', 'cyber:b', 'buff:c', 'item:d']);
      op = { op: 'removeMods', source, result: stats.modifiers.remove(source) };
      stats.invalidate();
    }
    op.state = plain(state());
    ops.push(op);
  }
  out({ suite: 'stats', seed, attrs, ops });
}

// ---------------------------------------------------------------- perks

for (const seed of [11, 12, 13, 14]) {
  const rng = makeRng(seed * 37);
  const tree = new PerkTree();
  const ops = [];
  for (let i = 0; i < 70; i++) {
    const stats = { attributes: randomAttrs(rng, 3, 20), level: intIn(rng, 1, 25), perkPoints: intIn(rng, 0, 4) };
    const r = rng();
    const id = rng() < 0.03 ? 'not_a_perk' : pickOf(rng, PERKS).id;
    let op;
    if (r < 0.35) op = { op: 'canBuy', id, stats: plain(stats), ...attempt(() => tree.canBuy(id, stats)) };
    else if (r < 0.93) op = { op: 'buy', id, stats: plain(stats), ...attempt(() => tree.buy(id, stats)) };
    else op = { op: 'respec', stats: plain(stats), ...attempt(() => tree.respec(stats)) };
    op.perkPointsAfter = stats.perkPoints;
    op.serialized = tree.serialize();
    op.abilities = tree.abilities();
    op.mods = tree.applyTo(new ModifierSet()).all();
    ops.push(op);
  }
  out({ suite: 'perks', seed, ops });
}

// ---------------------------------------------------------------- cyberware

for (const seed of [21, 22, 23, 24]) {
  const rng = makeRng(seed * 53);
  const rig = new CyberwareRig();
  const ops = [];
  for (let i = 0; i < 60; i++) {
    const stats = { attributes: randomAttrs(rng, 3, 20), level: intIn(rng, 1, 20), streetCred: intIn(rng, 1, 6), cyberCapacity: intIn(rng, 4, 40) };
    const r = rng();
    const id = rng() < 0.03 ? 'cw_nope' : pickOf(rng, CYBERWARE).id;
    let op;
    if (r < 0.3) op = { op: 'canInstall', id, stats: plain(stats), ...attempt(() => rig.canInstall(id, stats)) };
    else if (r < 0.8) op = { op: 'install', id, stats: plain(stats), ...attempt(() => rig.install(id, stats)) };
    else op = { op: 'remove', id, ...attempt(() => rig.remove(id)) };
    op.serialized = rig.serialize();
    op.used = rig.usedCapacity();
    op.bySlot = rig.bySlot();
    op.abilities = rig.abilities();
    op.mods = rig.applyTo(new ModifierSet()).all();
    ops.push(op);
  }
  out({ suite: 'cyber', seed, ops });
}

// ---------------------------------------------------------------- loot

const TYPES_FOR_ROLLS = ITEM_TYPES.filter((t) => t !== 'cyberware').concat(['cyberware']);
for (const seed of [1, 7, 4242, 20771, 90210]) {
  const gen = new LootGenerator(seed);
  const rng = makeRng(seed + 999);
  const ops = [];
  for (let i = 0; i < 80; i++) {
    const r = rng();
    if (r < 0.7) {
      const opts = {};
      if (rng() < 0.9) opts.level = rng() < 0.2 ? Math.round(rng() * 300) / 10 : intIn(rng, 1, 40);
      if (rng() < 0.5) opts.luck = Math.round(rng() * 100) / 100;
      if (rng() < 0.5) opts.tier = intIn(rng, 0, 3);
      const which = rng();
      if (which < 0.35) opts.type = pickOf(rng, TYPES_FOR_ROLLS);
      else if (which < 0.6) opts.templateId = pickOf(rng, ITEM_TEMPLATES).id;
      if (rng() < 0.25) opts.rarity = pickOf(rng, RARITY_KEYS);
      let entry;
      try { entry = { item: plain(gen.rollItem(opts)) }; } catch (err) { entry = { throws: err.name }; }
      ops.push({ op: 'rollItem', opts, ...entry });
    } else if (r < 0.9) {
      const opts = { level: intIn(rng, 1, 30), luck: Math.round(rng() * 50) / 100, tier: intIn(rng, 0, 2) };
      if (rng() < 0.5) opts.count = intIn(rng, 0, 4);
      if (rng() < 0.3) opts.moneyBase = intIn(rng, 10, 200);
      const drop = gen.rollDrop(opts);
      ops.push({ op: 'rollDrop', opts, items: plain(drop.items), money: drop.money });
    } else if (r < 0.95) {
      gen.reset();
      ops.push({ op: 'reset' });
    } else {
      ops.push({ op: 'uid', uid: gen.uid() });
    }
  }
  out({ suite: 'loot', seed, ops });
}
out({ suite: 'cyberItems', items: plain(CYBERWARE.map((c, i) => makeCyberwareItem(c.id, `cw${i}`))) });

// ---------------------------------------------------------------- inventory

for (const seed of [31, 32, 33, 34, 35]) {
  const rng = makeRng(seed * 71);
  const gen = new LootGenerator(seed);
  const init = { capacity: intIn(rng, 15, 120), deckSize: intIn(rng, 3, 5) };
  const inv = new Inventory(init);
  const ops = [];
  let fresh = 0;
  for (let i = 0; i < 90; i++) {
    const r = rng();
    const anyUid = () => (inv.items.length && rng() < 0.9 ? pickOf(rng, inv.items).uid : 'nope');
    let op;
    if (r < 0.4) {
      let item;
      if (inv.items.length && rng() < 0.35) {
        const base = pickOf(rng, inv.items);
        item = { ...base, uid: `copy${++fresh}`, qty: intIn(rng, 1, 14) };
      } else {
        const type = pickOf(rng, ['consumable', 'junk', 'quickhack', 'weapon', 'clothing', 'consumable', 'junk', 'cyberware']);
        item = gen.rollItem({ type, level: intIn(rng, 1, 12) });
        if ((item.maxStack ?? 1) > 1 && rng() < 0.5) item.qty = intIn(rng, 1, 25);
      }
      const json = plain(item);
      op = { op: 'add', item: json, ...attempt(() => inv.add(item)) };
    } else if (r < 0.55) {
      const uid = anyUid();
      const qty = rng() < 0.05 ? 0 : intIn(rng, 1, 6);
      op = { op: 'remove', uid, qty, ...attempt(() => inv.remove(uid, qty)) };
    } else if (r < 0.62) {
      const templateId = inv.items.length ? pickOf(rng, inv.items).templateId : 'c_maxdoc';
      const qty = intIn(rng, 1, 12);
      op = { op: 'removeByTemplate', templateId, qty, ...attempt(() => inv.removeByTemplate(templateId, qty)) };
    } else if (r < 0.75) {
      const uid = anyUid();
      op = { op: 'equip', uid, ...attempt(() => inv.equip(uid)) };
    } else if (r < 0.82) {
      const slot = rng() < 0.05 ? 'nonsense' : pickOf(rng, EQUIP_SLOTS);
      op = { op: 'unequip', slot, ...attempt(() => inv.unequip(slot)) };
    } else if (r < 0.92) {
      const hacks = inv.items.filter((x) => x.type === 'quickhack');
      const uid = hacks.length && rng() < 0.8 ? pickOf(rng, hacks).uid : anyUid();
      const index = intIn(rng, -1, 5);
      op = { op: 'equipHack', uid, index, ...attempt(() => inv.equipHack(uid, index)) };
    } else if (r < 0.96) {
      const index = intIn(rng, -1, 5);
      op = { op: 'unequipHack', index, ...attempt(() => inv.unequipHack(index)) };
    } else {
      const n = rng() < 0.1 ? 0 : intIn(rng, 1, 6);
      op = { op: 'setDeckSize', n, ...attempt(() => { inv.setDeckSize(n); }) };
    }
    const full = i % 15 === 14;
    const ser = inv.serialize();
    if (!full) ser.items = ser.items.map((x) => [x.uid, x.qty]);
    op.state = plain({
      full, serialized: ser, weight: inv.weight, free: inv.free, overweight: inv.overweight, size: inv.size,
      consumables: inv.byType('consumable').map((x) => x.uid), deck: inv.deckItems().map((x) => x?.uid ?? null),
      equippedModifiers: inv.equippedModifiers(), counts: Object.fromEntries(['c_maxdoc', 'j_scrap', 'c_stim'].map((t) => [t, inv.count(t)])),
    });
    ops.push(op);
  }
  out({ suite: 'inventory', seed, init, ops });
}

// ---------------------------------------------------------------- economy

const FLAG_NAMES = [...new Set(VENDORS.flatMap((v) => Object.keys(v.flagDiscounts ?? {})))];
for (const seed of [41, 42, 43, 44]) {
  const rng = makeRng(seed * 91);
  const attrs = randomAttrs(rng, 3, 14);
  const xp = intIn(rng, 0, 9000);
  const credXp = intIn(rng, 0, 600);
  const stats = new StatBlock({ attributes: attrs, xp, credXp });
  const flags = {};
  const inventory = new Inventory({ capacity: intIn(rng, 30, 150) });
  let uid = 0;
  let events = [];
  let economy;
  const view = {
    get level() { return stats.level; },
    get streetCred() { return stats.streetCred; },
    get money() { return economy.money; },
    attribute: (n) => stats.attributes[n],
    hasPerk: () => false,
    hasCyberware: () => false,
    hasAbility: () => false,
    flag: (n) => flags[n],
    countItem: (t) => inventory.count(t),
    questState: () => ({ state: 'inactive', stage: null, outcome: null }),
  };
  const host = {
    emit: (type, payload) => events.push({ type, payload: norm(payload) }),
    inventory,
    getDerived: () => stats.derived,
    getFlag: (f) => flags[f],
    getLevel: () => stats.level,
    nextUid: () => `u${++uid}`,
    view,
  };
  const money = intIn(rng, 0, 20000);
  economy = new Economy(host, { money });
  const gen = new LootGenerator(seed);
  const start = [];
  for (let i = 0; i < 6; i++) {
    const item = gen.rollItem({ level: 4 });
    start.push(plain(item));
    inventory.add(item);
  }
  const ops = [];
  for (let i = 0; i < 80; i++) {
    const r = rng();
    const vendorId = pickOf(rng, VENDORS).id;
    let op;
    if (r < 0.12) {
      const name = pickOf(rng, FLAG_NAMES);
      const value = rng() < 0.6 ? true : rng() < 0.5 ? false : 0;
      flags[name] = value;
      op = { op: 'setFlag', name, value };
    } else if (r < 0.2) {
      const attr = pickOf(rng, ATTRIBUTES);
      const value = intIn(rng, 3, 20);
      stats.attributes[attr] = value;
      stats.invalidate();
      op = { op: 'setAttr', attr, value };
    } else if (r < 0.35) {
      op = { op: 'open', vendorId, ...attempt(() => economy.open(vendorId)) };
    } else if (r < 0.4) {
      op = { op: 'close', ...attempt(() => economy.close()) };
    } else if (r < 0.62) {
      const stock = economy.state(vendorId).stock;
      const u = stock.length && rng() < 0.9 ? pickOf(rng, stock).uid : 'nope';
      op = { op: 'buy', vendorId, uid: u, ...attempt(() => economy.buy(vendorId, u)) };
    } else if (r < 0.77) {
      const u = inventory.items.length && rng() < 0.9 ? pickOf(rng, inventory.items).uid : 'nope';
      const qty = intIn(rng, 1, 3);
      op = { op: 'sell', vendorId, uid: u, qty, ...attempt(() => economy.sell(vendorId, u, qty)) };
    } else if (r < 0.87) {
      const hours = Math.round(rng() * 300) / 10;
      op = { op: 'tick', hours, ...attempt(() => { economy.tick(hours); }) };
    } else if (r < 0.95) {
      const amount = Math.round((rng() * 6000 - 3000) * 10) / 10;
      op = { op: 'addMoney', amount, ...attempt(() => economy.addMoney(amount, 'test')) };
    } else {
      const force = rng() < 0.5;
      op = { op: 'restock', vendorId, force, ...attempt(() => economy.restock(vendorId, { force })) };
    }
    op.events = events;
    events = [];
    const full = i % 20 === 19;
    const eco = economy.serialize();
    const inv = inventory.serialize();
    if (!full) {
      for (const v of eco.vendors) v.stock = v.stock.map((x) => [x.uid, x.qty]);
      inv.items = inv.items.map((x) => [x.uid, x.qty]);
    }
    op.state = plain({ full, economy: eco, inventory: inv, snapshot: norm(economy.snapshot(vendorId)), vendorId, open: economy.openVendorId });
    ops.push(op);
  }
  out({ suite: 'economy', seed, attrs, xp, credXp, money, capacity: inventory.capacity, start, ops });
}

// ---------------------------------------------------------------- conditions

{
  const viewData = {
    level: 7, streetCred: 3, money: 900,
    attributes: { body: 5, reflexes: 8, intelligence: 12, technical: 6, cool: 4 },
    perks: { bulk: 2, ghost: 1 },
    cyberware: ['cw_kiroshi_mk1'],
    abilities: ['scanner', 'remote_breach'],
    flags: { kaz_honest: true, count: 3, name: 'wire', off: false, zero: 0 },
    items: { j_scrap: 2, q_ghost_shard: 1 },
    quests: { q_main_ghost: { state: 'active', stage: 's_meet', outcome: null }, q_side: { state: 'done', stage: null, outcome: 'clean' } },
  };
  const view = {
    level: viewData.level, streetCred: viewData.streetCred, money: viewData.money,
    attribute: (n) => viewData.attributes[n],
    hasPerk: (id, rank) => (viewData.perks[id] ?? 0) >= rank,
    hasCyberware: (id) => viewData.cyberware.includes(id),
    hasAbility: (t) => viewData.abilities.includes(t),
    flag: (n) => viewData.flags[n],
    countItem: (t) => viewData.items[t] ?? 0,
    questState: (id) => viewData.quests[id] ?? { state: 'inactive', stage: null, outcome: null },
  };
  const fixed = [
    null, {}, { level: 5 }, { level: 9 }, { streetCred: 3 }, { streetCred: 4 }, { money: 900 }, { money: 5000 },
    { attribute: { intelligence: 6 } }, { attribute: { intelligence: 13, cool: 4, body: 9 } },
    { perk: 'bulk' }, { perk: ['bulk', 'ghost', 'haggler'] }, { perk: { bulk: 2, ghost: 2 } },
    { cyberware: 'cw_kiroshi_mk1' }, { cyberware: ['cw_kiroshi_mk1', 'cw_netdriver'] },
    { ability: 'scanner' }, { ability: ['remote_breach', 'double_jump'] },
    { flag: 'kaz_honest' }, { flag: ['kaz_honest', 'off', 'unset'] }, { flag: { kaz_honest: true, off: false, count: 3, name: 'wire' } },
    { flag: { off: true, zero: false, count: 4, name: 'kaz', unset: false } },
    { notFlag: 'kaz_honest' }, { notFlag: ['off', 'zero', 'unset'] },
    { item: { j_scrap: 2 } }, { item: { j_scrap: 9, q_ghost_shard: 1 } },
    { quest: { id: 'q_main_ghost', state: 'active' } }, { quest: [{ id: 'q_main_ghost', stage: 's_drop' }, { id: 'q_side', outcome: 'clean' }, { id: 'q_x', state: 'active' }] },
    { all: [{ level: 5 }, { money: 99999 }, { flag: 'nope' }] }, { any: [{ level: 99 }, { flag: 'kaz_honest' }] }, { any: [{ level: 99 }, { money: 99999 }] }, { any: [] },
    { not: { level: 99 } }, { not: { level: 1 } }, { not: null },
    { level: 9, attribute: { cool: 6 }, money: 1000, item: { j_scrap: 3 }, perk: { ghost: 3 }, ability: 'overclock', cyberware: 'cw_subdermal', notFlag: 'kaz_honest', streetCred: 5 },
    { nonsense: 1 }, { attribute: { charisma: 4 } }, { all: [{ bogus: true }] },
  ];
  const cases = fixed.map((cond) => ({ cond, ...attempt(() => checkCondition(cond, view)), describe: (() => { try { return describeCondition(cond); } catch (e) { return { throws: e.name }; } })() }));
  out({ suite: 'conditions', view: viewData, cases });
}

// ---------------------------------------------------------------- saves

{
  const cases = [
    { version: 1, money: 777, stats: { xp: 10 }, flags: { a: true } },
    { version: 1 },
    { version: 1, economy: { money: 5, vendors: [] }, stats: { credXp: 40 }, hacking: { ram: 3, active: [], buffs: [] } },
    { version: 2, economy: { money: 1, vendors: [] } },
    { version: 3 }, { version: 0 }, { version: 1.5 }, { version: 'x' }, {},
  ].map((input) => ({ input: plain(input), ...attempt(() => migrate(plain(input))) }));

  const storage = new MemoryStorage();
  let now = 1700000000000;
  const realNow = Date.now;
  Date.now = () => now;
  const mgr = new SaveManager(storage);
  const steps = [];
  steps.push({ op: 'write', slot: 'auto', payload: { stats: { xp: 5 }, summary: { level: 3 } }, result: mgr.write('auto', { stats: { xp: 5 }, summary: { level: 3 } }) });
  now += 5000;
  steps.push({ op: 'write', slot: 'one', payload: { version: 1, money: 12 }, result: mgr.write('one', { version: 1, money: 12 }) });
  now += 5000;
  steps.push({ op: 'write', slot: 'two', payload: { flags: {} }, result: mgr.write('two', { flags: {} }) });
  storage.setItem('nightsector:save:bad', '{oops');
  storage.setItem('other:key', '{}');
  steps.push({ op: 'list', result: mgr.list() });
  steps.push({ op: 'read', slot: 'one', ...attempt(() => mgr.read('one')) });
  steps.push({ op: 'read', slot: 'missing', ...attempt(() => mgr.read('missing')) });
  steps.push({ op: 'read', slot: 'bad', ...attempt(() => mgr.read('bad')) });
  steps.push({ op: 'has', slot: 'two', result: mgr.has('two') });
  mgr.remove('two');
  steps.push({ op: 'remove', slot: 'two' });
  steps.push({ op: 'has', slot: 'two', result: mgr.has('two') });
  steps.push({ op: 'raw', slot: 'auto', result: storage.getItem('nightsector:save:auto') });
  Date.now = realNow;
  out({ suite: 'save', cases, steps });
}

const here = dirname(fileURLToPath(import.meta.url));
const dir = join(here, '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(dir, { recursive: true });
const file = join(dir, 'rpg_character_golden.jsonl');
writeFileSync(file, lines.join('\n') + '\n');
console.log(`wrote ${lines.length} suites to ${file}`);
