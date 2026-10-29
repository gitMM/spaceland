using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Greybox interiors. People wear magnetic boots, so there is no up inside either: a cube is one open room whose six
    // walls are all floors. Each inner wall stands Shell (0.4 m) in from an exposed face, the depth of the window rows, so
    // their glass sits in it, and Partition (0.1 m) in from a face shared with a solid neighbour, so the wall between two
    // rooms is 0.2 m thick and stays behind the mullion on every tile boundary. Window rows and corridor portholes are
    // real openings where they are outside. Homes open into each other through octagonal hatches; a corridor's tube
    // (1.2 m inner apothem) runs on through a home's socket into the room, and ends at a sealed bulkhead against any other
    // tile, in space, or at a room too small to take it. Light bars sit on the two faces across the tile's axis (the
    // room's nominal deck and overhead) and in the tube's corners. StationInteriors builds them near the viewer.
    // On the curved grid the inner walls keep their depth in metres from the curved faces (CurvedTileMesh), meet where
    // the sheared faces' inner surfaces cross, hatches and sockets are true size round their face's centre (fixed parts)
    // and tubes are bent like the corridor's own. Wedge and apex cells (the ridges and crowns) have no interiors yet:
    // they are too sheared for a true-size room, so tubes that reach one end at a bulkhead.
    public sealed partial class TileBuilder
    {
        public const float Shell=0.4f,Partition=0.1f,TubeInner=1.2f,HatchRadius=0.9f;
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
        static readonly Finish Lamp=new Finish(TileSlot.Light,TilePattern.Plain,Color.white,0f);
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

        // Whether people pass between neighbouring cells a and b: along a corridor's tube, through a home's socket, or
        // through a hatch between two homes. A home whose face is too small for the opening and its plate stays closed.
        public bool Opens(Vector3Int a,Vector3Int b)
        {
            if(!Interior(a)||!Interior(b))return false;
            var pa=s.cells[a];var pb=s.cells[b];var d=b-a;
            bool ca=pa.family==TileFamily.Corridor,cb=pb.family==TileFamily.Corridor;
            if(ca&&!s.CorridorLinks(a,pa).Contains(d)||cb&&!s.CorridorLinks(b,pb).Contains(-d))return false;
            if(ca&&cb)return true;
            float r=ca||cb?TubeInner:HatchRadius;
            return (ca||Fits(a,d,r))&&(cb||Fits(b,-d,r));
        }
        bool Fits(Vector3Int c,Vector3Int n,float r)=>FaceRoom(c,n)*0.5f-Shell>=r+Rim;

        float Inset(Vector3Int cell,Vector3Int n)=>s.Solid(cell+n)?Partition:Shell;
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
        // depth in metres below that face.
        void Room(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            var primary=s.Primary(cell,p);var c=TileStructure.Center(cell);
            foreach(var n in TileStructure.Directions)
            {
                bool axial=n==p.axis||n==-p.axis;
                var y=axial?p.side:p.axis;var x=TileStructure.Cross(y,-n);
                float t=Inset(cell,n);var k=FaceScale(cell,n,x,y);
                var room=Rect.MinMaxRect(-Reach(cell,n,-x),-Reach(cell,n,-y),Reach(cell,n,x),Reach(cell,n,y));
                var holes=new List<Rect>();float opening=0;var plate=Vector2.zero;
                var next=cell+n;
                if(Opens(cell,next))
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
                if(opening>0)Opening(m,c+(Vector3)n*Half,opening,plate,t,opening==HatchRadius?InnerTrim:InnerWall);
                if(axial)Lamps(m,Mirror(room),opening>0?Mathf.Max(plate.x/k.x,plate.y/k.y):0);
                m.Pop();
            }
            m.surfaceFrame=Vector4.zero;
        }

        // Two light bars across a wall's room, clear of a plate of half-size clear round its centre.
        static void Lamps(TileMesh m,Rect room,float clear)
        {
            foreach(float side in new[]{-1f,1f})
            {
                float u=room.center.x+side*room.width*0.36f;
                if(Mathf.Abs(u)-0.07f<clear+0.05f)continue;
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
        // corridor's tube or the neighbour's half of a hatch exactly.
        static void Opening(TileMesh m,Vector3 anchor,float r,Vector2 plate,float t,in Finish lining)
        {
            const float proud=0.03f;float X=plate.x,Y=plate.y,KX=X*0.4142f,KY=Y*0.4142f;
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

        // Lamp strips along a tube's four corner sides, from a0 to a1 along A.
        static void TubeLamps(TileMesh m,Vector2[] oct,Vector3 pu,Vector3 pv,Vector3 A,float a0,float a1)
        {
            float len=a1-a0-0.5f;if(len<0.3f)return;
            for(int i=1;i<oct.Length;i+=2)
            {
                Vector2 p=oct[i],q=oct[(i+1)%oct.Length];
                var M=pu*(p.x+q.x)*0.5f+pv*(p.y+q.y)*0.5f;var n=M.normalized;
                m.Push(M-n*0.015f+A*((a0+a1)*0.5f),Quaternion.LookRotation(A,n));m.Box(Lamp,Vector3.zero,new Vector3(0.1f,0.03f,len));m.Pop();
            }
        }

        // The inside of the corridor's tube, bent like the tube outside (TileMesh.BeginTube): the same portholes,
        // stretched along the run like its ribs.
        void CorridorInterior(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            var links=s.CorridorLinks(cell,p);
            if(links.Count!=2||links[0]!=-links[1]){JunctionInterior(m,cell,p,links);return;}
            var run=links[0];var side=Vector3.Dot(run,p.side)==0?p.side:p.Third;
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
            TubeLamps(m,oct,B,C,A,lo,hi);
        }

        // A junction: a 2.4 m box room, open where a branch leaves it, and a tube along each branch. The box is rigid all
        // through (TileMesh.BeginRigid), like the junction's box outside, so it keeps its true size round the cell's
        // centre. A sheared run tilts the branch's bent lining against it, so each opening has a short rigid throat out
        // to Throat metres from the centre and a trim ring there, seen from both sides, that covers the joint with the
        // lining, which starts just behind it; the branches outside start beyond it.
        void JunctionInterior(TileMesh m,Vector3Int cell,in TilePlacement p,List<Vector3Int> links)
        {
            const float box=JunctionBox,r=TubeInner;
            var center=TileStructure.Center(cell);Vector3 up=p.axis;
            var oct=Octagon(TubeInner);
            using(m.BeginRigid("junction interior",center,new Vector4(up.x,up.y,up.z,Vector3.Dot(center,up))))
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
            foreach(var d in links)
            {
                Vector3 a=d,b=Vector3.Dot(d,p.side)==0?p.side:p.Third,c=Vector3.Cross(b,a);
                float k=1/RunStretch(cell,a);
                using var branch=m.BeginTube("corridor interior branch",center,a);
                bool open=Opens(cell,cell+d);float start=Along(cell,a,Throat-0.05f);
                float end=open?Half:Mathf.Max(start,Half-(s.Solid(cell+d)?0.65f:0.5f)*k);
                for(int i=0;i<oct.Length;i++)TubeSide(m,InnerWall,oct,i,b,c,a,start,end,NoPorts,0,0,true);
                if(!open)Bulkhead(m,oct,b,c,a,end);
            }
        }
    }
}
