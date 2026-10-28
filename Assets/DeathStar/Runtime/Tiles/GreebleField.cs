using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // One relief pattern over the whole station, built from real geometry. It is defined in station space on every
    // axis-aligned plane; a tile cuts out the window of each exposed face, so coplanar neighbours continue the same
    // plates with no seam and nothing in the pattern knows where tiles begin. Raised 0.1-0.8 m off the face, with
    // channels down to the face between plates. Hierarchy: 32 m districts set density and height; they tile the plane
    // with 12 m squares between them, every row stepping against the next (offset per plane so nothing lines up with
    // the 5 m lattice), and are cut unevenly into two to four top-level plates, so no large-scale line runs on for
    // more than 44 m and nothing forms a grid. Plates split recursively into
    // plates, steps, sunk frames, ribs, block grids, stepped pyramids, ramps, drums, domes, rings and glyph bars,
    // and into the carved armour's shapes at relief scale: plates with a corner cut away, split diagonally or by a
    // jogging groove into two parts a level apart (TileMesh.CutPlate).
    // Curved pieces break the lines further: wide arcs several tiles long with ribs, blocks, drums and buttresses,
    // some running into round wells, set in a low bed where the plates under them flatten. The same rules on every
    // plane: no preferred axis.
    public static class GreebleField
    {
        public const float MaxHeight=0.8f;
        const float Big=32f,Small=12f;
        static readonly float[] Levels={0.1f,0.2f,0.32f,0.45f,0.6f,0.8f};
        // A top-level plate, with its district's density and height.
        struct Plate{public Rect r;public uint seed;public float density,h;}

        public struct Face
        {
            public int axis,sign;      // the face normal: axis 0/1/2 and its sign
            public float plane;        // station coordinate of the face along that axis (m)
            public Rect window;        // the face in (u,v): u is axis (axis+1)%3, v is axis (axis+2)%3
            public System.Func<float,float,bool> continues; // whether relief carries on at a (u,v) just outside the window
            public List<Rect> holes;   // (u,v) areas left bare for family forms
            public Vector3 origin;     // station position of the mesh origin
        }

        // A curved piece: a band round c from radius ri to ro over span radians from a0, or a full ring (a well).
        struct Curve{public Vector2 c;public float ri,ro,a0,span,h;public bool well,endsInWell;public Vector2 wellAt;public uint seed;}
        sealed class Ctx{public TileMesh m;public Face face;public Finish finish;public readonly List<float> xs=new List<float>(),ys=new List<float>();public readonly List<Curve> curves=new List<Curve>();}

        static uint Hash(uint x){x^=x>>16;x*=0x7feb352dU;x^=x>>15;x*=0x846ca68bU;x^=x>>16;return x;}
        static uint Hash(uint a,int b)=>Hash(a*0x9E3779B1U^Hash((uint)b+0x632BE5ABU));
        static float R(uint seed,int k)=>(Hash(seed,k)&0xFFFFFF)/16777216f;
        static Vector2 Dir(float a)=>new Vector2(Mathf.Cos(a),Mathf.Sin(a));

        public static void Build(TileMesh m,in Face face,in Finish finish)
        {
            var start=m.ReliefStart();
            uint plane=Hash(Hash((uint)(face.axis*2+(face.sign>0?1:0))),Mathf.RoundToInt(face.plane*4));
            var off=new Vector2(R(plane,1)*Big,R(plane,2)*Big);
            var w=face.window;
            var c=new Ctx{m=m,face=face,finish=finish};
            // Curves first, from the plates on and round this face (a curve reaches about 12 m from its plate's
            // centre): the plates under them flatten into a low bed so the curves sit in the relief rather than on
            // top of it.
            var plates=new List<Plate>();
            Plates(plane,off,Rect.MinMaxRect(w.xMin-13f,w.yMin-13f,w.xMax+13f,w.yMax+13f),plates);
            foreach(var p in plates)Curves(c,p);
            foreach(var cv in c.curves)Curved(c,cv);
            foreach(var p in plates)
            {
                if(!Overlaps(p.r,w))continue;
                var r=p.r;
                if(R(p.seed,40)<0.55f)
                {
                    // A raised rim along some edges of the plate, never all four: the large-scale lines that read
                    // from far away, stopping and starting instead of closing into boxes.
                    float rimH=Levels[Mathf.Min(Levels.Length-1,Level(p.h)+1)],rw=0.55f;
                    var e=new bool[4];   // bottom, top, left, right
                    int n=0;
                    for(int k=0;k<4;k++){e[k]=R(p.seed,60+k)<0.5f;if(e[k])n++;}
                    if(n==0)e[(int)(R(p.seed,64)*4)&3]=true;
                    if(n==4)e[(int)(R(p.seed,64)*4)&3]=false;
                    var o=Inset(r,0.15f);
                    if(e[0])Box(c,new Rect(o.xMin,o.yMin,o.width,rw),0,rimH,p.seed);
                    if(e[1])Box(c,new Rect(o.xMin,o.yMax-rw,o.width,rw),0,rimH,p.seed);
                    float y0=e[0]?o.yMin+rw:o.yMin,y1=e[1]?o.yMax-rw:o.yMax;
                    if(e[2])Box(c,Rect.MinMaxRect(o.xMin,y0,o.xMin+rw,y1),0,rimH,p.seed);
                    if(e[3])Box(c,Rect.MinMaxRect(o.xMax-rw,y0,o.xMax,y1),0,rimH,p.seed);
                    float g=0.15f+rw+0.05f;
                    r=Rect.MinMaxRect(r.xMin+(e[2]?g:0),r.yMin+(e[0]?g:0),r.xMax-(e[3]?g:0),r.yMax-(e[1]?g:0));
                }
                Node(c,r,p.seed,0,p.h,p.density);
            }
            // the far level draws a traced shell instead, over the window less its holes
            var cells=new List<(Rect r,int sides)>();
            Cells(c,face.window,(cell,sides)=>cells.Add((cell,sides)),false);
            m.ReliefEnd(start,face.axis,face.sign,face.plane,cells,finish);
        }

        // The top-level plates over an area of a plane: a Pythagorean tiling of 32 m and 12 m squares, each row of
        // squares stepping 12 m against the next, so lines stagger both ways and none runs on for more than 44 m.
        // Each 32 m square is a district, cut at about a third to two thirds and its long pieces again across, into
        // two to four plates; the 12 m square beside it shares its district and stays whole.
        static void Plates(uint plane,Vector2 off,Rect area,List<Plate> o)
        {
            const float A=Big,B=Small,D=A*A+B*B;
            // lattice cell (i,j) has its big square at off+i*(A,B)+j*(-B,A) and its small square A to the right of it
            int i0=int.MaxValue,i1=int.MinValue,j0=int.MaxValue,j1=int.MinValue;
            foreach(var q in new[]{new Vector2(area.xMin,area.yMin),new Vector2(area.xMax,area.yMin),new Vector2(area.xMin,area.yMax),new Vector2(area.xMax,area.yMax)})
            {
                var d=q-off;
                float fi=(A*d.x+B*d.y)/D,fj=(A*d.y-B*d.x)/D;
                i0=Mathf.Min(i0,Mathf.FloorToInt(fi));i1=Mathf.Max(i1,Mathf.CeilToInt(fi));
                j0=Mathf.Min(j0,Mathf.FloorToInt(fj));j1=Mathf.Max(j1,Mathf.CeilToInt(fj));
            }
            for(int i=i0-2;i<=i1+1;i++)for(int j=j0-2;j<=j1+1;j++)
            {
                var p=off+new Vector2(i*A-j*B,i*B+j*A);
                uint district=Hash(Hash(plane,i+104729),j);
                // Some districts are quiet fields of big plates, others dense machinery.
                float density=R(district,1)<0.3f?0.32f:Mathf.Lerp(0.75f,1.6f,R(district,3));
                float h=Levels[(density<0.5f?0:1)+(int)(R(district,2)*3)];
                Split(new Rect(p.x,p.y,A,A),district,0,density,h,area,o);
                var small=new Rect(p.x+A,p.y,B,B);
                if(Overlaps(small,area))o.Add(new Plate{r=small,seed=Hash(district,303),density=density,h=h});
            }
        }
        static void Split(Rect r,uint seed,int depth,float density,float h,Rect area,List<Plate> o)
        {
            if(depth==2||(depth==1&&(Mathf.Max(r.width,r.height)<26f||R(seed,1)<0.25f)))
            {
                if(Overlaps(r,area))o.Add(new Plate{r=r,seed=seed,density=density,h=h});
                return;
            }
            bool alongU=r.width>r.height+0.5f||(Mathf.Abs(r.width-r.height)<=0.5f&&R(seed,2)<0.5f);
            float len=alongU?r.width:r.height,start=alongU?r.xMin:r.yMin;
            float cut=start+Mathf.Round(len*Mathf.Lerp(0.34f,0.66f,R(seed,3))*2f)*0.5f;
            Split(alongU?Rect.MinMaxRect(start,r.yMin,cut,r.yMax):Rect.MinMaxRect(r.xMin,start,r.xMax,cut),Hash(seed,301),depth+1,density,h,area,o);
            Split(alongU?Rect.MinMaxRect(cut,r.yMin,r.xMax,r.yMax):Rect.MinMaxRect(r.xMin,cut,r.xMax,r.yMax),Hash(seed,302),depth+1,density,h,area,o);
        }

        // The curves of one top-level plate: often a wide arc several tiles long, half of them ending in a well; on
        // some plates a well on its own.
        static void Curves(Ctx c,in Plate p)
        {
            uint seed=p.seed;
            var center=p.r.center+new Vector2((R(seed,42)-0.5f)*3f,(R(seed,43)-0.5f)*3f);
            if(R(seed,41)<0.5f)
            {
                float rm=Mathf.Lerp(4f,6.8f,R(seed,44)),w=Mathf.Lerp(1.3f,2.2f,R(seed,45));
                float a0=R(seed,46)*Mathf.PI*2,span=Mathf.Lerp(1.6f,4.4f,R(seed,47));
                var arc=new Curve{c=center,ri=rm-w*0.5f,ro=rm+w*0.5f,a0=a0,span=span,h=Levels[3+(int)(R(seed,48)*2)],seed=Hash(seed,90)};
                if(R(seed,49)<0.5f)
                {
                    arc.endsInWell=true;arc.wellAt=center+Dir(a0+span)*rm;
                    c.curves.Add(new Curve{c=arc.wellAt,ri=1.75f,ro=2.45f,span=Mathf.PI*2,h=Levels[4],well=true,seed=Hash(seed,91)});
                }
                c.curves.Add(arc);
            }
            else if(R(seed,50)<0.35f)
                c.curves.Add(new Curve{c=center,ri=1.75f,ro=2.45f,span=Mathf.PI*2,h=Levels[4],well=true,seed=Hash(seed,92)});
        }

        // Sector of a ring between radii r0..r1 and angles b0..b1, its top rising from hIn at r0 to hOut at r1.
        static void Sector(Ctx c,Vector2 o,float r0,float r1,float b0,float b1,float hIn,float hOut,float hBase,in Finish f)
        {
            var g=new Vector3[8];
            for(int q=0;q<8;q++)
            {
                bool outer=(q&2)!=0;var p=o+Dir((q&1)!=0?b1:b0)*(outer?r1:r0);
                g[q]=P(c,p.x,p.y,(q&4)!=0?(outer?hOut:hIn):hBase);
            }
            c.m.Hexa(f,g);
        }

        // A curve's geometry, cut into short pieces; each face builds the pieces centred on it, so the curve
        // continues across tiles. A band with cross ribs, blocks and drums along its top, angled buttresses
        // stepping down its sides and heavy blocks at its ends; a well is a ring wall with buttresses all round
        // and a drum in the middle.
        static void Curved(Ctx c,in Curve cv)
        {
            float rm=(cv.ri+cv.ro)*0.5f,w=cv.ro-cv.ri;
            var f=c.finish.Shade(0.95f+R(cv.seed,1)*0.1f);
            int n=Mathf.CeilToInt(cv.span*rm/0.5f);
            for(int k=0;k<n;k++)
            {
                float b0=cv.a0+cv.span*k/n,b1=cv.a0+cv.span*(k+1)/n;
                var mid=cv.c+Dir((b0+b1)*0.5f)*rm;
                if(!Free(c,mid))continue;
                if(cv.endsInWell&&(mid-cv.wellAt).sqrMagnitude<2.45f*2.45f)continue;
                Sector(c,cv.c,cv.ri,cv.ro,b0,b1,cv.h,cv.h,0,f);
            }
            // cross ribs every ~1.6 m
            int ribs=Mathf.Max(1,Mathf.RoundToInt(cv.span*rm/1.6f));
            float rw=0.28f/rm;
            for(int k=0;k<ribs;k++)
            {
                float b=cv.a0+cv.span*(k+0.5f)/ribs;
                var mid=cv.c+Dir(b)*rm;
                if(!Free(c,mid)||(cv.endsInWell&&(mid-cv.wellAt).sqrMagnitude<2.9f*2.9f))continue;
                Sector(c,cv.c,cv.ri-0.1f,cv.ro+0.1f,b-rw,b+rw,Mathf.Min(MaxHeight,cv.h+0.08f),Mathf.Min(MaxHeight,cv.h+0.08f),0,f);
            }
            // blocks and drums along the top, between the ribs
            for(int k=0;k<ribs;k++)
            {
                uint s=Hash(cv.seed,k+10);
                if(R(s,0)<0.25f)continue;
                if(k==ribs-1)break;
                float b=cv.a0+cv.span*(k+1f)/ribs;
                float rr=rm+(R(s,2)-0.5f)*w*0.45f;
                var p=cv.c+Dir(b)*rr;
                if(!Free(c,p)||(cv.endsInWell&&(p-cv.wellAt).sqrMagnitude<2.9f*2.9f))continue;
                float top=Mathf.Min(MaxHeight,cv.h+0.12f+R(s,3)*0.1f);
                if(R(s,4)<0.55f)
                {
                    float hw=Mathf.Lerp(0.18f,0.3f,R(s,5)),ha=Mathf.Lerp(0.2f,0.45f,R(s,6))/rr;
                    Sector(c,cv.c,rr-hw,rr+hw,b-ha,b+ha,top,top,cv.h,f);
                }
                else
                {
                    float r=Mathf.Lerp(0.18f,0.32f,R(s,5));
                    c.m.Lathe(f,P(c,p.x,p.y,cv.h),Normal(c),new[]{new Vector2(0,0),new Vector2(r,0),new Vector2(r,top-cv.h),new Vector2(0,top-cv.h)},14,false);
                }
            }
            // angled buttresses down the sides, mostly outside
            int nb=cv.well?8:Mathf.Max(1,Mathf.RoundToInt(cv.span*rm/2.8f));
            for(int k=0;k<nb;k++)
            {
                uint s=Hash(cv.seed,k+200);
                float b=cv.a0+cv.span*(k+0.5f)/nb;
                float len=cv.well?1.4f:Mathf.Lerp(0.8f,1.2f,R(s,0)),ha=0.16f/cv.ro;
                var p=cv.c+Dir(b)*(cv.ro+len*0.5f);
                if(Free(c,p)&&!(cv.endsInWell&&(p-cv.wellAt).sqrMagnitude<3.2f*3.2f))
                    Sector(c,cv.c,cv.ro-0.05f,cv.ro+len,b-ha,b+ha,cv.h,Levels[0],0,f);
                if(!cv.well&&R(s,1)<0.4f)
                {
                    float hi=0.16f/cv.ri;var q=cv.c+Dir(b+0.5f*cv.span/nb)*(cv.ri-0.4f);
                    if(cv.ri>1.4f&&Free(c,q))Sector(c,cv.c,cv.ri-0.8f,cv.ri+0.05f,b+0.5f*cv.span/nb-hi,b+0.5f*cv.span/nb+hi,Levels[0],cv.h,0,f);
                }
            }
            if(cv.well)
            {
                var p=cv.c;
                if(Free(c,p))
                {
                    c.m.Lathe(f,P(c,p.x,p.y,0),Normal(c),new[]{new Vector2(0,0),new Vector2(0.75f,0),new Vector2(0.75f,0.4f),new Vector2(0.45f,0.55f),new Vector2(0,0.55f)},20,false);
                }
                return;
            }
            // heavy blocks at the ends (not where the arc runs into a well)
            float ea=0.8f/rm;
            foreach(var (e,dirn) in new[]{(cv.a0,-1f),(cv.a0+cv.span,1f)})
            {
                if(dirn>0&&cv.endsInWell)continue;
                var p=cv.c+Dir(e)*rm;
                if(!Free(c,p))continue;
                Sector(c,cv.c,cv.ri-0.2f,cv.ro+0.2f,e-ea,e+ea,Mathf.Min(MaxHeight,cv.h+0.2f),Mathf.Min(MaxHeight,cv.h+0.2f),0,f);
            }
        }

        // Whether a rect lies under a curve or its buttresses: such plates flatten into the low bed round it.
        static bool UnderCurve(Ctx c,Rect r)
        {
            foreach(var cv in c.curves)
            {
                float reach=cv.ro+(cv.well?1.7f:1.4f),inner=cv.well?0:cv.ri-1.1f;
                var near=new Vector2(Mathf.Clamp(cv.c.x,r.xMin,r.xMax),Mathf.Clamp(cv.c.y,r.yMin,r.yMax));
                if((near-cv.c).sqrMagnitude>reach*reach)continue;
                for(int x=0;x<=4;x++)for(int y=0;y<=4;y++)
                {
                    var d=new Vector2(Mathf.Lerp(r.xMin,r.xMax,x/4f),Mathf.Lerp(r.yMin,r.yMax,y/4f))-cv.c;
                    float dist=d.magnitude;
                    if(dist<inner||dist>reach)continue;
                    if(cv.span>=6.28f)return true;
                    float margin=1.0f/Mathf.Max(dist,1f);
                    float ang=Mathf.Repeat(Mathf.Atan2(d.y,d.x)-cv.a0+margin,Mathf.PI*2);
                    if(ang<=cv.span+2*margin)return true;
                }
            }
            return false;
        }
        // Inside this face's window and outside its holes.
        static bool Free(Ctx c,Vector2 p)
        {
            var w=c.face.window;
            if(p.x<w.xMin||p.x>=w.xMax||p.y<w.yMin||p.y>=w.yMax)return false;
            if(c.face.holes!=null)foreach(var h in c.face.holes)if(h.Contains(p))return false;
            return true;
        }
        static bool Clear(Ctx c,Rect r)
        {
            var w=c.face.window;
            if(r.xMin<w.xMin||r.xMax>w.xMax||r.yMin<w.yMin||r.yMax>w.yMax)return false;
            if(c.face.holes!=null)foreach(var h in c.face.holes)if(Overlaps(r,h))return false;
            return true;
        }
        static Vector3 P(Ctx c,float u,float v,float h)
        {
            var F=c.face;int a=F.axis;var p=Vector3.zero;p[(a+1)%3]=u;p[(a+2)%3]=v;p[a]=F.plane+F.sign*h;return p-F.origin;
        }
        static Vector3 Normal(Ctx c){var n=Vector3.zero;n[c.face.axis]=c.face.sign;return n;}

        static int Level(float h){int best=0;for(int i=1;i<Levels.Length;i++)if(Mathf.Abs(Levels[i]-h)<Mathf.Abs(Levels[best]-h))best=i;return best;}
        static Rect Inset(Rect r,float d)=>Rect.MinMaxRect(r.xMin+d,r.yMin+d,r.xMax-d,r.yMax-d);
        static bool Overlaps(Rect a,Rect b)=>a.xMin<b.xMax&&a.xMax>b.xMin&&a.yMin<b.yMax&&a.yMax>b.yMin;

        static void Node(Ctx c,Rect r,uint seed,int depth,float h,float density)
        {
            if(!Overlaps(r,c.face.window))return;
            float w=r.width,ht=r.height,big=Mathf.Max(w,ht);
            float stop=depth<1?0:Mathf.Clamp01((0.16f+0.13f*depth)/density);
            if(big<1.2f||(big<6.5f&&R(seed,0)<stop)){Leaf(c,r,seed,h);return;}
            bool alongU=w>ht*1.3f||(ht<=w*1.3f&&R(seed,1)<0.5f);
            float len=alongU?w:ht,start=alongU?r.xMin:r.yMin;
            int parts=len>4.5f&&R(seed,2)<0.35f?3:2;
            var cuts=new List<float>{start};
            float last=start;
            for(int k=1;k<parts;k++)
            {
                float f=(k+(R(seed,3+k)-0.5f)*0.55f)/parts;
                float cut=start+Mathf.Round(f*len/0.25f)*0.25f;
                cut=Mathf.Clamp(cut,last+0.5f,start+len-0.5f*(parts-k));
                cuts.Add(cut);last=cut;
            }
            cuts.Add(start+len);
            for(int k=0;k<parts;k++)
            {
                var child=alongU?Rect.MinMaxRect(cuts[k],r.yMin,cuts[k+1],r.yMax):Rect.MinMaxRect(r.xMin,cuts[k],r.xMax,cuts[k+1]);
                int step=new[]{-1,0,0,1,1,2}[(int)(R(seed,10+k)*6)];
                float hk=Levels[Mathf.Clamp(Level(h)+step,0,Levels.Length-1)];
                Node(c,child,Hash(seed,k+20),depth+1,hk,density);
            }
        }

        static void Leaf(Ctx c,Rect r,uint seed,float h)
        {
            var q=Inset(r,0.07f+R(seed,3)*0.07f);
            if(UnderCurve(c,q)){if(q.width>0.12f&&q.height>0.12f)Box(c,q,0,Levels[0],seed);return;}
            float small=Mathf.Min(q.width,q.height),big=Mathf.Max(q.width,q.height);
            if(small<0.12f)return;
            float t=R(seed,4);
            int lv=Level(h);
            float up=Levels[Mathf.Min(lv+1,Levels.Length-1)],up2=Levels[Mathf.Min(lv+2,Levels.Length-1)];
            if(t<0.18f)Box(c,q,0,h,seed);
            else if(t<0.39f)
            {
                // the carved armour's plate shapes
                var kind=t<0.27f?TileMesh.PlateCut.Corner:t<0.34f?TileMesh.PlateCut.Diagonal:TileMesh.PlateCut.Jog;
                CutLeaf(c,q,seed,lv,kind);
            }
            else if(t<0.47f&&big<small*1.5f&&small>0.9f&&Clear(c,q))Round(c,q,seed,h);
            else if(t<0.53f&&small>0.6f&&Clear(c,q))
            {
                // ramp rising along the long side
                bool alongU=q.width>=q.height;bool rise=R(seed,7)<0.5f;
                var g=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    float u=(i&1)!=0?q.xMax:q.xMin,v=(i&2)!=0?q.yMax:q.yMin;
                    bool far=alongU?(i&1)!=0:(i&2)!=0;
                    g[i]=P(c,u,v,(i&4)==0?0:(far==rise?Mathf.Max(h,Levels[2]):Levels[0]));
                }
                c.m.Hexa(c.finish.Shade(0.93f+R(seed,9)*0.14f),g);
            }
            else if(t<0.60f)
            {
                // stepped plate
                Box(c,q,0,h,seed);
                if(up>h)Box(c,Inset(q,Mathf.Min(0.4f,small*0.22f)),h,up,seed+1);
            }
            else if(t<0.67f&&small>0.6f)
            {
                // sunk panel in a raised frame
                float rw=Mathf.Min(0.22f,small*0.18f);var i=Inset(q,rw);
                Box(c,new Rect(q.xMin,q.yMin,q.width,rw),0,h,seed);Box(c,new Rect(q.xMin,i.yMax,q.width,rw),0,h,seed);
                Box(c,new Rect(q.xMin,i.yMin,rw,i.height),0,h,seed);Box(c,new Rect(i.xMax,i.yMin,rw,i.height),0,h,seed);
                Box(c,i,0,Levels[0],seed+2);
            }
            else if(t<0.75f&&small>0.5f)
            {
                // ribs along the long side on a low plate
                Box(c,q,0,Levels[0],seed);
                bool alongU=q.width>=q.height;int n=Mathf.Max(1,Mathf.FloorToInt(small/0.36f));float pitch=small/n;
                for(int k=0;k<n;k++)
                {
                    float a=(alongU?q.yMin:q.xMin)+k*pitch+pitch*0.28f,b=a+pitch*0.44f;
                    Box(c,alongU?Rect.MinMaxRect(q.xMin,a,q.xMax,b):Rect.MinMaxRect(a,q.yMin,b,q.yMax),Levels[0],Mathf.Max(h,Levels[1]),seed+3);
                }
            }
            else if(t<0.85f)
            {
                // grid of small blocks at mixed heights
                int nx=Mathf.Max(1,Mathf.RoundToInt(q.width/0.5f)),ny=Mathf.Max(1,Mathf.RoundToInt(q.height/0.5f));
                float cw=q.width/nx,ch=q.height/ny;
                for(int x=0;x<nx;x++)for(int y=0;y<ny;y++)
                {
                    uint s=Hash(seed,x*31+y*977+5);
                    if(R(s,0)<0.12f)continue;
                    var b=Rect.MinMaxRect(q.xMin+x*cw+0.05f,q.yMin+y*ch+0.05f,q.xMin+(x+1)*cw-0.05f,q.yMin+(y+1)*ch-0.05f);
                    Box(c,b,0,Levels[Mathf.Clamp(lv-1+(int)(R(s,1)*3),0,Levels.Length-1)],s);
                }
            }
            else if(t<0.93f&&small>1.3f&&big<small*1.7f)
            {
                // stepped pyramid
                Box(c,q,0,h,seed);
                if(up>h)Box(c,Inset(q,small*0.2f),h,up,seed+1);
                if(up2>up)Box(c,Inset(q,small*0.36f),up,up2,seed+2);
            }
            else
            {
                // glyph: a few bars standing out of a sunk plate
                Box(c,q,0,Levels[0],seed);
                int n=2+(int)(R(seed,5)*3);
                for(int k=0;k<n;k++)
                {
                    uint s=Hash(seed,k+50);bool alongU=R(s,0)<0.5f;
                    float th=Mathf.Min(0.18f,small*0.25f),length=(alongU?q.width:q.height)*Mathf.Lerp(0.3f,0.8f,R(s,1));
                    float a=Mathf.Lerp(alongU?q.xMin:q.yMin,(alongU?q.xMax:q.yMax)-length,R(s,2));
                    float b=Mathf.Lerp(alongU?q.yMin:q.xMin,(alongU?q.yMax:q.xMax)-th,R(s,3));
                    var bar=alongU?new Rect(a,b,length,th):new Rect(b,a,th,length);
                    Box(c,bar,Levels[0],Mathf.Max(Levels[1],h)-k*0.015f,s);
                }
            }
        }

        // A leaf cut like the carved armour's plates, at relief scale: a corner cut away at 45 degrees, or split along a
        // diagonal or a jogging groove into two parts a level apart. Falls back to a corner cut where a split doesn't fit.
        static void CutLeaf(Ctx c,Rect q,uint seed,int lv,TileMesh.PlateCut kind)
        {
            float s=Mathf.Min(q.width,q.height)/0.6f;
            int lo=lv<Levels.Length-1?lv:lv-1;
            var pieces=new List<(List<Vector2> poly,float h)>();
            System.Func<int,float> rnd=k=>R(seed,60+k);
            TileMesh.CutPlate(kind,q,rnd,s,0.05f,Levels[lo],Levels[lo+1],pieces);
            if(pieces.Count==0)TileMesh.CutPlate(TileMesh.PlateCut.Corner,q,rnd,s,0.05f,Levels[lv],Levels[lv],pieces);
            var f=c.finish.Shade(0.93f+R(seed,9)*0.14f);var n=Normal(c);
            System.Func<Vector2,float,Vector3> lift=(p,ht)=>P(c,p.x,p.y,ht);
            // Clipped to the face like a box: each part of the window outside the holes builds its piece, with sides
            // only where the relief doesn't carry on.
            foreach(var (poly,h) in pieces)Cells(c,q,(cell,sides)=>
            {
                var piece=poly;
                piece=TileMesh.ClipLeft(piece,new Vector2(cell.xMin,cell.yMin),Vector2.right);
                piece=TileMesh.ClipLeft(piece,new Vector2(cell.xMax,cell.yMin),Vector2.up);
                piece=TileMesh.ClipLeft(piece,new Vector2(cell.xMax,cell.yMax),Vector2.left);
                piece=TileMesh.ClipLeft(piece,new Vector2(cell.xMin,cell.yMax),Vector2.down);
                if(piece.Count<3||Mathf.Abs(TileMesh.Area(piece))<1e-4f)return;
                bool Side(Vector2 a,Vector2 b)
                {
                    const float eps=1e-4f;
                    if(Mathf.Abs(a.x-cell.xMin)<eps&&Mathf.Abs(b.x-cell.xMin)<eps)return (sides&1)!=0;
                    if(Mathf.Abs(a.x-cell.xMax)<eps&&Mathf.Abs(b.x-cell.xMax)<eps)return (sides&2)!=0;
                    if(Mathf.Abs(a.y-cell.yMin)<eps&&Mathf.Abs(b.y-cell.yMin)<eps)return (sides&4)!=0;
                    if(Mathf.Abs(a.y-cell.yMax)<eps&&Mathf.Abs(b.y-cell.yMax)<eps)return (sides&8)!=0;
                    return true;
                }
                c.m.Slab(f,piece,0,h,lift,n,0,Side);
            });
        }

        // Round leaf: a drum, a low dome on a plinth, or a ring round a sunk disc.
        static void Round(Ctx c,Rect q,uint seed,float h)
        {
            var f=c.finish.Shade(0.93f+R(seed,9)*0.14f);
            float r=Mathf.Min(q.width,q.height)*0.46f;
            var o=P(c,q.center.x,q.center.y,0);var n=Normal(c);
            float t=R(seed,6);
            float top=Mathf.Max(h,Levels[2]);
            if(t<0.35f)
            {
                c.m.Lathe(f,o,n,new[]{new Vector2(0,0),new Vector2(r,0),new Vector2(r,top),new Vector2(0,top)},24,false);
                c.m.Lathe(f,o,n,new[]{new Vector2(0,top),new Vector2(r*0.55f,top),new Vector2(r*0.55f,Mathf.Min(MaxHeight,top+0.15f)),new Vector2(0,Mathf.Min(MaxHeight,top+0.15f))},20,false);
            }
            else if(t<0.7f)
            {
                c.m.Lathe(f,o,n,new[]{new Vector2(0,0),new Vector2(r,0),new Vector2(r,Levels[0]),new Vector2(r*0.85f,Levels[0])},24,false);
                var dome=new List<Vector2>();
                for(int k=0;k<=6;k++){float a=k/6f*Mathf.PI*0.5f;dome.Add(new Vector2(r*0.85f*Mathf.Cos(a),Levels[0]+(MaxHeight-Levels[0])*Mathf.Sin(a)));}
                c.m.Lathe(f,o,n,dome,24,true);
            }
            else
            {
                float ri=r*0.62f;
                c.m.Lathe(f,o,n,new[]{new Vector2(r,0),new Vector2(r,top),new Vector2(ri,top),new Vector2(ri,0)},24,false);
                c.m.Lathe(f,o,n,new[]{new Vector2(0,0),new Vector2(ri,0),new Vector2(ri,Levels[0]),new Vector2(0,Levels[0])},20,false);
            }
        }

        // A raised box from h0 to h1, clipped to the face window and its holes. Sides are kept on the box's own edges
        // and round holes; at the window edge only where the relief does not carry on into the neighbouring face
        // (convex edges, and where the neighbour's face is taken by a family form).
        static void Box(Ctx c,Rect r,float h0,float h1,uint seed)
        {
            if(h1-h0<0.005f||r.width<0.01f||r.height<0.01f)return;
            if(h1>Levels[0]&&UnderCurve(c,r)){if(h0>=Levels[0])return;h1=Levels[0];}
            var f=c.finish.Shade(0.93f+R(seed,9)*0.14f);
            Cells(c,r,(cell,sides)=>Emit(c,cell,h0,h1,sides,f));
        }
        // The parts of r inside the face window and outside its holes, each with the sides it needs: on r's own
        // edges (unless ownEdges is false: then they count as the window's) and round holes; at the window edge only
        // where the relief does not carry on into the neighbouring face. sides: 1 left (u min), 2 right, 4 bottom
        // (v min), 8 top.
        static void Cells(Ctx c,Rect r,System.Action<Rect,int> emit,bool ownEdges=true)
        {
            var win=c.face.window;
            if(!Overlaps(r,win))return;
            var holes=c.face.holes;
            bool inside=r.xMin>=win.xMin&&r.xMax<=win.xMax&&r.yMin>=win.yMin&&r.yMax<=win.yMax;
            bool hit=false;
            if(holes!=null)foreach(var hole in holes)if(Overlaps(r,hole)){hit=true;break;}
            if(inside&&!hit&&ownEdges){emit(r,15);return;}
            var xs=c.xs;var ys=c.ys;xs.Clear();ys.Clear();
            xs.Add(r.xMin);xs.Add(r.xMax);ys.Add(r.yMin);ys.Add(r.yMax);
            void AddX(float x){if(x>r.xMin&&x<r.xMax)xs.Add(x);}
            void AddY(float y){if(y>r.yMin&&y<r.yMax)ys.Add(y);}
            AddX(win.xMin);AddX(win.xMax);AddY(win.yMin);AddY(win.yMax);
            if(holes!=null)foreach(var hole in holes)if(Overlaps(r,hole)){AddX(hole.xMin);AddX(hole.xMax);AddY(hole.yMin);AddY(hole.yMax);}
            xs.Sort();ys.Sort();
            int nx=xs.Count-1,ny=ys.Count-1;
            bool Keep(int i,int j)
            {
                if(i<0||j<0||i>=nx||j>=ny)return false;
                float x=(xs[i]+xs[i+1])*0.5f,y=(ys[j]+ys[j+1])*0.5f;
                if(xs[i+1]-xs[i]<1e-4f||ys[j+1]-ys[j]<1e-4f)return false;
                if(x<win.xMin||x>win.xMax||y<win.yMin||y>win.yMax)return false;
                if(holes!=null)foreach(var hole in holes)if(x>hole.xMin&&x<hole.xMax&&y>hole.yMin&&y<hole.yMax)return false;
                return true;
            }
            int Side(int i,int j,int di,int dj,int bit,bool onEdge)
            {
                if(onEdge&&ownEdges)return bit;
                if(Keep(i+di,j+dj))return 0;
                float x=di==0?(xs[i]+xs[i+1])*0.5f:(di<0?xs[i]-1e-3f:xs[i+1]+1e-3f);
                float y=dj==0?(ys[j]+ys[j+1])*0.5f:(dj<0?ys[j]-1e-3f:ys[j+1]+1e-3f);
                if(x<win.xMin||x>win.xMax||y<win.yMin||y>win.yMax)return c.face.continues!=null&&c.face.continues(x,y)?0:bit;
                return bit;
            }
            for(int i=0;i<nx;i++)for(int j=0;j<ny;j++)
            {
                if(!Keep(i,j))continue;
                int sides=Side(i,j,-1,0,1,i==0)|Side(i,j,1,0,2,i==nx-1)|Side(i,j,0,-1,4,j==0)|Side(i,j,0,1,8,j==ny-1);
                emit(Rect.MinMaxRect(xs[i],ys[j],xs[i+1],ys[j+1]),sides);
            }
        }

        static void Emit(Ctx c,Rect r,float h0,float h1,int sides,in Finish f)=>
            c.m.FaceBox(f,c.face.axis,c.face.sign,c.face.plane,c.face.origin,r,h0,h1,sides);
    }
}
