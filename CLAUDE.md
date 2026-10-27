Spaceland: the procedural Death Star / space-station settlement prototype (moved out of Experiments3 on 2026-10-27). Everything lives in `Assets/DeathStar`; see `Assets/DeathStar/README.md` and the design doc `Assets/DeathStar/doc/modular-settlement-tile-design.md`.

Drive the Unity editor with the `unity` CLI from Bash (`unity command <name>`, com.unity.pipeline) - there is no Unity MCP. Use the project-local `spaceland-unity` skill for Unity Editor work. This project's pipeline server is on port 7804; always pass `--project-path "C:\Transhuman\spaceland"`.
Don't refresh and cause a domain reload if editor in play mode.
To judge the settlement tiles, run them headless with `tools/tile-lab.sh report` (writes `DeathStarRuns/Tiles/`) instead of playing and watching.
