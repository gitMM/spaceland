using System;
using Unity.Mathematics;

namespace DeathStar
{
    // Integer cube coordinates identify shared vertices exactly, including the eight valence-three corners.
    public static class CubeSphere
    {
        public static void Frame(int face, out float3 normal, out float3 u, out float3 v)
        {
            switch (face)
            {
                case 0: normal = new float3(1,0,0); u = new float3(0,0,-1); v = new float3(0,1,0); break;
                case 1: normal = new float3(-1,0,0); u = new float3(0,0,1); v = new float3(0,1,0); break;
                case 2: normal = new float3(0,1,0); u = new float3(1,0,0); v = new float3(0,0,-1); break;
                case 3: normal = new float3(0,-1,0); u = new float3(1,0,0); v = new float3(0,0,1); break;
                case 4: normal = new float3(0,0,1); u = new float3(1,0,0); v = new float3(0,1,0); break;
                default: normal = new float3(0,0,-1); u = new float3(-1,0,0); v = new float3(0,1,0); break;
            }
        }

        public static int Count(int resolution) => 6 * resolution * resolution;
        public static int Id(int face, int x, int y, int n) => face * n * n + y * n + x;
        public static void Decode(int id, int n, out int face, out int x, out int y)
        { face = id / (n * n); x = id % n; y = id / n % n; }

        public static float3 Direction(int id, int n)
        {
            Decode(id, n, out int face, out int x, out int y);
            Frame(face, out var normal, out var u, out var v);
            return math.normalize(normal + u * ((2f*x+1)/n-1) + v * ((2f*y+1)/n-1));
        }

        public static int FromDirection(float3 p, int n)
        {
            float3 a = math.abs(p);
            int f = a.x >= a.y && a.x >= a.z ? (p.x >= 0 ? 0 : 1) :
                a.y >= a.z ? (p.y >= 0 ? 2 : 3) : (p.z >= 0 ? 4 : 5);
            Frame(f, out var normal, out var u, out var v);
            p /= math.dot(p, normal);
            int x = math.clamp((int)math.floor((math.dot(p,u)+1)*0.5f*n),0,n-1);
            int y = math.clamp((int)math.floor((math.dot(p,v)+1)*0.5f*n),0,n-1);
            return Id(f,x,y,n);
        }

        // S, E, N, W; tangent coordinates are transported into the adjoining face.
        public static int Neighbour(int id, int side, int n)
        {
            Decode(id,n,out int f,out int x,out int y);
            int nx=x+(side==1?1:side==3?-1:0), ny=y+(side==2?1:side==0?-1:0);
            if (nx>=0 && nx<n && ny>=0 && ny<n) return Id(f,nx,ny,n);
            Frame(f,out var normal,out var u,out var v);
            // Exact edge point, offset half a cell into the adjacent face (no cube-projection ambiguity).
            float3 p = normal*(1f-1f/n) + u*(side==1?1:side==3?-1:(2f*x+1)/n-1)
                + v*(side==2?1:side==0?-1:(2f*y+1)/n-1);
            return FromDirection(p,n);
        }

        public static int4 Neighbours(int id,int n) => new int4(Neighbour(id,0,n),Neighbour(id,1,n),Neighbour(id,2,n),Neighbour(id,3,n));

        public static int3 CornerKey(int id, int corner, int n)
        {
            Decode(id,n,out int f,out int x,out int y);
            Frame(f,out var normal,out var u,out var v);
            x += corner==1 || corner==2 ? 1 : 0;
            y += corner>=2 ? 1 : 0;
            return (int3)(normal*n + u*(2*x-n) + v*(2*y-n));
        }
    }
}
