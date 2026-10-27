using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Wedge and apex cells (BlockConstruction.Shape, doc: Ridges, keels and crowns). Their open faces keep the skin and
    // the station's relief, but build no family forms: a box form squeezed onto half a roof is what looked wrong.
    // Instead the cell builds its shape's kit in curved space, from its real corners (TileMesh.BeginWorld). Phase 3
    // builds the Hull kit for every family:
    //   crest   an armoured spine along a ridge's fold line, ribs on a ~1.25 m pitch and running lights
    //   crown   a hub with a beacon where three ridges meet, and an arm along each seam into its ridge's crest
    //   gable   an armour plate and a round hatch on an open triangular end
    //   keel    a lit armoured strip along the edge of a wedge hanging into space, a hatch on every second cell
    //   slope   armour courses in chevrons and a maintenance hatch on an open slope (TileBuilder.KitsC.cs)
    // A part crossing into the next cell ends on a section taken from the shared face's four corners only, so both
    // cells build the same vertices there and runs join without a seam.
    public sealed partial class TileBuilder
    {
        static readonly Finish RunningLight=new Finish(TileSlot.Light,TilePattern.Plain,new Color(1f,0.32f,0.12f),0f);
        static readonly Vector2[] CrestBody={new Vector2(-0.65f,-0.35f),new Vector2(0.65f,-0.35f),new Vector2(0.42f,0.5f),new Vector2(-0.42f,0.5f)};
        static readonly Vector2[] CrestRib={new Vector2(-0.9f,-0.35f),new Vector2(0.9f,-0.35f),new Vector2(0.6f,0.75f),new Vector2(-0.6f,0.75f)};
        static readonly Vector2[] CrestLight={new Vector2(-0.08f,0.47f),new Vector2(0.08f,0.47f),new Vector2(0.08f,0.56f),new Vector2(-0.08f,0.56f)};
        static readonly Vector2[] KeelBody={new Vector2(-0.35f,-0.1f),new Vector2(0.35f,-0.1f),new Vector2(0.2f,0.28f),new Vector2(-0.2f,0.28f)};
        static readonly Vector2[] KeelLight={new Vector2(-0.06f,0.26f),new Vector2(0.06f,0.26f),new Vector2(0.06f,0.33f),new Vector2(-0.06f,0.33f)};

        // A ridge's cross-section at one lattice face: its crest (fold) corner, keel corner and the two eaves, with an
        // orthonormal up (keel to crest) and across (eave a to eave b). Lofts use crest as their origin.
        struct Section{public Vector3 crest,keel,eaveA,eaveB,up,across;}

        static (int axis,int sign) FaceAxis(int face)=>(face>>1,(face&1)==0?1:-1);
        static List<int> RoofFaces(in ShapeInfo sh){var list=new List<int>();for(int f=0;f<6;f++)if((sh.roof>>f&1)!=0)list.Add(f);return list;}

        // The face of `cell` across `s` at offset oS (0 or 1), for roof axes a and b with their outward signs.
        Section MakeSection(Vector3Int cell,int s,int oS,int a,int sa,int b,int sb)
        {
            Vector3 C(bool roofA,bool roofB)
            {
                var k=cell;k[s]+=oS;k[a]+=roofA==(sa>0)?1:0;k[b]+=roofB==(sb>0)?1:0;
                return curved.Corner(k);
            }
            var sec=new Section{crest=C(true,true),keel=C(false,false),eaveA=C(true,false),eaveB=C(false,true)};
            sec.up=(sec.crest-sec.keel).normalized;
            var ab=sec.eaveB-sec.eaveA;sec.across=(ab-sec.up*Vector3.Dot(ab,sec.up)).normalized;
            return sec;
        }

        // How a special cell meets open space (as BlockConstruction.Exposure, from this structure's solids), and which
        // ends along its spine are open.
        CellExposure Exposure(Vector3Int cell,in ShapeInfo sh,out bool open0,out bool open1)
        {
            open0=open1=false;int roofs=0,roofOpen=0,slopeOpen=0;
            for(int f=0;f<6;f++)
            {
                bool open=!s.Solid(cell+TileStructure.Directions[f]);
                if((sh.roof>>f&1)!=0){roofs++;if(open)roofOpen++;}
                else if(f>>1==sh.spine){if(open){if((f&1)==0)open1=true;else open0=true;}}
                else if(open)slopeOpen++;
            }
            bool wedge=sh.kind==CellShape.Wedge;
            if(roofOpen==roofs)return wedge?CellExposure.Ridge:CellExposure.Crown;
            if(roofOpen>0)return CellExposure.Half;
            if(slopeOpen>0)return wedge?CellExposure.Keel:CellExposure.Spike;
            return CellExposure.Buried;
        }
        // Whether a crest carries on into the cell across the spine face at e: a ridge wedge of the same family along the
        // same spine, or a crown of the same family whose arm arrives there.
        bool CrestJoins(Vector3Int cell,int spine,int e)
        {
            var step=Vector3Int.zero;step[spine]=e;var q=cell+step;
            if(!s.special.TryGetValue(q,out var o)||!s.TryGet(q,out var qp)||!s.TryGet(cell,out var own)||qp.family!=own.family)return false;
            var x=Exposure(q,o,out _,out _);
            return o.kind==CellShape.Wedge?o.spine==spine&&x==CellExposure.Ridge:x==CellExposure.Crown;
        }

        void Special(TileMesh m,Vector3Int cell,in TilePlacement p,in ShapeInfo sh)
        {
            var c=TileStructure.Center(cell);
            for(int i=0;i<6;i++)
            {
                var n=TileStructure.Directions[i];
                if(s.Solid(cell+n))continue;
                m.surfaceFrame=new Vector4(n.x,n.y,n.z,Vector3.Dot(n,c)+Half);
                var y=n==p.axis||n==-p.axis?p.side:p.axis;
                var f=new Face{m=m,s=s,cell=cell,p=p,n=n,y=y,curved=curved};f.x=TileStructure.Cross(f.y,-n);
                m.Push((Vector3)n*Half,Quaternion.LookRotation(-(Vector3)n,(Vector3)y));
                bool slope=(sh.roof>>i&1)==0&&(sh.kind==CellShape.Apex||i>>1!=sh.spine);
                if(slope)SlopeFor(f,p.family);
                m.Wall(Smooth,new Rect(-Half,-Half,Cell,Cell),f.recess);
                m.Pop();
                int a=AxisOf(n),ua=(a+1)%3,va=(a+2)%3;
                var holes=SpecialHoles(cell,n,sh);fieldHoles[(cell,n)]=holes;
                var nn=n;
                var gf=new GreebleField.Face{axis=a,sign=n[a],plane=c[a]+n[a]*Half,window=new Rect(c[ua]-Half,c[va]-Half,Cell,Cell),
                    holes=holes,origin=c,continues=(u,v)=>ReliefContinues(cell,nn,c,ua,va,u,v)};
                GreebleField.Build(m,gf,Relief);
            }
            m.surfaceFrame=Vector4.zero;
            var exposure=Exposure(cell,sh,out bool open0,out bool open1);
            var roofs=RoofFaces(sh);
            m.PushMatrix(Matrix4x4.Translate(-c));
            if(sh.kind==CellShape.Wedge)
            {
                var (a,sa)=FaceAxis(roofs[0]);var (b,sb)=FaceAxis(roofs[1]);
                var s0=MakeSection(cell,sh.spine,0,a,sa,b,sb);var s1=MakeSection(cell,sh.spine,1,a,sa,b,sb);
                if(exposure==CellExposure.Ridge)
                    using(m.BeginWorld("crest"))CrestFor(m,p.family,cell,sh.spine,s0,s1,CrestJoins(cell,sh.spine,-1),CrestJoins(cell,sh.spine,1));
                if(exposure==CellExposure.Keel)
                    using(m.BeginWorld("keel"))Keel(m,s0,s1,(cell[sh.spine]&1)==0);
                var along=(s1.crest-s0.crest).normalized;
                if(open0)using(m.BeginWorld("gable"))GableFor(m,p.family,s0,-along);
                if(open1)using(m.BeginWorld("gable"))GableFor(m,p.family,s1,along);
            }
            else if(exposure==CellExposure.Crown)using(m.BeginWorld("crown"))Crown(m,p.family,cell,roofs);
            m.Pop();
        }

        // Relief stays clear of the kit: strips along a roof face's fold edges (the crest) and the corner an apex's hub
        // stands on, the whole of an open gable (its plate), the middle of a slope (its hatch). Station rects in the
        // face's (u, v), as GreebleField.Face.holes.
        List<Rect> SpecialHoles(Vector3Int cell,Vector3Int n,in ShapeInfo sh)
        {
            var list=new List<Rect>();var c=TileStructure.Center(cell);
            int face=System.Array.IndexOf(TileStructure.Directions,n),a=AxisOf(n),ua=(a+1)%3,va=(a+2)%3;
            Rect Span(int axis,float lo,float hi,int other,float olo,float ohi)=>axis==ua?Rect.MinMaxRect(lo,olo,hi,ohi):Rect.MinMaxRect(olo,lo,ohi,hi);
            if((sh.roof>>face&1)!=0)
            {
                var folds=new List<(int axis,float edge,float width)>();
                for(int g=0;g<6;g++)
                {
                    if(g==face||(sh.roof>>g&1)==0||g>>1==a)continue;
                    var (b,sb)=FaceAxis(g);var dir=Vector3.zero;dir[b]=1;
                    float width=Mathf.Min(Half,1.05f/Mathf.Max(0.2f,curved.Scale(c+(Vector3)n*(Half-0.01f),dir)));
                    float edge=c[b]+sb*Half;folds.Add((b,edge,width*sb));
                }
                foreach(var (b,edge,w) in folds)
                {
                    int other=b==ua?va:ua;
                    list.Add(Span(b,Mathf.Min(edge,edge-w),Mathf.Max(edge,edge-w),other,c[other]-Half,c[other]+Half));
                }
                if(folds.Count==2)
                {
                    // the apex's corner, under the hub
                    var (b0,e0,w0)=folds[0];var (b1,e1,w1)=folds[1];
                    float lo0=Mathf.Min(e0,e0-1.6f*w0),hi0=Mathf.Max(e0,e0-1.6f*w0),lo1=Mathf.Min(e1,e1-1.6f*w1),hi1=Mathf.Max(e1,e1-1.6f*w1);
                    list.Add(Span(b0,lo0,hi0,b1,lo1,hi1));
                }
            }
            else list.Add(new Rect(c[ua]-Half,c[va]-Half,Cell,Cell));   // a gable's plate, or a slope's form
            return list;
        }

        // A prism along a ridge from section s0 (t = 0) to s1 (t = 1), between t0 and t1, its convex profile (across,
        // up) in metres round the crest line. Frames are interpolated, so prisms ending on a shared section meet exactly.
        static void Loft(TileMesh m,in Finish f,in Section s0,in Section s1,Vector2[] profile,float t0,float t1,bool cap0,bool cap1,bool sides=true)
        {
            var a0=s0;var a1=s1;
            Vector3 P(float t,Vector2 q)
            {
                var up=Vector3.Lerp(a0.up,a1.up,t).normalized;var ac=Vector3.Lerp(a0.across,a1.across,t);ac=(ac-up*Vector3.Dot(ac,up)).normalized;
                return Vector3.Lerp(a0.crest,a1.crest,t)+ac*q.x+up*q.y;
            }
            var centroid=Vector2.zero;foreach(var q in profile)centroid+=q;centroid/=profile.Length;
            var middle=P((t0+t1)*0.5f,centroid);int n=profile.Length;
            for(int i=0;i<n&&sides;i++)
            {
                Vector2 q0=profile[i],q1=profile[(i+1)%n];
                Vector3 a=P(t0,q0),b=P(t0,q1),c=P(t1,q1),d=P(t1,q0);
                var normal=Vector3.Cross(b-a,d-a).normalized;if(Vector3.Dot(normal,(a+b+c+d)*0.25f-middle)<0)normal=-normal;
                m.Quad(f,a,b,c,d,normal,(d-a).normalized);
            }
            var along=(P(t1,centroid)-P(t0,centroid)).normalized;
            foreach(var (t,cap,dir) in new[]{(t0,cap0,-along),(t1,cap1,along)})
            {
                if(!cap)continue;
                var p0=P(t,profile[0]);
                for(int i=1;i<n-1;i++)m.Triangle(f,p0,P(t,profile[i]),P(t,profile[i+1]),dir);
            }
        }

        // Spine, ribs and running lights along a ridge; caps only where the run stops.
        static void Crest(TileMesh m,in Section s0,in Section s1,bool join0,bool join1)
        {
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));
            Loft(m,Armor,s0,s1,CrestBody,0,1,!join0,!join1);
            Ribs(m,s0,s1,CrestRib,CrestLight,length);
        }
        static void Ribs(TileMesh m,in Section s0,in Section s1,Vector2[] rib,Vector2[] light,float length)
        {
            int ribs=Mathf.Max(1,Mathf.RoundToInt(length/1.25f));float w=0.15f/length,lw=0.22f/length;
            for(int k=0;k<ribs;k++){float t=(k+0.5f)/ribs;Loft(m,Armor,s0,s1,rib,t-w,t+w,true,true);}
            if(light==null)return;
            // lights between the ribs, none on the shared ends, so neighbours don't double them
            for(int k=1;k<ribs;k++){float t=k/(float)ribs;Loft(m,RunningLight,s0,s1,light,t-lw,t+lw,true,true);}
        }
        // Along the keel: the section turned over, its origin on the keel corner and up pointing into open space.
        static void Keel(TileMesh m,in Section s0,in Section s1,bool hatch)
        {
            Section Turn(in Section s)=>new Section{crest=s.keel,keel=s.crest,up=-s.up,across=s.across};
            var k0=Turn(s0);var k1=Turn(s1);
            Loft(m,Armor,k0,k1,KeelBody,0,1,false,false);
            float length=Mathf.Max(0.5f,Vector3.Distance(k0.crest,k1.crest));
            int lights=Mathf.Max(1,Mathf.RoundToInt(length/1.25f));float lw=0.2f/length;
            for(int k=0;k<lights;k++){float t=(k+0.5f)/lights;Loft(m,RunningLight,k0,k1,KeelLight,t-lw,t+lw,true,true);}
            if(!hatch)return;
            // a hatch on the strip of every second cell, between its lights
            m.PushMatrix(FrameAt(k0,k1,lights>1?1f/lights:0.5f));
            m.Lathe(Smooth,new Vector3(0,0.28f,0),Vector3.up,new[]{new Vector2(0.42f,0),new Vector2(0.42f,0.1f),new Vector2(0.3f,0.1f),new Vector2(0.3f,0)},16,false);
            m.Lathe(Smooth,new Vector3(0,0.28f,0),Vector3.up,new[]{new Vector2(0,0),new Vector2(0.3f,0),new Vector2(0.3f,0.05f),new Vector2(0,0.05f)},16,false);
            m.Pop();
        }
        // An armour plate inset on the triangular end and a round hatch in it.
        static void Gable(TileMesh m,in Section s,Vector3 outward)
        {
            var g=(s.keel+s.eaveA+s.eaveB)/3;var lift=outward*0.12f;
            Vector3 In(Vector3 p)=>g+(p-g)*0.8f;
            Vector3 k=In(s.keel),a=In(s.eaveA),b=In(s.eaveB);
            foreach(var (p,q) in new[]{(k,a),(a,b),(b,k)})
            {
                var normal=Vector3.Cross(q-p,outward).normalized;if(Vector3.Dot(normal,(p+q)*0.5f-g)<0)normal=-normal;
                m.Quad(Armor,p,q,q+lift,p+lift,normal,(q-p).normalized);
            }
            m.Triangle(Armor,k+lift,a+lift,b+lift,outward);
            var o=g+lift;
            m.Lathe(Smooth,o,outward,new[]{new Vector2(0.75f,0),new Vector2(0.75f,0.22f),new Vector2(0.58f,0.22f),new Vector2(0.58f,0)},24,false);
            m.Lathe(Smooth,o,outward,new[]{new Vector2(0,0),new Vector2(0.58f,0),new Vector2(0.58f,0.1f),new Vector2(0,0.1f)},24,false);
        }
        // Where three ridges meet: a drum hub and beacon on the outer corner, and an arm along each seam out to the
        // face it shares with the next wedge, ending on that face's section so it runs on into the ridge's crest.
        void Crown(TileMesh m,TileFamily family,Vector3Int cell,List<int> roofs)
        {
            var axes=new (int axis,int sign)[3];for(int i=0;i<3;i++)axes[i]=FaceAxis(roofs[i]);
            var outer=cell;var inner=cell;
            foreach(var (ax,sg) in axes){outer[ax]+=sg>0?1:0;inner[ax]+=sg>0?0:1;}
            Vector3 o=curved.Corner(outer),up=(o-curved.Corner(inner)).normalized;var arms=new Vector3[3];
            for(int i=0;i<3;i++)
            {
                var (ax,sg)=axes[i];var (a,sa)=axes[(i+1)%3];var (b,sb)=axes[(i+2)%3];
                if(a>b){var t=(a,sa);(a,sa)=(b,sb);(b,sb)=t;}
                var end=MakeSection(cell,ax,sg>0?0:1,a,sa,b,sb);
                var start=new Section{crest=o,up=up,across=(end.across-up*Vector3.Dot(end.across,up)).normalized};
                // the arm starts inside the hub, in the crown's family's crest
                var from=start;from.crest=Vector3.Lerp(o,end.crest,Mathf.Min(0.5f,1f/Mathf.Max(0.5f,Vector3.Distance(o,end.crest))));
                CrestFor(m,family,cell,ax,from,end,true,CrestJoins(cell,ax,-sg));
                arms[i]=end.crest-o;
            }
            HubFor(m,family,o,up,arms);
        }
        // The Hull crown's hub: a drum and a beacon.
        static void Hub(TileMesh m,Vector3 o,Vector3 up)
        {
            m.Lathe(Smooth,o-up*0.2f,up,new[]{new Vector2(1.3f,0),new Vector2(1.3f,0.7f),new Vector2(1.0f,0.95f),new Vector2(0,0.95f)},28,false);
            m.Cylinder(Smooth,o+up*0.75f,o+up*1.6f,0.22f,12,false,true,0.02f);
            m.Cylinder(RunningLight,o+up*1.6f,o+up*1.85f,0.16f,12,false,true);
        }
    }
}
