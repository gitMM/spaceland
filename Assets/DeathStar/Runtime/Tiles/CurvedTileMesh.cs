using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Station metres -> shared eight-corner cells. The map is sampled by station position, never by mesh owner,
    // so a pad quarter, a relief fragment and a landmark crossing a cell boundary all evaluate the same point.
    public sealed class CurvedTileMesh
    {
        readonly BlockConstruction model;
        readonly Dictionary<Vector3Int,Vector3[]> cubes=new Dictionary<Vector3Int,Vector3[]>();
        readonly Dictionary<(Vector3Int,Vector4),Vector3[]> faceNormals=new Dictionary<(Vector3Int,Vector4),Vector3[]>();
        readonly Dictionary<(Vector4,Vector4),Matrix4x4> metricFrames=new Dictionary<(Vector4,Vector4),Matrix4x4>();
        // Consecutive vertices nearly always share a cell and frame: check the last key before hashing.
        Vector3Int lastCube=new Vector3Int(int.MinValue,0,0);Vector3[] lastCorners;
        (Vector3Int,Vector4) lastFace=(new Vector3Int(int.MinValue,0,0),default);Vector3[] lastNormals;
        public CurvedTileMesh(BlockConstruction model){this.model=model;}

        public Vector3 Map(Vector3 station)
        {Sample(station,out var p,out _,out _,out _);return p;}
        void Sample(Vector3 station,out Vector3 p,out Vector3 dx,out Vector3 dy,out Vector3 dz)
        {
            var grid=station/TileBuilder.Cell+Vector3.one*0.5f;
            var cell=Vector3Int.FloorToInt(grid);var uvw=grid-(Vector3)cell;
            Vector3[] corners;
            if(cell==lastCube)corners=lastCorners;
            else
            {
                if(!cubes.TryGetValue(cell,out corners))
                {
                    corners=new Vector3[8];
                    for(int i=0;i<8;i++)
                    {
                        var key=cell+BlockConstruction.CornerOffset(i);
                        corners[i]=(model.HasCorner(key)?model.Corner(key):model.Guide.Map(key))*TileBuilder.Cell;
                    }
                    cubes.Add(cell,corners);
                }
                lastCube=cell;lastCorners=corners;
            }
            var a=Vector3.LerpUnclamped(corners[0],corners[1],uvw.x);var b=Vector3.LerpUnclamped(corners[2],corners[3],uvw.x);
            var c=Vector3.LerpUnclamped(corners[4],corners[5],uvw.x);var d=Vector3.LerpUnclamped(corners[6],corners[7],uvw.x);
            p=Vector3.LerpUnclamped(Vector3.LerpUnclamped(a,b,uvw.y),Vector3.LerpUnclamped(c,d,uvw.y),uvw.z);
            dx=Vector3.LerpUnclamped(Vector3.LerpUnclamped(corners[1]-corners[0],corners[3]-corners[2],uvw.y),Vector3.LerpUnclamped(corners[5]-corners[4],corners[7]-corners[6],uvw.y),uvw.z)/5;
            dy=Vector3.LerpUnclamped(b-a,d-c,uvw.z)/5;
            dz=(Vector3.LerpUnclamped(c,d,uvw.y)-Vector3.LerpUnclamped(a,b,uvw.y))/5;
        }
        public Vector3 Position(Vector3 station,Vector4 frame)
        {Transform(station,frame,out var p,out _,out _,out _);return p;}
        public float Scale(Vector3 station,Vector3 direction)
        {Sample(station,out _,out var x,out var y,out var z);return (x*direction.x+y*direction.y+z*direction.z).magnitude;}
        // Metres a 5 m station face spans round a point along u and v, shrunk by the sine of the angle between them
        // so a sheared face counts as the square that fits inside it.
        public Vector2 Room(Vector3 station,Vector3 u,Vector3 v)
        {
            Sample(station,out _,out var x,out var y,out var z);
            Vector3 a=x*u.x+y*u.y+z*u.z,b=x*v.x+y*v.y+z*v.z;
            float la=a.magnitude,lb=b.magnitude,sin=Vector3.Cross(a,b).magnitude/Mathf.Max(1e-6f,la*lb);
            return new Vector2(la,lb)*(TileBuilder.Cell*sin);
        }
        // Inverse square root of the tangent metric. All quarters use the same assembly anchor, keeping the
        // 9 m deck and central hatch joined while removing local stretch and shear from their human-scale forms.
        public Matrix4x4 Metric(Vector4 anchor,Vector4 frame)
        {
            if(anchor.w<0.5f||((Vector3)frame).sqrMagnitude<0.5f)return Matrix4x4.identity;
            if(metricFrames.TryGetValue((anchor,frame),out var m))return m;
            int axis=frame.x!=0?0:frame.y!=0?1:2,u=(axis+1)%3,v=(axis+2)%3;
            Transform(anchor,frame,out _,out var dx,out var dy,out var dz);
            Vector3 Basis(int i)=>i==0?dx:i==1?dy:dz;
            var a=Basis(u);var b=Basis(v);float aa=a.sqrMagnitude,ab=Vector3.Dot(a,b),bb=b.sqrMagnitude;
            float det=Mathf.Sqrt(Mathf.Max(0.00001f,aa*bb-ab*ab)),k=Mathf.Sqrt(aa+bb+2*det);
            float inv=k/Mathf.Max(0.00001f,(aa+det)*(bb+det)-ab*ab);
            m=Matrix4x4.identity;m[u,u]=(bb+det)*inv;m[v,v]=(aa+det)*inv;m[u,v]=m[v,u]=-ab*inv;
            metricFrames.Add((anchor,frame),m);return m;
        }
        // Rigid placement of a fixed-size part: the anchor's curved position, and an orthonormal frame from the
        // metric-corrected tangent axes and the face normal there. Shared anchors (2 x 2 pads) share it exactly.
        public const float Foot=0.3f;
        readonly Dictionary<(Vector4,Vector4),Matrix4x4> rigidFrames=new Dictionary<(Vector4,Vector4),Matrix4x4>();
        public Matrix4x4 Rigid(Vector4 anchor,Vector4 frame)
        {
            if(rigidFrames.TryGetValue((anchor,frame),out var r))return r;
            var m=Metric(anchor,frame);
            Transform(anchor,frame,out var o,out var dx,out var dy,out var dz);
            Vector3 Column(int i)=>dx*m[0,i]+dy*m[1,i]+dz*m[2,i];
            int axis=frame.x!=0?0:frame.y!=0?1:2,ua=(axis+1)%3,va=(axis+2)%3;
            var n=Column(axis).normalized;
            var u=Column(ua);u=(u-n*Vector3.Dot(u,n)).normalized;
            var v=Vector3.Cross(n,u);if(Vector3.Dot(v,Column(va))<0)v=-v;
            r=Matrix4x4.identity;r.SetColumn(axis,n);r.SetColumn(ua,u);r.SetColumn(va,v);
            var t=o-r.MultiplyVector(anchor);r.SetColumn(3,new Vector4(t.x,t.y,t.z,1));
            rigidFrames.Add((anchor,frame),r);return r;
        }
        // How a fixed-size part came out (1 = true size): its widest span across its own up axis after bending, over
        // before. Up is left out because feet drop onto the curved surface by design. Mesh vertex range start..end.
        public float SizeRatio(Vector3[] before,Vector3[] after,List<Vector4> frames,List<Vector4> anchors,int start,int end)
        {
            Vector3 n=frames[start];var up=Rigid(anchors[start],frames[start]).MultiplyVector(n).normalized;
            Vector3 S(int i)=>before[i]-n*Vector3.Dot(before[i],n);
            Vector3 W(int i)=>after[i]-up*Vector3.Dot(after[i],up);
            int a=start,b=start;float best=-1;
            for(int i=start;i<end;i++){float d=(S(i)-S(start)).sqrMagnitude;if(d>best){best=d;b=i;}}
            best=-1;for(int i=start;i<end;i++){float d=(S(i)-S(b)).sqrMagnitude;if(d>best){best=d;a=i;}}
            return (W(a)-W(b)).magnitude/Mathf.Max(1e-4f,(S(a)-S(b)).magnitude);
        }
        void Transform(Vector3 station,Vector4 frame,out Vector3 p,out Vector3 dx,out Vector3 dy,out Vector3 dz)
        {
            Vector3 n=frame;
            if(n.sqrMagnitude<0.5f){Sample(station,out p,out dx,out dy,out dz);return;}
            float height=Vector3.Dot(station,n)-frame.w;
            var basePoint=station-n*height;
            int axis=n.x!=0?0:n.y!=0?1:2,ua=(axis+1)%3,va=(axis+2)%3;
            var grid=basePoint/5+Vector3.one*0.5f;var cell=Vector3Int.FloorToInt(grid);
            Vector3[] nn;
            if(cell==lastFace.Item1&&frame==lastFace.Item2)nn=lastNormals;
            else
            {
                if(!faceNormals.TryGetValue((cell,frame),out nn))
                {
                    nn=new Vector3[4];var u=Vector3.zero;u[ua]=0.02f;var v=Vector3.zero;v[va]=0.02f;
                    for(int k=0;k<4;k++)
                    {
                        var q=basePoint;q[ua]=(cell[ua]+(k&1)-0.5f)*5;q[va]=(cell[va]+(k>>1)-0.5f)*5;
                        nn[k]=Vector3.Cross(Map(q+u)-Map(q-u),Map(q+v)-Map(q-v)).normalized*n[axis];
                    }
                    faceNormals.Add((cell,frame),nn);
                }
                lastFace=(cell,frame);lastNormals=nn;
            }
            float s=grid[ua]-cell[ua],t=grid[va]-cell[va];
            var raw=Vector3.LerpUnclamped(Vector3.LerpUnclamped(nn[0],nn[1],s),Vector3.LerpUnclamped(nn[2],nn[3],s),t);
            float length=Mathf.Max(raw.magnitude,0.0001f);var normal=raw/length;
            var ns=Vector3.LerpUnclamped(nn[1]-nn[0],nn[3]-nn[2],t)/5;
            var nt=Vector3.LerpUnclamped(nn[2]-nn[0],nn[3]-nn[1],s)/5;
            ns=(ns-normal*Vector3.Dot(normal,ns))/length;nt=(nt-normal*Vector3.Dot(normal,nt))/length;
            Sample(basePoint,out p,out dx,out dy,out dz);p+=normal*height;
            // Analytic derivatives of the bilinear surface plus its smoothly interpolated metric relief normal.
            Vector3 Derivative(int a,Vector3 d)=>a==axis?normal*n[axis]:d+height*(a==ua?ns:nt);
            dx=Derivative(0,dx);dy=Derivative(1,dy);dz=Derivative(2,dz);
        }
        // A far mesh starts with the same vertices as its near mesh (TileMesh.SharedVertices): copy those already bent.
        public void Bend(Mesh mesh,Vector3Int cell,Mesh bentNear=null,int shared=0)
        {
            var vertices=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;
            var frames=new List<Vector4>();mesh.GetUVs(4,frames);
            var anchors=new List<Vector4>();mesh.GetUVs(5,anchors);
            var origin=TileStructure.Center(cell);
            int start=0;
            if(bentNear!=null&&shared>0)
            {
                start=Mathf.Min(shared,vertices.Length);
                System.Array.Copy(bentNear.vertices,vertices,start);System.Array.Copy(bentNear.normals,normals,start);
                System.Array.Copy(bentNear.tangents,tangents,start);
            }
            for(int i=start;i<vertices.Length;i++)
            {
                var p=origin+vertices[i];var f=frames.Count==vertices.Length?frames[i]:Vector4.zero;
                var anchor=anchors.Count==vertices.Length?anchors[i]:Vector4.zero;
                var metric=Metric(anchor,f);var pivot=(Vector3)anchor;
                if(anchor.w>=0.5f&&((Vector3)f).sqrMagnitude>=0.5f)
                {
                    // Fixed-size part: placed rigidly, so it is true size throughout. Its foot (the lowest Foot metres)
                    // drops along the normal onto the curved surface, which falls away from the anchor's tangent plane.
                    var rigid=Rigid(anchor,f);var placed=rigid.MultiplyPoint3x4(p);
                    float h=Vector3.Dot(p-pivot,(Vector3)f);
                    if(h<Foot)
                    {
                        Transform(pivot+metric.MultiplyVector(p-pivot),f,out var surface,out _,out _,out _);
                        var up=rigid.MultiplyVector((Vector3)f);
                        placed+=up*(Vector3.Dot(surface-placed,up)*(1-Mathf.Max(0,h)/Foot));
                    }
                    vertices[i]=placed;normals[i]=rigid.MultiplyVector(normals[i]).normalized;
                    var rt=tangents[i];var rw=rigid.MultiplyVector(rt);tangents[i]=new Vector4(rw.x,rw.y,rw.z,rt.w);
                    continue;
                }
                p=pivot+metric.MultiplyVector(p-pivot);
                // Transform normals with the cofactor (inverse transpose); preserve authored hard edges and glass UVs.
                Transform(p,f,out var bent,out var dx,out var dy,out var dz);
                var x=dx*metric.m00+dy*metric.m10+dz*metric.m20;
                var y=dx*metric.m01+dy*metric.m11+dz*metric.m21;
                var z=dx*metric.m02+dy*metric.m12+dz*metric.m22;dx=x;dy=y;dz=z;
                var n=normals[i];normals[i]=(Vector3.Cross(dy,dz)*n.x+Vector3.Cross(dz,dx)*n.y+Vector3.Cross(dx,dy)*n.z).normalized;
                var t=tangents[i];var wt=(dx*t.x+dy*t.y+dz*t.z).normalized;
                tangents[i]=new Vector4(wt.x,wt.y,wt.z,t.w);
                vertices[i]=bent;
            }
            mesh.vertices=vertices;mesh.normals=normals;mesh.tangents=tangents;mesh.RecalculateBounds();
            mesh.SetUVs(4,new List<Vector4>());mesh.SetUVs(5,new List<Vector4>());
        }
    }
}
