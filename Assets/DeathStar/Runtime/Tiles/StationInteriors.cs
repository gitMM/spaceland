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
    // With people, each room gets a reference person or two (TileBuilder.Figure), standing on its walls for scale: a
    // home's on any of its six walls (there is no up), a tube's on its flats, each placed by casting from the room's
    // middle onto its own mesh, so they stand on the real, bent surfaces. They have no colliders.
    [ExecuteAlways]
    public sealed class StationInteriors : MonoBehaviour
    {
        public Transform ringCentre;
        public float radius=15,hold=4;
        public int perFrame=4;
        public bool colliders,people=true;
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
            if(people)People(cell,go.transform,mesh);
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
        void OnDestroy(){foreach(var f in figures)Release(f);}

        // ---------------------------------------------------------------- people for scale

        Mesh[] figures=new Mesh[0];
        Mesh[] Figures
        {
            get
            {
                if(figures.Length==2&&figures[0]!=null)return figures;
                figures=new Mesh[2];
                for(int i=0;i<2;i++){figures[i]=TileBuilder.Figure(i).ToMesh("Person "+i);figures[i].hideFlags=HideFlags.DontSave;}
                return figures;
            }
        }
        const float Tall=1.8f;
        // One or two people in a home, one in a tube or junction, on walls the cell's hash picks: each stands where a ray
        // from the room's middle (moved across the wall, clear of its hatch or socket) meets that wall square on, with
        // room over its head.
        void People(Vector3Int cell,Transform room,Mesh mesh)
        {
            var p=structure.cells[cell];var v=mesh.vertices;var t=mesh.triangles;
            for(int i=0;i<v.Length;i++)v[i]=room.TransformPoint(v[i]);
            uint h=(uint)(cell.x*73856093)^(uint)(cell.y*19349663)^(uint)(cell.z*83492791);
            float R(int k){uint x=h^(uint)(k*0x9E3779B1U);x^=x>>16;x*=0x7feb352dU;x^=x>>15;x*=0x846ca68bU;x^=x>>16;return (x&0xFFFFFF)/16777216f;}
            var c=centres[cell];bool tube=p.family==TileFamily.Corridor;
            var links=tube?structure.CorridorLinks(cell,p):null;
            bool run=tube&&links.Count==2&&links[0]==-links[1];
            var walls=new List<Vector3Int>();
            foreach(var d in TileStructure.Directions)
                if(!tube||(run?Vector3.Dot(d,links[0])==0:!links.Contains(d)))walls.Add(d);
            int want=tube?1:1+(R(1)<0.6f?1:0),placed=0;
            for(int k=0;k<walls.Count*2&&placed<want;k++)
            {
                var n=walls[(int)(R(10+k)*walls.Count)%walls.Count];
                var dir=FaceNormal(cell,n);
                // two directions across the wall, as bent
                var a=TileStructure.Directions[0];foreach(var d in TileStructure.Directions)if(Vector3.Dot(d,n)==0){a=d;break;}
                var b=TileStructure.Cross(n,a);
                Vector3 Across(Vector3Int d)=>Vector3.ProjectOnPlane(Centre(cell+d)-c,dir).normalized;
                var origin=c;
                if(run)origin+=Vector3.ProjectOnPlane(Centre(cell+links[0])-c,dir).normalized*(R(20+k)*2-1)*1.2f;
                else if(tube)origin+=Across(a)*(R(20+k)-0.5f)*0.6f;
                else origin+=Across(a)*(R(20+k)<0.5f?-1.4f:1.4f)+Across(b)*(R(30+k)*2-1)*0.9f;
                if(!Hit(v,t,origin,dir,4,out var feet,out var normal)||Vector3.Dot(normal,-dir)<0.8f)continue;
                var up=normal;
                if(Hit(v,t,feet+up*0.05f,up,Tall+0.1f,out _,out _))continue;   // no room over its head
                float turn=R(40+k)*Mathf.PI*2;var t1=Vector3.ProjectOnPlane(Across(a),up).normalized;var t2=Vector3.Cross(up,t1);
                var facing=t1*Mathf.Cos(turn)+t2*Mathf.Sin(turn);
                var go=new GameObject("Person"){hideFlags=HideFlags.DontSave};go.transform.SetParent(room,true);
                go.transform.SetPositionAndRotation(feet,Quaternion.LookRotation(facing,up));
                go.AddComponent<MeshFilter>().sharedMesh=Figures[(int)(R(50+k)*2)%2];
                var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=materials;r.renderingLayerMask=1u|InteriorLayer;
                placed++;
            }
        }
        // The nearest triangle of a world-space mesh (v, t) along a ray within max: where and its normal facing the ray.
        static bool Hit(Vector3[] v,int[] t,Vector3 o,Vector3 d,float max,out Vector3 point,out Vector3 normal)
        {
            point=normal=default;float best=max;bool found=false;
            for(int i=0;i+2<t.Length;i+=3)
            {
                Vector3 a=v[t[i]],e1=v[t[i+1]]-a,e2=v[t[i+2]]-a,p=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,p);
                if(Mathf.Abs(det)<1e-9f)continue;
                float inv=1/det;var s=o-a;float u=Vector3.Dot(s,p)*inv;if(u<0||u>1)continue;
                var q=Vector3.Cross(s,e1);float w=Vector3.Dot(d,q)*inv;if(w<0||u+w>1)continue;
                float dist=Vector3.Dot(e2,q)*inv;if(dist<=1e-4f||dist>=best)continue;
                best=dist;found=true;point=o+d*dist;normal=Vector3.Cross(e1,e2).normalized;if(Vector3.Dot(normal,d)>0)normal=-normal;
            }
            return found;
        }
    }
}
