using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    // Material slots of a settlement tile: one submesh and one TileLibrary material each.
    // Relief: the far level's shells over the station relief (TileMesh.Shells.cs), traced by TileSurface.
    public enum TileSlot { Paint, Trim, Dark, Metal, Recess, Glass, Light, Accent, Relief }

    // Surface treatment read by DeathStar/Tile Surface from uv0.x.
    // Carved: the near level's armour carving (plates and the face under them), plain paint marked for the relief view.
    public enum TilePattern { Plain, Panels, Hazard, Deck, Grille, Markings, Armour, Carved }

    public readonly struct Finish
    {
        public readonly TileSlot slot;
        public readonly TilePattern pattern;
        public readonly Color tint;
        public readonly float wear;
        public Finish(TileSlot slot,TilePattern pattern,Color tint,float wear=1f){this.slot=slot;this.pattern=pattern;this.tint=tint;this.wear=wear;}
        public Finish(TileSlot slot,TilePattern pattern=TilePattern.Plain):this(slot,pattern,Color.white){}
        public Finish Shade(float k)=>new Finish(slot,pattern,new Color(tint.r*k,tint.g*k,tint.b*k),wear);
        public Finish With(TilePattern p)=>new Finish(slot,p,tint,wear);
        public Finish Worn(float w)=>new Finish(slot,pattern,tint,w);
    }

    // Builds a tile's mesh from flat-shaded primitives. Each primitive is placed through a transform stack; face
    // frames (TileBuilder.Face) have x to the right and y up as seen from outside, and z pointing into the face.
    public sealed partial class TileMesh
    {
        public const int SlotCount=9;
        readonly List<Vector3> positions=new List<Vector3>(),normals=new List<Vector3>();
        readonly List<Vector4> tangents=new List<Vector4>();
        readonly List<Vector2> codes=new List<Vector2>(),local=new List<Vector2>(),extent=new List<Vector2>();
        readonly List<Color> colors=new List<Color>();
        readonly List<Vector4> overlays=new List<Vector4>();
        readonly List<Vector4> surfaceFrames=new List<Vector4>();
        // Curved Foundry meshes retain their source face; w is its signed station-space plane.
        public Vector4 surfaceFrame;
        public bool retainWarpData;
        readonly List<Vector4> metricAnchors=new List<Vector4>();
        public Vector4 metricAnchor;
        readonly List<int>[] indices=new List<int>[SlotCount];
        readonly Stack<Matrix4x4> stack=new Stack<Matrix4x4>();
        Matrix4x4 matrix=Matrix4x4.identity;
        public float seed;
        // Planning-overlay colour for everything this mesh holds (uv3; alpha 0 = no overlay colour).
        public Color overlay=Color.clear;

        public TileMesh(){for(int i=0;i<SlotCount;i++)indices[i]=new List<int>();}
        public int VertexCount=>positions.Count+jobs.Count*4;
        public int TriangleCount{get{int n=0;foreach(var list in indices)n+=list.Count;return n/3;}}
        public Bounds Bounds{get{if(positions.Count==0)return default;var b=new Bounds(positions[0],Vector3.zero);foreach(var p in positions)b.Encapsulate(p);return b;}}

        public void Push(Vector3 position,Quaternion rotation){stack.Push(matrix);matrix=matrix*Matrix4x4.TRS(position,rotation,Vector3.one);}
        // Uniform scale about a point of the current frame (equipment size classes).
        public void PushScale(Vector3 pivot,float scale)
        {stack.Push(matrix);matrix=matrix*Matrix4x4.TRS(pivot,Quaternion.identity,Vector3.one*scale)*Matrix4x4.Translate(-pivot);}
        public void Pop()=>matrix=stack.Pop();

        // Fixed-size parts: built at true size round an anchor (CurvedTileMesh.Metric). Their vertex ranges let the
        // lab check the size they come out at. Only free-standing parts: anything that must meet the face's wall or
        // a cell edge exactly stays in station space.
        public readonly List<(string name,int start,int end)> fixedParts=new List<(string,int,int)>();
        // frame: the plane the part stands on, for parts not on a face (connector hubs); default the current face.
        public FixedPart BeginFixed(string name,Vector3 anchor,Vector4? frame=null)=>new FixedPart(this,name,new Vector4(anchor.x,anchor.y,anchor.z,1),frame,fixedParts);
        // A fixed part that stays rigid all through: none of it drops onto the bent surface (CurvedTileMesh.Foot), for
        // parts that stand free rather than on a face, such as a corridor junction's box.
        public FixedPart BeginRigid(string name,Vector3 anchor,Vector4 frame)=>new FixedPart(this,name,new Vector4(anchor.x,anchor.y,anchor.z,1.25f),frame,fixedParts);
        // Tube parts (corridors, conduits): a true-size cross-section round the spine through origin along axis A
        // (a lattice direction); the run follows the cells. Ranges let the lab check joints and widths.
        public readonly List<(string name,int start,int end)> tubeParts=new List<(string,int,int)>();
        public FixedPart BeginTube(string name,Vector3 origin,Vector3 A)=>new FixedPart(this,name,new Vector4(origin.x,origin.y,origin.z,2),new Vector4(A.x,A.y,A.z,0),tubeParts);
        public readonly struct FixedPart:System.IDisposable
        {
            readonly TileMesh m;readonly string name;readonly int start;readonly Vector4 savedAnchor,savedFrame;
            readonly List<(string,int,int)> list;
            public FixedPart(TileMesh m,string name,Vector4 anchor,Vector4? frame,List<(string,int,int)> list)
            {
                this.m=m;this.name=name;this.list=list;start=m.positions.Count;savedAnchor=m.metricAnchor;savedFrame=m.surfaceFrame;
                m.metricAnchor=anchor;if(frame.HasValue)m.surfaceFrame=frame.Value;
            }
            public void Dispose()
            {m.metricAnchor=savedAnchor;m.surfaceFrame=savedFrame;if(m.positions.Count>start)list.Add((name,start,m.positions.Count));}
        }
        // Kit parts of wedge and apex cells, built in curved space from the cell's real corners: positions are world
        // positions less the tile's station centre, and CurvedTileMesh.Bend leaves them as they are.
        public readonly List<(string name,int start,int end)> kitParts=new List<(string,int,int)>();
        public FixedPart BeginWorld(string name)=>new FixedPart(this,name,new Vector4(0,0,0,3),Vector4.zero,kitParts);
        // Any local transform on the stack (e.g. undoing a cell's stretch along a tube's run).
        public void PushMatrix(Matrix4x4 local){stack.Push(matrix);matrix=matrix*local;}

        // Parts with a far level of their own (kits, family forms, relief): Detail builds them as they are for the near
        // mesh and keeps the build, which ToMeshes runs again coarse for the far mesh in their place. Coarse level 1: a
        // third of the sides on revolutions (never under six to the turn), half the steps on domes and dishes, no
        // chamfers, armour plain. A far mesh still over farBudget is built at level 2: a fifth of the sides (never
        // under four), a third of the steps.
        public int coarse,farBudget;
        bool inDetail;
        readonly List<(int v0,int v1,int j0,int j1,int[] i0,int[] i1)> details=new List<(int,int,int,int,int[],int[])>();
        readonly List<(System.Action<TileMesh> build,Matrix4x4 matrix,Vector4 frame,Vector4 anchor,int panes)> farBuilds=
            new List<(System.Action<TileMesh>,Matrix4x4,Vector4,Vector4,int)>();
        int[] IndexCounts(){var c=new int[SlotCount];for(int i=0;i<SlotCount;i++)c[i]=indices[i].Count;return c;}
        public void Detail(System.Action<TileMesh> build)
        {
            if(coarse>0||inDetail){build(this);return;}
            int v0=positions.Count,j0=jobs.Count;var i0=IndexCounts();
            farBuilds.Add((build,matrix,surfaceFrame,metricAnchor,panes));
            inDetail=true;build(this);inDetail=false;
            details.Add((v0,positions.Count,j0,jobs.Count,i0,IndexCounts()));
        }
        TileMesh Coarse(int level)
        {
            var t=new TileMesh{coarse=level,stationOffset=stationOffset,cellHalf=cellHalf,retainWarpData=retainWarpData,courseAxis=courseAxis,
                courseSide=courseSide,seed=seed,overlay=overlay};
            foreach(var (build,m,frame,anchor,p) in farBuilds){t.matrix=m;t.surfaceFrame=frame;t.metricAnchor=anchor;t.panes=p;build(t);}
            return t;
        }
        int Sides(int n,float arc=Mathf.PI*2)
        {
            if(coarse==0)return n;
            float turn=arc/(Mathf.PI*2);
            return coarse==1?Mathf.Max(Mathf.CeilToInt(6*turn-1e-3f),Mathf.CeilToInt(n/3f)):Mathf.Max(Mathf.CeilToInt(4*turn-1e-3f),Mathf.CeilToInt(n/5f));
        }
        int Steps(int n,int least=2)=>coarse==0?n:Mathf.Max(least,coarse==1?n/2:n/3);

        // Dry: the primitives return at once and build nothing (the transform stack and part scopes still work), for
        // running a face's forms only for the rects they report (TileBuilder.FaceHoles).
        public bool dry;

        int Vertex(Vector3 p,Vector3 n,Vector4 t,in Finish f,Vector2 g=default,Vector2 size=default)
        {
            positions.Add(matrix.MultiplyPoint3x4(p));normals.Add(matrix.MultiplyVector(n).normalized);
            Vector3 tw=matrix.MultiplyVector(t).normalized;tangents.Add(new Vector4(tw.x,tw.y,tw.z,t.w==0?1:t.w));
            codes.Add(new Vector2((int)f.pattern,seed));local.Add(g);extent.Add(size);
            colors.Add(new Color(f.tint.r,f.tint.g,f.tint.b,f.wear));overlays.Add(overlay);
            surfaceFrames.Add(surfaceFrame);
            metricAnchors.Add(metricAnchor);
            faces.Add(face);
            return positions.Count-1;
        }
        void Emit(TileSlot slot,int a,int b,int c,int d,bool flip)
        {
            var list=indices[(int)slot];
            if(!flip){list.Add(a);list.Add(b);list.Add(c);list.Add(a);list.Add(c);list.Add(d);}
            else{list.Add(a);list.Add(c);list.Add(b);list.Add(a);list.Add(d);list.Add(c);}
        }
        // Diagonal cross product: robust when one edge of the quad collapses.
        static bool Flip(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n)=>Vector3.Dot(Vector3.Cross(c-a,d-b),n)<0;

        public void Quad(in Finish f,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 n,Vector3 t)
        {
            if(dry||Hidden(a,b,c,d,n))return;
            if(f.pattern==TilePattern.Armour&&coarse==0&&Carve(f,a,b,c,d,n))return;
            NextFace();
            Vector4 t4=t;t4.w=1;
            Emit(f.slot,Vertex(a,n,t4,f),Vertex(b,n,t4,f),Vertex(c,n,t4,f),Vertex(d,n,t4,f),Flip(a,b,c,d,n));
        }
        public void QuadSmooth(in Finish f,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 na,Vector3 nb,Vector3 nc,Vector3 nd,Vector3 t)
        {
            if(dry||Hidden(a,b,c,d,na+nb+nc+nd))return;
            NextFace(true);
            Vector4 t4=t;t4.w=1;
            Emit(f.slot,Vertex(a,na,t4,f),Vertex(b,nb,t4,f),Vertex(c,nc,t4,f),Vertex(d,nd,t4,f),Flip(a,b,c,d,na+nb+nc+nd));
        }
        public void Triangle(in Finish f,Vector3 a,Vector3 b,Vector3 c,Vector3 n)
        {
            if(dry)return;
            NextFace();
            Vector4 t4=(b-a).normalized;t4.w=1;
            int i=Vertex(a,n,t4,f),j=Vertex(b,n,t4,f),k=Vertex(c,n,t4,f);
            var list=indices[(int)f.slot];
            if(Vector3.Dot(Vector3.Cross(b-a,c-a),n)>=0){list.Add(i);list.Add(j);list.Add(k);}else{list.Add(i);list.Add(k);list.Add(j);}
        }
        static Vector3 Newell(IList<Vector3> p)
        {
            Vector3 n=Vector3.zero;
            for(int i=0;i<p.Count;i++){Vector3 a=p[i],b=p[(i+1)%p.Count];n.x+=(a.y-b.y)*(a.z+b.z);n.y+=(a.z-b.z)*(a.x+b.x);n.z+=(a.x-b.x)*(a.y+b.y);}
            return n;
        }

        static Vector3 Axis(int a){var v=Vector3.zero;v[a]=1;return v;}
        static Vector3 P(int a,float va,int b,float vb,int k,float vk){var p=Vector3.zero;p[a]=va;p[b]=vb;p[k]=vk;return p;}

        // plain: faces (bit axis * 2 + 1 for the + side) left uncarved, e.g. where a tube leaves through them.
        public void Box(in Finish f,Vector3 center,Vector3 size,float chamfer=0,int plain=0)
        {
            if(dry)return;
            Vector3 h=size*0.5f;if(coarse>0)chamfer=0;
            SolidBox(center,h);
            ChamferedBox(f,center,h,Mathf.Min(chamfer,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*0.5f),plain);
        }
        // Chamfered edges catch grazing light.
        void ChamferedBox(in Finish f,Vector3 o,Vector3 h,float c,int plain=0)
        {
            for(int a=0;a<3;a++)
            {
                int b=(a+1)%3,k=(a+2)%3;
                for(int s=-1;s<=1;s+=2)
                {
                    float hb=h[b]-c,hk=h[k]-c,ha=s*h[a];
                    var face=(plain&(1<<(a*2+(s>0?1:0))))!=0?f.With(TilePattern.Plain):f;
                    Quad(face,o+P(a,ha,b,-hb,k,-hk),o+P(a,ha,b,hb,k,-hk),o+P(a,ha,b,hb,k,hk),o+P(a,ha,b,-hb,k,hk),Axis(a)*s,Axis(b));
                    if(c<=0)continue;
                    for(int s2=-1;s2<=1;s2+=2)
                    {
                        Vector3 p0=P(a,s*h[a],b,s2*(h[b]-c),k,-hk),p1=P(a,s*(h[a]-c),b,s2*h[b],k,-hk);
                        Vector3 p2=P(a,s*(h[a]-c),b,s2*h[b],k,hk),p3=P(a,s*h[a],b,s2*(h[b]-c),k,hk);
                        Quad(f,o+p0,o+p1,o+p2,o+p3,(Axis(a)*s+Axis(b)*s2).normalized,Axis(k));
                    }
                }
            }
            if(c<=0)return;
            for(int sx=-1;sx<=1;sx+=2)for(int sy=-1;sy<=1;sy+=2)for(int sz=-1;sz<=1;sz+=2)
                Triangle(f,o+new Vector3(sx*h.x,sy*(h.y-c),sz*(h.z-c)),o+new Vector3(sx*(h.x-c),sy*h.y,sz*(h.z-c)),
                    o+new Vector3(sx*(h.x-c),sy*(h.y-c),sz*h.z),new Vector3(sx,sy,sz).normalized);
        }

        // Planar wall at z=0 facing out (-z), minus rectangular holes.
        public void Wall(in Finish f,Rect r,List<Rect> holes)
        {
            if(dry)return;
            var xs=new List<float>{r.xMin,r.xMax};var ys=new List<float>{r.yMin,r.yMax};
            if(holes!=null)foreach(var hole in holes)
            {
                xs.Add(Mathf.Clamp(hole.xMin,r.xMin,r.xMax));xs.Add(Mathf.Clamp(hole.xMax,r.xMin,r.xMax));
                ys.Add(Mathf.Clamp(hole.yMin,r.yMin,r.yMax));ys.Add(Mathf.Clamp(hole.yMax,r.yMin,r.yMax));
            }
            xs.Sort();ys.Sort();
            for(int j=0;j<ys.Count-1;j++)
            {
                float y0=ys[j],y1=ys[j+1];if(y1-y0<1e-4f)continue;
                int i=0;
                while(i<xs.Count-1)
                {
                    if(xs[i+1]-xs[i]<1e-4f||InHole(holes,(xs[i]+xs[i+1])*0.5f,(y0+y1)*0.5f)){i++;continue;}
                    int e=i+1;
                    while(e<xs.Count-1&&(xs[e+1]-xs[e]<1e-4f||!InHole(holes,(xs[e]+xs[e+1])*0.5f,(y0+y1)*0.5f)))e++;
                    Quad(f,new Vector3(xs[i],y0,0),new Vector3(xs[e],y0,0),new Vector3(xs[e],y1,0),new Vector3(xs[i],y1,0),Vector3.back,Vector3.right);
                    i=e;
                }
            }
        }
        static bool InHole(List<Rect> holes,float x,float y)
        {
            if(holes==null)return false;
            foreach(var h in holes)if(x>h.xMin&&x<h.xMax&&y>h.yMin&&y<h.yMax)return true;
            return false;
        }

        // Inner sides of a hole cut into the current face, from the surface (z=0) to depth.
        public void Reveal(in Finish f,Rect h,float depth)=>Reveal(f,h,depth,15);
        // sides: 1 left (x min), 2 right, 4 bottom (y min), 8 top.
        public void Reveal(in Finish f,Rect h,float depth,int sides)
        {
            if((sides&1)!=0)Quad(f,new Vector3(h.xMin,h.yMin,0),new Vector3(h.xMin,h.yMax,0),new Vector3(h.xMin,h.yMax,depth),new Vector3(h.xMin,h.yMin,depth),Vector3.right,Vector3.up);
            if((sides&2)!=0)Quad(f,new Vector3(h.xMax,h.yMin,0),new Vector3(h.xMax,h.yMax,0),new Vector3(h.xMax,h.yMax,depth),new Vector3(h.xMax,h.yMin,depth),Vector3.left,Vector3.up);
            if((sides&4)!=0)Quad(f,new Vector3(h.xMin,h.yMin,0),new Vector3(h.xMax,h.yMin,0),new Vector3(h.xMax,h.yMin,depth),new Vector3(h.xMin,h.yMin,depth),Vector3.up,Vector3.right);
            if((sides&8)!=0)Quad(f,new Vector3(h.xMin,h.yMax,0),new Vector3(h.xMax,h.yMax,0),new Vector3(h.xMax,h.yMax,depth),new Vector3(h.xMin,h.yMax,depth),Vector3.down,Vector3.right);
        }
        // Flat back of an opening at depth z, facing out.
        public void Back(in Finish f,Rect r,float z)=>Quad(f,new Vector3(r.xMin,r.yMin,z),new Vector3(r.xMax,r.yMin,z),new Vector3(r.xMax,r.yMax,z),new Vector3(r.xMin,r.yMax,z),Vector3.back,Vector3.right);
        // Glazing at depth z: uv1 holds metres across the pane, uv2 its size; the glass shader builds a room behind it.
        public void Glass(Rect r,float z)
        {
            if(dry)return;
            var f=new Finish(TileSlot.Glass);var size=r.size;NextFace();
            // Tangent along x; w makes cross(normal,tangent)*w point up the pane.
            var t=new Vector4(1,0,0,-1);
            int a=Vertex(new Vector3(r.xMin,r.yMin,z),Vector3.back,t,f,Vector2.zero,size);
            int b=Vertex(new Vector3(r.xMax,r.yMin,z),Vector3.back,t,f,new Vector2(size.x,0),size);
            int c=Vertex(new Vector3(r.xMax,r.yMax,z),Vector3.back,t,f,size,size);
            int d=Vertex(new Vector3(r.xMin,r.yMax,z),Vector3.back,t,f,new Vector2(0,size.y),size);
            Emit(TileSlot.Glass,a,b,c,d,Flip(new Vector3(r.xMin,r.yMin,z),new Vector3(r.xMax,r.yMin,z),new Vector3(r.xMax,r.yMax,z),new Vector3(r.xMin,r.yMax,z),Vector3.back));
            // uv0.x of a pane: its own random number, so each room is lit differently.
            var pane=new Vector2((panes++*0.618034f+seed*0.37f)%1f,seed);
            for(int i=a;i<=d;i++)codes[i]=pane;
        }
        int panes;

        // Convex hexahedron from 8 corners indexed by bits (1: +x side, 2: top, 4: +z side). Degenerate faces are skipped.
        public void Hexa(in Finish f,Vector3[] c)
        {
            if(dry)return;
            Vector3 centroid=Vector3.zero;foreach(var p in c)centroid+=p;centroid/=8;
            int[,] faces={{0,2,6,4},{1,3,7,5},{0,1,5,4},{2,3,7,6},{0,1,3,2},{4,5,7,6}};
            var planes=new List<(Vector3,Vector3)>(6);
            for(int i=0;i<6;i++)
            {
                var q=new[]{c[faces[i,0]],c[faces[i,1]],c[faces[i,2]],c[faces[i,3]]};
                Vector3 n=Newell(q);if(n.sqrMagnitude<1e-10f)continue;n.Normalize();
                if(Vector3.Dot(n,(q[0]+q[1]+q[2]+q[3])*0.25f-centroid)<0)n=-n;
                planes.Add((n,(q[0]+q[1]+q[2]+q[3])*0.25f));
                Quad(f,q[0],q[1],q[2],q[3],n,(q[1]-q[0]).sqrMagnitude>1e-8f?(q[1]-q[0]).normalized:(q[2]-q[1]).normalized);
            }
            SolidPlanes(planes);
        }

        // Surface of revolution; profile x = radius, y = distance along the axis, running from the bottom centre
        // outward and up so normals face out. smooth averages normals across profile points.
        public void Lathe(in Finish f,Vector3 origin,Vector3 axis,IList<Vector2> profile,int segments,bool smooth)=>
            Lathe(f,origin,axis,profile,segments,smooth,Vector3.Cross(axis,Mathf.Abs(axis.normalized.y)<0.95f?Vector3.up:Vector3.forward),Mathf.PI*2);
        // Part of a revolution: from start (perpendicular to axis) through arc radians towards cross(axis,start).
        public void Lathe(in Finish f,Vector3 origin,Vector3 axis,IList<Vector2> profile,int segments,bool smooth,Vector3 start,float arc)
        {
            if(dry)return;
            axis.Normalize();segments=Sides(segments,arc);
            Vector3 u=Vector3.ProjectOnPlane(start,axis).normalized,w=Vector3.Cross(axis,u);
            int n=profile.Count;var seg=new Vector2[n-1];
            for(int i=0;i<n-1;i++){Vector2 d=profile[i+1]-profile[i];seg[i]=new Vector2(d.y,-d.x).normalized;}
            for(int i=0;i<n-1;i++)
            {
                Vector2 p0=profile[i],p1=profile[i+1];
                if((p1-p0).sqrMagnitude<1e-12f||(p0.x<1e-6f&&p1.x<1e-6f))continue;
                Vector2 n0=seg[i],n1=seg[i];
                if(smooth){if(i>0)n0=(seg[i-1]+seg[i]).normalized;if(i<n-2)n1=(seg[i]+seg[i+1]).normalized;}
                for(int j=0;j<segments;j++)
                {
                    float a0=j*arc/segments,a1=(j+1)*arc/segments;
                    Vector3 r0=Mathf.Cos(a0)*u+Mathf.Sin(a0)*w,r1=Mathf.Cos(a1)*u+Mathf.Sin(a1)*w;
                    QuadSmooth(f,origin+axis*p0.y+r0*p0.x,origin+axis*p0.y+r1*p0.x,origin+axis*p1.y+r1*p1.x,origin+axis*p1.y+r0*p1.x,
                        r0*n0.x+axis*n0.y,r1*n0.x+axis*n0.y,r1*n1.x+axis*n1.y,r0*n1.x+axis*n1.y,Vector3.Cross(axis,r0));
                }
            }
        }

        public void Cylinder(in Finish f,Vector3 a,Vector3 b,float r,int segments=16,bool capA=true,bool capB=true,float chamfer=0)
        {
            float len=(b-a).magnitude;if(coarse>0)chamfer=0;float c=Mathf.Min(chamfer,Mathf.Min(r*0.5f,len*0.25f));
            var p=new List<Vector2>();
            if(capA){p.Add(new Vector2(0,0));if(c>0){p.Add(new Vector2(r-c,0));p.Add(new Vector2(r,c));}else p.Add(new Vector2(r,0));}
            else p.Add(new Vector2(r,0));
            if(capB){if(c>0){p.Add(new Vector2(r,len-c));p.Add(new Vector2(r-c,len));}else p.Add(new Vector2(r,len));p.Add(new Vector2(0,len));}
            else p.Add(new Vector2(r,len));
            Lathe(f,a,b-a,p,segments,false);
        }

        // Vessel standing on bottom along axis: flat base, straight body, elliptical dome on top.
        public void Tank(in Finish f,Vector3 bottom,Vector3 axis,float r,float body,float dome,int segments=24)
        {
            axis.Normalize();
            Lathe(f,bottom,axis,new[]{new Vector2(0,0),new Vector2(r,0),new Vector2(r,body)},segments,false);
            var cap=new List<Vector2>();
            int steps=Steps(6);
            for(int k=0;k<=steps;k++){float t=k/(float)steps*Mathf.PI*0.5f;cap.Add(new Vector2(r*Mathf.Cos(t),body+dome*Mathf.Sin(t)));}
            Lathe(f,bottom,axis,cap,segments,true);
        }
        public void Sphere(in Finish f,Vector3 center,float r,int segments=16)
        {
            var p=new List<Vector2>();int steps=Steps(8,3);
            for(int k=0;k<=steps;k++){float t=-Mathf.PI*0.5f+k/(float)steps*Mathf.PI;p.Add(new Vector2(r*Mathf.Cos(t),r*Mathf.Sin(t)));}
            p[0]=new Vector2(0,-r);p[steps]=new Vector2(0,r);
            Lathe(f,center,Vector3.up,p,segments,true);
        }
        // Part of a torus: tube radius r round a circle of radius R about axis through center, swept from start.
        public void Torus(in Finish f,Vector3 center,Vector3 axis,Vector3 start,float arc,float R,float r,int segments=20,int tube=12)
        {
            var p=new List<Vector2>();tube=Sides(tube);
            for(int k=0;k<=tube;k++){float t=k/(float)tube*Mathf.PI*2;p.Add(new Vector2(R+r*Mathf.Cos(t),r*Mathf.Sin(t)));}
            Lathe(f,center,axis,p,segments,true,start,arc);
        }
        // Parabolic dish opening along axis, with a thin rim.
        public void Dish(in Finish f,Vector3 center,Vector3 axis,float radius,float depth,int segments=24)
        {
            const float thickness=0.035f;
            var back=new List<Vector2>();var front=new List<Vector2>();
            int steps=Steps(6);
            for(int i=0;i<=steps;i++){float r=radius*i/steps;back.Add(new Vector2(r,depth*(r/radius)*(r/radius)));}
            for(int i=steps;i>=0;i--){float r=radius*i/steps;front.Add(new Vector2(r,depth*(r/radius)*(r/radius)+thickness));}
            Lathe(f,center,axis,back,segments,true);
            Lathe(f,center,axis,new[]{new Vector2(radius,depth),new Vector2(radius,depth+thickness)},segments,false);
            Lathe(f,center,axis,front,segments,true);
        }

        // Extrude a convex polygon (in the plane of pu, pv) along axis from a0 to a1. skip: bit per edge to leave out.
        public void Prism(in Finish f,IList<Vector2> poly,Vector3 origin,Vector3 pu,Vector3 pv,Vector3 axis,float a0,float a1,bool caps,int skip=0)
        {
            Vector2 centroid=Vector2.zero;foreach(var p in poly)centroid+=p;centroid/=poly.Count;
            for(int i=0;i<poly.Count;i++)
            {
                if((skip&(1<<i))!=0)continue;
                Vector2 p=poly[i],q=poly[(i+1)%poly.Count],e=q-p;
                Vector2 n2=new Vector2(e.y,-e.x).normalized;if(Vector2.Dot(n2,(p+q)*0.5f-centroid)<0)n2=-n2;
                Vector3 P0=origin+pu*p.x+pv*p.y,Q0=origin+pu*q.x+pv*q.y;
                Quad(f,P0+axis*a0,Q0+axis*a0,Q0+axis*a1,P0+axis*a1,pu*n2.x+pv*n2.y,axis);
            }
            if(!caps)return;
            Vector3 C=origin+pu*centroid.x+pv*centroid.y;
            for(int cap=0;cap<2;cap++)
            {
                float a=cap==0?a0:a1;
                BeginPolygon();
                for(int i=0;i<poly.Count;i++)
                {
                    Vector2 p=poly[i],q=poly[(i+1)%poly.Count];
                    Vector3 P0=origin+pu*p.x+pv*p.y,Q0=origin+pu*q.x+pv*q.y;
                    Triangle(f,C+axis*a,P0+axis*a,Q0+axis*a,cap==0?-axis:axis);
                }
                EndPolygon();
            }
        }
        // Flat ring between two polygons with matching corners, at a along axis, facing sign*axis.
        public void Annulus(in Finish f,IList<Vector2> inner,IList<Vector2> outer,Vector3 origin,Vector3 pu,Vector3 pv,Vector3 axis,float a,float sign)
        {
            for(int i=0;i<inner.Count;i++)
            {
                int j=(i+1)%inner.Count;
                Vector3 A=origin+pu*inner[i].x+pv*inner[i].y+axis*a,B=origin+pu*inner[j].x+pv*inner[j].y+axis*a;
                Vector3 C=origin+pu*outer[j].x+pv*outer[j].y+axis*a,D=origin+pu*outer[i].x+pv*outer[i].y+axis*a;
                Quad(f,A,B,C,D,axis*sign,pu);
            }
        }

        public Mesh ToMesh(string name){Flush();return Build(name,true);}
        // For each vertex of the far mesh from the last ToMeshes, the near vertex it is identical to (-1: its own).
        public int[] FarFromNear {get;private set;}
        // The coarse level the last ToMeshes built the Detail parts at (0: none).
        public int FarLevel {get;private set;}
        // The mesh with the armour carved, and a far level of detail: the armour plain, the Detail parts coarse (null if
        // nothing is carved and there is no Detail).
        // The middle level of the last ToMeshes (null if nothing was carved): the near mesh with the armour plain and its
        // relief traced instead (TileRelief.hlsl), family forms and kits at full detail. Its vertices before the armour
        // are the near mesh's own (MidFromNear).
        public Mesh Mid {get;private set;}
        public int[] MidFromNear {get;private set;}
        public (Mesh near,Mesh far) ToMeshes(string name)
        {
            FarFromNear=null;FarLevel=0;Mid=null;MidFromNear=null;
            if(jobs.Count==0&&details.Count==0)return (Build(name,true),null);
            var carved=jobs.ToArray();
            var farJob=new int[carved.Length];for(int k=0;k<farJob.Length;k++)farJob[k]=-1;
            var far=new TileMesh{retainWarpData=retainWarpData,seed=seed,overlay=overlay};var map=new List<int>(positions.Count);
            // Everything outside the Detail ranges (the station relief's among them), in order; then the armour plain;
            // then the relief's shells; then the Detail parts, coarse.
            var into=new int[positions.Count];int d=0;
            for(int v=0;v<positions.Count;v++)
            {
                while(d<details.Count&&v>=details[d].v1)d++;
                if(d<details.Count&&v>=details[d].v0){into[v]=-1;continue;}
                into[v]=far.positions.Count;map.Add(v);
                far.positions.Add(positions[v]);far.normals.Add(normals[v]);far.tangents.Add(tangents[v]);far.codes.Add(codes[v]);
                far.local.Add(local[v]);far.extent.Add(extent[v]);far.colors.Add(colors[v]);far.overlays.Add(overlays[v]);
                far.surfaceFrames.Add(surfaceFrames[v]);far.metricAnchors.Add(metricAnchors[v]);far.faces.Add(faces[v]);
            }
            for(int i=0;i<SlotCount;i++)
            {
                var src=indices[i];var dst=far.indices[i];d=0;
                for(int k=0;k<src.Count;k++)
                {
                    while(d<details.Count&&k>=details[d].i1[i])d++;
                    if(d<details.Count&&k>=details[d].i0[i]){k=details[d].i1[i]-1;continue;}
                    dst.Add(into[src[k]]);
                }
            }
            d=0;
            for(int k=0;k<jobs.Count;k++)
            {
                while(d<details.Count&&k>=details[d].j1)d++;
                if(d<details.Count&&k>=details[d].j0)continue;
                var j=jobs[k];
                far.surfaceFrame=j.frame;far.metricAnchor=j.anchor;farJob[k]=far.positions.Count;
                Vector4 t4=(j.B-j.A).normalized;t4.w=1;
                far.Emit(j.f.slot,far.Raw(j.A,j.N,t4,j.f),far.Raw(j.B,j.N,t4,j.f),far.Raw(j.C,j.N,t4,j.f),far.Raw(j.D,j.N,t4,j.f),Flip(j.A,j.B,j.C,j.D,j.N));
                for(int q=0;q<4;q++)map.Add(-1);
            }
            var shellData=new List<(int index,Vector4 uv1,Vector4 uv2,Vector4 uv6)>();
            EmitShells(far,map,shellData);
            if(farBuilds.Count>0)
            {
                var twin=Coarse(1);
                FarLevel=1;
                if(farBudget>0&&far.TriangleCount+twin.TriangleCount>farBudget){twin=Coarse(2);FarLevel=2;}
                int n=twin.positions.Count;twin.MoveTail(far,0,new int[SlotCount]);for(int k=0;k<n;k++)map.Add(-1);
            }
            // The middle level: every vertex so far, then the armour plain.
            TileMesh mid=null;var midJob=new int[carved.Length];
            if(carved.Length>0)
            {
                mid=new TileMesh{retainWarpData=retainWarpData,seed=seed,overlay=overlay};
                mid.positions.AddRange(positions);mid.normals.AddRange(normals);mid.tangents.AddRange(tangents);mid.codes.AddRange(codes);
                mid.local.AddRange(local);mid.extent.AddRange(extent);mid.colors.AddRange(colors);mid.overlays.AddRange(overlays);
                mid.surfaceFrames.AddRange(surfaceFrames);mid.metricAnchors.AddRange(metricAnchors);mid.faces.AddRange(faces);
                for(int i=0;i<SlotCount;i++)mid.indices[i].AddRange(indices[i]);
                mid.faceCount=faceCount;
                for(int k=0;k<carved.Length;k++)
                {
                    var j=carved[k];mid.surfaceFrame=j.frame;mid.metricAnchor=j.anchor;midJob[k]=mid.positions.Count;
                    Vector4 t4=(j.B-j.A).normalized;t4.w=1;mid.NextFace();
                    mid.Emit(j.f.slot,mid.Raw(j.A,j.N,t4,j.f),mid.Raw(j.B,j.N,t4,j.f),mid.Raw(j.C,j.N,t4,j.f),mid.Raw(j.D,j.N,t4,j.f),Flip(j.A,j.B,j.C,j.D,j.N));
                }
                var midMap=new int[mid.positions.Count];for(int v=0;v<midMap.Length;v++)midMap[v]=v<positions.Count?v:-1;
                MidFromNear=midMap;
            }
            farBuilds.Clear();details.Clear();shells.Clear();
            Flush();
            // Now the carving's outcome per face is known: the relief channel marks the faces the near mesh carved.
            var farMesh=far.Build(name+" far",false,shellData);FarFromNear=map.ToArray();
            var far6=Relief(far.positions.Count,carved,farJob);
            foreach(var x in shellData)far6[x.index]=x.uv6;
            farMesh.SetUVs(6,far6);
            if(mid!=null){Mid=mid.Build(name+" mid",true);Mid.SetUVs(6,Relief(mid.positions.Count,carved,midJob));}
            return (Build(name,true),farMesh);
        }
        // uv6 of a mesh with the armour plain (TileRelief.hlsl): on the four vertices of each face the near mesh carved,
        // its station position and its layout packed as ((key * 9 + ua * 3 + va) * 2 + calm), the way CarveFace lays it
        // out; zero elsewhere. start: each face's first vertex in that mesh (-1 if it is not there).
        Vector4[] Relief(int count,Job[] carved,int[] start)
        {
            var o=new Vector4[count];
            for(int k=0;k<carved.Length;k++)
            {
                if(start[k]<0||k>=outcomes.Length||outcomes[k]>CarvedCalm)continue;
                var j=carved[k];var N=j.N;
                int ax=0;for(int i=1;i<3;i++)if(Mathf.Abs(N[i])>Mathf.Abs(N[ax]))ax=i;
                int va=courseAxis<0?(ax+2)%3:courseAxis!=ax?courseAxis:courseSide,ua=3-ax-va;
                int key=ax*2+(N[ax]>0?1:0)+31*Mathf.RoundToInt(Vector3.Dot(N,j.A+stationOffset)*8);
                float code=((key*9+ua*3+va)*2)+(outcomes[k]==CarvedCalm?1:0);
                var corners=new[]{j.A,j.B,j.C,j.D};
                for(int q=0;q<4;q++){var s=corners[q]+stationOffset;o[start[k]+q]=new Vector4(s.x,s.y,s.z,code);}
            }
            return o;
        }
        // Moves the vertices from v0 on, and the triangles from i0 on in each slot (which use only those), to the end
        // of another mesh.
        void MoveTail(TileMesh to,int v0,int[] i0)
        {
            int n=positions.Count-v0,offset=to.positions.Count-v0;
            to.positions.AddRange(positions.GetRange(v0,n));to.normals.AddRange(normals.GetRange(v0,n));
            to.tangents.AddRange(tangents.GetRange(v0,n));to.codes.AddRange(codes.GetRange(v0,n));
            to.local.AddRange(local.GetRange(v0,n));to.extent.AddRange(extent.GetRange(v0,n));
            to.colors.AddRange(colors.GetRange(v0,n));to.overlays.AddRange(overlays.GetRange(v0,n));
            to.surfaceFrames.AddRange(surfaceFrames.GetRange(v0,n));to.metricAnchors.AddRange(metricAnchors.GetRange(v0,n));
            to.faces.AddRange(faces.GetRange(v0,n));
            for(int i=0;i<SlotCount;i++)for(int k=i0[i];k<indices[i].Count;k++)to.indices[i].Add(indices[i][k]+offset);
            Truncate(v0,i0);
        }
        void Truncate(int v0,int[] i0)
        {
            int n=positions.Count-v0;
            positions.RemoveRange(v0,n);normals.RemoveRange(v0,n);tangents.RemoveRange(v0,n);codes.RemoveRange(v0,n);
            local.RemoveRange(v0,n);extent.RemoveRange(v0,n);colors.RemoveRange(v0,n);overlays.RemoveRange(v0,n);
            surfaceFrames.RemoveRange(v0,n);
            metricAnchors.RemoveRange(v0,n);faces.RemoveRange(v0,n);
            for(int i=0;i<SlotCount;i++)indices[i].RemoveRange(i0[i],indices[i].Count-i0[i]);
        }
        // A vertex already in mesh space.
        int Raw(Vector3 p,Vector3 n,Vector4 t,in Finish f){var saved=matrix;matrix=Matrix4x4.identity;int i=Vertex(p,n,t,f);matrix=saved;return i;}
        // edges: work out the face edges for the close-up surface (TileMesh.Edges.cs); the far level goes without.
        // shells: uv1 and uv2 of the relief shells' vertices (EmitShells).
        Mesh Build(string name,bool edges,List<(int index,Vector4 uv1,Vector4 uv2,Vector4 uv6)> shells=null)
        {
            var mesh=new Mesh{name=name,indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetColors(colors);
            SetEdgeChannels(mesh,edges,shells);mesh.SetUVs(3,overlays);
            if(retainWarpData){mesh.SetUVs(4,surfaceFrames);mesh.SetUVs(5,metricAnchors);}
            mesh.subMeshCount=SlotCount;
            for(int i=0;i<SlotCount;i++)mesh.SetTriangles(indices[i],i,false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
