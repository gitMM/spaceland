using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Armour carving: a large flat face with the Armour pattern is cut into plates in courses with real seams between
    // them, like the towers of the Death Star trench models. Courses stack along the tile's axis (along its side on the
    // faces across the axis), so they wrap round a tower and every side matches. The layout lives in station space, so
    // it carries on across tiles and across primitives on the same plane.
    // Station space is split into 8 m zones, busy or calm. Busy zones have 0.62 m courses of 0.31-1.24 m plates, often
    // cut; calm zones 1.24 m courses of 0.62-2.48 m plates, mostly long and whole. Plates stand 4-10 cm proud with a bevelled
    // top. Some have a corner cut away at 45 degrees; some are split by a diagonal cut, others by a cut that jogs
    // diagonally between two levels, the two parts standing well apart; some are sunk panels in a raised frame, some
    // hatches. A plain 8 cm band runs along the face's edges (not along tile seams, where the face carries on), so
    // corners read solid; slivers the edges would leave are dropped.
    // Carving waits until the mesh is done: plates under other solids (boxes and hexahedra) are left out, and
    // ToMeshes also builds a far level of detail with the armour plain.
    public sealed partial class TileMesh
    {
        // Station position of the mesh origin, so carving lines up across tiles.
        public Vector3 stationOffset;
        // Station axes (0-2) the courses stack along: the tile's axis, and its side on faces across the axis.
        // -1: each face's own second axis.
        public int courseAxis=-1,courseSide=-1;
        // Half the size of the tile cell the mesh is centred on (0: not a tile). Faces lying on the cell's wall and
        // facing into it are never seen, so they are not built.
        public float cellHalf;

        const float Gap=0.014f,Cut=0.03f,Bevel=0.016f,Trim=0.08f,Zone=8f,CalmShare=0.4f;
        static readonly Vector3 ZoneOffset=new Vector3(3.1f,1.7f,5.9f);
        // Cumulative odds of plain, corner cut, sunk panel, hatch and diagonal split; the rest are jogged splits.
        static readonly float[] BusyOdds={0.30f,0.46f,0.56f,0.60f,0.80f},CalmOdds={0.58f,0.70f,0.82f,0.90f,0.95f};

        struct Job{public Finish f;public Vector3 A,B,C,D,N;public Vector4 frame,anchor;}
        readonly List<Job> jobs=new List<Job>();
        // Convex solids built so far, as planes (outward normal, offset) in mesh space.
        readonly List<Vector4[]> solids=new List<Vector4[]>();

        static float H01(int a,int b,int c,int k)
        {
            uint h=(uint)a*73856093u^(uint)b*19349663u^(uint)c*83492791u^(uint)k*2654435761u;
            h^=h>>16;h*=0x7feb352dU;h^=h>>15;h*=0x846ca68bU;h^=h>>16;
            return (h&0xFFFFFF)/16777216f;
        }
        static float Cross2(Vector2 a,Vector2 b)=>a.x*b.y-a.y*b.x;
        static float SignedArea(List<Vector2> p){float s=0;for(int i=0;i<p.Count;i++)s+=Cross2(p[i],p[(i+1)%p.Count]);return s*0.5f;}
        static float Perimeter(List<Vector2> p){float s=0;for(int i=0;i<p.Count;i++)s+=(p[(i+1)%p.Count]-p[i]).magnitude;return s;}
        // About the width of a polygon's narrowest part: 2 x area / perimeter.
        static float Thickness(List<Vector2> p)=>p.Count<3?0:2*Mathf.Abs(SignedArea(p))/Mathf.Max(Perimeter(p),1e-6f);
        static Vector2 Centroid(List<Vector2> p){var c=Vector2.zero;foreach(var q in p)c+=q;return c/p.Count;}
        static List<Vector2> Corners(Rect q)=>new List<Vector2>{new Vector2(q.xMin,q.yMin),new Vector2(q.xMax,q.yMin),new Vector2(q.xMax,q.yMax),new Vector2(q.xMin,q.yMax)};

        // A face lying on the wall of the tile's cell and facing into it.
        bool Hidden(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n)
        {
            if(cellHalf<=0)return false;
            Vector3 A=matrix.MultiplyPoint3x4(a),B=matrix.MultiplyPoint3x4(b),C=matrix.MultiplyPoint3x4(c),D=matrix.MultiplyPoint3x4(d),N=matrix.MultiplyVector(n);
            for(int k=0;k<3;k++)
            {
                if(Mathf.Abs(Mathf.Abs(A[k])-cellHalf)>1e-3f)continue;
                if(Mathf.Abs(B[k]-A[k])>1e-3f||Mathf.Abs(C[k]-A[k])>1e-3f||Mathf.Abs(D[k]-A[k])>1e-3f)continue;
                if(N[k]*Mathf.Sign(A[k])<-0.5f*N.magnitude)return true;
            }
            return false;
        }

        void SolidBox(Vector3 o,Vector3 h)
        {
            var planes=new Vector4[6];int i=0;
            for(int a=0;a<3;a++)for(int s=-1;s<=1;s+=2)
            {
                Vector3 n=matrix.MultiplyVector(Axis(a)*s).normalized,p=matrix.MultiplyPoint3x4(o+Axis(a)*(s*h[a]));
                planes[i++]=new Vector4(n.x,n.y,n.z,Vector3.Dot(n,p));
            }
            solids.Add(planes);
        }
        void SolidPlanes(List<(Vector3 n,Vector3 p)> faces)
        {
            var planes=new Vector4[faces.Count];
            for(int i=0;i<faces.Count;i++)
            {
                Vector3 n=matrix.MultiplyVector(faces[i].n).normalized,p=matrix.MultiplyPoint3x4(faces[i].p);
                planes[i]=new Vector4(n.x,n.y,n.z,Vector3.Dot(n,p));
            }
            if(planes.Length>=4)solids.Add(planes);
        }
        bool InSolid(Vector3 p)
        {
            foreach(var s in solids)
            {
                bool inside=true;
                foreach(var q in s)if(q.x*p.x+q.y*p.y+q.z*p.z-q.w>-0.004f){inside=false;break;}
                if(inside)return true;
            }
            return false;
        }

        // Queues a face for carving; false (and nothing queued) for faces too small or thin to carve.
        bool Carve(in Finish f,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n)
        {
            Vector3 A=matrix.MultiplyPoint3x4(a),B=matrix.MultiplyPoint3x4(b),C=matrix.MultiplyPoint3x4(c),D=matrix.MultiplyPoint3x4(d);
            float w=Mathf.Max((B-A).magnitude,(C-D).magnitude),h=Mathf.Max((C-B).magnitude,(D-A).magnitude);
            if(Mathf.Min(w,h)<0.55f||Vector3.Cross(C-A,D-B).magnitude*0.5f<0.35f)return false;
            jobs.Add(new Job{f=f,A=A,B=B,C=C,D=D,N=matrix.MultiplyVector(n).normalized,frame=surfaceFrame,anchor=metricAnchor});
            return true;
        }
        // Most triangles the near mesh may have (0: no limit). Over it, faces switch to calm plates (fewer, longer), those
        // that save the most first, until the tile fits; if even all calm is too much, faces are carved largest first and
        // the rest left plain.
        public int nearBudget;
        // What the last Flush did to stay within it: 0 nothing, 1 some faces calm, 2 some faces also left plain.
        public int NearCap {get;private set;}
        // Faces of the last Flush switched to calm plates, of all it carved.
        public (int calm,int faces) CalmFaces {get;private set;}
        bool allCalm;
        void Flush()
        {
            if(jobs.Count==0)return;
            var saved=matrix;matrix=Matrix4x4.identity;
            var list=jobs.ToArray();jobs.Clear();
            int v0=positions.Count;var i0=IndexCounts();int plain=TriangleCount;
            void CarveJob(in Job j,bool calm){allCalm=calm;surfaceFrame=j.frame;metricAnchor=j.anchor;CarveFace(j);allCalm=false;}
            foreach(var j in list)CarveJob(j,false);
            NearCap=0;CalmFaces=(0,list.Length);
            if(nearBudget>0&&TriangleCount>nearBudget)
            {
                // Each face's carving stands alone, so its cost both ways adds up.
                var busy=new int[list.Length];var calmCost=new int[list.Length];
                Truncate(v0,i0);
                for(int k=0;k<list.Length;k++)
                    foreach(bool calm in new[]{false,true})
                    {
                        CarveJob(list[k],calm);int n=TriangleCount-plain;Truncate(v0,i0);
                        if(calm)calmCost[k]=n;else busy[k]=n;
                    }
                var order=new List<int>();for(int k=0;k<list.Length;k++)order.Add(k);
                order.Sort((a,b)=>(busy[b]-calmCost[b]).CompareTo(busy[a]-calmCost[a]));
                long total=plain;foreach(int n in busy)total+=n;
                var calmed=new bool[list.Length];int count=0;
                foreach(int k in order){if(total<=nearBudget)break;total-=busy[k]-calmCost[k];calmed[k]=true;count++;}
                NearCap=1;CalmFaces=(count,list.Length);
                if(total<=nearBudget)for(int k=0;k<list.Length;k++)CarveJob(list[k],calmed[k]);
                else
                {
                    NearCap=2;
                    var largest=new List<Job>(list);largest.Sort((a,b)=>Area(b).CompareTo(Area(a)));
                    // each face still to come takes at least its plain quad
                    int left=largest.Count;
                    foreach(var j in largest)
                    {
                        left--;int v=positions.Count;var marks=IndexCounts();
                        CarveJob(j,true);
                        if(TriangleCount+left*2<=nearBudget)continue;
                        Truncate(v,marks);surfaceFrame=j.frame;metricAnchor=j.anchor;
                        Quad(j.f.With(TilePattern.Plain),j.A,j.B,j.C,j.D,j.N,(j.B-j.A).normalized);
                    }
                }
            }
            matrix=saved;
        }
        static float Area(in Job j)=>Vector3.Cross(j.C-j.A,j.D-j.B).magnitude*0.5f;

        // A face looking at another solid across a gap under a metre over most of its area (a radiator fin's side, the
        // inside of a narrow slot) is only ever seen edge-on, so it stays plain.
        bool Facing(Vector3 A,Vector3 B,Vector3 C,Vector3 D,Vector3 N)
        {
            int blocked=0;
            for(int i=0;i<5;i++)
            {
                float s=i==0?0.5f:(i&1)!=0?0.25f:0.75f,t=i==0?0.5f:i<3?0.25f:0.75f;
                if(Blocked(Vector3.Lerp(Vector3.Lerp(A,B,s),Vector3.Lerp(D,C,s),t),N,1f))blocked++;
                if(blocked+4-i<4)return false;
            }
            return blocked>=4;
        }
        // Whether the ray from p along dir meets a solid within reach (clipped against each solid's planes).
        bool Blocked(Vector3 p,Vector3 dir,float reach)
        {
            foreach(var s in solids)
            {
                float t0=0.02f,t1=reach;
                foreach(var q in s)
                {
                    float d=q.x*dir.x+q.y*dir.y+q.z*dir.z,h=q.x*p.x+q.y*p.y+q.z*p.z-q.w;
                    if(Mathf.Abs(d)<1e-6f){if(h>0){t0=t1+1;break;}continue;}
                    float t=-h/d;if(d<0)t0=Mathf.Max(t0,t);else t1=Mathf.Min(t1,t);
                    if(t0>t1)break;
                }
                if(t0<=t1)return true;
            }
            return false;
        }

        // A half-plane: the side to the left of e through o.
        struct Half{public Vector2 o,e;public Half(Vector2 o,Vector2 e){this.o=o;this.e=e;}}

        void CarveFace(in Job j)
        {
            Vector3 A=j.A,B=j.B,C=j.C,D=j.D,N=j.N;
            var plain=j.f.With(TilePattern.Plain);
            // A face under another solid (a box's foot, a plinth's top under a tower) is left out.
            var mid=(A+B+C+D)*0.25f;
            bool Under(Vector3 p)=>InSolid(Vector3.MoveTowards(p,mid,0.01f)+N*0.01f);
            if(Under(mid)&&Under(A)&&Under(B)&&Under(C)&&Under(D))return;
            Quad(plain,A,B,C,D,N,(B-A).normalized);   // the floor the seams show
            if(Facing(A,B,C,D,N))return;
            int ax=0;for(int i=1;i<3;i++)if(Mathf.Abs(N[i])>Mathf.Abs(N[ax]))ax=i;
            int va=courseAxis<0?(ax+2)%3:courseAxis!=ax?courseAxis:courseSide,ua=3-ax-va;
            Vector3 so=stationOffset;
            Vector2 To2(Vector3 p)=>new Vector2(p[ua]+so[ua],p[va]+so[va]);
            float planeC=Vector3.Dot(N,A);
            Vector3 Lift(Vector2 q,float height)
            {
                var p=Vector3.zero;p[ua]=q.x-so[ua];p[va]=q.y-so[va];
                p[ax]=(planeC-N[ua]*p[ua]-N[va]*p[va])/N[ax];
                return p+N*height;
            }
            var quad=new List<Vector2>{To2(A),To2(B),To2(C),To2(D)};
            if(SignedArea(quad)<0)quad.Reverse();
            // Edges: trimmed by the plain band, except tile seams; wide leaves seams open to tell slivers from plates
            // that carry on into the next tile.
            var clip=new List<Half>();var wide=new List<Half>();
            float cell=cellHalf*2;
            bool Lattice(float x){if(cell<=0)return false;float m=Mathf.Repeat(x-cellHalf,cell);return Mathf.Min(m,cell-m)<2e-3f;}
            for(int i=0;i<4;i++)
            {
                Vector2 p=quad[i],q=quad[(i+1)%4],e=q-p;
                if(e.sqrMagnitude<1e-8f)continue;
                bool seam=(Mathf.Abs(e.x)<1e-3f&&Lattice(p.x))||(Mathf.Abs(e.y)<1e-3f&&Lattice(p.y));
                if(seam){clip.Add(new Half(p,e));continue;}
                var hp=new Half(p+new Vector2(-e.y,e.x).normalized*Trim,e);
                clip.Add(hp);wide.Add(hp);
            }
            int key=ax*2+(N[ax]>0?1:0)+31*Mathf.RoundToInt(Vector3.Dot(N,A+so)*8);
            float uMin=float.MaxValue,uMax=float.MinValue,vMin=float.MaxValue,vMax=float.MinValue;
            foreach(var q in quad){uMin=Mathf.Min(uMin,q.x);uMax=Mathf.Max(uMax,q.x);vMin=Mathf.Min(vMin,q.y);vMax=Mathf.Max(vMax,q.y);}
            int iw=Mathf.FloorToInt((mid[ax]+so[ax]+ZoneOffset[ax])/Zone);
            var zone=new List<Half>(4);
            for(int iu=Mathf.FloorToInt((uMin+ZoneOffset[ua])/Zone);iu<=Mathf.FloorToInt((uMax+ZoneOffset[ua])/Zone);iu++)
            for(int iv=Mathf.FloorToInt((vMin+ZoneOffset[va])/Zone);iv<=Mathf.FloorToInt((vMax+ZoneOffset[va])/Zone);iv++)
            {
                var idx=Vector3Int.zero;idx[ax]=iw;idx[ua]=iu;idx[va]=iv;
                bool calm=allCalm||H01(idx.x,idx.y,idx.z,77)<CalmShare;
                float s=calm?2:1,rowH=0.62f*s,cellW=0.31f*s;
                float x0=iu*Zone-ZoneOffset[ua],x1=x0+Zone,y0=iv*Zone-ZoneOffset[va],y1=y0+Zone;
                zone.Clear();
                zone.Add(new Half(new Vector2(x0,y0+Gap),Vector2.right));zone.Add(new Half(new Vector2(x1-Gap,y0),Vector2.up));
                zone.Add(new Half(new Vector2(x1,y1-Gap),Vector2.left));zone.Add(new Half(new Vector2(x0+Gap,y1),Vector2.down));
                float zu0=Mathf.Max(uMin,x0),zu1=Mathf.Min(uMax,x1),zv0=Mathf.Max(vMin,y0),zv1=Mathf.Min(vMax,y1);
                if(zu1<=zu0||zv1<=zv0)continue;
                int zkey=key*2+(calm?1:0);
                for(int row=Mathf.FloorToInt(zv0/rowH);row<=Mathf.FloorToInt(zv1/rowH);row++)
                {
                    float r0=row*rowH,r1=r0+rowH,stag=row*0.47f*s;
                    int k=Mathf.FloorToInt((zu0+stag)/cellW);
                    while(!Edge(k,row,zkey,calm))k--;
                    while(true)
                    {
                        int k2=k+1;while(!Edge(k2,row,zkey,calm))k2++;
                        float a=k*cellW-stag,b=k2*cellW-stag;
                        if(a>zu1)break;
                        if(b>zu0)Plate(plain,zkey,row,k,Rect.MinMaxRect(a+Gap,r0+Gap,b-Gap,r1-Gap),s,clip,wide,zone,Lift,N);
                        k=k2;
                    }
                }
            }
        }
        // Plate edges fall on cell boundaries by hash, and at least every fourth boundary, so every face agrees. Calm
        // plates are mostly long.
        static bool Edge(int k,int row,int key,bool calm)=>((k%4)+4)%4==0||H01(k,row,key,0)<(calm?0.2f:0.45f);

        void Plate(in Finish f,int key,int row,int k,Rect r,float s,List<Half> clip,List<Half> wide,List<Half> zone,Func<Vector2,float,Vector3> lift,Vector3 N)
        {
            Func<int,float> rnd=i=>H01(k,row,key,i);
            float t=rnd(1),h=0.04f+0.03f*Mathf.Floor(rnd(2)*3);
            var fin=f.Shade(0.95f+rnd(11)*0.1f);
            var odds=s>1.5f?CalmOdds:BusyOdds;
            var parts=new List<(List<Vector2> poly,float h0,float h1)>();
            var cut=new List<(List<Vector2> poly,float h)>();
            if(t<odds[0]){}
            else if(t<odds[1])CutPlate(PlateCut.Corner,r,rnd,s,Cut,h,h,cut);
            else if(t<odds[3])
            {
                // A hatch only where the whole plate fits (one cut short by an edge reads as a mistake); a sunk panel
                // fits what the edges leave of the plate.
                bool whole=true;
                foreach(var p in Corners(r))if(!Inside(p,clip)||!Inside(p,zone))whole=false;
                var outer=whole?Corners(r):Clip(Corners(r),clip,zone);
                if(outer.Count>=3&&Thickness(outer)>0.27f)Panel(outer,r,s,h,whole&&t>=odds[2],parts);
            }
            else if(t<odds[4])CutPlate(PlateCut.Diagonal,r,rnd,s,Cut,h+0.05f,h+0.12f,cut);
            else CutPlate(PlateCut.Jog,r,rnd,s,Cut,h+0.05f,h+0.12f,cut);
            foreach(var (poly,ph) in cut)parts.Add((poly,0,ph));
            if(parts.Count==0)parts.Add((Corners(r),0,h));
            foreach(var (poly,h0,h1) in parts)
            {
                var open=Clip(poly,wide,zone);
                if(open.Count<3)continue;
                float thick=Thickness(open);
                if(thick<0.06f&&thick<Thickness(poly)*0.8f)continue;   // a sliver left by an edge
                var piece=Clip(poly,clip,zone);
                if(piece.Count<3||Mathf.Abs(SignedArea(piece))<0.004f)continue;
                if(InSolid(lift(Centroid(piece),(h0+h1)*0.5f)))continue;
                Extrude(fin,piece,h0,h1,Bevel,lift,N);
            }
        }

        // A sunk panel: a raised frame round a plate set low, on outer (a CCW polygon). A hatch, on the whole plate r: a
        // door in the frame with hinges and a handle, or, where the plate is about square, a round door with a hub and
        // a bar.
        static void Panel(List<Vector2> outer,Rect r,float s,float h,bool hatch,List<(List<Vector2>,float,float)> o)
        {
            float small=hatch?Mathf.Min(r.width,r.height):Thickness(outer)*1.4f;
            if(hatch&&(small<0.5f||Mathf.Max(r.width,r.height)<0.8f))hatch=false;
            float fw=Mathf.Clamp(small*0.12f,0.05f,0.12f),top=h+0.03f;
            bool round=hatch&&Mathf.Abs(r.width-r.height)<0.3f*s;
            if(round)
            {
                float c=small*0.29f;
                outer=new List<Vector2>{new Vector2(r.xMin+c,r.yMin),new Vector2(r.xMax-c,r.yMin),new Vector2(r.xMax,r.yMin+c),new Vector2(r.xMax,r.yMax-c),
                    new Vector2(r.xMax-c,r.yMax),new Vector2(r.xMin+c,r.yMax),new Vector2(r.xMin,r.yMax-c),new Vector2(r.xMin,r.yMin+c)};
            }
            var inner=Inset(outer,fw);
            var door=inner!=null?Inset(inner,Gap):null;
            if(door==null)return;
            for(int i=0;i<outer.Count;i++)
            {
                int n=(i+1)%outer.Count;
                o.Add((new List<Vector2>{outer[i],outer[n],inner[n],inner[i]},0,top));
            }
            if(!hatch){o.Add((door,0,0.012f));return;}
            o.Add((door,0,0.03f));
            var d=Rect.MinMaxRect(r.xMin+fw+Gap,r.yMin+fw+Gap,r.xMax-fw-Gap,r.yMax-fw-Gap);
            if(round)
            {
                var c=d.center;float hub=0.07f*s,bar=0.0175f*s;
                o.Add((Corners(Rect.MinMaxRect(c.x-d.width*0.36f,c.y-bar,c.x+d.width*0.36f,c.y+bar)),0.03f,0.055f));
                o.Add((Corners(Rect.MinMaxRect(c.x-hub,c.y-hub,c.x+hub,c.y+hub)),0.03f,0.075f));
                return;
            }
            foreach(float at in new[]{0.22f,0.78f})
            {
                float y=Mathf.Lerp(d.yMin,d.yMax,at);
                o.Add((Corners(Rect.MinMaxRect(d.xMin-fw*0.6f,y-0.05f*s,d.xMin+0.06f*s,y+0.05f*s)),0,top+0.015f));
            }
            float hx=d.xMax-0.1f*s;
            o.Add((Corners(Rect.MinMaxRect(hx-0.035f*s,d.center.y-0.08f*s,hx,d.center.y+0.08f*s)),0.03f,0.065f));
        }

        // The plate shapes shared by the armour carving and the station relief.
        public enum PlateCut{Corner,Diagonal,Jog}
        // Cuts plate r: a corner cut away at 45 degrees (one part at lo), a split along a diagonal seam, or a split
        // whose groove jogs diagonally between two levels (two parts, one at lo and one at hi, random which). Splits
        // run along the plate's long side. s scales the cut lengths (1 for 0.6 m courses), cut is half the groove's
        // width, rnd gives random numbers 3-10. Adds nothing if the plate is too small for the shape.
        public static void CutPlate(PlateCut kind,Rect r,Func<int,float> rnd,float s,float cut,float lo,float hi,List<(List<Vector2> poly,float h)> o)
        {
            if(kind==PlateCut.Corner)
            {
                float c=Mathf.Min(Mathf.Lerp(0.12f,0.26f,rnd(3))*s,Mathf.Min(r.width,r.height)*0.45f);
                int corner=(int)(rnd(4)*4)&3;
                var p=Corners(r);var at=p[corner];var prev=p[(corner+3)%4];var next=p[(corner+1)%4];
                p.RemoveAt(corner);
                p.Insert(corner,at+(next-at).normalized*c);
                p.Insert(corner,at+(prev-at).normalized*c);
                o.Add((p,lo));
                return;
            }
            // work with the long side along x, and turn back
            bool turn=r.height>r.width;
            if(turn)r=new Rect(r.y,r.x,r.height,r.width);
            List<Vector2> Back(List<Vector2> p){if(turn)for(int i=0;i<p.Count;i++)p[i]=new Vector2(p[i].y,p[i].x);return p;}
            bool first=(kind==PlateCut.Diagonal?rnd(7):rnd(10))<0.5f;
            float ha=first?lo:hi,hb=first?hi:lo;
            if(kind==PlateCut.Diagonal)
            {
                if(r.width<=r.height+0.22f*s)return;
                float a=Mathf.Lerp(r.xMin+0.1f*s,r.xMax-r.height-0.1f*s,rnd(5));
                bool rising=rnd(6)<0.5f;
                Func<Vector2,float> d=rising?(p=>((p.x-a)-(p.y-r.yMin))*0.7071f):(p=>((p.x-a)-(r.yMax-p.y))*0.7071f);
                o.Add((Back(ClipHalf(Corners(r),d,-cut,true)),ha));
                o.Add((Back(ClipHalf(Corners(r),d,cut,false)),hb));
                return;
            }
            if(r.width<=0.6f*s||r.height<0.26f*s+2*cut)return;
            float ya=Mathf.Lerp(r.yMin+0.13f*s,r.yMax-0.13f*s,rnd(8));
            float yb=ya<(r.yMin+r.yMax)*0.5f?Mathf.Min(ya+0.24f*s,r.yMax-0.1f*s):Mathf.Max(ya-0.24f*s,r.yMin+0.1f*s);
            float run=Mathf.Abs(yb-ya),xa=Mathf.Lerp(r.xMin+0.1f*s,r.xMax-0.1f*s-run,rnd(9)),xb=xa+run;
            o.Add((Back(new List<Vector2>{new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,yb-cut),new Vector2(xb,yb-cut),new Vector2(xa,ya-cut),new Vector2(r.xMin,ya-cut)}),ha));
            o.Add((Back(new List<Vector2>{new Vector2(r.xMin,ya+cut),new Vector2(xa,ya+cut),new Vector2(xb,yb+cut),new Vector2(r.xMax,yb+cut),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax)}),hb));
        }

        // Raise a polygon off a face from h0 to h1: a top (bevelled in by bevel) and sloped sides down to h0. lift
        // maps a point of the polygon and a height above the face to mesh space; N is the face normal. side says
        // which edges get a side (all if null).
        public void Slab(in Finish f,List<Vector2> p,float h0,float h1,Func<Vector2,float,Vector3> lift,Vector3 N,float bevel=0,Func<Vector2,Vector2,bool> side=null)
        {
            var saved=matrix;matrix=Matrix4x4.identity;
            Extrude(f,new List<Vector2>(p),h0,h1,bevel,lift,N,side);
            matrix=saved;
        }
        public static float Area(List<Vector2> p)=>SignedArea(p);
        // The part of polygon p left of the line through o along e.
        public static List<Vector2> ClipLeft(List<Vector2> p,Vector2 o,Vector2 e)=>p.Count<3?p:Clip(p,new List<Half>{new Half(o,e)},NoHalves);
        static readonly List<Half> NoHalves=new List<Half>();
        void Extrude(in Finish f,List<Vector2> p,float h0,float h1,float bevel,Func<Vector2,float,Vector3> lift,Vector3 N,Func<Vector2,Vector2,bool> side=null)
        {
            if(SignedArea(p)<0)p.Reverse();
            var top=bevel>0?Inset(p,bevel)??p:p;
            var tris=Triangulate(top);
            for(int i=0;i+2<tris.Count;i+=3)Triangle(f,lift(top[tris[i]],h1),lift(top[tris[i+1]],h1),lift(top[tris[i+2]],h1),N);
            for(int i=0;i<p.Count;i++)
            {
                int j=(i+1)%p.Count;
                Vector2 e=p[j]-p[i];if(e.sqrMagnitude<1e-8f)continue;
                if(side!=null&&!side(p[i],p[j]))continue;
                var mid=(p[i]+p[j])*0.5f;
                Vector3 outward=lift(mid+new Vector2(e.y,-e.x).normalized*0.01f,0)-lift(mid,0);
                Vector3 b0=lift(p[i],h0),b1=lift(p[j],h0),t1=lift(top[j],h1),t0=lift(top[i],h1);
                var nn=Vector3.Cross(b1-b0,t0-b0).normalized;
                if(Vector3.Dot(nn,outward)<0)nn=-nn;
                Quad(f,b0,b1,t1,t0,nn,(b1-b0).normalized);
            }
        }

        // Mitred inward offset of a CCW polygon; null if it folds.
        static List<Vector2> Inset(List<Vector2> p,float d)
        {
            var o=new List<Vector2>(p.Count);
            for(int i=0;i<p.Count;i++)
            {
                Vector2 a=p[(i+p.Count-1)%p.Count],b=p[i],c=p[(i+1)%p.Count];
                Vector2 e1=(b-a).normalized,e2=(c-b).normalized;
                Vector2 n1=new Vector2(-e1.y,e1.x),n2=new Vector2(-e2.y,e2.x);
                Vector2 m=(n1+n2);if(m.sqrMagnitude<1e-6f)m=n1;m.Normalize();
                o.Add(b+m*d/Mathf.Max(Vector2.Dot(m,n1),0.35f));
            }
            // folded: an edge turned round, or a short edge's ends crossed
            for(int i=0;i<p.Count;i++)
            {
                int j=(i+1)%p.Count;
                if(Vector2.Dot(o[j]-o[i],p[j]-p[i])<=0)return null;
            }
            return SignedArea(o)>0.0005f?o:null;
        }

        // Keep the part of p where s is below (or above) limit.
        static List<Vector2> ClipHalf(List<Vector2> p,Func<Vector2,float> s,float limit,bool below)
        {
            var o=new List<Vector2>();
            for(int i=0;i<p.Count;i++)
            {
                Vector2 a=p[i],b=p[(i+1)%p.Count];
                float sa=(s(a)-limit)*(below?1:-1),sb=(s(b)-limit)*(below?1:-1);
                if(sa<=0)o.Add(a);
                if((sa<0)!=(sb<0)&&Mathf.Abs(sa-sb)>1e-9f)o.Add(Vector2.Lerp(a,b,sa/(sa-sb)));
            }
            return o;
        }
        static bool Inside(Vector2 p,List<Half> planes){foreach(var h in planes)if(Cross2(h.e,p-h.o)<0)return false;return true;}
        // Sutherland-Hodgman: subject against two lists of half-planes.
        static List<Vector2> Clip(List<Vector2> subject,List<Half> a,List<Half> b)
        {
            var o=subject;
            for(int l=0;l<2;l++)foreach(var h in l==0?a:b)
            {
                if(o.Count==0)return o;
                var input=o;o=new List<Vector2>(input.Count+2);
                for(int j=0;j<input.Count;j++)
                {
                    Vector2 p=input[j],q=input[(j+1)%input.Count];
                    float sp=-Cross2(h.e,p-h.o),sq=-Cross2(h.e,q-h.o);   // <=0 inside (left of the edge)
                    if(sp<=0)o.Add(p);
                    if((sp<0)!=(sq<0)&&Mathf.Abs(sp-sq)>1e-9f)o.Add(Vector2.Lerp(p,q,sp/(sp-sq)));
                }
            }
            for(int i=o.Count-1;i>0&&o.Count>2;i--)if((o[i]-o[i-1]).sqrMagnitude<1e-8f)o.RemoveAt(i);
            return o;
        }
        // Ear clipping for a small CCW polygon; returns index triples.
        static List<int> Triangulate(List<Vector2> p)
        {
            var idx=new List<int>();for(int i=0;i<p.Count;i++)idx.Add(i);
            var tris=new List<int>();
            for(int guard=0;idx.Count>3&&guard<64;guard++)
            {
                bool cut=false;
                for(int i=0;i<idx.Count;i++)
                {
                    int i0=idx[(i+idx.Count-1)%idx.Count],i1=idx[i],i2=idx[(i+1)%idx.Count];
                    Vector2 a=p[i0],b=p[i1],c=p[i2];
                    if(Cross2(b-a,c-b)<=1e-7f)continue;
                    bool inside=false;
                    foreach(int j in idx)
                    {
                        if(j==i0||j==i1||j==i2)continue;
                        var q=p[j];
                        if(Cross2(b-a,q-a)>0&&Cross2(c-b,q-b)>0&&Cross2(a-c,q-c)>0){inside=true;break;}
                    }
                    if(inside)continue;
                    tris.Add(i0);tris.Add(i1);tris.Add(i2);idx.RemoveAt(i);cut=true;break;
                }
                if(!cut)break;
            }
            if(idx.Count==3){tris.Add(idx[0]);tris.Add(idx[1]);tris.Add(idx[2]);}
            return tris;
        }
    }
}
