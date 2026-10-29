# Optional Unity integration

No files have been installed into the existing Unity project. The standalone solution
builds and tests without Unity. Do not refresh or reload a running editor to build it.

For a future adapter in a separate Unity project:

1. Build Release, then copy Station.Contracts.dll, Station.Mind.dll and Station.Core.dll
   from the shared projects' `bin/Release/netstandard2.1` directories into that project's
   `Assets/Plugins/ImperialStation/`. Do not copy Runner or test assemblies.
2. Load catalog/scenario data in adapter-owned I/O, or decode a checkpoint envelope's
   base64 state and call `World.Restore(byte[])`. The core accepts simulation-owned
   definitions, tiles, actor seeds and items; no prefab lookup supplies gameplay data.
3. Advance with integer target ticks via `World.AdvanceTo`. Submit actor inputs through
   `World.Submit` for an unprocessed tick. Rendering must never directly change state.
4. Draw tiles using definition visual keys; draw actors using `InspectActor` source,
   destination and rational progress. Convert to floats only in rendering.
5. Compare fixture digests on .NET, Unity Mono and IL2CPP before claiming runtime parity.

Compatibility is currently established at the target-framework/language boundary.
No Unity/Mono/IL2CPP execution or FPS controls have been verified. A production adapter
and cross-runtime comparisons are H4 work. If source sharing replaces DLL packaging,
use asmdefs with `noEngineReferences: true`; Mind may reference only Contracts.
