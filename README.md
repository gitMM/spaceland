# Spaceland

Procedural Death Star / space-station prototype for Unity 6 (6000.3.24f1, URP 17.3).

- **Settlement tiles** — Death Star → Open Tile Gallery (`Assets/DeathStar/Scenes/TileGallery.unity`)
- **Original procedural planet** — Death Star → Open Experiment (`Assets/DeathStar/Scenes/DeathStar.unity`)
- **Block Foundry** — Death Star → Open Block Foundry (`Assets/DeathStar/Scenes/BlockFoundry.unity`)

Details, controls and validation: [`Assets/DeathStar/README.md`](Assets/DeathStar/README.md).
Tile design doc: [`Assets/DeathStar/doc/modular-settlement-tile-design.md`](Assets/DeathStar/doc/modular-settlement-tile-design.md).

Headless tile checks (editor open, edit mode):

```bash
tools/tile-lab.sh report
```

Output goes to `DeathStarRuns/` (not tracked).
