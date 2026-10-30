using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    sealed class StationLocalLightFrame:ContextItem
    {
        public TextureHandle irradiance;
        public override void Reset(){irradiance=TextureHandle.nullHandle;}
    }
    // Accumulate diffuse irradiance before forward shading. Shared station materials apply
    // their own reflectance; no multiplication of the already-lit camera image, no GI history.
    sealed class StationLocalLightPass:ScriptableRenderPass,System.IDisposable
    {
        static readonly int TextureId=Shader.PropertyToID("_StationLocalLightTexture");
        static readonly int ActiveId=Shader.PropertyToID("_StationLocalLightActive");
        readonly List<StationLocalLights.LightData> lights=new List<StationLocalLights.LightData>();
        readonly Plane[] planes=new Plane[6];
        Material material;GraphicsBuffer buffer;
        public StationLocalLightPass() {
            renderPassEvent=RenderPassEvent.AfterRenderingGbuffer+2;
            ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Normal);
            var shader=Resources.Load<Shader>("StationLighting/StationLocalLights");
            if(shader)material=CoreUtils.CreateEngineMaterial(shader);
        }
        public static void Reset() => Shader.SetGlobalFloat(ActiveId,0);
        public void Dispose(){buffer?.Dispose();buffer=null;CoreUtils.Destroy(material);Reset();}
        sealed class Data {public TextureHandle depth,normals;public Material material;public GraphicsBuffer buffer;public int count;}
        public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
        {
            StationLocalLights.VisibleCount=0;
            var resources=frameData.Get<UniversalResourceData>();var camera=frameData.Get<UniversalCameraData>();
            var settings=StationLightingSettings.Current;
            if(!material||!resources.cameraDepthTexture.IsValid()||!resources.cameraNormalsTexture.IsValid())return;
            lights.Clear();StationLocalLights.Gather(camera.camera,lights);
            GeometryUtility.CalculateFrustumPlanes(camera.camera,planes);
            var eye=camera.camera.transform.position;float reach=Mathf.Max(1,settings.localLightDistance);
            int kept=0;
            for(int i=0;i<lights.Count;i++) {
                var l=lights[i];Vector3 a=l.a,b=l.b,centre=(a+b)*.5f;
                float radius=l.a.w*settings.localLightRange;
                var bounds=new Bounds(centre,new Vector3(Mathf.Abs(a.x-b.x),Mathf.Abs(a.y-b.y),Mathf.Abs(a.z-b.z))+Vector3.one*radius*2);
                float distance=Vector3.Distance(eye,centre);
                if(distance>reach+radius||!GeometryUtility.TestPlanesAABB(planes,bounds))continue;
                l.a.w=radius;l.b.w*=settings.localLightStrength*settings.emission*Mathf.Clamp01((reach+radius-distance)/Mathf.Max(1,reach*.2f));
                lights[kept++]=l;
            }
            if(kept<lights.Count)lights.RemoveRange(kept,lights.Count-kept);
            lights.Sort((x,y)=>(((Vector3)x.a-eye).sqrMagnitude).CompareTo(((Vector3)y.a-eye).sqrMagnitude));
            int count=Mathf.Min(lights.Count,Mathf.Clamp(settings.localLightBudget,16,2048));
            if(count==0)return;
            if(buffer==null||buffer.count<count){buffer?.Dispose();buffer=new GraphicsBuffer(GraphicsBuffer.Target.Structured,Mathf.NextPowerOfTwo(count),64);}
            buffer.SetData(lights,0,0,count);StationLocalLights.VisibleCount=count;
            var lightBuffer=graph.ImportBuffer(buffer);
            var desc=camera.cameraTargetDescriptor;desc.msaaSamples=1;desc.depthBufferBits=0;desc.depthStencilFormat=GraphicsFormat.None;
            desc.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;
            var target=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station local lamp irradiance",true,FilterMode.Point);
            frameData.Create<StationLocalLightFrame>().irradiance=target;
            using(var builder=graph.AddRasterRenderPass<Data>("Station local lamps",out var data)) {
                data.depth=resources.cameraDepthTexture;data.normals=resources.cameraNormalsTexture;data.material=material;data.buffer=buffer;data.count=count;
                builder.UseTexture(data.depth);builder.UseTexture(data.normals);builder.SetRenderAttachment(target,0,AccessFlags.Write);
                builder.UseBuffer(lightBuffer,AccessFlags.Read);
                builder.AllowGlobalStateModification(true);builder.AllowPassCulling(false);builder.SetGlobalTextureAfterPass(target,TextureId);
                builder.SetRenderFunc((Data d,RasterGraphContext ctx)=> {
                    d.material.SetTexture("_CameraDepthTexture",d.depth);d.material.SetTexture("_CameraNormalsTexture",d.normals);
                    d.material.SetBuffer("_LocalLamps",d.buffer);
                    ctx.cmd.DrawProcedural(Matrix4x4.identity,d.material,0,MeshTopology.Triangles,36,d.count);
                    ctx.cmd.SetGlobalFloat(ActiveId,1);
                });
            }
        }
    }
}
