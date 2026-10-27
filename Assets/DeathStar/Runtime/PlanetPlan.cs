using System.Runtime.InteropServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace DeathStar
{
    public enum SurfaceKind : uint { Armor, Structure, Trench, Machinery, Array }
    public enum PaintTool { Armor, Raise, Lower, Trench, Machinery, Erase }

    [StructLayout(LayoutKind.Sequential)]
    public struct CellPlan // 32 bytes, mirrored in PlanetCommon.hlsl.
    {
        public float height, tone, density, feature;
        public uint kind, seed, reserved0, reserved1;
    }

    public struct CellEdit
    {
        public int kind; // zero means procedural; otherwise SurfaceKind + 1
        public int steps;
    }

    [BurstCompile]
    public struct BuildPlanetPlan : IJobParallelFor
    {
        public int resolution, seed;
        public float radius, heightStep;
        [ReadOnly] public NativeArray<CellEdit> edits;
        [WriteOnly] public NativeArray<CellPlan> plans;

        public static uint Hash(uint x)
        { x ^= x>>16; x *= 0x7feb352du; x ^= x>>15; x *= 0x846ca68bu; return x^(x>>16); }
        public static float Unit(uint x) => (Hash(x)&0xffffffu)/16777216f;
        static float Noise(float3 p, uint seed)
        {
            int3 i=(int3)math.floor(p); float3 f=math.frac(p); f=f*f*(3-2*f);
            float a=0;
            for(int z=0;z<2;z++) for(int y=0;y<2;y++) for(int x=0;x<2;x++)
            {
                uint h=math.hash(i+new int3(x,y,z)) ^ seed;
                a+=Unit(h)*(x==0?1-f.x:f.x)*(y==0?1-f.y:f.y)*(z==0?1-f.z:f.z);
            }
            return a;
        }

        public void Execute(int id)
        {
            float3 d=CubeSphere.Direction(id,resolution);
            uint h=Hash((uint)id ^ Hash((uint)seed));
            float districts=Noise(d*9+17,(uint)seed);
            float fine=Noise(d*38+71,(uint)seed+113);
            SurfaceKind kind=SurfaceKind.Armor;
            float height=math.floor(districts*5)*heightStep*0.16f;
            if(districts>0.54f)
            { kind=SurfaceKind.Structure; height=heightStep*(1+math.floor((districts-0.54f)*24)); }
            if(districts>0.44f && districts<0.52f && fine>0.52f) kind=SurfaceKind.Machinery;

            // Primary trench is a world-space great circle, continuous across all cube seams.
            float equator=math.abs(d.y);
            float meridian=math.abs(math.dot(d,math.normalize(new float3(0.8f,0,0.6f))));
            if(equator<0.028f || (meridian<0.009f && d.y<0.62f))
            { kind=SurfaceKind.Trench; height=-radius*(equator<0.028f?0.027f:0.012f); }
            else if(equator<0.041f) {kind=SurfaceKind.Structure; height=heightStep*2.2f;}

            // A monumental recessed sensor array; its latitude/longitude are independent of the grid.
            float angle=math.acos(math.clamp(math.dot(d,math.normalize(new float3(0.34f,0.48f,0.81f))),-1,1));
            float feature=0;
            if(angle<0.205f)
            {
                kind=SurfaceKind.Array;
                float t=angle/0.18f;
                height=angle<0.18f ? -radius*0.055f*(1-t*t) : heightStep*2.5f;
                feature=t;
            }
            CellEdit edit=edits[id];
            if(edit.kind!=0)
            {
                kind=(SurfaceKind)(edit.kind-1);
                height=kind==SurfaceKind.Trench ? -radius*0.027f : 0;
                feature=0;
            }
            height=math.clamp(height+edit.steps*heightStep,-radius*0.08f,radius*0.07f);
            plans[id]=new CellPlan{height=height,tone=0.38f+districts*0.32f+Unit(h)*0.09f,
                density=kind==SurfaceKind.Machinery?0.95f:kind==SurfaceKind.Structure?0.65f:0.14f,
                feature=feature,kind=(uint)kind,seed=h};
        }
    }
}
