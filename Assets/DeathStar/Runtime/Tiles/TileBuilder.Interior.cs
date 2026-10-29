using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Greybox interiors. Inside, up is always out from the station's core (StationWalker walks upright in its gravity):
    // a cube is one open room whose floor is the face towards the core (Down), whatever way its tile is turned, with light
    // bars on its ceiling. The flat review stage's core lies far below it, so there every floor is a -y face. The cells
    // round the core itself are the zero-g hub (Hub): no floor, light bars on the two faces across the tile's axis, and
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
    // follow the lattice and lean by up to about 26 degrees near the cube's diagonals; doors lean with them, tall enough
    // for an upright person. Wedge and apex cells (the ridges and crowns) have no interiors yet: they are too sheared for
    // a true-size room, so tubes that reach one end at a bulkhead.
    public sealed partial class TileBuilder
    {
        public const float Shell=0.4f,Partition=0.1f,TubeInner=1.2f,HatchRadius=0.9f;
        // A sliding door's opening, and the height of a tube's walkway below its spine (metres).
        public const float DoorWidth=1.3f,DoorHeight=2.2f,WalkwayDepth=0.9f;
        // A cell whose centre lies within this many cells of the core is part of the zero-g hub.
        const float HubCells=0.9f;
        // Half-width of a tube's walkway: the inner octagon's width at its height.
        const float WalkwayHalf=TubeInner*0.4142f+TubeInner-WalkwayDepth-0.01f;
        // A stair's risers and treads: 0.25 m and 0.3 m, or as steep as 0.3 m and 0.2 m where the room is short of space.
        const float Riser=0.25f,Tread=0.3f,MaxRiser=0.3f,MinTread=0.2f,StairHalf=0.7f;
        // How people pass between neighbouring interiors: a sliding door, a lift, a hub hatch, a corridor's tube.
        public enum Way{None,Door,Lift,Hatch,Tube}
        // A junction's room is a box of this half-size, in metres, inside its 1.4 m box outside, so the tube's octagon
        // through each face keeps a frame round it; its rigid throats reach Throat from the centre.
        const float JunctionBox=1.32f,Throat=1.38f;
        // Width of the plate round a hatch or socket on the room side.
        const float Rim=0.22f;
        const float PortholeHalf=0.24f;
        // Edges of Octagon() facing +pu and -pu: a corridor tube's sides, where its portholes sit.
        const int PortholeEdges=1<<0|1<<4;
        static readonly Finish InnerWall=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(1.5f,1.5f,1.5f),0f);
        static readonly Finish InnerTrim=new Finish(TileSlot.Trim,TilePattern.Plain,Color.white,0f);
        static readonly Finish InnerDark=new Finish(TileSlot.Dark,TilePattern.Plain,Color.white,0f);
        static readonly Finish Lamp=new Finish(TileSlot.Light,TilePattern.Plain,Color.white,0f).Mounted(Vector3.back);
        static readonly float[] NoPorts=new float[0];

        public static bool HasInterior(TileFamily f)=>f==TileFamily.Habitat||f==TileFamily.Corridor;
        public bool Interior(Vector3Int c)=>s.TryGet(c,out var p)&&HasInterior(p.family)&&!(curved!=null&&s.special.ContainsKey(c));

        // The cell's interior, centred on the cell like its tile (bend it like the tile on curved stages); empty for
        // families without one.
        public TileMesh BuildInterior(Vector3Int cell)
        {
            var m=new TileMesh{stationOffset=TileStructure.Center(cell),cellHalf=Half,retainWarpData=curved!=null};
            if(!Interior(cell))return m;
            var p=s.cells[cell];m.seed=Rand(Hash(cell),1);
            if(p.family==TileFamily.Corridor)CorridorInterior(m,cell,p);else Room(m,cell,p);
            return m;
        }

        // Which way is down in a cell: the face towards the core (zero for the hub's cells). On the curved grid, the face
        // whose normal points most nearly at the core; on the flat review stage, whose core lies far below it, -y.
        readonly Dictionary<Vector3Int,Vector3Int> downs=new Dictionary<Vector3Int,Vector3Int>();
        public Vector3Int Down(Vector3Int cell)
        {
            if(curved==null)return Vector3Int.down;
            if(downs.TryGetValue(cell,out var d))return d;
            var p=curved.Map(TileStructure.Center(cell));d=Vector3Int.zero;
            if((p-curved.Core).magnitude>=HubCells*Cell)
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
            float r=tube?TubeInner:HatchRadius;
            bool round=ca&&cb||(ca||Fits(a,d,r))&&(cb||Fits(b,-d,r));
            if(Hub(a)||Hub(b))return !round?Way.None:tube?Way.Tube:Way.Hatch;
            var down=Down(a);if(Down(b)!=down)return Way.None;
            if(d==down||d==-down)return round?Way.Lift:Way.None;
            if(tube)return round?Way.Tube:Way.None;
            return DoorFits(a,d)&&DoorFits(b,-d)?Way.Door:Way.None;
        }
        // Whether people pass between neighbouring cells a and b at all.
        public bool Opens(Vector3Int a,Vector3Int b)=>WayBetween(a,b)!=Way.None;
        bool Fits(Vector3Int c,Vector3Int n,float r)=>FaceRoom(c,n)*0.5f-Shell>=r+Rim;
        bool DoorFits(Vector3Int c,Vector3Int n)=>FaceRoom(c,n)-2*Shell>=Mathf.Max(DoorWidth+2*Rim,DoorHeight+Rim);

        // The sliding door in face n of cell (Way.Door), on the face's plane round its centre: the lattice directions
        // across it and up it, where its sill and head lie up the face and its half-width across (face units: true size
        // once bent). The sill is the higher of the two rooms' floors, so both halves of the doorway and its leaves share
        // one opening.
        public void DoorFrame(Vector3Int cell,Vector3Int n,out Vector3Int across,out Vector3Int up,out float sill,out float head,out float half)
        {
            var down=Down(cell);up=-down;across=TileStructure.Cross(n,up);
            var k=FaceScale(cell,n,across,up);
            sill=Mathf.Max(-Reach(cell,n,down),-Reach(cell+n,-n,down));
            head=sill+DoorHeight/k.y;half=DoorWidth*0.5f/k.x;
        }

        // Depth of the inner wall below face n: a thin partition against a solid neighbour, the window rows' depth
        // against anything else.
        public float Inset(Vector3Int cell,Vector3Int n)=>s.Solid(cell+n)?Partition:Shell;
        static Rect Mirror(Rect r)=>Rect.MinMaxRect(-r.xMax,r.yMin,-r.xMin,r.yMax);
        // Metres per station metre along face n's x and y round its centre (1 on flat stages).
        Vector2 FaceScale(Vector3Int cell,Vector3Int n,Vector3Int x,Vector3Int y)=>
            curved==null?Vector2.one:curved.Room(TileStructure.Center(cell)+(Vector3)n*(Half-0.01f),x,y)/Cell;
        // Outward normal of face n of cell at its centre, on the curved grid as bent.
        public Vector3 FaceNormal(Vector3Int cell,Vector3Int n)=>curved==null?(Vector3)n:CurvedNormal(n,TileStructure.Center(cell)+(Vector3)n*Half);
        // Outward normal of curved face n at station point q on its plane.
        Vector3 CurvedNormal(Vector3Int n,Vector3 q)
        {
            int a=AxisOf(n);var u=Vector3.zero;u[(a+1)%3]=0.02f;var v=Vector3.zero;v[(a+2)%3]=0.02f;
            return Vector3.Cross(curved.Map(q+u)-curved.Map(q-u),curved.Map(q+v)-curved.Map(q-v)).normalized*n[a];
        }
        // Face units from the centre of face n to the room's edge towards d, where this wall's inner surface meets the
        // wall of face d. A curved cell is sheared: the meeting follows the angle between the two faces at their edge,
        // in metres, and the wall runs most of the other wall's thickness past it (the faces' smoothed normals tilt the
        // inner surfaces by a few centimetres at their edges), so the two always close without coming through.
        float Reach(Vector3Int cell,Vector3Int n,Vector3Int d)
        {
            float tn=Inset(cell,n),td=Inset(cell,d);
            if(curved==null)return Half-td;
            var edge=TileStructure.Center(cell)+(Vector3)(n+d)*Half;
            Vector3 nn=CurvedNormal(n,edge-(Vector3)d*0.05f),nd=CurvedNormal(d,edge-(Vector3)n*0.05f);
            float cos=-Vector3.Dot(nn,nd),sin=Mathf.Max(0.3f,Mathf.Sqrt(Mathf.Max(0,1-cos*cos)));
            float metres=(td+tn*cos)/sin-Mathf.Min(0.1f,td*0.7f);
            return Mathf.Max(0.2f,Half-metres/Mathf.Max(0.05f,curved.Scale(edge-(Vector3)(n+d)*0.05f,d)));
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
                bool axial=n==p.axis||n==-p.axis;
                var y=axial?p.side:p.axis;var x=TileStructure.Cross(y,-n);
                float t=Inset(cell,n);var k=FaceScale(cell,n,x,y);
                var room=Rect.MinMaxRect(-Reach(cell,n,-x),-Reach(cell,n,-y),Reach(cell,n,x),Reach(cell,n,y));
                // station directions in the wall's own frame (the face's, x mirrored)
                Vector3 lx=TileStructure.Cross(y,n),ly=y;
                Vector2 L(Vector3 v)=>new Vector2(Vector3.Dot(v,lx),Vector3.Dot(v,ly));
                var holes=new List<Rect>();float opening=0;var plate=Vector2.zero;Rect door=default;
                var next=cell+n;var way=WayBetween(cell,next);
                if(way==Way.Door)
                {
                    DoorFrame(cell,n,out var across,out var up,out float sill,out float head,out float half);
                    Vector2 a=L((Vector3)across*half+(Vector3)up*sill),b=L(-(Vector3)across*half+(Vector3)up*head);
                    door=Rect.MinMaxRect(Mathf.Min(a.x,b.x),Mathf.Min(a.y,b.y),Mathf.Max(a.x,b.x),Mathf.Max(a.y,b.y));holes.Add(door);
                }
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
                    foreach(var r in face.recess)
                    {
                        var w=Rect.MinMaxRect(Mathf.Max(r.xMin,room.xMin),Mathf.Max(r.yMin,room.yMin),Mathf.Min(r.xMax,room.xMax),Mathf.Min(r.yMax,room.yMax));
                        if(w.width>0.01f&&w.height>0.01f)holes.Add(Mirror(w));
                    }
                }
                m.surfaceFrame=new Vector4(n.x,n.y,n.z,Vector3.Dot((Vector3)n,c)+Half);
                m.Push((Vector3)n*(Half-t),Quaternion.LookRotation((Vector3)n,(Vector3)y));
                m.Wall(InnerWall,Mirror(room),holes);
                // flush in a floor, so people walk across it
                if(opening>0)Opening(m,c+(Vector3)n*Half,opening,plate,t,opening==HatchRadius?InnerTrim:InnerWall,n==down?0.004f:0.03f);
                if(way==Way.Door)Doorway(m,door,L(-(Vector3)down),t);
                if(way==Way.Tube&&!hub)
                {
                    // clear of the floor's middle, and of a lift's opening there
                    var across=TileStructure.Cross(n,-down);var below=WayBetween(cell,cell+down);
                    float margin=below==Way.Lift||below==Way.Hatch?(s.cells[cell+down].family==TileFamily.Corridor?TubeInner:HatchRadius)+Rim+0.05f:0.4f;
                    float depth=(Half-t)*(curved!=null?curved.Scale(c,(Vector3)n):1);
                    Stairs(m,c+(Vector3)n*Half,L(-(Vector3)down),L((Vector3)across),Reach(cell,n,down)*FaceScale(cell,n,across,-down).y,t,depth-margin);
                }
                if(hub?axial:n==-down)Lamps(m,Mirror(room),opening>0?Mathf.Max(plate.x/k.x,plate.y/k.y):0,holes);
                if(!hub&&n==down&&opening==0)
                {
                    var over=WayBetween(cell,cell-down);
                    if(over==Way.Lift||over==Way.Hatch)LiftPad(m,s.cells[cell-down].family==TileFamily.Corridor?TubeInner:HatchRadius,k);
                }
                m.Pop();
            }
            m.surfaceFrame=Vector4.zero;
        }

        // A sliding door's lining through the wall to its face, sill included (the neighbour lines the rest, and
        // StationInteriors hangs the leaves on the face), and a frame round its jambs and head on the room side. up: the
        // wall frame's direction up the wall.
        static void Doorway(TileMesh m,Rect d,Vector2 up,float t)
        {
            const float w=0.12f,proud=0.03f;
            m.Reveal(InnerTrim,d,t);
            if(up.x<0.5f)m.Box(InnerTrim,new Vector3(d.xMin-w*0.5f,d.center.y,-proud*0.5f),new Vector3(w,d.height+2*w,proud));
            if(up.x>-0.5f)m.Box(InnerTrim,new Vector3(d.xMax+w*0.5f,d.center.y,-proud*0.5f),new Vector3(w,d.height+2*w,proud));
            if(up.y<0.5f)m.Box(InnerTrim,new Vector3(d.center.x,d.yMin-w*0.5f,-proud*0.5f),new Vector3(d.width,w,proud));
            if(up.y>-0.5f)m.Box(InnerTrim,new Vector3(d.center.x,d.yMax+w*0.5f,-proud*0.5f),new Vector3(d.width,w,proud));
        }

        // A lit ring on a floor round the foot of a lift (a round opening of apothem r overhead), at the floor's centre;
        // k: the floor's metres per face unit.
        static void LiftPad(TileMesh m,float r,Vector2 k)
        {
            var inner=Octagon(r-0.06f);var outer=Octagon(r+0.06f);
            for(int i=0;i<8;i++){inner[i]=new Vector2(inner[i].x/k.x,inner[i].y/k.y);outer[i]=new Vector2(outer[i].x/k.x,outer[i].y/k.y);}
            m.Annulus(Lamp,inner,outer,Vector3.zero,Vector3.right,Vector3.up,Vector3.forward,-0.003f,-1);
        }

        // From a room's floor up to the walkway of the tube its socket in this wall lets in: a landing through the
        // socket's lining at the walkway's height and a flight of steps down into the room, true size round the face's
        // centre (anchor). up and across: the wall frame's directions up the wall and across it; floor: metres from the
        // face's centre down to the floor; t: the wall's depth; clear: metres the flight may reach into the room (it
        // steepens to fit).
        static void Stairs(TileMesh m,Vector3 anchor,Vector2 up,Vector2 across,float floor,float t,float clear)
        {
            const float proud=0.03f;
            Vector3 V(float u,float a,float z)=>new Vector3(up.x*u+across.x*a,up.y*u+across.y*a,z);
            Vector3 S(float u,float a,float z)=>new Vector3(Mathf.Abs(up.x)*u+Mathf.Abs(across.x)*a,Mathf.Abs(up.y)*u+Mathf.Abs(across.y)*a,z);
            float rise=floor-WalkwayDepth;if(rise<0.05f)return;
            int steps=Mathf.CeilToInt(rise/Riser-1e-3f);float tread=Tread;
            if((steps-1)*tread>clear){steps=Mathf.Max(2,Mathf.CeilToInt(rise/MaxRiser-1e-3f));tread=Mathf.Clamp(clear/(steps-1),MinTread,Tread);}
            float h=rise/steps;
            using(m.BeginFixed("stairs",anchor))
            {
                m.Box(InnerTrim,V(-WalkwayDepth-0.03f,0,(t-proud)*0.5f),S(0.06f,2*WalkwayHalf,t+proud));
                for(int i=1;i<steps;i++)
                {
                    float top=-WalkwayDepth-i*h;
                    m.Box(InnerTrim,V((top-floor)*0.5f,0,-proud-(i-0.5f)*tread),S(top+floor,2*StairHalf,tread));
                }
            }
        }

        // A tube's walkway: a plate WalkwayDepth below its spine towards down, from a0 to a1 along its run A, open over the
        // portholes at ports (half-size ha along the run, hc across) where they are in the floor.
        static void WalkwayPlate(TileMesh m,Vector3 down,Vector3 A,float a0,float a1,float[] ports,float ha,float hc)
        {
            var holes=new List<Rect>();foreach(var c in ports)holes.Add(Rect.MinMaxRect(-hc,c-ha,hc,c+ha));
            m.Push(down*WalkwayDepth,Quaternion.LookRotation(down,A));
            m.Wall(InnerTrim,Rect.MinMaxRect(-WalkwayHalf,a0,WalkwayHalf,a1),holes);
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
                    m.Wall(InnerTrim,new Rect(-box,-box,2*box,2*box),holes);
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
