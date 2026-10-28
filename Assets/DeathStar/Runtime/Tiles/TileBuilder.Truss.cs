using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Truss: an open cell, like Corridor and Conduit, holding a space frame of slender members in a clear hierarchy:
    // box chords (0.46 m on the trusses' outline, 0.22 m on seams across a face, 0.2 m inside), cast nodes half as big
    // again as the chords they join, and flat X braces with gusset plates on every face onto open space.
    // Trusses side by side make one frame. Each member is built once, by the first truss cell round it, and pushed in
    // from its lattice line until it is flush with the trusses' outer surface, so nothing reaches into a neighbour's
    // armour. Faces between trusses stay open, so a run is a hollow girder; a solid face beside a truss becomes a
    // bearing plate. Braced faces that fill an aligned 2 x 2 block (on even station cells, like the 2 x 2 landmarks)
    // carry one big X across the 10 m panel; an aligned 4 x 4 block (multiples of four) a big X across 20 m with a
    // diamond inscribed in it, each cell carrying one diagonal of the pattern. Members are built from the cells' real
    // corners at true size (thinner in cells under 4.2 m), so the frame follows wedge and apex cells too: a wedge
    // becomes a triangular girder with a king post, an apex an inverted pyramid. Their kits (TileBuilder.ShapeKit):
    //   crest  a triangular lattice girder along the ridge, X-laced, on saddles, with a walkway and running lights
    //   gable  an end flange; a tapered jib out into space where the next two cells along the ridge are clear
    //   keel   a keel chord with running lights
    //   crown  a lattice tower: three legs from the ridges' girders up to a 13 m platform, a mast and a beacon
    // Gussets, splice bands and cross plates are left out of the coarse far level.
    public sealed partial class TileBuilder
    {
        static readonly Finish Steel=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.86f,0.86f,0.86f),0f);
        static readonly Finish Cast=new Finish(TileSlot.Paint,TilePattern.Plain,Color.white,0f);
        static readonly Finish Web=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.68f,0.68f,0.68f),0f);
        static readonly Finish Band=new Finish(TileSlot.Trim);
        // Member widths in metres: chords on the trusses' outline, on a seam across a face, inside; nodes against the
        // widest chord they join; braces in a lone cell's X, a 2 x 2 block's X, a 4 x 4 block's X and its diamond.
        const float OutlineChord=0.46f,SeamChord=0.22f,InnerChord=0.2f,NodeScale=1.5f;
        const float SingleBrace=0.2f,PairBrace=0.42f,QuadBrace=0.55f,DiamondBrace=0.3f;
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
        // A chord's width from the trusses round its edge (axis a, through corner k): inside four, on a seam between two
        // side by side, otherwise on the outline (a convex or concave edge of the trusses' mass).
        static float ChordWidth(List<Vector3Int> round,Vector3Int k,int a)
        {
            if(round.Count==4)return InnerChord;
            int b=(a+1)%3,c=(a+2)%3;
            return round.Count==2&&OneSide(round,k,b)!=OneSide(round,k,c)?SeamChord:OutlineChord;
        }

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
            float widest=0;var edge=new List<Vector3Int>(4);
            for(int a=0;a<3;a++)for(int e=-1;e<=0;e++)
            {
                edge.Clear();foreach(var q in round)if(q[a]==k[a]+e)edge.Add(q);
                if(edge.Count>0)widest=Mathf.Max(widest,ChordWidth(edge,k,a));
            }
            float size=widest*NodeScale*Thin(Mathf.Min(D(0).magnitude,Mathf.Min(D(1).magnitude,D(2).magnitude))*0.5f);
            var into=(Middle(round)-at).normalized;
            var o=sides==0?at:at+into*(size*0.5f*Mathf.Sqrt(sides));
            m.PushMatrix(Basis(o,D(0),D(1),D(2)));m.Box(Cast,Vector3.zero,Vector3.one*size,0.1f*size);m.Pop();
            if(sides==3&&Rand(Hash(k),5)<0.25f)
            {
                m.PushMatrix(Basis(at+into*0.08f,D(0),D(1),D(2)));m.Box(RunningLight,Vector3.zero,Vector3.one*0.16f,0.02f);m.Pop();
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

        // A chord along the lattice edge from corner k along axis a, pushed in like the nodes on the two axes across it;
        // outline chords carry a splice band for every ~2.5 m between the nodes.
        void Chord(TileMesh m,Vector3Int k,int a,List<Vector3Int> round)
        {
            int b=(a+1)%3,c=(a+2)%3;var ea=Unit(a);
            Vector3 p0=CornerAt(k),p1=CornerAt(k+ea),t=p1-p0;float len=t.magnitude;if(len<0.1f)return;t/=len;
            float thin=Thin(len),width=ChordWidth(round,k,a),w=width*thin;
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
            Member(m,Steel,q0,q1,across,w,w,0.04f*thin);
            if(width<OutlineChord||m.coarse>0)return;
            float clear=len-width*NodeScale*thin;int bands=Mathf.Max(0,Mathf.RoundToInt(clear/2.5f));
            for(int i=1;i<=bands;i++)
            {
                var at=Vector3.Lerp(q0,q1,0.5f+(i/(float)(bands+1)-0.5f)*clear/len);
                Member(m,Band,at-t*0.08f,at+t*0.08f,across,w+0.08f,w+0.08f,0.02f);
            }
        }

        // Whether a truss cell shows a braced box face on side n: open space beyond it, and not a wedge or apex.
        bool BracedFace(Vector3Int q,Vector3Int n)=>IsTruss(q)&&!s.cells.ContainsKey(q+n)&&!s.special.ContainsKey(q);
        // The aligned block of braced faces on side n that a cell's face belongs to: 4 (station coordinates in the face's
        // plane multiples of four), 2 (even), or 1.
        int FaceBlock(Vector3Int cell,Vector3Int n)
        {
            int a=AxisOf(n),u=(a+1)%3,v=(a+2)%3;
            foreach(int size in new[]{4,2})
            {
                var o=cell;o[u]=o[u]&~(size-1);o[v]=o[v]&~(size-1);bool all=true;
                for(int i=0;i<size&&all;i++)for(int j=0;j<size&&all;j++){var q=o;q[u]+=i;q[v]+=j;all=BracedFace(q,n);}
                if(all)return size;
            }
            return 1;
        }
        // The bracing on an open face, just behind it. A lone face carries an X with a cross plate where its diagonals
        // meet (on a folded face, a wedge's end or an apex's roof, only the diagonal that doesn't run along its edges).
        // A face in a 2 x 2 or 4 x 4 block carries one diagonal of the block's pattern: rising where the cell's place in
        // the block is even, so the four of a 2 x 2 make one big X and the sixteen of a 4 x 4 a big X round a diamond.
        void Brace(TileMesh m,Vector3Int cell,int f)
        {
            var n=TileStructure.Directions[f];int a=AxisOf(n),u=(a+1)%3,v=(a+2)%3;
            var o=cell;if(n[a]>0)o[a]+=1;
            Vector3 P(int i,int j){var k=o;k[u]+=i;k[v]+=j;return CornerAt(k);}
            Vector3 p00=P(0,0),p10=P(1,0),p01=P(0,1),p11=P(1,1);
            var normal=Vector3.Cross(p11-p00,p01-p10).normalized;
            if(Vector3.Dot(normal,(p00+p10+p01+p11)*0.25f-CellCentre(cell))<0)normal=-normal;
            float k=Thin(Mathf.Min((p10-p00).magnitude,(p01-p00).magnitude));
            int block=FaceBlock(cell,n);
            if(block>1)
            {
                int i=cell[u]&(block-1),j=cell[v]&(block-1);
                float w=(block==2?PairBrace:i==j||i+j==3?QuadBrace:DiamondBrace)*k;
                if(((i+j)&1)==0)Diagonal(m,p00,p11,p10,p01,normal,w);else Diagonal(m,p10,p01,p00,p11,normal,w);
                return;
            }
            // Spread: how far a diagonal's other two corners stand off it; a folded face has one near zero.
            static float Spread(Vector3 a,Vector3 c,Vector3 b,Vector3 d)
            {var t=(c-a).normalized;return Mathf.Min(Vector3.Cross(b-a,t).magnitude,Vector3.Cross(d-a,t).magnitude);}
            float rise=Spread(p00,p11,p10,p01),fall=Spread(p10,p01,p00,p11),ws=SingleBrace*k;
            bool r=rise>=0.6f*fall,fl=fall>=0.6f*rise;
            if(r)Diagonal(m,p00,p11,p10,p01,normal,ws);
            if(fl)Diagonal(m,p10,p01,p00,p11,normal,ws);
            if(r&&fl&&m.coarse==0)
            {
                Vector3 d1=(p11-p00).normalized,d2=(p01-p10).normalized,mid=(p00+p10+p01+p11)*0.25f-normal*(ws*0.4f+0.04f);
                float L=ws*2.2f;
                Plate(m,mid+d1*L,mid+d2*L,mid-d1*L,mid-d2*L,normal,ws*0.4f);
            }
        }
        // One diagonal from corner a to corner c of a face, w wide, set just behind it, with a gusset plate at each end
        // spreading onto the face's edges towards its other corners b and d.
        static void Diagonal(TileMesh m,Vector3 a,Vector3 c,Vector3 b,Vector3 d,Vector3 normal,float w)
        {
            float depth=w*0.8f,inset=depth*0.5f+0.04f;var dir=(c-a).normalized;
            Member(m,Web,a+dir*w-normal*inset,c-dir*w-normal*inset,Vector3.Cross(normal,dir),w,depth,0.03f*w/SingleBrace);
            if(m.coarse>0)return;
            foreach(var (o,g) in new[]{(a,dir),(c,-dir)})
            {
                Vector3 e1=(b-o).normalized,e2=(d-o).normalized,lift=-normal*inset;
                Plate(m,o+lift,o+e1*Mathf.Min(w*3f,1.1f)+lift,o+g*Mathf.Min(w*4f,1.5f)+lift,o+e2*Mathf.Min(w*3f,1.1f)+lift,normal,Mathf.Min(w*0.35f,0.12f));
            }
        }
        // A flat plate on the quadrilateral a b c d (in order round it), thick across normal.
        static void Plate(TileMesh m,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 normal,float thick)
        {
            var quad=new[]{a,b,c,d};var g=new Vector3[8];
            for(int i=0;i<8;i++)g[i]=quad[(i&1)!=0?((i&2)!=0?2:1):((i&2)!=0?3:0)]+normal*((i&4)!=0?0.5f:-0.5f)*thick;
            m.Hexa(Web,g);
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

        static readonly Vector2 GirderFootL=new Vector2(-1.3f,0.4f),GirderFootR=new Vector2(1.3f,0.4f),GirderApex=new Vector2(0,2.8f);
        static Vector2[] Square(Vector2 c,float w){float h=w*0.5f;return new[]{c+new Vector2(-h,-h),c+new Vector2(h,-h),c+new Vector2(h,h),c+new Vector2(-h,h)};}
        static readonly Vector2[] Walkway={new Vector2(-1.05f,0.34f),new Vector2(1.05f,0.34f),new Vector2(1.05f,0.44f),new Vector2(-1.05f,0.44f)};
        // A point on a run between two sections, in Loft's frames, so members meet its chords.
        static Vector3 OnRun(in Section s0,in Section s1,float t,Vector2 q)
        {
            var up=Vector3.Lerp(s0.up,s1.up,t).normalized;var ac=Vector3.Lerp(s0.across,s1.across,t);ac=(ac-up*Vector3.Dot(ac,up)).normalized;
            return Vector3.Lerp(s0.crest,s1.crest,t)+ac*q.x+up*q.y;
        }
        // Crest: a triangular lattice girder along the ridge in the frame's language: two foot chords and an apex chord,
        // X-laced bay by bay on both sloping sides, cast nodes at the bay ends, ties and a walkway between the feet, a
        // saddle down to the roof under every bay and a running light over it. Bay ends on a joined section are left to
        // the next cell.
        static void Girder(TileMesh m,in Section s0,in Section s1,bool join0,bool join1)
        {
            foreach(var chord in new[]{GirderFootL,GirderFootR})Loft(m,Steel,s0,s1,Square(chord,0.4f),0,1,!join0,!join1);
            Loft(m,Steel,s0,s1,Square(GirderApex,0.44f),0,1,!join0,!join1);
            Loft(m,Band,s0,s1,Walkway,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));
            int bays=Mathf.Max(1,Mathf.RoundToInt(length/2.8f));
            var along=s1.crest-s0.crest;var a0=s0;var a1=s1;
            void Node(float t,Vector2 q,float size){m.PushMatrix(FrameAt(a0,a1,t));m.Box(Cast,new Vector3(q.x,q.y,0),Vector3.one*size,0.06f);m.Pop();}
            for(int i=0;i<=bays;i++)
            {
                float t0=i/(float)bays;
                if(i==0&&join0||i==bays&&join1)continue;
                Member(m,Web,OnRun(s0,s1,t0,GirderFootL),OnRun(s0,s1,t0,GirderFootR),along,0.26f,0.22f,0.02f);
                Node(t0,GirderFootL,0.6f);Node(t0,GirderFootR,0.6f);Node(t0,GirderApex,0.66f);
            }
            for(int i=0;i<bays;i++)
            {
                float t0=i/(float)bays,tm=(i+0.5f)/bays,t1=(i+1f)/bays;
                foreach(var foot in new[]{GirderFootL,GirderFootR})
                {
                    Member(m,Web,OnRun(s0,s1,t0,foot),OnRun(s0,s1,t1,GirderApex),along,0.22f,0.18f,0.02f);
                    Member(m,Web,OnRun(s0,s1,t0,GirderApex),OnRun(s0,s1,t1,foot),along,0.22f,0.18f,0.02f);
                }
                m.PushMatrix(FrameAt(s0,s1,tm));
                m.Box(Cast,new Vector3(0,0.02f,0),new Vector3(3.3f,0.36f,0.5f),0.04f);
                m.Box(RunningLight,new Vector3(0,3.1f,0),new Vector3(0.16f,0.12f,0.16f),0.02f);
                m.Pop();
            }
        }
        // Gable: a flange round the triangular end, standing proud of it, with hazard blocks on its corners.
        static void EndPortal(TileMesh m,in Section s,Vector3 outward)
        {
            var g=(s.keel+s.eaveA+s.eaveB)/3;
            Vector3 In(Vector3 p)=>g+(p-g)*0.9f+outward*0.16f;
            var c=new[]{In(s.keel),In(s.eaveA),In(s.eaveB)};
            for(int i=0;i<3;i++)Member(m,Web,c[i],c[(i+1)%3],outward,0.28f,0.36f,0.03f);
            foreach(var p in c)
            {
                m.PushMatrix(Frame(p,s.across,s.up,Vector3.Cross(s.across,s.up)));m.Box(Hazard,Vector3.zero,Vector3.one*0.5f,0.05f);m.Pop();
            }
        }
        // A tapered jib out from the girder's end into open space: its chords converge over 7.5 m in four X-laced bays
        // to a hazard-painted tip block with a light.
        static void Jib(TileMesh m,in Section s,Vector3 outward)
        {
            var z=(outward-s.up*Vector3.Dot(outward,s.up)).normalized;var sec=s;
            Vector3 P(Vector2 q,float d)=>sec.crest+sec.across*q.x+sec.up*q.y+z*d;
            const float L=7.5f;const int bays=4;
            Vector2 Foot(float t,float side)=>new Vector2(side*Mathf.Lerp(1.3f,0.45f,t),Mathf.Lerp(0.4f,0.85f,t));
            Vector2 Apex(float t)=>new Vector2(0,Mathf.Lerp(2.8f,1.45f,t));
            foreach(float side in new[]{-1f,1f})Member(m,Steel,P(Foot(0,side),0),P(Foot(1,side),L),s.across,0.36f,0.36f,0.03f);
            Member(m,Steel,P(Apex(0),0),P(Apex(1),L),s.across,0.4f,0.4f,0.03f);
            for(int i=0;i<bays;i++)
            {
                float t0=i/(float)bays,t1=(i+1f)/bays;
                foreach(float side in new[]{-1f,1f})
                {
                    Member(m,Web,P(Foot(t0,side),L*t0),P(Apex(t1),L*t1),z,0.2f,0.16f,0.02f);
                    Member(m,Web,P(Apex(t0),L*t0),P(Foot(t1,side),L*t1),z,0.2f,0.16f,0.02f);
                }
                Member(m,Web,P(Foot(t1,-1),L*t1),P(Foot(t1,1),L*t1),z,0.22f,0.22f,0.02f);
            }
            m.PushMatrix(Frame(P(new Vector2(0,1.05f),L+0.3f),s.across,s.up,z));
            m.Box(Hazard,Vector3.zero,new Vector3(1.3f,1.2f,0.7f),0.08f);
            m.Box(RunningLight,new Vector3(0,0.66f,0.15f),new Vector3(0.2f,0.12f,0.2f),0.02f);
            m.Pop();
        }
        static readonly Vector2[] KeelBeam={new Vector2(-0.32f,-0.1f),new Vector2(0.32f,-0.1f),new Vector2(0.2f,0.3f),new Vector2(-0.2f,0.3f)};
        static readonly Vector2[] KeelBeamLight={new Vector2(-0.06f,0.28f),new Vector2(0.06f,0.28f),new Vector2(0.06f,0.37f),new Vector2(-0.06f,0.37f)};
        // Keel: a chord along the edge hanging into space, its section turned over, with running lights.
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
            m.Box(Cast,new Vector3(0,0.6f,0),new Vector3(2.2f,1.2f,2.2f),0.15f);
            for(int i=0;i<3;i++)
            {
                m.Box(Cast,Leg(i,0.5f),new Vector3(0.95f,0.8f,0.95f),0.08f);
                Member(m,Steel,Leg(i,0.5f),Leg(i,H),Leg((i+1)%3,0)-Leg(i,0),0.5f,0.5f,0.04f);
            }
            float[] levels={1.2f,4.2f,7.2f,10.2f,H-0.4f};
            for(int l=0;l<levels.Length;l++)for(int i=0;i<3;i++)
            {
                int j=(i+1)%3;float h=levels[l];var side=Leg(j,h)-Leg(i,h);
                Member(m,Steel,Leg(i,h),Leg(j,h),Vector3.up,0.34f,0.3f,0.03f);
                m.Box(Cast,Leg(i,h),Vector3.one*0.72f,0.06f);
                if(l+1<levels.Length)
                {
                    float h1=levels[l+1];var across=Vector3.Cross(side,Vector3.up);
                    Member(m,Web,Leg(i,h),Leg(j,h1),across,0.24f,0.18f,0.02f);
                    Member(m,Web,Leg(j,h),Leg(i,h1),across,0.24f,0.18f,0.02f);
                }
            }
            var deck=new Vector2[6];for(int i=0;i<6;i++){float a=i*Mathf.PI/3;deck[i]=new Vector2(Mathf.Cos(a),Mathf.Sin(a))*3.0f;}
            m.Box(Cast,new Vector3(0,H-0.7f,0),new Vector3(2.0f,1.2f,2.0f),0.15f);
            m.Prism(Cast,deck,Vector3.zero,Vector3.right,Vector3.forward,Vector3.up,H,H+0.5f,true);
            m.Box(Hazard,new Vector3(0,H+0.56f,0),new Vector3(5.4f,0.1f,0.2f),0.02f);
            m.Box(Steel,new Vector3(0.4f,H+1.45f,0),new Vector3(2.2f,1.8f,1.8f),0.1f);
            m.Box(Slit,new Vector3(1.51f,H+1.75f,0),new Vector3(0.06f,0.3f,1.3f),0f);
            Member(m,Steel,new Vector3(-0.6f,H+0.5f,0),new Vector3(-0.6f,H+8.2f,0),Vector3.right,0.36f,0.36f,0.03f);
            foreach(float h in new[]{H+4.3f,H+6.5f})Member(m,Web,new Vector3(-2.2f,h,0),new Vector3(1.0f,h,0),Vector3.up,0.16f,0.16f,0.01f);
            m.Cylinder(RunningLight,new Vector3(-0.6f,H+8.2f,0),new Vector3(-0.6f,H+8.5f,0),0.18f,10,false,true);
            m.Pop();
        }
    }
}
