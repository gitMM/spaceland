using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    // The station's interiors near the viewer (TileBuilder.BuildInterior). Cells with an interior whose centres lie
    // within radius of the ring centre get their room and a lamp (and in play a collider), and their tiles' windows turn
    // to clear glass so the real room shows through them (not Command's or Industrial's, whose towers stand glazed
    // outside the cube with no room in them); further out the windows keep TileGlass's fake rooms. A cell
    // drops its interior again past radius + hold. Rooms are built nearest first, perFrame at a time in play. On the
    // Foundry's curved grid they are bent like the tiles (CurvedTileMesh.Bend) and distances use the cells' curved centres.
    // Lamps light only the interiors (rendering layer 1), so no light falls through walls onto the hull outside. With
    // colliders (always in play) each room gets a mesh collider and its tile's window glass another, for StationWalker.
    // Each room hangs the sliding doors on its faces towards +x, +y and +z (its neighbour hangs the others); TickDoors
    // slides them open as someone comes near, in play the ring centre. The lifts (Lift, LiftAt) are found for the whole
    // station at Init. Up and Gravity give the walker its gravity: out from the core, fading to nothing towards it.
    // With people, each room gets a reference person or two (TileBuilder.Figure), standing on its floor for scale (in
    // the hub, on any wall; in a tube, on its walkway), each placed by casting from the room's middle onto its own mesh,
    // so they stand on the real, bent surfaces. They have no colliders.
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
        // Gravity is full from FullGravity metres out from the core and fades to nothing at HubRadius.
        public const float HubRadius=3f,FullGravity=15f;
        // A door opens when someone comes within DoorReach metres of its middle, taking DoorTime seconds.
        public const float DoorReach=2.4f,DoorTime=0.35f;

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
        // A cell's tile renderers (near level first), and whether and how people pass between two cells (TileBuilder).
        public MeshRenderer[] Tile(Vector3Int cell)=>tiles!=null&&tiles.TryGetValue(cell,out var r)?r:null;
        public bool Opens(Vector3Int a,Vector3Int b)=>builder!=null&&builder.Opens(a,b);
        public TileBuilder.Way Way(Vector3Int a,Vector3Int b)=>builder!=null?builder.WayBetween(a,b):TileBuilder.Way.None;
        // The face towards the core (zero in the hub), and the middle of a room's floor.
        public Vector3Int Down(Vector3Int c)=>builder.Down(c);
        // The cell that stands for the hall c belongs to (TileBuilder.Hall; itself if it stands alone).
        public Vector3Int Hall(Vector3Int c)=>builder.Hall(c);
        public bool Hub(Vector3Int c)=>builder.Hub(c);
        public Vector3 Floor(Vector3Int c){var d=builder.Down(c);return FaceCentre(c,d)-FaceNormal(c,d)*builder.Inset(c,d);}
        // The point of cell c's floor under p: its real surface (bent on the curved grid, as the room's mesh is), even
        // where an opening is cut in it, found where p lies across the floor; for a corridor, its walkway's plane.
        public Vector3 FloorUnder(Vector3Int c,Vector3 p)
        {
            var d=builder.Down(c);
            if(structure.cells[c].family==TileFamily.Corridor)
            {var n=FaceNormal(c,d);var w=Centre(c)+n*TileBuilder.WalkwayDepth;return p-n*Vector3.Dot(p-w,n);}
            var centre=TileStructure.Center(c)+(Vector3)d*(TileBuilder.Half-builder.Inset(c,d));
            if(warp==null)return p-(Vector3)d*Vector3.Dot(p-centre,(Vector3)d);
            var frame=new Vector4(d.x,d.y,d.z,Vector3.Dot((Vector3)d,TileStructure.Center(c))+TileBuilder.Half);
            Vector3 a=d.x!=0?Vector3.up:Vector3.right,b=Vector3.Cross(d,a);
            Vector3 F(float x,float y)=>warp.Position(centre+a*x+b*y,frame);
            float u=0,v=0;
            for(int i=0;i<3;i++)
            {
                var ta=F(u+0.25f,v)-F(u-0.25f,v);var tb=F(u,v+0.25f)-F(u,v-0.25f);var r=p-F(u,v);
                float aa=Vector3.Dot(ta,ta),ab=Vector3.Dot(ta,tb),bb=Vector3.Dot(tb,tb),det=aa*bb-ab*ab;if(det<1e-8f)break;
                float ra=Vector3.Dot(ta,r),rb=Vector3.Dot(tb,r);
                u+=0.5f*(bb*ra-ab*rb)/det;v+=0.5f*(aa*rb-ab*ra)/det;
            }
            return F(u,v);
        }
        public TileStructure Structure=>structure;

        // ---------------------------------------------------------------- gravity

        // Up at a point: the floors' own up (their faces towards the core, so out from it), blended between the rooms
        // round p by nearness (a 1.5 m Gaussian of the distance to each room's middle), so it is level on every floor
        // and turns smoothly from room to room across a door; where no floor is near, out from the core square to its
        // shells (CurvedTileMesh.Out; zero right at the core). Near the core, and on a guide of unequal extents, the bent
        // cells' floors stand well off the shells, so their own normals rather than the shells' keep them level. The
        // flat review stage's core lies far below it: +y.
        public Vector3 Up(Vector3 p)
        {
            if(warp==null)return Vector3.up;
            var sum=Vector3.zero;
            foreach(var (centre,up) in floors){float d2=(centre-p).sqrMagnitude;if(d2<36)sum+=up*Mathf.Exp(-d2/(1.5f*1.5f));}
            return sum.sqrMagnitude>1e-8f?sum.normalized:warp.Out(p);
        }
        readonly List<(Vector3 centre,Vector3 up)> floors=new List<(Vector3,Vector3)>();
        // Gravity at a point, as a share of full: none in the hub's rooms, fading in from HubRadius to full at
        // FullGravity out from the core. Full everywhere on the flat review stage.
        public float Gravity(Vector3 p)
        {
            if(warp==null)return 1;
            if(CellAt(p,out var c)&&builder.Hub(c))return 0;
            return Mathf.SmoothStep(0,1,Mathf.InverseLerp(HubRadius,FullGravity,(p-warp.Core).magnitude));
        }

        // ---------------------------------------------------------------- lifts

        // A lift: rooms stacked out from the core and open to each other (TileBuilder.Way.Lift), or a room's floor open
        // onto the hub. Its axis runs from the lowest floor (or 2 m into the hub room below its hatch) up through each
        // opening's centre to 2.2 m above the top floor; its stops are the floors people step off at (homes', and
        // junctions' walkways). StationWalker rides it with Space and walks across its openings at their floors' level.
        public sealed class Lift
        {
            public readonly List<Vector3> axis=new List<Vector3>(),stops=new List<Vector3>();
            // each stop's cell (FloorUnder: the floor people walk across the opening on)
            public readonly List<Vector3Int> stopCells=new List<Vector3Int>();
            public float radius;
            // Whether it starts in the hub (its axis's first point then lies in the hub room, below its hatch).
            public bool fromHub;
            // The point of the axis along metres from its start (clamped to its ends).
            public Vector3 At(float along)
            {
                for(int i=0;i+1<axis.Count;i++)
                {
                    float len=(axis[i+1]-axis[i]).magnitude;
                    if(along<=len||i+2==axis.Count)return len<1e-5f?axis[i]:Vector3.Lerp(axis[i],axis[i+1],Mathf.Clamp01(along/len));
                    along-=len;
                }
                return axis[0];
            }
            // How far along the axis the point of it nearest p lies, that point and the axis's direction there (up).
            public float Nearest(Vector3 p,out Vector3 at,out Vector3 up)
            {
                float best=float.MaxValue,along=0,run=0;at=axis[0];up=(axis[axis.Count-1]-axis[0]).normalized;
                for(int i=0;i+1<axis.Count;i++)
                {
                    var a=axis[i];var d=axis[i+1]-a;float len=d.magnitude;if(len<1e-5f)continue;var u=d/len;
                    float t=Mathf.Clamp(Vector3.Dot(p-a,u),0,len);var q=a+u*t;float dist=(p-q).sqrMagnitude;
                    if(dist<best){best=dist;at=q;up=u;along=run+t;}
                    run+=len;
                }
                return along;
            }
            public float Along(Vector3 p)=>Nearest(p,out _,out _);
        }
        readonly List<Lift> lifts=new List<Lift>();
        public IReadOnlyList<Lift> Lifts=>lifts;
        // The lift whose column holds p: within its opening's radius of the axis and reach more, so a body standing
        // anywhere over the opening is in it. Null if none.
        public Lift LiftAt(Vector3 p,float reach=0.3f)
        {
            foreach(var l in lifts){l.Nearest(p,out var at,out _);float r=l.radius+reach;if((p-at).sqrMagnitude<r*r)return l;}
            return null;
        }
        void FindLifts()
        {
            lifts.Clear();
            foreach(var c in centres.Keys)
            {
                var d=builder.Down(c);if(d==Vector3Int.zero)continue;
                var below=builder.WayBetween(c,c+d);
                bool hub=below!=TileBuilder.Way.None&&builder.Hub(c+d);
                if(below==TileBuilder.Way.Lift||!hub&&builder.WayBetween(c,c-d)!=TileBuilder.Way.Lift)continue;   // not a lift's foot
                var lift=new Lift();float r=0;
                bool Tube(Vector3Int a,Vector3Int b)=>structure.cells[a].family==TileFamily.Corridor||structure.cells[b].family==TileFamily.Corridor;
                if(hub)
                {
                    // from 2 m into the hub room below its hatch, where a ride down out of the lift leaves people floating
                    var hatch=FaceCentre(c,d);lift.axis.Add(hatch+(Centre(c+d)-hatch).normalized*2f);lift.axis.Add(hatch);lift.fromHub=true;
                    r=Tube(c,c+d)?TileBuilder.TubeInner:TileBuilder.HatchRadius;
                }
                else lift.axis.Add(Floor(c));
                var cur=c;
                while(true)
                {
                    var p=structure.cells[cur];
                    if(p.family!=TileFamily.Corridor){lift.stops.Add(Floor(cur));lift.stopCells.Add(cur);}
                    else{var links=structure.CorridorLinks(cur,p);if(links.Count!=2||links[0]!=-links[1]){lift.stops.Add(Centre(cur)+FaceNormal(cur,d)*TileBuilder.WalkwayDepth);lift.stopCells.Add(cur);}}
                    if(builder.WayBetween(cur,cur-d)!=TileBuilder.Way.Lift)break;
                    lift.axis.Add(FaceCentre(cur,-d));r=Mathf.Max(r,Tube(cur,cur-d)?TileBuilder.TubeInner:TileBuilder.HatchRadius);cur-=d;
                }
                lift.axis.Add(Floor(cur)-FaceNormal(cur,d)*2.2f);
                lift.radius=r;lifts.Add(lift);
            }
        }

        // ---------------------------------------------------------------- doors

        // A sliding door between two homes: its two leaves, where they hang shut and the way each slides open (station
        // space), how far, its middle and how open it is.
        sealed class Door{public Transform[] leaves=new Transform[2];public Vector3[] shut=new Vector3[2],slide=new Vector3[2];public Vector3 centre;public float travel,open;public Mesh mesh;}
        readonly Dictionary<Vector3Int,List<Door>> doors=new Dictionary<Vector3Int,List<Door>>();
        static readonly Finish LeafFinish=new Finish(TileSlot.Trim,TilePattern.Plain,new Color(0.72f,0.74f,0.78f),0f);
        Vector3 Station(Vector3 p)=>warp!=null?warp.Map(p):p;
        // The door in face d of cell (Way.Door) on the face's plane: its middle, the directions across and up it (their
        // lengths its width and height) and its normal.
        public void DoorAt(Vector3Int cell,Vector3Int d,out Vector3 centre,out Vector3 across,out Vector3 up,out Vector3 normal)
        {
            builder.DoorFrame(cell,d,out var a,out var u,out float sill,out float head,out float half);
            var fc=TileStructure.Center(cell)+(Vector3)d*TileBuilder.Half;float mid=(sill+head)*0.5f;
            Vector3 W(float x,float y)=>Station(fc+(Vector3)a*x+(Vector3)u*y);
            across=W(half,mid)-W(-half,mid);up=W(0,head)-W(0,sill);centre=(W(0,head)+W(0,sill))*0.5f;
            normal=Vector3.Cross(up,across).normalized;if(Vector3.Dot(normal,FaceNormal(cell,d))<0)normal=-normal;
        }
        // Each leaf is half the opening, shaped to it (on sheared cells it is a parallelogram), 6 cm thick on the face.
        void HangDoors(Vector3Int cell,Transform room)
        {
            List<Door> list=null;
            foreach(var d in TileStructure.Directions)
            {
                if(d.x+d.y+d.z<0||builder.WayBetween(cell,cell+d)!=TileBuilder.Way.Door)continue;
                DoorAt(cell,d,out var centre,out var across,out var up,out var normal);
                var corners=new Vector3[8];
                for(int i=0;i<8;i++)corners[i]=across*((i&1)!=0?0.25f:-0.25f)+up*((i&2)!=0?0.5f:-0.5f)+normal*((i&4)!=0?0.03f:-0.03f);
                var t=new TileMesh();t.Hexa(LeafFinish,corners);
                var door=new Door{centre=centre,travel=across.magnitude*0.5f,mesh=t.ToMesh("Door leaf")};door.mesh.hideFlags=HideFlags.DontSave;
                for(int i=0;i<2;i++)
                {
                    float side=i==0?-1:1;
                    var go=new GameObject("Door leaf"){hideFlags=HideFlags.DontSave};go.transform.SetParent(room,true);
                    door.shut[i]=centre+across*(0.25f*side);door.slide[i]=across.normalized*side;
                    go.transform.SetPositionAndRotation(Holder.TransformPoint(door.shut[i]),Holder.rotation);
                    go.AddComponent<MeshFilter>().sharedMesh=door.mesh;
                    var r=go.AddComponent<MeshRenderer>();r.sharedMaterials=materials;r.renderingLayerMask=1u|InteriorLayer;
                    if(Application.isPlaying||colliders){var c=go.AddComponent<MeshCollider>();c.sharedMesh=door.mesh;c.convex=true;}
                    door.leaves[i]=go.transform;
                }
                (list??=new List<Door>()).Add(door);
            }
            if(list!=null)doors[cell]=list;
        }
        // Opens each door whose middle lies within DoorReach of who and shuts the others, over dt.
        public void TickDoors(Vector3 who,float dt)
        {
            bool moved=false;var at=Holder.InverseTransformPoint(who);
            foreach(var list in doors.Values)foreach(var d in list)
            {
                float target=(at-d.centre).magnitude<DoorReach?1:0;if(d.open==target)continue;
                d.open=Mathf.MoveTowards(d.open,target,dt/DoorTime);float e=d.open*d.open*(3-2*d.open);moved=true;
                for(int i=0;i<2;i++)if(d.leaves[i]!=null)d.leaves[i].position=Holder.TransformPoint(d.shut[i]+d.slide[i]*(d.travel*e));
            }
            if(moved)Physics.SyncTransforms();
        }
        // The opening from cell into cell+d on their face, as a rectangle: its middle, the directions across and up it
        // and its half-sizes along them (a round way's square). False where it doesn't open.
        public bool Portal(Vector3Int cell,Vector3Int d,out Vector3 centre,out Vector3 across,out Vector3 up,out Vector2 half)
        {
            var way=Way(cell,cell+d);centre=across=up=default;half=default;
            if(way==TileBuilder.Way.None)return false;
            if(way==TileBuilder.Way.Open)
            {
                // no wall between two cells of a hall: the whole face, generously (it may be sheared)
                centre=FaceCentre(cell,d);var fn=FaceNormal(cell,d);
                across=Vector3.Cross(fn,Mathf.Abs(fn.y)<0.9f?Vector3.up:Vector3.right).normalized;up=Vector3.Cross(across,fn);
                half=Vector2.one*TileBuilder.Half*1.4f;return true;
            }
            if(way==TileBuilder.Way.Door)
            {
                DoorAt(cell,d,out centre,out var a,out var u,out _);
                across=a.normalized;up=u.normalized;half=new Vector2(a.magnitude,u.magnitude)*0.5f;return true;
            }
            centre=FaceCentre(cell,d);var n=FaceNormal(cell,d);
            across=Vector3.Cross(n,Mathf.Abs(n.y)<0.9f?Vector3.up:Vector3.right).normalized;up=Vector3.Cross(across,n);
            bool tube=structure.cells[cell].family==TileFamily.Corridor||structure.cells[cell+d].family==TileFamily.Corridor;
            half=Vector2.one*(tube?TileBuilder.TubeInner:TileBuilder.HatchRadius);return true;
        }
        // A cell's centre and the centre and outward normal of its face n, where they are (bent on the curved grid).
        public Vector3 Centre(Vector3Int c)=>warp!=null?warp.Map(TileStructure.Center(c)):TileStructure.Center(c);
        // A point in a cell's open room (TileBuilder.RoomPoint: its centre, unless a hangar bay or pit stands into it).
        public Vector3 RoomPoint(Vector3Int c)=>Station(builder.RoomPoint(c));
        public Vector3 FaceCentre(Vector3Int c,Vector3Int n)=>warp!=null?warp.Map(TileStructure.Center(c)+(Vector3)n*TileBuilder.Half):TileStructure.Center(c)+(Vector3)n*TileBuilder.Half;
        public Vector3 FaceNormal(Vector3Int c,Vector3Int n)=>builder.FaceNormal(c,n);
        // The interior cell whose centre is nearest p (within 4 m), or false.
        public bool CellAt(Vector3 p,out Vector3Int cell)
        {
            cell=default;float best=16;
            foreach(var pair in centres){float d=(pair.Value-p).sqrMagnitude;if(d<best){best=d;cell=pair.Key;}}
            if(best>=16)return false;
            // On the curved grid a sheared neighbour's centre can lie nearer than the centre of the cell p is in: find the
            // station point p maps from (a few Newton steps from the nearest centre) and take its cell, if it has an
            // interior.
            if(warp!=null)
            {
                var q=TileStructure.Center(cell);
                for(int i=0;i<4;i++)
                {
                    var r=warp.Map(q)-p;if(r.sqrMagnitude<1e-6f)break;
                    const float h=0.05f;
                    var j=new Matrix4x4();
                    for(int a=0;a<3;a++){var e=Vector3.zero;e[a]=h;var col=(warp.Map(q+e)-warp.Map(q-e))/(2*h);j.SetColumn(a,new Vector4(col.x,col.y,col.z,0));}
                    j.m33=1;q-=(Vector3)(j.inverse.MultiplyVector(r));
                }
                var inside=Vector3Int.FloorToInt(q/TileBuilder.Cell+Vector3.one*0.5f);
                if(centres.ContainsKey(inside))cell=inside;
            }
            return true;
        }
        // Where to stand to start: on the floor of the home nearest near that has a sliding door to another home (else to
        // any room; and, if any does, a wall open to space), 1.4 m back from its middle (clear of a floor opening), facing that wall, else the door. door: the
        // face to the next room.
        public bool Spawn(Vector3 near,out Vector3 feet,out Vector3 up,out Vector3 forward,out Vector3Int cell,out Vector3Int door)
        {
            feet=up=forward=default;cell=door=default;float best=float.MaxValue;bool found=false,windows=false;
            if(structure==null)return false;
            // a door to another home if any home has one, else a door to any room (homes side by side in one block are a
            // hall, with no door between them)
            for(int pass=0;pass<2&&!found;pass++)
            foreach(var pair in centres)
            {
                var c=pair.Key;var p=structure.cells[c];if(p.family!=TileFamily.Habitat||builder.Hub(c))continue;
                var down=builder.Down(c);
                Vector3Int w=default,h=default;bool hasW=false,hasH=false;
                foreach(var d in TileStructure.Directions)
                {
                    if(!hasW&&d!=down&&d!=-down&&!structure.TryGet(c+d,out _)){w=d;hasW=true;}
                    if(!hasH&&builder.WayBetween(c,c+d)==TileBuilder.Way.Door&&(pass>0||structure.cells[c+d].family==TileFamily.Habitat)){h=d;hasH=true;}
                }
                float dist=(pair.Value-near).sqrMagnitude;
                if(!hasH||windows&&!hasW||dist>=best&&hasW==windows)continue;
                best=dist;found=true;windows=hasW;cell=c;door=h;
                up=-FaceNormal(c,down);var look=FaceCentre(c,hasW?w:h);
                feet=Floor(c)-Vector3.ProjectOnPlane(look-Floor(c),up).normalized*1.4f;
                forward=Vector3.ProjectOnPlane(look-feet,up);
            }
            // upright in the station's up where it stands, which blends in the rooms open to this one
            if(found){var u=Up(feet+up*0.9f);if(u!=Vector3.zero){up=u;forward=Vector3.ProjectOnPlane(forward,up);}}
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
            floors.Clear();
            foreach(var c in centres.Keys)if(!builder.Hub(c))floors.Add((centres[c],-FaceNormal(c,builder.Down(c))));
            FindLifts();
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
            go.AddComponent<StationLocalLightSource>();
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
            HangDoors(cell,go.transform);
            var family=structure.cells[cell].family;
            if(family!=TileFamily.Command&&family!=TileFamily.Industrial)SetGlass(cell,clear);
            built[cell]=(go,mesh,glass);
        }

        void Drop(Vector3Int cell)
        {
            if(!built.TryGetValue(cell,out var b))return;
            built.Remove(cell);
            if(doors.TryGetValue(cell,out var hung)){foreach(var d in hung)Release(d.mesh);doors.Remove(cell);}
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
            TickDoors(ringCentre.position+ringCentre.up,Time.deltaTime);
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
        // One or two people in a home, one in a tube or the hub's junction, on its floor (in the hub, on walls the cell's
        // hash picks): each stands where a ray from the room's middle (moved across the wall, clear of its hatch or
        // socket) meets that wall square on, with room over its head; in a tube along a floor, on its walkway.
        void People(Vector3Int cell,Transform room,Mesh mesh)
        {
            var p=structure.cells[cell];var v=mesh.vertices;var t=mesh.triangles;
            for(int i=0;i<v.Length;i++)v[i]=room.TransformPoint(v[i]);
            uint h=(uint)(cell.x*73856093)^(uint)(cell.y*19349663)^(uint)(cell.z*83492791);
            float R(int k){uint x=h^(uint)(k*0x9E3779B1U);x^=x>>16;x*=0x7feb352dU;x^=x>>15;x*=0x846ca68bU;x^=x>>16;return (x&0xFFFFFF)/16777216f;}
            var c=centres[cell];bool tube=p.family==TileFamily.Corridor;
            var links=tube?structure.CorridorLinks(cell,p):null;
            bool run=tube&&links.Count==2&&links[0]==-links[1];
            var down=builder.Down(cell);bool hub=down==Vector3Int.zero;
            var walls=new List<Vector3Int>();
            foreach(var d in TileStructure.Directions)
                if(hub?!tube||(run?Vector3.Dot(d,links[0])==0:!links.Contains(d)):d==down&&(!tube||run&&Vector3.Dot(d,links[0])==0))walls.Add(d);
            if(walls.Count==0)return;
            int want=tube?1:1+(R(1)<0.6f?1:0),placed=0;
            for(int k=0;k<walls.Count*2&&placed<want;k++)
            {
                var n=walls[(int)(R(10+k)*walls.Count)%walls.Count];
                var dir=FaceNormal(cell,n);
                // two directions across the wall, as bent
                var a=TileStructure.Directions[0];foreach(var d in TileStructure.Directions)if(Vector3.Dot(d,n)==0){a=d;break;}
                var b=TileStructure.Cross(n,a);
                Vector3 Across(Vector3Int d)=>Vector3.ProjectOnPlane(Centre(cell+d)-c,dir).normalized;
                var origin=RoomPoint(cell);
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
