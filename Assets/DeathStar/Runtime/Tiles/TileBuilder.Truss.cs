using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Truss: an open cell, like Corridor and Conduit, holding a heavy space frame. Brutalist rather than aerospace:
    // 0.8 m box chords with raised splice bands, 1.3 m cast nodes, flat diagonal braces with gusset plates at their ends.
    // Trusses side by side make one frame. Each member is built once, by the first truss cell round it, and pushed in
    // from its lattice line until it is flush with the trusses' outer surface, so nothing reaches into a neighbour's
    // armour. Faces onto open space are braced, the diagonal alternating from cell to cell so runs read as a Warren
    // zigzag; faces between trusses stay open, so a run is a hollow girder; a solid face beside a truss becomes a
    // bearing plate. Members are built from the cells' real corners at true size (thinner in cells under 4.2 m), so the
    // frame follows wedge and apex cells too: a wedge becomes a triangular girder with a king post, an apex an
    // inverted pyramid. Their kits (TileBuilder.ShapeKit):
    //   crest  a triangular lattice girder along the ridge on saddles, with a walkway and running lights
    //   gable  a heavy end flange; a tapered jib out into space where the next two cells along the ridge are clear
    //   keel   a heavy keel chord with running lights
    //   crown  a lattice tower: three legs from the ridges' girders up to a 13 m platform, a mast and a beacon
    public sealed partial class TileBuilder
    {
        static readonly Finish Steel=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.86f,0.86f,0.86f),0f);
        static readonly Finish Cast=new Finish(TileSlot.Paint,TilePattern.Plain,Color.white,0f);
        static readonly Finish Web=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.68f,0.68f,0.68f),0f);
        static readonly Finish Band=new Finish(TileSlot.Trim);
        const float ChordWidth=0.8f,NodeSize=1.3f,BraceWidth=0.6f,BraceDepth=0.38f;
        // Members keep their size in cells this wide and thin in proportion below it.
        static float Thin(float span)=>Mathf.Min(1f,span/4.2f);

        bool IsTruss(Vector3Int c)=>s.TryGet(c,out var p)&&p.family==TileFamily.Truss;
        // A lattice corner (cell c has corners c + 0/1 on each axis), curved or flat.
        Vector3 CornerAt(Vector3Int k)=>curved!=null?curved.Corner(k):((Vector3)k-Vector3.one*0.5f)*Cell;
        readonly Dictionary<Vector3Int,Vector3> cellCentres=new Dictionary<Vector3Int,Vector3>();
        Vector3 CellCentre(Vector3Int c)
        {
            if(cellCentres.TryGetValue(c,out var v))return v;
            v=Vector3.zero;for(int i=0;i<8;i++)v+=CornerAt(c+BlockConstruction.CornerOffset(i));
            return cellCentres[c]=v/8;
        }
        static Vector3Int Unit(int axis){var v=Vector3Int.zero;v[axis]=1;return v;}
        // The first of the trusses round a corner or edge builds what they share.
        static Vector3Int Owner(List<Vector3Int> round)
        {
            var o=round[0];
            foreach(var q in round)if(q.x<o.x||q.x==o.x&&(q.y<o.y||q.y==o.y&&q.z<o.z))o=q;
            return o;
        }
        // Whether the trusses round lattice corner k all lie on one side of it along an axis.
        static bool OneSide(List<Vector3Int> round,Vector3Int k,int axis)
        {
            bool lo=false,hi=false;
            foreach(var q in round){if(q[axis]==k[axis])hi=true;else lo=true;}
            return lo!=hi;
        }
        Vector3 Middle(List<Vector3Int> round){var v=Vector3.zero;foreach(var q in round)v+=CellCentre(q);return v/round.Count;}

        void Truss(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            m.surfaceFrame=Vector4.zero;
            m.PushMatrix(Matrix4x4.Translate(-TileStructure.Center(cell)));
            ShapeInfo shape=default;bool special=curved!=null&&s.special.TryGetValue(cell,out shape);var place=p;
            m.Detail(t=>
            {
                using(t.BeginWorld("truss"))TrussFrame(t,cell);
                if(special)ShapeKit(t,cell,place,shape);
            });
            m.Pop();
        }

        void TrussFrame(TileMesh m,Vector3Int cell)
        {
            var round=new List<Vector3Int>(8);
            for(int i=0;i<8;i++)
            {
                var k=cell+BlockConstruction.CornerOffset(i);round.Clear();
                for(int j=0;j<8;j++){var q=k-BlockConstruction.CornerOffset(j);if(IsTruss(q))round.Add(q);}
                if(Owner(round)==cell)Node(m,k,round);
            }
            for(int a=0;a<3;a++)for(int i=0;i<4;i++)
            {
                int b=(a+1)%3,c=(a+2)%3;var k=cell;k[b]+=i&1;k[c]+=i>>1;round.Clear();
                for(int j=0;j<4;j++){var q=k;q[b]-=j&1;q[c]-=j>>1;if(IsTruss(q))round.Add(q);}
                if(Owner(round)==cell)Chord(m,k,a,round);
            }
            // Braces only onto open space: not between trusses, not against a bearing plate, clear of tubes.
            for(int f=0;f<6;f++)if(!s.cells.ContainsKey(cell+TileStructure.Directions[f]))Brace(m,cell,f);
        }

        // A cast node, pushed in from the corner by half its size along each axis the trusses round it lie to one side
        // of; a running light on the corner of some outer ones.
        void Node(TileMesh m,Vector3Int k,List<Vector3Int> round)
        {
            var at=CornerAt(k);int sides=0;
            for(int a=0;a<3;a++)if(OneSide(round,k,a))sides++;
            Vector3 D(int a)=>CornerAt(k+Unit(a))-CornerAt(k-Unit(a));
            float size=NodeSize*Thin(Mathf.Min(D(0).magnitude,Mathf.Min(D(1).magnitude,D(2).magnitude))*0.5f);
            var into=(Middle(round)-at).normalized;
            var o=sides==0?at:at+into*(size*0.5f*Mathf.Sqrt(sides));
            m.PushMatrix(Basis(o,D(0),D(1),D(2)));m.Box(Cast,Vector3.zero,Vector3.one*size,0.18f*size/NodeSize);m.Pop();
            if(sides==3&&Rand(Hash(k),5)<0.25f)
            {
                m.PushMatrix(Basis(at+into*0.12f,D(0),D(1),D(2)));m.Box(RunningLight,Vector3.zero,Vector3.one*0.2f,0.03f);m.Pop();
            }
        }
        // An orthonormal frame from three lattice directions, led by the pair nearest a right angle (a folded cell has
        // two nearly opposite).
        static Matrix4x4 Basis(Vector3 o,Vector3 d0,Vector3 d1,Vector3 d2)
        {
            var d=new[]{d0.normalized,d1.normalized,d2.normalized};
            int bi=0,bj=1;float best=2;
            for(int i=0;i<3;i++)for(int j=i+1;j<3;j++){float c=Mathf.Abs(Vector3.Dot(d[i],d[j]));if(c<best){best=c;bi=i;bj=j;}}
            var x=d[bi];var y=(d[bj]-x*Vector3.Dot(d[bj],x)).normalized;
            return Frame(o,x,y,Vector3.Cross(x,y));
        }

        // A chord along the lattice edge from corner k along axis a, pushed in like the nodes on the two axes across it,
        // with a raised splice band for every ~2.5 m between the nodes.
        void Chord(TileMesh m,Vector3Int k,int a,List<Vector3Int> round)
        {
            int b=(a+1)%3,c=(a+2)%3;var ea=Unit(a);
            Vector3 p0=CornerAt(k),p1=CornerAt(k+ea),t=p1-p0;float len=t.magnitude;if(len<0.1f)return;t/=len;
            float thin=Thin(len),w=ChordWidth*thin;
            int sides=(OneSide(round,k,b)?1:0)+(OneSide(round,k,c)?1:0);
            var middle=Middle(round);
            Vector3 In(Vector3 p)
            {
                if(sides==0)return p;
                var d=middle-p;d-=t*Vector3.Dot(d,t);
                return p+d.normalized*(w*0.5f*Mathf.Sqrt(sides));
            }
            Vector3 q0=In(p0),q1=In(p1);
            // Square to the faces: across follows whichever lattice direction is nearer a right angle to the chord.
            Vector3 db=CornerAt(k+Unit(b))-p0+CornerAt(k+ea+Unit(b))-p1,dc=CornerAt(k+Unit(c))-p0+CornerAt(k+ea+Unit(c))-p1;
            var across=Mathf.Abs(Vector3.Dot(db.normalized,t))<Mathf.Abs(Vector3.Dot(dc.normalized,t))?db:dc;
            Member(m,Steel,q0,q1,across,w,w,0.07f*thin);
            float clear=len-NodeSize*thin;int bands=Mathf.Max(0,Mathf.RoundToInt(clear/2.5f));
            for(int i=1;i<=bands;i++)
            {
                var at=Vector3.Lerp(q0,q1,0.5f+(i/(float)(bands+1)-0.5f)*clear/len);
                Member(m,Band,at-t*0.11f,at+t*0.11f,across,w+0.14f,w+0.14f,0.03f);
            }
        }

        // One diagonal across an open face, set just behind the face, with a gusset plate at each end. It alternates
        // with the cell's parity, so runs zigzag; on a folded face (a wedge's end, an apex's roof) the diagonal that
        // would run along the edges gives way to the other.
        void Brace(TileMesh m,Vector3Int cell,int f)
        {
            var p=new Vector3[4];
            for(int i=0;i<4;i++)p[i]=CornerAt(cell+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[f,i]));
            var n=Vector3.Cross(p[2]-p[0],p[3]-p[1]).normalized;
            if(Vector3.Dot(n,(p[0]+p[1]+p[2]+p[3])*0.25f-CellCentre(cell))<0)n=-n;
            float Spread(int i)
            {
                var d=(p[i+2]-p[i]).normalized;
                return Mathf.Min(Vector3.Cross(p[(i+1)%4]-p[i],d).magnitude,Vector3.Cross(p[(i+3)%4]-p[i],d).magnitude);
            }
            float s0=Spread(0),s1=Spread(1);
            int pick=(cell.x+cell.y+cell.z)&1;
            if(s0<0.6f*s1)pick=1;else if(s1<0.6f*s0)pick=0;
            float k=Thin(Mathf.Min((p[1]-p[0]).magnitude,(p[2]-p[1]).magnitude));
            float inset=BraceDepth*k*0.5f+0.08f*k;
            Vector3 a=p[pick],c=p[pick+2],dir=(c-a).normalized;
            Member(m,Web,a+dir*0.5f*k-n*inset,c-dir*0.5f*k-n*inset,Vector3.Cross(n,dir),BraceWidth*k,BraceDepth*k,0.05f*k);
            foreach(int e in new[]{pick,pick+2})
            {
                Vector3 o=p[e],e1=(p[(e+1)%4]-o).normalized,e2=(p[(e+3)%4]-o).normalized,g=(p[(e+2)%4]-o).normalized;
                var kite=new[]{o,o+e1*1.35f*k,o+g*1.75f*k,o+e2*1.35f*k};
                var plate=new Vector3[8];
                for(int i=0;i<8;i++)plate[i]=kite[(i&1)!=0?((i&2)!=0?2:1):((i&2)!=0?3:0)]-n*(inset+((i&4)!=0?0.07f:-0.07f)*k);
                m.Hexa(Web,plate);
            }
        }

        // A box beam from a to b: w wide along across, d deep at right angles to both.
        static void Member(TileMesh m,in Finish f,Vector3 a,Vector3 b,Vector3 across,float w,float d,float chamfer)
        {
            var z=b-a;float len=z.magnitude;if(len<1e-3f)return;z/=len;
            var x=across-z*Vector3.Dot(across,z);
            if(x.sqrMagnitude<1e-6f)x=Vector3.Cross(z,Mathf.Abs(z.y)<0.9f?Vector3.up:Vector3.right);
            x.Normalize();
            m.PushMatrix(Frame((a+b)*0.5f,x,Vector3.Cross(z,x),z));m.Box(f,Vector3.zero,new Vector3(w,d,len),chamfer);m.Pop();
        }

        // A solid face a truss stands on or leans against: a plate of carved armour, with no relief or family form.
        static void Bearing(Face f)
        {
            f.Box(Armor,-Half+0.1f,-Half+0.1f,Half-0.1f,Half-0.1f,0,0.12f,0.04f);
            f.bare.Add(Whole);
        }

        // ---------------------------------------------------------------- kits

        // Whether the next two cells along a wedge's spine past its end at e are empty: room for a jib.
        bool ClearBeyond(Vector3Int cell,int spine,int e)
        {
            var step=Vector3Int.zero;step[spine]=e;
            return !s.cells.ContainsKey(cell+step)&&!s.cells.ContainsKey(cell+step*2);
        }

        static readonly Vector2 GirderFootL=new Vector2(-1.45f,0.55f),GirderFootR=new Vector2(1.45f,0.55f),GirderApex=new Vector2(0,3.1f);
        static Vector2[] Square(Vector2 c,float w){float h=w*0.5f;return new[]{c+new Vector2(-h,-h),c+new Vector2(h,-h),c+new Vector2(h,h),c+new Vector2(-h,h)};}
        static readonly Vector2[] Walkway={new Vector2(-1.1f,0.45f),new Vector2(1.1f,0.45f),new Vector2(1.1f,0.6f),new Vector2(-1.1f,0.6f)};
        // A point on a run between two sections, in Loft's frames, so members meet its chords.
        static Vector3 OnRun(in Section s0,in Section s1,float t,Vector2 q)
        {
            var up=Vector3.Lerp(s0.up,s1.up,t).normalized;var ac=Vector3.Lerp(s0.across,s1.across,t);ac=(ac-up*Vector3.Dot(ac,up)).normalized;
            return Vector3.Lerp(s0.crest,s1.crest,t)+ac*q.x+up*q.y;
        }
        // Crest: a triangular lattice girder along the ridge in the frame's language: two foot chords and an apex chord,
        // laced in a Warren pattern, cast nodes at the panel points, a walkway and ties between the feet, a saddle down to
        // the roof under every bay and a running light on every apex node. Bay ends on a joined section are left to the
        // next cell.
        static void Girder(TileMesh m,in Section s0,in Section s1,bool join0,bool join1)
        {
            foreach(var chord in new[]{GirderFootL,GirderFootR})Loft(m,Steel,s0,s1,Square(chord,0.75f),0,1,!join0,!join1);
            Loft(m,Steel,s0,s1,Square(GirderApex,0.8f),0,1,!join0,!join1);
            Loft(m,Band,s0,s1,Walkway,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));
            int bays=Mathf.Max(1,Mathf.RoundToInt(length/2.8f));
            var along=s1.crest-s0.crest;var a0=s0;var a1=s1;
            void Node(float t,Vector2 q,float size){m.PushMatrix(FrameAt(a0,a1,t));m.Box(Cast,new Vector3(q.x,q.y,0),Vector3.one*size,0.12f);m.Pop();}
            for(int i=0;i<=bays;i++)
            {
                float t0=i/(float)bays;
                if(i==0&&join0||i==bays&&join1)continue;
                Member(m,Web,OnRun(s0,s1,t0,GirderFootL),OnRun(s0,s1,t0,GirderFootR),along,0.5f,0.45f,0.04f);
                Node(t0,GirderFootL,1.05f);Node(t0,GirderFootR,1.05f);
            }
            for(int i=0;i<bays;i++)
            {
                float t0=i/(float)bays,tm=(i+0.5f)/bays,t1=(i+1f)/bays;
                var top=OnRun(s0,s1,tm,GirderApex);
                foreach(var foot in new[]{GirderFootL,GirderFootR})
                {
                    Member(m,Web,OnRun(s0,s1,t0,foot),top,along,0.55f,0.42f,0.04f);
                    Member(m,Web,top,OnRun(s0,s1,t1,foot),along,0.55f,0.42f,0.04f);
                }
                Node(tm,GirderApex,1.1f);
                m.PushMatrix(FrameAt(s0,s1,tm));
                m.Box(Cast,new Vector3(0,0.02f,0),new Vector3(3.9f,0.55f,0.9f),0.08f);
                m.Box(RunningLight,new Vector3(0,3.7f,0),new Vector3(0.2f,0.16f,0.2f),0.02f);
                m.Pop();
            }
        }
        // Gable: a heavy flange round the triangular end, standing proud of it, with hazard blocks on its corners.
        static void EndPortal(TileMesh m,in Section s,Vector3 outward)
        {
            var g=(s.keel+s.eaveA+s.eaveB)/3;
            Vector3 In(Vector3 p)=>g+(p-g)*0.9f+outward*0.22f;
            var c=new[]{In(s.keel),In(s.eaveA),In(s.eaveB)};
            for(int i=0;i<3;i++)Member(m,Web,c[i],c[(i+1)%3],outward,0.42f,0.7f,0.05f);
            foreach(var p in c)
            {
                m.PushMatrix(Frame(p,s.across,s.up,Vector3.Cross(s.across,s.up)));m.Box(Hazard,Vector3.zero,Vector3.one*0.85f,0.08f);m.Pop();
            }
        }
        // A tapered jib out from the girder's end into open space: its chords converge over 7.5 m in four laced bays to
        // a hazard-painted tip block with a light.
        static void Jib(TileMesh m,in Section s,Vector3 outward)
        {
            var z=(outward-s.up*Vector3.Dot(outward,s.up)).normalized;var sec=s;
            Vector3 P(Vector2 q,float d)=>sec.crest+sec.across*q.x+sec.up*q.y+z*d;
            const float L=7.5f;const int bays=4;
            Vector2 Foot(float t,float side)=>new Vector2(side*Mathf.Lerp(1.45f,0.5f,t),Mathf.Lerp(0.55f,0.95f,t));
            Vector2 Apex(float t)=>new Vector2(0,Mathf.Lerp(3.1f,1.6f,t));
            foreach(float side in new[]{-1f,1f})Member(m,Steel,P(Foot(0,side),0),P(Foot(1,side),L),s.across,0.65f,0.65f,0.05f);
            Member(m,Steel,P(Apex(0),0),P(Apex(1),L),s.across,0.7f,0.7f,0.05f);
            for(int i=0;i<bays;i++)
            {
                float t0=i/(float)bays,tm=(i+0.5f)/bays,t1=(i+1f)/bays;
                foreach(float side in new[]{-1f,1f})
                {
                    Member(m,Web,P(Foot(t0,side),L*t0),P(Apex(tm),L*tm),z,0.42f,0.34f,0.03f);
                    Member(m,Web,P(Apex(tm),L*tm),P(Foot(t1,side),L*t1),z,0.42f,0.34f,0.03f);
                }
                Member(m,Web,P(Foot(t1,-1),L*t1),P(Foot(t1,1),L*t1),z,0.4f,0.4f,0.03f);
            }
            m.PushMatrix(Frame(P(new Vector2(0,1.15f),L+0.4f),s.across,s.up,z));
            m.Box(Hazard,Vector3.zero,new Vector3(1.7f,1.6f,1.0f),0.12f);
            m.Box(RunningLight,new Vector3(0,0.88f,0.2f),new Vector3(0.24f,0.16f,0.24f),0.02f);
            m.Pop();
        }
        static readonly Vector2[] KeelBeam={new Vector2(-0.5f,-0.15f),new Vector2(0.5f,-0.15f),new Vector2(0.32f,0.42f),new Vector2(-0.32f,0.42f)};
        static readonly Vector2[] KeelBeamLight={new Vector2(-0.07f,0.4f),new Vector2(0.07f,0.4f),new Vector2(0.07f,0.5f),new Vector2(-0.07f,0.5f)};
        // Keel: a heavy chord along the edge hanging into space, its section turned over, with running lights.
        static void KeelChord(TileMesh m,in Section s0,in Section s1)
        {
            Section Turn(in Section s)=>new Section{crest=s.keel,keel=s.crest,up=-s.up,across=s.across};
            var k0=Turn(s0);var k1=Turn(s1);
            Loft(m,Steel,k0,k1,KeelBeam,0,1,false,false);
            float length=Mathf.Max(0.5f,Vector3.Distance(k0.crest,k1.crest));
            int lights=Mathf.Max(1,Mathf.RoundToInt(length/1.25f));float lw=0.2f/length;
            for(int k=0;k<lights;k++){float t=(k+0.5f)/lights;Loft(m,RunningLight,k0,k1,KeelBeamLight,t-lw,t+lw,true,true);}
        }
        // Crown: a lattice tower where the three girders meet, in the frame's language. A cast plinth over the junction,
        // three legs from shoes on the girders up to a 13 m platform, a ring with cast nodes every ~3 m and X bracing in
        // every panel; on the platform a blockhouse with a lit slit, a mast with yards and a beacon.
        static void LatticeTower(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            var frame=HubFrame(o,up,arms[0]);var inv=frame.inverse;
            m.PushMatrix(frame);
            const float H=13f;
            var dirs=new Vector3[3];
            for(int i=0;i<3;i++){var d=inv.MultiplyVector(arms[i]);d.y=0;dirs[i]=d.normalized;}
            Vector3 Leg(int i,float h)=>dirs[i]*Mathf.Lerp(2.8f,1.35f,h/H)+Vector3.up*h;
            m.Box(Cast,new Vector3(0,0.8f,0),new Vector3(3.0f,1.6f,3.0f),0.3f);
            for(int i=0;i<3;i++)
            {
                m.Box(Cast,Leg(i,0.6f),new Vector3(1.6f,1.2f,1.6f),0.18f);
                Member(m,Steel,Leg(i,0.6f),Leg(i,H),Leg((i+1)%3,0)-Leg(i,0),0.95f,0.95f,0.08f);
            }
            float[] levels={1.2f,4.2f,7.2f,10.2f,H-0.5f};
            for(int l=0;l<levels.Length;l++)for(int i=0;i<3;i++)
            {
                int j=(i+1)%3;float h=levels[l];var side=Leg(j,h)-Leg(i,h);
                Member(m,Steel,Leg(i,h),Leg(j,h),Vector3.up,0.7f,0.6f,0.06f);
                m.Box(Cast,Leg(i,h),Vector3.one*1.25f,0.15f);
                if(l+1<levels.Length)
                {
                    float h1=levels[l+1];var across=Vector3.Cross(side,Vector3.up);
                    Member(m,Web,Leg(i,h),Leg(j,h1),across,0.5f,0.36f,0.04f);
                    Member(m,Web,Leg(j,h),Leg(i,h1),across,0.5f,0.36f,0.04f);
                }
            }
            var deck=new Vector2[6];for(int i=0;i<6;i++){float a=i*Mathf.PI/3;deck[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*3.1f;}
            m.Box(Cast,new Vector3(0,H-0.9f,0),new Vector3(2.8f,1.8f,2.8f),0.3f);
            m.Prism(Cast,deck,Vector3.zero,Vector3.right,Vector3.forward,Vector3.up,H,H+0.8f,true);
            m.Box(Hazard,new Vector3(0,H+0.86f,0),new Vector3(5.6f,0.12f,0.26f),0.02f);
            m.Box(Steel,new Vector3(0.4f,H+1.8f,0),new Vector3(2.4f,1.9f,2.0f),0.15f);
            m.Box(Slit,new Vector3(1.6f,H+2.1f,0),new Vector3(0.06f,0.32f,1.5f),0f);
            Member(m,Steel,new Vector3(-0.6f,H+0.8f,0),new Vector3(-0.6f,H+8.5f,0),Vector3.right,0.55f,0.55f,0.05f);
            foreach(float h in new[]{H+4.6f,H+6.8f})Member(m,Web,new Vector3(-2.2f,h,0),new Vector3(1.0f,h,0),Vector3.up,0.22f,0.22f,0.02f);
            m.Cylinder(RunningLight,new Vector3(-0.6f,H+8.5f,0),new Vector3(-0.6f,H+8.85f,0),0.22f,10,false,true);
            m.Pop();
        }
    }
}
