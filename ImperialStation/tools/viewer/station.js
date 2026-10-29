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
  text: color("text-primary"), text2: color("text-secondary"), muted: color("text-muted"), accent: color("accent"),
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
  places.forEach(p => { p.changes = (p.statusChanges || []).map(r => [r[0], r[1] || 0]); p.changeTicks = p.changes.map(r => r[0]); });
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
  buildAuthority(data, byAccount); buildNodes(); buildCases(); openCase = null;
  authoritySelected = false; focusId = 0; chainActors = new Set(); authorityInChain = false;
  deck = decks[0] ?? 0;
  $("deck").replaceChildren(...decks.map(y => new Option("Y " + y, String(y)))); $("deck").value = String(deck); $("deck").disabled = decks.length < 2;
  $("scrub").min = String(data.startTick); $("scrub").max = String(data.horizon); $("tickInput").max = String(data.horizon);
  $("run").textContent = `${data.scenario} · seed ${data.seed} · ${cits.length} citizens · ${rooms.length} rooms · ${(data.horizon / (data.dayTicks || data.horizon)).toFixed(0)} days · digest ${data.finalDigest.slice(0, 12)}… · ${source}`;
  $("empty").hidden = true;
  selected = null; hovered = null; layoutDeck = null; stripCache = null;
  buildPeople(); buildLegend(); fit(); setTick(data.startTick, true); renderDossier(true);
}
// ---------- authority, messages and cause links ----------
let messages = [], messageTicks = [], AUTH = null, nodes = new Map(), childrenOf = new Map(), firstOfMessage = new Map();
let authoritySelected = false, focusId = 0, chainActors = new Set(), authorityInChain = false, hubHovered = false, maxLatency = 0;
// Physical-world events carry no actor but are not authority decisions.
const WORLD_EVENTS = new Set(["DirtAdded", "MachineFaulted", "TerminalStateChanged", "FaultPartConsumed", "ObservationRecorded"]);
const INSTITUTIONAL_MESSAGES = new Set(["FileCase", "CaseDecision", "RequestDetention", "DetentionDecision", "EscortOrder", "EscortAcknowledged", "DetentionIntakeReceipt",
  "DetentionReleaseReceipt", "DetentionReleaseNotice", "PatrolOrder", "PatrolAccepted", "PatrolFound", "PatrolNotLocated", "PatrolBlocked"]);
function buildAuthority(data, byAccount) {
  messages = (data.messages || []).map(m => ({ id: m[0], kind: S[m[1]], sender: m[2] || 0, senderAccount: m[3] || 0, recipientKind: S[m[4] || 0], recipient: m[5] || 0,
    terminal: m[6] || 0, submitted: m[7] || 0, delivered: m[8] ?? -1, read: m[9] ?? -1, attempt: m[10] || 0, task: m[11] || 0, subject: S[m[12] || 0], claim: S[m[13] || 0] }));
  for (const m of messages) {
    m.toAuthority = m.recipientKind === "Authority"; m.fromAuthority = !m.sender && !m.senderAccount;
    m.from = citById.get(m.sender) || byAccount.get(m.senderAccount) || null; m.to = m.recipientKind === "Account" ? byAccount.get(m.recipient) || null : null;
    m.institutional = INSTITUTIONAL_MESSAGES.has(m.kind);
  }
  messages.sort((a, b) => a.submitted - b.submitted || a.id - b.id); messageTicks = messages.map(m => m.submitted);
  maxLatency = messages.reduce((n, m) => Math.max(n, m.delivered - m.submitted), 0);
  for (const c of cits) c.mail = messages.filter(m => m.to === c && m.delivered >= 0);
  const A = data.authority || { beliefs: [], beliefChanges: [], attempts: [], expectations: [], incidents: [], facilityTasks: [], frames: {} };
  AUTH = {
    beliefs: A.beliefs.map(b => ({ key: S[b[0]], value: S[b[1]], source: S[b[2] || 0], message: b[3] || 0, at: b[5] || 0, event: 0 })),
    changes: A.beliefChanges.map(b => ({ tick: b[0], key: S[b[1]], value: S[b[2]], message: b[3] || 0, event: b[4] || 0 })),
    attempts: new Map(A.attempts.map(a => [a[0], { id: a[0], job: a[1] || 0, actor: a[2] || 0, dispatched: a[3] || 0, order: a[4] || 0, acknowledged: a[5] ?? -1,
      assignment: S[a[6] || 0], execution: S[a[7] || 0], status: S[a[8] || 0] }])),
    expectations: A.expectations.map(x => ({ attempt: x[0], job: x[1] || 0, actor: x[2] || 0, opened: x[3] || 0, deadline: x[4] ?? -1, closed: x[5] ?? -1, status: S[x[6] || 0], reason: S[x[7] || 0], task: S[x[8] || 0] })),
    incidents: A.incidents.map(i => ({ id: i[0], attempt: i[1] || 0, job: i[2] || 0, actor: i[3] || 0, opened: i[4] || 0, admitted: i[5] ?? -1, closed: i[6] ?? -1,
      kind: S[i[7] || 0], response: S[i[8] || 0], resolution: S[i[9] || 0], worker: S[i[10] || 0], responseMessage: i[11] || 0, replacement: i[12] || 0,
      replacementResult: i[13] || 0, patrolOrder: i[14] || 0, patrolResult: i[15] || 0 })),
    facility: A.facilityTasks.map(f => ({ job: f[0], subject: S[f[1] || 0], opened: f[2] || 0, closed: f[3] ?? -1, status: S[f[4] || 0], reason: S[f[5] || 0], reports: f[6] || 0, attempt: f[7] || 0 })),
    frames: A.frames || {},
    log: events.filter(e => !e.actor && !WORLD_EVENTS.has(e.type)),
    execution: new Map()
  };
  AUTH.logTicks = AUTH.log.map(e => e.tick);
  for (const e of events) if (e.attempt && e.type.startsWith("WorkExecution")) (AUTH.execution.get(e.attempt) || AUTH.execution.set(e.attempt, []).get(e.attempt)).push({ tick: e.tick, state: e.type.slice(13) });
}
// What the authority believes at a tick: initial beliefs plus every received change up to it.
function beliefsAt(t) {
  const out = new Map(AUTH.beliefs.map(b => [b.key, b]));
  for (const b of AUTH.changes) { if (b.tick > t) break; out.set(b.key, { key: b.key, value: b.value, source: "Reported", message: b.message, at: b.tick, event: b.event }); }
  return out;
}
// Truth for belief/knowledge keys the export can check; "info" truths use a different vocabulary.
function truthAt(key, t) {
  const fi = frameAt(t);
  let m = /^Tile:(\d+)\/Dirt$/.exec(key); if (m) { const r = roomById.get(+m[1]); return r?.dirt ? { value: String(r.dirt[fi]), numeric: true } : null; }
  m = /^Machine:(\d+)\/Operational$/.exec(key); if (m) { const p = placeByKey.get("Machine:" + m[1]); return p ? { value: String(placeUp(p, t) === 2) } : null; }
  m = /^Actor:(\d+)\/Location$/.exec(key);
  if (m) { const c = citById.get(+m[1]), loc = c && locate(c, t); return loc && !loc.moving && loc.room ? { value: `at (${loc.room.position.join(", ")})` } : null; }
  m = /^Attempt:(\d+)\/WorkStatus$/.exec(key);
  if (m) { const list = AUTH.execution.get(+m[1]) || []; let state = null; for (const x of list) if (x.tick <= t) state = x.state; return state ? { value: "physically " + state, info: true } : null; }
  return null;
}
// Belief versus truth: "wrong" for categorical facts, "stale" for counts that have since changed.
function beliefCheck(b, t) {
  const truth = truthAt(b.key, t);
  if (!truth || truth.info || truth.value === b.value) return { truth, state: truth && !truth.info ? "right" : "unknown" };
  return { truth, state: truth.numeric ? "stale" : "wrong" };
}
function buildNodes() {
  nodes = new Map(); childrenOf = new Map(); firstOfMessage = new Map();
  for (const e of events) {
    nodes.set(e.id, { id: e.id, tick: e.tick, kind: "event", e, actor: e.actor, cause: e.cause });
    if (e.message && !firstOfMessage.has(e.message)) firstOfMessage.set(e.message, e);
  }
  for (const c of cits) {
    for (const a of c.acts) nodes.set(a.reqEvent, { id: a.reqEvent, tick: a.req, kind: "act", c, a, actor: c.id, cause: a.cause });
    for (const l of c.learned) nodes.set(l[4], { id: l[4], tick: l[0], kind: "learned", c, key: S[l[1]], value: l[2] < 0 ? null : S[l[2]], source: S[l[3] || 0], actor: c.id, cause: l[6] || 0 });
  }
  for (const n of nodes.values()) { const p = parentOf(n); if (p) (childrenOf.get(p.id) || childrenOf.set(p.id, []).get(p.id)).push(n); }
  for (const list of childrenOf.values()) list.sort((a, b) => a.tick - b.tick || a.id - b.id);
}
// Explicit cause first; otherwise an event continues the first event of its message.
function parentOf(n) {
  if (n.cause) return nodes.get(n.cause) || null;
  if (n.kind === "event" && n.e.message) { const first = firstOfMessage.get(n.e.message); if (first && first.id !== n.id) return nodes.get(first.id) || null; }
  return null;
}
function nodeCategory(n) { return n.kind === "act" ? (actInfo(n.a.kind)[0] === "life" ? "life" : "work") : n.kind === "learned" ? "plan" : n.e.cat; }
function nodeText(n) {
  if (n.kind === "act") return { title: `${n.c.name}: ${actInfo(n.a.kind)[2].toLowerCase()}`, sub: `action request · ${n.a.origin || "?"}${n.a.outcome === 2 ? " · failed " + n.a.failure : n.a.outcome === 3 ? " · cancelled" : ""}` };
  if (n.kind === "learned") return { title: `${n.c.name} ${n.value == null ? "forgot" : "learned"} ${n.key}${n.value != null ? " = " + n.value : ""}`, sub: n.source || "knowledge" };
  const e = n.e, who = e.actor ? citById.get(e.actor)?.name || "actor " + e.actor : WORLD_EVENTS.has(e.type) ? "Station" : "Authority";
  return { title: words(e.type), sub: [who, e.detail, e.message ? "message " + e.message : "", e.attempt ? "attempt " + e.attempt : ""].filter(Boolean).join(" · ") };
}
function focusNode(id, seek) {
  focusId = nodes.has(id) ? id : 0;
  const n = nodes.get(focusId);
  chainActors = new Set(); authorityInChain = false;
  if (n) {
    const involved = [n, ...(childrenOf.get(n.id) || []).slice(0, 12)]; let p = parentOf(n);
    for (let k = 0; p && k < 16; k++, p = parentOf(p)) involved.push(p);
    for (const x of involved) { if (x.actor) chainActors.add(x.actor); else if (x.kind === "event" && !WORLD_EVENTS.has(x.e.type)) authorityInChain = true; }
    if (seek) setTick(n.tick, true);
  }
  dossierSig = ""; renderDossier(true); dirty = true;
}
function selectAuthority() { selected = null; authoritySelected = true; stripCache = null; dossierSig = ""; renderDossier(true); dirty = true; }

// ---------- cases ----------
// A case grows from seed identifiers through the events and messages that share them.
let cases = [], openCase = null, caseView = null, caseHits = [];
// Job ids only seed a case: many attempts share one job, so growing through them joins unrelated work.
const CASE_KEYS = new Set(["attempt", "message", "authorization", "case"]);
const MILESTONES = {
  ItemInstalled: "Part installed", MachineTested: "Machine tested", QualitySampled: "QA sampled", VerificationInspected: "Inspected", DiscrepancyAuditOpened: "Audit opened",
  DiscrepancyAuditResolved: "Audit resolved", CaseFilingQueued: "Case filed", ReprimandAccepted: "Reprimand", CaseDecisionRead: "Decision read", DetentionRequestQueued: "Detention requested",
  DetentionAuthorized: "Authorized", EscortTeamDispatched: "Escort dispatched", GuardOrderRead: "Order read", GuardJoinedTeam: "Joined team", GuardTeamFormedLocally: "Team formed",
  CustodyBegan: "Custody", EscortAwaitingIntake: "At detention", Detained: "Detained", DetentionReleased: "Released", MissingWorkReportOpened: "Incident opened", IncidentAdmitted: "Admitted",
  IncidentResponseChanged: "Response changed", PatrolResultReceived: "Patrol result", FacilityTaskCreated: "Task created", FacilityTaskResponse: "Task response", WorkAssigned: "Assigned",
  CardLost: "Card lost", CredentialRecoveryNeeded: "Recovery needed", CredentialRecoveryCompleted: "Recovered", CleanupGoalCompleted: "Cleanup done", RepairGoalCompleted: "Repair done",
  ReportExpectationSatisfied: "Report received", AssignmentSuperseded: "Superseded", WorkReportRejected: "Report rejected"
};
const WORK_KINDS = new Set(["Clean", "InspectTile", "Install", "InspectMachine", "FunctionalTest", "ProduceRecipe"]);
function eventKeys(e) {
  const keys = [];
  if (e.attempt) keys.push("attempt:" + e.attempt); if (e.task) keys.push("task:" + e.task); if (e.message) keys.push("message:" + e.message);
  for (const [re, kind] of [[/attempt=(\d+)/, "attempt"], [/job=(\d+)/, "task"], [/authorization=(\d+)/, "authorization"], [/case=(\d+)/, "case"], [/account=(\d+)/, "account"]]) { const m = re.exec(e.detail); if (m) keys.push(kind + ":" + m[1]); }
  const triple = /(\d+)\/(\d+)\/(\d+)\/[A-Za-z]/.exec(e.detail); if (triple) keys.push("task:" + triple[1], "attempt:" + triple[2], "case:" + triple[3]);
  return keys;
}
function messageKeys(m) {
  const keys = ["message:" + m.id]; if (m.attempt) keys.push("attempt:" + m.attempt); if (m.task) keys.push("task:" + m.task);
  const a = /^Attempt:(\d+)\//.exec(m.subject); if (a) keys.push("attempt:" + a[1]);
  return keys;
}
function buildCases() {
  cases = [];
  const eventIndex = new Map(), messageIndex = new Map(), push = (map, k, v) => (map.get(k) || map.set(k, []).get(k)).push(v);
  for (const e of events) for (const k of eventKeys(e)) push(eventIndex, k, e);
  for (const m of messages) for (const k of messageKeys(m)) push(messageIndex, k, m);
  const collect = (seeds, subject) => {
    const keys = new Set(seeds), found = new Set(), mail = new Set(); let frontier = [...seeds];
    for (let depth = 0; depth < 3 && frontier.length && found.size < 800; depth++) {
      const next = [], grow = k => { if (!keys.has(k) && CASE_KEYS.has(k.split(":")[0])) { keys.add(k); next.push(k); } };
      for (const k of frontier) {
        for (const e of eventIndex.get(k) || []) if (!found.has(e)) { found.add(e); eventKeys(e).forEach(grow); }
        for (const m of messageIndex.get(k) || []) if (!mail.has(m)) { mail.add(m); messageKeys(m).forEach(grow); }
      }
      frontier = next;
    }
    // The subject's own report names the machine it worked on; its physical work there belongs to the case.
    if (subject) for (const m of mail) {
      const machine = m.from === subject && /^Machine:(\d+)\//.exec(m.subject); if (!machine) continue;
      for (const e of subject.events) if (e.actor === subject.id && e.detail.startsWith("Machine:" + machine[1]) && e.tick <= m.submitted) found.add(e);
    }
    return { events: [...found].sort((a, b) => a.tick - b.tick || a.id - b.id), messages: [...mail].sort((a, b) => a.submitted - b.submitted || a.id - b.id) };
  };
  for (const k of T.custody || []) {
    const subject = citById.get(k.actorId); if (!subject) continue;
    addCase("Detention", subject.name, subject, collect([`authorization:${k.authorizationId}`, `account:${subject.account}`], subject), k.releaseTick, "auth:" + k.authorizationId);
  }
  for (const i of AUTH.incidents) {
    const worker = citById.get(i.actor), seeds = [`attempt:${i.attempt}`];
    if (i.replacement) seeds.push(`attempt:${i.replacement}`);
    for (const id of [i.responseMessage, i.replacementResult, i.patrolOrder, i.patrolResult]) if (id) seeds.push(`message:${id}`);
    addCase("Missing report", `${worker?.name || "actor " + i.actor} · attempt ${i.attempt}`, worker, collect(seeds, null), -1, "incident:" + i.id);
  }
  for (const f of AUTH.facility) {
    const seeds = [`task:${f.job}`]; if (f.attempt) seeds.push(`attempt:${f.attempt}`);
    for (const m of messages) if (m.subject === f.subject && m.submitted >= f.opened - 1 && (f.closed < 0 || m.submitted <= f.closed)) seeds.push(`message:${m.id}`);
    addCase("Facility task", `${f.subject} · job ${f.job}`, null, collect(seeds, null), -1, "facility:" + f.job);
  }
  for (const lost of events.filter(e => e.type === "CardLost")) {
    const c = citById.get(lost.actor); if (!c) continue;
    const done = c.events.find(e => e.type === "CredentialRecoveryCompleted" && e.actor === c.id && e.tick >= lost.tick), end = done ? done.tick : T.horizon;
    const found = c.events.filter(e => e.tick >= lost.tick && e.tick <= end && /Card|Credential|Recovery|Replacement/.test(e.type));
    const mail = messages.filter(m => m.submitted >= lost.tick && m.submitted <= end && (m.from === c || m.to === c) && /Card|Recovery|Credential|Replacement/.test(m.kind + m.recipientKind));
    addCase("Card recovery", c.name, c, { events: found, messages: mail }, -1, "card:" + lost.id);
  }
  cases.sort((a, b) => a.start - b.start);
}
function addCase(kind, title, subject, found, releaseTick, ref) {
  const { events: ev, messages: mail } = found; if (!ev.length) return;
  const acts = [];
  // Actions that directly caused case events show who physically did what.
  for (const e of ev) { const p = parentOf(nodes.get(e.id)); if (p && p.kind === "act") acts.push(p); }
  if (subject && releaseTick >= 0) {
    const resumed = subject.acts.find(a => a.req > releaseTick && WORK_KINDS.has(a.kind) && a.outcome === 1);
    if (resumed) acts.push({ ...nodes.get(resumed.reqEvent), resumed: true });
  }
  const ticks = [...ev.map(e => e.tick), ...mail.map(m => m.delivered >= 0 ? m.delivered : m.submitted), ...acts.map(a => a.a.end >= 0 ? a.a.end : a.tick)];
  const keyOf = e => e.actor ? e.actor : WORLD_EVENTS.has(e.type) ? "S" : "A";
  const touches = [...ev.map(e => ({ t: e.tick, k: keyOf(e) })), ...acts.map(a => ({ t: a.tick, k: a.actor })),
    ...mail.flatMap(m => [{ t: m.submitted, k: m.fromAuthority ? "A" : m.from?.id }, { t: m.delivered, k: m.toAuthority ? "A" : m.to?.id }])].filter(x => x.k).sort((a, b) => a.t - b.t);
  // At most twelve lanes: the subject, the authority, the station and the most involved citizens; the rest share one.
  const counts = new Map(); for (const x of touches) counts.set(x.k, (counts.get(x.k) || 0) + 1);
  const keep = new Set([...counts.keys()].filter(k => typeof k !== "number")); if (subject) keep.add(subject.id);
  for (const [k] of [...counts].filter(([k]) => typeof k === "number" && !keep.has(k)).sort((a, b) => b[1] - a[1])) if (keep.size < 11 || counts.size <= 12) keep.add(k);
  const lanes = [], laneOf = new Map(), lane = key => {
    if (!keep.has(key)) { if (!laneOf.has("O")) { laneOf.set("O", lanes.length); lanes.push({ key: "O", label: "Other citizens", members: 0 }); } const i = laneOf.get("O"); if (!laneOf.has(key)) { laneOf.set(key, i); lanes[i].members++; } return i; }
    if (!laneOf.has(key)) { laneOf.set(key, lanes.length); lanes.push(key === "A" ? { key, label: "Station authority" } : key === "S" ? { key, label: "Station" } : { key, c: citById.get(key), label: citById.get(key)?.name || "actor " + key }); }
    return laneOf.get(key);
  };
  if (subject) lane(subject.id);
  for (const x of touches) lane(x.k);
  const others = lanes.find(l => l.key === "O"); if (others) others.label = `Other citizens (${others.members})`;
  const milestones = ev.filter(e => MILESTONES[e.type]).map(e => ({ tick: e.tick, label: MILESTONES[e.type], node: e.id, lane: lane(keyOf(e)), e }));
  for (const a of acts.filter(a => a.resumed)) milestones.push({ tick: a.tick, label: "Resumed work", node: a.id, lane: lane(a.actor), act: true });
  milestones.sort((a, b) => a.tick - b.tick);
  cases.push({ id: cases.length + 1, ref, kind, title, subject, events: ev, messages: mail, acts, lanes, laneOf, keyOf, milestones, start: Math.min(...ticks), end: Math.max(...ticks) });
}
function showCase(k) {
  openCase = k; caseView = k ? fitCaseView(k) : null;
  $("caseWrap").hidden = !k; $("stage").classList.toggle("case-open", !!k);
  if (k) { $("caseTitle").textContent = `${k.kind} · ${k.title}`; $("caseSub").textContent = `${clockText(k.start)} → ${clockText(k.end)} · ${k.lanes.length} participants · ${k.events.length} events · ${k.messages.length} messages`; }
  document.querySelectorAll(".case-button").forEach(b => b.classList.toggle("selected", +b.dataset.case === k?.id));
  stripCache = null; dossierSig = ""; renderDossier(true); dirty = true;
}
// One lane per participant: custody bands and faint activity behind, causing actions as bars,
// events as marks with milestone labels, messages as lines from sender to recipient.
const caseCanvas = $("caseCanvas"), cctx = caseCanvas.getContext("2d");
function caseGeometry() {
  const r = caseCanvas.getBoundingClientRect(), left = 150, top = 26, k = openCase, v = caseView;
  const laneH = clamp((r.height - top - 6) / k.lanes.length, 20, 48), plotW = Math.max(20, r.width - left - 12);
  return { r, left, top, laneH, plotW, x: t => left + (t - v.t0) / (v.t1 - v.t0) * plotW, t: px => v.t0 + (px - left) / plotW * (v.t1 - v.t0), y: i => top + i * laneH };
}
function drawCase() {
  if (!openCase) return;
  resizeCanvas(caseCanvas);
  const dpr = window.devicePixelRatio || 1, g = caseGeometry(), { r, left, top, laneH } = g, k = openCase, v = caseView, W = r.width, H = r.height;
  cctx.setTransform(dpr, 0, 0, dpr, 0, 0); cctx.clearRect(0, 0, W, H); caseHits = [];
  const steps = [10, 30, 60, 150, 300, 600, 1500, 3000, 6000, 12000, 36000, 72000, 180000];
  const step = steps.find(s => s / (v.t1 - v.t0) * g.plotW >= 92) || steps[steps.length - 1];
  cctx.font = "10px ui-monospace, monospace"; cctx.textBaseline = "middle"; cctx.textAlign = "left";
  for (let t = Math.ceil(v.t0 / step) * step; t <= v.t1; t += step) { const px = g.x(t); cctx.fillStyle = COL.line; cctx.fillRect(px, top - 6, 1, H - top + 6); cctx.fillStyle = COL.muted; cctx.fillText(clockText(t).replace("Day ", "D"), px + 3, 11); }
  k.lanes.forEach((ln, i) => {
    const y = g.y(i);
    if (i % 2 === 0) { cctx.fillStyle = "rgba(255,255,255,0.025)"; cctx.fillRect(0, y, W, laneH); }
    cctx.textAlign = "right"; cctx.fillStyle = ln.key === "A" ? COL.text : COL.text2; cctx.font = `${ln.c && ln.c === k.subject ? "600 " : ""}12px system-ui`;
    cctx.fillText(ln.label, left - 10, y + laneH / 2 - (ln.c && laneH > 28 ? 6 : 0), left - 16);
    if (ln.c && laneH > 28) { cctx.font = "10px system-ui"; cctx.fillStyle = COL.muted; cctx.fillText(ln.c === k.subject ? ln.c.role + " · subject" : ln.c.role, left - 10, y + laneH / 2 + 7); }
    if (!ln.c) return;
    cctx.save(); cctx.beginPath(); cctx.rect(left, y, g.plotW, laneH); cctx.clip();
    for (const kc of ln.c.custody) { const a = Math.max(kc.from, v.t0), b = Math.min(kc.to, v.t1); if (b <= a) continue; cctx.fillStyle = kc.kind === "guard" ? "rgba(201, 133, 0, 0.16)" : "rgba(208, 59, 59, 0.2)"; cctx.fillRect(g.x(a), y + 1, g.x(b) - g.x(a), laneH - 2); }
    cctx.globalAlpha = 0.5;
    for (let j = Math.max(0, upper(ln.c.actReqs, v.t0) - 4); j < ln.c.acts.length && ln.c.acts[j].req <= v.t1; j++) {
      const a = ln.c.acts[j]; if (a.kind === "Move") continue;
      const end = a.end >= 0 ? a.end : T.horizon, began = a.began >= 0 ? a.began : a.req; if (end < v.t0) continue;
      cctx.fillStyle = COL[actInfo(a.kind)[0]] || COL.idle; cctx.fillRect(g.x(began), y + laneH - 6, Math.max(1, g.x(end) - g.x(began)), 4);
    }
    for (let j = Math.max(0, upper(ln.c.moveDeps, v.t0) - 1); j < ln.c.moves.length && ln.c.moves[j].dep <= v.t1; j++) { const m = ln.c.moves[j]; cctx.fillStyle = COL.travel; cctx.fillRect(g.x(m.dep), y + laneH - 6, Math.max(1, g.x(m.arr) - g.x(m.dep)), 4); }
    cctx.globalAlpha = 1; cctx.restore();
  });
  const mid = i => g.y(i) + laneH / 2;
  for (const m of k.messages) {
    const from = k.laneOf.get(m.fromAuthority ? "A" : m.from?.id), to = k.laneOf.get(m.toAuthority ? "A" : m.to?.id); if (from == null || to == null) continue;
    const t1 = m.delivered >= 0 ? m.delivered : m.submitted; if (t1 < v.t0 && (m.read < 0 || m.read < v.t0) || m.submitted > v.t1) continue;
    const ax = g.x(m.submitted), ay = mid(from), bx = Math.max(g.x(t1), ax + 2), by = mid(to), colour = m.fromAuthority || m.institutional ? COL.inst : COL.text;
    cctx.strokeStyle = colour; cctx.globalAlpha = 0.75; cctx.lineWidth = 1.2; cctx.beginPath(); cctx.moveTo(ax, ay); cctx.lineTo(bx, by); cctx.stroke();
    const ang = Math.atan2(by - ay, bx - ax); cctx.fillStyle = colour; cctx.beginPath(); cctx.moveTo(bx, by); cctx.lineTo(bx - 7 * Math.cos(ang - 0.4), by - 7 * Math.sin(ang - 0.4)); cctx.lineTo(bx - 7 * Math.cos(ang + 0.4), by - 7 * Math.sin(ang + 0.4)); cctx.fill();
    if (m.read >= 0 && !m.toAuthority) { const rx = g.x(m.read); cctx.setLineDash([3, 3]); cctx.beginPath(); cctx.moveTo(bx, by); cctx.lineTo(rx, by); cctx.stroke(); cctx.setLineDash([]); cctx.beginPath(); cctx.arc(rx, by, 3.5, 0, Math.PI * 2); cctx.lineWidth = 1.5; cctx.stroke(); }
    cctx.globalAlpha = 1;
    const first = firstOfMessage.get(m.id);
    caseHits.push({ x: (ax + bx) / 2, y: (ay + by) / 2, node: first?.id, tick: m.submitted, title: `${words(m.kind)} · message ${m.id}`, sub: `${m.fromAuthority ? "Authority" : m.from?.name || "?"} → ${m.toAuthority ? "Authority" : m.to?.name || m.recipientKind}${m.subject ? " · " + m.subject + (m.claim ? " = " + m.claim : "") : ""} · delivered ${m.delivered >= 0 ? clockText(m.delivered) : "never"}${m.read >= 0 ? " · read " + clockText(m.read) : ""}` });
  }
  for (const a of k.acts) {
    const i = k.laneOf.get(a.actor); if (i == null) continue;
    const began = a.a.began >= 0 ? a.a.began : a.a.req, end = a.a.end >= 0 ? a.a.end : T.horizon; if (end < v.t0 || began > v.t1) continue;
    const y = g.y(i), [fam, glyph, label] = actInfo(a.a.kind);
    cctx.fillStyle = COL[fam] || COL.idle; cctx.fillRect(g.x(began), y + laneH * 0.3, Math.max(3, g.x(end) - g.x(began)), laneH * 0.28);
    caseHits.push({ x: g.x(began), y: y + laneH * 0.44, node: a.id, tick: began, title: `${a.c.name}: ${label.toLowerCase()}${a.resumed ? " (resumed work)" : ""}`, sub: `${a.a.origin} · ${clockText(began)} → ${a.a.end >= 0 ? clockText(a.a.end) : "open"}` });
  }
  const labelEnd = new Map();
  for (const e of k.events) {
    const i = k.laneOf.get(k.keyOf(e)); if (i == null || e.tick < v.t0 || e.tick > v.t1) continue;
    const px = g.x(e.tick), py = mid(i), milestone = MILESTONES[e.type];
    cctx.beginPath(); cctx.arc(px, py, milestone ? 4.5 : 2.6, 0, Math.PI * 2); cctx.fillStyle = CATS[e.cat].color; cctx.fill();
    if (milestone) { cctx.strokeStyle = COL.surface1; cctx.lineWidth = 1.5; cctx.stroke(); }
    if (e.id === focusId) { cctx.beginPath(); cctx.arc(px, py, 8, 0, Math.PI * 2); cctx.strokeStyle = COL.accent; cctx.lineWidth = 2; cctx.stroke(); }
    if (milestone && laneH >= 24) {
      cctx.font = "10px system-ui"; const w = cctx.measureText(milestone).width;
      if (px - 2 > (labelEnd.get(i) ?? -1)) { cctx.fillStyle = COL.text; cctx.textAlign = "left"; cctx.fillText(milestone, px + 6, py - laneH * 0.28); labelEnd.set(i, px + 6 + w); }
    }
    caseHits.push({ x: px, y: py, node: e.id, tick: e.tick, title: words(e.type), sub: nodeText(nodes.get(e.id)).sub, milestone: !!milestone });
  }
  // Milestones outside the view as labelled arrows at the edges.
  const edge = new Map();
  for (const m of k.milestones) {
    if (m.tick >= v.t0 && m.tick <= v.t1) continue;
    const right = m.tick > v.t1, key = m.lane + (right ? "r" : "l"); if (edge.has(key) && (right ? edge.get(key) <= m.tick : edge.get(key) >= m.tick)) continue; edge.set(key, m.tick);
  }
  cctx.font = "10px system-ui";
  for (const [key, t] of edge) {
    const right = key.endsWith("r"), i = parseInt(key, 10), m = k.milestones.find(x => x.tick === t && x.lane === i), y = mid(i), text = right ? `${m.label} · ${clockText(t).replace("Day ", "D")} ▶` : `◀ ${m.label} · ${clockText(t).replace("Day ", "D")}`;
    cctx.fillStyle = COL.accent; cctx.textAlign = right ? "right" : "left"; cctx.fillText(text, right ? W - 6 : left + 4, y + laneH * 0.3);
    const w = cctx.measureText(text).width; caseHits.push({ x: right ? W - 6 - w / 2 : left + 4 + w / 2, y: y + laneH * 0.3, node: m.node, tick: t, title: m.label, sub: "outside the view; click to jump", milestone: true });
  }
  const px = g.x(tick);
  if (px >= left && px <= W) { cctx.fillStyle = COL.text; cctx.fillRect(px - 0.75, top - 6, 1.5, H - top + 6); }
  else { cctx.fillStyle = COL.accent; cctx.font = "11px system-ui"; cctx.textAlign = px < left ? "left" : "right"; cctx.fillText(px < left ? "◀ now" : "now ▶", px < left ? left + 4 : W - 6, 11); }
}
function caseHitAt(px, py) {
  let best = null, bestD = 9;
  for (const h of caseHits) { const d = Math.hypot(h.x - px, h.y - py) - (h.milestone ? 2 : 0); if (d < bestD) { best = h; bestD = d; } }
  return best;
}
// A summary of the open case in the dossier: who took part and how long each stage waited.
function caseSection() {
  const k = openCase, s = section(), head = el("div", "chain-head"), close = el("button", "close", "×");
  close.type = "button"; close.title = "Close the case (Esc)"; close.addEventListener("click", () => showCase(null));
  head.append(el("h2", null, `Case · ${k.kind}`), close); s.append(head);
  s.append(el("p", "hint", `${k.title} · ${clockText(k.start)} → ${clockText(k.end)} (${durationText(k.end - k.start)})`));
  const chips = el("div", "chips");
  for (const ln of k.lanes) {
    const b = el("button", (ln.c && ln.c === selected) || (ln.key === "A" && authoritySelected) ? "on" : "", ln.label); b.type = "button";
    b.addEventListener("click", () => { if (ln.c) select(ln.c); else if (ln.key === "A") selectAuthority(); }); chips.append(b);
  }
  s.append(chips);
  const rows = el("div", "rows"); let previous = null;
  for (const m of k.milestones) {
    const gap = previous ? m.tick - previous.tick : 0, r = el("div", "row" + (m.tick > tick ? " future" : "") + (m.node === focusId ? " now" : ""));
    const who = k.lanes[m.lane]?.label || "", what = el("span", "what", m.label); what.append(el("small", null, who));
    r.append(el("span", "t", clockText(m.tick).replace("Day ", "D")), el("span", "muted", "•"), what, el("span", "end" + (gap > (T.dayTicks ? T.dayTicks / 12 : 3000) ? " warn" : ""), previous ? "+" + durationText(gap) : ""));
    r.title = gap ? `Waited ${durationText(gap)} after ${previous.label.toLowerCase()}. Click for the cause chain.` : "Click for the cause chain.";
    r.addEventListener("click", () => focusNode(m.node, true)); rows.append(r); previous = m;
  }
  s.append(rows);
  return s;
}
let caseDrag = null;
caseCanvas.addEventListener("mousedown", e => { if (openCase) caseDrag = { x: e.clientX, t0: caseView.t0, t1: caseView.t1, moved: false }; });
window.addEventListener("mousemove", e => {
  if (!caseDrag) return;
  const dx = e.clientX - caseDrag.x; if (Math.abs(dx) > 3) caseDrag.moved = true; if (!caseDrag.moved) return;
  const g = caseGeometry(), shift = dx / g.plotW * (caseDrag.t1 - caseDrag.t0);
  caseView = { t0: caseDrag.t0 - shift, t1: caseDrag.t1 - shift }; dirty = true;
});
window.addEventListener("mouseup", e => {
  if (caseDrag && !caseDrag.moved && e.target === caseCanvas) {
    const r = caseCanvas.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, hit = caseHitAt(px, py), g = caseGeometry();
    if (hit?.node) focusNode(hit.node, true); else if (hit) setTick(hit.tick, true); else if (px >= g.left) setTick(Math.round(g.t(px)), true);
  }
  caseDrag = null;
});
caseCanvas.addEventListener("mousemove", e => {
  if (!openCase || caseDrag?.moved) return;
  const r = caseCanvas.getBoundingClientRect(), hit = caseHitAt(e.clientX - r.left, e.clientY - r.top), tip = $("caseTip");
  if (!hit) { tip.hidden = true; return; }
  tip.replaceChildren(el("strong", null, hit.title), el("div", null, `${clockText(hit.tick)}${hit.sub ? " · " + hit.sub : ""}`));
  tip.hidden = false; tip.style.left = `${Math.min(r.width - 300, e.clientX - r.left + 12)}px`; tip.style.top = `${Math.max(0, e.clientY - r.top - 50)}px`;
});
caseCanvas.addEventListener("mouseleave", () => { $("caseTip").hidden = true; });
caseCanvas.addEventListener("wheel", e => {
  if (!openCase) return; e.preventDefault();
  const r = caseCanvas.getBoundingClientRect(), g = caseGeometry(), at = g.t(e.clientX - r.left), k = Math.exp(e.deltaY * 0.0015);
  const span = clamp((caseView.t1 - caseView.t0) * k, 30, T.horizon - T.startTick);
  const f = clamp((at - caseView.t0) / (caseView.t1 - caseView.t0), 0, 1);
  caseView = { t0: at - span * f, t1: at - span * f + span }; dirty = true;
}, { passive: false });
caseCanvas.addEventListener("dblclick", () => { if (openCase) { caseView = fitCaseView(openCase); dirty = true; } });
$("caseFit").addEventListener("click", () => { if (openCase) { caseView = fitCaseView(openCase); dirty = true; } });
$("caseClose").addEventListener("click", () => showCase(null));
// Fit the milestones; later stragglers (re-issued orders read days later) stay reachable by scrolling.
function fitCaseView(k) {
  const ms = k.milestones; let a = ms.length ? ms[0].tick : k.start, b = ms.length ? ms[ms.length - 1].tick : k.end;
  // A long quiet tail (work resumed days later) would squeeze the story: fit the part before the largest gap.
  let gap = 0, cut = -1; for (let i = 1; i < ms.length; i++) if (ms[i].tick - ms[i - 1].tick > gap) { gap = ms[i].tick - ms[i - 1].tick; cut = i; }
  if (cut > 1 && gap > 0.6 * (b - a)) b = ms[cut - 1].tick;
  const pad = Math.max(60, (b - a) * 0.05);
  return { t0: Math.max(T.startTick, a - pad), t1: Math.min(T.horizon, Math.max(a + 120, b + pad)) };
}
function casesFor(c) { return cases.filter(k => k.laneOf.has(c.id)); }
const placeKey = (kind, id) => (NEED_PLACE_KINDS.has(kind) ? "svc" : kind) + ":" + id;
// Exact status (2 working, 1 disconnected, 0 down) from recorded changes; frame samples otherwise.
function placeUp(p, t) { if (p.changes.length) { const i = upper(p.changeTicks, t) - 1; return i >= 0 ? p.changes[i][1] : p.changes[0][1]; } return p.state.up[frameAt(t)]; }
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
  // The band above the rooms holds the authority hub that messages travel to and from.
  const W = 1000, gap = 26, margin = 20, band = 96, aspect = mapSize.w > 0 ? mapSize.h / mapSize.w : 0.5;
  const cellW = (W - margin * 2 - gap * (cols - 1)) / cols;
  const cellH = clamp((W * aspect - margin * 2 - band - gap * (rowsN - 1)) / rowsN, cellW * 1.1, cellW * 3);
  const H = margin * 2 + band + rowsN * cellH + gap * (rowsN - 1);
  for (const r of deckRooms) layoutRoom(r, margin + (r.position[0] - minX) * (cellW + gap), margin + band + (r.position[2] - minZ) * (cellH + gap), cellW, cellH);
  layoutDeck = { deck, w: W, h: H, rooms: deckRooms, gap, hub: { x: W / 2 - 180, y: margin, w: 360, h: 64 } };
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
  const believed = beliefsAt(tick);
  if ($("showBeliefs").checked) drawBeliefs(believed);
  drawHub(believed);
  drawTrail();
  // Citizens: queued ones are hollow, rings show the most urgent need band, red frame = custody.
  const ids = $("showIds").checked, showMail = $("showMail").checked;
  for (const c of cits) {
    if (!c.disp) continue;
    const st = c.state, r = c.target.r, fill = COL[st.family] || COL.idle, sel = selected === c, hov = hovered === c;
    const needs = needsAt(c, tick); let worst = 0; needs.forEach((v, k) => { worst = Math.max(worst, band(needDefs[k], v)); });
    mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r, 0, Math.PI * 2);
    if (st.waiting) { mctx.fillStyle = COL.surface1; mctx.fill(); mctx.strokeStyle = fill; mctx.lineWidth = r * 0.35; mctx.stroke(); }
    else { mctx.fillStyle = fill; mctx.fill(); mctx.strokeStyle = COL.surface0; mctx.lineWidth = Math.max(0.6, r * 0.12); mctx.stroke(); }
    if (worst > 0) { mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r * 1.38, 0, Math.PI * 2); mctx.strokeStyle = BAND[worst].color; mctx.lineWidth = r * (worst === 3 ? 0.42 : 0.26); mctx.stroke(); }
    if (st.custody) { const s = r * 1.75; mctx.strokeStyle = st.custody.kind === "guard" ? COL.inst : COL.critical; mctx.lineWidth = r * 0.25; mctx.strokeRect(c.disp.x - s, c.disp.y - s, s * 2, s * 2); }
    if (chainActors.has(c.id)) { mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r * 2.6, 0, Math.PI * 2); mctx.setLineDash([r * 0.5, r * 0.4]); mctx.strokeStyle = COL.accent; mctx.lineWidth = r * 0.2; mctx.stroke(); mctx.setLineDash([]); }
    if (showMail && c.mail.some(m => m.delivered <= tick && (m.read < 0 || m.read > tick))) {
      // Delivered, not yet read: an order or reply waiting in the mailbox.
      const ex = c.disp.x + r * 1.15, ey = c.disp.y - r * 1.55, ew = r * 1.2, eh = r * 0.85;
      mctx.fillStyle = COL.inst; mctx.fillRect(ex, ey, ew, eh); mctx.strokeStyle = COL.surface0; mctx.lineWidth = r * 0.12;
      mctx.beginPath(); mctx.moveTo(ex, ey); mctx.lineTo(ex + ew / 2, ey + eh * 0.55); mctx.lineTo(ex + ew, ey); mctx.stroke();
    }
    const label = ids ? String(c.id) : st.glyph;
    if (label && r * view.scale >= 5) { mctx.fillStyle = st.waiting ? COL.text : COL.surface0; mctx.font = `700 ${r * (label.length > 1 ? 0.95 : 1.15)}px ui-monospace, monospace`; mctx.textAlign = "center"; mctx.textBaseline = "middle"; mctx.fillText(label, c.disp.x, c.disp.y + r * 0.05); }
    if (sel || hov) {
      mctx.beginPath(); mctx.arc(c.disp.x, c.disp.y, r * 2.1, 0, Math.PI * 2); mctx.strokeStyle = sel ? COL.text : COL.text2; mctx.lineWidth = Math.max(1.2 / view.scale, r * 0.16); mctx.stroke();
      if (sel) { mctx.fillStyle = COL.text; mctx.font = `600 ${r * 1.25}px system-ui`; mctx.textAlign = "center"; mctx.textBaseline = "bottom"; mctx.fillText(c.name, c.disp.x, c.disp.y - r * 2.3); }
    }
  }
  if (showMail) drawMessages();
}
// The authority as a place on the map: its workload now and how many of its beliefs are wrong.
function drawHub(believed) {
  const h = layoutDeck.hub, F = AUTH.frames, fi = frameAt(tick), at = k => (F[k] ? F[k][fi] : 0) || 0;
  let wrong = 0, stale = 0; for (const b of believed.values()) { const s = beliefCheck(b, tick).state; if (s === "wrong") wrong++; else if (s === "stale") stale++; }
  roundRect(mctx, h.x, h.y, h.w, h.h, 12); mctx.fillStyle = COL.surface2; mctx.fill();
  mctx.strokeStyle = authoritySelected ? COL.text : authorityInChain ? COL.accent : hubHovered ? COL.text2 : COL.lineStrong; mctx.lineWidth = (authoritySelected || authorityInChain ? 2.2 : 1) / view.scale;
  if (authorityInChain && !authoritySelected) mctx.setLineDash([6, 4]); mctx.stroke(); mctx.setLineDash([]);
  mctx.textBaseline = "middle"; mctx.textAlign = "left"; mctx.fillStyle = COL.text; mctx.font = "600 17px system-ui"; mctx.fillText("Station authority", h.x + 16, h.y + 20);
  mctx.fillStyle = COL.text2; mctx.font = "12px ui-monospace, monospace";
  mctx.fillText(`${at("openExpectations")} awaiting reports · ${at("overdueExpectations")} overdue · ${at("openIncidents")} incidents · ${at("openFacilityTasks")} facility tasks`, h.x + 16, h.y + 44, h.w - 32);
  mctx.textAlign = "right"; mctx.font = "600 12px system-ui";
  mctx.fillStyle = wrong ? COL.critical : COL.muted; mctx.fillText(wrong ? `${wrong} wrong belief${wrong > 1 ? "s" : ""}` : `${believed.size} beliefs`, h.x + h.w - 16, h.y + 20);
  if (stale) { mctx.fillStyle = COL.muted; mctx.font = "11px system-ui"; mctx.fillText(`${stale} stale`, h.x + h.w - 16 - (wrong ? 120 : 80), h.y + 20); }
}
// What the authority believes about rooms, machines and people, drawn over the truth.
function drawBeliefs(believed) {
  for (const room of layoutDeck.rooms) {
    const u = room.u, { x, y, w } = room.rect, dirt = believed.get(`Tile:${room.id}/Dirt`);
    if (dirt) {
      const check = beliefCheck(dirt, tick);
      mctx.fillStyle = check.state === "right" ? COL.muted : COL.serious; mctx.font = `${u * 0.62}px ui-monospace, monospace`; mctx.textAlign = "right"; mctx.textBaseline = "middle";
      mctx.fillText(`authority: dirt ${dirt.value}`, x + w - u * 0.7, y + u * 1.75, w * 0.7);
    }
    for (const p of places.filter(q => q.room === room && q.kind === "Machine" && q.rect)) {
      const b = believed.get(`Machine:${p.id}/Operational`); if (!b) continue;
      // Corner badge: what the authority believes (check = working, cross = down); red when that is wrong.
      const check = beliefCheck(b, tick), bx = p.rect.x + p.rect.w, by = p.rect.y, br = u * 0.48;
      mctx.beginPath(); mctx.arc(bx, by, br, 0, Math.PI * 2); mctx.fillStyle = check.state === "wrong" ? COL.critical : COL.surface3; mctx.fill();
      mctx.strokeStyle = COL.surface0; mctx.lineWidth = u * 0.1; mctx.stroke();
      mctx.fillStyle = COL.text; mctx.font = `700 ${u * 0.6}px system-ui`; mctx.textAlign = "center"; mctx.textBaseline = "middle";
      mctx.fillText(b.value === "true" ? "✓" : "✕", bx, by + u * 0.03);
      if (check.state === "wrong") { roundRect(mctx, p.rect.x - u * 0.15, p.rect.y - u * 0.15, p.rect.w + u * 0.3, p.rect.h + u * 0.3, u * 0.4); mctx.setLineDash([u * 0.4, u * 0.3]); mctx.strokeStyle = COL.critical; mctx.lineWidth = u * 0.14; mctx.stroke(); mctx.setLineDash([]); }
    }
  }
  for (const b of believed.values()) {
    const m = /^Actor:(\d+)\/Location$/.exec(b.key), at = /\((-?\d+), (-?\d+), (-?\d+)\)/.exec(b.value); if (!m || !at) continue;
    const room = roomByKey.get(`${at[1]},${at[2]},${at[3]}`), c = citById.get(+m[1]);
    if (!room?.rect || room.position[1] !== deck || !c || c.state?.loc.room === room) continue;
    const u = room.u, gx = room.floor.x + room.floor.w - u * 1.2, gy = room.floor.y + u * 1.2;
    mctx.beginPath(); mctx.arc(gx, gy, u * 0.55, 0, Math.PI * 2); mctx.setLineDash([u * 0.25, u * 0.2]); mctx.strokeStyle = COL.critical; mctx.lineWidth = u * 0.12; mctx.stroke(); mctx.setLineDash([]);
    mctx.fillStyle = COL.text2; mctx.font = `${u * 0.55}px ui-monospace, monospace`; mctx.textAlign = "center"; mctx.fillText(String(c.id), gx, gy);
  }
}
// Messages as pulses: reports climb to the authority, orders and decisions come back down.
// Real latency is a few ticks, so each pulse stays visible for about a second of playback.
function drawMessages() {
  const hub = layoutDeck.hub, hubPoint = { x: hub.x + hub.w / 2, y: hub.y + hub.h };
  const visible = clamp(+$("speed").value * 1000 / T.tickMs * 0.9, 30, 40000);
  const terminalPoint = id => { const p = placeByKey.get("Terminal:" + id); return p?.rect && p.room.position[1] === deck ? { x: p.rect.x + p.rect.w / 2, y: p.rect.y } : null; };
  const citizenPoint = c => c?.disp ? { x: c.disp.x, y: c.disp.y } : null;
  for (let i = upper(messageTicks, tick - visible - maxLatency - 1); i < messages.length && messages[i].submitted <= tick; i++) {
    const m = messages[i], span = Math.max(visible, m.delivered >= 0 ? m.delivered - m.submitted : 0), p = (tick - m.submitted) / span;
    if (p < 0 || p > 1) continue;
    const a = m.fromAuthority ? hubPoint : terminalPoint(m.terminal) || citizenPoint(m.from);
    const b = m.toAuthority ? hubPoint : citizenPoint(m.to); if (!a || !b) continue;
    const cx = (a.x + b.x) / 2, cy = Math.min(a.y, b.y) - 40, colour = m.fromAuthority || m.institutional ? COL.inst : COL.text;
    const point = s => ({ x: (1 - s) * (1 - s) * a.x + 2 * (1 - s) * s * cx + s * s * b.x, y: (1 - s) * (1 - s) * a.y + 2 * (1 - s) * s * cy + s * s * b.y });
    mctx.fillStyle = colour;
    // Constant size on screen whatever the zoom.
    for (let k = 4; k >= 0; k--) { const s = p - k * 0.035; if (s < 0) continue; const q = point(s); mctx.globalAlpha = k ? 0.12 + 0.12 * (4 - k) : 1; mctx.beginPath(); mctx.arc(q.x, q.y, (k ? 2.4 : 4) / view.scale, 0, Math.PI * 2); mctx.fill(); }
    mctx.globalAlpha = 1;
  }
}
function drawFixture(p, fi, u) {
  if (!p.rect) return;
  const { x, y, w, h } = p.rect, up = placeUp(p, tick), busy = p.state.busy[fi], queued = p.state.queue[fi], det = p.detentionFacilityId > 0 || p.kind === "Detention";
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
  label(g.rows.life_strip, authoritySelected ? "Authority work" : selected ? selected.name : "Select a citizen");
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
  // The selected citizen's whole life as activity bands, or the authority's open work.
  const row = g.rows.life_strip; c.fillStyle = COL.surface1; c.fillRect(g.left, row.y, g.plotW, row.h);
  if (authoritySelected) {
    const open = AUTH.frames.openExpectations || [], incidents = AUTH.frames.openIncidents || [], max = Math.max(1, ...open);
    c.beginPath(); c.moveTo(g.left, row.y + row.h);
    for (let i = 0; i < FT.length; i++) c.lineTo(g.x(FT[i]), row.y + row.h - row.h * (open[i] || 0) / max);
    c.lineTo(g.left + g.plotW, row.y + row.h); c.closePath(); c.globalAlpha = 0.85; c.fillStyle = COL.work; c.fill(); c.globalAlpha = 1;
    c.fillStyle = COL.critical; for (let i = 0; i + 1 < FT.length; i++) if (incidents[i] > 0) c.fillRect(g.x(FT[i]), row.y, Math.max(1, g.x(FT[i + 1]) - g.x(FT[i])), 3);
  }
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
  if (openCase) { const a = stripGeom.x(openCase.start), z = stripGeom.x(openCase.end); sctx.fillStyle = "rgba(127, 211, 196, 0.13)"; sctx.fillRect(a, 12, Math.max(2, z - a), stripGeom.h - 12); sctx.fillStyle = COL.accent; sctx.fillRect(a, 12, Math.max(2, z - a), 2); }
}

// ---------- people list ----------
let peopleRows = new Map(), authorityRow = null;
function buildPeople() {
  const host = $("people"); host.replaceChildren(); peopleRows = new Map();
  // The authority is pinned above the citizens: it is the station's central mind.
  const inst = el("div", "group"); inst.append(el("div", "group-title", "Institution"));
  const ab = el("button", "person"); ab.type = "button";
  const adot = el("span", "dot", "A"); adot.style.background = COL.text2;
  authorityRow = { b: ab, doing: el("span", "doing"), flag: el("span", "flag"), last: "" };
  ab.append(adot, el("span", "name", "Station authority"), authorityRow.flag, authorityRow.doing); ab.addEventListener("click", selectAuthority);
  inst.append(ab); host.append(inst);
  if (cases.length) {
    const group = el("div", "group"); group.append(el("div", "group-title", `Cases · ${cases.length}`));
    for (const k of cases) {
      const b = el("button", "person case-button"); b.type = "button"; b.dataset.case = String(k.id);
      const dot = el("span", "dot", k.kind[0]); dot.style.background = k.kind === "Detention" ? COL.critical : COL.text2;
      b.append(dot, el("span", "name", `${k.kind} · ${k.title}`), el("span", "flag"), el("span", "doing", `${clockText(k.start)} · ${k.lanes.length} involved`));
      b.addEventListener("click", () => showCase(openCase === k ? null : k)); group.append(b);
    }
    host.append(group);
  }
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
  if (authorityRow) {
    const F = AUTH.frames, fi = frameAt(tick), at = k => (F[k] ? F[k][fi] : 0) || 0;
    let wrong = 0; for (const b of beliefsAt(tick).values()) if (beliefCheck(b, tick).state === "wrong") wrong++;
    const sig = `${at("openExpectations")}|${at("overdueExpectations")}|${at("openIncidents")}|${wrong}|${authoritySelected}`;
    if (sig !== authorityRow.last) {
      authorityRow.last = sig;
      authorityRow.doing.textContent = `${at("openExpectations")} awaiting reports · ${at("openIncidents")} incidents`;
      authorityRow.flag.textContent = wrong ? `${wrong} WRONG` : at("overdueExpectations") ? "OVERDUE" : ""; authorityRow.flag.style.color = wrong || at("overdueExpectations") ? COL.critical : "";
      authorityRow.b.classList.toggle("selected", authoritySelected);
    }
  }
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
  selected = c; authoritySelected = false; stripCache = null; dossierSig = ""; renderDossier(true); dirty = true;
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
  const sig = (authoritySelected ? "A" : selected ? selected.id : 0) + "|" + focusId + "|" + (openCase?.id || 0) + "|" + Math.floor(tick / (playing ? 30 : 1)) + "|" + [...eventFilter].join(",");
  if (!force && sig === dossierSig) return;
  dossierSig = sig;
  const scroll = host.scrollTop;
  const chain = focusId ? chainSection() : null, kase = openCase ? caseSection() : null;
  host.replaceChildren(...(chain ? [chain] : []), ...(kase ? [kase] : []), ...(authoritySelected ? authorityDossier() : selected ? citizenDossier(selected) : stationOverview()));
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
    const problems = places.filter(p => p.room === r && (p.kind === "Terminal" || p.kind === "Machine") && placeUp(p, tick) !== 2).map(p => `${p.kind.toLowerCase()} ${p.id} ${p.kind === "Machine" ? "not operational" : "offline"}`);
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
  const theirs = casesFor(c);
  if (theirs.length) {
    const cs = section("Cases"), chips = el("div", "chips");
    for (const k of theirs) { const b = el("button", openCase === k ? "on" : "", `${k.kind}${k.subject === c ? "" : " · " + k.title}`); b.type = "button"; b.addEventListener("click", () => showCase(openCase === k ? null : k)); chips.append(b); }
    cs.append(chips); out.push(cs);
  }
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
    ...(() => {
      const unread = c.mail.filter(m => m.delivered <= tick && (m.read < 0 || m.read > tick)), last = [...c.mail].reverse().find(m => m.delivered <= tick);
      return [["Mail", unread.length ? `${unread.length} unread: ${unread.map(m => words(m.kind)).join(", ")}` : last ? "nothing unread" : "",
        last ? `last: ${words(last.kind)}${last.subject ? " · " + last.subject : ""} · delivered ${agoText(last.delivered)}${last.read >= 0 && last.read <= tick ? " · read " + agoText(last.read) : ""}` : ""]];
    })(),
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
    if (x.reqEvent === focusId) r.classList.add("now");
    r.addEventListener("click", () => focusNode(x.reqEvent, true)); hrows.append(r);
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
      const truth = truthAt(key, tick); if (truth && truth.value !== f.value) src.append(el("span", truth.info ? "muted" : "bad", truth.info ? ` · ${truth.value}` : `actual ${truth.value}`));
      row.append(src); det.append(row);
    }
    ms.append(det);
  }
  out.push(ms);
  return out;
}
// Physical truth for the few facts the export samples (dirt, machine and terminal status).
// The focused event with the recorded causes that led to it and what it led to in turn.
function chainSection() {
  const n = nodes.get(focusId); if (!n) return null;
  const s = section(), head = el("div", "chain-head"), close = el("button", "close", "×");
  close.type = "button"; close.title = "Close (Esc)"; close.addEventListener("click", () => focusNode(0, false));
  head.append(el("h2", null, "Cause chain"), close); s.append(head);
  const ancestors = [], seen = new Set([n.id]); let p = parentOf(n);
  while (p && ancestors.length < 14 && !seen.has(p.id)) { ancestors.push(p); seen.add(p.id); p = parentOf(p); }
  const list = el("div", "chain");
  if (p && !seen.has(p.id)) list.append(chainRow(p, "cause", "… earlier causes"));
  for (const a of ancestors.reverse()) list.append(chainRow(a, "cause"));
  list.append(chainRow(n, "focus"));
  const kids = childrenOf.get(n.id) || [];
  if (kids.length) {
    list.append(el("div", "chain-label", `Led to · ${kids.length}`));
    for (const k of kids.slice(0, 10)) list.append(chainRow(k, "effect"));
    if (kids.length > 10) list.append(el("div", "chain-label", `+ ${kids.length - 10} more`));
  }
  s.append(list);
  if (!ancestors.length && !p) s.append(el("p", "hint", "The trace records no cause for this. It began from a schedule, a need threshold, a roster or a disturbance."));
  return s;
}
function chainRow(n, role, label) {
  const r = el("div", `chain-row ${role}${n.tick > tick ? " future" : ""}`), sw = el("i"), t = nodeText(n), body = el("div");
  sw.style.background = CATS[nodeCategory(n)].color;
  body.append(el("strong", null, label || t.title), el("p", null, t.sub));
  if (role === "focus") {
    const c = citById.get(n.actor);
    if (c && c !== selected) { const b = el("button", "link-button", `Open ${c.name}`); b.type = "button"; b.addEventListener("click", ev => { ev.stopPropagation(); select(c); }); body.append(b); }
    if (!n.actor && n.kind === "event" && !WORLD_EVENTS.has(n.e.type) && !authoritySelected) { const b = el("button", "link-button", "Open authority"); b.type = "button"; b.addEventListener("click", ev => { ev.stopPropagation(); selectAuthority(); }); body.append(b); }
  }
  r.append(el("span", "t", clockText(n.tick).replace("Day ", "D")), sw, body);
  r.title = `#${n.id} · ${n.kind === "act" ? "action request" : n.kind === "learned" ? "knowledge change" : "event"}`;
  if (role !== "focus") r.addEventListener("click", () => focusNode(n.id, true));
  return r;
}
function authorityDossier() {
  const out = [], fi = frameAt(tick), F = AUTH.frames, at = k => (F[k] ? F[k][fi] : 0) || 0, believed = beliefsAt(tick);
  const checks = [...believed.values()].map(b => ({ b, ...beliefCheck(b, tick) }));
  const wrong = checks.filter(x => x.state === "wrong"), stale = checks.filter(x => x.state === "stale");
  // Identity.
  const head = section(), who = el("div", "who"), big = el("span", "big", "A"), text = el("div"), close = el("button", "close", "×");
  big.style.background = COL.text2;
  text.append(el("h3", null, "Station authority"), el("p", null, "Central institution: reads reports, keeps beliefs, assigns work, opens incidents, decides cases and detention. It knows only what messages tell it."));
  close.type = "button"; close.title = "Close (Esc)"; close.addEventListener("click", () => select(null));
  who.append(big, text, close); head.append(who); out.push(head);
  // Now.
  const now = section("Now"), grid = el("div", "stat-grid");
  const stat = (n, label, bad) => { const d = el("div", "stat"); const b = el("b", bad && n ? "bad" : null, String(n)); d.append(b, el("span", null, label)); return d; };
  grid.append(stat(at("openExpectations"), "Awaiting reports"), stat(at("overdueExpectations"), "Overdue", true), stat(at("openIncidents"), "Open incidents", true),
    stat(at("openFacilityTasks"), "Facility tasks"), stat(at("awaitingAcknowledgment"), "Orders unacknowledged"), stat(wrong.length, "Wrong beliefs", true));
  now.append(grid); out.push(now);
  // Workload over the whole run, one row per queue on its own scale.
  const ws = section("Workload over the run"), chart = document.createElement("canvas"); chart.className = "workload"; chart.title = "Click to jump";
  ws.append(chart); out.push(ws);
  const series = [["openExpectations", "Awaiting reports", COL.work], ["openIncidents", "Incidents", COL.critical], ["openFacilityTasks", "Facility tasks", COL.inst], ["awaitingAcknowledgment", "Unacknowledged", COL.travel]];
  requestAnimationFrame(() => drawWorkload(chart, series));
  chart.addEventListener("click", e => { const r = chart.getBoundingClientRect(), left = 110; setTick(T.startTick + clamp((e.clientX - r.left - left) / (r.width - left - 6), 0, 1) * (T.horizon - T.startTick), true); });
  // Beliefs that disagree with the physical truth.
  if (wrong.length || stale.length) {
    const bs = section("Beliefs that disagree with the station"), rows = el("div", "rows");
    for (const x of [...wrong, ...stale].slice(0, 24)) {
      const r = el("div", "row"), what = el("span", "what", x.b.key);
      what.append(el("small", x.state === "wrong" ? "bad" : null, `believes ${x.b.value} · actual ${x.truth.value}`));
      r.append(el("span", "t", clockText(x.b.at).replace("Day ", "D")), el("span", x.state === "wrong" ? "bad" : "muted", x.state === "wrong" ? "✕" : "~"), what, el("span", "end", x.b.message ? "msg " + x.b.message : x.b.source));
      r.title = `Believed since ${clockText(x.b.at)}${x.b.message ? " from message " + x.b.message : ""}. Click for the report that caused it.`;
      if (x.b.event) r.addEventListener("click", () => focusNode(x.b.event, true)); rows.append(r);
    }
    bs.append(rows); out.push(bs);
  }
  // Work the authority is waiting on at this tick.
  const os = section("Open work"), rows = el("div", "rows");
  const label = text => { const l = el("div", "chain-label", text); rows.append(l); };
  const open = AUTH.expectations.filter(x => x.opened <= tick && (x.closed < 0 || x.closed > tick)).sort((a, b) => a.deadline - b.deadline);
  label(`Awaiting reports · ${open.length}`);
  for (const x of open.slice(0, 12)) {
    const c = citById.get(x.actor), overdue = x.deadline >= 0 && tick > x.deadline, r = el("div", "row");
    const what = el("span", "what", `${c?.name || "actor " + x.actor} · ${words(x.task)}`); what.append(el("small", null, `attempt ${x.attempt}`));
    r.append(el("span", "t", clockText(x.opened).replace("Day ", "D")), el("span", overdue ? "bad" : "muted", overdue ? "!" : "·"), what, el("span", "end" + (overdue ? " bad" : ""), x.deadline >= 0 ? (overdue ? "overdue " : "due ") + agoText(x.deadline) : "no estimate"));
    r.title = `Expectation for attempt ${x.attempt}; final status ${x.status}${x.reason ? " (" + x.reason + ")" : ""}. Click to open ${c?.name || "the worker"}.`;
    if (c) r.addEventListener("click", () => select(c)); rows.append(r);
  }
  if (open.length > 12) label(`+ ${open.length - 12} more`);
  const incidents = AUTH.incidents.filter(i => i.opened <= tick && (i.closed < 0 || i.closed > tick));
  label(`Incidents · ${incidents.length}`);
  for (const i of incidents) {
    const c = citById.get(i.actor), r = el("div", "row"), what = el("span", "what", `${words(i.kind)} · ${c?.name || "actor " + i.actor}`);
    what.append(el("small", null, `response ${words(i.response)}${i.replacement ? " · replacement attempt " + i.replacement : ""}`));
    r.append(el("span", "t", clockText(i.opened).replace("Day ", "D")), el("span", "bad", "!"), what, el("span", "end", i.admitted >= 0 && i.admitted <= tick ? "admitted" : "waiting"));
    r.title = `Incident ${i.id} for attempt ${i.attempt}; resolution ${i.resolution}, worker status ${i.worker}.`;
    r.addEventListener("click", () => { const k = cases.find(x => x.ref === "incident:" + i.id); if (k) showCase(k); else setTick(i.opened, true); }); rows.append(r);
  }
  const tasks = AUTH.facility.filter(f => f.opened <= tick && (f.closed < 0 || f.closed > tick));
  label(`Facility tasks · ${tasks.length}`);
  for (const f of tasks) {
    const r = el("div", "row"), what = el("span", "what", f.subject); what.append(el("small", null, `${f.reports} reports in total${f.attempt ? " · attempt " + f.attempt : ""}`));
    r.append(el("span", "t", clockText(f.opened).replace("Day ", "D")), el("span", "muted", "·"), what, el("span", "end", agoText(f.opened)));
    r.title = `Facility task for job ${f.job}; final status ${f.status}${f.reason ? " (" + f.reason + ")" : ""}. Click to open the case.`;
    r.addEventListener("click", () => { const k = cases.find(x => x.ref === "facility:" + f.job); if (k) showCase(k); }); rows.append(r);
  }
  const custody = (T.custody || []).filter(k => k.escortStarted >= 0 && k.escortStarted <= tick && (k.releaseTick < 0 || k.releaseTick > tick));
  if (custody.length) {
    label(`Custody · ${custody.length}`);
    for (const k of custody) {
      const c = citById.get(k.actorId), r = el("div", "row"), phase = k.intakeTick >= 0 && k.intakeTick <= tick ? "detained" : "under escort";
      r.append(el("span", "t", clockText(k.escortStarted).replace("Day ", "D")), el("span", "bad", "■"), el("span", "what", `${c?.name || k.actorId} · ${phase}`), el("span", "end", k.releaseTick >= 0 ? "release " + agoText(k.releaseTick) : ""));
      r.addEventListener("click", () => { const kase = cases.find(x => x.ref === "auth:" + k.authorizationId); if (kase) showCase(kase); else if (c) select(c); }); rows.append(r);
    }
  }
  os.append(rows); out.push(os);
  // Decision log.
  const ds = section("Decisions and received evidence (latest first)"), chips = el("div", "chips");
  for (const [key, cat] of Object.entries(CATS)) {
    const b = el("button", eventFilter.has(key) ? "on" : "", cat.label); b.type = "button";
    b.addEventListener("click", () => { if (eventFilter.has(key)) eventFilter.delete(key); else eventFilter.add(key); renderDossier(true); });
    chips.append(b);
  }
  ds.append(chips);
  const log = [];
  for (let i = upper(AUTH.logTicks, tick) - 1; i >= 0 && log.length < 50; i--) if (eventFilter.has(AUTH.log[i].cat)) log.push(AUTH.log[i]);
  ds.append(...log.map(e => eventRow(e)));
  if (!log.length) ds.append(el("p", "hint", "No authority decisions in the selected categories yet."));
  out.push(ds);
  // Messages to and from the authority.
  const ms = section("Messages (latest first)"), mrows = el("div", "rows");
  const recent = []; for (let i = upper(messageTicks, tick) - 1; i >= 0 && recent.length < 30; i--) if (messages[i].toAuthority || messages[i].fromAuthority) recent.push(messages[i]);
  for (const m of recent) {
    const r = el("div", "row"), state = m.delivered < 0 || m.delivered > tick ? "in transit" : m.toAuthority ? "received" : m.read >= 0 && m.read <= tick ? "read" : "unread";
    const what = el("span", "what", `${m.fromAuthority ? "Authority" : m.from?.name || "account " + m.senderAccount} → ${m.toAuthority ? "Authority" : m.to?.name || m.recipientKind}`);
    what.append(el("small", null, `${words(m.kind)}${m.subject ? " · " + m.subject + (m.claim ? " = " + m.claim : "") : ""}`));
    r.append(el("span", "t", clockText(m.submitted).replace("Day ", "D")), el("span", "muted", m.toAuthority ? "↑" : "↓"), what, el("span", "end" + (state === "unread" ? " warn" : ""), state));
    const first = firstOfMessage.get(m.id); r.title = `Message ${m.id}${m.attempt ? " · attempt " + m.attempt : ""}. Click for its causes and effects.`;
    r.addEventListener("click", () => first ? focusNode(first.id, true) : setTick(m.submitted, true)); mrows.append(r);
  }
  if (!recent.length) mrows.append(el("p", "hint", "No messages yet."));
  ms.append(mrows); out.push(ms);
  // Every belief, grouped by subject, against the truth where the export can check it.
  const all = section("What the authority believes");
  all.append(el("p", "hint", `${believed.size} beliefs · ${wrong.length} wrong · ${stale.length} stale. Highlighted beliefs changed in the last ${durationText(T.dayTicks ? T.dayTicks / 12 : 3000)}.`));
  const groups = new Map(); for (const x of checks) { const kind = x.b.key.split(":")[0]; (groups.get(kind) || groups.set(kind, []).get(kind)).push(x); }
  for (const [kind, list] of [...groups].sort((a, b) => a[0].localeCompare(b[0]))) {
    const det = document.createElement("details"), key = "authority:" + kind;
    det.open = openGroups.has(key) ? openGroups.get(key) : list.length <= 12; det.addEventListener("toggle", () => openGroups.set(key, det.open));
    det.append(el("summary", null, `${kind} · ${list.length}`));
    list.sort((a, b) => b.b.at - a.b.at);
    for (const x of list.slice(0, 80)) {
      const row = el("div", "fact" + (tick - x.b.at < (T.dayTicks ? T.dayTicks / 12 : 3000) ? " fresh" : ""));
      row.append(el("span", "k", x.b.key.slice(kind.length + 1)), el("span", "v", x.b.value));
      const src = el("span", "src", `${x.b.message ? "message " + x.b.message : x.b.source} · ${agoText(x.b.at)}`);
      if (x.truth && x.truth.value !== x.b.value) src.append(el("span", x.truth.info ? "muted" : "bad", x.truth.info ? ` · ${x.truth.value}` : `actual ${x.truth.value}`));
      row.append(src); if (x.b.event) { row.style.cursor = "pointer"; row.addEventListener("click", () => focusNode(x.b.event, true)); }
      det.append(row);
    }
    all.append(det);
  }
  out.push(all);
  return out;
}
function drawWorkload(canvas, series) {
  const r = canvas.getBoundingClientRect(); if (!r.width) return;
  const dpr = window.devicePixelRatio || 1; canvas.width = Math.round(r.width * dpr); canvas.height = Math.round(r.height * dpr);
  const ctx = canvas.getContext("2d"); ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  const left = 110, w = r.width - left - 6, rowH = (r.height - 4) / series.length, n = FT.length, x = t => left + (t - T.startTick) / (T.horizon - T.startTick) * w;
  ctx.fillStyle = COL.surface2; ctx.fillRect(0, 0, r.width, r.height); ctx.font = "11px system-ui"; ctx.textBaseline = "middle";
  series.forEach(([key, label, colour], k) => {
    const data = AUTH.frames[key] || [], top = 2 + k * rowH, h = rowH - 4, max = Math.max(1, ...data);
    ctx.textAlign = "right"; ctx.fillStyle = COL.text2; ctx.fillText(`${label} (max ${max})`, left - 8, top + h / 2);
    ctx.fillStyle = COL.surface1; ctx.fillRect(left, top, w, h);
    ctx.beginPath(); ctx.moveTo(left, top + h);
    for (let i = 0; i < n; i++) ctx.lineTo(x(FT[i]), top + h - h * (data[i] || 0) / max);
    ctx.lineTo(left + w, top + h); ctx.closePath(); ctx.globalAlpha = 0.85; ctx.fillStyle = colour; ctx.fill(); ctx.globalAlpha = 1;
  });
  ctx.fillStyle = COL.text; ctx.fillRect(x(tick) - 0.75, 0, 1.5, r.height);
}
function eventRow(e) {
  const d = el("div", "event"), meta = el("div", "meta"), sw = el("i"); sw.style.background = CATS[e.cat].color;
  meta.append(sw, `${clockText(e.tick)} · ${agoText(e.tick)}${e.actor && e.actor !== selected?.id ? " · " + (citById.get(e.actor)?.name || "actor " + e.actor) : ""}`);
  d.append(meta, el("strong", null, words(e.type)));
  const bits = [e.detail, e.message ? "message " + e.message : "", e.terminal ? "terminal " + e.terminal : "", e.attempt ? "attempt " + e.attempt : "", e.tile ? "room #" + e.tile : ""].filter(Boolean);
  if (bits.length) d.append(el("p", null, bits.join(" · ")));
  d.title = `Event #${e.id} · click to see what caused it and what it led to`;
  if (e.id === focusId) d.classList.add("focused");
  d.addEventListener("click", () => focusNode(e.id, true));
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
  const pulse = (col, text) => { const d = el("span", "pulse"); d.style.background = col; item(d, text); };
  pulse(COL.text, "Report to authority"); pulse(COL.inst, "Order or decision");
  const badge = el("span", "badge-key", "✓"); item(badge, "Authority believes (Beliefs)");
  item(el("span", "envelope"), "Unread mail");
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
  if (drag && !drag.moved && e.target === map) {
    const r = map.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, c = citizenAt(px, py);
    if (c) select(c); else if (hubAt(px, py)) selectAuthority();
  }
  drag = null; map.classList.remove("dragging");
});
map.addEventListener("mousemove", e => {
  if (!T || drag?.moved) return;
  const r = map.getBoundingClientRect(), c = citizenAt(e.clientX - r.left, e.clientY - r.top), overHub = !c && hubAt(e.clientX - r.left, e.clientY - r.top);
  if (c !== hovered || overHub !== hubHovered) { hovered = c; hubHovered = overHub; dirty = true; }
  const tip = $("tip");
  if (overHub) {
    tip.replaceChildren(el("strong", null, "Station authority"), el("div", null, "Click for its beliefs, open work, decisions and messages."));
    tip.hidden = false; tip.style.left = `${Math.min(r.width - 220, e.clientX - r.left + 14)}px`; tip.style.top = `${e.clientY - r.top + 14}px`; return;
  }
  if (!c) { tip.hidden = true; return; }
  const st = c.state; tip.replaceChildren(el("strong", null, `${c.name} · ${c.role}`), el("div", null, `${st.custody ? st.custody.label + " · " : ""}${st.label}`), el("div", null, st.loc.room ? `${st.loc.room.definition} #${st.loc.room.id}` : ""));
  tip.hidden = false; tip.style.left = `${Math.min(r.width - 220, e.clientX - r.left + 14)}px`; tip.style.top = `${e.clientY - r.top + 14}px`;
});
map.addEventListener("mouseleave", () => { hovered = null; hubHovered = false; $("tip").hidden = true; dirty = true; });
function hubAt(px, py) {
  const h = layoutDeck?.hub; if (!h) return false;
  const x = (px - view.ox) / view.scale, y = (py - view.oy) / view.scale;
  return x >= h.x && x <= h.x + h.w && y >= h.y && y <= h.y + h.h;
}
map.addEventListener("wheel", e => {
  if (!T) return; e.preventDefault();
  const r = map.getBoundingClientRect(), px = e.clientX - r.left, py = e.clientY - r.top, k = Math.exp(-e.deltaY * 0.0015);
  const base = Math.min(r.width / layoutDeck.w, r.height / layoutDeck.h), scale = clamp(view.scale * k, base * 0.6, base * 10);
  view.ox = px - (px - view.ox) * scale / view.scale; view.oy = py - (py - view.oy) * scale / view.scale; view.scale = scale; dirty = true;
}, { passive: false });
map.addEventListener("dblclick", fit);
$("fit").addEventListener("click", fit);
$("deck").addEventListener("change", e => { deck = +e.target.value; layoutDeck = null; fit(); });
for (const id of ["showTrail", "showDirt", "showIds", "showMail", "showBeliefs"]) $(id).addEventListener("change", () => { dirty = true; });
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
  const list = selected ? selected.events.filter(e => eventFilter.has(e.cat)) : authoritySelected ? AUTH.log.filter(e => eventFilter.has(e.cat)) : events.filter(e => e.cat === "inst" || e.cat === "fail");
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
  else if (e.key === "Escape") { if (focusId) focusNode(0, false); else if (openCase) showCase(null); else select(null); }
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
new ResizeObserver(() => { dirty = true; }).observe($("caseCanvas"));
function frame(now) {
  const dt = Math.min(0.1, (now - (lastClock || now)) / 1000); lastClock = now;
  if (T && playing) { setTick(tick + dt * +$("speed").value * 1000 / T.tickMs, false); if (tick >= T.horizon) setPlaying(false); }
  if (T && (dirty || cits.some(c => c.disp && c.target && Math.hypot(c.disp.x - c.target.x, c.disp.y - c.target.y) > 0.3))) {
    placeCitizens(tick, dt); drawMap(); drawStrip(); drawCase(); updatePeople(); renderDossier(false);
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
