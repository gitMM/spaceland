using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    // A generation owns all of its GPU data. Only three indirect draws are submitted per camera.
    // CPU geometry never leaves the compact plan; shared primitive vertices are reconstructed in the shader.
    public sealed class PlanetRenderer : IDisposable
    {
        public const int SlotsPerCell=12;
        readonly ComputeShader compute;
        readonly Material material;
        readonly GraphicsBuffer neighbours;
        readonly int generateKernel,cullKernel,resolution,count;
        readonly float radius;
        readonly Plane[] planes=new Plane[6];
        readonly Vector4[] planeVectors=new Vector4[6];
        readonly MaterialPropertyBlock properties=new MaterialPropertyBlock();
        static readonly string[] VisibilityNames={"_VisibleTops","_VisibleWalls","_VisibleBoxes"};
        readonly List<Retired> retired=new List<Retired>();
        Generation active;
        bool disposed;
        float nextReadback;
        int pendingReadbacks;
        public uint VisibleTops {get;private set;}
        public uint VisibleWalls {get;private set;}
        public uint VisibleBoxes {get;private set;}
        public long ResidentBytes => (active?.bytes??0)+neighbours.count*16L;
        public long PeakResidentBytes {get;private set;}
        public int Revision {get;private set;}
        public bool HasGeneration => active!=null;

        sealed class Generation : IDisposable
        {
            public GraphicsBuffer plans,primitives,counts,bounds;
            public readonly GraphicsBuffer[] visible=new GraphicsBuffer[3],args=new GraphicsBuffer[3];
            public long bytes;
            public Generation(int count)
            {
                try
                {
                    plans=Buffer(count,32); primitives=Buffer(count*SlotsPerCell,32);
                    counts=Buffer(count,4);bounds=Buffer(count,8);
                    for(int i=0;i<3;i++)
                    {
                        int capacity=count*(i==0?1:i==1?4:7);
                        visible[i]=new GraphicsBuffer(GraphicsBuffer.Target.Append,capacity,4);
                        args[i]=new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments,1,GraphicsBuffer.IndirectDrawArgs.size);
                        args[i].SetData(new[]{new GraphicsBuffer.IndirectDrawArgs{vertexCountPerInstance=(uint)(i==2?36:6),instanceCount=0,startVertex=0,startInstance=0}});
                    }
                    bytes=count*(32L+SlotsPerCell*32L+4+8+SlotsPerCell*4)+3*GraphicsBuffer.IndirectDrawArgs.size;
                }
                catch {Dispose();throw;}
            }
            static GraphicsBuffer Buffer(int count,int stride)=>new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,stride);
            public void Dispose()
            {
                plans?.Dispose();primitives?.Dispose();counts?.Dispose();bounds?.Dispose();
                for(int i=0;i<3;i++){visible[i]?.Dispose();args[i]?.Dispose();}
            }
        }
        sealed class Retired
        {public Generation generation;public int frame;public bool fenced;public GraphicsFence fence;}

        public PlanetRenderer(ComputeShader shader,Material source,int resolution,float radius)
        {
            this.resolution=resolution;this.radius=radius;count=CubeSphere.Count(resolution);
            compute=UnityEngine.Object.Instantiate(shader);
            material=new Material(source){name="Death Star runtime surface"};
            generateKernel=compute.FindKernel("Generate");cullKernel=compute.FindKernel("Cull");
            neighbours=new GraphicsBuffer(GraphicsBuffer.Target.Structured,count,16);
            using(var data=new NativeArray<int4>(count,Allocator.Temp))
            {var writable=data;for(int i=0;i<count;i++) writable[i]=CubeSphere.Neighbours(i,resolution);neighbours.SetData(data);}
        }

        public void Publish(NativeArray<CellPlan> plans)
        {
            var next=new Generation(count);
            try
            {
                next.plans.SetData(plans);
                Bind(next,generateKernel);
                compute.SetBuffer(generateKernel,"_Neighbours",neighbours);
                compute.Dispatch(generateKernel,(count+63)/64,1,1);
            }
            catch {next.Dispose();throw;}
            if(active!=null) retired.Add(new Retired{generation=active,frame=Time.frameCount});
            active=next;Revision++;
            long bytes=ResidentBytes;foreach(var item in retired) bytes+=item.generation.bytes;
            PeakResidentBytes=Math.Max(PeakResidentBytes,bytes);
        }

        void Bind(Generation generation,int kernel)
        {
            compute.SetInt("_Resolution",resolution);compute.SetFloat("_Radius",radius);compute.SetInt("_CellCount",count);
            compute.SetBuffer(kernel,"_Plans",generation.plans);
            compute.SetBuffer(kernel,"_Primitives",generation.primitives);
            compute.SetBuffer(kernel,"_Counts",generation.counts);
            compute.SetBuffer(kernel,"_HeightBounds",generation.bounds);
        }

        public void Draw(Camera camera,Vector3 sun,Vector4 brush,Color brushColor,float detailScale)
        {
            CollectRetired();
            if(active==null || camera==null) return;
            GeometryUtility.CalculateFrustumPlanes(camera,planes);
            for(int i=0;i<6;i++) planeVectors[i]=new Vector4(planes[i].normal.x,planes[i].normal.y,planes[i].normal.z,planes[i].distance);
            Bind(active,cullKernel);
            compute.SetVectorArray("_Frustum",planeVectors);
            compute.SetVector("_CameraPosition",camera.transform.position);
            compute.SetFloat("_PixelScale",camera.pixelHeight/(2*Mathf.Tan(camera.fieldOfView*Mathf.Deg2Rad*0.5f)));
            compute.SetFloat("_DetailScale",detailScale);
            for(int i=0;i<3;i++){active.visible[i].SetCounterValue(0);compute.SetBuffer(cullKernel,VisibilityNames[i],active.visible[i]);}
            compute.Dispatch(cullKernel,(count+63)/64,1,1);
            for(int i=0;i<3;i++) GraphicsBuffer.CopyCount(active.visible[i],active.args[i],4);
            properties.SetBuffer("_Plans",active.plans);properties.SetBuffer("_Primitives",active.primitives);
            properties.SetInt("_Resolution",resolution);properties.SetFloat("_Radius",radius);
            properties.SetVector("_SunDirection",sun);properties.SetVector("_Brush",brush);properties.SetColor("_BrushColor",brushColor);
            for(int i=0;i<3;i++)
            {
                properties.SetBuffer("_Visible",active.visible[i]);
                var rp=new RenderParams(material){camera=camera,worldBounds=new Bounds(Vector3.zero,Vector3.one*radius*2.4f),
                    matProps=properties,layer=0,renderingLayerMask=1u,rendererPriority=0,
                    shadowCastingMode=ShadowCastingMode.Off,receiveShadows=false,lightProbeUsage=LightProbeUsage.Off};
                Graphics.RenderPrimitivesIndirect(rp,MeshTopology.Triangles,active.args[i]);
            }
            if(Time.unscaledTime>=nextReadback && pendingReadbacks==0 && SystemInfo.supportsAsyncGPUReadback)
            {
                nextReadback=Time.unscaledTime+1;
                int revision=Revision;
                for(int i=0;i<3;i++)
                {
                    int bucket=i;pendingReadbacks++;
                    AsyncGPUReadback.Request(active.args[i],request=>
                    {
                        pendingReadbacks--;
                        if(disposed || request.hasError || revision!=Revision) return;
                        uint amount=request.GetData<uint>()[1];
                        if(bucket==0) VisibleTops=amount;else if(bucket==1) VisibleWalls=amount;else VisibleBoxes=amount;
                    });
                }
            }
        }

        void CollectRetired()
        {
            for(int i=retired.Count-1;i>=0;i--)
            {
                var item=retired[i];
                // Indirect draw descriptors can remain queued on the render thread. Fence only after
                // those frames have been submitted, then wait for actual GPU completion.
                if(Time.frameCount-item.frame<4) continue;
                if(!item.fenced && SystemInfo.supportsGraphicsFence)
                {item.fence=Graphics.CreateGraphicsFence(GraphicsFenceType.AsyncQueueSynchronisation,SynchronisationStageFlags.AllGPUOperations);item.fenced=true;continue;}
                if(item.fenced && !item.fence.passed) continue;
                if(pendingReadbacks!=0) continue;
                item.generation.Dispose();retired.RemoveAt(i);
            }
        }

        public void Dispose()
        {
            if(disposed)return;disposed=true;
            // Unity releases the native resources after outstanding commands; readback callbacks ignore disposal.
            active?.Dispose();foreach(var item in retired)item.generation.Dispose();retired.Clear();
            neighbours.Dispose();
            if(Application.isPlaying){UnityEngine.Object.Destroy(material);UnityEngine.Object.Destroy(compute);}
            else {UnityEngine.Object.DestroyImmediate(material);UnityEngine.Object.DestroyImmediate(compute);}
        }
    }
}
