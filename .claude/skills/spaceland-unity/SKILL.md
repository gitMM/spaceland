---
name: spaceland-unity
description: Use for any Unity Editor work in the spaceland project (the Death Star prototype) through the direct Unity CLI (`unity command`), including live scene or asset changes, editor commands, C# eval, recompiles, shader error checks, and reload decisions. Load this before the generic unity-cli or unity-pipeline skills.
---

# spaceland Unity Editor

Drive the editor with `unity command` through `com.unity.pipeline`; there is no Unity MCP in this project. Consult the project-local `unity-cli` skill for CLI syntax and `unity-pipeline` for editor command behavior when needed.

- **Bash only.** Run `unity` with the Bash tool and always pass `--project-path "C:\Transhuman\spaceland"`: other editors (e.g. `C:\Transhuman\Experiments3` on port 7802, `C:\Transhuman\Experiments2` on 7801, `C:\Transhuman\Edsger` on 7800; this project's server is pinned to 7804 in `ProjectSettings/Packages/com.unity.pipeline/EditorPipelineConfig.json`) may be open at the same time. The PowerShell tool's wrapper breaks on quoted C#. Wrap `eval` bodies in single quotes; multi-line bodies are fine. Git Bash rewrites arguments that start with `/` into Windows paths (`--target "/Green Cube"` arrives as `C:/Program Files/Git/Green Cube`), so prefix hierarchy-path calls with `MSYS_NO_PATHCONV=1` or pass the `globalId`.
- **Output:** add `--result-only --no-banner` whenever the bare result is enough, and use `--json` when you need the success/error envelope. The default output is a human-readable table. For `eval`, `--result-only` still prints eval's own object (`output`, `diagnostics`, `success`, `result`); the value is in `result`.
- **Auto-tick stays on.** It survives script reloads but not an editor restart. After a restart, run `unity command set_autotick --enable true` before work that needs editor updates.
- **Play mode:** before anything that refreshes assets or causes a domain reload (`recompile`, `AssetDatabase.Refresh`, package changes), check at that moment:
  `unity command eval --result-only --no-banner --project-path "C:\Transhuman\spaceland" 'return UnityEditor.EditorApplication.isPlaying;'`
  Do not refresh or recompile while it returns true.
- **Prefer typed commands over eval** (`create_gameobject`, `set_component_properties`, `menu`, `screenshot`, `console`); list them with `unity command --query <term>`. Anything longer than a few lines goes in a file run with `run_script --file <path> --entry Type.Method`. Keep that file outside `Assets/` (e.g. `Temp/`) so it causes no import.
- **Console noise:** a failed `unity command` also writes a `LogError` to the Unity console, with `Unity.Pipeline.BasePipelineServer` in its stack. Skip those entries when judging a change by console errors, or run `unity command clear_console` first.
- **Shader errors:** read `UnityEditor.ShaderUtil.GetShaderMessages(UnityEngine.Shader.Find("<name>"))` through eval instead of trusting the console. Unity auto-imports on save even when unfocused, so an include edited before its shader can leave stale errors; refresh once at the end and re-read.
- **Refresh can silently fail** (e.g. a locked asset aborts `Assets/Refresh`). If a change does not show up, import the changed files individually (`AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate)`).
- **No instance found:** `unity status` finding nothing does not prove the editor is closed. It may be in Safe Mode from compile errors (check with `unity pipeline list`). For a compile check with no editor, run `dotnet build DeathStar.Editor.csproj -v q -nologo`.
