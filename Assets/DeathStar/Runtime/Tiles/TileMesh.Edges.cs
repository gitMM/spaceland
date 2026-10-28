using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;

namespace DeathStar
{
    // Face edges for the close-up surface (doc/close-up-detail-plan.md, layer 2). Each face (a quad, or a polygon
    // emitted as triangles) learns which of its edges are convex, concave or neither, from the faces that meet the
    // middle of each edge: a face turning away is convex (soft when it turns less than 50 degrees, like a chamfer), one
    // rising over it concave, a coplanar one (or a smooth neighbour) a surface carrying on. An edge nothing meets is a
    // tile seam or an open end, and gets nothing, so the cube grid stays invisible. Every vertex then carries its
    // distance to up to eight flagged edges of its face; distance to a line is affine across a plane, so it
    // interpolates exactly and DeathStar/Tile Surface can draw a rounded bevel, worn chips and dark creases at any
    // distance from an edge, without UVs. Vertex channels:
    //   uv0 = (pattern, seed, edge kinds: two bits per edge, part thickness in whole millimetres + a random number for the
    //         face in the fraction, from its station position so it holds across rebuilds)
    //   uv1, uv2 = distances in metres to edges 0-3 and 4-7 (FarEdge where unused). Glass keeps pane metres and size in xy.
    public sealed partial class TileMesh
    {
        public const int EdgeNone=0,EdgeConvex=1,EdgeSoft=2,EdgeConcave=3,MaxEdges=8;
        public const float FarEdge=1000f;
        const int SmoothFace=1<<30;
        readonly List<int> faces=new List<int>();
        int face,faceCount;bool polygon;
        // Each primitive is a face of its own; smooth faces (revolutions) carry on into smooth neighbours.
        void NextFace(bool smooth=false){if(!polygon)face=++faceCount|(smooth?SmoothFace:0);}
        // Triangles until EndPolygon make one face (a cap or a plate top), so their edges are the polygon's.
        public void BeginPolygon(){face=++faceCount;polygon=true;}
        public void EndPolygon()=>polygon=false;

        // Off: meshes carry no edges (the lab's before and after).
        public static bool BuildEdges=true;
        // Edges found so far by kind (none, convex, soft, concave), and the time spent finding them, for the lab.
        public static readonly int[] EdgeStats=new int[4];
        public static long EdgeTicks;

        // uv0-uv2 of the mesh being built; find: work out the edges (the far level goes without).
        void SetEdgeChannels(Mesh mesh,bool find)
        {
            int n=positions.Count;
            long t0=System.Diagnostics.Stopwatch.GetTimestamp();
            int count=0;for(int s=0;s<SlotCount;s++)if(s!=(int)TileSlot.Glass)count+=indices[s].Count/3;
            var tri=new int[count*3];var triSlot=new int[count];int k=0;
            for(int s=0;s<SlotCount;s++)if(s!=(int)TileSlot.Glass)
            {
                int c=indices[s].Count/3;indices[s].CopyTo(0,tri,k*3,c*3);
                for(int i=0;i<c;i++)triSlot[k+i]=s;k+=c;
            }
            var job=new EdgeJob
            {
                p=new NativeArray<Vector3>(positions.ToArray(),Allocator.TempJob).Reinterpret<float3>(),
                faceOf=new NativeArray<int>(faces.ToArray(),Allocator.TempJob),
                tris=new NativeArray<int>(tri,Allocator.TempJob).Reinterpret<int3>(sizeof(int)),
                triSlot=new NativeArray<int>(triSlot,Allocator.TempJob),
                slots=new NativeList<KitSlot>(Allocator.TempJob),
                codes=new NativeArray<Vector2>(codes.ToArray(),Allocator.TempJob).Reinterpret<float2>(),
                local=new NativeArray<Vector2>(local.ToArray(),Allocator.TempJob).Reinterpret<float2>(),
                extent=new NativeArray<Vector2>(extent.ToArray(),Allocator.TempJob).Reinterpret<float2>(),
                glass=new NativeArray<int>(indices[(int)TileSlot.Glass].ToArray(),Allocator.TempJob),
                find=find&&BuildEdges,station=stationOffset,
                uv0=new NativeArray<float4>(n,Allocator.TempJob,NativeArrayOptions.UninitializedMemory),
                uv1=new NativeArray<float4>(n,Allocator.TempJob,NativeArrayOptions.UninitializedMemory),
                uv2=new NativeArray<float4>(n,Allocator.TempJob,NativeArrayOptions.UninitializedMemory),
                stats=new NativeArray<int>(4,Allocator.TempJob),
            };
            try
            {
                job.Run();
                mesh.SetUVs(0,job.uv0);mesh.SetUVs(1,job.uv1);mesh.SetUVs(2,job.uv2);
                for(int i=0;i<4;i++)EdgeStats[i]+=job.stats[i];
                if(job.slots.Length>0)KitSlots.Add(mesh,new List<KitSlot>(job.slots.AsArray().ToArray()));
            }
            finally
            {
                job.p.Dispose();job.faceOf.Dispose();job.tris.Dispose();job.triSlot.Dispose();job.slots.Dispose();job.codes.Dispose();job.local.Dispose();job.extent.Dispose();job.glass.Dispose();
                job.uv0.Dispose();job.uv1.Dispose();job.uv2.Dispose();job.stats.Dispose();
            }
            if(job.find)EdgeTicks+=System.Diagnostics.Stopwatch.GetTimestamp()-t0;
        }

        // Kit slots (KitField): rectangular faces with four sharp convex edges, as TileSurface frames them for its fine
        // detail, 0.35-3 m a side. a, b, c: the vertices of a triangle of the face, bary: the face centre in it (both
        // survive the Foundry's bend); edge: two vertices along the face's first edge; size: (U, V) along the normals
        // of its first edge and of the edge square to that; packed: the face's uv0.w (thickness and random number).
        // Whether a slot holds a prop is decided at runtime (KitField), the same way TileSurface folds its darkening.
        public struct KitSlot{public int a,b,c,edgeA,edgeB;public Vector3 bary;public Vector2 size;public float packed;}
        public static readonly System.Runtime.CompilerServices.ConditionalWeakTable<Mesh,List<KitSlot>> KitSlots=
            new System.Runtime.CompilerServices.ConditionalWeakTable<Mesh,List<KitSlot>>();

        [BurstCompile]
        struct EdgeJob:IJob
        {
            [ReadOnly] public NativeArray<float3> p;
            [ReadOnly] public NativeArray<int> faceOf;
            [ReadOnly] public NativeArray<int3> tris;
            [ReadOnly] public NativeArray<int> triSlot;
            public NativeList<KitSlot> slots;
            [ReadOnly] public NativeArray<float2> codes,local,extent;
            [ReadOnly] public NativeArray<int> glass;
            public bool find;
            public float3 station;
            public NativeArray<float4> uv0,uv1,uv2;
            public NativeArray<int> stats;

            const float Touch=0.003f;
            // Three grid levels: each triangle goes to the finest on which it covers at most 48 cells (long thin plate
            // sides stay fine, big walls go coarse); a lookup visits the point's cell on every level.
            const int Levels=3;
            static float Cell(int level)=>level==0?0.25f:level==1?1f:4f;
            static int Floor(float v,int level)=>(int)math.floor(v/Cell(level));
            // Wraps every 512 cells: far apart cells only share candidates, which the geometric test rejects.
            static int Key(int3 c,int level)=>(c.x&511)|((c.y&511)<<9)|((c.z&511)<<18)|(level<<27);
            static int Hash(int key){uint h=(uint)key*2654435761u;return (int)(h^(h>>15));}
            struct Edge{public float3 p,inward;public int kind,neighbour,from,to;}

            // Built inside Execute (a job's own container fields must all exist before it runs).
            struct Scratch
            {
                public NativeArray<float3> tn;public NativeArray<float> plane,area;public NativeArray<int> tf;
                public NativeArray<int> slotKey,slotStart,slotCount,cellTris;public int mask;
            }

            public void Execute()
            {
                for(int v=0;v<uv0.Length;v++){uv0[v]=new float4(codes[v],0,0);uv1[v]=FarEdge;uv2[v]=FarEdge;}
                for(int g=0;g<glass.Length;g++){int v=glass[g];uv1[v]=new float4(local[v],0,0);uv2[v]=new float4(extent[v],0,0);}
                int count=tris.Length;
                if(!find||count==0)return;
                var w=new Scratch{tn=new NativeArray<float3>(count,Allocator.Temp),plane=new NativeArray<float>(count,Allocator.Temp),
                    area=new NativeArray<float>(count,Allocator.Temp),tf=new NativeArray<int>(count,Allocator.Temp)};
                var tn=w.tn;var plane=w.plane;var area=w.area;var tf=w.tf;
                var pairs=new NativeList<long>(count*4,Allocator.Temp);
                var faceKeys=new NativeList<long>(count,Allocator.Temp);
                for(int t=0;t<count;t++)
                {
                    int3 i=tris[t];float3 A=p[i.x],B=p[i.y],C=p[i.z];
                    float3 cr=math.cross(B-A,C-A);float area2=math.length(cr);
                    tf[t]=faceOf[i.x];
                    if(area2<1e-9f)continue;
                    float3 nn=cr/area2;tn[t]=nn;plane[t]=math.dot(nn,A);area[t]=area2*0.5f;
                    faceKeys.Add(((long)tf[t]<<32)|(uint)t);
                    float3 lo=math.min(A,math.min(B,C))-Touch,hi=math.max(A,math.max(B,C))+Touch;
                    int level=0;int3 c0,c1;
                    while(true)
                    {
                        c0=new int3(Floor(lo.x,level),Floor(lo.y,level),Floor(lo.z,level));c1=new int3(Floor(hi.x,level),Floor(hi.y,level),Floor(hi.z,level));
                        int3 span=c1-c0+1;
                        if(level==Levels-1||span.x*span.y*span.z<=48)break;
                        level++;
                    }
                    for(int x=c0.x;x<=c1.x;x++)for(int y=c0.y;y<=c1.y;y++)for(int z=c0.z;z<=c1.z;z++)
                        pairs.Add(((long)Key(new int3(x,y,z),level)<<32)|(uint)t);
                }
                pairs.Sort();
                var cellTris=w.cellTris=new NativeArray<int>(pairs.Length,Allocator.Temp);
                int unique=0;
                for(int i=0;i<pairs.Length;i++){cellTris[i]=(int)(pairs[i]&0xffffffffL);if(i==0||(pairs[i]>>32)!=(pairs[i-1]>>32))unique++;}
                int size=16;while(size<unique*2)size<<=1;int mask=w.mask=size-1;
                var slotKey=w.slotKey=new NativeArray<int>(size,Allocator.Temp);var slotStart=w.slotStart=new NativeArray<int>(size,Allocator.Temp);
                var slotCount=w.slotCount=new NativeArray<int>(size,Allocator.Temp);
                for(int i=0;i<size;i++)slotKey[i]=-1;
                for(int i=0;i<pairs.Length;)
                {
                    int key=(int)(pairs[i]>>32),j=i;while(j<pairs.Length&&(int)(pairs[j]>>32)==key)j++;
                    int h=Hash(key)&mask;while(slotKey[h]>=0)h=(h+1)&mask;
                    slotKey[h]=key;slotStart[h]=i;slotCount[h]=j-i;
                    i=j;
                }

                // Every face's edges and thickness first, then each vertex's channels (a part's thickness takes its
                // convex neighbours' into account, so both sides of an edge round off alike).
                faceKeys.Sort();
                int m=faceKeys.Length;
                var order=new NativeArray<int>(m,Allocator.Temp);
                for(int r=0;r<m;r++)order[r]=(int)(faceKeys[r]&0xffffffffL);
                var runs=new NativeList<int4>(Allocator.Temp);var thick=new NativeList<float>(Allocator.Temp);var runFace=new NativeList<int>(Allocator.Temp);
                var random=new NativeList<float>(Allocator.Temp);var rect=new NativeList<bool>(Allocator.Temp);var centres=new NativeList<float3>(Allocator.Temp);
                var edges=new NativeList<Edge>(Allocator.Temp);
                for(int s=0;s<m;)
                {
                    int faceId=(int)(faceKeys[s]>>32),e=s;while(e<m&&(int)(faceKeys[e]>>32)==faceId)e++;
                    float faceArea=0,perimeter=0;int first=edges.Length,boundary=0;float3 n=tn[order[s]],centre=0;
                    for(int r=s;r<e;r++)
                    {
                        int t=order[r];faceArea+=area[t];int3 tri=tris[t];centre+=(p[tri.x]+p[tri.y]+p[tri.z])*(area[t]/3);
                        for(int side=0;side<3;side++)
                        {
                            int i=side==0?tri.x:side==1?tri.y:tri.z,j=side==0?tri.y:side==1?tri.z:tri.x,k=side==0?tri.z:side==1?tri.x:tri.y;
                            if(e-s>1&&Shared(order,s,e,t,i,j))continue;
                            float3 a=p[i],b=p[j],along=b-a;float length=math.length(along);
                            if(length<1e-5f)continue;
                            perimeter+=length;
                            float3 outward=math.normalize(math.cross(along,n));if(math.dot(outward,p[k]-a)>0)outward=-outward;
                            int kind=Classify(ref w,(a+b)*0.5f,n,outward,faceId,out int neighbour);
                            stats[kind]++;
                            boundary++;
                            if(kind!=EdgeNone)edges.Add(new Edge{p=a,inward=-outward,kind=kind,neighbour=neighbour,from=i,to=j});
                        }
                    }
                    runs.Add(new int4(s,e,first,edges.Length));thick.Add(perimeter>0?2*faceArea/perimeter:0);runFace.Add(faceId);
                    rect.Add(e-s==2&&boundary==4&&edges.Length-first==4&&triSlot[order[s]]!=(int)TileSlot.Light);centres.Add(centre/math.max(faceArea,1e-6f));
                    uint h=math.hash(new int4((int3)math.round((station+centre/math.max(faceArea,1e-6f))*100),(int)math.round(math.dot(n,new float3(1,10,100))*10)));
                    random.Add((h&0xffff)/65537f);
                    s=e;
                }
                var chosen=new NativeArray<int>(MaxEdges,Allocator.Temp);
                for(int run=0;run<runs.Length;run++)
                {
                    int4 rr=runs[run];int e0=rr.z,e1=rr.w;
                    if(e1==e0)continue;
                    float part=thick[run];
                    for(int k=e0;k<e1;k++)
                    {
                        int other=Find(runFace,edges[k].neighbour);
                        if(other>=0)part=math.min(part,thick[other]);
                    }
                    float packed=math.round(math.min(part,60)*1000)+random[run];
                    if(rect[run])Slot(rr,e0,packed,centres[run],edges,order);
                    for(int r=rr.x;r<rr.y;r++)
                    {
                        int t=order[r];int3 tri=tris[t];float3 A=p[tri.x],B=p[tri.y],C=p[tri.z];
                        // Edges whose line the triangle lies wholly inside of (all of a convex face's; a concave face
                        // leaves out lines that cut through this triangle), the first eight.
                        int n=0;
                        for(int k=e0;k<e1&&n<MaxEdges;k++)
                        {
                            var ed=edges[k];
                            if(math.dot(A-ed.p,ed.inward)<-1e-3f||math.dot(B-ed.p,ed.inward)<-1e-3f||math.dot(C-ed.p,ed.inward)<-1e-3f)continue;
                            chosen[n++]=k;
                        }
                        if(n==0)continue;
                        int flags=0;for(int c=0;c<n;c++)flags|=edges[chosen[c]].kind<<(2*c);
                        for(int corner=0;corner<3;corner++)
                        {
                            int v=corner==0?tri.x:corner==1?tri.y:tri.z;
                            float4 a=FarEdge,b=FarEdge;
                            for(int c=0;c<n;c++)
                            {
                                var ed=edges[chosen[c]];float d=math.max(0,math.dot(p[v]-ed.p,ed.inward));
                                if(c<4)a[c]=d;else b[c-4]=d;
                            }
                            uv0[v]=new float4(codes[v],flags,packed);uv1[v]=a;uv2[v]=b;
                        }
                    }
                }
            }

            // A kit slot from a rectangular face whose four edges are all sharp and convex, framed as TileSurface frames it:
            // a its first edge, b the first square to it, a2 and b2 their opposites.
            void Slot(int4 run,int e0,float packed,float3 centre,NativeList<Edge> edges,NativeArray<int> order)
            {
                for(int k=e0;k<e0+4;k++)if(edges[k].kind!=EdgeConvex)return;
                var a=edges[e0];int b=-1,a2=-1,b2=-1;
                for(int k=e0+1;k<e0+4;k++)
                {
                    float c=math.dot(edges[k].inward,a.inward);
                    if(b<0&&math.abs(c)<0.3f)b=k;
                    if(a2<0&&c<-0.9f)a2=k;
                }
                if(b<0||a2<0)return;
                for(int k=e0;k<e0+4;k++)if(k!=b&&math.dot(edges[k].inward,edges[b].inward)<-0.9f){b2=k;break;}
                if(b2<0)return;
                float U=math.dot(edges[a2].p-a.p,a.inward),V=math.dot(edges[b2].p-edges[b].p,edges[b].inward);
                if(math.min(U,V)<0.35f||math.max(U,V)>3f)return;
                int3 tri=tris[order[run.x]];float3 A=p[tri.x],B=p[tri.y],C=p[tri.z];
                // barycentric of the centre in the face's first triangle (the centre lies on a rectangle's diagonal)
                float3 v0=B-A,v1=C-A,v2=centre-A;float d00=math.dot(v0,v0),d01=math.dot(v0,v1),d11=math.dot(v1,v1),d20=math.dot(v2,v0),d21=math.dot(v2,v1);
                float den=d00*d11-d01*d01;if(math.abs(den)<1e-12f)return;
                float w1=(d11*d20-d01*d21)/den,w2=(d00*d21-d01*d20)/den;
                slots.Add(new KitSlot{a=tri.x,b=tri.y,c=tri.z,edgeA=a.from,edgeB=a.to,bary=new float3(1-w1-w2,w1,w2),size=new float2(U,V),packed=packed});
            }

            // The run of a face id (runFace is sorted), or -1.
            static int Find(NativeList<int> runFace,int faceId)
            {
                if(faceId==0)return -1;
                int lo=0,hi=runFace.Length-1;
                while(lo<=hi){int mid=(lo+hi)>>1;int f=runFace[mid];if(f==faceId)return mid;if(f<faceId)lo=mid+1;else hi=mid-1;}
                return -1;
            }

            // Whether another triangle of the face run has the edge i-j (the same vertices, or the same positions).
            bool Shared(NativeArray<int> order,int s,int e,int self,int i,int j)
            {
                float3 a=p[i],b=p[j];
                for(int r=s;r<e;r++)
                {
                    int t=order[r];if(t==self)continue;
                    int3 tri=tris[t];
                    if(math.any(tri==i)&&math.any(tri==j))return true;
                    if(Same(p[tri.x],p[tri.y],a,b)||Same(p[tri.y],p[tri.z],a,b)||Same(p[tri.z],p[tri.x],a,b))return true;
                }
                return false;
            }
            static bool Same(float3 p,float3 q,float3 a,float3 b)=>
                (math.lengthsq(p-a)<1e-8f&&math.lengthsq(q-b)<1e-8f)||(math.lengthsq(p-b)<1e-8f&&math.lengthsq(q-a)<1e-8f);

            int Classify(ref Scratch w,float3 mid,float3 n,float3 outward,int faceId,out int neighbour)
            {
                var tf=w.tf;var tn=w.tn;var plane=w.plane;var slotKey=w.slotKey;var cellTris=w.cellTris;int mask=w.mask;
                neighbour=0;
                bool concave=false;float turn=2;
                for(int level=0;level<Levels;level++)
                {
                    int key=Key(new int3(Floor(mid.x,level),Floor(mid.y,level),Floor(mid.z,level)),level);
                    int h=Hash(key)&mask;
                    while(slotKey[h]>=0&&slotKey[h]!=key)h=(h+1)&mask;
                    if(slotKey[h]<0)continue;
                    int start=w.slotStart[h],end=start+w.slotCount[h];
                    for(int i=start;i<end;i++)
                    {
                        int t=cellTris[i];
                        if(tf[t]==faceId)continue;
                        float3 n2=tn[t];
                        float d=math.dot(n2,mid)-plane[t];
                        if(d>Touch||d<-Touch)continue;
                        int3 tri=tris[t];
                        if(math.lengthsq(Closest(mid,p[tri.x],p[tri.y],p[tri.z])-mid)>Touch*Touch)continue;
                        float dn=math.dot(n2,n);
                        if(dn>0.985f||(dn>0.7f&&(faceId&SmoothFace)!=0&&(tf[t]&SmoothFace)!=0))return EdgeNone;   // the surface carries on
                        if(dn<-0.985f)continue;   // back to back
                        float s=math.dot(n2,outward);
                        if(s>0.1f){if(dn<turn){turn=dn;neighbour=tf[t];}}
                        else if(s<-0.1f)concave=true;
                    }
                }
                if(concave){neighbour=0;return EdgeConcave;}
                if(turn>1)return EdgeNone;
                return turn<0.643f?EdgeConvex:EdgeSoft;   // cos 50 degrees
            }

            // Closest point on triangle abc to q (Ericson, Real-Time Collision Detection 5.1.5).
            static float3 Closest(float3 q,float3 a,float3 b,float3 c)
            {
                float3 ab=b-a,ac=c-a,aq=q-a;
                float d1=math.dot(ab,aq),d2=math.dot(ac,aq);if(d1<=0&&d2<=0)return a;
                float3 bq=q-b;float d3=math.dot(ab,bq),d4=math.dot(ac,bq);if(d3>=0&&d4<=d3)return b;
                float vc=d1*d4-d3*d2;if(vc<=0&&d1>=0&&d3<=0)return a+ab*(d1/(d1-d3));
                float3 cq=q-c;float d5=math.dot(ab,cq),d6=math.dot(ac,cq);if(d6>=0&&d5<=d6)return c;
                float vb=d5*d2-d1*d6;if(vb<=0&&d2>=0&&d6<=0)return a+ac*(d2/(d2-d6));
                float va=d3*d6-d5*d4;if(va<=0&&d4-d3>=0&&d5-d6>=0)return b+(c-b)*((d4-d3)/((d4-d3)+(d5-d6)));
                float den=1/(va+vb+vc);return a+ab*(vb*den)+ac*(vc*den);
            }
        }
    }
}
