// Runs the web prototype's RPG core (src/rpg/Core.js and the quest, dialogue, hacking and location rules under it) headless under node and writes what it does
// as a golden fixture for the C# port (Assets/Tests/EditMode/Fixtures/rpg_world_golden.jsonl). Read-only against the prototype.
//
//   node tools/gen-golden-rpg-world.mjs <path-to-cyberpunkish>
//
// Suites: the places put on test cities and on the real seeded city; breach puzzles generated and played; the quickhack deck; and whole RpgCore sessions:
// playthroughs of the jobs (a driver walks to each objective, talks, chooses, breaches, reaches each ending of the main job) and seeded random sessions over the
// whole API. Each operation is written with its arguments, its result and every event it raised, so the C# test replays the arguments and compares them all. The
// static metadata in a stats payload (attributeMeta, derivedMeta) is written once and left out after.
import { pathToFileURL } from 'node:url';
import { mkdirSync, writeFileSync } from 'node:fs';
import { join, resolve, dirname } from 'node:path';
import { fileURLToPath } from 'node:url';

const protoRoot = resolve(process.argv[2] ?? '');
if (!process.argv[2]) {
  console.error('usage: node tools/gen-golden-rpg-world.mjs <path-to-cyberpunkish>');
  process.exit(1);
}
const load = (rel) => import(pathToFileURL(join(protoRoot, rel)).href);

const { makeRng } = await load('src/engine/Assets.js');
const { City } = await load('src/world/City.js');
const { buildLocations } = await load('src/rpg/Locations.js');
const { generateBreach, BreachSession, QuickhackDeck, BREACH_TARGETS } = await load('src/rpg/Hacking.js');
const { RpgCore } = await load('src/rpg/Core.js');
const { MemoryStorage } = await load('src/rpg/Save.js');
const { PERKS } = await load('src/rpg/Perks.js');
const { CYBERWARE } = await load('src/rpg/Cyberware.js');
const { ITEM_TEMPLATES, EQUIP_SLOTS } = await load('src/rpg/Items.js');
const { VENDORS } = await load('src/rpg/Economy.js');
const { QUESTS } = await load('src/rpg/Quests.js');
const { ATTRIBUTES } = await load('src/rpg/Stats.js');

const lines = [];
const out = (o) => lines.push(JSON.stringify(o));
const plain = (v) => (v === undefined ? null : JSON.parse(JSON.stringify(v, (k, x) => (typeof x === 'number' && !Number.isFinite(x) ? null : x))));

/** A JSON copy without the static stats metadata, which is compared once. */
function lean(v) {
  const p = plain(v);
  const strip = (x) => {
    if (Array.isArray(x)) { x.forEach(strip); return; }
    if (x && typeof x === 'object') {
      delete x.attributeMeta;
      delete x.derivedMeta;
      Object.values(x).forEach(strip);
    }
  };
  strip(p);
  return p;
}

const pickOf = (rng, arr) => arr[Math.floor(rng() * arr.length)];
const intIn = (rng, a, b) => a + Math.floor(rng() * (b - a + 1));

// ---------------------------------------------------------------- cities

/** The test city of tests/rpg.test.mjs. */
function fakeCity({ grid = 7, block = 46, road = 16, heightBias = 0 } = {}) {
  const cell = block + road;
  const half = (grid * cell) / 2;
  const streets = [];
  const buildings = [];
  const mid = grid >> 1;
  for (let gz = 0; gz < grid; gz++) {
    for (let gx = 0; gx < grid; gx++) {
      const cx = -half + gx * cell + cell / 2;
      const cz = -half + gz * cell + cell / 2;
      const isPlaza = gx === mid && gz === mid;
      streets.push({ gx, gz, cx, cz, isPlaza });
      if (!isPlaza) buildings.push({ x: cx, z: cz, w: 40, d: 40, h: 20 + gx * 3 + gz * 2 + heightBias });
    }
  }
  return { blockSize: block, roadWidth: road, cell, grid, extent: grid * cell, streets, buildings };
}
const cityJson = (c) => ({ blockSize: c.blockSize, cell: c.cell, grid: c.grid, streets: c.streets, buildings: c.buildings.map((b) => ({ x: b.x, z: b.z, w: b.w, d: b.d, h: b.h })) });

{
  const cases = [];
  for (const [i, city] of [fakeCity(), fakeCity({ heightBias: 5 }), fakeCity({ grid: 5 }), fakeCity({ grid: 9, block: 40 })].entries()) {
    for (const seed of [7717, 1, 20771 ^ 0x5f37, 99]) cases.push({ city: i, seed, locations: plain([...buildLocations(city, seed).values()]) });
  }
  out({ suite: 'locations', cities: [fakeCity(), fakeCity({ heightBias: 5 }), fakeCity({ grid: 5 }), fakeCity({ grid: 9, block: 40 })].map(cityJson), cases });

  // the real city: the C# side builds its own from the same seed and must put every place in the same spot
  const real = new City({ seed: 20771 });
  real._layout(makeRng(20771));
  const meta = { blockSize: 46, cell: 62, grid: 7, streets: real.streets, buildings: real.buildings };
  out({ suite: 'realCity', citySeed: 20771, seed: 20771 ^ 0x5f37, locations: plain([...buildLocations(meta, 20771 ^ 0x5f37).values()]) });
}

// ---------------------------------------------------------------- breach and the deck

{
  const rng = makeRng(808);
  const cases = [];
  for (let i = 0; i < 60; i++) {
    const opts = {
      seed: intIn(rng, 0, 2 ** 31), size: intIn(rng, 3, 8), bufferSize: intIn(rng, 2, 14) + (rng() < 0.2 ? 0.5 : 0), timeLimit: intIn(rng, 4, 30),
      tier: intIn(rng, 0, 3), targetId: 'dev' + i, targetName: 'Device ' + i,
    };
    if (rng() < 0.2) opts.daemonCount = intIn(rng, 1, 6);
    const state = generateBreach(opts);
    const initial = plain(state);
    const daemonEvents = [];
    const endEvents = [];
    const session = new BreachSession(state, { onDaemon: (d) => daemonEvents.push(d.id), onEnd: (r) => endEvents.push(plain(r)) });
    const ops = [];
    const followSolution = rng() < 0.5;
    let step = 0;
    for (let k = 0; k < 20 && !session.done; k++) {
      const r = rng();
      if (r < 0.75) {
        let cell;
        if (followSolution && step < state.solutionPath.length) cell = state.solutionPath[step];
        else if (rng() < 0.8) { const legal = session.legalCells(); cell = legal.length ? pickOf(rng, legal) : { r: 0, c: 0 }; }
        else cell = { r: intIn(rng, -1, opts.size), c: intIn(rng, -1, opts.size) };
        const res = session.select(cell.r, cell.c);
        if (res.ok) step++;
        ops.push({ op: 'select', r: cell.r, c: cell.c, result: plain(res), legal: plain(session.legalCells()) });
      } else if (r < 0.9) {
        const dt = Math.round(rng() * 80) / 10;
        session.tick(dt);
        ops.push({ op: 'tick', dt, timeLeft: state.timeLeft, state: state.state });
      } else {
        ops.push({ op: 'finish', result: plain(session.finish('uploaded')) });
      }
    }
    cases.push({ opts, initial, ops, final: plain(state), daemonEvents, endEvents });
  }
  out({ suite: 'breach', cases });

  const deck = new QuickhackDeck();
  const drng = makeRng(909);
  const hacks = ITEM_TEMPLATES.filter((t) => t.type === 'quickhack').map((t) => ({ templateId: t.id, name: t.baseNames[0], hack: t.hack }));
  const ops = [];
  deck.setMaxRam(8);
  deck.fill();
  for (let i = 0; i < 120; i++) {
    const r = drng();
    if (r < 0.4) {
      const item = drng() < 0.05 ? { templateId: 'junk', name: 'x' } : pickOf(drng, hacks);
      const target = pickOf(drng, ['t1', 't2', 't3']);
      const durationScale = Math.round((0.5 + drng()) * 100) / 100;
      const discount = drng() < 0.3 ? 1 : 0;
      ops.push({ op: 'upload', item, target, durationScale, discount, result: plain(deck.upload(item, target, { durationScale, discount })) });
    } else if (r < 0.85) {
      const dt = Math.round(drng() * 40) / 10;
      const regen = Math.round(drng() * 100) / 100;
      ops.push({ op: 'tick', dt, regen, result: plain(deck.tick(dt, regen)) });
    } else {
      const max = intIn(drng, 1, 14);
      ops.push({ op: 'setMaxRam', max, result: deck.setMaxRam(max) });
    }
    ops[ops.length - 1].state = plain(deck.serialize());
  }
  out({ suite: 'deck', ops });
}

// ---------------------------------------------------------------- the core

const NOW = 1700000000000;
Date.now = () => NOW;

function session(label, { city = fakeCity(), seed = 20771 } = {}) {
  const events = [];
  const core = new RpgCore({ emit: (type, payload) => events.push({ type, payload: lean(payload) }), storage: new MemoryStorage(), seed });
  core.autosaveEnabled = false;
  const ops = [];
  const construction = events.splice(0);
  const record = (op, run) => {
    let entry;
    try {
      const result = run();
      entry = { ...op, result: lean(result) };
    } catch (err) {
      entry = { ...op, throws: err.name };
    }
    entry.events = events.splice(0);
    ops.push(entry);
    return entry;
  };
  const snapshot = () => ops.push({ op: 'snapshot', result: lean(core.getSnapshot()), serialized: lean(core.serialize()) });
  record({ op: 'setCity' }, () => core.setCity(city));
  return { core, ops, record, snapshot, construction, label, seed, city: cityJson(city) };
}

/** Move the player onto a place and let a tick notice. */
function goTo(s, key) {
  const loc = s.core.locations.get(key);
  s.record({ op: 'setPlayerPosition', x: loc.x, y: loc.y, z: loc.z }, () => s.core.setPlayerPosition(loc.x, loc.y, loc.z));
  s.record({ op: 'tick', dt: 1 / 60, gameHours: 0 }, () => s.core.tick(1 / 60));
}

/** Talk through a conversation, preferring the given option ids and otherwise choosing at random among the unlocked ones. */
function talk(s, rng, prefer) {
  for (let i = 0; i < 30 && s.core.dialogue.active; i++) {
    const node = s.core.getDialogueNode();
    const open = node.options.filter((o) => !o.locked);
    if (!open.length) { s.record({ op: 'endDialogue' }, () => s.core.endDialogue()); return; }
    const preferred = open.find((o) => prefer.includes(o.id));
    const pick = preferred ?? pickOf(rng, open);
    s.record({ op: 'choose', optionId: pick.id }, () => s.core.chooseDialogueOption(pick.id));
  }
  if (s.core.dialogue.active) s.record({ op: 'endDialogue' }, () => s.core.endDialogue());
}

/** Play the breach that is open by walking its guaranteed path (all daemons), or part of it. */
function playBreach(s, rng, full = true) {
  const b = s.core.breach;
  if (!b || b.done) return;
  const path = b.s.solutionPath;
  const n = full ? path.length : intIn(rng, 0, path.length);
  for (let i = 0; i < n && !b.done; i++) s.record({ op: 'breachSelect', r: path[i].r, c: path[i].c }, () => s.core.breachSelect(path[i].r, path[i].c));
  if (!b.done) s.record({ op: 'breachUpload' }, () => { const r = s.core.breachUpload(); return 'ok' in r ? r : { ok: true, ...r }; });
}

function boost(s, attr, to) {
  while (s.core.stats.attributes[attr] < to) {
    s.record({ op: 'grantAttributePoints', n: 1 }, () => { s.core.stats.attributePoints += 1; });
    s.record({ op: 'spendAttributePoint', attr }, () => s.core.spendAttributePoint(attr));
  }
}

/** Drive the active quests by their open objectives until the given quest ends or the step budget runs out. */
function drive(s, rng, questId, prefer, steps = 80) {
  for (let i = 0; i < steps; i++) {
    const st = s.core.quests.getState(questId);
    if (st.state !== 'active') return;
    const snap = s.core.quests.snapshot(questId);
    const open = snap.objectives.filter((o) => !o.done && !o.optional);
    const o = open[0] ?? snap.objectives.find((x) => !x.done);
    if (!o) return;
    if (o.trigger === 'reach') goTo(s, o.locationKey);
    else if (o.trigger === 'talk') {
      const def = QUESTS.find((q) => q.id === questId).stages.find((x) => x.id === st.stage).objectives.find((x) => x.id === o.id);
      const graph = s.core.dialogue.graphs[def.dialogue];
      if (graph.location) goTo(s, graph.location);
      s.record({ op: 'startDialogue', graphId: def.dialogue }, () => s.core.startDialogue(def.dialogue));
      talk(s, rng, prefer);
    } else if (o.trigger === 'hack') {
      const def = QUESTS.find((q) => q.id === questId).stages.find((x) => x.id === st.stage).objectives.find((x) => x.id === o.id);
      const target = BREACH_TARGETS[def.target];
      goTo(s, target.locationKey);
      s.record({ op: 'startBreach', targetId: def.target }, () => s.core.startBreach(def.target));
      playBreach(s, rng, true);
    } else if (o.trigger === 'manual') {
      s.record({ op: 'completeObjective', questId, objectiveId: o.id }, () => s.core.completeObjective(questId, o.id));
    } else if (o.trigger === 'item') {
      const def = QUESTS.find((q) => q.id === questId).stages.find((x) => x.id === st.stage).objectives.find((x) => x.id === o.id);
      for (const [tpl, qty] of Object.entries(def.item)) s.record({ op: 'giveItem', templateId: tpl, qty }, () => s.core.giveItem(tpl, { qty }));
    } else if (o.trigger === 'flag') {
      const def = QUESTS.find((q) => q.id === questId).stages.find((x) => x.id === st.stage).objectives.find((x) => x.id === o.id);
      s.record({ op: 'setFlag', name: def.flag, value: true }, () => s.core.setFlag(def.flag, true));
    } else if (o.trigger === 'kill') {
      s.record({ op: 'notifyKill', tag: o.tag ?? 'x', n: 1 }, () => s.core.notifyKill(o.tag, 1));
    }
    if (i % 6 === 5) s.record({ op: 'tick', dt: 0.5, gameHours: 0.5 }, () => s.core.tick(0.5, { gameHours: 0.5 }));
  }
}

function finish(s) {
  s.snapshot();
  out({ suite: 'core', label: s.label, seed: s.seed, city: s.city, construction: s.construction, ops: s.ops });
}

// the starting kit, as the prototype's RPG system (src/rpg/Systems.js) hands it to a new character
const STARTING_KIT = [
  { templateId: 'w_pistol', rarity: 'uncommon', equip: true },
  { templateId: 'w_baton', rarity: 'common', equip: true },
  { templateId: 'a_jacket', rarity: 'common', equip: true },
  { templateId: 'a_boots', rarity: 'common', equip: true },
  { templateId: 'c_maxdoc', qty: 3 },
  { templateId: 'qh_ping', deck: 0 },
];
{
  const s = session('kit');
  s.record({ op: 'giveStartingKit' }, () => {
    for (const entry of STARTING_KIT) {
      const res = s.core.giveItem(entry.templateId, { rarity: entry.rarity ?? null, level: 1, qty: entry.qty ?? 1 });
      if (!res.ok || !res.item) continue;
      if (entry.equip) s.core.equipItem(res.item.uid);
      if (entry.deck !== undefined) s.core.equipHack(res.item.uid, entry.deck);
    }
    s.core.deck.fill();
  });
  finish(s);
}

// the main job to each of its endings, each with a different character
for (const [label, prefer, attrs] of [
  ['main-corpo', ['w_o_sell', 'k_o_accept', 'k_o_eat'], {}],
  ['main-burn', ['w_o_burn', 'k_o_read', 'k_o_read_ask', 'k_o_tech', 'k_o_haggle', 'k_o_haggle_yes', 'k_o_accept'], { intelligence: 8, technical: 6, cool: 8 }],
  ['main-keep', ['w_o_keep', 'w_o_scan', 'w_o_scan_choice', 'k_o_accept'], { intelligence: 10, body: 6 }],
]) {
  const s = session(label);
  const rng = makeRng(label.length * 977);
  for (const [attr, to] of Object.entries(attrs)) boost(s, attr, to);
  drive(s, rng, 'q_main_ghost', prefer, 120);
  s.snapshot();
  // then the side jobs as far as they go
  for (const [vendorGraph, prefer2] of [['dlg_vey', ['v_o_job', 'v_o_job_pay', 'v_o_pay_yes', 'v_o_accept_end']], ['dlg_marlene', ['m_o_work', 'm_o_work_pay', 'm_o_pay_yes', 'm_o_accept_end']]]) {
    const graph = s.core.dialogue.graphs[vendorGraph];
    goTo(s, graph.location);
    s.record({ op: 'interact' }, () => s.core.interact());
    talk(s, rng, prefer2);
  }
  for (const q of ['q_side_chrometax', 'q_gig_ledger', 'q_side_deadair', 'q_gig_cardiac']) {
    if (s.core.quests.getState(q).state === 'inactive') s.record({ op: 'startQuest', questId: q, force: true }, () => s.core.startQuest(q, { force: true }));
    drive(s, rng, q, ['v_o_return_hand', 'm_o_give', 'v_o_paid_end'], 60);
  }
  for (let t = 0; t < 30; t++) s.record({ op: 'tick', dt: 1 / 60, gameHours: 0 }, () => s.core.tick(1 / 60));
  s.record({ op: 'save', slot: 'auto' }, () => s.core.save('auto'));
  s.record({ op: 'load', slot: 'auto' }, () => s.core.load('auto'));
  finish(s);
}

// seeded random sessions over the whole API
for (const seed of [1, 2, 3, 4, 5, 6]) {
  const s = session('fuzz-' + seed, { seed: 1000 + seed, city: seed % 2 ? fakeCity() : fakeCity({ heightBias: 3 }) });
  const rng = makeRng(seed * 4099);
  const core = s.core;
  const anyItem = () => (core.inventory.items.length && rng() < 0.9 ? pickOf(rng, core.inventory.items).uid : 'nope');
  const locKeys = () => [...core.locations.keys()];
  for (let i = 0; i < 260; i++) {
    const r = rng();
    if (core.dialogue.active && rng() < 0.85) {
      const node = core.getDialogueNode();
      const opt = node.options.length ? pickOf(rng, node.options) : null;
      if (opt) s.record({ op: 'choose', optionId: opt.id }, () => core.chooseDialogueOption(opt.id));
      else s.record({ op: 'endDialogue' }, () => core.endDialogue());
      continue;
    }
    if (core.breach && !core.breach.done && rng() < 0.85) {
      const legal = core.breach.legalCells();
      if (legal.length && rng() < 0.9) { const c = pickOf(rng, legal); s.record({ op: 'breachSelect', r: c.r, c: c.c }, () => core.breachSelect(c.r, c.c)); }
      else {
        const which = rng() < 0.5 ? 'breachUpload' : 'cancelBreach';
        s.record({ op: which }, () => { const res = which === 'breachUpload' ? core.breachUpload() : core.cancelBreach(); return 'ok' in res ? res : { ok: true, ...res }; });
      }
      continue;
    }
    if (r < 0.08) { const key = pickOf(rng, locKeys()); goTo(s, key); }
    else if (r < 0.14) s.record({ op: 'interact' }, () => core.interact());
    else if (r < 0.2) { const dt = Math.round(rng() * 300) / 100 + 0.01; const gh = rng() < 0.3 ? Math.round(rng() * 100) / 10 : 0; s.record({ op: 'tick', dt, gameHours: gh }, () => core.tick(dt, { gameHours: gh })); }
    else if (r < 0.24) { const n = Math.round(rng() * 3000); s.record({ op: 'addXp', amount: n, source: 'fuzz' }, () => core.addXp(n, 'fuzz')); }
    else if (r < 0.26) { const n = Math.round(rng() * 300); s.record({ op: 'addCred', amount: n }, () => core.addCred(n)); }
    else if (r < 0.3) { const a = pickOf(rng, ATTRIBUTES); s.record({ op: 'spendAttributePoint', attr: a }, () => core.spendAttributePoint(a)); }
    else if (r < 0.34) { const p = pickOf(rng, PERKS).id; const which = rng() < 0.8 ? 'buyPerk' : 'canBuyPerk'; s.record({ op: which, id: p }, () => (which === 'buyPerk' ? core.buyPerk(p) : core.canBuyPerk(p))); }
    else if (r < 0.37) { const c = pickOf(rng, CYBERWARE).id; s.record({ op: 'installCyberware', id: c }, () => core.installCyberware(c)); }
    else if (r < 0.39) { const c = core.cyber.installed.length ? pickOf(rng, core.cyber.installed) : pickOf(rng, CYBERWARE).id; s.record({ op: 'removeCyberware', id: c }, () => core.removeCyberware(c)); }
    else if (r < 0.45) { const t = pickOf(rng, ITEM_TEMPLATES); const opts = { rarity: rng() < 0.3 ? pickOf(rng, ['rare', 'epic', 'uncommon']) : null, level: rng() < 0.3 ? intIn(rng, 1, 20) : null, qty: intIn(rng, 1, 4) }; s.record({ op: 'giveItem', templateId: t.id, ...opts }, () => core.giveItem(t.id, opts)); }
    else if (r < 0.47) { const c = pickOf(rng, CYBERWARE).id; s.record({ op: 'giveCyberware', id: c }, () => core.giveCyberware(c)); }
    else if (r < 0.52) { const u = anyItem(); s.record({ op: 'equipItem', uid: u }, () => core.equipItem(u)); }
    else if (r < 0.54) { const sl = pickOf(rng, EQUIP_SLOTS); s.record({ op: 'unequipSlot', slot: sl }, () => core.unequipSlot(sl)); }
    else if (r < 0.57) { const hacks = core.inventory.items.filter((x) => x.type === 'quickhack'); const u = hacks.length ? pickOf(rng, hacks).uid : anyItem(); const idx = intIn(rng, 0, 4); s.record({ op: 'equipHack', uid: u, index: idx }, () => core.equipHack(u, idx)); }
    else if (r < 0.6) { const u = anyItem(); s.record({ op: 'useItem', uid: u }, () => core.useItem(u)); }
    else if (r < 0.62) { const u = anyItem(); const q = intIn(rng, 1, 3); s.record({ op: 'removeItem', uid: u, qty: q }, () => core.removeItem(u, q)); }
    else if (r < 0.65) { const opts = { tier: intIn(rng, 0, 2), count: rng() < 0.5 ? intIn(rng, 0, 3) : null, source: 'fuzz', autoPickup: rng() < 0.8 }; s.record({ op: 'dropLoot', ...opts }, () => core.dropLoot(opts)); }
    else if (r < 0.69) { const f = pickOf(rng, ['kaz_honest', 'vey_trusts', 'marlene_enemy', 'marta_owes', 'ghost_sold', 'fuzz_flag', 'wire_met']); const v = rng() < 0.7 ? true : rng() < 0.5 ? false : 'x'; s.record({ op: 'setFlag', name: f, value: v }, () => core.setFlag(f, v)); }
    else if (r < 0.72) { const v = pickOf(rng, VENDORS).id; s.record({ op: 'openVendor', id: v }, () => core.openVendor(v)); }
    else if (r < 0.75) { const v = pickOf(rng, VENDORS).id; const st = core.economy.state(v).stock; const u = st.length ? pickOf(rng, st).uid : 'nope'; s.record({ op: 'buyFrom', vendorId: v, uid: u }, () => core.buyFrom(v, u)); }
    else if (r < 0.77) { const v = pickOf(rng, VENDORS).id; const u = anyItem(); s.record({ op: 'sellTo', vendorId: v, uid: u, qty: 1 }, () => core.sellTo(v, u, 1)); }
    else if (r < 0.79) s.record({ op: 'closeVendor' }, () => core.closeVendor());
    else if (r < 0.82) { const t = pickOf(rng, Object.keys(BREACH_TARGETS)); s.record({ op: 'startBreach', targetId: t }, () => core.startBreach(t)); }
    else if (r < 0.85) { const idx = intIn(rng, 0, 4); const t = pickOf(rng, ['npc_a', 'npc_b', 'cam']); s.record({ op: 'uploadQuickhack', index: idx, targetId: t }, () => core.uploadQuickhack(idx, t)); }
    else if (r < 0.87) { const n = Math.round(rng() * 900) / 10; s.record({ op: 'damage', amount: n, source: 'fuzz' }, () => core.damage(n, 'fuzz')); }
    else if (r < 0.88) { const n = Math.round(rng() * 900) / 10; s.record({ op: 'heal', amount: n }, () => core.heal(n)); }
    else if (r < 0.89) { const n = Math.round(rng() * 900) / 10; s.record({ op: 'spendStamina', amount: n }, () => core.spendStamina(n)); }
    else if (r < 0.9) s.record({ op: 'respecPerks' }, () => core.respecPerks());
    else if (r < 0.92) { const g = pickOf(rng, Object.keys(core.dialogue.graphs)); s.record({ op: 'startDialogue', graphId: g }, () => core.startDialogue(g)); }
    else if (r < 0.93) { const q = pickOf(rng, QUESTS).id; const force = rng() < 0.5; s.record({ op: 'startQuest', questId: q, force }, () => core.startQuest(q, { force })); }
    else if (r < 0.94) { const n = intIn(rng, 1, 4); s.record({ op: 'grantAttributePoints', n }, () => { core.stats.attributePoints += n; }); }
    else if (r < 0.95) { const n = Math.round(rng() * 5000) - 1000; s.record({ op: 'addMoney', amount: n, reason: 'fuzz' }, () => core.addMoney(n, 'fuzz')); }
    else if (r < 0.96) { const sl = pickOf(rng, ['a', 'b']); s.record({ op: 'save', slot: sl }, () => core.save(sl)); }
    else if (r < 0.97) { const sl = pickOf(rng, ['a', 'b', 'empty']); s.record({ op: 'load', slot: sl }, () => core.load(sl)); }
    else if (r < 0.98) { const q = pickOf(rng, QUESTS).id; s.record({ op: 'trackQuest', questId: q }, () => core.trackQuest(q)); }
    else s.snapshot();
  }
  finish(s);
}

const here = dirname(fileURLToPath(import.meta.url));
const dir = join(here, '..', 'Assets', 'Tests', 'EditMode', 'Fixtures');
mkdirSync(dir, { recursive: true });
const file = join(dir, 'rpg_world_golden.jsonl');
writeFileSync(file, lines.join('\n') + '\n');
console.log(`wrote ${lines.length} suites to ${file}`);
