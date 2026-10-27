#!/usr/bin/env bash
# Tile lab: render the settlement tile exemplars headless and write pictures and a report to DeathStarRuns/Tiles/.
#   tools/tile-lab.sh report
#   tools/tile-lab.sh validate   # solver, curved geometry and Foundry edit/undo/picking checks
#   tools/tile-lab.sh foundry    # automatically solved curved settlement pictures
#   tools/tile-lab.sh limit      # just the cap at the build limit (narrowest and flattest cells)
#   tools/tile-lab.sh snap family=<Name> [back=1] [clay=1]  |  snap x= y= z= tx= ty= tz= [fov=] [clay=1]  -> DeathStarRuns/Tiles/snap.png
# Needs the spaceland editor open and not in play mode. Imports changed scripts first so the run uses the code
# on disk (set TILE_LAB_NO_COMPILE=1 to skip).
set -uo pipefail
cd "$(dirname "$0")/.."
P='C:\Transhuman\spaceland'
u() { unity command "$@" --project-path "$P" --result-only --no-banner; }

state=$(u eval 'return UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode;' 2>&1)
if echo "$state" | grep -q '"result": true'; then echo "The editor is in play mode. The lab only runs in edit mode: stop play mode first."; exit 2; fi
if ! echo "$state" | grep -q '"result": false'; then echo "Unity editor not reachable:"; echo "$state"; exit 2; fi

if [ -z "${TILE_LAB_NO_COMPILE:-}" ]; then
  u eval 'UnityEditor.AssetDatabase.Refresh(); return true;' >/dev/null 2>&1
  sleep 3   # a compile queued by the refresh (or by leaving play mode) may not have started yet
  ok=0
  for _ in $(seq 1 180); do
    r=$(u eval 'return UnityEditor.EditorApplication.isCompiling || UnityEditor.EditorApplication.isUpdating;' 2>&1)
    if echo "$r" | grep -q '"result": false'; then ok=$((ok + 1)); [ $ok -ge 3 ] && break; else ok=0; fi
    sleep 1
  done
  r=$(u eval 'return UnityEditor.EditorUtility.scriptCompilationFailed;' 2>&1)
  if echo "$r" | grep -q '"result": true'; then
    echo "Scripts don't compile; fix the errors first (unity command console --project-path '$P')."
    exit 3
  fi
fi

# Shaders that just changed only draw from a later editor update on: draw once in a call of its own, then let the
# variants finish compiling.
unity command run_script --timeout 120 --project-path "$P" --file tools/TileLabEntry.cs --entry TileLabEntry.Run --args '["warmup"]' --result-only --no-banner >/dev/null 2>&1
for _ in $(seq 1 60); do
  r=$(u eval 'return UnityEditor.ShaderUtil.anythingCompiling;' 2>&1)
  echo "$r" | grep -q '"result": false' && break
  sleep 1
done

args=$(printf '%s ' "${@:-report}")
args=${args% }
# A detached job: a direct call is aborted after 30 s on the editor's main thread, and the lab takes longer.
sub=$(unity command run_script --detach --project-path "$P" --file tools/TileLabEntry.cs --entry TileLabEntry.Run --args "[\"$args\"]" --json --no-banner 2>&1)
job=$(echo "$sub" | grep -o '"jobId": *"[0-9a-f]*"' | head -1 | grep -o '[0-9a-f]\{16,\}')
if [ -z "$job" ]; then echo "Could not start the lab job:"; echo "$sub"; exit 1; fi
out=$(unity job wait "$job" --timeout 600 --project-path "$P" --json --no-banner 2>&1)
if ! echo "$out" | grep -q '"result": "DONE'; then echo "$out"; exit 1; fi
if [ "${1:-report}" != "report" ]; then echo "$out" | grep -o '"result": "DONE[^"]*' | sed 's/"result": "//'; exit 0; fi
cat DeathStarRuns/Tiles/last.txt
