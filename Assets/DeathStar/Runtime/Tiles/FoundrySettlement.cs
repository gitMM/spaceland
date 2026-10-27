using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;
using Object=UnityEngine.Object;

namespace DeathStar
{
    public enum DebugView { Off, Family, Shape }

    // Owns generated meshes only; the construction's family data remains the source of truth.
    public sealed class FoundrySettlement
    {
        sealed class Tile
        {
            public GameObject root;public Mesh near,far;public Color shape;
            public readonly List<MeshRenderer> renderers=new List<MeshRenderer>(2);
        }
        // Where the last rebuild spent its time, in ms: solving, family forms and relief, carving and mesh assembly,
        // bending onto the sphere grid, and scene objects (create, destroy, LOD).
        public struct Timings
        {
            public double solve,forms,meshes,bend,objects;public int tiles;
            public double Total=>solve+forms+meshes+bend+objects;
            public void Add(in Timings o){solve+=o.solve;forms+=o.forms;meshes+=o.meshes;bend+=o.bend;objects+=o.objects;tiles+=o.tiles;}
        }
        readonly Dictionary<Vector3Int,Tile> tiles=new Dictionary<Vector3Int,Tile>();
        readonly Transform parent;
        readonly Material[] materials;
        readonly MaterialPropertyBlock debugProperties=new MaterialPropertyBlock();
        static readonly int OverlayId=Shader.PropertyToID("_TileOverlay"),TintId=Shader.PropertyToID("_TileOverlayTint");
        public DebugView View {get;private set;}
        public bool ColorCoding=>View!=DebugView.Off;
        public TileStructure Structure {get;private set;}
        public Timings LastTimings {get;private set;}
        // Lab: while set, rebuilds record how every fixed-size part comes out (CurvedTileMesh.SizeRatio), untimed.
        public bool measureFixedParts;
        public readonly List<(string name,float ratio)> fixedPartSizes=new List<(string,float)>();
        public int MeshCount=>tiles.Count;
        public FoundrySettlement(Transform parent,Material[] materials)
        {this.parent=parent;this.materials=materials;debugProperties.SetFloat(OverlayId,0);}
        // Renderer overrides keep this build independent of the gallery/lab's global colour switch.
        // Both LODs and every newly rebuilt tile receive the same state, without regenerating geometry.
        public void SetColorCoding(bool enabled)=>SetDebugView(enabled?DebugView.Family:DebugView.Off);
        public void SetDebugView(DebugView view)
        {
            View=view;
            foreach(var tile in tiles.Values)ApplyDebug(tile);
        }
        void ApplyDebug(Tile tile)
        {
            debugProperties.SetFloat(OverlayId,View!=DebugView.Off?1:0);
            debugProperties.SetVector(TintId,View==DebugView.Shape?(Vector4)tile.shape:Vector4.zero);
            foreach(var renderer in tile.renderers)renderer.SetPropertyBlock(debugProperties);
        }
        static void Release(Object o){if(o==null)return;if(Application.isPlaying)Object.Destroy(o);else Object.DestroyImmediate(o);}
        void Remove(Vector3Int cell)
        {
            if(!tiles.TryGetValue(cell,out var tile))return;
            Release(tile.near);Release(tile.far);Release(tile.root);tiles.Remove(cell);
        }
        public void Rebuild(BlockConstruction model,IEnumerable<Vector3Int> edits=null)
        {
            var timings=new Timings();var clock=Stopwatch.StartNew();
            double Lap(){double ms=clock.Elapsed.TotalMilliseconds;clock.Restart();return ms;}
            var next=SettlementSolver.Solve(model);
            var dirty=Structure==null||edits==null?new HashSet<Vector3Int>(next.cells.Keys):SettlementSolver.Dirty(Structure,next,edits);
            timings.solve+=Lap();
            foreach(var c in new List<Vector3Int>(tiles.Keys))if(!model.Contains(c))Remove(c);
            Structure=next;var warp=new CurvedTileMesh(model);var builder=new TileBuilder(next,warp);
            var ordered=new List<Vector3Int>(dirty);ordered.Sort(BlockConstruction.Compare);
            timings.objects+=Lap();
            foreach(var cell in ordered)
            {
                Remove(cell);if(!next.TryGet(cell,out var placement)){timings.objects+=Lap();continue;}
                timings.objects+=Lap();
                var source=builder.Build(cell);timings.forms+=Lap();if(source.VertexCount==0)continue;
                var (near,far)=source.ToMeshes(placement.family+" "+cell);
                Vector3[] before=null;List<Vector4> frames=null,anchors=null;
                if(measureFixedParts&&source.fixedParts.Count>0)
                {before=near.vertices;frames=new List<Vector4>();near.GetUVs(4,frames);anchors=new List<Vector4>();near.GetUVs(5,anchors);}
                timings.meshes+=Lap();
                warp.Bend(near,cell);if(far!=null)warp.Bend(far,cell,near,source.SharedVertices);timings.bend+=Lap();
                if(before!=null)
                {
                    var after=near.vertices;
                    foreach(var (name,start,end) in source.fixedParts)fixedPartSizes.Add((name,warp.SizeRatio(before,after,frames,anchors,start,end)));
                    clock.Restart();
                }
                var root=new GameObject(near.name){hideFlags=HideFlags.DontSave};root.transform.SetParent(parent,false);
                var tile=new Tile{root=root,near=near,far=far,shape=TileLook.ShapeColor(model.Shape(cell))};tiles.Add(cell,tile);
                MeshRenderer Add(string name,Mesh mesh)
                {
                    var go=new GameObject(name){hideFlags=HideFlags.DontSave};go.transform.SetParent(root.transform,false);
                    go.AddComponent<MeshFilter>().sharedMesh=mesh;
                    var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterials=materials;
                    tile.renderers.Add(renderer);return renderer;
                }
                var r=Add("Near",near);
                if(far!=null)
                {
                    var f=Add("Far",far);var lod=root.AddComponent<LODGroup>();
                    lod.SetLODs(new[]{new LOD(TileGallery.NearDetail,new Renderer[]{r}),new LOD(0,new Renderer[]{f})});lod.RecalculateBounds();
                }
                ApplyDebug(tile);timings.tiles++;timings.objects+=Lap();
            }
            LastTimings=timings;
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
