using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DeathStar
{
    // Pipes and cables between kit props (doc/close-up-detail-plan.md, milestone 5). Props on the same plate plane
    // 0.8-4 m apart are joined, each to at most one neighbour, by a hashed choice of station position, so the routes
    // are deterministic. A route leaves one socket along its way out, runs along the plate's axes at socket height and
    // turns into the other socket; corners are rounded (a pipe's bend radius, a cable's wider sweep). Both ends taking
    // pipes make a pipe with flanges at its ends; anything else a cable with clamps along it. The tubes are swept in a
    // Burst job with rotation-minimising frames. A route grows in with the lower of its two props (StationKit).
    public static class KitRoutes
    {
        public struct Placement{public Vector3 position,normal,along;public float scale;public int prop;}
        public sealed class Route{public Mesh mesh;public TileSlot slot;public Vector3 plane,normal;public int a,b;}

        const float Near=0.8f,Far=4f,Lead=0.08f;

        public static List<Route> Build(IReadOnlyList<Placement> placements,System.Func<Vector3,IEnumerable<int>> nearby)
        {
            var routes=new List<Route>();
            var paths=new List<(List<Vector3> points,float radius,bool pipe,int a,int b,Vector3 normal)>();
            var joined=new HashSet<int>();
            for(int i=0;i<placements.Count;i++)
            {
                var a=placements[i];var socketsA=KitProps.Sockets[a.prop];
                if(socketsA.Length==0||joined.Contains(i))continue;
                int best=-1;float bestD=float.MaxValue;
                foreach(int j in nearby(a.position))
                {
                    if(j==i||joined.Contains(j))continue;
                    var b=placements[j];if(KitProps.Sockets[b.prop].Length==0)continue;
                    if(Vector3.Dot(a.normal,b.normal)<0.99f||Mathf.Abs(Vector3.Dot(b.position-a.position,a.normal))>0.02f)continue;
                    float d=Vector3.Distance(a.position,b.position);
                    if(d<Near||d>Far||d>=bestD)continue;
                    if(Hash(a.position+b.position)>0.6f)continue;
                    best=j;bestD=d;
                }
                if(best<0)continue;
                joined.Add(i);joined.Add(best);
                var path=Path(a,placements[best],out float radius,out bool pipe);
                if(path!=null)paths.Add((path,radius,pipe,i,best,a.normal));
            }
            if(paths.Count==0)return routes;
            var meshes=Sweep(paths);
            for(int k=0;k<paths.Count;k++)
                routes.Add(new Route{mesh=meshes[k],slot=paths[k].pipe?TileSlot.Metal:TileSlot.Dark,plane=placements[paths[k].a].position,normal=paths[k].normal,a=paths[k].a,b=paths[k].b});
            return routes;
        }
        static float Hash(Vector3 p)
        {
            uint h=(uint)Mathf.RoundToInt(p.x*100)*73856093u^(uint)Mathf.RoundToInt(p.y*100)*19349663u^(uint)Mathf.RoundToInt(p.z*100)*83492791u;
            h^=h>>16;h*=0x7feb352dU;h^=h>>15;return (h&0xffff)/65535f;
        }

        static (Vector3 at,Vector3 way,bool pipe) World(in Placement p,(Vector3 at,Vector3 way,bool pipe) socket)
        {
            var rotation=Quaternion.LookRotation(Vector3.Cross(p.along,p.normal),p.normal);
            return (p.position+rotation*(socket.at*p.scale),rotation*socket.way,socket.pipe);
        }
        // The path from the socket of a nearest b to the socket of b nearest a, along a's plate axes.
        static List<Vector3> Path(in Placement a,in Placement b,out float radius,out bool pipe)
        {
            (Vector3 at,Vector3 way,bool pipe) sa=default,sb=default;float best=float.MaxValue;
            foreach(var s in KitProps.Sockets[a.prop]){var w=World(a,s);float d=Vector3.Distance(w.at,b.position);if(d<best){best=d;sa=w;}}
            best=float.MaxValue;
            foreach(var s in KitProps.Sockets[b.prop]){var w=World(b,s);float d=Vector3.Distance(w.at,sa.at);if(d<best){best=d;sb=w;}}
            pipe=sa.pipe&&sb.pipe;
            radius=pipe?0.035f*Mathf.Min(a.scale,b.scale):0.012f;
            float bend=pipe?0.09f:0.16f;
            var n=a.normal;var e1=a.along;var e2=Vector3.Cross(n,e1);
            Vector3 p1=sa.at+sa.way*Lead,p4=sb.at+sb.way*Lead;
            // keep clear of the plate: at least the tube's radius plus a little over it
            float lift=radius+0.012f;
            p1+=n*Mathf.Max(0,lift-Vector3.Dot(p1-a.position,n));p4+=n*Mathf.Max(0,lift-Vector3.Dot(p4-a.position,n));
            var span=p4-p1;
            // leave along the axis nearest the way out, so the first turn is the corner
            bool firstAlongE1=Mathf.Abs(Vector3.Dot(sa.way,e1))>=Mathf.Abs(Vector3.Dot(sa.way,e2));
            var axis=firstAlongE1?e1:e2;
            var corner=p1+axis*Vector3.Dot(span,axis);
            var corners=new List<Vector3>{sa.at,p1,corner,p4,sb.at};
            for(int i=corners.Count-2;i>0;i--)if((corners[i]-corners[i-1]).sqrMagnitude<1e-6f)corners.RemoveAt(i);
            return Fillet(corners,bend);
        }
        // A polyline with every inner corner rounded by an arc of radius r (smaller where the legs are short), sampled
        // every 15 degrees.
        static List<Vector3> Fillet(List<Vector3> c,float r)
        {
            var o=new List<Vector3>{c[0]};
            for(int i=1;i<c.Count-1;i++)
            {
                Vector3 a=c[i-1],p=c[i],b=c[i+1];
                Vector3 u=(a-p).normalized,v=(b-p).normalized;
                float angle=Vector3.Angle(u,v)*Mathf.Deg2Rad;
                if(angle<1e-3f||angle>Mathf.PI-1e-3f){o.Add(p);continue;}
                float t=Mathf.Min(r/Mathf.Tan(angle*0.5f),Mathf.Min((a-p).magnitude,(b-p).magnitude)*0.45f);
                Vector3 s=p+u*t,e=p+v*t;
                int steps=Mathf.Max(2,Mathf.CeilToInt((Mathf.PI-angle)/(15*Mathf.Deg2Rad)));
                for(int k=0;k<=steps;k++){float q=k/(float)steps;o.Add(Vector3.Lerp(Vector3.Lerp(s,p,q),Vector3.Lerp(p,e,q),q));}
            }
            o.Add(c[c.Count-1]);
            return o;
        }

        // One mesh per path: a tube of 10 sides (6 for a cable) along it, with flanges at a pipe's ends and clamps
        // every half metre along a cable, swept in one Burst job.
        static Mesh[] Sweep(List<(List<Vector3> points,float radius,bool pipe,int a,int b,Vector3 normal)> paths)
        {
            var points=new List<float3>();var rings=new List<float>();var ranges=new List<int4>();
            foreach(var (pts,radius,pipe,_,_,normal) in paths)
            {
                // Expand each path into rings: (point, radius). Flanges and clamps are short wider sections.
                int start=points.Count;float run=0;
                for(int i=0;i<pts.Count;i++)
                {
                    if(i>0)run+=(pts[i]-pts[i-1]).magnitude;
                    points.Add(pts[i]);rings.Add(radius);
                }
                // collars: a pipe's two flanges, a cable's clamps; each a ring pair a little wider, inserted later in the job
                ranges.Add(new int4(start,points.Count-start,pipe?10:6,pipe?1:0));
            }
            var job=new SweepJob{points=new NativeArray<float3>(points.ToArray(),Allocator.TempJob),radii=new NativeArray<float>(rings.ToArray(),Allocator.TempJob),
                ranges=new NativeArray<int4>(ranges.ToArray(),Allocator.TempJob),ups=new NativeArray<float3>(paths.Count,Allocator.TempJob),
                vertices=new NativeList<float3>(Allocator.TempJob),normals=new NativeList<float3>(Allocator.TempJob),indices=new NativeList<int>(Allocator.TempJob),
                spans=new NativeArray<int4>(paths.Count,Allocator.TempJob)};
            for(int i=0;i<paths.Count;i++)job.ups[i]=paths[i].normal;
            var meshes=new Mesh[paths.Count];
            try
            {
                job.Run();
                for(int i=0;i<paths.Count;i++)
                {
                    var span=job.spans[i];
                    var mesh=new Mesh{name=paths[i].pipe?"Kit pipe":"Kit cable",hideFlags=HideFlags.HideAndDontSave};
                    mesh.SetVertices(job.vertices.AsArray(),span.x,span.y);mesh.SetNormals(job.normals.AsArray(),span.x,span.y);
                    var tri=new int[span.w];for(int k=0;k<span.w;k++)tri[k]=job.indices[span.z+k]-span.x;
                    // TileSurface's channels: plain pattern, white tint, no wear, no edges
                    var colors=new Color[span.y];var codes=new Vector4[span.y];var none=new Vector4[span.y];
                    for(int k=0;k<span.y;k++){colors[k]=Color.white;codes[k]=new Vector4(0,0.5f,0,0);none[k]=new Vector4(TileMesh.FarEdge,TileMesh.FarEdge,TileMesh.FarEdge,TileMesh.FarEdge);}
                    mesh.SetColors(colors);mesh.SetUVs(0,codes);mesh.SetUVs(1,none);mesh.SetUVs(2,none);mesh.SetUVs(3,new Vector4[span.y]);
                    mesh.SetTriangles(tri,0);mesh.RecalculateBounds();
                    meshes[i]=mesh;
                }
            }
            finally{job.points.Dispose();job.radii.Dispose();job.ranges.Dispose();job.ups.Dispose();job.vertices.Dispose();job.normals.Dispose();job.indices.Dispose();job.spans.Dispose();}
            return meshes;
        }

        [BurstCompile]
        struct SweepJob:IJob
        {
            [ReadOnly] public NativeArray<float3> points;
            [ReadOnly] public NativeArray<float> radii;
            // per path: first point, point count, sides, 1 for a pipe
            [ReadOnly] public NativeArray<int4> ranges;
            [ReadOnly] public NativeArray<float3> ups;
            public NativeList<float3> vertices,normals;
            public NativeList<int> indices;
            // per path: first vertex, vertex count, first index, index count
            public NativeArray<int4> spans;

            public void Execute()
            {
                for(int r=0;r<ranges.Length;r++)
                {
                    int4 range=ranges[r];int v0=vertices.Length,i0=indices.Length;
                    int sides=range.z;bool pipe=range.w==1;
                    // the centre line, with collars: flanges near a pipe's ends, clamps every half metre along a cable
                    var line=new NativeList<float4>(Allocator.Temp);
                    float total=0;for(int k=1;k<range.y;k++)total+=math.distance(points[range.x+k],points[range.x+k-1]);
                    float run=0,nextClamp=0.25f;
                    for(int k=0;k<range.y;k++)
                    {
                        float3 p=points[range.x+k];float radius=radii[range.x+k];
                        if(k>0)
                        {
                            float3 q=points[range.x+k-1];float leg=math.distance(p,q);
                            float3 dir=(p-q)/math.max(leg,1e-6f);
                            // collars that fall in this leg
                            for(int c=0;c<4;c++)
                            {
                                float at=pipe?(c==0?0.05f:c==1?total-0.05f:-1):(c==0?nextClamp:-1);
                                if(at<run+0.02f||at>run+leg-0.02f)continue;
                                float w=pipe?0.018f:0.012f,wide=pipe?radius*1.45f:radius*1.8f;
                                float3 m=q+dir*(at-run);
                                line.Add(new float4(m-dir*w,radius));line.Add(new float4(m-dir*w,wide));
                                line.Add(new float4(m+dir*w,wide));line.Add(new float4(m+dir*w,radius));
                                if(!pipe)nextClamp+=0.5f;
                            }
                            run+=leg;
                        }
                        line.Add(new float4(p,radius));
                    }
                    // rotation-minimising frames (parallel transport), starting from the plate's normal
                    float3 up=ups[r];float3 tPrev=float3.zero,nPrev=float3.zero;
                    for(int k=0;k<line.Length;k++)
                    {
                        float3 p=line[k].xyz;
                        float3 t=k==0?line[1].xyz-p:k==line.Length-1?p-line[k-1].xyz:line[k+1].xyz-line[k-1].xyz;
                        t=math.normalizesafe(t,tPrev);
                        float3 n;
                        if(k==0)n=math.normalizesafe(up-t*math.dot(up,t),math.normalizesafe(math.cross(t,new float3(0,1,0.1f))));
                        else
                        {
                            float3 axis=math.cross(tPrev,t);float s=math.length(axis);
                            n=nPrev;
                            if(s>1e-6f){float angle=math.atan2(s,math.dot(tPrev,t));n=math.mul(quaternion.AxisAngle(axis/s,angle),nPrev);}
                            n=math.normalizesafe(n-t*math.dot(n,t),nPrev);
                        }
                        float3 b=math.cross(t,n);
                        for(int j=0;j<=sides;j++)
                        {
                            float a=j*2*math.PI/sides;float3 radial=n*math.cos(a)+b*math.sin(a);
                            vertices.Add(p+radial*line[k].w);normals.Add(radial);
                        }
                        if(k>0)
                        {
                            int baseA=v0+(k-1)*(sides+1),baseB=v0+k*(sides+1);
                            for(int j=0;j<sides;j++)
                            {
                                indices.Add(baseA+j);indices.Add(baseB+j);indices.Add(baseB+j+1);
                                indices.Add(baseA+j);indices.Add(baseB+j+1);indices.Add(baseA+j+1);
                            }
                        }
                        tPrev=t;nPrev=n;
                    }
                    spans[r]=new int4(v0,vertices.Length-v0,i0,indices.Length-i0);
                }
            }
        }
    }
}
