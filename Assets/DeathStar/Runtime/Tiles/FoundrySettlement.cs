using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Owns generated meshes only; the construction's family data remains the source of truth.
    public sealed class FoundrySettlement
    {
        sealed class Tile
        {
            public GameObject root;public Mesh near,far;
            public readonly List<MeshRenderer> renderers=new List<MeshRenderer>(2);
        }
        readonly Dictionary<Vector3Int,Tile> tiles=new Dictionary<Vector3Int,Tile>();
        readonly Transform parent;
        readonly Material[] materials;
        readonly MaterialPropertyBlock debugProperties=new MaterialPropertyBlock();
        static readonly int OverlayId=Shader.PropertyToID("_TileOverlay");
        public bool ColorCoding {get;private set;}
        public TileStructure Structure {get;private set;}
        public int MeshCount=>tiles.Count;
        public FoundrySettlement(Transform parent,Material[] materials)
        {this.parent=parent;this.materials=materials;debugProperties.SetFloat(OverlayId,0);}
        // Renderer overrides keep this build independent of the gallery/lab's global colour switch.
        // Both LODs and every newly rebuilt tile receive the same state, without regenerating geometry.
        public void SetColorCoding(bool enabled)
        {
            ColorCoding=enabled;debugProperties.SetFloat(OverlayId,enabled?1:0);
            foreach(var tile in tiles.Values)foreach(var renderer in tile.renderers)renderer.SetPropertyBlock(debugProperties);
        }
        static void Release(Object o){if(o==null)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
        void Remove(Vector3Int cell)
        {
            if(!tiles.TryGetValue(cell,out var tile))return;
            Release(tile.near);Release(tile.far);Release(tile.root);tiles.Remove(cell);
        }
        public void Rebuild(BlockConstruction model,IEnumerable<Vector3Int> edits=null)
        {
            var next=SettlementSolver.Solve(model);
            var dirty=Structure==null||edits==null?new HashSet<Vector3Int>(next.cells.Keys):SettlementSolver.Dirty(Structure,next,edits);
            foreach(var c in new List<Vector3Int>(tiles.Keys))if(!model.Contains(c))Remove(c);
            Structure=next;var warp=new CurvedTileMesh(model);var builder=new TileBuilder(next,warp);
            var ordered=new List<Vector3Int>(dirty);ordered.Sort(BlockConstruction.Compare);
            foreach(var cell in ordered)
            {
                Remove(cell);if(!next.TryGet(cell,out var placement))continue;
                var source=builder.Build(cell);if(source.VertexCount==0)continue;
                var (near,far)=source.ToMeshes(placement.family+" "+cell);
                warp.Bend(near,cell);if(far!=null)warp.Bend(far,cell);
                var root=new GameObject(near.name){hideFlags=HideFlags.DontSave};root.transform.SetParent(parent,false);
                var tile=new Tile{root=root,near=near,far=far};tiles.Add(cell,tile);
                MeshRenderer Add(string name,Mesh mesh)
                {
                    var go=new GameObject(name){hideFlags=HideFlags.DontSave};go.transform.SetParent(root.transform,false);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                    renderer.SetPropertyBlock(debugProperties);tile.renderers.Add(renderer);return renderer;
                }
                var r=Add("Near",near);
                if(far!=null)
                {
                    var f=Add("Far",far);var lod=root.AddComponent<LODGroup>();
                    lod.SetLODs(new[]{new LOD(TileGallery.NearDetail,new Renderer[]{r}),new LOD(0,new Renderer[]{f})});lod.RecalculateBounds();
                }
            }
        }
        public Bounds Bounds
        {
            get
            {
                bool first=true;Bounds b=default;
                foreach(var t in tiles.Values)
                {
                    var r=t.root.GetComponentInChildren<MeshRenderer>();
                    if(first){b=r.bounds;first=false;}else b.Encapsulate(r.bounds);
                }
                return b;
            }
        }
        public void Dispose(){foreach(var c in new List<Vector3Int>(tiles.Keys))Remove(c);Structure=null;}
    }
}
