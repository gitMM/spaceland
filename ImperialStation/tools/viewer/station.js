"use strict";
// Imperial Station timeline viewer. Replays a `station_timeline` export
// (Station.Runner timeline): a top-down deck slice, a time slider and a
// per-citizen dossier. The page only reads the export; it never runs the simulation.

const $ = id => document.getElementById(id);
const CSS = getComputedStyle(document.documentElement);
const color = name => CSS.getPropertyValue("--" + name).trim();
const COL = {
  travel: color("travel"), work: color("work"), life: color("life"), inst: color("inst"), idle: color("idle"),
  good: color("good"), warning: color("warning"), serious: color("serious"), critical: color("critical"),
  text: color("text-primary"), text2: color("text-secondary"), muted: color("text-muted"),
  surface0: color("surface-0"), surface1: color("surface-1"), surface2: color("surface-2"), surface3: color("surface-3"), line: color("line"), lineStrong: color("line-strong")
};
const FAMILY = { travel: "Travel", work: "Work & duty", life: "Daily life", idle: "Idle" };
// Activity → [colour family, glyph, label]. Glyphs are the secondary encoding for the
// three map colours, so the activity never depends on colour alone.
const ACT = {
  Move: ["travel", "", "Travelling"],
  Clean: ["work", "C", "Cleaning"], InspectTile: ["work", "C", "Inspecting a tile"],
  Install: ["work", "R", "Installing a part"], InspectMachine: ["work", "R", "Inspecting a machine"], FunctionalTest: ["work", "R", "Testing a machine"],
  ProduceRecipe: ["work", "P", "Producing"], IssueReplacement: ["work", "P", "Issuing a card"], PickUp: ["work", "P", "Picking up"], PutDown: ["work", "P", "Putting down"],
  SplitStack: ["work", "P", "Splitting a stack"], MergeStacks: ["work", "P", "Merging stacks"], StoreInStash: ["work", "P", "Stashing"], Construct: ["work", "P", "Constructing"], InspectConstruction: ["work", "P", "Surveying"],
  Login: ["work", "T", "Logging in"], ReadInbox: ["work", "T", "Reading the inbox"], PostMessage: ["work", "T", "Posting a message"], Logout: ["work", "T", "Logging out"],
  QueryRecords: ["work", "T", "Querying records"], RecoveryLogin: ["work", "T", "Recovery identity check"], PostLostCard: ["work", "T", "Reporting a lost card"], ReadRecoveryInbox: ["work", "T", "Reading recovery mail"],
  Equip: ["work", "G", "Equipping"], JoinGuardTeam: ["work", "G", "Joining the guard team"], BeginEscort: ["work", "G", "Starting an escort"], RegisterIntake: ["work", "G", "Registering intake"],
  ReleaseDetention: ["work", "G", "Releasing from detention"], InspectActor: ["work", "G", "Searching for someone"], Inspect: ["work", "G", "Inspecting"],
  SuspendWork: ["work", "", "Suspending work"], ResumeWork: ["work", "", "Resuming work"], Cancel: ["idle", "", "Cancelling"],
  Eat: ["life", "M", "Eating"], UseToilet: ["life", "W", "Washroom"], Rest: ["life", "B", "Resting"], Sleep: ["life", "Z", "Sleeping"], Converse: ["life", "S", "Talking"], AllocateBerth: ["life", "Z", "Claiming a berth"]
};
const actInfo = kind => ACT[kind] || ["work", "", kind];
const NEED_PLACE = { Eat: "Cafeteria", UseToilet: "Toilet", Rest: "RestSpot", Sleep: "Bed" };
const NEED_RELIEF = { Hunger: "Eat", Bladder: "UseToilet", SleepPressure: "Sleep", WorkStrain: "Rest", Social: "Converse" };
const TERMINAL_ACTS = new Set(["Login", "ReadInbox", "PostMessage", "Logout", "QueryRecords", "RecoveryLogin", "PostLostCard", "ReadRecoveryInbox"]);
const MACHINE_ACTS = new Set(["Install", "InspectMachine", "FunctionalTest"]);
const STATION_ACTS = new Set(["ProduceRecipe", "IssueReplacement"]);
const DETENTION_ACTS = new Set(["RegisterIntake", "ReleaseDetention"]);
const KIND_ORDER = ["Terminal", "Machine", "Workstation", "Detention", "Cafeteria", "Toilet", "RestSpot", "Bed"];
const PLAN_NAMES = {
  "periodic-check-in": "Periodic check-in at a terminal", "daily-routine": "Daily routine goal", "report-due": "Report work to the authority",
  "clean-known-tile": "Clean a known dirty tile", "conversation": "Talk to someone", "seek-toilet": "Find a washroom", "off-duty": "Go to the off-duty rest place",
  "urgent-need": "Relieve an urgent need", "personal-need": "Relieve a personal need", "repair-known-equipment": "Repair known broken equipment",
  "verify-known-machine": "Verify a repair (QA)", "audit-records": "Audit records for a discrepancy", "guard-preparation": "Prepare for an escort",
  "guard-join": "Join the guard team", "guard-search": "Search for the escort subject", "begin-escort": "Begin the escort", "register-intake": "Register detention intake",
  "restore-credential": "Restore a lost credential", "patrol-search": "Patrol search"
};
const DRIVERS = new Set(["NeedThresholdReached", "DailyGoalDue", "PersonalNeedDue", "CheckInDue", "MessageRead", "DutyBoundary", "GuardOrderRead", "CaseDecisionRead",
  "DetentionDecisionRead", "CardLost", "CredentialRecoveryNeeded", "FacilityReportQueued", "ConversationConsented"]);
// Event filter categories shared by the strip and the dossier.
const CATS = {
  plan: { label: "Decisions", color: COL.travel }, life: { label: "Daily life", color: COL.life }, work: { label: "Work", color: COL.work },
  msg: { label: "Messages", color: COL.work }, inst: { label: "Institutions", color: COL.inst }, fail: { label: "Failures", color: COL.critical }, other: { label: "Other", color: COL.idle }
};
function eventCategory(type) {
  if (/^(ActionFailed|PlanUnavailable|WorkReportRejected)$|Failure|Stale|Interrupted|Overdue/.test(type)) return "fail";
  if (/^(Plan|RoutineActivity|DutyBoundary|DailyGoal|PersonalNeedDue|NeedThreshold|CheckInDue|NeedModeChanged)/.test(type)) return "plan";
  if (/^(Quality|Verification|Discrepancy|Case|Reprimand|Detention|Detained|Escort|Guard|Custody|Patrol|Incident|MissingWork|Authority|Belief|Assignment|Acknowledgment|ReportExpectation|WorkAssigned|WorkOrder|Admission|Equipment|Order)/.test(type)) return "inst";
  if (/^(Need|Meal|Conversation|Berth|Sleep)/.test(type)) return "life";
  if (/^(Cleaning|Cleanup|Work|Item|Machine|Repair|Fault|Dirt|Stack|Resource|Facility|Construction|Recipe)/.test(type)) return "work";
  if (/^(Terminal|Message|Mailbox|Records|Report|Credential|Recovery|Card|Roster|Session|Login)/.test(type)) return "msg";
  return "other";
}

// ---------- utilities ----------
const pad2 = n => String(n).padStart(2, "0");
function upper(arr, v) { let lo = 0, hi = arr.length; while (lo < hi) { const m = (lo + hi) >> 1; if (arr[m] <= v) lo = m + 1; else hi = m; } return lo; }
function el(tag, cls, text) { const e = document.createElement(tag); if (cls) e.className = cls; if (text != null) e.textContent = text; return e; }
function words(type) { const s = type.replace(/([a-z])([A-Z])/g, "$1 $2").toLowerCase(); return s.charAt(0).toUpperCase() + s.slice(1); }
function hash(n) { n = Math.imul(n ^ (n >>> 16), 0x45d9f3b); n = Math.imul(n ^ (n >>> 16), 0x45d9f3b); return (n ^ (n >>> 16)) >>> 0; }
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));

// ---------- model ----------
let T = null, S = [], FT = [], eventTicks = [], needDefs = [], cits = [], citById = new Map(), rooms = [], roomByKey = new Map(), roomById = new Map(), places = [], placeByKey = new Map(), events = [];
let tick = 0, playing = false, selected = null, hovered = null, deck = 0, decks = [];
let view = { scale: 1, ox: 0, oy: 0 }, mapSize = { w: 0, h: 0 }, layoutDeck = null, lastRenderTick = null, lastClock = 0, dirty = true;
const eventFilter = new Set(["plan", "life", "work", "msg", "inst", "fail", "other"]);

function start(data, source) {
  if (!data || data.kind !== "station_timeline" || data.schemaVersion !== 1) throw Error("Not a station timeline (schema 1). Export one with Station.Runner timeline.");
  T = data; S = data.strings; FT = data.frames.ticks; needDefs = data.needs;
  rooms = data.rooms.map(r => ({ ...r, key: r.position.join(","), people: 0 }));
  roomByKey = new Map(rooms.map(r => [r.key, r])); roomById = new Map(rooms.map(r => [r.id, r]));
  decks = [...new Set(rooms.map(r => r.position[1]))].sort((a, b) => a - b);
  const dirtByTile = new Map(data.frames.dirt.map(d => [d.tileId, d.units]));
  rooms.forEach(r => { r.dirt = dirtByTile.get(r.id) || null; });
  places = data.places.map((p, i) => ({ ...p, index: i, room: roomByKey.get(p.position.join(",")), state: data.frames.places[i] }));
  placeByKey = new Map(places.map(p => [placeKey(p.kind, p.id), p]));
  events = data.events.map(r => ({ id: r[0], tick: r[1], type: S[r[2]], actor: r[3] || 0, detail: S[r[4] || 0], tile: r[5] || 0, cause: r[6] || 0, message: r[7] || 0, terminal: r[8] || 0, attempt: r[9] || 0, task: r[10] || 0, item: r[11] || 0, qty: r[12] || 0 }));
  events.forEach(e => { e.cat = eventCategory(e.type); }); eventTicks = events.map(e => e.tick);
  cits = data.citizens.map((c, i) => {
    const f = data.frames.citizens[i];
    const moves = c.moves.map(m => ({ dep: m[0], arr: m[1], from: [m[2] || 0, m[6] || 0, m[3] || 0], to: [m[4] || 0, m[7] || 0, m[5] || 0] }));
    const acts = c.acts.map(a => ({ req: a[0], began: a[1] ?? -1, end: a[2] ?? -1, kind: S[a[3] || 0], outcome: a[4] || 0, reqEvent: a[5] || 0, cause: a[6] || 0,
      origin: S[a[7] || 0], attempt: a[8] || 0, failure: S[a[9] || 0], queue: a[10] || 0, terminal: a[11] || 0, service: a[12] || 0, partner: a[13] || 0 }));
    return { ...c, idx: i, f, moves, moveDeps: moves.map(m => m.dep), acts, actReqs: acts.map(a => a.req), events: [], custody: [], disp: null, hue: hash(c.id) };
  });
  citById = new Map(cits.map(c => [c.id, c]));
  const byAccount = new Map(cits.map(c => [c.account, c]));
  for (const e of events) {
    const own = citById.get(e.actor); if (own) own.events.push(e);
    if (!e.actor) { const m = /account=(\d+)/.exec(e.detail); const c = m && byAccount.get(+m[1]); if (c) c.events.push(e); }
    const p = /partner=(\d+)/.exec(e.detail); const partner = p && citById.get(+p[1]); if (partner && partner !== own) partner.events.push(e);
  }
  cits.forEach(c => { c.events.sort((a, b) => a.tick - b.tick || a.id - b.id); c.eventTicks = c.events.map(e => e.tick); });
  for (const k of data.custody || []) {
    const end = k.releaseTick >= 0 ? k.releaseTick : Infinity, intake = k.intakeTick >= 0 ? k.intakeTick : end, subject = citById.get(k.actorId);
    if (k.escortStarted >= 0) {
      subject?.custody.push({ from: k.escortStarted, to: intake, label: "Under escort", kind: "escorted", auth: k.authorizationId });
      for (const g of k.escortMembers) if (g !== k.actorId) citById.get(g)?.custody.push({ from: k.escortStarted, to: intake, label: "Escorting " + (subject?.name || k.actorId), kind: "guard", auth: k.authorizationId });
    }
    if (k.intakeTick >= 0) subject?.custody.push({ from: k.intakeTick, to: end, label: "Detained", kind: "detained", auth: k.authorizationId });
  }
  deck = decks[0] ?? 0;
  $("deck").replaceChildren(...decks.map(y => new Option("Y " + y, String(y)))); $("deck").value = String(deck); $("deck").disabled = decks.length < 2;
  $("scrub").min = String(data.startTick); $("scrub").max = String(data.horizon); $("tickInput").max = String(data.horizon);
  $("run").textContent = `${data.scenario} · seed ${data.seed} · ${cits.length} citizens · ${rooms.length} rooms · ${(data.horizon / (data.dayTicks || data.horizon)).toFixed(0)} days · digest ${data.finalDigest.slice(0, 12)}… · ${source}`;
  $("empty").hidden = true;
  selected = null; hovered = null; layoutDeck = null; stripCache = null;
  buildPeople(); buildLegend(); fit(); setTick(data.startTick, true); renderDossier(true);
}
const placeKey = (kind, id) => (NEED_PLACE_KINDS.has(kind) ? "svc" : kind) + ":" + id;
const NEED_PLACE_KINDS = new Set(["Cafeteria", "Toilet", "RestSpot", "Bed"]);

// ---------- time ----------
function clockText(t) {
  if (T.dayTicks > 0) {
    if (t > 0 && t >= T.horizon && t % T.dayTicks === 0) return `Day ${t / T.dayTicks} · 24:00`;
    const day = Math.floor(t / T.dayTicks) + 1, m = (t % T.dayTicks) / T.dayTicks * 1440; return `Day ${day} · ${pad2(Math.floor(m / 60))}:${pad2(Math.floor(m % 60))}`;
  }
  return simText(t);
}
function simText(t) { const s = Math.floor(t * T.tickMs / 1000); return `${Math.floor(s / 3600)}:${pad2(Math.floor(s / 60) % 60)}:${pad2(s % 60)}`; }
function durationText(ticks) {
  if (ticks < 0 || !isFinite(ticks)) return "—";
  const minutes = T.dayTicks > 0 ? ticks / T.dayTicks * 1440 : ticks * T.tickMs / 60000;
  if (minutes < 1) return "<1 min"; if (minutes < 90) return Math.round(minutes) + " min";
  const h = Math.floor(minutes / 60), m = Math.round(minutes % 60); return h + " h" + (m ? " " + m + " min" : "");
}
function agoText(t0) { return t0 > tick ? "in " + durationText(t0 - tick) : durationText(tick - t0) + " ago"; }
function setTick(value, snap) {
  if (!T) return;
  const t = clamp(value, T.startTick, T.horizon);
  if (snap || lastRenderTick == null || Math.abs(t - tick) > 900) cits.forEach(c => { c.snap = true; });
  tick = t; dirty = true;
}
function frameAt(t) { return Math.max(0, upper(FT, t) - 1); }

// ---------- per-citizen state at a tick ----------
function locate(c, t) {
  const i = upper(c.moveDeps, t) - 1, m = c.moves;
  if (i >= 0 && t < m[i].arr) return { moving: true, move: m[i], p: (t - m[i].dep) / Math.max(1, m[i].arr - m[i].dep), room: roomByKey.get(m[i].from.join(",")) };
  const pos = i >= 0 ? m[i].to : c.position; return { moving: false, room: roomByKey.get(pos.join(",")) };
}
function actAt(c, t) {
  const i = upper(c.actReqs, t) - 1;
  for (let k = i; k >= 0 && k > i - 8; k--) { const a = c.acts[k]; if (a.end < 0 || a.end > t) return a; }
  return null;
}
const waiting = (a, t) => a.queue > 0 && (a.began < 0 || t < a.began);
function custodyAt(c, t) { for (const k of c.custody) if (t >= k.from && t < k.to) return k; return null; }
function needsAt(c, t) {
  const i = frameAt(t), j = Math.min(FT.length - 1, i + 1), K = needDefs.length, span = FT[j] - FT[i], w = span > 0 ? clamp((t - FT[i]) / span, 0, 1) : 0;
  return needDefs.map((d, k) => {
    const a = c.f.needs[i * K + k], b = c.f.needs[j * K + k];
    if (b < a) { // Relief is a step at the end of the relieving activity, not a ramp.
      const relief = NEED_RELIEF[d.kind], hit = relief && c.acts.find(x => x.kind === relief && x.outcome === 1 && x.end > FT[i] && x.end <= FT[j]);
      if (hit) return t < hit.end ? a : b;
    }
    return a + (b - a) * w;
  });
}
function band(def, v) { return def.critical > 0 && v >= def.critical ? 3 : def.urgent > 0 && v >= def.urgent ? 2 : def.seek > 0 && v >= def.seek ? 1 : 0; }
const BAND = [null, { label: "Seek", color: COL.warning }, { label: "Urgent", color: COL.serious }, { label: "Critical", color: COL.critical }];
function frameString(c, field, t) { return S[c.f[field][frameAt(t)] || 0]; }
function intentAt(c, t) {
  for (let i = upper(c.eventTicks, t) - 1; i >= 0; i--) { const e = c.events[i]; if (e.actor === c.id && (e.type === "PlanSelected" || e.type === "PlanDeferred")) return e; }
  return null;
}
function driverBefore(c, t, limit) {
  for (let i = upper(c.eventTicks, t) - 1; i >= 0 && c.events[i].tick >= limit; i--) { const e = c.events[i]; if (e.actor === c.id && DRIVERS.has(e.type)) return e; }
  return null;
}
function planName(detail) { const key = detail.split(";")[0].replace(/\/v\d+$/, ""); return PLAN_NAMES[key] || words(key.replace(/-/g, " ")); }
function machineFor(a, c, room) {
  if (a._machine === undefined) {
    a._machine = null;
    const from = upper(c.eventTicks, a.req - 1), to = a.end >= 0 ? a.end : Infinity;
    for (let i = from; i < c.events.length && c.events[i].tick <= to; i++) { const m = /Machine:(\d+)/.exec(c.events[i].detail); if (m) { a._machine = placeByKey.get("Machine:" + m[1]) || null; break; } }
  }
  return a._machine && a._machine.room === room ? a._machine : places.find(p => p.room === room && p.kind === "Machine") || null;
}
function placeFor(c, a, room) {
  const k = a.kind; let p = null;
  if (NEED_PLACE[k]) p = (a.service && placeByKey.get("svc:" + a.service)) || places.find(x => x.room === room && x.kind === NEED_PLACE[k] && (!x.assignedActorId || x.assignedActorId === c.id));
  else if (TERMINAL_ACTS.has(k)) p = (a.terminal && placeByKey.get("Terminal:" + a.terminal)) || places.find(x => x.room === room && x.kind === "Terminal");
  else if (MACHINE_ACTS.has(k)) p = machineFor(a, c, room);
  else if (STATION_ACTS.has(k)) p = places.find(x => x.room === room && x.kind === "Workstation");
  else if (DETENTION_ACTS.has(k)) p = places.find(x => x.room === room && x.kind === "Detention");
  return p && p.room === room ? p : null;
}
function describe(c, t) {
  const loc = locate(c, t), a = actAt(c, t), k = custodyAt(c, t);
  let family = "idle", glyph = "", label = "Idle", isWaiting = false;
  if (loc.moving) { family = "travel"; label = "Travelling"; }
  else if (a && a.kind !== "Move") { [family, glyph, label] = actInfo(a.kind); isWaiting = waiting(a, t); }
  return { loc, act: a, custody: k, family, glyph, label: isWaiting ? "Waiting · " + label.toLowerCase() : label, waiting: isWaiting };
}

// ---------- map layout ----------
function layoutMap() {
  const deckRooms = rooms.filter(r => r.position[1] === deck);
  if (!deckRooms.length) return;
  const xs = deckRooms.map(r => r.position[0]), zs = deckRooms.map(r => r.position[2]);
  const minX = Math.min(...xs), minZ = Math.min(...zs), cols = Math.max(...xs) - minX + 1, rowsN = Math.max(...zs) - minZ + 1;
  // Rooms take the canvas aspect so the slice fills the view; very wide or tall canvases stay readable.
  const W = 1000, gap = 26, margin = 20, aspect = mapSize.w > 0 ? mapSize.h / mapSize.w : 0.5;
  const cellW = (W - margin * 2 - gap * (cols - 1)) / cols;
  const cellH = clamp((W * aspect - margin * 2 - gap * (rowsN - 1)) / rowsN, cellW * 1.1, cellW * 3);
  const H = margin * 2 + rowsN * cellH + gap * (rowsN - 1);
  for (const r of deckRooms) layoutRoom(r, margin + (r.position[0] - minX) * (cellW + gap), margin + (r.position[2] - minZ) * (cellH + gap), cellW, cellH);
  layoutDeck = { deck, w: W, h: H, rooms: deckRooms, gap };
}
function layoutRoom(room, x, y, w, h) {
  const ps = places.filter(p => p.room === room);
  const beds = ps.filter(p => p.kind === "Bed" && !p.detentionFacilityId);
  const fixtures = ps.filter(p => !beds.includes(p)).sort((a, b) => KIND_ORDER.indexOf(a.kind) - KIND_ORDER.indexOf(b.kind) || a.detentionFacilityId - b.detentionFacilityId || a.id - b.id);
  let u = Math.min(w, h / 1.4) / 13;
  for (let attempt = 0; attempt < 8; attempt++, u *= 0.88) {
    const pad = u * 0.7, header = u * 2.2, inner = { x: x + pad, y: y + header, w: w - 2 * pad, h: h - header - pad };
    let cx = inner.x, cy = inner.y, rowH = 0;
    const slotW = u * 1.5;
    for (const p of fixtures) {
      const shown = Math.min(p.capacity, 16), perRow = Math.max(1, Math.floor(inner.w / slotW)), cols = Math.min(shown, perRow), rowsN = Math.ceil(shown / cols);
      const fw = Math.min(inner.w, Math.max(u * 2.6, cols * slotW)), fh = u * 1.5 + rowsN * u * 1.5;
      if (cx + fw > inner.x + inner.w + 0.5 && cx > inner.x) { cx = inner.x; cy += rowH + u * 0.55; rowH = 0; }
      p.rect = { x: cx, y: cy, w: fw, h: fh }; p.slotW = slotW;
      p.slots = Array.from({ length: shown }, (_, s) => ({ x: cx + slotW * (s % cols) + slotW / 2, y: cy + u * 1.5 + u * 1.5 * Math.floor(s / cols) + u * 0.75 }));
      cx += fw + u * 0.55; rowH = Math.max(rowH, fh);
    }
    const top = fixtures.length ? cy + rowH + u * 0.7 : inner.y;
    const bw = u * 1.75, bh = u * 1.2, bg = u * 0.35, bcols = Math.max(1, Math.floor((inner.w + bg) / (bw + bg))), brows = Math.ceil(beds.length / bcols);
    const bedsTop = beds.length ? inner.y + inner.h - brows * (bh + bg) + bg : inner.y + inner.h;
    const floorH = (beds.length ? bedsTop - u * 0.7 : inner.y + inner.h) - top;
    if (floorH < u * 3 && attempt < 7) continue;
    beds.forEach((p, i) => { const bx = inner.x + (i % bcols) * (bw + bg), by = bedsTop + Math.floor(i / bcols) * (bh + bg); p.rect = { x: bx, y: by, w: bw, h: bh }; p.slots = [{ x: bx + bw / 2, y: by + bh / 2 }]; });
    room.rect = { x, y, w, h }; room.u = u; room.bedsTop = bedsTop;
    room.floor = { x: inner.x, y: top, w: inner.w, h: Math.max(u * 1.5, floorH) };
    const step = u * 1.45, fc = Math.max(1, Math.floor(room.floor.w / step)), fr = Math.max(1, Math.floor(room.floor.h / step));
    room.floorSlots = [];
    for (let r = 0; r < fr; r++) for (let q = 0; q < fc; q++) room.floorSlots.push({ x: room.floor.x + step * (q + 0.5) + (room.floor.w - fc * step) / 2, y: room.floor.y + step * (r + 0.5) });
    return;
  }
}
function fit() {
  if (!T) return;
  const r = $("map").getBoundingClientRect(); mapSize = { w: r.width, h: r.height };
  layoutMap();
  if (!layoutDeck) return;
  const scale = Math.min(r.width / layoutDeck.w, r.height / layoutDeck.h);
  view = { scale, ox: (r.width - layoutDeck.w * scale) / 2, oy: (r.height - layoutDeck.h * scale) / 2 };
  cits.forEach(c => { c.snap = true; }); dirty = true;
}

// ---------- placing citizens ----------
function placeCitizens(t, dt) {
  const occupants = new Map(), queues = new Map(), floor = new Map();
  for (const r of rooms) r.people = 0;
  const states = new Map();
  for (const c of cits) {
    const d = describe(c, t); states.set(c, d); c.state = d; c.target = null;
    if (d.loc.moving) {
      const a = roomByKey.get(d.loc.move.from.join(",")), b = roomByKey.get(d.loc.move.to.join(","));
      const sameDeck = d.loc.move.from[1] === d.loc.move.to[1], onDeck = sameDeck ? d.loc.move.from[1] === deck : (d.loc.p < 0.5 ? d.loc.move.from[1] : d.loc.move.to[1]) === deck;
      if (!onDeck || !a?.rect || !b?.rect) continue;
      const lane = ((c.hue % 7) - 3) * a.u * 0.45, p = sameDeck ? d.loc.p : d.loc.p < 0.5 ? 0 : 1;
      const ax = a.rect.x + a.rect.w / 2, ay = a.floor.y + a.floor.h / 2 + lane, bx = b.rect.x + b.rect.w / 2, by = b.floor.y + b.floor.h / 2 + lane;
      c.target = { x: ax + (bx - ax) * p, y: ay + (by - ay) * p, r: a.u * 0.5 };
      continue;
    }
    const room = d.loc.room; if (!room || room.position[1] !== deck || !room.rect) continue;
    room.people++;
    const a = d.act; let p = a ? placeFor(c, a, room) : null;
    if (!p && d.custody?.kind === "detained") p = places.find(x => x.room === room && x.kind === "Detention") || null;
    if (p) {
      const list = d.waiting ? (queues.get(p) || queues.set(p, []).get(p)) : (occupants.get(p) || occupants.set(p, []).get(p));
      list.push({ c, since: d.waiting ? a.req : (a ? Math.max(a.began, a.req) : 0) });
    } else (floor.get(room) || floor.set(room, []).get(room)).push(c);
  }
  for (const [p, list] of occupants) {
    list.sort((x, y) => x.since - y.since || x.c.id - y.c.id);
    list.forEach((o, i) => {
      if (i < p.slots.length) o.c.target = { x: p.slots[i].x, y: p.slots[i].y, r: p.room.u * 0.5 };
      else (queues.get(p) || queues.set(p, []).get(p)).unshift({ c: o.c, since: o.since });
    });
  }
  for (const [p, list] of queues) {
    list.sort((x, y) => x.since - y.since || x.c.id - y.c.id);
    const u = p.room.u, f = p.room.floor;
    list.forEach((o, i) => {
      const per = Math.max(1, Math.floor(f.w / (u * 1.2))), x0 = clamp(p.rect.x + u * 0.75, f.x + u * 0.6, f.x + f.w - u * 0.6);
      const col = i % per, row = Math.floor(i / per);
      o.c.target = { x: Math.min(f.x + f.w - u * 0.6, x0 + col * u * 1.2), y: f.y + u * 0.75 + row * u * 1.2, r: u * 0.5, queue: p };
    });
  }
  for (const [room, list] of floor) {
    const slots = room.floorSlots, used = new Set(), u = room.u;
    // Talking pairs stand together: the partner with the lower id picks the spot.
    list.sort((x, y) => pairKey(x) - pairKey(y) || x.id - y.id);
    for (const c of list) {
      let target = null;
      const partner = c.state.act?.kind === "Converse" ? citById.get(c.state.act.partner) : null;
      if (partner && partner.target && partner.state?.loc.room === room && !partner.state.loc.moving) target = { x: partner.target.x + u * 1.05, y: partner.target.y, r: u * 0.5 };
      if (!target) {
        // Stable spot from the citizen id; probe for a free one.
        let s = (c.hue >>> 3) % slots.length;
        for (let n = 0; n < slots.length && used.has(s); n++) s = (s + 1) % slots.length;
        used.add(s); target = { x: slots[s].x, y: slots[s].y, r: u * 0.5 };
      }
      c.target = target;
    }
  }
  for (const c of cits) {
    if (!c.target) { c.disp = null; continue; }
    if (!c.disp || c.snap) c.disp = { x: c.target.x, y: c.target.y };
    else { const k = 1 - Math.exp(-dt * 14); c.disp.x += (c.target.x - c.disp.x) * k; c.disp.y += (c.target.y - c.disp.y) * k; }
    c.snap = false;
  }
}
const pairKey = c => { const a = c.state?.act; return a?.kind === "Converse" && a.partner ? Math.min(c.id, a.partner) : c.id; };

// ---------- map drawing ----------
const map = $("map"), mctx = map.getContext("2d");
function resizeCanvas(canvas) {
  const r = canvas.getBoundingClientRect(), dpr = window.devicePixelRatio || 1;
  const w = Math.max(1, Math.round(r.width * dpr)), h = Math.max(1, Math.round(r.height * dpr));
  if (canvas.width !== w || canvas.height !== h) { canvas.width = w; canvas.height = h; return true; }
  return false;
}
function roundRect(ctx, x, y, w, h, r) { ctx.beginPath(); ctx.roundRect(x, y, w, h, r); }
function drawMap() {
  resizeCanvas(map);
  const dpr = window.devicePixelRatio || 1;
  mctx.setTransform(dpr, 0, 0, dpr, 0, 0); mctx.clearRect(0, 0, map.width, map.height);
  if (!T || !layoutDeck) return;
  mctx.setTransform(dpr * view.scale, 0, 0, dpr * view.scale, dpr * view.ox, dpr * view.oy);
  const fi = frameAt(tick), showDirt = $("showDirt").checked;
  // Corridors between neighbouring rooms.
  for (const a of layoutDeck.rooms) for (const b of layoutDeck.rooms) {
    if (a.id >= b.id || Math.abs(a.position[0] - b.position[0]) + Math.abs(a.position[2] - b.position[2]) !== 1) continue;
    const u = a.u; mctx.fillStyle = COL.surface3;
    if (a.position[0] !== b.position[0]) { const l = Math.min(a.rect.x + a.rect.w, b.rect.x + b.rect.w), r = Math.max(a.rect.x, b.rect.x), y = a.floor.y + a.floor.h / 2; mctx.fillRect(Math.min(l, r) - 2, y - u * 2.2, Math.abs(r - l) + 4, u * 4.4); }
    else { const t = Math.min(a.rect.y + a.rect.h, b.rect.y + b.rect.h), bt = Math.max(a.rect.y, b.rect.y), x = a.rect.x + a.rect.w / 2; mctx.fillRect(x - u * 2.2, Math.min(t, bt) - 2, u * 4.4, Math.abs(bt - t) + 4); }
  }
  for (const room of layoutDeck.rooms) {
    const { x, y, w, h } = room.rect, u = room.u;
    roundRect(mctx, x, y, w, h, u * 0.6); mctx.fillStyle = COL.surface2; mctx.fill();
    const dirt = room.dirt ? room.dirt[fi] : 0;
    if (showDirt && dirt > 0) { mctx.fillStyle = `rgba(150, 105, 40, ${Math.min(0.3, 0.06 + dirt / 1600)})`; mctx.fill(); }
    mctx.strokeStyle = COL.lineStrong; mctx.lineWidth = 1 / view.scale; mctx.stroke();
    mctx.fillStyle = COL.text; mctx.font = `600 ${u * 0.95}px system-ui`; mctx.textAlign = "left"; mctx.textBaseline = "middle";
    mctx.fillText(`${room.definition}`, x + u * 0.7, y + u * 0.95, w - u * 5);
    mctx.fillStyle = COL.muted; mctx.font = `${u * 0.72}px ui-monospace, monospace`; mctx.textAlign = "right";
    mctx.fillText(`#${room.id} · ${room.people} in${showDirt && dirt ? ` · dirt ${dirt}` : ""}`, x + w - u * 0.7, y + u * 0.95, w * 0.55);
    for (const p of places.filter(q => q.room === room)) drawFixture(p, fi, u);
  }
  drawTrail();
  // Citizens: queued ones are hollow, rings show the most urgent need band, red frame = custody.
  const ids = $("showIds").checked;
  for (const c of cits) {
    if (!c.disp) continue;
    const st = c.state, r = c.target.r, fill = COL[st.family] || COL.idle, sel = selected === c, hov = hovered === c;
    const needs = needsAt(c, tick); let worst = 0; needs.forEach((v, k) => { worst = Math.max(worst, band(needDefs[k], v)); });
    mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r, 0, Math.PI * 2);
    if (st.waiting) { mctx.fillStyle = COL.surface1; mctx.fill(); mctx.strokeStyle = fill; mctx.lineWidth = r * 0.35; mctx.stroke(); }
    else { mctx.fillStyle = fill; mctx.fill(); mctx.strokeStyle = COL.surface0; mctx.lineWidth = Math.max(0.6, r * 0.12); mctx.stroke(); }
    if (worst > 0) { mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r * 1.38, 0, Math.PI * 2); mctx.strokeStyle = BAND[worst].color; mctx.lineWidth = r * (worst === 3 ? 0.42 : 0.26); mctx.stroke(); }
    if (st.custody) { const s = r * 1.75; mctx.strokeStyle = st.custody.kind === "guard" ? COL.inst : COL.critical; mctx.lineWidth = r * 0.25; mctx.strokeRect(c.disp.x - s, c.disp.y - s, s * 2, s * 2); }
    const label = ids ? String(c.id) : st.glyph;
    if (label && r * view.scale >= 5) { mctx.fillStyle = st.waiting ? COL.text : COL.surface0; mctx.font = `700 ${r * (label.length > 1 ? 0.95 : 1.15)}px ui-monospace, monospace`; mctx.textAlign = "center"; mctx.textBaseline = "middle"; mctx.fillText(label, c.disp.x, c.disp.y + r * 0.05); }
    if (sel || hov) {
      mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r * 2.1, 0, Math.PI * 2); mctx.strokeStyle = sel ? COL.text : COL.text2; mctx.lineWidth = Math.max(1.2 / view.scale, r * 0.16); mctx.stroke();
      if (sel) { mctx.fillStyle = COL.text; mctx.font = `600 ${r * 1.25}px system-ui`; mctx.textAlign = "center"; mctx.textBaseline = "bottom"; mctx.fillText(c.name, c.disp.x, c.disp.y - r * 2.3); }
    }
  }
}
function drawFixture(p, fi, u) {
  if (!p.rect) return;
  const { x, y, w, h } = p.rect, up = p.state.up[fi], busy = p.state.busy[fi], queued = p.state.queue[fi], det = p.detentionFacilityId > 0 || p.kind === "Detention";
  roundRect(mctx, x, y, w, h, u * 0.3);
  mctx.fillStyle = p.kind === "Bed" && !det ? COL.surface3 : "rgba(255,255,255,0.025)"; mctx.fill();
  mctx.strokeStyle = det ? "rgba(208, 59, 59, 0.55)" : COL.line; mctx.lineWidth = 1 / view.scale; mctx.stroke();
  if (p.kind === "Bed" && !det) {
    const own = citById.get(p.assignedActorId);
    if (own && $("showIds").checked) { mctx.fillStyle = COL.muted; mctx.font = `${u * 0.55}px ui-monospace, monospace`; mctx.textAlign = "center"; mctx.textBaseline = "middle"; mctx.fillText(String(own.id), x + w / 2, y + h / 2); }
    return;
  }
  mctx.fillStyle = COL.text2; mctx.font = `${u * 0.62}px system-ui`; mctx.textAlign = "left"; mctx.textBaseline = "middle";
  const name = { RestSpot: "Rest", Cafeteria: "Cafeteria", Toilet: "Washroom", Bed: "Cell bed", Detention: "Detention", Workstation: "Desk" }[p.kind] || p.kind;
  const label = `${det && p.kind !== "Detention" ? "Cell " : ""}${name}${p.kind === "Terminal" || p.kind === "Machine" ? " " + p.id : ""}`;
  let lx = x + u * 0.35;
  if (p.kind === "Terminal" || p.kind === "Machine" || p.kind === "Detention") {
    const status = up === 2 ? COL.good : up === 1 ? COL.warning : COL.critical;
    mctx.beginPath(); mctx.arc(x + u * 0.6, y + u * 0.7, u * 0.28, 0, Math.PI * 2); mctx.fillStyle = status; mctx.fill(); lx = x + u * 1.05;
    if (up !== 2) { mctx.fillStyle = status; mctx.font = `700 ${u * 0.55}px system-ui`; mctx.textAlign = "right"; mctx.fillText(p.kind === "Machine" ? "DOWN" : up === 1 ? "OFFLINE" : "UNPOWERED", x + w - u * 0.3, y + h - u * 0.3); mctx.textAlign = "left"; mctx.font = `${u * 0.62}px system-ui`; }
  }
  mctx.fillStyle = COL.text2; mctx.fillText(label, lx, y + u * 0.7, w - (lx - x) - u * 0.3);
  mctx.strokeStyle = COL.lineStrong; mctx.lineWidth = 1 / view.scale;
  for (const s of p.slots) { mctx.beginPath(); mctx.arc(s.x, s.y, u * 0.56, 0, Math.PI * 2); mctx.stroke(); }
  if (queued > 0 && p.kind !== "Detention") { mctx.fillStyle = COL.serious; mctx.font = `600 ${u * 0.55}px ui-monospace, monospace`; mctx.textAlign = "right"; mctx.fillText(`queue ${queued}`, x + w - u * 0.3, y + h - u * 0.3); }
  if (p.kind === "Detention" && busy > 0) { mctx.strokeStyle = COL.critical; mctx.lineWidth = u * 0.12; for (let i = 1; i < 5; i++) { const bx = x + w * i / 5; mctx.beginPath(); mctx.moveTo(bx, y + u * 1.3); mctx.lineTo(bx, y + h - u * 0.2); mctx.stroke(); } }
}
// Selected citizen's path: past room-to-room moves fade with age, upcoming moves dashed.
function drawTrail() {
  if (!selected || !$("showTrail").checked) return;
  const c = selected, windowTicks = T.dayTicks ? T.dayTicks / 6 : 6000, i = upper(c.moveDeps, tick);
  const center = pos => { const r = roomByKey.get(pos.join(",")); return r && r.rect && r.position[1] === deck ? { x: r.rect.x + r.rect.w / 2, y: r.floor.y + r.floor.h / 2, u: r.u } : null; };
  const arc = (m, alpha, dashed, below) => {
    const a = center(m.from), b = center(m.to); if (!a || !b) return;
    const bend = (below ? 1 : -1) * Math.max(a.u * 2.5, Math.hypot(b.x - a.x, b.y - a.y) * 0.18);
    mctx.beginPath(); mctx.moveTo(a.x, a.y); mctx.quadraticCurveTo((a.x + b.x) / 2, (a.y + b.y) / 2 + bend, b.x, b.y);
    mctx.setLineDash(dashed ? [a.u * 0.6, a.u * 0.5] : []); mctx.strokeStyle = `rgba(230, 237, 243, ${alpha})`; mctx.lineWidth = a.u * 0.22; mctx.stroke();
  };
  for (let k = i - 1; k >= 0 && c.moves[k].arr >= tick - windowTicks; k--) arc(c.moves[k], 0.15 + 0.6 * (1 - (tick - c.moves[k].arr) / windowTicks), false, false);
  for (let k = i, n = 0; k < c.moves.length && n < 12 && c.moves[k].dep <= tick + windowTicks / 2; k++, n++) arc(c.moves[k], 0.45, true, true);
  mctx.setLineDash([]);
}

// ---------- event strip ----------
const strip = $("strip"), sctx = strip.getContext("2d");
let stripCache = null, stripGeom = null;
const ROWS = [
  { key: "move", label: "Movement", color: COL.travel },
  { key: "work", label: "Work & messages", color: COL.work },
  { key: "life", label: "Daily life", color: COL.life },
  { key: "inst", label: "Institutions", color: COL.inst }
];
function stripLayout(w, h) {
  const left = 112, right = 14, plotW = Math.max(10, w - left - right);
  let y = 18; const rows = {};
  rows.dist = { y, h: 16 }; y += 20;
  for (const r of ROWS) { rows[r.key] = { y, h: 15 }; y += 18; }
  rows.fail = { y, h: 10 }; y += 15;
  rows.life_strip = { y, h: 16 }; y += 20;
  return { left, right, plotW, rows, w, h, x: t => left + (t - T.startTick) / Math.max(1, T.horizon - T.startTick) * plotW, t: px => T.startTick + clamp((px - left) / plotW, 0, 1) * (T.horizon - T.startTick) };
}
function buildStripCache() {
  const dpr = window.devicePixelRatio || 1, r = strip.getBoundingClientRect();
  const g = stripLayout(r.width, r.height); stripGeom = g;
  const off = document.createElement("canvas"); off.width = Math.round(r.width * dpr); off.height = Math.round(r.height * dpr);
  const c = off.getContext("2d"); c.setTransform(dpr, 0, 0, dpr, 0, 0);
  c.fillStyle = COL.surface2; c.fillRect(0, 0, r.width, r.height);
  // Days and shifts.
  c.font = "10px ui-monospace, monospace"; c.textBaseline = "middle"; c.textAlign = "left";
  if (T.dayTicks > 0) {
    for (let d = 0; d * T.dayTicks < T.horizon; d++) {
      const x0 = g.x(d * T.dayTicks), x1 = g.x(Math.min(T.horizon, (d + 1) * T.dayTicks));
      if (d % 2 === 0) { c.fillStyle = "rgba(255,255,255,0.025)"; c.fillRect(x0, 12, x1 - x0, r.height - 12); }
      c.fillStyle = COL.muted; if (x1 - x0 > 34) c.fillText("Day " + (d + 1), x0 + 4, 8);
      if (T.dutyTicks > 0) for (let s = T.dutyTicks; s < T.dayTicks; s += T.dutyTicks) { const xs = g.x(d * T.dayTicks + s); c.fillStyle = COL.line; c.fillRect(xs, 14, 1, r.height - 14); }
    }
  }
  // Row labels: text in text colours, a colour key beside each.
  c.textAlign = "right";
  const label = (row, text, key) => { c.fillStyle = COL.text2; c.fillText(text, g.left - 16, row.y + row.h / 2); if (key) { c.fillStyle = key; c.fillRect(g.left - 11, row.y + row.h / 2 - 4, 8, 8); } };
  label(g.rows.dist, "Disturbances"); ROWS.forEach(rw => label(g.rows[rw.key], rw.label, rw.color)); label(g.rows.fail, "Failures", COL.critical);
  label(g.rows.life_strip, selected ? selected.name : "Select a citizen");
  // Small multiples: each category on its own scale (they differ by orders of magnitude).
  const bins = Math.max(10, Math.floor(g.plotW / 2)), span = (T.horizon - T.startTick) / bins;
  const counts = Object.fromEntries([...ROWS.map(rw => rw.key), "fail"].map(k => [k, new Float64Array(bins)]));
  const bin = t => clamp(Math.floor((t - T.startTick) / span), 0, bins - 1);
  for (const cz of cits) for (const m of cz.moves) counts.move[bin(m.dep)]++;
  for (const e of events) { const k = e.cat === "msg" ? "work" : e.cat === "plan" || e.cat === "other" ? null : e.cat; if (k) counts[k][bin(e.tick)]++; }
  stripGeom.counts = counts; stripGeom.bins = bins; stripGeom.span = span;
  for (const rw of [...ROWS, { key: "fail", color: COL.critical }]) {
    const row = g.rows[rw.key], data = counts[rw.key], sorted = [...data].sort((a, b) => a - b), cap = Math.max(1, sorted[Math.floor(sorted.length * 0.98)] || 1);
    c.fillStyle = COL.surface1; c.fillRect(g.left, row.y, g.plotW, row.h);
    if (rw.key === "fail") { c.fillStyle = rw.color; for (let i = 0; i < bins; i++) if (data[i] > 0) { const hh = Math.max(3, row.h * Math.min(1, data[i] / cap)); c.fillRect(g.left + i * g.plotW / bins, row.y + row.h - hh, Math.max(1, g.plotW / bins - 0.5), hh); } continue; }
    c.beginPath(); c.moveTo(g.left, row.y + row.h);
    for (let i = 0; i < bins; i++) c.lineTo(g.left + (i + 0.5) * g.plotW / bins, row.y + row.h - row.h * Math.min(1, data[i] / cap));
    c.lineTo(g.left + g.plotW, row.y + row.h); c.closePath(); c.globalAlpha = 0.85; c.fillStyle = rw.color; c.fill(); c.globalAlpha = 1;
  }
  // Disturbances and institutional milestones as markers.
  stripGeom.markers = [];
  const mark = (e, row, shape, fill, text) => { const x = g.x(e.tick); stripGeom.markers.push({ x, row, e, text }); c.fillStyle = fill; const cy = row.y + row.h / 2;
    c.beginPath(); if (shape === "tri") { c.moveTo(x, cy - 5); c.lineTo(x + 5, cy + 4); c.lineTo(x - 5, cy + 4); } else if (shape === "dia") { c.moveTo(x, cy - 5); c.lineTo(x + 4, cy); c.lineTo(x, cy + 5); c.lineTo(x - 4, cy); } else if (shape === "sq") { c.rect(x - 3.5, cy - 3.5, 7, 7); } else c.arc(x, cy, 3.5, 0, Math.PI * 2);
    c.fill(); c.strokeStyle = COL.surface2; c.lineWidth = 1.5; c.stroke(); };
  for (const e of events) {
    if (e.type === "MachineFaulted") mark(e, g.rows.dist, "tri", COL.critical, "Machine fault · " + e.detail.split(";")[0]);
    else if (e.type === "DirtAdded") mark(e, g.rows.dist, "circle", COL.warning, `Spill · ${e.qty} dirt in room #${e.tile}`);
    else if (e.type === "CardLost") mark(e, g.rows.dist, "dia", COL.warning, "Card lost · " + (citById.get(e.actor)?.name || e.actor));
    else if (e.type === "TerminalStateChanged") { const off = /connected=False|enabled=False/.test(e.detail); mark(e, g.rows.dist, "sq", off ? COL.serious : COL.good, `Terminal ${e.terminal} ${off ? "disconnected" : "reconnected"}`); }
    else if (e.type === "ReprimandAccepted") mark(e, g.rows.inst, "circle", COL.text, "Reprimand · " + e.detail);
    else if (e.type === "Detained") mark(e, g.rows.inst, "sq", COL.critical, "Detained · " + e.detail);
    else if (e.type === "DetentionReleased") mark(e, g.rows.inst, "sq", COL.good, "Released · " + (citById.get(e.actor)?.name || e.actor));
  }
  // The selected citizen's whole life as activity bands.
  const row = g.rows.life_strip; c.fillStyle = COL.surface1; c.fillRect(g.left, row.y, g.plotW, row.h);
  if (selected) {
    for (const a of selected.acts) {
      if (a.kind === "Move" || a.end < 0 && a.outcome !== 0) continue;
      const end = a.end >= 0 ? a.end : T.horizon, began = a.began >= 0 ? a.began : a.req, fam = actInfo(a.kind)[0];
      const x0 = g.x(a.req), x1 = Math.max(g.x(end), x0 + 0.6);
      if (a.queue && began > a.req) { c.fillStyle = COL.lineStrong; c.fillRect(x0, row.y + row.h * 0.35, g.x(began) - x0, row.h * 0.3); }
      c.fillStyle = COL[fam] || COL.idle; c.fillRect(g.x(began), row.y + (a.kind === "Sleep" ? 0 : 2), Math.max(0.6, x1 - g.x(began)), row.h - (a.kind === "Sleep" ? 0 : 4));
    }
    for (const m of selected.moves) { c.fillStyle = COL.travel; c.fillRect(g.x(m.dep), row.y + 1, Math.max(0.6, g.x(m.arr) - g.x(m.dep)), row.h - 2); }
    for (const k of selected.custody) { c.strokeStyle = k.kind === "guard" ? COL.inst : COL.critical; c.lineWidth = 2; c.strokeRect(g.x(k.from), row.y - 1, Math.max(2, g.x(Math.min(k.to, T.horizon)) - g.x(k.from)), row.h + 2); }
  }
  stripCache = off;
}
function drawStrip() {
  const box = strip.getBoundingClientRect();
  if (!T || box.width < 20 || box.height < 20) { resizeCanvas(strip); sctx.clearRect(0, 0, strip.width, strip.height); return; }
  if (resizeCanvas(strip) || !stripCache) buildStripCache();
  const dpr = window.devicePixelRatio || 1;
  sctx.setTransform(1, 0, 0, 1, 0, 0); sctx.drawImage(stripCache, 0, 0);
  sctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const x = stripGeom.x(tick);
  sctx.fillStyle = COL.text; sctx.fillRect(x - 0.75, 12, 1.5, stripGeom.h - 12);
  sctx.beginPath(); sctx.moveTo(x - 5, 10); sctx.lineTo(x + 5, 10); sctx.lineTo(x, 16); sctx.fill();
}

// ---------- people list ----------
let peopleRows = new Map();
function buildPeople() {
  const host = $("people"); host.replaceChildren(); peopleRows = new Map();
  const roles = [...new Set(cits.map(c => c.role))].sort();
  for (const role of roles) {
    const group = el("div", "group"); group.append(el("div", "group-title", role + "s"));
    for (const c of cits.filter(x => x.role === role)) {
      const b = el("button", "person"); b.type = "button";
      const dot = el("span", "dot"), name = el("span", "name", c.name), flag = el("span", "flag"), doing = el("span", "doing");
      b.append(dot, name, flag, doing); b.addEventListener("click", () => select(c));
      group.append(b); peopleRows.set(c, { b, dot, flag, doing, last: "" });
    }
    host.append(group);
  }
  filterPeople();
}
function filterPeople() {
  const q = $("search").value.trim().toLowerCase();
  for (const [c, row] of peopleRows) row.b.hidden = !!q && !`${c.name} ${c.id} ${c.role} ${row.doing.textContent}`.toLowerCase().includes(q);
  document.querySelectorAll(".group").forEach(g => { g.hidden = ![...g.querySelectorAll(".person")].some(b => !b.hidden); });
}
function updatePeople() {
  for (const [c, row] of peopleRows) {
    const st = c.state || describe(c, tick), needs = needsAt(c, tick);
    let worst = 0; needs.forEach((v, k) => { worst = Math.max(worst, band(needDefs[k], v)); });
    const room = st.loc.room ? `#${st.loc.room.id}` : "";
    const sig = `${st.family}|${st.glyph}|${st.label}|${room}|${worst}|${st.custody?.label || ""}|${selected === c}`;
    if (sig === row.last) continue; row.last = sig;
    row.dot.style.background = COL[st.family]; row.dot.style.borderColor = COL[st.family]; row.dot.className = "dot" + (st.waiting ? " hollow" : ""); row.dot.textContent = st.waiting ? "" : st.glyph;
    row.doing.textContent = `${st.custody ? st.custody.label + " · " : ""}${st.label}${room ? " · " + room : ""}`;
    row.flag.textContent = worst ? BAND[worst].label.toUpperCase() : ""; row.flag.style.color = worst ? BAND[worst].color : "";
    row.b.classList.toggle("selected", selected === c);
  }
}

// ---------- dossier ----------
let dossierSig = "", dossierAt = 0, pointerInDossier = false;
const openGroups = new Map();
function select(c) {
  selected = c; stripCache = null; dossierSig = ""; renderDossier(true); dirty = true;
  if (c) { const row = peopleRows.get(c); row?.b.scrollIntoView({ block: "nearest" }); }
}
function section(title) { const s = el("section", "section"); if (title) s.append(el("h2", null, title)); return s; }
function kv(pairs) {
  const dl = el("dl", "kv");
  for (const [k, v, sub, extra] of pairs) { if (v == null || v === "") continue; dl.append(el("dt", null, k)); const dd = el("dd", null, v); if (sub) dd.append(el("small", null, sub)); if (extra) dd.append(extra); dl.append(dd); }
  return dl;
}
function renderDossier(force) {
  const host = $("dossier");
  if (!T) { host.replaceChildren(); return; }
  const now = performance.now();
  // Rebuilding under the pointer would swallow clicks, so slow down while it is there.
  if (!force && now - dossierAt < (pointerInDossier ? 1000 : 140)) return;
  dossierAt = now;
  const sig = (selected ? selected.id : 0) + "|" + Math.floor(tick / (playing ? 30 : 1)) + "|" + [...eventFilter].join(",");
  if (!force && sig === dossierSig) return;
  dossierSig = sig;
  const scroll = host.scrollTop;
  host.replaceChildren(...(selected ? citizenDossier(selected) : stationOverview()));
  host.scrollTop = scroll;
}
function stationOverview() {
  const out = [];
  const s = section("Station now");
  const counts = { travel: 0, work: 0, life: 0, idle: 0 }; let waitingN = 0, custodyN = 0;
  for (const c of cits) { const st = c.state || describe(c, tick); counts[st.family]++; if (st.waiting) waitingN++; if (st.custody && st.custody.kind !== "guard") custodyN++; }
  const grid = el("div", "stat-grid");
  const stat = (n, label, col, hollow) => { const d = el("div", "stat"); d.append(el("b", null, String(n))); const sp = el("span"); const dot = el("i", "dot" + (hollow ? " hollow" : "")); dot.style.background = col; dot.style.borderColor = col; dot.style.width = dot.style.height = "9px"; sp.append(dot, label); d.append(sp); return d; };
  grid.append(stat(counts.work, FAMILY.work, COL.work), stat(counts.life, FAMILY.life, COL.life), stat(counts.travel, FAMILY.travel, COL.travel), stat(counts.idle, FAMILY.idle, COL.idle), stat(waitingN, "In a queue", COL.text2, true), stat(custodyN, "In custody", COL.critical));
  s.append(grid);
  s.append(el("p", "hint", "Click a citizen on the map or in the list to see their path, intent, needs, history and what they know. Drag the strip below or press Space to play."));
  out.push(s);
  const fi = frameAt(tick), rs = section("Rooms");
  const rows = el("div", "rows");
  for (const r of rooms) {
    const row = el("div", "row"); row.style.cursor = "default";
    const problems = places.filter(p => p.room === r && (p.kind === "Terminal" || p.kind === "Machine") && p.state.up[fi] !== 2).map(p => `${p.kind.toLowerCase()} ${p.id} ${p.kind === "Machine" ? "not operational" : "offline"}`);
    const queue = places.filter(p => p.room === r).reduce((n, p) => n + (p.kind === "Detention" ? 0 : p.state.queue[fi]), 0);
    row.append(el("span", "t", `#${r.id}`), el("span"), el("span", "what", r.definition), el("span", "end", `${r.people} in${r.dirt && r.dirt[fi] ? " · dirt " + r.dirt[fi] : ""}${queue ? " · queue " + queue : ""}`));
    rows.append(row);
    if (problems.length) { const p = el("div", "row"); p.style.cursor = "default"; p.append(el("span"), el("span", "bad", "!"), el("span", "what bad", problems.join(", ")), el("span")); rows.append(p); }
  }
  rs.append(rows); out.push(rs);
  const es = section("Station events (latest first)");
  const notable = [];
  for (let i = upper(eventTicks, tick) - 1; i >= 0 && notable.length < 40; i--) { const e = events[i]; if (e.cat === "inst" || e.cat === "fail" || /^(MachineFaulted|DirtAdded|CardLost|TerminalStateChanged)$/.test(e.type)) notable.push(e); }
  es.append(...notable.map(e => eventRow(e)));
  if (!notable.length) es.append(el("p", "hint", "Nothing notable yet."));
  out.push(es);
  return out;
}
function citizenDossier(c) {
  const out = [], st = describe(c, tick), fi = frameAt(tick);
  // Identity.
  const head = section(); const who = el("div", "who");
  const big = el("span", "big", st.glyph || ""); big.style.background = COL[st.family];
  const text = el("div"); text.append(el("h3", null, c.name));
  const roster = [...c.rosters].reverse().find(r => r[0] <= tick) || c.rosters[0];
  const shift = roster && T.dayTicks ? ` · cohort ${roster[2] || 0}, duty ${clockText(roster[3] || 0).split("· ")[1]}–${clockText(((roster[3] || 0) + (roster[4] || 0)) % T.dayTicks).split("· ")[1]}` : "";
  const sub = el("p"); sub.append(el("span", "chip", c.role), `id ${c.id} · account ${c.account}${shift}`); text.append(sub);
  const close = el("button", "close", "×"); close.type = "button"; close.title = "Close (Esc)"; close.addEventListener("click", () => select(null));
  who.append(big, text, close); head.append(who); out.push(head);
  // Now.
  const now = section("Now");
  const a = st.act, loc = st.loc;
  let where = loc.room ? `${loc.room.definition} #${loc.room.id}` : "—", whereSub = "";
  if (loc.moving) { const to = roomByKey.get(loc.move.to.join(",")); where = `${loc.room?.definition || "?"} → ${to?.definition || "?"}`; whereSub = `arrives ${agoText(loc.move.arr)}`; }
  let doing = st.label, doingSub = "", progress = null;
  if (a && a.kind !== "Move") {
    const p = placeFor(c, a, loc.room);
    if (st.waiting) doingSub = `queued for ${durationText(tick - a.req)}${p ? " at " + p.label : ""}`;
    else {
      const began = a.began >= 0 ? a.began : a.req;
      doingSub = `since ${durationText(tick - began)}${p ? " · " + p.label : ""}${a.end >= 0 ? ` · ends ${agoText(a.end)}` : ""}${a.origin && a.origin !== "Autonomous" && a.origin !== "None" ? " · " + a.origin : ""}`;
      if (a.end > began) { progress = el("div", "progress"); const bar = el("i"); bar.style.width = `${clamp((tick - began) / (a.end - began), 0, 1) * 100}%`; progress.append(bar); }
    }
  } else if (!loc.moving) doing = "Idle";
  const intent = intentAt(c, tick), driver = intent ? driverBefore(c, intent.tick + 1, intent.tick - (T.dayTicks ? T.dayTicks / 12 : 3000)) : null;
  const unavailable = (() => { for (let i = upper(c.eventTicks, tick) - 1; i >= 0 && c.events[i].tick > tick - (T.dayTicks ? T.dayTicks / 24 : 1500); i--) if (c.events[i].type === "PlanUnavailable" && c.events[i].actor === c.id) return c.events[i]; return null; })();
  const failure = (() => { for (let i = upper(c.eventTicks, tick) - 1; i >= 0; i--) if (c.events[i].type === "ActionFailed" && c.events[i].actor === c.id) return c.events[i]; return null; })();
  now.append(kv([
    ["Where", where, whereSub],
    ["Doing", doing, doingSub, progress],
    ["Intent", intent ? planName(intent.detail) : "—", intent ? `${intent.detail.split(";").slice(1).join(";").trim()} · chosen ${agoText(intent.tick)}` : ""],
    ["Because", driver ? `${words(driver.type)}${driver.detail ? ": " + driver.detail : ""}` : "", driver ? agoText(driver.tick) : ""],
    ["Blocked", unavailable ? planName(unavailable.detail) : "", unavailable ? unavailable.detail.split(";").slice(1).join(";").trim() + " · " + agoText(unavailable.tick) : ""],
    ["Routine", [frameString(c, "activity", tick), frameString(c, "mode", tick)].filter(Boolean).join(" · ")],
    ["Custody", st.custody ? st.custody.label : "", st.custody ? `${agoText(st.custody.from)}${isFinite(st.custody.to) ? " · until " + clockText(st.custody.to) : ""}` : ""],
    ["Carrying", frameString(c, "inventory", tick) || "nothing"],
    ["Last failure", failure ? failure.detail : "", failure ? agoText(failure.tick) : ""]
  ]));
  out.push(now);
  // Needs: bar with thresholds now, whole-run sparkline with the cursor.
  const ns = section("Needs"); const values = needsAt(c, tick);
  needDefs.forEach((d, k) => {
    const v = values[k], b = band(d, v), row = el("div", "need");
    const label = el("div", "label"); label.append(el("b", null, words(d.kind)), `${Math.round(v)}${b ? " · " + BAND[b].label : ""}`);
    const bar = el("div", "bar"); const fill = el("i"); fill.style.width = `${clamp(v / 1000, 0, 1) * 100}%`; fill.style.background = b ? BAND[b].color : COL.text2; bar.append(fill);
    for (const thr of [d.seek, d.urgent, d.critical]) if (thr > 0) { const tk = el("u"); tk.style.left = `${thr / 10}%`; bar.append(tk); }
    bar.title = `Seek ${d.seek || "—"} · Urgent ${d.urgent || "—"} · Critical ${d.critical || "—"}`;
    const spark = document.createElement("canvas"); spark.title = "Whole run — click to jump";
    row.append(label, bar, spark); ns.append(row);
    requestAnimationFrame(() => drawSpark(spark, c, k, d));
    spark.addEventListener("click", e => { const r = spark.getBoundingClientRect(); setTick(T.startTick + (e.clientX - r.left) / r.width * (T.horizon - T.startTick), true); });
  });
  out.push(ns);
  // Path: recent and upcoming room visits.
  const ps = section("Path"); const prow = el("div", "rows");
  const visits = []; let pos = c.position, since = T.startTick;
  for (const m of c.moves) { visits.push({ room: roomByKey.get(pos.join(",")), from: since, to: m.dep }); pos = m.to; since = m.arr; }
  visits.push({ room: roomByKey.get(pos.join(",")), from: since, to: T.horizon });
  const merged = []; for (const v of visits) { const last = merged[merged.length - 1]; if (last && last.room === v.room && v.from - last.to <= 30) last.to = v.to; else merged.push({ ...v }); }
  const vi = Math.max(0, merged.findIndex(v => v.to > tick));
  for (const v of merged.slice(Math.max(0, vi - 6), vi + 4)) {
    const r = el("div", "row" + (v.from > tick ? " future" : v.to > tick ? " now" : ""));
    r.append(el("span", "t", clockText(v.from).replace("Day ", "D")), el("span"), el("span", "what", v.room ? `${v.room.definition} #${v.room.id}` : "?"), el("span", "end", durationText(Math.min(v.to, T.horizon) - v.from)));
    r.addEventListener("click", () => setTick(v.from, true)); prow.append(r);
  }
  ps.append(prow); out.push(ps);
  // Activity history around now.
  const hs = section("Activity"); const hrows = el("div", "rows");
  const acts = c.acts.filter(x => x.kind !== "Move" && (x.end < 0 || x.end > x.req || x.outcome === 2)), ai = upper(acts.map(x => x.req), tick);
  for (const x of acts.slice(Math.max(0, ai - 10), ai + 4)) {
    const [fam, glyph, label] = actInfo(x.kind), cur = x.req <= tick && (x.end < 0 || x.end > tick);
    const r = el("div", "row" + (x.req > tick ? " future" : cur ? " now" : ""));
    const dot = el("span", "dot", glyph); dot.style.background = COL[fam];
    const what = el("span", "what", label); const place = placeFor(c, x, locate(c, Math.max(x.req, x.began)).room);
    if (place) what.append(el("small", null, place.label)); if (x.failure) what.append(el("small", "bad", x.failure));
    const outcome = x.outcome === 1 ? "✓" : x.outcome === 2 ? "✕" : x.outcome === 3 ? "⊘" : "…";
    const end = el("span", "end " + (x.outcome === 2 ? "bad" : x.outcome === 1 ? "" : "warn"), `${x.end >= 0 ? durationText(x.end - x.req) : "open"} ${outcome}`);
    r.append(el("span", "t", clockText(x.req).replace("Day ", "D")), dot, what, end);
    r.title = `${x.kind} · requested ${clockText(x.req)}${x.began >= 0 ? " · began " + clockText(x.began) : ""}${x.end >= 0 ? " · ended " + clockText(x.end) : ""} · ${x.origin}`;
    r.addEventListener("click", () => setTick(x.began >= 0 ? x.began : x.req, true)); hrows.append(r);
  }
  hs.append(hrows); out.push(hs);
  // Events.
  const es = section("Events (latest first)");
  const chips = el("div", "chips");
  for (const [key, cat] of Object.entries(CATS)) {
    const b = el("button", eventFilter.has(key) ? "on" : "", cat.label); b.type = "button";
    b.addEventListener("click", () => { if (eventFilter.has(key)) eventFilter.delete(key); else eventFilter.add(key); renderDossier(true); });
    chips.append(b);
  }
  es.append(chips);
  const list = [];
  for (let i = upper(c.eventTicks, tick) - 1; i >= 0 && list.length < 60; i--) if (eventFilter.has(c.events[i].cat)) list.push(c.events[i]);
  es.append(...list.map(e => eventRow(e)));
  if (!list.length) es.append(el("p", "hint", "No events in the selected categories yet."));
  out.push(es);
  // Mind: knowledge at this tick, rebuilt from initial facts plus recorded changes.
  const ms = section("What they know");
  const known = new Map();
  for (const k of c.knowledge) known.set(S[k[0]], { value: S[k[1]], source: S[k[2] || 0] || "Seeded", at: k[3] || 0 });
  for (const l of c.learned) { if (l[0] > tick) break; const key = S[l[1]]; if (l[2] < 0) known.delete(key); else known.set(key, { value: S[l[2]], source: S[l[3] || 0], at: l[0], message: l[5] || 0 }); }
  const groups = new Map();
  for (const [key, f] of known) { const kind = key.split(":")[0]; (groups.get(kind) || groups.set(kind, []).get(kind)).push([key, f]); }
  ms.append(el("p", "hint", `${known.size} facts. Highlighted facts changed in the last ${durationText(T.dayTicks ? T.dayTicks / 12 : 3000)}.`));
  for (const [kind, facts] of [...groups].sort((x, y) => x[0].localeCompare(y[0]))) {
    const det = document.createElement("details"); det.open = openGroups.has(kind) ? openGroups.get(kind) : facts.length <= 12;
    det.addEventListener("toggle", () => openGroups.set(kind, det.open));
    det.append(el("summary", null, `${kind} · ${facts.length}`));
    facts.sort((x, y) => y[1].at - x[1].at);
    for (const [key, f] of facts.slice(0, 80)) {
      const row = el("div", "fact" + (tick - f.at < (T.dayTicks ? T.dayTicks / 12 : 3000) ? " fresh" : ""));
      row.append(el("span", "k", key.slice(kind.length + 1)), el("span", "v", f.value));
      const src = el("span", "src", `${f.source}${f.message ? " · message " + f.message : ""} · ${agoText(f.at)}`);
      const truth = truthFor(key, fi); if (truth != null && truth !== f.value) src.append(el("span", "bad", `actual ${truth}`));
      row.append(src); det.append(row);
    }
    ms.append(det);
  }
  out.push(ms);
  return out;
}
// Physical truth for the few facts the export samples (dirt, machine and terminal status).
function truthFor(key, fi) {
  let m = /^Tile:(\d+)\/Dirt$/.exec(key); if (m) { const r = roomById.get(+m[1]); return r?.dirt ? String(r.dirt[fi]) : null; }
  m = /^Machine:(\d+)\/Operational$/.exec(key); if (m) { const p = placeByKey.get("Machine:" + m[1]); return p ? String(p.state.up[fi] === 2) : null; }
  return null;
}
function eventRow(e) {
  const d = el("div", "event"), meta = el("div", "meta"), sw = el("i"); sw.style.background = CATS[e.cat].color;
  meta.append(sw, `${clockText(e.tick)} · ${agoText(e.tick)}${e.actor && e.actor !== selected?.id ? " · " + (citById.get(e.actor)?.name || "actor " + e.actor) : ""}`);
  d.append(meta, el("strong", null, words(e.type)));
  const bits = [e.detail, e.message ? "message " + e.message : "", e.terminal ? "terminal " + e.terminal : "", e.attempt ? "attempt " + e.attempt : "", e.tile ? "room #" + e.tile : ""].filter(Boolean);
  if (bits.length) d.append(el("p", null, bits.join(" · ")));
  d.title = `Event #${e.id}${e.cause ? " · caused by #" + e.cause : ""} · click to jump`;
  d.addEventListener("click", () => setTick(e.tick, true));
  return d;
}
function drawSpark(canvas, c, k, d) {
  const r = canvas.getBoundingClientRect(); if (!r.width) return;
  const dpr = window.devicePixelRatio || 1; canvas.width = Math.round(r.width * dpr); canvas.height = Math.round(r.height * dpr);
  const ctx = canvas.getContext("2d"); ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const K = needDefs.length, n = FT.length, y = v => r.height - 2 - clamp(v / 1000, 0, 1) * (r.height - 4), x = t => (t - T.startTick) / (T.horizon - T.startTick) * r.width;
  ctx.fillStyle = COL.surface2; ctx.fillRect(0, 0, r.width, r.height);
  for (const thr of [d.seek, d.urgent, d.critical]) if (thr > 0) { ctx.fillStyle = COL.line; ctx.fillRect(0, y(thr), r.width, 1); }
  ctx.beginPath(); for (let i = 0; i < n; i++) { const v = c.f.needs[i * K + k]; if (i) ctx.lineTo(x(FT[i]), y(v)); else ctx.moveTo(x(FT[i]), y(v)); }
  ctx.strokeStyle = COL.text2; ctx.lineWidth = 1.2; ctx.stroke();
  ctx.fillStyle = COL.text; ctx.fillRect(x(tick) - 0.75, 0, 1.5, r.height);
}

// ---------- legend ----------
function buildLegend() {
  const L = $("legend"); L.replaceChildren();
  const item = (node, text, key) => { const s = el("span"); s.append(node, text); if (key) s.append(el("span", "key", key)); L.append(s); };
  const dot = (col, hollow, glyph) => { const d = el("span", "dot" + (hollow ? " hollow" : ""), glyph || ""); d.style.background = col; d.style.borderColor = col; return d; };
  item(dot(COL.travel), FAMILY.travel);
  item(dot(COL.work, false, "C"), FAMILY.work, "C clean · R repair · T terminal · G guard · P items");
  item(dot(COL.life, false, "M"), FAMILY.life, "M meal · W washroom · B rest · Z sleep · S talk");
  item(dot(COL.idle), FAMILY.idle);
  item(dot(COL.text2, true), "Queued");
  for (let b = 1; b <= 3; b++) { const r = el("span", "ring"); r.style.borderColor = BAND[b].color; item(r, BAND[b].label + " need"); }
  item(el("span", "cage"), "Custody");
}

// ---------- interaction ----------
function citizenAt(px, py) {
  let best = null, bestD = Infinity;
  for (const c of cits) {
    if (!c.disp) continue;
    const sx = c.disp.x * view.scale + view.ox, sy = c.disp.y * view.scale + view.oy, rr = Math.max(10, c.target.r * view.scale + 5), dd = Math.hypot(sx - px, sy - py);
    if (dd < rr && dd < bestD) { best = c; bestD = dd; }
  }
  return best;
}
let drag = null;
map.addEventListener("mousedown", e => { drag = { x: e.clientX, y: e.clientY, ox: view.ox, oy: view.oy, moved: false }; });
window.addEventListener("mousemove", e => {
  if (drag) {
    const dx = e.clientX - drag.x, dy = e.clientY - drag.y;
    if (Math.abs(dx) + Math.abs(dy) > 3) { drag.moved = true; map.classList.add("dragging"); }
    if (drag.moved) { view.ox = drag.ox + dx; view.oy = drag.oy + dy; dirty = true; }
  }
});
window.addEventListener("mouseup", e => {
  if (drag && !drag.moved && e.target === map) { const r = map.getBoundingClientRect(), c = citizenAt(e.clientX - r.left, e.clientY - r.top); if (c) select(c); }
  drag = null; map.classList.remove("dragging");
});
map.addEventListener("mousemove", e => {
  if (!T || drag?.moved) return;
  const r = map.getBoundingClientRect(), c = citizenAt(e.clientX - r.left, e.clientY - r.top);
  if (c !== hovered) { hovered = c; dirty = true; }
  const tip = $("tip");
  if (!c) { tip.hidden = true; return; }
  const st = c.state; tip.replaceChildren(el("strong", null, `${c.name} · ${c.role}`), el("div", null, `${st.custody ? st.custody.label + " · " : ""}${st.label}`), el("div", null, st.loc.room ? `${st.loc.room.definition} #${st.loc.room.id}` : ""));
  tip.hidden = false; tip.style.left = `${Math.min(r.width - 220, e.clientX - r.left + 14)}px`; tip.style.top = `${e.clientY - r.top + 14}px`;
});
map.addEventListener("mouseleave", () => { hovered = null; $("tip").hidden = true; dirty = true; });
map.addEventListener("wheel", e => {
  if (!T) return; e.preventDefault();
  const r = map.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, k = Math.exp(-e.deltaY * 0.0015);
  const base = Math.min(r.width / layoutDeck.w, r.height / layoutDeck.h), scale = clamp(view.scale * k, base * 0.6, base * 10);
  view.ox = px - (px - view.ox) * scale / view.scale; view.oy = py - (py - view.oy) * scale / view.scale; view.scale = scale; dirty = true;
}, { passive: false });
map.addEventListener("dblclick", fit);
$("fit").addEventListener("click", fit);
$("deck").addEventListener("change", e => { deck = +e.target.value; layoutDeck = null; fit(); });
for (const id of ["showTrail", "showDirt", "showIds"]) $(id).addEventListener("change", () => { dirty = true; });
$("search").addEventListener("input", filterPeople);
$("dossier").addEventListener("mouseenter", () => { pointerInDossier = true; });
$("dossier").addEventListener("mouseleave", () => { pointerInDossier = false; });

let stripDrag = false;
const stripSeek = e => { if (!T || !stripGeom) return; const r = strip.getBoundingClientRect(); setTick(Math.round(stripGeom.t(e.clientX - r.left)), true); };
strip.addEventListener("mousedown", e => { stripDrag = true; stripSeek(e); });
window.addEventListener("mousemove", e => { if (stripDrag) stripSeek(e); });
window.addEventListener("mouseup", () => { stripDrag = false; });
strip.addEventListener("mousemove", e => {
  if (!T || !stripGeom) return;
  const r = strip.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, tip = $("stripTip");
  if (px < stripGeom.left) { tip.hidden = true; return; }
  const t = stripGeom.t(px), mk = stripGeom.markers.find(m => Math.abs(m.x - px) < 5 && py >= m.row.y - 3 && py <= m.row.y + m.row.h + 3);
  const b = clamp(Math.floor((t - T.startTick) / stripGeom.span), 0, stripGeom.bins - 1), ct = stripGeom.counts;
  tip.replaceChildren(el("strong", null, clockText(mk ? mk.e.tick : t)));
  if (mk) tip.append(el("div", null, mk.text));
  else tip.append(el("div", null, `Moves ${ct.move[b]} · work & messages ${ct.work[b]} · daily life ${ct.life[b]} · institutions ${ct.inst[b]} · failures ${ct.fail[b]}`));
  tip.hidden = false; tip.style.left = `${Math.min(r.width - 300, px + 12)}px`; tip.style.top = "4px";
});
strip.addEventListener("mouseleave", () => { $("stripTip").hidden = true; });

$("scrub").addEventListener("input", e => setTick(+e.target.value, false));
$("tickInput").addEventListener("change", e => setTick(+e.target.value, true));
$("toStart").addEventListener("click", () => setTick(T ? T.startTick : 0, true));
$("play").addEventListener("click", () => setPlaying(!playing));
$("prevEvent").addEventListener("click", () => jumpEvent(-1));
$("nextEvent").addEventListener("click", () => jumpEvent(1));
function setPlaying(on) {
  playing = on && !!T; $("play").textContent = playing ? "❚❚" : "▶"; $("play").setAttribute("aria-label", playing ? "Pause" : "Play");
  if (playing && tick >= T.horizon) setTick(T.startTick, true);
}
function jumpEvent(dir) {
  if (!T) return;
  const list = selected ? selected.events.filter(e => eventFilter.has(e.cat)) : events.filter(e => e.cat === "inst" || e.cat === "fail");
  const target = dir > 0 ? list.find(e => e.tick > Math.floor(tick)) : [...list].reverse().find(e => e.tick < Math.floor(tick));
  if (target) setTick(target.tick, true);
}
window.addEventListener("keydown", e => {
  if (!T || e.target instanceof HTMLInputElement && e.target.type !== "range" || e.target instanceof HTMLSelectElement) return;
  if (e.target instanceof HTMLButtonElement && (e.key === " " || e.key === "Enter")) return;
  if (e.target instanceof HTMLInputElement && e.key.startsWith("Arrow")) return; // the focused slider steps itself
  const step = e.shiftKey ? 600 : 10;
  if (e.key === " ") { e.preventDefault(); setPlaying(!playing); }
  else if (e.key === "ArrowRight") { e.preventDefault(); setTick(Math.floor(tick) + step, false); }
  else if (e.key === "ArrowLeft") { e.preventDefault(); setTick(Math.floor(tick) - step, false); }
  else if (e.key === "]") jumpEvent(1); else if (e.key === "[") jumpEvent(-1);
  else if (e.key === "Home") setTick(T.startTick, true); else if (e.key === "End") setTick(T.horizon, true);
  else if (e.key === "Escape") select(null);
});

// ---------- loading ----------
async function readText(buffer) {
  const bytes = new Uint8Array(buffer);
  if (bytes[0] === 0x1f && bytes[1] === 0x8b) return await new Response(new Blob([bytes]).stream().pipeThrough(new DecompressionStream("gzip"))).text();
  return new TextDecoder().decode(bytes);
}
const setStatus = text => { $("status").textContent = text; };
async function load(read, name) {
  try { setStatus("Reading " + name + " …"); const data = JSON.parse(await readText(await read())); start(data, name); setStatus(""); }
  catch (error) { $("empty").hidden = false; setStatus(error.message); console.error(error); }
}
$("file").addEventListener("change", e => { const f = e.target.files[0]; if (f) load(() => f.arrayBuffer(), f.name); });
const wrap = $("mapWrap");
wrap.addEventListener("dragover", e => { e.preventDefault(); $("empty").classList.add("dragover"); $("empty").hidden = false; });
wrap.addEventListener("dragleave", () => { $("empty").classList.remove("dragover"); if (T) $("empty").hidden = true; });
wrap.addEventListener("drop", e => { e.preventDefault(); $("empty").classList.remove("dragover"); const f = e.dataTransfer.files[0]; if (f) load(() => f.arrayBuffer(), f.name); });
const param = new URLSearchParams(location.search).get("timeline");
if (param) load(async () => { const r = await fetch(param); if (!r.ok) throw Error(`${param}: HTTP ${r.status}`); return r.arrayBuffer(); }, param);

// ---------- frame loop ----------
new ResizeObserver(() => { if (T) { const keep = view; fit(); if (keep.scale) dirty = true; stripCache = null; } dirty = true; }).observe($("mapWrap"));
new ResizeObserver(() => { stripCache = null; dirty = true; }).observe(strip);
function frame(now) {
  const dt = Math.min(0.1, (now - (lastClock || now)) / 1000); lastClock = now;
  if (T && playing) { setTick(tick + dt * +$("speed").value * 1000 / T.tickMs, false); if (tick >= T.horizon) setPlaying(false); }
  if (T && (dirty || cits.some(c => c.disp && c.target && Math.hypot(c.disp.x - c.target.x, c.disp.y - c.target.y) > 0.3))) {
    placeCitizens(tick, dt); drawMap(); drawStrip(); updatePeople(); renderDossier(false);
    const t = Math.floor(tick);
    $("clock").textContent = clockText(t); $("tickLabel").textContent = `sim ${simText(t)} · tick ${t.toLocaleString()}`;
    if (document.activeElement !== $("scrub")) $("scrub").value = String(t); else if (playing) $("scrub").value = String(t);
    if (document.activeElement !== $("tickInput")) $("tickInput").value = String(t);
    lastRenderTick = tick; dirty = false;
  }
  requestAnimationFrame(frame);
}
requestAnimationFrame(frame);
drawStrip();
