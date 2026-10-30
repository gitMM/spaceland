using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Rooms in wedge and apex cells. These cells are too sheared to bend a room into, and their two or three lower faces
    // meet in a V at the keel, so each is built in curved space from the cell's real corners (TileMesh.BeginWorld): a
    // convex room between six flat walls, each kept inside its bent face's four corners, and a flat floor across the V. A
    // section (BlockConstruction's cross-section at a wedge's end, or at an apex's lower face) decides where the floor
    // crosses it from its four corners alone, so the two cells sharing it agree. Up in these rooms is square to their
    // floor, keel towards crest, which lies between the floors either side.
    // Doors: through a section to the next wedge or apex; and through a lower face to a room whose floor is lower down,
    // usually near the keel: the door stands at this floor's height, with a landing and steps up to it on that room's side
    // (a hub's room floats, so it takes none). This is what joins a station's six sides. DoorBetween decides where a door
    // fits; it is built as every door is (TileBuilder.Portals.cs).
    public sealed partial class TileBuilder
    {
        // Wall depth below an exposed face (no window rows here), the floor's clear width across the V where the door above
        // it fits, and the narrowest floor and door people can use.
        const float ExposedSpecial=0.2f,SpecialFloorWidth=1.3f,SpecialMinWidth=0.75f,SpecialMinDoor=0.37f;
        // Least room to the eave above a door's sill along a lower face (metres), and the floor a room needs (m2).
        const float SpecialDoorSpace=DoorHeight+0.25f,SpecialMinFloor=0.8f;
        // An apex's three sides leave a small triangle at its sections' height: it stands a step above them.
        const float ApexStep=0.28f;
        // How far a ceiling stops below the highest corner, clear of a crest's ribs or a crown's hub.
        const float CrestCap=0.9f,ApexCap=1.3f;
        // The most a flight may climb to a door, the most a door may stand below that room's floor (a step), and the most
        // the two rooms' floors may differ across one (degrees).
        const float SpecialMaxRise=1.7f,SpecialStepDown=0.3f,SpecialDoorFold=45;

        sealed class SpecialShape
        {
            // Walls: inside where Dot(normals[i], x) <= depths[i], in TileStructure.Directions order.
            public readonly Vector3[] normals=new Vector3[6];public readonly float[] depths=new float[6];
            // Floor: above where Dot(up, x) >= floorDepth. Ceiling, under an open roof's kit: below capDepth, if capped.
            public Vector3 up;public float floorDepth,capDepth;public bool capped;
            public Vector3 point;public bool ok;
        }
        struct SpecialDoor
        {
            public bool open;
            // On the face: sill, sill, head, head, going round.
            public Vector3[] corners;
            // In the plain room's frame (DoorFrame), when one room is plain: how far its flight climbs in metres.
            public Vector3Int across,up;public float sill,head,half,rise;
        }
        readonly Dictionary<Vector3Int,SpecialShape> specialShapes=new Dictionary<Vector3Int,SpecialShape>();
        readonly Dictionary<(Vector3Int,Vector3Int),SpecialDoor> specialDoors=new Dictionary<(Vector3Int,Vector3Int),SpecialDoor>();

        // Whether c is a wedge or apex cell with a room.
        public bool SpecialRoom(Vector3Int c)
        {
            if(curved==null||!s.special.ContainsKey(c)||!s.TryGet(c,out var p)||!HasInterior(p.family)||p.family==TileFamily.Corridor)return false;
            return Down(c)!=Vector3Int.zero&&Shape(c).ok;
        }
        public Vector3 SpecialUp(Vector3Int c)=>Shape(c).up;
        // A point in the room, 1 m above the middle of its floor (in curved space).
        public Vector3 SpecialPoint(Vector3Int c)=>Shape(c).point;
        // Where the floor crosses a section: the middle of that line and its direction, and the half-width clear of the
        // walls. As high as gives SpecialFloorWidth, if a door still fits above that up both lower faces.
        static bool SectionFloor(in Section sec,out Vector3 mid,out Vector3 across,out float half)
        {
            mid=across=default;half=0;
            Vector3 ea=sec.eaveA-sec.keel,eb=sec.eaveB-sec.keel;
            float ha=Vector3.Dot(ea,sec.up),hb=Vector3.Dot(eb,sec.up);if(ha<0.2f||hb<0.2f)return false;
            // metres across per metre up, each side
            float ra=Vector3.ProjectOnPlane(ea,sec.up).magnitude/ha,rb=Vector3.ProjectOnPlane(eb,sec.up).magnitude/hb;
            float lost=Partition*(Mathf.Sqrt(1+ra*ra)+Mathf.Sqrt(1+rb*rb));
            float la=ea.magnitude,lb=eb.magnitude;
            float door=Mathf.Min((la-SpecialDoorSpace)*ha/la,(lb-SpecialDoorSpace)*hb/lb);
            float h=Mathf.Max(Mathf.Min((SpecialFloorWidth+lost)/(ra+rb),door),(SpecialMinWidth+lost)/(ra+rb));
            Vector3 pa=sec.keel+ea*(h/ha),pb=sec.keel+eb*(h/hb);
            mid=(pa+pb)*0.5f;across=(pb-pa).normalized;half=((pb-pa).magnitude-lost)*0.5f;
            return true;
        }
        // The section at face n of a wedge or apex (its spine's ends; an apex's lower faces). False on any other face.
        bool FaceSection(Vector3Int cell,Vector3Int n,out Section sec)
        {
            sec=default;var sh=s.special[cell];var roofs=RoofFaces(sh);int a=AxisOf(n);
            if(sh.kind==CellShape.Wedge)
            {
                if(a!=sh.spine||roofs.Count!=2)return false;
                var (ra,sa)=FaceAxis(roofs[0]);var (rb,sb)=FaceAxis(roofs[1]);
                sec=MakeSection(cell,a,n[a]>0?1:0,ra,sa,rb,sb);return true;
            }
            if(roofs.Count!=3)return false;
            var others=new List<(int axis,int sign)>();bool lower=false;
            foreach(var f in roofs){var (axis,sign)=FaceAxis(f);if(axis==a)lower=sign==-n[a];else others.Add((axis,sign));}
            if(!lower||others.Count!=2)return false;
            sec=MakeSection(cell,a,n[a]>0?1:0,others[0].axis,others[0].sign,others[1].axis,others[1].sign);
            return true;
        }

        SpecialShape Shape(Vector3Int cell)
        {
            if(specialShapes.TryGetValue(cell,out var shape))return shape;
            specialShapes[cell]=shape=new SpecialShape();
            var corners=new Vector3[8];var centre=Vector3.zero;
            for(int i=0;i<8;i++){corners[i]=curved.Corner(cell+BlockConstruction.CornerOffset(i));centre+=corners[i]*0.125f;}
            for(int f=0;f<6;f++)
            {
                Vector3 a=corners[BlockConstruction.FaceCorners[f,0]],b=corners[BlockConstruction.FaceCorners[f,1]],c=corners[BlockConstruction.FaceCorners[f,2]],d=corners[BlockConstruction.FaceCorners[f,3]];
                Vector3 n;float depth;
                if(FaceSection(cell,TileStructure.Directions[f],out var section))
                {
                    // a section folds across its eaves: the wall follows the part below them, which holds the floor and the door
                    n=Vector3.Cross(section.eaveA-section.keel,section.eaveB-section.keel).normalized;
                    if(Vector3.Dot(n,section.keel-centre)<0)n=-n;
                    depth=Vector3.Dot(n,section.keel);
                }
                else
                {
                    // a bent face is twisted: the wall stays inside all four of its corners
                    n=Vector3.Cross(c-a,d-b).normalized;if(Vector3.Dot(n,(a+b+c+d)*0.25f-centre)<0)n=-n;
                    depth=Mathf.Min(Vector3.Dot(n,a),Mathf.Min(Vector3.Dot(n,b),Mathf.Min(Vector3.Dot(n,c),Vector3.Dot(n,d))));
                }
                shape.normals[f]=n;shape.depths[f]=depth-(s.Solid(cell+TileStructure.Directions[f])?Partition:ExposedSpecial);
            }
            var mids=new List<Vector3>();var acrosses=new List<Vector3>();var ups=Vector3.zero;
            foreach(var n in TileStructure.Directions)
            {
                if(!FaceSection(cell,n,out var sec))continue;
                if(!SectionFloor(sec,out var mid,out var across,out _))return shape;
                mids.Add(mid);acrosses.Add(across);ups+=sec.up;
            }
            Vector3 up;
            if(mids.Count==2)up=Vector3.Cross(mids[1]-mids[0],acrosses[0]+acrosses[1]);
            else if(mids.Count==3)up=Vector3.Cross(mids[1]-mids[0],mids[2]-mids[0]);
            else return shape;
            up=up.normalized;if(Vector3.Dot(up,ups)<0)up=-up;
            var point=Vector3.zero;foreach(var mid in mids)point+=mid/mids.Count;
            shape.up=up;shape.floorDepth=Vector3.Dot(up,point)+(mids.Count==3?ApexStep:0);
            // a crest or crown stands in through an open roof: the room stops short of it
            var open=Exposure(cell,s.special[cell],out _,out _);
            if(open==CellExposure.Ridge||open==CellExposure.Crown||open==CellExposure.Half)
            {
                float top=float.MinValue;foreach(var q in corners)top=Mathf.Max(top,Vector3.Dot(up,q));
                shape.capped=true;shape.capDepth=top-(mids.Count==3?ApexCap:CrestCap);
            }
            var floor=SpecialPolygon(shape,-1,centre);
            float area=Area(floor);if(area<SpecialMinFloor)return shape;
            var middle=Vector3.zero;foreach(var q in floor)middle+=q/floor.Count;
            shape.point=middle+up;shape.ok=true;
            return shape;
        }

        // Keeps the part of a convex polygon where Dot(n, x) <= d.
        static List<Vector3> Clip(List<Vector3> polygon,Vector3 n,float d)
        {
            var kept=new List<Vector3>(polygon.Count+1);
            for(int i=0;i<polygon.Count;i++)
            {
                Vector3 p=polygon[i],q=polygon[(i+1)%polygon.Count];
                float dp=Vector3.Dot(n,p)-d,dq=Vector3.Dot(n,q)-d;
                if(dp<=0)kept.Add(p);
                if(dp<0&&dq>0||dp>0&&dq<0)kept.Add(p+(q-p)*(dp/(dp-dq)));
            }
            return kept;
        }
        static float Area(List<Vector3> polygon)
        {
            var sum=Vector3.zero;
            for(int i=1;i+1<polygon.Count;i++)sum+=Vector3.Cross(polygon[i]-polygon[0],polygon[i+1]-polygon[0]);
            return sum.magnitude*0.5f;
        }
        // The room's surface on wall face (or -1 its floor, -2 its ceiling): the plane cut by the room's other planes.
        static List<Vector3> SpecialPolygon(SpecialShape shape,int face,Vector3 near)
        {
            var n=face<0?shape.up:shape.normals[face];float d=face==-1?shape.floorDepth:face==-2?shape.capDepth:shape.depths[face];
            var centre=near-n*(Vector3.Dot(n,near)-d);
            var u=Vector3.Cross(n,Mathf.Abs(n.y)<0.9f?Vector3.up:Vector3.right).normalized*50;var v=Vector3.Cross(n,u);
            var polygon=new List<Vector3>{centre-u-v,centre+u-v,centre+u+v,centre-u+v};
            for(int j=0;j<6&&polygon.Count>0;j++)if(j!=face)polygon=Clip(polygon,shape.normals[j],shape.depths[j]);
            if(face!=-1&&polygon.Count>0)polygon=Clip(polygon,-shape.up,-shape.floorDepth);
            if(face!=-2&&shape.capped&&polygon.Count>0)polygon=Clip(polygon,shape.up,shape.capDepth);
            return polygon;
        }
        // Whether p (on face skip of the room) lies within its other walls, and no further below its floor than below.
        static bool SpecialHolds(SpecialShape shape,int skip,Vector3 p,float below)
        {
            for(int j=0;j<6;j++)if(j!=skip&&Vector3.Dot(shape.normals[j],p)>shape.depths[j])return false;
            if(shape.capped&&Vector3.Dot(shape.up,p)>shape.capDepth)return false;
            return Vector3.Dot(shape.up,p)>=shape.floorDepth-below;
        }
        static int FaceIndex(Vector3Int n)=>System.Array.IndexOf(TileStructure.Directions,n);

        // The door between neighbours a and b, at least one of them a wedge or apex.
        SpecialDoor DoorBetween(Vector3Int a,Vector3Int b)
        {
            var d=b-a;var key=d.x+d.y+d.z<0?(b,a):(a,b);
            if(specialDoors.TryGetValue(key,out var door))return door;
            specialDoors[key]=default;   // nothing asked while it's found may build on it
            door=SpecialRoom(a)&&SpecialRoom(b)?SectionDoor(a,b):SpecialRoom(a)?FaceDoor(a,b):FaceDoor(b,a);
            return specialDoors[key]=door;
        }
        // Upright in the section the two share, as wide as the floor there lets it be.
        SpecialDoor SectionDoor(Vector3Int a,Vector3Int b)
        {
            var d=b-a;var none=default(SpecialDoor);
            if(!FaceSection(a,d,out var sa)||!FaceSection(b,-d,out var sb))return none;
            if((sa.keel-sb.keel).sqrMagnitude>1e-4f||(sa.eaveA-sb.eaveA).sqrMagnitude+(sa.eaveB-sb.eaveB).sqrMagnitude>1e-3f&&(sa.eaveA-sb.eaveB).sqrMagnitude+(sa.eaveB-sb.eaveA).sqrMagnitude>1e-3f)return none;
            if(!SectionFloor(sa,out var mid,out var across,out float half))return none;
            var head=sa.up*DoorHeight;
            SpecialShape A=Shape(a),B=Shape(b);int fa=FaceIndex(d),fb=FaceIndex(-d);
            // as wide as fits between the walls of both rooms, which may stand in further than the floor's width allows for
            for(float w=Mathf.Min(half-0.02f,DoorWidth*0.5f);w>=SpecialMinDoor;w-=0.02f)
            {
                var corners=new[]{mid-across*w,mid+across*w,mid+across*w+head,mid-across*w+head};
                bool fits=true;
                foreach(var q in corners)fits&=SpecialHolds(A,fa,q,ApexStep+0.05f)&&SpecialHolds(B,fb,q,ApexStep+0.05f);
                if(fits)return new SpecialDoor{open=true,corners=corners};
            }
            return none;
        }
        // Through face n of the special room into a plain room (or the hub's): in that room's own wall, on the lattice
        // direction nearest this floor's up, with its sill where this floor crosses the face.
        SpecialDoor FaceDoor(Vector3Int special,Vector3Int plain)
        {
            var none=default(SpecialDoor);var n=plain-special;var back=-n;var shape=Shape(special);
            var down=Down(plain);bool hub=down==Vector3Int.zero;
            var fc=TileStructure.Center(plain)+(Vector3)back*Half;
            Vector3 W(Vector3 v)=>curved.Map(fc+v);
            Vector3Int up=-down;
            if(hub)
            {
                float best=-2;
                foreach(var e in TileStructure.Directions)
                {
                    if(AxisOf(e)==AxisOf(n))continue;
                    float k=Vector3.Dot((W((Vector3)e)-W(-(Vector3)e)).normalized,shape.up);if(k>best){best=k;up=e;}
                }
            }
            else if(AxisOf(up)==AxisOf(n)||Vector3.Angle(FloorNormal(plain),-shape.up)>SpecialDoorFold)return none;
            var across=TileStructure.Cross(back,up);
            float Above(float y)=>Vector3.Dot(shape.up,W((Vector3)up*y))-shape.floorDepth;
            float lo=-Half,hi=Half;
            if(Above(hi)<0)return none;
            if(Above(lo)<0)for(int i=0;i<16;i++){float m=(lo+hi)*0.5f;if(Above(m)<0)lo=m;else hi=m;}
            float sill=lo<=-Half&&Above(lo)>=0?lo:hi;
            var scale=FaceScale(plain,back,across,up);
            float rise=0;
            if(!hub)
            {
                float own=-Reach(plain,back,down);
                rise=(sill-own)*scale.y;
                if(rise>SpecialMaxRise)return none;
                if(rise<0){if(rise<-SpecialStepDown)return none;sill=own;rise=0;}
            }
            else if(sill<-Reach(plain,back,-up)+0.05f)return none;
            float head=sill+DoorHeight/scale.y,half=DoorWidth*0.5f/scale.x;
            if(head>Reach(plain,back,up)-0.1f)return none;
            if(half+Rim/scale.x>Mathf.Min(Reach(plain,back,across),Reach(plain,back,-across)))return none;
            Vector3 P(float x,float y)=>W((Vector3)across*x+(Vector3)up*y);
            var corners=new[]{P(-half,sill),P(half,sill),P(half,head),P(-half,head)};
            int face=FaceIndex(n);
            foreach(var q in corners)if(!SpecialHolds(shape,face,q,0.3f))return none;
            return new SpecialDoor{open=true,corners=corners,across=across,up=up,sill=sill,head=head,half=half,rise=rise};
        }

        // How far the flight climbs to the door in face n of plain room cell (0 if it has none).
        float DoorRise(Vector3Int cell,Vector3Int n)=>SpecialRoom(cell+n)?DoorBetween(cell,cell+n).rise:0;

        readonly Dictionary<(Vector3Int,Vector3Int),Vector3[]> roomWallCorners=new Dictionary<(Vector3Int,Vector3Int),Vector3[]>();
        Vector3[] RoomWallCorners(Vector3Int cell,Vector3Int n)
        {
            if(roomWallCorners.TryGetValue((cell,n),out var corners))return corners;
            var p=s.cells[cell];var y=n==p.axis||n==-p.axis?p.side:p.axis;var x=TileStructure.Cross(y,-n);
            var c=TileStructure.Center(cell);var anchor=c+(Vector3)n*(Half-Inset(cell,n));
            var frame=new Vector4(n.x,n.y,n.z,Vector3.Dot(c,n)+Half);
            var r=Rect.MinMaxRect(-Reach(cell,n,-x),-Reach(cell,n,-y),Reach(cell,n,x),Reach(cell,n,y));
            corners=new Vector3[4];
            for(int i=0;i<4;i++)corners[i]=curved.Position(anchor+(Vector3)x*((i&1)==0?r.xMin:r.xMax)+(Vector3)y*((i&2)==0?r.yMin:r.yMax),frame);
            return roomWallCorners[(cell,n)]=corners;
        }
        static bool WindowRayHits(Vector3 o,Vector3 d,Vector3 a,Vector3 b,Vector3 c)
        {
            var e=b-a;var f=c-a;var p=Vector3.Cross(d,f);float det=Vector3.Dot(e,p);if(Mathf.Abs(det)<1e-7f)return false;
            var t=o-a;float u=Vector3.Dot(t,p)/det;if(u<0||u>1)return false;
            var q=Vector3.Cross(t,e);float v=Vector3.Dot(d,q)/det;if(v<0||u+v>1)return false;
            float distance=Vector3.Dot(f,q)/det;return distance>=0&&distance<0.8f;
        }
        bool ClearHullWindow(Face f,Rect pane)
        {
            if(curved==null)return true;
            var frame=new Vector4(f.n.x,f.n.y,f.n.z,Vector3.Dot(TileStructure.Center(f.cell),f.n)+Half);
            foreach(var uv in new[]{pane.center,new Vector2(pane.xMin,pane.yMin),new Vector2(pane.xMax,pane.yMin),new Vector2(pane.xMax,pane.yMax),new Vector2(pane.xMin,pane.yMax)})
            {
                var at=f.At(uv.x,uv.y);var p=curved.Position(at-(Vector3)f.n*0.5f,frame);var inward=-CurvedNormal(f.n,at);
                foreach(var d in TileStructure.Directions)
                {
                    bool Blocks(in InteriorStep step)
                    {
                        var inverse=Quaternion.Inverse(step.rotation);var bounds=new Bounds(Vector3.zero,step.size+Vector3.one*0.04f);
                        return bounds.IntersectRay(new Ray(inverse*(p-step.centre),inverse*inward),out float distance)&&distance<0.8f;
                    }
                    foreach(var step in DoorSteps(f.cell,d))if(Blocks(step))return false;
                    if(WayBetween(f.cell,f.cell+d)==Way.Door&&Blocks(PortalBlock(f.cell,d)))return false;
                    // A deeply sheared side wall can also cross the view through a floor or
                    // wall pane. Reserve clearance to the neighbouring room faces as well.
                    if(d==f.n||d==-f.n||Joins(f.cell,f.cell+d))continue;
                    var q=RoomWallCorners(f.cell,d);
                    if(WindowRayHits(p,inward,q[0],q[1],q[3])||WindowRayHits(p,inward,q[0],q[3],q[2]))return false;
                }
            }
            return true;
        }

        void SpecialInterior(TileMesh m,Vector3Int cell)
        {
            var shape=Shape(cell);var c=TileStructure.Center(cell);
            var near=shape.point;
            var sh=s.special[cell];
            using(m.BeginWorld("special room"))
            {
                m.PushMatrix(Matrix4x4.Translate(-c));
                var floor=SpecialPolygon(shape,-1,near);
                Fan(m,InnerFloor,floor,shape.up);
                if(floor.Count>=3)
                {
                    var middle=Vector3.zero;foreach(var p in floor)middle+=p/floor.Count;
                    for(int i=0;i<floor.Count;i++)
                    {
                        var a=floor[i]+shape.up*0.006f;var b=floor[(i+1)%floor.Count]+shape.up*0.006f;
                        var inward=Vector3.Cross(shape.up,b-a).normalized;
                        if(Vector3.Dot(inward,middle-a)<0)inward=-inward;
                        m.Quad(FloorEdge,a,b,b+inward*0.04f,a+inward*0.04f,shape.up,(b-a).normalized);
                    }
                }
                if(shape.capped){var cap=SpecialPolygon(shape,-2,near);Fan(m,InnerWall,cap,-shape.up);SpecialLamp(m,cap,-shape.up);}
                for(int f=0;f<6;f++)
                {
                    var n=TileStructure.Directions[f];
                    var polygon=SpecialPolygon(shape,f,near);if(polygon.Count<3)continue;
                    var inward=-shape.normals[f];
                    if(WayBetween(cell,cell+n)==Way.Door)
                    {
                        // the opening, carried along its way into this room onto the wall; its block stands in front
                        var p=Portal(cell,n);var into=PortalInto(cell,n,p);var hole=new Vector3[4];float k=Vector3.Dot(shape.normals[f],into);float w=p.half+0.02f;
                        var q=new[]{p.Point(-w,0),p.Point(w,0),p.Point(w,p.height),p.Point(-w,p.height)};
                        for(int i=0;i<4;i++)hole[i]=q[i]+into*(Mathf.Abs(k)>1e-3f?(shape.depths[f]-Vector3.Dot(shape.normals[f],q[i]))/k:0);
                        Pierced(m,polygon,hole,inward);
                    }
                    else Fan(m,InnerWall,polygon,inward);
                    if((sh.roof>>f&1)!=0&&!shape.capped)SpecialLamp(m,polygon,inward);
                }
                m.Pop();
            }
            foreach(var n in TileStructure.Directions)if(WayBetween(cell,cell+n)==Way.Door)PortalHalf(m,cell,n);
        }
        static void Fan(TileMesh m,in Finish f,List<Vector3> polygon,Vector3 normal)
        {
            if(polygon.Count<3)return;
            m.BeginPolygon();
            for(int i=1;i+1<polygon.Count;i++)m.Triangle(f,polygon[0],polygon[i],polygon[i+1],normal);
            m.EndPolygon();
        }
        // A convex wall with a four-sided hole: the parts outside each of the hole's sides in turn.
        static void Pierced(TileMesh m,List<Vector3> polygon,Vector3[] hole,Vector3 normal)
        {
            var middle=(hole[0]+hole[1]+hole[2]+hole[3])*0.25f;var rest=polygon;
            for(int i=0;i<4&&rest.Count>=3;i++)
            {
                Vector3 p=hole[i],q=hole[(i+1)%4];
                var e=Vector3.Cross(q-p,normal).normalized;if(Vector3.Dot(e,p-middle)<0)e=-e;
                float d=Vector3.Dot(e,p);
                Fan(m,InnerWall,Clip(rest,-e,-d),normal);
                rest=Clip(rest,e,d);
            }
        }
        // A light bar along the longest edge's direction through the middle of a wall.
        static void SpecialLamp(TileMesh m,List<Vector3> polygon,Vector3 inward)
        {
            if(polygon.Count<3||Area(polygon)<1.5f)return;
            var middle=Vector3.zero;foreach(var q in polygon)middle+=q/polygon.Count;
            var along=Vector3.right;float longest=0;
            for(int i=0;i<polygon.Count;i++){var e=polygon[(i+1)%polygon.Count]-polygon[i];if(e.magnitude>longest){longest=e.magnitude;along=e.normalized;}}
            m.PushMatrix(Matrix4x4.TRS(middle,Quaternion.LookRotation(-inward,along),Vector3.one));
            m.Box(Lamp,new Vector3(0,0,-0.015f),new Vector3(0.14f,Mathf.Min(longest*0.45f,1.8f),0.03f));
            m.Pop();
        }
    }
}
