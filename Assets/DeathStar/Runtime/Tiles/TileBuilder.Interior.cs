using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Greybox interiors. Inside, up is always out from the station's core (StationWalker walks upright in its gravity):
    // a cube is one open room whose floor is the face towards the core (Down), whatever way its tile is turned, with light
    // bars on its ceiling. The flat review stage's core lies far below it, so there every floor is a -y face. The cells
    // explicitly marked zero-g are the hub (Hub): no floor, light bars on the two faces across the tile's axis, and
    // round hatches to every neighbour, as when people wore magnetic boots. Each inner wall stands Shell (0.4 m) in from
    // an exposed face, the depth of the window rows, so their glass sits in it, and Partition (0.1 m) in from a face shared
    // with a solid neighbour, so the wall between two rooms is 0.2 m thick and stays behind the mullion on every tile
    // boundary. Window rows and corridor portholes are real openings where they are outside: a tile's axis only shapes
    // its outside, so a home turned across the core has a skylight, a floor window and upright rows in two walls.
    // Neighbours whose floors face the same way open into each other: side by side through a sliding door at floor level
    // (Way.Door; StationInteriors hangs and slides its leaves), stacked out from the core through round openings in floor
    // and ceiling, a lift tube (Way.Lift; StationInteriors.LiftAt). Neighbours whose floors face different ways stay
    // closed. A corridor's tube (1.2 m inner apothem) runs on through a home's socket into the room and ends at a sealed
    // bulkhead against any other tile, in space, or at a room too small to take it; running along a floor it carries a
    // walkway WalkwayDepth below its spine, which a stair takes down to the room's floor, and running out from the core it is
    // a lift shaft. StationInteriors builds them near the viewer.
    // On the curved grid the inner walls keep their depth in metres from the curved faces (CurvedTileMesh), meet where
    // the sheared faces' inner surfaces cross, hatches, sockets and stairs are true size round their face's centre (fixed
    // parts) and tubes are bent like the corridor's own. Floors lie on the sphere's shells, so they are level, but walls
    // follow the lattice and lean by up to about 27 degrees near the cube's diagonals; doors stand upright in a block that
    // takes up the lean (TileBuilder.Portals.cs). Wedge and apex cells (the ridges and crowns) are too sheared to bend a room into: they build
    // their own (TileBuilder.SpecialRooms.cs), with doors to the rooms round them, and tubes that reach one end at a bulkhead.
    public sealed partial class TileBuilder
    {
        public const float Shell=0.4f,Partition=0.1f,TubeInner=1.2f,HatchRadius=0.9f;
        // A sliding door's opening, and the height of a tube's walkway below its spine (metres).
        public const float DoorWidth=1.3f,DoorHeight=2.2f,WalkwayDepth=0.9f;
        // Half-width of a tube's walkway: the inner octagon's width at its height.
        const float WalkwayHalf=TubeInner*0.4142f+TubeInner-WalkwayDepth-0.01f;
        // A stair's risers and treads: 0.25 m and 0.3 m, or as steep as 0.3 m and 0.2 m where the room is short of space.
        const float Riser=0.25f,Tread=0.3f,MaxRiser=0.3f,MinTread=0.2f,StairHalf=0.7f;
        // How people pass between neighbouring interiors: a sliding door, a lift, a hub hatch, a corridor's tube, or no
        // wall at all between two cells of one hall.
        public enum Way{None,Door,Lift,Hatch,Tube,Open}
        // Rooms of one family side by side on one floor join into halls of up to HallCells x HallCells; the blocks they
        // join in align on multiples of HallCells along the station's axes, so every cell agrees on them.
        public const int HallCells=4;
        // Half-width of a column where four cells of a hall meet.
        const float ColumnHalf=0.25f;
        // A junction's room is a box of this half-size, in metres, inside its 1.4 m box outside, so the tube's octagon
        // through each face keeps a frame round it; its rigid throats reach Throat from the centre.
        const float JunctionBox=1.32f,Throat=1.38f;
        // Width of the plate round a hatch or socket on the room side.
        const float Rim=0.22f,HatchRim=0.1f;
        const float PortholeHalf=0.24f;
        // Edges of Octagon() facing +pu and -pu: a corridor tube's sides, where its portholes sit.
        const int PortholeEdges=1<<0|1<<4;
        static readonly Finish InnerWall=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(1.5f,1.5f,1.5f),0f);
        static readonly Finish InnerFloor=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.42f,0.50f,0.57f),0f);
        static readonly Finish FloorEdge=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(1.7f,1.25f,0.55f),0f);
        static readonly Finish InnerTrim=new Finish(TileSlot.Trim,TilePattern.Plain,Color.white,0f);
        static readonly Finish InnerDark=new Finish(TileSlot.Dark,TilePattern.Plain,Color.white,0f);
        static readonly Finish Lamp=new Finish(TileSlot.Light,TilePattern.Plain,Color.white,0f).Mounted(Vector3.back);
        static readonly float[] NoPorts=new float[0];

        // Every solid family has rooms; a conduit's pipes and a truss's open frame have none.
        public static bool HasInterior(TileFamily f)=>f!=TileFamily.Conduit&&f!=TileFamily.Truss;
        public bool Interior(Vector3Int c)=>s.TryGet(c,out var p)&&HasInterior(p.family)&&(curved!=null&&s.special.ContainsKey(c)?SpecialRoom(c):p.family==TileFamily.Corridor||Roomy(c));

        // The cell's interior, centred on the cell like its tile (bend it like the tile on curved stages); empty for
        // families without one.
        public TileMesh BuildInterior(Vector3Int cell)
        {
            var m=new TileMesh{stationOffset=TileStructure.Center(cell),cellHalf=Half,retainWarpData=curved!=null};
            if(!Interior(cell))return m;
            var p=s.cells[cell];m.seed=Rand(Hash(cell),1);
            if(SpecialRoom(cell))SpecialInterior(m,cell);else if(p.family==TileFamily.Corridor)CorridorInterior(m,cell,p);else Room(m,cell,p);
            return m;
        }

        // Which way is down in a cell: the face towards the core (zero only for authored zero-g cells). On the curved grid, the face
        // whose normal points most nearly at the core; on the flat review stage, whose core lies far below it, -y.
        readonly Dictionary<Vector3Int,Vector3Int> downs=new Dictionary<Vector3Int,Vector3Int>();
        public Vector3Int Down(Vector3Int cell)
        {
            if(s.zeroGravity.Contains(cell))return Vector3Int.zero;
            if(curved==null)return Vector3Int.down;
            if(downs.TryGetValue(cell,out var d))return d;
            var p=curved.Map(TileStructure.Center(cell));d=Vector3Int.zero;
            // Even the core has a floor. At the exact centre use a deterministic deck direction.
            d=Vector3Int.down;
            if((p-curved.Core).sqrMagnitude>1e-6f)
            {
                float best=-2;var o=curved.Out(p);
                foreach(var n in TileStructure.Directions){float k=-Vector3.Dot(FaceNormal(cell,n),o);if(k>best){best=k;d=n;}}
            }
            return downs[cell]=d;
        }
        public bool Hub(Vector3Int cell)=>Down(cell)==Vector3Int.zero;

        // How people pass between neighbouring cells a and b. A corridor's tube links only along its runs. Into or within
        // the hub: a round hatch at the face's centre (a tube for a corridor). Otherwise both floors must face the same
        // way: stacked along it, a lift through floor and ceiling; side by side, a sliding door, or a tube through a
        // home's socket. A home whose face is too small for the opening and its frame stays closed.
        public Way WayBetween(Vector3Int a,Vector3Int b)
        {
            if(!Interior(a)||!Interior(b))return Way.None;
            var pa=s.cells[a];var pb=s.cells[b];var d=b-a;
            bool ca=pa.family==TileFamily.Corridor,cb=pb.family==TileFamily.Corridor,tube=ca||cb;
            if(ca&&!s.CorridorLinks(a,pa).Contains(d)||cb&&!s.CorridorLinks(b,pb).Contains(-d))return Way.None;
            if(SpecialRoom(a)||SpecialRoom(b))return !tube&&DoorBetween(a,b).open?Way.Door:Way.None;
            float r=tube?TubeInner:HatchRadius;
            bool round=ca&&cb||(ca||Fits(a,d,r))&&(cb||Fits(b,-d,r));
            // a hub's hatch takes a narrower plate, so the cells round the core, the smallest there are, still open
            if(Hub(a)||Hub(b))
            {
                if(tube)return round?Way.Tube:Way.None;
                // a room whose wall, not whose floor, meets the hub is left on foot, through a door at its floor
                if(Hub(a)!=Hub(b)&&AxisOf(Down(Hub(a)?b:a))!=AxisOf(d)&&DoorFits(a,d)&&DoorFits(b,-d))return Way.Door;
                return HatchFits(a,d)&&HatchFits(b,-d)?Way.Hatch:Way.None;
            }
            var down=Down(a);if(Down(b)!=down)return Way.None;
            // a hall's lift rises from the cell that stands for it, so stacked halls of one plan share one column
            if(d==down||d==-down)return round&&Hall(a)==a&&Hall(b)==b?Way.Lift:Way.None;
            if(Joins(a,b))return Way.Open;
            if(tube)return round?Way.Tube:Way.None;
            // floors that turn too far between them (a fold over the cube's edge) face different ways
            if(curved!=null&&Vector3.Angle(FloorNormal(a),FloorNormal(b))>DoorFold)return Way.None;
            return DoorFits(a,d)&&DoorFits(b,-d)?Way.Door:Way.None;
        }
        // Whether people pass between neighbouring cells a and b at all.
        public bool Opens(Vector3Int a,Vector3Int b)=>WayBetween(a,b)!=Way.None;
        bool Fits(Vector3Int c,Vector3Int n,float r)=>FaceRoom(c,n)*0.5f-Shell>=r+Rim;
        bool HatchFits(Vector3Int c,Vector3Int n)=>FaceRoom(c,n)*0.5f-Shell>=HatchRadius+HatchRim;
        bool DoorFits(Vector3Int c,Vector3Int n)=>FaceRoom(c,n)-2*Shell>=Mathf.Max(DoorWidth+2*Rim,DoorHeight+Rim);

        // The sliding door in face n of cell (Way.Door), on the face's plane round its centre: the lattice directions
        // across it and up it, where its sill and head lie up the face and its half-width across (face units: true size
        // once bent). The sill is the higher of the two rooms' floors, so both halves of the doorway and its leaves share
        // one opening.
        public void DoorFrame(Vector3Int cell,Vector3Int n,out Vector3Int across,out Vector3Int up,out float sill,out float head,out float half)
        {
            if(SpecialRoom(cell)||SpecialRoom(cell+n))
            {
                var door=DoorBetween(cell,cell+n);
                across=door.across;up=door.up;sill=door.sill;head=door.head;half=door.half;return;
            }
            // a door into the hub goes by the floor of the room on the other side
            if(Hub(cell)){cell+=n;n=-n;}
            var down=Down(cell);up=-down;across=TileStructure.Cross(n,up);
            var k=FaceScale(cell,n,across,up);
            sill=Mathf.Max(-Reach(cell,n,down),-Reach(cell+n,-n,down));
            head=sill+DoorHeight/k.y;half=DoorWidth*0.5f/k.x;
        }

        // Whether side-by-side neighbours a and b are one hall: rooms (not corridors) of one family whose floors face
        // the same way, in the same aligned block across their floors, and whose floors turn by at most HallFold
        // degrees between them (on the curved grid; a hall never folds over the cube's edge). A sliding door takes a
        // turn of up to DoorFold.
        const float HallFold=25,DoorFold=35;
        bool Joins(Vector3Int a,Vector3Int b)
        {
            if(!Interior(a)||!Interior(b)||SpecialRoom(a)||SpecialRoom(b))return false;
            var fa=s.cells[a].family;if(fa!=s.cells[b].family||fa==TileFamily.Corridor)return false;
            var down=Down(a);var d=b-a;
            if(down==Vector3Int.zero||Down(b)!=down||d==down||d==-down)return false;
            int ax=AxisOf(d);if(Block(a[ax])!=Block(b[ax]))return false;
            return curved==null||Vector3.Angle(FloorNormal(a),FloorNormal(b))<=HallFold;
        }
        readonly Dictionary<Vector3Int,Vector3> floorNormals=new Dictionary<Vector3Int,Vector3>();
        Vector3 FloorNormal(Vector3Int c)=>floorNormals.TryGetValue(c,out var n)?n:floorNormals[c]=FaceNormal(c,Down(c));
        static int Block(int v)=>v>=0?v/HallCells:(v+1)/HallCells-1;
        // The cell that stands for the hall cell belongs to (the first of its cells in x, then y, then z order; itself
        // if it stands alone), and how many cells the hall has.
        readonly Dictionary<Vector3Int,(Vector3Int first,int size)> halls=new Dictionary<Vector3Int,(Vector3Int,int)>();
        public Vector3Int Hall(Vector3Int cell)=>HallOf(cell).first;
        (Vector3Int first,int size) HallOf(Vector3Int cell)
        {
            if(halls.TryGetValue(cell,out var h))return h;
            var members=new List<Vector3Int>{cell};var seen=new HashSet<Vector3Int>{cell};
            for(int i=0;i<members.Count;i++)
                foreach(var d in TileStructure.Directions){var q=members[i]+d;if(!seen.Contains(q)&&Joins(members[i],q)){seen.Add(q);members.Add(q);}}
            var first=cell;
            foreach(var q in members)if(q.x<first.x||q.x==first.x&&(q.y<first.y||q.y==first.y&&q.z<first.z))first=q;
            h=(first,members.Count);foreach(var q in members)halls[q]=h;
            return h;
        }

        // Depth of the inner wall below face n: none towards a cell of the same hall, a thin partition against a solid
        // neighbour, the window rows' depth against anything else. In a hall, floor, walls and ceiling step by 0.3 m
        // where what lies behind them changes from cell to cell (a floor step is low enough to walk up), and a jamb
        // closes each step (Room).
        public float Inset(Vector3Int cell,Vector3Int n)=>Joins(cell,cell+n)?0:s.Solid(cell+n)?Partition:Shell;

        // The recesses deeper than the window rows that the family's forms cut in each open face of a cell (Face.wells),
        // found once. A cell whose recesses leave less than MinClear metres of room behind them (a hangar bay) is all bay
        // and has no room.
        const float MinClear=1.2f;
        readonly Dictionary<Vector3Int,List<(Vector3Int n,(Rect r,float from,float to) w)>> cellWells=new Dictionary<Vector3Int,List<(Vector3Int,(Rect,float,float))>>();
        // How many cells' recesses are being found, one inside another, and the most there have been (the lab keeps this small).
        int findingWells;
        public int DeepestWells {get;private set;}
        List<(Vector3Int n,(Rect r,float from,float to) w)> Wells(Vector3Int cell)
        {
            if(cellWells.TryGetValue(cell,out var list))return list;
            // empty while the forms run: a socket asks whether its tube opens into this cell's room
            cellWells[cell]=list=new List<(Vector3Int,(Rect,float,float))>();
            if(SpecialRoom(cell))return list;
            var found=new List<(Vector3Int,(Rect,float,float))>();var p=s.cells[cell];var primary=s.Primary(cell,p);
            findingWells++;DeepestWells=Mathf.Max(DeepestWells,findingWells);
            foreach(var n in TileStructure.Directions)
            {
                if(s.Solid(cell+n))continue;
                var face=Forms(new TileMesh{dry=true},cell,p,n,primary);face.m.Pop();
                foreach(var w in face.wells)found.Add((n,w));
            }
            findingWells--;
            list.AddRange(found);
            return list;
        }
        bool Roomy(Vector3Int cell)
        {
            foreach(var (n,w) in Wells(cell))if(w.to+Partition>Cell*Stretch(cell,n)-Shell-MinClear)return false;
            return true;
        }
        // Metres per station metre across a cell along n (1 on flat stages): recesses keep their depth in metres.
        float Stretch(Vector3Int cell,Vector3Int n)=>curved==null?1:Mathf.Max(0.05f,curved.Scale(TileStructure.Center(cell),(Vector3)n));
        // A point in the cell's room, in station space: its centre, moved out of any recess that stands into it from a
        // face, past its back, so it lies in the open room.
        readonly Dictionary<Vector3Int,Vector3> roomPoints=new Dictionary<Vector3Int,Vector3>();
        public Vector3 RoomPoint(Vector3Int cell)
        {
            if(roomPoints.TryGetValue(cell,out var known))return known;
            var c=TileStructure.Center(cell);
            if(!Interior(cell))return c;
            foreach(var (n,w) in Wells(cell))
            {
                float back=w.to+Partition,k=Stretch(cell,n);
                if(curved==null)
                {if(back>Half*k&&w.r.Contains(Vector2.zero))c-=(Vector3)n*((back+0.3f)/k-Half);continue;}
                // Recess depth follows the bent face normal, not a lattice axis. On a sheared cell,
                // the centre can lie inside a pit even when its unbent rectangle misses (0,0).
                var p=s.cells[cell];var y=n==p.axis||n==-p.axis?p.side:p.axis;var x=TileStructure.Cross(y,-n);
                var anchor=TileStructure.Center(cell)+(Vector3)n*Half;
                var frame=new Vector4(n.x,n.y,n.z,Vector3.Dot(anchor,n));
                var corners=new Vector3[8];var middle=Vector3.zero;
                for(int i=0;i<8;i++)
                {
                    var q=anchor+(Vector3)x*((i&1)==0?w.r.xMin:w.r.xMax)+(Vector3)y*((i&2)==0?w.r.yMin:w.r.yMax)-(Vector3)n*((i&4)==0?0:back);
                    middle+=(corners[i]=curved.Position(q,frame))*0.125f;
                }
                var planes=new Vector4[6];
                for(int f=0;f<6;f++)
                {
                    var a=corners[BlockConstruction.FaceCorners[f,0]];var b=corners[BlockConstruction.FaceCorners[f,1]];
                    var d=corners[BlockConstruction.FaceCorners[f,2]];var e=corners[BlockConstruction.FaceCorners[f,3]];
                    var normal=Vector3.Cross(d-a,e-b).normalized;if(Vector3.Dot(normal,(a+b+d+e)*0.25f-middle)<0)normal=-normal;
                    float depth=Mathf.Max(Mathf.Max(Vector3.Dot(normal,a),Vector3.Dot(normal,b)),Mathf.Max(Vector3.Dot(normal,d),Vector3.Dot(normal,e)));
                    planes[f]=new Vector4(normal.x,normal.y,normal.z,depth+0.15f);
                }
                for(int attempt=0;attempt<24;attempt++)
                {
                    var world=curved.Map(c);bool inside=true;
                    foreach(var plane in planes)if(Vector3.Dot((Vector3)plane,world)>plane.w){inside=false;break;}
                    if(!inside)break;
                    c-=(Vector3)n*(0.15f/k);
                }
            }
            return roomPoints[cell]=c;
        }
        static Rect Mirror(Rect r)=>Rect.MinMaxRect(-r.xMax,r.yMin,-r.xMin,r.yMax);
        // Metres per station metre along face n's x and y round its centre (1 on flat stages).
        Vector2 FaceScale(Vector3Int cell,Vector3Int n,Vector3Int x,Vector3Int y)=>
            curved==null?Vector2.one:curved.Room(TileStructure.Center(cell)+(Vector3)n*(Half-0.01f),x,y)/Cell;
        // Outward normal of face n of cell at its centre, on the curved grid as bent.
        readonly Dictionary<(Vector3Int,Vector3Int),Vector3> faceNormalCache=new Dictionary<(Vector3Int,Vector3Int),Vector3>();
        public Vector3 FaceNormal(Vector3Int cell,Vector3Int n)
        {
            if(curved==null)return n;
            if(faceNormalCache.TryGetValue((cell,n),out var normal))return normal;
            return faceNormalCache[(cell,n)]=CurvedNormal(n,TileStructure.Center(cell)+(Vector3)n*Half);
        }
        // Outward normal of curved face n at station point q on its plane.
        Vector3 CurvedNormal(Vector3Int n,Vector3 q)
        {
            int a=AxisOf(n);var u=Vector3.zero;u[(a+1)%3]=0.02f;var v=Vector3.zero;v[(a+2)%3]=0.02f;
            return Vector3.Cross(curved.Map(q+u)-curved.Map(q-u),curved.Map(q+v)-curved.Map(q-v)).normalized*n[a];
        }
        // Face units from the centre of face n to the room's edge towards d, where this wall's inner surface meets the
        // wall of face d, and then most of that wall's thickness past it, so the two always close without coming
        // through. A curved cell is sheared and its walls stand off their faces along the faces' smoothed normals, so
        // the meeting is measured on the bent surfaces themselves: where this wall's inner surface, across the middle of
        // the face, crosses the plane of the other's near their edge. Into a cell of the same hall (no wall d) it runs
        // Overlap past the face between them, so the two cells' walls never part. tn: the wall's depth, if not its own.
        const float Overlap=0.03f;
        readonly Dictionary<(Vector3Int,Vector3Int,Vector3Int,float),float> reaches=new Dictionary<(Vector3Int,Vector3Int,Vector3Int,float),float>();
        float Reach(Vector3Int cell,Vector3Int n,Vector3Int d)=>Reach(cell,n,d,Inset(cell,n));
        float Reach(Vector3Int cell,Vector3Int n,Vector3Int d,float tn)
        {
            var key=(cell,n,d,tn);if(reaches.TryGetValue(key,out float cached))return cached;
            float td=Inset(cell,d),past=Mathf.Min(0.1f,td*0.7f)+(td==0?Overlap:0);
            if(curved==null)return Half-td+past;
            var c=TileStructure.Center(cell);Vector3 faceN=c+(Vector3)n*Half,faceD=c+(Vector3)d*Half,along=TileStructure.Cross(n,d);
            var fn=new Vector4(n.x,n.y,n.z,Vector3.Dot((Vector3)n,c)+Half);var fd=new Vector4(d.x,d.y,d.z,Vector3.Dot((Vector3)d,c)+Half);
            // A bent face is twisted, so the meeting drifts along the edge: measured near both ends (the drift is
            // largest there), the wall runs as far as the further needs.
            float reach=0.2f;
            foreach(float e in new[]{-(Half-0.4f),Half-0.4f})
            {
                // the other wall's inner surface near the edge, as a plane (its normal out through face d)
                var near=faceD+(Vector3)n*(Half-0.3f)+along*e;var w=curved.Position(near-(Vector3)d*td,fd);var nd=CurvedNormal(d,near);
                // metres this wall's inner surface lies past that plane, u face units along d from face n's centre
                float S(float u)=>Vector3.Dot(curved.Position(faceN+(Vector3)d*u+along*e-(Vector3)n*tn,fn)-w,nd);
                const float u0=Half-0.6f,u1=Half;float s0=S(u0),s1=S(u1),k=(s1-s0)/(u1-u0);
                reach=Mathf.Max(reach,k<0.05f?Half-td:u0+(past-s0)/k);
            }
            return reaches[key]=reach;
        }

        // A room: six inner walls, each in the frame of the face outside it (x mirrored, so the wall faces in), at its
        // depth in metres below that face, each with what it opens onto: a sliding door, a round lift opening or hatch, a
        // corridor's socket and the stair up to its walkway, or the window rows. Light bars on the ceiling (in the hub,
        // on the faces across the tile's axis), a lit ring on the floor at the foot of a lift.
        void Room(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            var primary=s.Primary(cell,p);var c=TileStructure.Center(cell);var down=Down(cell);bool hub=down==Vector3Int.zero;
            foreach(var n in TileStructure.Directions)
            {
                var next=cell+n;var way=WayBetween(cell,next);
                if(way==Way.Open)continue;
                bool axial=n==p.axis||n==-p.axis;
                var y=axial?p.side:p.axis;var x=TileStructure.Cross(y,-n);
                float t=Inset(cell,n);var k=FaceScale(cell,n,x,y);
                var room=Rect.MinMaxRect(-Reach(cell,n,-x),-Reach(cell,n,-y),Reach(cell,n,x),Reach(cell,n,y));
                // station directions in the wall's own frame (the face's, x mirrored)
                Vector3 lx=TileStructure.Cross(y,n),ly=y;
                Vector2 L(Vector3 v)=>new Vector2(Vector3.Dot(v,lx),Vector3.Dot(v,ly));
                var holes=new List<Rect>();float opening=0;var plate=Vector2.zero;
                List<(Rect r,float from,float to)> wells=null;
                if(way==Way.Door)PortalHoles(holes,cell,n,L);
                else if(way!=Way.None)
                {
                    opening=s.cells[next].family==TileFamily.Corridor?TubeInner:HatchRadius;
                    var hole=Rect.MinMaxRect(-opening/k.x,-opening/k.y,opening/k.x,opening/k.y);holes.Add(hole);
                    plate=Plate(cell,n,x,y,t,hole,opening);
                }
                else if(!s.Solid(next))
                {
                    // the window rows the face's forms cut outside, where they fall in the room
                    var face=Forms(new TileMesh{dry=true},cell,p,n,primary);face.m.Pop();
                    foreach(var r in face.roomOpenings)
                    {
                        var w=Rect.MinMaxRect(Mathf.Max(r.xMin,room.xMin),Mathf.Max(r.yMin,room.yMin),Mathf.Min(r.xMax,room.xMax),Mathf.Min(r.yMax,room.yMax));
                        if(w.width>0.01f&&w.height>0.01f)holes.Add(Mirror(w));
                    }
                    wells=face.wells;
                }
                m.surfaceFrame=new Vector4(n.x,n.y,n.z,Vector3.Dot((Vector3)n,c)+Half);
                m.Push((Vector3)n*(Half-t),Quaternion.LookRotation((Vector3)n,(Vector3)y));
                m.Wall(n==down?InnerFloor:InnerWall,Mirror(room),holes);
                // Hull glazing sits 10 cm behind the room wall. Give its interior edge its
                // own lining; the exterior reveal's triangles can twist away on curved cells.
                if(p.family==TileFamily.Hull&&!s.Solid(next))foreach(var hole in holes)m.Reveal(InnerTrim,hole,t-0.5f);
                if(!hub&&n==down)FloorBorder(m,Mirror(room),holes,k);
                if(wells!=null)foreach(var w in wells)Well(m,w,wells,room,t);
                // Where this wall ends at a cell of the same hall whose own wall here stands nearer the face (or which has
                // none, being open onward), a jamb on the face between them closes the step.
                foreach(var d in TileStructure.Directions)
                {
                    if(d==n||d==-n||!Joins(cell,cell+d))continue;
                    float step=t-Inset(cell+d,n);if(step<1e-3f)continue;
                    // from where this wall meets that face to where the neighbour's would, on a sheared cell not square
                    var e=L((Vector3)d);var M=Mirror(room);float r0=Reach(cell,n,d),r1=Reach(cell,n,d,t-step);
                    if(Mathf.Abs(e.x)>0.5f)
                    {
                        float s0=Mathf.Sign(e.x);
                        m.Quad(InnerWall,new Vector3(s0*r0,M.yMin,0),new Vector3(s0*r0,M.yMax,0),new Vector3(s0*r1,M.yMax,step),new Vector3(s0*r1,M.yMin,step),new Vector3(e.x,0,0),Vector3.up);
                    }
                    else
                    {
                        float s0=Mathf.Sign(e.y);
                        m.Quad(InnerWall,new Vector3(M.xMin,s0*r0,0),new Vector3(M.xMax,s0*r0,0),new Vector3(M.xMax,s0*r1,step),new Vector3(M.xMin,s0*r1,step),new Vector3(0,e.y,0),Vector3.right);
                    }
                }
                // flush in a floor, so people walk across it
                if(opening>0)Opening(m,c+(Vector3)n*Half,opening,plate,t,opening==HatchRadius?InnerTrim:InnerWall,n==down?0.004f:0.03f);
                if(hub?axial:n==-down)Lamps(m,Mirror(room),opening>0?Mathf.Max(plate.x/k.x,plate.y/k.y):0,holes);
                if(!hub&&n==down&&opening==0)
                {
                    var over=WayBetween(cell,cell-down);
                    if(over==Way.Lift||over==Way.Hatch)LiftPad(m,s.cells[cell-down].family==TileFamily.Corridor?TubeInner:HatchRadius,k);
                }
                m.Pop();
            }
            m.surfaceFrame=Vector4.zero;
            if(!hub)Columns(m,cell,down);
            foreach(var n in TileStructure.Directions)if(WayBetween(cell,cell+n)==Way.Door)PortalHalf(m,cell,n);
            if(!hub)foreach(var n in TileStructure.Directions)DoorFlight(m,cell,n);
        }

        // A recess the face's forms cut deeper than the wall (a hangar bay, a pit) stands into the room as a closed
        // block, in the wall's frame (x mirrored, at depth t below the face): its sides from the wall, or from the back
        // of the recess it opens from, to Partition behind its own back, and that back, open where a deeper recess goes
        // on. Where it reaches the edge of the face it runs on to it, with no side there: behind the room's wall, which
        // closes it however that wall leans, or on into the next cell of a hall, which carries it on.
        static void Well(TileMesh m,(Rect r,float from,float to) w,List<(Rect r,float from,float to)> wells,Rect room,float t)
        {
            const float e=1e-3f;
            // sides where the recess stops short of the face's edges (face frame: left, right, bottom, top)
            bool l=w.r.xMin>-Half+e,r=w.r.xMax<Half-e,b=w.r.yMin>-Half+e,u=w.r.yMax<Half-e;
            var clip=Rect.MinMaxRect(Mathf.Min(room.xMin,-Half),Mathf.Min(room.yMin,-Half),Mathf.Max(room.xMax,Half),Mathf.Max(room.yMax,Half));
            var f=Rect.MinMaxRect(Mathf.Max(l?w.r.xMin:clip.xMin,clip.xMin),Mathf.Max(b?w.r.yMin:clip.yMin,clip.yMin),
                Mathf.Min(r?w.r.xMax:clip.xMax,clip.xMax),Mathf.Min(u?w.r.yMax:clip.yMax,clip.yMax));
            if(f.width<0.01f||f.height<0.01f)return;
            var R=Mirror(f);
            float z0=w.from>0?t-w.from-Partition:0,z1=t-w.to-Partition;
            // mirrored: the face's left side is the wall frame's right
            if(r)m.Quad(InnerWall,new Vector3(R.xMin,R.yMin,z0),new Vector3(R.xMin,R.yMax,z0),new Vector3(R.xMin,R.yMax,z1),new Vector3(R.xMin,R.yMin,z1),Vector3.left,Vector3.up);
            if(l)m.Quad(InnerWall,new Vector3(R.xMax,R.yMin,z0),new Vector3(R.xMax,R.yMax,z0),new Vector3(R.xMax,R.yMax,z1),new Vector3(R.xMax,R.yMin,z1),Vector3.right,Vector3.up);
            if(b)m.Quad(InnerWall,new Vector3(R.xMin,R.yMin,z0),new Vector3(R.xMax,R.yMin,z0),new Vector3(R.xMax,R.yMin,z1),new Vector3(R.xMin,R.yMin,z1),Vector3.down,Vector3.right);
            if(u)m.Quad(InnerWall,new Vector3(R.xMin,R.yMax,z0),new Vector3(R.xMax,R.yMax,z0),new Vector3(R.xMax,R.yMax,z1),new Vector3(R.xMin,R.yMax,z1),Vector3.up,Vector3.right);
            var deeper=new List<Rect>();
            foreach(var o in wells)if(Mathf.Abs(o.from-w.to)<e)deeper.Add(Mirror(o.r));
            m.Push(new Vector3(0,0,z1),Quaternion.identity);m.Wall(InnerWall,R,deeper);m.Pop();
        }

        // A column on each edge across the floor where four cells of one hall meet, floor to ceiling, built by the cell
        // on the edge's lower side along both axes.
        void Columns(TileMesh m,Vector3Int cell,Vector3Int down)
        {
            foreach(var a in TileStructure.Directions)foreach(var b in TileStructure.Directions)
            {
                if(a.x+a.y+a.z<0||b.x+b.y+b.z<0||AxisOf(a)>=AxisOf(b)||AxisOf(a)==AxisOf(down)||AxisOf(b)==AxisOf(down))continue;
                if(!Joins(cell,cell+a)||!Joins(cell,cell+b)||!Joins(cell+a,cell+a+b)||!Joins(cell+b,cell+a+b))continue;
                // a few centimetres into floor and ceiling, which keep their depth in metres from the bent faces
                float lo=-(Half-Inset(cell,down))-0.05f,hi=Half-Inset(cell,-down)+0.05f;
                m.Push(((Vector3)a+(Vector3)b)*Half,Quaternion.LookRotation(-(Vector3)down,(Vector3)a));
                m.Box(InnerWall,new Vector3(0,0,(lo+hi)*0.5f),new Vector3(2*ColumnHalf,2*ColumnHalf,hi-lo),0.04f);
                m.Pop();
            }
        }

        // Thin, flush wayfinding paint; clip every strip to the floor's real openings.
        static void FloorBorder(TileMesh m,Rect room,List<Rect> holes,Vector2 scale)
        {
            float x=0.04f/Mathf.Max(0.1f,scale.x),y=0.04f/Mathf.Max(0.1f,scale.y);
            room=Rect.MinMaxRect(room.xMin+x*2.5f,room.yMin+y*2.5f,room.xMax-x*2.5f,room.yMax-y*2.5f);
            m.Push(new Vector3(0,0,-0.006f),Quaternion.identity);
            m.Wall(FloorEdge,Rect.MinMaxRect(room.xMin,room.yMin,room.xMin+x,room.yMax),holes);
            m.Wall(FloorEdge,Rect.MinMaxRect(room.xMax-x,room.yMin,room.xMax,room.yMax),holes);
            m.Wall(FloorEdge,Rect.MinMaxRect(room.xMin+x,room.yMin,room.xMax-x,room.yMin+y),holes);
            m.Wall(FloorEdge,Rect.MinMaxRect(room.xMin+x,room.yMax-y,room.xMax-x,room.yMax),holes);
            m.Pop();
        }

        // A lit ring on a floor round the foot of a lift (a round opening of apothem r overhead), at the floor's centre;
        // k: the floor's metres per face unit.
        static void LiftPad(TileMesh m,float r,Vector2 k)
        {
            var inner=Octagon(r-0.06f);var outer=Octagon(r+0.06f);
            for(int i=0;i<8;i++){inner[i]=new Vector2(inner[i].x/k.x,inner[i].y/k.y);outer[i]=new Vector2(outer[i].x/k.x,outer[i].y/k.y);}
            m.Annulus(Lamp,inner,outer,Vector3.zero,Vector3.right,Vector3.up,Vector3.forward,-0.003f,-1);
        }

        // A tube's walkway: a plate WalkwayDepth below its spine towards down, from a0 to a1 along its run A, open over the
        // portholes at ports (half-size ha along the run, hc across) where they are in the floor.
        static void WalkwayPlate(TileMesh m,Vector3 down,Vector3 A,float a0,float a1,float[] ports,float ha,float hc)
        {
            var holes=new List<Rect>();foreach(var c in ports)holes.Add(Rect.MinMaxRect(-hc,c-ha,hc,c+ha));
            m.Push(down*WalkwayDepth,Quaternion.LookRotation(down,A));
            var deck=Rect.MinMaxRect(-WalkwayHalf,a0,WalkwayHalf,a1);
            m.Wall(InnerFloor,deck,holes);FloorBorder(m,deck,holes,Vector2.one);
            m.Pop();
        }

        // Two light bars across a wall's room, clear of a plate of half-size clear round its centre and of the wall's
        // holes (a ceiling open to space has skylights).
        static void Lamps(TileMesh m,Rect room,float clear,List<Rect> holes)
        {
            foreach(float side in new[]{-1f,1f})
            {
                float u=room.center.x+side*room.width*0.36f;
                if(Mathf.Abs(u)-0.07f<clear+0.05f)continue;
                var bar=new Rect(u-0.07f,room.center.y-room.height*0.275f,0.14f,room.height*0.55f);
                if(holes.Exists(h=>h.Overlaps(bar)))continue;
                m.Box(Lamp,new Vector3(u,room.center.y,-0.015f),new Vector3(0.14f,room.height*0.55f,0.03f));
            }
        }

        // Half-size in metres (across x and y) of the plate round an opening of apothem r in face n's inner wall at
        // depth t, which must cover the wall's hole (in face units): r + Rim on flat stages; on a curved one the hole
        // bends with the cells while the plate stays true size, so it takes the hole's bent corners in the plate's frame.
        Vector2 Plate(Vector3Int cell,Vector3Int n,Vector3Int x,Vector3Int y,float t,Rect hole,float r)
        {
            if(curved==null)return Vector2.one*(r+Rim);
            var c=TileStructure.Center(cell);Vector3 anchor=c+(Vector3)n*Half;
            var frame=new Vector4(n.x,n.y,n.z,Vector3.Dot((Vector3)n,c)+Half);
            var back=curved.Rigid(new Vector4(anchor.x,anchor.y,anchor.z,1),frame).inverse;
            float hx=0,hy=0;
            foreach(var (u,v) in new[]{(hole.xMin,hole.yMin),(hole.xMax,hole.yMin),(hole.xMin,hole.yMax),(hole.xMax,hole.yMax)})
            {
                var d=back.MultiplyPoint3x4(curved.Position(anchor+(Vector3)x*u+(Vector3)y*v-(Vector3)n*t,frame))-anchor;
                hx=Mathf.Max(hx,Mathf.Abs(Vector3.Dot(d,(Vector3)x)));hy=Mathf.Max(hy,Mathf.Abs(Vector3.Dot(d,(Vector3)y)));
            }
            return new Vector2(Mathf.Max(hx+0.05f,r+Rim),Mathf.Max(hy+0.05f,r+Rim));
        }

        // An octagonal way of apothem r through the centre of an inner wall (in its frame, facing -z) out to the tile's
        // face at depth t: a plate of half-size plate round it on the room side, which covers the wall's hole, and its
        // lining. A fixed part round anchor (the face's centre), so on curved stages it is true size and meets a
        // corridor's tube or the neighbour's half of a hatch exactly. proud: how far the plate stands off the wall.
        static void Opening(TileMesh m,Vector3 anchor,float r,Vector2 plate,float t,in Finish lining,float proud)
        {
            float X=plate.x,Y=plate.y,KX=X*0.4142f,KY=Y*0.4142f;
            var oct=Octagon(r);
            // the plate's outline: the octagon's corners pushed out along their own directions onto the rectangle
            var outer=new[]{new Vector2(X,-KX),new Vector2(X,KX),new Vector2(KY,Y),new Vector2(-KY,Y),new Vector2(-X,KX),new Vector2(-X,-KX),new Vector2(-KY,-Y),new Vector2(KY,-Y)};
            using(m.BeginFixed("opening",anchor))
            {
                m.Annulus(InnerTrim,oct,outer,Vector3.zero,Vector3.right,Vector3.up,Vector3.forward,-proud,-1);
                for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)
                    m.Triangle(InnerTrim,new Vector3(sx*X,sy*Y,-proud),new Vector3(sx*X,sy*KX,-proud),new Vector3(sx*KY,sy*Y,-proud),Vector3.back);
                m.Prism(InnerTrim,new[]{new Vector2(X,-Y),new Vector2(X,Y),new Vector2(-X,Y),new Vector2(-X,-Y)},Vector3.zero,Vector3.right,Vector3.up,Vector3.forward,-proud,0,false);
                for(int i=0;i<oct.Length;i++)TubeSide(m,lining,oct,i,Vector3.right,Vector3.up,Vector3.forward,-proud,t,NoPorts,0,0,true);
            }
        }

        // Side i of an octagonal tube (Octagon's edge from oct[i] to oct[i+1], across pu and pv), from a0 to a1 along A,
        // with square openings of half-size ha along A and hc across, centred at the places in ports. Faces out, or in
        // towards the tube's axis.
        static void TubeSide(TileMesh m,in Finish f,Vector2[] oct,int i,Vector3 pu,Vector3 pv,Vector3 A,float a0,float a1,float[] ports,float ha,float hc,bool inward)
        {
            Vector2 p=oct[i],q=oct[(i+1)%oct.Length];
            Vector3 P=pu*p.x+pv*p.y,Q=pu*q.x+pv*q.y,M=(P+Q)*0.5f,E=(Q-P).normalized,n=inward?-M.normalized:M.normalized;
            float h=(Q-P).magnitude*0.5f;
            var cuts=new List<float>{a0,a1};
            foreach(var c in ports){cuts.Add(Mathf.Clamp(c-ha,a0,a1));cuts.Add(Mathf.Clamp(c+ha,a0,a1));}
            cuts.Sort();
            for(int j=0;j<cuts.Count-1;j++)
            {
                float x0=cuts[j],x1=cuts[j+1];if(x1-x0<1e-4f)continue;
                float mid=(x0+x1)*0.5f;bool open=false;
                foreach(var c in ports)if(Mathf.Abs(mid-c)<ha)open=true;
                if(open)
                {
                    m.Quad(f,M-E*h+A*x0,M-E*hc+A*x0,M-E*hc+A*x1,M-E*h+A*x1,n,A);
                    m.Quad(f,M+E*hc+A*x0,M+E*h+A*x0,M+E*h+A*x1,M+E*hc+A*x1,n,A);
                }
                else m.Quad(f,M-E*h+A*x0,M+E*h+A*x0,M+E*h+A*x1,M-E*h+A*x1,n,A);
            }
        }

        // The sides of a porthole's opening through the tube wall, between the same side of the inner and outer octagons.
        static void PortholeReveal(TileMesh m,Vector2[] inner,Vector2[] outer,int i,Vector3 pu,Vector3 pv,Vector3 A,float c,float ha,float hc)
        {
            Vector3 Mid(Vector2[] o)=>pu*(o[i].x+o[(i+1)%o.Length].x)*0.5f+pv*(o[i].y+o[(i+1)%o.Length].y)*0.5f;
            Vector3 Mi=Mid(inner),Mo=Mid(outer);Vector2 d=inner[(i+1)%inner.Length]-inner[i];var E=(pu*d.x+pv*d.y).normalized;
            Vector3 At(Vector3 M,float e,float a)=>M+E*e+A*a;
            foreach(float s in new[]{-1f,1f})
            {
                float a=c+s*ha,e=s*hc;
                m.Quad(InnerTrim,At(Mi,-hc,a),At(Mi,hc,a),At(Mo,hc,a),At(Mo,-hc,a),-A*s,E);
                m.Quad(InnerTrim,At(Mi,e,c-ha),At(Mi,e,c+ha),At(Mo,e,c+ha),At(Mo,e,c-ha),-E*s,A);
            }
        }

        // A sealed end of a tube: an octagonal bulkhead at at along dir, facing back down the tube, with a round door.
        static void Bulkhead(TileMesh m,Vector2[] oct,Vector3 pu,Vector3 pv,Vector3 dir,float at)
        {
            var c=dir*at;
            for(int i=0;i<oct.Length;i++)
            {
                Vector2 p=oct[i],q=oct[(i+1)%oct.Length];
                m.Triangle(InnerWall,c,c+pu*p.x+pv*p.y,c+pu*q.x+pv*q.y,-dir);
            }
            m.Cylinder(InnerTrim,c,c-dir*0.03f,0.85f,24);
            m.Cylinder(InnerDark,c-dir*0.03f,c-dir*0.08f,0.72f,24);
        }

        // Lamp strips along a tube's four corner sides, from a0 to a1 along A; none on the two below a walkway towards
        // below.
        static void TubeLamps(TileMesh m,Vector2[] oct,Vector3 pu,Vector3 pv,Vector3 A,float a0,float a1,Vector3 below=default)
        {
            float len=a1-a0-0.5f;if(len<0.3f)return;
            for(int i=1;i<oct.Length;i+=2)
            {
                Vector2 p=oct[i],q=oct[(i+1)%oct.Length];
                var M=pu*(p.x+q.x)*0.5f+pv*(p.y+q.y)*0.5f;var n=M.normalized;
                if(Vector3.Dot(n,below)>0.5f)continue;
                m.Push(M-n*0.015f+A*((a0+a1)*0.5f),Quaternion.LookRotation(A,n));m.Box(Lamp.Mounted(Vector3.down),Vector3.zero,new Vector3(0.1f,0.03f,len));m.Pop();
            }
        }

        // The inside of the corridor's tube, bent like the tube outside (TileMesh.BeginTube): the same portholes,
        // stretched along the run like its ribs. Along a floor it has a walkway, open over portholes in the floor, and
        // lamps only in its upper corners; out from the core it is a lift shaft.
        void CorridorInterior(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            var links=s.CorridorLinks(cell,p);
            if(links.Count!=2||links[0]!=-links[1]){JunctionInterior(m,cell,p,links);return;}
            var run=links[0];var side=Vector3.Dot(run,p.side)==0?p.side:p.Third;
            var down=Down(cell);bool walk=down!=Vector3Int.zero&&Vector3.Dot(run,down)==0;
            Vector3 A=run,B=side,C=TileStructure.Cross(side,run);
            float stretch=RunStretch(cell,A),k=1/stretch;
            using var tube=m.BeginTube("corridor interior",TileStructure.Center(cell),A);
            var oct=Octagon(TubeInner);var outer=Octagon(1.4f);
            int ribs=Mathf.Max(1,Mathf.RoundToInt(Cell*stretch/1.25f));float pitch=Cell/ribs;
            var ports=new float[ribs-1];for(int r=1;r<ribs;r++)ports[r-1]=-Half+r*pitch;
            float lo=-Half,hi=Half;
            foreach(int e in new[]{-1,1})
            {
                if(Opens(cell,cell+run*e))continue;
                // short of what reaches in from the end: the docking collar's inner cap (0.6 m) where the run ends in
                // space, the neighbour's socket (0.5 m proud) where it ends against a tile
                float end=Half-0.65f*k;
                if(e<0)lo=-end;else hi=end;
                Bulkhead(m,oct,B,C,A*e,end);
            }
            for(int i=0;i<oct.Length;i++)TubeSide(m,InnerWall,oct,i,B,C,A,lo,hi,(PortholeEdges&(1<<i))!=0?ports:NoPorts,PortholeHalf*k,PortholeHalf,true);
            foreach(int i in new[]{0,4})foreach(var c in ports)PortholeReveal(m,oct,outer,i,B,C,A,c,PortholeHalf*k,PortholeHalf);
            if(walk)WalkwayPlate(m,(Vector3)down,A,lo,hi,Vector3.Dot((Vector3)down,B)!=0?ports:NoPorts,PortholeHalf*k,PortholeHalf);
            TubeLamps(m,oct,B,C,A,lo,hi,walk?(Vector3)down:Vector3.zero);
        }

        // A junction: a 2.4 m box room, open where a branch leaves it, and a tube along each branch. The box is rigid all
        // through (TileMesh.BeginRigid), like the junction's box outside, so it keeps its true size round the cell's
        // centre. A sheared run tilts the branch's bent lining against it, so each opening has a short rigid throat out
        // to Throat metres from the centre and a trim ring there, seen from both sides, that covers the joint with the
        // lining, which starts just behind it; the branches outside start beyond it. Outside the hub a walkway crosses the
        // box at the branches' walkways' height, open where a branch leaves it downwards.
        void JunctionInterior(TileMesh m,Vector3Int cell,in TilePlacement p,List<Vector3Int> links)
        {
            const float box=JunctionBox,r=TubeInner;
            var center=TileStructure.Center(cell);Vector3 up=p.axis;
            var oct=Octagon(TubeInner);var down=Down(cell);bool walk=down!=Vector3Int.zero;
            using(m.BeginRigid("junction interior",center,new Vector4(up.x,up.y,up.z,Vector3.Dot(center,up))))
            {
                if(walk)
                {
                    var holes=new List<Rect>();if(links.Contains(down))holes.Add(new Rect(-r,-r,2*r,2*r));
                    m.Push((Vector3)down*WalkwayDepth,Quaternion.LookRotation((Vector3)down,down.y!=0?Vector3.forward:Vector3.up));
                    var deck=new Rect(-box,-box,2*box,2*box);
                    m.Wall(InnerFloor,deck,holes);FloorBorder(m,deck,holes,Vector2.one);
                    m.Pop();
                }
                foreach(var d in TileStructure.Directions)
                {
                    // any up across d: the octagon's flats follow the station's axes either way
                    m.Push((Vector3)d*box,Quaternion.LookRotation((Vector3)d,d.y!=0?Vector3.forward:Vector3.up));
                    if(links.Contains(d))
                    {
                        float k=r*0.4142f;
                        m.Wall(InnerWall,new Rect(-box,-box,2*box,2*box),new List<Rect>{new Rect(-r,-r,2*r,2*r)});
                        for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)
                            m.Triangle(InnerWall,new Vector3(sx*r,sy*r,0),new Vector3(sx*r,sy*k,0),new Vector3(sx*k,sy*r,0),Vector3.back);
                        const float depth=Throat-box,plate=0.02f;
                        var ring=Octagon(r-0.2f);var rim=Octagon(r+0.05f);
                        for(int i=0;i<oct.Length;i++)TubeSide(m,InnerWall,oct,i,Vector3.right,Vector3.up,Vector3.forward,0,depth-plate,NoPorts,0,0,true);
                        m.Annulus(InnerTrim,ring,rim,Vector3.zero,Vector3.right,Vector3.up,Vector3.forward,depth-plate,-1);
                        m.Annulus(InnerTrim,ring,rim,Vector3.zero,Vector3.right,Vector3.up,Vector3.forward,depth,1);
                        for(int i=0;i<ring.Length;i++)TubeSide(m,InnerTrim,ring,i,Vector3.right,Vector3.up,Vector3.forward,depth-plate,depth,NoPorts,0,0,true);
                    }
                    else
                    {
                        m.Quad(InnerWall,new Vector3(-box,-box,0),new Vector3(box,-box,0),new Vector3(box,box,0),new Vector3(-box,box,0),Vector3.back,Vector3.right);
                        m.Box(Lamp,new Vector3(0,0,-0.015f),new Vector3(0.5f,0.5f,0.03f));
                    }
                    m.Pop();
                }
            }
            foreach(var d in links)
            {
                Vector3 a=d,b=Vector3.Dot(d,p.side)==0?p.side:p.Third,c=Vector3.Cross(b,a);
                float k=1/RunStretch(cell,a);
                using var branch=m.BeginTube("corridor interior branch",center,a);
                bool open=Opens(cell,cell+d);float start=Along(cell,a,Throat-0.05f);
                float end=open?Half:Mathf.Max(start,Half-(s.Solid(cell+d)?0.65f:0.5f)*k);
                for(int i=0;i<oct.Length;i++)TubeSide(m,InnerWall,oct,i,b,c,a,start,end,NoPorts,0,0,true);
                if(!open)Bulkhead(m,oct,b,c,a,end);
                if(walk&&Vector3.Dot(a,(Vector3)down)==0)WalkwayPlate(m,(Vector3)down,a,start,end,NoPorts,0,0);
            }
        }
    }
}
