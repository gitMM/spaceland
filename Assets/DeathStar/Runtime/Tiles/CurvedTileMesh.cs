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
        public CurvedTileMesh(BlockConstruction model){this.model=model;}

        public Vector3 Map(Vector3 station)
        {Sample(station,out var p,out _,out _,out _);return p;}
        void Sample(Vector3 station,out Vector3 p,out Vector3 dx,out Vector3 dy,out Vector3 dz)
        {
            var grid=station/TileBuilder.Cell+Vector3.one*0.5f;
            var cell=Vector3Int.FloorToInt(grid);var uvw=grid-(Vector3)cell;
            if(!cubes.TryGetValue(cell,out var corners))
            {
                corners=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    var key=cell+BlockConstruction.CornerOffset(i);
                    corners[i]=(model.HasCorner(key)?model.Corner(key):model.Guide.Map(key))*TileBuilder.Cell;
                }
                cubes.Add(cell,corners);
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
        void Transform(Vector3 station,Vector4 frame,out Vector3 p,out Vector3 dx,out Vector3 dy,out Vector3 dz)
        {
            Vector3 n=frame;
            if(n.sqrMagnitude<0.5f){Sample(station,out p,out dx,out dy,out dz);return;}
            float height=Vector3.Dot(station,n)-frame.w;
            var basePoint=station-n*height;
            int axis=n.x!=0?0:n.y!=0?1:2,ua=(axis+1)%3,va=(axis+2)%3;
            var grid=basePoint/5+Vector3.one*0.5f;var cell=Vector3Int.FloorToInt(grid);
            if(!faceNormals.TryGetValue((cell,frame),out var nn))
            {
                nn=new Vector3[4];var u=Vector3.zero;u[ua]=0.02f;var v=Vector3.zero;v[va]=0.02f;
                for(int k=0;k<4;k++)
                {
                    var q=basePoint;q[ua]=(cell[ua]+(k&1)-0.5f)*5;q[va]=(cell[va]+(k>>1)-0.5f)*5;
                    nn[k]=Vector3.Cross(Map(q+u)-Map(q-u),Map(q+v)-Map(q-v)).normalized*n[axis];
                }
                faceNormals.Add((cell,frame),nn);
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
        public void Bend(Mesh mesh,Vector3Int cell)
        {
            var vertices=mesh.vertices;var normals=mesh.normals;var tangents=mesh.tangents;
            var frames=new List<Vector4>();mesh.GetUVs(4,frames);
            var anchors=new List<Vector4>();mesh.GetUVs(5,anchors);
            var origin=TileStructure.Center(cell);
            for(int i=0;i<vertices.Length;i++)
            {
                var p=origin+vertices[i];var f=frames.Count==vertices.Length?frames[i]:Vector4.zero;
                var anchor=anchors.Count==vertices.Length?anchors[i]:Vector4.zero;
                var metric=Metric(anchor,f);var pivot=(Vector3)anchor;
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
