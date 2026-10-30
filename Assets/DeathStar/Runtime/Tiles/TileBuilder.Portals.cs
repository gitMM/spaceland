using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Doors and changes of level. Walls follow the lattice and lean, floors fold between rooms, but people stand upright:
    // a door is a flattened octagon through an upright block that takes up the wall's lean. The door's plane stands on the
    // two rooms' mean up, and on each side the block is upright in that room, its front a plane square to the room's
    // floor and its sides running level, so the two halves meet in a mitre where the leaves hang. The wall behind has a
    // hole only as big as the opening needs, hidden by the block, and each room builds its own half.
    // A door or a corridor's walkway above a room's floor comes down on a landing across the wall, between the room's
    // side walls, with steps across it where the rise is low and a central flight where it is high.
    public sealed partial class TileBuilder
    {
        // The door's trim, the block above its head, how far the block stands off the wall and past a leaf's travel.
        const float PortalTrim=0.1f,PortalHead=0.22f,PortalProud=0.04f,PortalPocket=0.06f,PortalMinSide=0.25f,PortalMaxDepth=1.7f;
        const float ChamferHead=0.34f,ChamferFoot=0.12f;
        // A landing's depth, how much it may lose, and the rise above which only a central flight comes down from it.
        const float LandingDepth=0.8f,LandingMinDepth=0.5f,WideStepRise=0.8f;
        // A landing stops this short of an exposed wall, whose window rows stand at its height.
        const float LandingWindowGap=0.55f;

        // In the station's curved space. across, up and normal are unit vectors, normal from the face's -x, -y or -z room to
        // its +x, +y or +z room; centre is the middle of the opening on the door's plane. half and height: the opening;
        // block and top: the half-width of the block and its top above the sill.
        public struct DoorPortal
        {
            public Vector3 centre,across,up,normal;public float half,height,block,top;
            public Vector3 Point(float w,float h)=>centre+across*w+up*(h-height*0.5f);
        }
        readonly Dictionary<(Vector3Int,Vector3Int),DoorPortal> portals=new Dictionary<(Vector3Int,Vector3Int),DoorPortal>();
        readonly Dictionary<(Vector3Int,Vector3Int),float> portalDepths=new Dictionary<(Vector3Int,Vector3Int),float>();

        // The door in face n of cell (Way.Door), the same from either room.
        public DoorPortal Portal(Vector3Int cell,Vector3Int n)
        {
            var key=n.x+n.y+n.z<0?(cell+n,-n):(cell,n);
            if(portals.TryGetValue(key,out var p))return p;
            portals[key]=default;   // nothing asked while it's found may build on it
            return portals[key]=MakePortal(key.Item1,key.Item2);
        }
        Vector3 RoomUp(Vector3Int c)=>SpecialRoom(c)?SpecialUp(c):Hub(c)?Vector3.zero:-FaceNormal(c,Down(c));
        Vector3 Curved(Vector3 p)=>curved!=null?curved.Map(p):p;

        DoorPortal MakePortal(Vector3Int a,Vector3Int d)
        {
            var b=a+d;var up=(RoomUp(a)+RoomUp(b)).normalized;
            float half;Vector3 sill,wallNormal,along;bool special=SpecialRoom(a)||SpecialRoom(b);
            if(special)
            {
                var q=DoorBetween(a,b).corners;
                sill=(q[0]+q[1])*0.5f;along=q[1]-q[0];half=along.magnitude*0.5f;
                wallNormal=Vector3.Cross(along,q[3]-q[0]).normalized;if(Vector3.Dot(wallNormal,FaceNormal(a,d))<0)wallNormal=-wallNormal;
            }
            else
            {
                DoorFrame(a,d,out var across,out var lattice,out float low,out _,out _);
                var fc=TileStructure.Center(a)+(Vector3)d*Half;
                sill=Curved(fc+(Vector3)lattice*low);along=Curved(fc+(Vector3)across*0.5f)-Curved(fc-(Vector3)across*0.5f);half=DoorWidth*0.5f;
                wallNormal=FaceNormal(a,d);
            }
            var normal=Vector3.ProjectOnPlane(wallNormal,up).normalized;
            var w=Vector3.Cross(up,normal).normalized;if(Vector3.Dot(w,along)<0)w=-w;
            // the door's plane crosses the wall at mid height, so the block stands off it evenly to either side
            float H=DoorHeight;
            var centre=sill+up*(H*0.5f)-normal*(H*0.5f*Vector3.Dot(up,wallNormal)/Vector3.Dot(normal,wallNormal));
            // a wall's lattice can run across a door on a slant, and the hole cut along it rises above the head at one end
            float slant=special?0:Mathf.Abs(Vector3.Dot(along,up))/Mathf.Max(0.1f,Vector3.ProjectOnPlane(along,up).magnitude);
            float top=H+Mathf.Max(PortalHead,(2*half-Mathf.Min(ChamferHead,half*0.5f))*slant+0.05f);
            foreach(var c in new[]{a,b})
                if(SpecialRoom(c)&&Shape(c).capped)top=Mathf.Min(top,Mathf.Max(H+0.05f,(Shape(c).capDepth-Vector3.Dot(Shape(c).up,centre-up*(H*0.5f)))/Mathf.Max(0.3f,Vector3.Dot(Shape(c).up,up))-0.05f));
            // the room's side walls either side of the door, near its sill and at its top, in both rooms
            float left=-3,right=3;
            foreach(float h in new[]{0.1f,top})
            {
                var at=centre+up*(h-H*0.5f);
                WallLimits(a,d,at,w,normal,ref left,ref right);WallLimits(b,-d,at,w,normal,ref left,ref right);
            }
            if(!special)
            {
                // a door shifts towards the wider side
                float shift=Mathf.Clamp((left+right)*0.5f,-0.6f,0.6f);
                centre+=w*shift;left-=shift;right-=shift;
            }
            float block=Mathf.Max(half+PortalMinSide,Mathf.Min(Mathf.Min(-left,right),2*half+PortalPocket));
            var p=new DoorPortal{centre=centre,across=w,up=up,normal=normal,half=half,height=H,block=block,top=top};
            // the wall's lean adds to one room's fold and takes from the other's: the door's plane moves so that each room's
            // half of the block stands as deep
            portals[(a,d)]=p;
            float da=PortalDepth(a,d),db=PortalDepth(b,-d),move=(da-db)*0.5f;
            p.centre-=normal*move;
            portalDepths[(a,d)]=Mathf.Max(da-move*Mathf.Abs(Vector3.Dot(normal,PortalInto(a,d,p))),Inset(a,d)+PortalProud);
            portalDepths[(b,-d)]=Mathf.Max(db+move*Mathf.Abs(Vector3.Dot(normal,PortalInto(b,-d,p))),Inset(b,-d)+PortalProud);
            // a sill never lies under either room's floor
            float raise=0;
            foreach(var c in new[]{a,b})if(!SpecialRoom(c)&&!Hub(c))raise=Mathf.Max(raise,FloorAbove(c,p.Point(0,0),up));
            p.centre+=up*Mathf.Clamp(raise,0,0.12f);
            return p;
        }

        // The wall of a room facing along w either way, seen from at: narrows left and right along w.
        void WallLimits(Vector3Int cell,Vector3Int n,Vector3 at,Vector3 w,Vector3 normal,ref float left,ref float right)
        {
            if(SpecialRoom(cell))
            {
                var shape=Shape(cell);int skip=FaceIndex(n);
                for(int j=0;j<6;j++)
                {
                    float k=Vector3.Dot(shape.normals[j],w);if(j==skip||Mathf.Abs(k)<0.3f)continue;
                    float s=(shape.depths[j]-Vector3.Dot(shape.normals[j],at))/k;
                    if(k>0)right=Mathf.Min(right,s);else left=Mathf.Max(left,s);
                }
                return;
            }
            foreach(var e in TileStructure.Directions)
            {
                if(AxisOf(e)==AxisOf(n))continue;
                float k=Vector3.Dot(FaceNormal(cell,e),w);if(Mathf.Abs(k)<0.3f||!WallHit(cell,e,at,w,out float s,out _,out _,out _))continue;
                // clear of the window rows in an exposed wall, as it leans towards the block's front
                if(!this.s.Solid(cell+e))
                {
                    var front=at+(n.x+n.y+n.z>0?-normal:normal)*0.8f;
                    if(WallHit(cell,e,front,w,out float s2,out _,out _,out _))s=k>0?Mathf.Min(s,s2):Mathf.Max(s,s2);
                    s-=LandingWindowGap*Mathf.Sign(k);
                }
                if(k>0)right=Mathf.Min(right,s);else left=Mathf.Max(left,s);
            }
        }

        // Where the line from q along dir meets the inner surface of wall n of a plain room: how far along, the point, the
        // wall's outward normal there and the offset across the face from the wall's middle, in station units. False where
        // the line runs along the wall.
        bool WallHit(Vector3Int cell,Vector3Int n,Vector3 q,Vector3 dir,out float along,out Vector3 point,out Vector3 normal,out Vector3 offset)
        {
            var c=TileStructure.Center(cell);float t=Inset(cell,n);var origin=c+(Vector3)n*(Half-t);
            offset=Vector3.zero;along=0;point=q;normal=n;
            if(curved==null)
            {
                float k=Vector3.Dot(dir,(Vector3)n);if(Mathf.Abs(k)<1e-4f)return false;
                along=Vector3.Dot(origin-q,(Vector3)n)/k;point=q+dir*along;offset=Vector3.ProjectOnPlane(point-origin,(Vector3)n);
                return true;
            }
            var frame=new Vector4(n.x,n.y,n.z,Vector3.Dot((Vector3)n,c)+Half);
            int ax=AxisOf(n);var e1=Vector3.zero;e1[(ax+1)%3]=1;var e2=Vector3.zero;e2[(ax+2)%3]=1;
            const float eps=0.05f;
            // the wall hardly bends: its slopes at the middle serve every step
            point=curved.Position(origin,frame);
            var ex=(curved.Position(origin+e1*eps,frame)-point)/eps;var ey=(curved.Position(origin+e2*eps,frame)-point)/eps;
            normal=Vector3.Cross(ex,ey);if(normal.sqrMagnitude<1e-8f)return false;
            normal.Normalize();if(Vector3.Dot(normal,FaceNormal(cell,n))<0)normal=-normal;
            var cross=Vector3.Cross(ey,-dir);float det=Vector3.Dot(ex,cross);if(Mathf.Abs(det)<1e-4f)return false;
            for(int i=0;i<2;i++)
            {
                // point + a ex + b ey = q + s dir
                var r=q+dir*along-point;
                offset+=e1*(Vector3.Dot(r,cross)/det)+e2*(Vector3.Dot(ex,Vector3.Cross(r,-dir))/det);
                along+=Vector3.Dot(ex,Vector3.Cross(ey,r))/det;
                point=curved.Position(origin+offset,frame);
            }
            along=Vector3.Dot(point-q,dir);
            return true;
        }
        // How far a room's floor lies above q along up; a hub's has none, so it reports a little below.
        float FloorAbove(Vector3Int cell,Vector3 q,Vector3 up)
        {
            if(SpecialRoom(cell)){var shape=Shape(cell);return (shape.floorDepth-Vector3.Dot(shape.up,q))/Mathf.Max(0.3f,Vector3.Dot(shape.up,up));}
            if(Hub(cell))return -0.15f;
            return WallHit(cell,Down(cell),q,up,out float s,out _,out _,out _)?s:0;
        }
        // Level in this room, from the door's plane into it.
        Vector3 PortalInto(Vector3Int cell,Vector3Int n,in DoorPortal p)
        {
            var into=n.x+n.y+n.z>0?-p.normal:p.normal;var up=RoomUp(cell);
            return up==Vector3.zero?into:Vector3.ProjectOnPlane(into,up).normalized;
        }
        // The door's plane to the block's front in this room, along PortalInto: past the wall behind every corner of the
        // front, so the wall never comes through it.
        float PortalDepth(Vector3Int cell,Vector3Int n)
        {
            if(portalDepths.TryGetValue((cell,n),out float known))return known;
            var p=Portal(cell,n);var into=PortalInto(cell,n,p);float t=Inset(cell,n),depth=t+PortalProud;
            float low=Mathf.Min(0,FloorAbove(cell,p.Point(0,0),p.up))-0.1f;
            var shape=SpecialRoom(cell)?Shape(cell):null;int f=FaceIndex(n);
            foreach(float w in new[]{-p.block,p.block})foreach(float h in new[]{low,p.top})
            {
                var q=p.Point(w,h);float s;
                if(shape!=null){float k=Vector3.Dot(shape.normals[f],into);if(Mathf.Abs(k)<1e-3f)continue;s=(shape.depths[f]-Vector3.Dot(shape.normals[f],q))/k;}
                else if(!WallHit(cell,n,q,into,out s,out _,out _,out _))continue;
                depth=Mathf.Max(depth,Vector3.Dot(q+into*s-p.centre,into)+PortalProud);
            }
            return portalDepths[(cell,n)]=Mathf.Min(depth,PortalMaxDepth);
        }

        // The hole a door takes in this room's wall, in the wall's own frame (L: a station direction in that frame): rows
        // that follow the opening up the leaning wall, so the block in front covers them.
        void PortalHoles(List<Rect> holes,Vector3Int cell,Vector3Int n,System.Func<Vector3,Vector2> L)
        {
            var p=Portal(cell,n);var into=PortalInto(cell,n,p);float w=p.half+0.02f,H=p.height;
            if(!WallHit(cell,n,p.Point(-w,0),into,out _,out _,out _,out var o0)||!WallHit(cell,n,p.Point(w,0),into,out _,out _,out _,out var o1)||
               !WallHit(cell,n,p.Point(-w,H),into,out _,out _,out _,out var o2)||!WallHit(cell,n,p.Point(w,H),into,out _,out _,out _,out var o3))return;
            Vector2 a=L(o0),b=L(o1),c=L(o2),d=L(o3);
            // the wall hardly bends across a door: a point of the opening from its four corners
            Vector2 At(float x,float h){float u=(x+w)/(2*w),v=h/H;return Vector2.Lerp(Vector2.Lerp(a,b,u),Vector2.Lerp(c,d,u),v);}
            void Row(float h0,float x0,float h1,float x1)
            {
                Vector2 q0=At(-x0,h0),q1=At(x0,h0),q2=At(-x1,h1),q3=At(x1,h1);
                holes.Add(Rect.MinMaxRect(Mathf.Min(Mathf.Min(q0.x,q1.x),Mathf.Min(q2.x,q3.x)),Mathf.Min(Mathf.Min(q0.y,q1.y),Mathf.Min(q2.y,q3.y)),Mathf.Max(Mathf.Max(q0.x,q1.x),Mathf.Max(q2.x,q3.x)),Mathf.Max(Mathf.Max(q0.y,q1.y),Mathf.Max(q2.y,q3.y))));
            }
            float drift=Mathf.Min(Mathf.Abs((c-a).x),Mathf.Abs((c-a).y)),slant=Mathf.Min(Mathf.Abs((b-a).x),Mathf.Abs((b-a).y));
            if(drift<=0.05f&&slant<=0.05f){Row(0,w,H,w);return;}
            // a row for the chamfered head, narrowing, so its corners stay under the block's top
            float chamfer=Mathf.Min(ChamferHead,p.half*0.5f),below=H-chamfer;int rows=Mathf.Clamp(Mathf.CeilToInt(drift/0.25f),1,4);
            for(int i=0;i<rows;i++)Row(below*i/rows,w,below*(i+1)/rows,w);
            Row(below,w,H,w-chamfer);
        }

        // The opening's outline from the sill, going round from the sill's +across end: the two sides of a flattened octagon.
        public static Vector2[] DoorOutline(float half,float height,float grow=0)
        {
            float head=Mathf.Min(ChamferHead,half*0.5f)+grow*0.4142f,foot=Mathf.Min(ChamferFoot,half*0.18f)+grow*0.4142f;
            float w=half+grow,h=height+grow;
            return new[]{new Vector2(w-foot,0),new Vector2(w,foot),new Vector2(w,h-head),new Vector2(w-head,h),new Vector2(-w+head,h),new Vector2(-w,h-head),new Vector2(-w,foot),new Vector2(-w+foot,0)};
        }

        // This room's half of a door's block as a solid, for what must stay clear of it: corners as TileMesh.Hexa takes them.
        InteriorStep PortalBlock(Vector3Int cell,Vector3Int n)
        {
            var p=Portal(cell,n);var into=PortalInto(cell,n,p);float depth=PortalDepth(cell,n),low=Mathf.Min(0,FloorAbove(cell,p.Point(0,0),p.up))-0.06f;
            var c=new Vector3[8];
            for(int k=0;k<8;k++)
            {
                var q=p.Point((k&1)!=0?p.block:-p.block,(k&2)!=0?p.top:low);
                c[k]=(k&4)!=0?q+into*(depth-Vector3.Dot(q-p.centre,into)):q;
            }
            return new InteriorStep(c,into,p.up,false);
        }

        // This room's half of a door's block: its front with the opening and trim, its sides and top back to the door's
        // plane, the opening's lining and the threshold, which climbs to a floor standing above the sill.
        void PortalHalf(TileMesh m,Vector3Int cell,Vector3Int n)
        {
            var p=Portal(cell,n);var into=PortalInto(cell,n,p);float depth=PortalDepth(cell,n);
            var inner=DoorOutline(p.half,p.height);var ring=DoorOutline(p.half,p.height,PortalTrim);
            float B=p.block,top=p.top,low=0;
            Vector3 Front(Vector3 v)=>v+into*(depth-Vector3.Dot(v-p.centre,into));
            foreach(float w in new[]{-B,B})low=Mathf.Min(low,FloorAbove(cell,Front(p.Point(w,0)),p.up));
            low=Mathf.Min(low,FloorAbove(cell,Front(p.Point(0,0)),p.up))-0.06f;
            float ringTop=Mathf.Min(top-0.01f,ring[3].y);
            // the block's edge opposite each point of the trim's outline
            var edge=new[]{new Vector2(ring[0].x,low),new Vector2(B,ring[1].y),new Vector2(B,ring[2].y),new Vector2(ring[3].x,top),new Vector2(ring[4].x,top),new Vector2(-B,ring[5].y),new Vector2(-B,ring[6].y),new Vector2(ring[7].x,low)};
            for(int i=0;i<8;i++)ring[i].y=Mathf.Min(ring[i].y,ringTop);
            Vector3 F(Vector2 v)=>Front(p.Point(v.x,v.y));Vector3 P(Vector2 v)=>p.Point(v.x,v.y);
            var mid=p.Point(0,p.height*0.5f);
            using(m.BeginWorld("door block"))
            {
                m.PushMatrix(Matrix4x4.Translate(-TileStructure.Center(cell)));
                void Ring(in Finish f,Vector2[] a,Vector2[] b,int from)
                {
                    for(int i=from;i<8;i++)
                    {
                        int j=(i+1)%8;if(i==7&&from>0)continue;
                        m.Quad(f,F(a[i]),F(a[j]),F(b[j]),F(b[i]),into,p.across);
                    }
                }
                Ring(InnerTrim,inner,ring,0);
                Ring(InnerWall,ring,edge,0);
                foreach(var (x,y,i) in new[]{(B,low,0),(B,top,2),(-B,top,4),(-B,low,6)})
                    m.Triangle(InnerWall,F(edge[i]),F(edge[i+1]),F(new Vector2(x,y)),into);
                // sides and top, from the front back to the door's plane
                void Side(Vector2 a,Vector2 b,Vector3 normal)=>m.Quad(InnerWall,P(a),P(b),F(b),F(a),normal,into);
                Side(new Vector2(B,low),new Vector2(B,top),p.across);
                Side(new Vector2(-B,low),new Vector2(-B,top),-p.across);
                Side(new Vector2(-B,top),new Vector2(B,top),p.up);
                // the lining, all round but the sill
                for(int i=0;i<7;i++)
                {
                    var a=inner[i];var b=inner[i+1];
                    m.Quad(InnerTrim,P(a),P(b),F(b),F(a),(mid-(P(a)+P(b))*0.5f).normalized,into);
                }
                // the threshold
                var s0=F(inner[7]);var s1=F(inner[0]);
                float h0=FloorAbove(cell,s0,p.up),h1=FloorAbove(cell,s1,p.up);
                if(h0>0&&h0<0.4f)s0+=p.up*(h0+0.005f);if(h1>0&&h1<0.4f)s1+=p.up*(h1+0.005f);
                var at0=P(inner[7]);var at1=P(inner[0]);
                m.Quad(InnerTrim,at0,at1,s1,s0,p.up,p.across);
                // a warm sill identifies the bottom of every doorway from either side
                var lip=(s0-at0).normalized*0.04f;
                m.Quad(FloorEdge,at0+p.up*0.003f,at1+p.up*0.003f,at1+lip+p.up*0.003f,at0+lip+p.up*0.003f,p.up,p.across);
                m.Pop();
            }
        }

        readonly struct InteriorStep
        {
            public readonly Vector3 centre,size;public readonly Quaternion rotation;public readonly Vector3[] corners;public readonly bool nosing;
            // corners as TileMesh.Hexa takes them, along right, up and into
            public InteriorStep(Vector3[] corners,Vector3 into,Vector3 up,bool nosing)
            {
                this.corners=corners;this.nosing=nosing;rotation=Quaternion.LookRotation(into,up);
                var back=Quaternion.Inverse(rotation);var b=new Bounds(back*corners[0],Vector3.zero);
                foreach(var q in corners)b.Encapsulate(back*q);
                centre=rotation*b.center;size=b.size;
            }
        }
        readonly Dictionary<(Vector3Int,Vector3Int),List<InteriorStep>> doorSteps=new Dictionary<(Vector3Int,Vector3Int),List<InteriorStep>>();

        // The landing and steps down from a door or a corridor's walkway in face n of a plain room to its floor. Shared by
        // stair construction and window placement: glazing needs clearance behind it.
        List<InteriorStep> DoorSteps(Vector3Int cell,Vector3Int n)
        {
            if(doorSteps.TryGetValue((cell,n),out var stepsFound))return stepsFound;
            var result=new List<InteriorStep>();doorSteps[(cell,n)]=result;
            if(SpecialRoom(cell)||!s.TryGet(cell,out var placed)||placed.family==TileFamily.Corridor)return result;
            var down=Down(cell);if(down==Vector3Int.zero||AxisOf(n)==AxisOf(down))return result;
            var way=WayBetween(cell,cell+n);var up=RoomUp(cell);
            Vector3 into,sill;float start=0;bool tube=way==Way.Tube;
            if(way==Way.Door)
            {
                if(DoorRise(cell,n)<=0.05f)return result;
                var p=Portal(cell,n);into=PortalInto(cell,n,p);sill=p.Point(0,0);start=PortalDepth(cell,n)-Vector3.Dot(sill-p.centre,into);
            }
            else if(tube)
            {
                var anchor=TileStructure.Center(cell)+(Vector3)n*Half;
                sill=anchor+(Vector3)down*WalkwayDepth;
                if(curved!=null)sill=curved.Rigid(new Vector4(anchor.x,anchor.y,anchor.z,1),new Vector4(n.x,n.y,n.z,Vector3.Dot(anchor,(Vector3)n))).MultiplyPoint3x4(sill);
                into=-Vector3.ProjectOnPlane(FaceNormal(cell,n),up).normalized;
            }
            else return result;
            if(!WallHit(cell,down,sill,up,out float floorAbove,out _,out _,out _))return result;
            float rise=-floorAbove;if(rise<0.05f)return result;
            if(!WallHit(cell,n,sill+into*Mathf.Max(start,0.05f),into,out _,out var wallPoint,out var wallNormal,out _))return result;
            var right=Vector3.Cross(up,into).normalized;

            // how far it may reach into the room: short of the room's middle, or clear of a lift's opening in the floor
            var below=WayBetween(cell,cell+down);
            float margin=below==Way.Lift||below==Way.Hatch?(s.cells[cell+down].family==TileFamily.Corridor?TubeInner:HatchRadius)+Rim+0.05f:0.4f;
            float reach=Mathf.Max(LandingMinDepth,Vector3.Dot(Curved(TileStructure.Center(cell))-sill,into)-margin-start);
            int steps=Mathf.CeilToInt(rise/Riser-1e-3f);float tread=Tread,landing=LandingDepth;
            if(landing+(steps-1)*tread>reach)
            {
                landing=Mathf.Max(LandingMinDepth,reach-(steps-1)*tread);
                if(landing+(steps-1)*tread>reach)
                {
                    steps=Mathf.Max(1,Mathf.CeilToInt(rise/MaxRiser-1e-3f));
                    tread=steps>1?Mathf.Clamp((reach-landing)/(steps-1),MinTread,Tread):Tread;
                }
            }
            float h=rise/steps;bool wide=rise<=WideStepRise;

            // the side walls, as planes where the flight stands: neither bends much over its length
            var mid=sill+into*(start+landing*0.5f)-up*(rise*0.5f);
            var lattice=TileStructure.Cross(n,-down);if(Vector3.Dot(FaceNormal(cell,lattice),right)<0)lattice=-lattice;
            var sides=new (Vector3Int e,bool wall,Vector3 point,Vector3 normal)[2];
            for(int i=0;i<2;i++)
            {
                var e=i==0?lattice:-lattice;
                sides[i]=WallHit(cell,e,mid,i==0?right:-right,out _,out var point,out var normal,out _)?(e,true,point,normal):(e,false,Vector3.zero,Vector3.zero);
            }
            // what must stay clear beside the wall at a stretch along into: a door's block or a socket in it, or windows
            float Keep(int i,float z0,float z1)
            {
                var e=sides[i].e;
                if(!s.Solid(cell+e))return LandingWindowGap;
                var way2=WayBetween(cell,cell+e);float centre,half,proud;
                if(way2==Way.Door){var o=Portal(cell,e);centre=Vector3.Dot(o.centre-sill,into);half=o.block;proud=PortalDepth(cell,e);}
                else if(way2==Way.Tube){centre=Vector3.Dot(Curved(TileStructure.Center(cell)+(Vector3)e*Half)-sill,into);half=TubeInner+Rim;proud=0.1f;}
                else return 0;
                return z1>centre-half-0.1f&&z0<centre+half+0.1f?proud+0.7f:0;
            }
            // across the room from q to side i's wall, less its inset
            float To(Vector3 q,int i,float inset)
            {
                var dir=i==0?right:-right;var (_,wall,point,normal)=sides[i];
                if(!wall)return StairHalf;
                float k=Vector3.Dot(dir,normal);if(k<0.2f)return StairHalf;
                float d=Vector3.Dot(point-q,normal)/k;
                return Mathf.Max(StairHalf,inset>0?d-inset:d+0.02f);
            }
            Vector3[] Solid(float z0,float z1,float top,bool toWalls,bool backToWall)
            {
                var c=new Vector3[8];float i0=toWalls?Keep(0,z0,z1):0,i1=toWalls?Keep(1,z0,z1):0;
                for(int k=0;k<8;k++)
                {
                    bool tip=(k&2)!=0,far=(k&4)!=0,plus=(k&1)!=0;
                    float height=tip?top:-0.06f;var q=sill+up*(height-rise)+into*(far?z1:z0);
                    float d=toWalls?To(q,plus?0:1,plus?i0:i1):StairHalf;
                    q+=(plus?right:-right)*d;
                    if(backToWall&&!far)
                    {
                        float kn=Vector3.Dot(into,wallNormal);
                        if(Mathf.Abs(kn)>0.2f)q+=into*(Vector3.Dot(wallPoint-q,wallNormal)/kn-0.03f);
                    }
                    c[k]=q;
                }
                return c;
            }
            if(tube)
            {
                // on through the wall to the walkway, across its width
                var c=new Vector3[8];
                for(int k=0;k<8;k++)c[k]=sill+right*((k&1)!=0?WalkwayHalf:-WalkwayHalf)+up*((k&2)!=0?0:-0.06f)+into*((k&4)!=0?start+0.12f+Inset(cell,n):-0.02f);
                result.Add(new InteriorStep(c,into,up,false));
            }
            float z=start+landing;
            result.Add(new InteriorStep(Solid(start,z,rise,true,true),into,up,true));
            for(int i=1;i<steps;i++)
            {
                result.Add(new InteriorStep(Solid(z,z+tread,rise-i*h,wide,false),into,up,true));
                z+=tread;
            }
            return result;
        }
        // Level treads square to the floor, independent of the wall's lean.
        void DoorFlight(TileMesh m,Vector3Int cell,Vector3Int n)
        {
            var steps=DoorSteps(cell,n);if(steps.Count==0)return;
            using(m.BeginWorld("door flight"))
            {
                m.PushMatrix(Matrix4x4.Translate(-TileStructure.Center(cell)));
                foreach(var step in steps)
                {
                    m.Hexa(InnerFloor,step.corners);
                    if(!step.nosing)continue;
                    var c=step.corners;var back=(c[3]-c[7]).normalized*0.04f;var up=step.rotation*Vector3.up*0.003f;
                    m.Quad(FloorEdge,c[6]+up,c[7]+up,c[7]+back+up,c[6]+back+up,step.rotation*Vector3.up,step.rotation*Vector3.right);
                }
                m.Pop();
            }
        }
    }
}
