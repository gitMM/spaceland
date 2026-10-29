using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    // The station's interiors near the viewer (TileBuilder.BuildInterior). Habitat and Corridor cells whose centres lie
    // within radius of the ring centre get their room and a lamp (and in play a collider), and their tiles' windows turn
    // to clear glass so the real room shows through them; further out the windows keep TileGlass's fake rooms. A cell
    // drops its interior again past radius + hold. Rooms are built nearest first, perFrame at a time in play. On the
    // Foundry's curved grid they are bent like the tiles (CurvedTileMesh.Bend) and distances use the cells' curved centres.
    // Lamps light only the interiors (rendering layer 1), so no light falls through walls onto the hull outside. With
    // colliders (always in play) each room gets a mesh collider and its tile's window glass another, for MagBootWalker.
    [ExecuteAlways]
    public sealed class StationInteriors : MonoBehaviour
    {
        public Transform ringCentre;
        public float radius=15,hold=4;
        public int perFrame=4;
        public bool colliders;
        public float lampIntensity=4,lampRange=6;
        public Color lampColor=new Color(1f,0.9f,0.78f);
        public const uint InteriorLayer=1u<<1;

        TileStructure structure;TileBuilder builder;CurvedTileMesh warp;
        readonly Dictionary<Vector3Int,Vector3> centres=new Dictionary<Vector3Int,Vector3>();
        Material[] materials,inside;Material clear;
        Dictionary<Vector3Int,MeshRenderer[]> tiles;
        readonly Dictionary<Vector3Int,(GameObject go,Mesh mesh,Mesh glass)> built=new Dictionary<Vector3Int,(GameObject,Mesh,Mesh)>();
        readonly List<Vector3Int> scratch=new List<Vector3Int>();
        Transform holder;

        public int Count=>built.Count;
        public IEnumerable<Vector3Int> Cells=>built.Keys;
        public GameObject Get(Vector3Int cell)=>built.TryGetValue(cell,out var b)?b.go:null;
        // A cell's tile renderers (near level first), and whether people pass between two cells (TileBuilder.Opens).
        public MeshRenderer[] Tile(Vector3Int cell)=>tiles!=null&&tiles.TryGetValue(cell,out var r)?r:null;
        public bool Opens(Vector3Int a,Vector3Int b)=>builder!=null&&builder.Opens(a,b);
        public TileStructure Structure=>structure;
        // A cell's centre and the centre and outward normal of its face n, where they are (bent on the curved grid).
        public Vector3 Centre(Vector3Int c)=>warp!=null?warp.Map(TileStructure.Center(c)):TileStructure.Center(c);
        public Vector3 FaceCentre(Vector3Int c,Vector3Int n)=>warp!=null?warp.Map(TileStructure.Center(c)+(Vector3)n*TileBuilder.Half):TileStructure.Center(c)+(Vector3)n*TileBuilder.Half;
        public Vector3 FaceNormal(Vector3Int c,Vector3Int n)=>builder.FaceNormal(c,n);
        // The interior cell whose centre is nearest p (within 4 m), or false.
        public bool CellAt(Vector3 p,out Vector3Int cell)
        {
            cell=default;float best=16;
            foreach(var pair in centres){float d=(pair.Value-p).sqrMagnitude;if(d<best){best=d;cell=pair.Key;}}
            return best<16;
        }
        // Where to stand to start: on the deck (its axis's - face) of the home nearest near that has a window wall and a
        // hatch to another home, a metre back from its middle, facing the windows. hatch: the face to the next home.
        public bool Spawn(Vector3 near,out Vector3 feet,out Vector3 up,out Vector3 forward,out Vector3Int cell,out Vector3Int hatch)
        {
            feet=up=forward=default;cell=hatch=default;float best=float.MaxValue;bool found=false;
            if(structure==null)return false;
            foreach(var pair in centres)
            {
                var c=pair.Key;var p=structure.cells[c];if(p.family!=TileFamily.Habitat)continue;
                Vector3Int w=default,h=default;bool hasW=false,hasH=false;
                foreach(var d in TileStructure.Directions)
                {
                    if(!hasW&&d!=p.axis&&d!=-p.axis&&!structure.TryGet(c+d,out _)){w=d;hasW=true;}
                    if(!hasH&&builder.Opens(c,c+d)&&structure.cells[c+d].family==TileFamily.Habitat){h=d;hasH=true;}
                }
                float dist=(pair.Value-near).sqrMagnitude;
                if(!hasW||!hasH||dist>=best)continue;
                best=dist;found=true;cell=c;hatch=h;
                var deck=-p.axis;var n=FaceNormal(c,deck);float inset=structure.Solid(c+deck)?TileBuilder.Partition:TileBuilder.Shell;
                up=-n;var window=FaceNormal(c,w);
                feet=FaceCentre(c,deck)-n*inset-Vector3.ProjectOnPlane(window,up).normalized;
                forward=Vector3.ProjectOnPlane(FaceCentre(c,w)-feet,up);
            }
            return found;
        }
        // Last ShowAround's build time (ms) and the interiors it built.
        public double BuildMs {get;private set;}
        public int Built {get;private set;}

        // tiles: each cell's tile renderers (all its levels of detail), whose glass slot turns clear. warp: the curved
        // grid the tiles are bent onto, or null.
        public void Init(TileStructure structure,TileBuilder builder,Material[] materials,Material clear,Dictionary<Vector3Int,MeshRenderer[]> tiles,CurvedTileMesh warp=null)
        {
            Clear();
            this.structure=structure;this.builder=builder;this.materials=materials;this.tiles=tiles;this.warp=warp;
            centres.Clear();
            foreach(var pair in structure.cells)
                if(builder.Interior(pair.Key))centres[pair.Key]=warp!=null?warp.Map(TileStructure.Center(pair.Key)):TileStructure.Center(pair.Key);
            this.clear=clear!=null?clear:materials[(int)TileSlot.Glass];
            inside=(Material[])materials.Clone();inside[(int)TileSlot.Glass]=this.clear;
        }

        // Builds the interiors within r of centre, nearest first (at most max of them; 0: all), and drops those past
        // r + hold.
        public void ShowAround(Vector3 centre,float r,int max=0)
        {
            if(structure==null)return;
            var sw=System.Diagnostics.Stopwatch.StartNew();
            scratch.Clear();
            foreach(var cell in built.Keys)if(Vector3.Distance(centres[cell],centre)>r+hold)scratch.Add(cell);
            foreach(var cell in scratch)Drop(cell);
            scratch.Clear();
            foreach(var pair in centres)if(!built.ContainsKey(pair.Key)&&Vector3.Distance(pair.Value,centre)<=r)scratch.Add(pair.Key);
            scratch.Sort((a,b)=>(centres[a]-centre).sqrMagnitude.CompareTo((centres[b]-centre).sqrMagnitude));
            int n=max>0?Mathf.Min(max,scratch.Count):scratch.Count;
            for(int i=0;i<n;i++)Add(scratch[i]);
            Built=n;BuildMs=sw.Elapsed.TotalMilliseconds;
        }

        public void Clear()
        {
            scratch.Clear();scratch.AddRange(built.Keys);
            foreach(var cell in scratch)Drop(cell);
        }

        Transform Holder
        {
            get
            {
                if(holder!=null)return holder;
                var go=new GameObject("Interiors"){hideFlags=HideFlags.DontSave};go.transform.SetParent(transform,false);
                return holder=go.transform;
            }
        }

        void Add(Vector3Int cell)
        {
            var t=builder.BuildInterior(cell);
            if(t.VertexCount==0){built[cell]=(null,null,null);return;}
            var mesh=t.ToMesh("Interior "+cell);mesh.hideFlags=HideFlags.DontSave;
            if(warp!=null)warp.Bend(mesh,cell);   // to curved positions (not relative to the cell), like the tiles
            var go=new GameObject("Interior "+cell){hideFlags=HideFlags.DontSave};
            go.transform.SetParent(Holder,false);if(warp==null)go.transform.localPosition=TileStructure.Center(cell);
            go.AddComponent<MeshFilter>().sharedMesh=mesh;
            var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=inside;r.renderingLayerMask=1u|InteriorLayer;
            var lamp=new GameObject("Lamp"){hideFlags=HideFlags.DontSave}.AddComponent<Light>();lamp.transform.SetParent(go.transform,false);
            if(warp!=null)lamp.transform.localPosition=centres[cell];
            lamp.type=LightType.Point;lamp.range=lampRange;lamp.intensity=lampIntensity;lamp.color=lampColor;lamp.shadows=LightShadows.None;
            lamp.renderingLayerMask=(int)InteriorLayer;lamp.GetUniversalAdditionalLightData().renderingLayers=InteriorLayer;
            Mesh glass=null;
            if(Application.isPlaying||colliders)
            {
                go.AddComponent<MeshCollider>().sharedMesh=mesh;
                glass=GlassMesh(cell,go.transform);if(glass!=null)go.AddComponent<MeshCollider>().sharedMesh=glass;
            }
            SetGlass(cell,clear);
            built[cell]=(go,mesh,glass);
        }

        void Drop(Vector3Int cell)
        {
            if(!built.TryGetValue(cell,out var b))return;
            built.Remove(cell);
            if(b.go==null)return;
            SetGlass(cell,materials[(int)TileSlot.Glass]);
            Release(b.go);Release(b.mesh);Release(b.glass);
        }

        // The panes of a cell's tile (its near level), in to's space, both ways round (they face out, and colliders are
        // one-sided to queries), for a collider: people stop at the windows.
        Mesh GlassMesh(Vector3Int cell,Transform to)
        {
            var r=Tile(cell);if(r==null||r.Length==0||r[0]==null)return null;
            var source=r[0].GetComponent<MeshFilter>().sharedMesh;if(source==null||source.subMeshCount<=(int)TileSlot.Glass)return null;
            var index=source.GetIndices((int)TileSlot.Glass);if(index.Length==0)return null;
            var v=source.vertices;var points=new Vector3[index.Length];var tris=new int[index.Length*2];
            for(int i=0;i<index.Length;i++){points[i]=to.InverseTransformPoint(r[0].transform.TransformPoint(v[index[i]]));tris[i]=i;}
            for(int i=0;i<index.Length;i+=3){tris[index.Length+i]=i;tris[index.Length+i+1]=i+2;tris[index.Length+i+2]=i+1;}
            var m=new Mesh{name="Glass "+cell,hideFlags=HideFlags.DontSave};m.vertices=points;m.triangles=tris;
            return m;
        }

        void SetGlass(Vector3Int cell,Material glass)
        {
            if(tiles==null||!tiles.TryGetValue(cell,out var renderers))return;
            foreach(var r in renderers)
            {
                if(r==null)continue;
                var mats=r.sharedMaterials;
                if(mats.Length<=(int)TileSlot.Glass)continue;
                mats[(int)TileSlot.Glass]=glass;r.sharedMaterials=mats;
            }
        }

        static void Release(Object o){if(o==null)return;if(Application.isPlaying)Destroy(o);else DestroyImmediate(o);}

        void Update()
        {
            if(!Application.isPlaying||ringCentre==null)return;
            ShowAround(ringCentre.position,radius,perFrame);
        }
        void OnDisable()=>Clear();
    }
}
