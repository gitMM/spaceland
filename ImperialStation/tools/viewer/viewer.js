"use strict";
const byId = id => document.getElementById(id);
const canvas = byId("grid"), context = canvas.getContext("2d");
let snapshot = null, events = [], tick = 0, selected = null, transform = null, focusedActor = 0;
const equals = (a, b) => a && b && a.x === b.x && a.y === b.y && a.z === b.z;
const coord = p => `(${p.x}, ${p.y}, ${p.z})`;
function message(text) { byId("status").textContent = text; }
byId("snapshot").addEventListener("change", async e => {
  try {
    const data = JSON.parse(await e.target.files[0].text());
    if (data.schemaVersion !== 1 || data.kind !== "viewer_snapshot" || !Array.isArray(data.tiles) || !Array.isArray(data.actors)) throw Error("Select the run's snapshot.json (viewer snapshot).");
    snapshot = data; selected = null; focusedActor = data.actors[0]?.id || 0;
    const layers = [...new Set(data.tiles.map(t => t.position.y))].sort((a,b) => a-b);
    byId("layer").replaceChildren(...layers.map(y => new Option(`Deck ${y}`, String(y))));
    byId("scrub").max = String(data.horizon); byId("tick").max = String(data.horizon);
    byId("scrub").min = String(Math.max(0,data.startTick)); byId("tick").min = byId("scrub").min;
    byId("actors").replaceChildren(...data.actors.map(actor => {
      const button = document.createElement("button"); button.className = "actor-button"; button.textContent = `${actor.id} · ${actor.name || actor.role}`;
      button.addEventListener("click", () => { selected = {kind:"actor", id:actor.id}; focusedActor=actor.id; render(); }); return button;
    }));
    setTick(Math.max(0,data.startTick)); message(`${data.tiles.length} tiles · ${data.actors.length} actors. ${events.length ? `${events.length} events loaded.` : "Load the matching trace next."}`);
  } catch (error) { message(error.message); }
});
byId("trace").addEventListener("change", async e => {
  try {
    events = (await e.target.files[0].text()).split(/\r?\n/).filter(s => s.trim()).map(JSON.parse);
    if (events.some(e => !Number.isSafeInteger(e.tick) || !Number.isSafeInteger(e.id) || typeof e.type !== "string")) throw Error("Invalid event data or tick outside the viewer's safe integer range.");
    events.sort((a,b) => a.tick-b.tick || a.id-b.id); message(`${events.length} events loaded. ${snapshot ? "Ready to inspect." : "Load the snapshot next."}`); render();
  } catch (error) { events = []; message(error.message); }
});
function setTick(value) {
  tick = Math.max(Number(byId("scrub").min), Math.min(Number(byId("scrub").max), Math.trunc(Number(value)) || 0));
  byId("scrub").value = tick; byId("tick").value = tick; render();
}
byId("scrub").addEventListener("input", e => setTick(e.target.value));
byId("tick").addEventListener("input", e => setTick(e.target.value));
byId("back").addEventListener("click", () => setTick(tick-1));
byId("forward").addEventListener("click", () => setTick(tick+1));
byId("layer").addEventListener("change", render);
byId("information").addEventListener("change",()=>{selected=null;render();});
const factKey = key => `${key.subjectKind}:${key.subjectId}/${key.attribute}`;
function factsAt(mode) {
  const state=new Map();
  const initial=mode==="authority"?(snapshot.beliefs||[]):(snapshot.knowledge||[]).find(k=>k.actorId===focusedActor)?.facts||[];
  for(const fact of initial)state.set(factKey(fact.key),{key:fact.key,value:fact.value,detail:fact.sourceKind||"Reported"});
  for(const e of events){
    if(e.tick>tick)break;
    if(!e.factKey)continue;
    if(mode==="authority"&&e.type==="BeliefChanged")state.set(factKey(e.factKey),{key:e.factKey,value:e.factValue,detail:`Reported · t${e.tick} · message ${e.messageId}`});
    if(mode==="actor"&&e.actorId===focusedActor){
      if(e.type==="ObservationRecorded")state.set(factKey(e.factKey),{key:e.factKey,value:e.factValue,detail:e.detail});
      if(e.type==="KnowledgeEvicted")state.delete(factKey(e.factKey));
    }
  }
  return [...state.values()];
}
function pose(actor) {
  let position = actor.position, segment = actor.inTransit ? {source:actor.source,target:actor.destination,departure:actor.departure,arrival:actor.arrival} : null;
  for (const e of events) {
    if (e.tick > tick) break;
    if (e.actorId !== actor.id) continue;
    if (e.type === "MovementStarted") segment = e;
    if (e.type === "TileArrived") { position = e.target; segment = null; }
  }
  if (!segment) return {...position};
  const p = Math.max(0,Math.min(1,(tick-segment.departure)/(segment.arrival-segment.departure)));
  return {x:segment.source.x+(segment.target.x-segment.source.x)*p,y:segment.source.y+(segment.target.y-segment.source.y)*p,z:segment.source.z+(segment.target.z-segment.source.z)*p};
}
function render() {
  const rect = canvas.getBoundingClientRect(), ratio = window.devicePixelRatio || 1;
  canvas.width = Math.round(rect.width*ratio); canvas.height = Math.round(rect.height*ratio); context.setTransform(ratio,0,0,ratio,0,0);
  context.clearRect(0,0,rect.width,rect.height); byId("time").textContent = `Tick ${tick.toLocaleString()}`;
  if (!snapshot) { context.fillStyle="#647f96"; context.font="14px system-ui"; context.fillText("Load a snapshot to explore the station",24,42); return; }
  const layer = Number(byId("layer").value), tiles = snapshot.tiles.filter(t => t.position.y === layer);
  if (!tiles.length) return;
  const xs = tiles.map(t=>t.position.x), zs = tiles.map(t=>t.position.z), minX = Math.min(...xs), minZ = Math.min(...zs);
  const width = Math.max(...xs)-minX+1, height = Math.max(...zs)-minZ+1;
  const cell = Math.max(1, Math.min(90,(rect.width-60)/width,(rect.height-60)/height));
  const ox = (rect.width-width*cell)/2, oz = (rect.height-height*cell)/2;
  transform = {cell,ox,oz,minX,minZ,layer};
  context.font=`${Math.min(11,cell/5)}px ui-monospace,monospace`; context.textAlign="center";
  for (const tile of tiles) {
    const x=ox+(tile.position.x-minX)*cell, z=oz+(tile.position.z-minZ)*cell;
    context.fillStyle=tile.completed?"#1e3446":"#151e28"; context.strokeStyle=selected?.kind==="tile"&&selected.id===tile.id?"#84ddcf":"#3a546a";
    context.lineWidth=selected?.kind==="tile"&&selected.id===tile.id?2:1;
    context.fillRect(x+3,z+3,cell-6,cell-6); context.strokeRect(x+3,z+3,cell-6,cell-6);
    if(cell>32){context.fillStyle="#91a6b8";context.fillText(tile.definition,x+cell/2,z+cell*.27,cell-12);context.fillStyle="#6b8498";context.fillText(`#${tile.id} · ${tile.traversalTicks}t`,x+cell/2,z+cell*.85,cell-12);}
  }
  const information=byId("information").value;
  const displayedFacts=information==="truth"?[]:factsAt(information);
  byId("fact-title").textContent=information==="truth"?"Privileged physical view":information==="authority"?"Authority's received evidence":`Actor ${focusedActor} · known facts`;
  byId("facts").replaceChildren(...displayedFacts.map(f=>{
    const row=document.createElement("div");row.className="event";const title=document.createElement("strong");title.textContent=factKey(f.key);
    const value=document.createElement("p");value.textContent=`${f.value.kind}: ${f.value.kind==="Coordinate"||f.value.kind==="AbsentAt"?coord(f.value.coordinate):f.value.kind==="Text"?f.value.text:f.value.number}`;
    const source=document.createElement("p");source.className="muted";source.textContent=f.detail;row.append(title,value,source);return row;
  }));
  for (const actor of snapshot.actors.filter(a=>information==="truth"||information==="actor"&&a.id===focusedActor)) {
    const p=pose(actor); if(Math.abs(p.y-layer)>.5) continue;
    const x=ox+(p.x-minX+.5)*cell,z=oz+(p.z-minZ+.5)*cell;
    context.beginPath();context.arc(x,z,Math.max(3,Math.min(10,cell*.13)),0,Math.PI*2);context.fillStyle=selected?.kind==="actor"&&selected.id===actor.id?"#fff2ad":"#7bd8c8";context.fill();
    if(cell>40){context.fillStyle="#0e2022";context.font="10px system-ui";context.fillText(String(actor.id),x,z+3);}
  }
  for(const f of displayedFacts.filter(f=>f.key.subjectKind==="Item"&&f.key.attribute==="Location"&&(f.value.kind==="Coordinate"||f.value.kind==="AbsentAt"))){
    const p=f.value.coordinate;if(p.y!==layer)continue;const x=ox+(p.x-minX+.5)*cell,z=oz+(p.z-minZ+.65)*cell;
    context.fillStyle=f.value.kind==="AbsentAt"?"#f2a58e":"#c4b4ff";context.font="12px ui-monospace,monospace";context.fillText(`${f.value.kind==="AbsentAt"?"×":"◆"} item ${f.key.subjectId}`,x,z,cell-8);
  }
  byId("summary").textContent=`Deck ${layer} / ${tiles.length} tiles / ${events.filter(e=>e.tick<=tick).length} events`;
  byId("selection").textContent=selected?`${selected.kind==="actor"?"Actor":"Tile"} ${selected.id}`:"Event history";
  byId("entity").textContent=selected?.kind==="tile"?coord(snapshot.tiles.find(t=>t.id===selected.id).position):"Events through the selected tick · latest first";
  const tile=selected?.kind==="tile"?snapshot.tiles.find(t=>t.id===selected.id):null;
  const visible=events.filter(e=>e.tick<=tick&&(information==="truth"||information==="actor"&&e.actorId===focusedActor||information==="authority"&&(e.type==="BeliefChanged"||e.type==="MailboxDelivered"&&e.detail==="Authority"))&&(!selected||(selected.kind==="actor"?e.actorId===selected.id:equals(e.source,tile.position)||equals(e.target,tile.position)))).slice(-120).reverse();
  byId("events").replaceChildren(...visible.map(e=>{
    const row=document.createElement("div");row.className="event";
    const meta=document.createElement("span");meta.className="meta";meta.textContent=`t${e.tick} · #${e.id} · ${e.visibility}`;
    const title=document.createElement("strong");title.textContent=e.type;
    const detail=document.createElement("p");detail.textContent=`${e.actorId?`Actor ${e.actorId} · `:""}${e.accountId?`account ${e.accountId} · `:""}${e.messageId?`message ${e.messageId} · `:""}${e.terminalId?`terminal ${e.terminalId} · `:""}${e.actionId?`action ${e.actionId} · `:""}${e.type==="MovementStarted"?`${coord(e.source)} → ${coord(e.target)}, arrival ${e.arrival}`:e.detail}`;
    row.append(meta,title,detail);return row;
  }));
}
canvas.addEventListener("click",e=>{
  if(!snapshot||!transform)return; const r=canvas.getBoundingClientRect(),t=transform,x=(e.clientX-r.left-t.ox)/t.cell+t.minX,z=(e.clientY-r.top-t.oz)/t.cell+t.minZ;
  const mode=byId("information").value;
  const actor=snapshot.actors.filter(a=>mode==="truth"||mode==="actor"&&a.id===focusedActor).find(a=>{const p=pose(a);return Math.abs(p.y-t.layer)<=.5&&Math.hypot(p.x+.5-x,p.z+.5-z)<.3;});
  const tile=snapshot.tiles.find(a=>a.position.y===t.layer&&a.position.x===Math.floor(x)&&a.position.z===Math.floor(z));
  selected=actor?{kind:"actor",id:actor.id}:tile?{kind:"tile",id:tile.id}:null;if(actor)focusedActor=actor.id;render();
});
new ResizeObserver(render).observe(canvas);render();
