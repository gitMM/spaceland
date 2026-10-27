using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    // Material slots of a settlement tile: one submesh and one TileLibrary material each.
    public enum TileSlot { Paint, Trim, Dark, Metal, Recess, Glass, Light, Accent }

    // Surface treatment read by DeathStar/Tile Surface from uv0.x.
    public enum TilePattern { Plain, Panels, Hazard, Deck, Grille, Markings, Armour }

    public static class TileFace
    {
        public const int Front=0, Right=1, Back=2, Left=3, Top=4, Bottom=5;
        public const int AllExposed=(1<<Front)|(1<<Right)|(1<<Back)|(1<<Left)|(1<<Top);
        static readonly Vector3Int[] Directions={new Vector3Int(0,0,-1),new Vector3Int(1,0,0),new Vector3Int(0,0,1),new Vector3Int(-1,0,0),Vector3Int.up,Vector3Int.down};
        // Direction of a tile's local face after turning the tile by quarter turns about +Y (Quaternion.Euler(0,90*turns,0)).
        public static Vector3Int Direction(int face,int turns)
        {
            var d=Directions[face];
            for(int i=0;i<((turns%4)+4)%4;i++)d=new Vector3Int(d.z,d.y,-d.x);
            return d;
        }
    }

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
    // frames (PushFace) have x to the right and y up as seen from outside, and z pointing into the face.
    public sealed partial class TileMesh
    {
        public const int SlotCount=8;
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
        public FixedPart BeginFixed(string name,Vector3 anchor)=>new FixedPart(this,name,anchor);
        public readonly struct FixedPart:System.IDisposable
        {
            readonly TileMesh m;readonly string name;readonly int start;readonly Vector4 saved;
            public FixedPart(TileMesh m,string name,Vector3 at)
            {this.m=m;this.name=name;start=m.positions.Count;saved=m.metricAnchor;m.metricAnchor=new Vector4(at.x,at.y,at.z,1);}
            public void Dispose(){m.metricAnchor=saved;if(m.positions.Count>start)m.fixedParts.Add((name,start,m.positions.Count));}
        }

        // Frame on one face of the box min..max. Side faces put y=0 at the tile's ground so heights read the same on
        // every face; the top face has y towards the back (+Z).
        public void PushFace(int face,Vector3 min,Vector3 max)
        {
            Vector3 c=(min+max)*0.5f;
            switch(face)
            {
                case TileFace.Front:Push(new Vector3(c.x,0,min.z),Quaternion.identity);break;
                case TileFace.Right:Push(new Vector3(max.x,0,c.z),Quaternion.Euler(0,-90,0));break;
                case TileFace.Back:Push(new Vector3(c.x,0,max.z),Quaternion.Euler(0,180,0));break;
                case TileFace.Left:Push(new Vector3(min.x,0,c.z),Quaternion.Euler(0,90,0));break;
                case TileFace.Top:Push(new Vector3(c.x,max.y,c.z),Quaternion.Euler(90,0,0));break;
                default:Push(new Vector3(c.x,min.y,c.z),Quaternion.Euler(-90,0,0));break;
            }
        }
        // Face rectangle of the box in that face's frame.
        public static Rect FaceRect(int face,Vector3 min,Vector3 max)
        {
            Vector3 s=max-min;
            if(face==TileFace.Top||face==TileFace.Bottom)return new Rect(-s.x*0.5f,-s.z*0.5f,s.x,s.z);
            float w=face==TileFace.Front||face==TileFace.Back?s.x:s.z;
            return new Rect(-w*0.5f,min.y,w,s.y);
        }

        int Vertex(Vector3 p,Vector3 n,Vector4 t,in Finish f,Vector2 g=default,Vector2 size=default)
        {
            positions.Add(matrix.MultiplyPoint3x4(p));normals.Add(matrix.MultiplyVector(n).normalized);
            Vector3 tw=matrix.MultiplyVector(t).normalized;tangents.Add(new Vector4(tw.x,tw.y,tw.z,t.w==0?1:t.w));
            codes.Add(new Vector2((int)f.pattern,seed));local.Add(g);extent.Add(size);
            colors.Add(new Color(f.tint.r,f.tint.g,f.tint.b,f.wear));overlays.Add(overlay);
            surfaceFrames.Add(surfaceFrame);
            metricAnchors.Add(metricAnchor);
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
            if(Hidden(a,b,c,d,n))return;
            if(f.pattern==TilePattern.Armour&&Carve(f,a,b,c,d,n))return;
            Vector4 t4=t;t4.w=1;
            Emit(f.slot,Vertex(a,n,t4,f),Vertex(b,n,t4,f),Vertex(c,n,t4,f),Vertex(d,n,t4,f),Flip(a,b,c,d,n));
        }
        public void QuadSmooth(in Finish f,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Vector3 na,Vector3 nb,Vector3 nc,Vector3 nd,Vector3 t)
        {
            if(Hidden(a,b,c,d,na+nb+nc+nd))return;
            Vector4 t4=t;t4.w=1;
            Emit(f.slot,Vertex(a,na,t4,f),Vertex(b,nb,t4,f),Vertex(c,nc,t4,f),Vertex(d,nd,t4,f),Flip(a,b,c,d,na+nb+nc+nd));
        }
        public void Triangle(in Finish f,Vector3 a,Vector3 b,Vector3 c,Vector3 n)
        {
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

        public void Box(in Finish f,Vector3 center,Vector3 size,float chamfer=0)
        {
            Vector3 h=size*0.5f;
            SolidBox(center,h);
            ChamferedBox(f,center,h,Mathf.Min(chamfer,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*0.5f),true);
        }
        // Chamfered edges catch grazing light; faces=false leaves the six main faces to the caller (shells cut them).
        void ChamferedBox(in Finish f,Vector3 o,Vector3 h,float c,bool faces)
        {
            for(int a=0;a<3;a++)
            {
                int b=(a+1)%3,k=(a+2)%3;
                for(int s=-1;s<=1;s+=2)
                {
                    if(faces)
                    {
                        float hb=h[b]-c,hk=h[k]-c,ha=s*h[a];
                        Quad(f,o+P(a,ha,b,-hb,k,-hk),o+P(a,ha,b,hb,k,-hk),o+P(a,ha,b,hb,k,hk),o+P(a,ha,b,-hb,k,hk),Axis(a)*s,Axis(b));
                    }
                    if(c<=0)continue;
                    for(int s2=-1;s2<=1;s2+=2)
                    {
                        float hk=h[k]-c;
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

        // A chamfered box whose side and top walls have rectangular holes (in each face's frame). No bottom.
        public void Shell(in Finish f,Vector3 min,Vector3 max,float chamfer,List<Rect>[] holes)
        {
            Vector3 h=(max-min)*0.5f;
            float c=Mathf.Min(chamfer,Mathf.Min(h.x,Mathf.Min(h.y,h.z))*0.5f);
            ChamferedBox(f,(min+max)*0.5f,h,c,false);
            for(int face=0;face<5;face++)
            {
                Rect r=FaceRect(face,min,max);
                r=Rect.MinMaxRect(r.xMin+c,r.yMin+c,r.xMax-c,r.yMax-c);
                PushFace(face,min,max);Wall(f,r,holes?[face]);Pop();
            }
        }

        // Planar wall at z=0 facing out (-z), minus rectangular holes.
        public void Wall(in Finish f,Rect r,List<Rect> holes)
        {
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
            var f=new Finish(TileSlot.Glass);var size=r.size;
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
            axis.Normalize();
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
            float len=(b-a).magnitude;float c=Mathf.Min(chamfer,Mathf.Min(r*0.5f,len*0.25f));
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
            for(int k=0;k<=6;k++){float t=k/6f*Mathf.PI*0.5f;cap.Add(new Vector2(r*Mathf.Cos(t),body+dome*Mathf.Sin(t)));}
            Lathe(f,bottom,axis,cap,segments,true);
        }
        // Horizontal vessel: domes at both ends.
        public void Capsule(in Finish f,Vector3 a,Vector3 b,float r,float dome,int segments=24)
        {
            Vector3 axis=(b-a).normalized;float len=(b-a).magnitude;
            var p=new List<Vector2>();
            for(int k=0;k<=6;k++){float t=-Mathf.PI*0.5f+k/6f*Mathf.PI*0.5f;p.Add(new Vector2(r*Mathf.Cos(t),dome+dome*Mathf.Sin(t)));}
            Lathe(f,a-axis*dome,axis,p,segments,true);
            Lathe(f,a,axis,new[]{new Vector2(r,0),new Vector2(r,len)},segments,false);
            var q=new List<Vector2>();
            for(int k=0;k<=6;k++){float t=k/6f*Mathf.PI*0.5f;q.Add(new Vector2(r*Mathf.Cos(t),len+dome*Mathf.Sin(t)));}
            Lathe(f,a,axis,q,segments,true);
        }
        public void Sphere(in Finish f,Vector3 center,float r,int segments=16)
        {
            var p=new List<Vector2>();
            for(int k=0;k<=8;k++){float t=-Mathf.PI*0.5f+k/8f*Mathf.PI;p.Add(new Vector2(r*Mathf.Cos(t),r*Mathf.Sin(t)));}
            p[0]=new Vector2(0,-r);p[8]=new Vector2(0,r);
            Lathe(f,center,Vector3.up,p,segments,true);
        }
        // Part of a torus: tube radius r round a circle of radius R about axis through center, swept from start.
        public void Torus(in Finish f,Vector3 center,Vector3 axis,Vector3 start,float arc,float R,float r,int segments=20,int tube=12)
        {
            var p=new List<Vector2>();
            for(int k=0;k<=tube;k++){float t=k/(float)tube*Mathf.PI*2;p.Add(new Vector2(R+r*Mathf.Cos(t),r*Mathf.Sin(t)));}
            Lathe(f,center,axis,p,segments,true,start,arc);
        }
        // Parabolic dish opening along axis, with a thin rim.
        public void Dish(in Finish f,Vector3 center,Vector3 axis,float radius,float depth,int segments=24)
        {
            const float thickness=0.035f;
            var back=new List<Vector2>();var front=new List<Vector2>();
            for(int i=0;i<=6;i++){float r=radius*i/6f;back.Add(new Vector2(r,depth*(r/radius)*(r/radius)));}
            for(int i=6;i>=0;i--){float r=radius*i/6f;front.Add(new Vector2(r,depth*(r/radius)*(r/radius)+thickness));}
            Lathe(f,center,axis,back,segments,true);
            Lathe(f,center,axis,new[]{new Vector2(radius,depth),new Vector2(radius,depth+thickness)},segments,false);
            Lathe(f,center,axis,front,segments,true);
        }

        // Pipe run through points, with fittings at the bends and flanges at the ends.
        public void Pipe(in Finish f,float r,params Vector3[] points)
        {
            for(int i=0;i<points.Length-1;i++)Cylinder(f,points[i],points[i+1],r,10,false,false);
            for(int i=1;i<points.Length-1;i++)Box(f,points[i],Vector3.one*r*2.5f,r*0.4f);
            for(int e=0;e<2;e++)
            {
                Vector3 end=e==0?points[0]:points[points.Length-1],next=e==0?points[1]:points[points.Length-2];
                Vector3 d=(next-end).normalized;
                Cylinder(f,end,end+d*r*0.7f,r*1.45f,10,true,true,r*0.15f);
            }
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
            for(int i=0;i<poly.Count;i++)
            {
                Vector2 p=poly[i],q=poly[(i+1)%poly.Count];
                Vector3 P0=origin+pu*p.x+pv*p.y,Q0=origin+pu*q.x+pv*q.y;
                Triangle(f,C+axis*a0,P0+axis*a0,Q0+axis*a0,-axis);
                Triangle(f,C+axis*a1,P0+axis*a1,Q0+axis*a1,axis);
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

        public Mesh ToMesh(string name){Flush();return Build(name);}
        // Vertices at the start of both meshes from the last ToMeshes: identical in near and far.
        public int SharedVertices {get;private set;}
        // The mesh with the armour carved, and a far level of detail with the armour plain (null if nothing is carved).
        public (Mesh near,Mesh far) ToMeshes(string name)
        {
            SharedVertices=0;
            if(jobs.Count==0)return (Build(name),null);
            int mark=positions.Count;var marks=new int[SlotCount];for(int i=0;i<SlotCount;i++)marks[i]=indices[i].Count;
            SharedVertices=mark;
            foreach(var j in jobs)
            {
                surfaceFrame=j.frame;
                metricAnchor=j.anchor;
                Vector4 t4=(j.B-j.A).normalized;t4.w=1;
                Emit(j.f.slot,Raw(j.A,j.N,t4,j.f),Raw(j.B,j.N,t4,j.f),Raw(j.C,j.N,t4,j.f),Raw(j.D,j.N,t4,j.f),Flip(j.A,j.B,j.C,j.D,j.N));
            }
            var far=Build(name+" far");
            int n=positions.Count-mark;
            positions.RemoveRange(mark,n);normals.RemoveRange(mark,n);tangents.RemoveRange(mark,n);codes.RemoveRange(mark,n);
            local.RemoveRange(mark,n);extent.RemoveRange(mark,n);colors.RemoveRange(mark,n);overlays.RemoveRange(mark,n);
            surfaceFrames.RemoveRange(mark,n);
            metricAnchors.RemoveRange(mark,n);
            for(int i=0;i<SlotCount;i++)indices[i].RemoveRange(marks[i],indices[i].Count-marks[i]);
            Flush();
            return (Build(name),far);
        }
        // A vertex already in mesh space.
        int Raw(Vector3 p,Vector3 n,Vector4 t,in Finish f){var saved=matrix;matrix=Matrix4x4.identity;int i=Vertex(p,n,t,f);matrix=saved;return i;}
        Mesh Build(string name)
        {
            var mesh=new Mesh{name=name,indexFormat=positions.Count>65535?IndexFormat.UInt32:IndexFormat.UInt16};
            mesh.SetVertices(positions);mesh.SetNormals(normals);mesh.SetTangents(tangents);mesh.SetColors(colors);
            mesh.SetUVs(0,codes);mesh.SetUVs(1,local);mesh.SetUVs(2,extent);mesh.SetUVs(3,overlays);
            if(retainWarpData){mesh.SetUVs(4,surfaceFrames);mesh.SetUVs(5,metricAnchors);}
            mesh.subMeshCount=SlotCount;
            for(int i=0;i<SlotCount;i++)mesh.SetTriangles(indices[i],i,false);
            mesh.RecalculateBounds();
            return mesh;
        }
    }
}
