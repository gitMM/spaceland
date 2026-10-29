using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    // Assets the tile gallery draws with. Made and kept in step with TileLook by Death Star / Open Tile Gallery.
    public sealed class TileLibrary : ScriptableObject
    {
        public Material[] materials=new Material[TileMesh.SlotCount];   // indexed by TileSlot
        public Material clearGlass;   // windows with a built room behind them (StationInteriors)
        public Material sky;
        public Cubemap reflection;
        public VolumeProfile post;
    }
}
