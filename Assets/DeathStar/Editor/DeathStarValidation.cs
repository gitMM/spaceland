using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace DeathStar.EditorTools
{
    public static class DeathStarValidation
    {
        [StructLayout(LayoutKind.Sequential)]
        struct Primitive {public uint cell,flags;public float lower,upper;public float4 rect;}
        static void Require(bool condition,string message){if(!condition)throw new Exception(message);}

        public static string Run()
        {
            var surfaceShader=Shader.Find("DeathStar/Industrial Surface");
            Require(surfaceShader!=null && !ShaderUtil.ShaderHasError(surfaceShader),"Surface shader has compilation errors");
            int edges=0;
            foreach(int n in new[]{4,17,128})
            {
                var corners=new Dictionary<int3,int>();
                int count=CubeSphere.Count(n);
                for(int cell=0;cell<count;cell++)
                {
                    Require(CubeSphere.FromDirection(CubeSphere.Direction(cell,n),n)==cell,"Center picking round trip failed");
                    for(int corner=0;corner<4;corner++)
                    {int3 key=CubeSphere.CornerKey(cell,corner,n);corners.TryGetValue(key,out int value);corners[key]=value+1;}
                    for(int side=0;side<4;side++)
                    {
                        int neighbour=CubeSphere.Neighbour(cell,side,n);int reciprocal=-1;
                        for(int s=0;s<4;s++)if(CubeSphere.Neighbour(neighbour,s,n)==cell)reciprocal=s;
                        Require(reciprocal>=0,"Non-reciprocal cube edge");
                        int3 a=CubeSphere.CornerKey(cell,side,n),b=CubeSphere.CornerKey(cell,(side+1)%4,n);
                        int3 c=CubeSphere.CornerKey(neighbour,reciprocal,n),d=CubeSphere.CornerKey(neighbour,(reciprocal+1)%4,n);
                        Require(math.all(a==d)&&math.all(b==c),"Shared edge endpoints differ or winding was not transported");edges++;
                    }
                }
                int singular=0;
                foreach(int valence in corners.Values){Require(valence==3||valence==4,"Invalid vertex fan");if(valence==3)singular++;}
                Require(singular==8,"Cube must have exactly eight valence-three corners");
                Require(corners.Count==6*n*n+2,"Euler characteristic mismatch");
            }

            const int resolution=32;int cells=CubeSphere.Count(resolution);
            using(var edits=new NativeArray<CellEdit>(cells,Allocator.TempJob))
            using(var first=new NativeArray<CellPlan>(cells,Allocator.TempJob))
            using(var second=new NativeArray<CellPlan>(cells,Allocator.TempJob))
            {
                var job=new BuildPlanetPlan{resolution=resolution,radius=200,heightStep=1.25f,seed=1977,edits=edits,plans=first};
                job.Schedule(cells,128).Complete();job.plans=second;job.Schedule(cells,128).Complete();
                for(int i=0;i<cells;i++)
                {
                    Require(first[i].seed==second[i].seed && first[i].height==second[i].height,"Generation was not deterministic");
                    Require(math.isfinite(first[i].height)&&first[i].height>=-16&&first[i].height<=14,"Invalid radial bounds");
                }
                // Edit across a cube seam; both cells must obey authored state and the GPU must emit only the exposed wall.
                int a=CubeSphere.Id(0,resolution-1,resolution/2,resolution),b=CubeSphere.Neighbour(a,1,resolution);
                var writableEdits=edits;
                writableEdits[a]=new CellEdit{kind=(int)SurfaceKind.Armor+1,steps=4};
                writableEdits[b]=new CellEdit{kind=(int)SurfaceKind.Trench+1};
                job.plans=second;job.Schedule(cells,128).Complete();
                Require(second[a].height==5 && math.abs(second[b].height+5.4f)<0.001f,"Authored seam edit failed");
                ValidateGpu(second,resolution);
                job.seed=1978;job.Schedule(cells,128).Complete();
                Require(second[0].seed!=first[0].seed,"Seed did not change generation");
            }
            return $"PASS: {edges:N0} directed edges; reciprocal face transport, exact edge vertices, eight cube corners, picking round trips, deterministic plans, radial bounds, seam edits and GPU primitive/count contracts.";
        }

        static void ValidateGpu(NativeArray<CellPlan> plans,int resolution)
        {
            Require(Marshal.SizeOf<CellPlan>()==32 && Marshal.SizeOf<Primitive>()==32,"GPU record stride mismatch");
            var source=AssetDatabase.LoadAssetAtPath<ComputeShader>(DeathStarMenu.Root+"/Shaders/PlanetGeometry.compute");
            var compute=UnityEngine.Object.Instantiate(source);
            int count=plans.Length;
            using(var input=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,32))
            using(var neighbours=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,16))
            using(var output=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count*12,32))
            using(var counts=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,4))
            using(var bounds=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,8))
            {
                try
                {
                    var links=new int4[count];for(int i=0;i<count;i++)links[i]=CubeSphere.Neighbours(i,resolution);
                    input.SetData(plans);neighbours.SetData(links);
                    int kernel=compute.FindKernel("Generate");
                    compute.SetInt("_Resolution",resolution);compute.SetInt("_CellCount",count);compute.SetFloat("_Radius",200);
                    compute.SetBuffer(kernel,"_Plans",input);compute.SetBuffer(kernel,"_Neighbours",neighbours);
                    compute.SetBuffer(kernel,"_Primitives",output);compute.SetBuffer(kernel,"_Counts",counts);compute.SetBuffer(kernel,"_HeightBounds",bounds);
                    compute.Dispatch(kernel,(count+63)/64,1,1);
                    // Synchronous readback is restricted to this explicit development validation.
                    var totals=new uint[count];var primitives=new Primitive[count*12];counts.GetData(totals);output.GetData(primitives);
                    for(int cell=0;cell<count;cell++)
                    {
                        Require(totals[cell]>=1 && totals[cell]<=12,"Primitive output exceeded fixed reservation");
                        int expectedWalls=0,actualWalls=0;
                        for(int side=0;side<4;side++)if(plans[cell].height>plans[links[cell][side]].height+0.001f)expectedWalls++;
                        for(int k=0;k<totals[cell];k++)
                        {
                            var p=primitives[cell*12+k];Require(p.cell==cell,"Primitive ownership escaped reserved range");
                            Require(math.isfinite(p.lower)&&math.isfinite(p.upper)&&p.upper>=p.lower,"Invalid primitive height");
                            if((p.flags&3)==1)actualWalls++;
                        }
                        Require(actualWalls==expectedWalls,"Missing or duplicate exposed wall");
                    }
                }
                finally {UnityEngine.Object.DestroyImmediate(compute);}
            }
        }
    }
}
