// SSILVB implementation adapted from Button.
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    // Shared SSILVB GI and AO. Source lighting never includes this pass's
    // output. No temporal history means no feedback or ghost trails on moving props.
    public sealed class StationScreenGiFeature : ScriptableRendererFeature
    {
        Material material;
        GiPass pass;
        CompositePass compositePass;
        ContactPass contactPass;
        bool customAoActive;
        StationVoxelGi voxels;
        // Keep these indices in the same order as StationScreenGi.shader.
        const int FilterGiAo=0, CompositeGi=1, ShowAo=2, FilterAo=3, Trace=4, TraceAo=5, Contact=6, ContactBlur=7;
        static readonly int AoTextureId=Shader.PropertyToID("_ScreenSpaceOcclusionTexture");
        static readonly int SourceId=Shader.PropertyToID("_GiSource");
        static readonly int ReceiverId=Shader.PropertyToID("_GiReceiver");
        static readonly int DeltaId=Shader.PropertyToID("_GiDelta");
        static readonly int FilteredId=Shader.PropertyToID("_GiFiltered");
        static readonly int DebugAoId=Shader.PropertyToID("_GiDebugAo");
        static readonly int GiOnlyId=Shader.PropertyToID("_GiOnly");
        static readonly int DepthId=Shader.PropertyToID("_CameraDepthTexture");
        static readonly int NormalsId=Shader.PropertyToID("_CameraNormalsTexture");
        static readonly int ParametersId=Shader.PropertyToID("_GiParameters");
        static readonly int SizeId=Shader.PropertyToID("_GiSize");
        static readonly int SliceParametersId=Shader.PropertyToID("_GiSliceParameters");
        static readonly int AoParametersId=Shader.PropertyToID("_GiAoParameters");
        static readonly int AmbientOcclusionId=Shader.PropertyToID("_AmbientOcclusionParam");
        static readonly int ContactTextureId=Shader.PropertyToID("_StationContactShadowTexture");
        static readonly int ContactGlobalId=Shader.PropertyToID("_StationContactShadowParams");
        static readonly int ContactParametersId=Shader.PropertyToID("_ContactParams");
        static readonly int ContactSettingsId=Shader.PropertyToID("_ContactSettings");
        static readonly int ContactLightId=Shader.PropertyToID("_ContactLightDir");
        static readonly int ContactRawId=Shader.PropertyToID("_ContactRaw");
        public override void Create()
        {
            voxels?.Dispose();voxels=new StationVoxelGi();
            ResetAo();
            CoreUtils.Destroy(material);
            var shader=Resources.Load<Shader>("StationLighting/StationScreenGi");
            if(shader) material=CoreUtils.CreateEngineMaterial(shader);
            pass=new GiPass();
            compositePass=new CompositePass();
            contactPass=new ContactPass();
            RenderPipelineManager.endCameraRendering-=EndCamera;
            RenderPipelineManager.endCameraRendering+=EndCamera;
        }
        void ResetAo() {
            if(customAoActive) {
                Shader.DisableKeyword("_SCREEN_SPACE_OCCLUSION");
                Shader.DisableKeyword("_STATION_SCREEN_OCCLUSION");
            }
            customAoActive=false;
            // Contact shadows are valid only while their pass has run for the camera being drawn.
            Shader.SetGlobalVector(ContactGlobalId,Vector4.zero);
        }
        void EndCamera(ScriptableRenderContext context,Camera camera) => ResetAo();
        public override void AddRenderPasses(ScriptableRenderer renderer,ref RenderingData data)
        {
            ResetAo();
            var settings=StationLightingSettings.Current;


            if(!material || !settings || data.cameraData.isPreviewCamera) return;
            settings.ApplyGlobals();
            bool supported=!data.cameraData.camera.stereoEnabled && data.cameraData.cameraType!=CameraType.Reflection &&
                data.cameraData.renderType==CameraRenderType.Base && data.cameraData.cameraTargetDescriptor.msaaSamples<=1;
            if(!supported) return;
            if(settings.ContactShadowsActive) {
                contactPass.Material=material;
                contactPass.Parameters=new Vector4(Mathf.Max(.05f,settings.contactLengthNear),Mathf.Max(.05f,settings.contactLengthFar),
                    Mathf.Max(1,settings.contactFarDistance),Mathf.Max(.02f,settings.contactThickness));
                contactPass.Settings=new Vector4(Mathf.Clamp(settings.contactSteps,4,64),Mathf.Clamp01(settings.contactStrength),0,0);
                // Before the GI sources are drawn, so bounce sources see the same sun shadows as the receivers.
                contactPass.renderPassEvent=RenderPassEvent.AfterRenderingGbuffer+1;
                renderer.EnqueuePass(contactPass);
            }
            bool contactDebug=settings.giDebugView==GiDebugView.ContactShadowsOnly;
            bool aoDebug=settings.giDebugView==GiDebugView.ScreenSpaceAoOnly||contactDebug;
            bool voxelAvailable=!aoDebug && settings.giDebugView!=GiDebugView.ScreenSpaceGiOnly &&
                settings.giDebugView!=GiDebugView.DiffuseSources && voxels.Prepare(data.cameraData.camera,settings,material);
            if(!voxelAvailable)material.SetVector("_StationVoxelParams",Vector4.zero);
            bool giAvailable=settings.screenSpaceGi && settings.screenGiStrength>0 && !aoDebug && settings.giDebugView!=GiDebugView.VoxelGiOnly;
            if(settings.giDebugView==GiDebugView.Normal && !giAvailable && !voxelAvailable &&
                !(settings.screenSpaceAmbientOcclusion && settings.ssaoStrength>0 && settings.screenGiAoIntensity>0)) return;
            pass.Material=material;
            pass.DebugView=settings.giDebugView;
            pass.Parameters=new Vector4(giAvailable?Mathf.Clamp01(settings.screenGiStrength):0,
                Mathf.Clamp(settings.screenGiRadius,.1f,30),voxelAvailable?1:0,settings.giDebugView==GiDebugView.VoxelGiOnly?1:0);
            pass.SliceParameters=new Vector4(Mathf.Clamp(settings.screenGiSlices,2,12),
                Mathf.Clamp(settings.screenGiSliceSteps,4,32),Mathf.Clamp(settings.screenGiSurfaceThickness,.01f,3),0);
            pass.AoParameters=new Vector4(Mathf.Clamp(settings.screenGiAoRadius,.05f,5),Mathf.Clamp(settings.screenGiAoIntensity,0,3),0,0);
            pass.DirectAo=settings.ssaoDirectStrength;

            // AO writes its own texture and never reads camera color. Only the
            // cascade replacement and the separate composite need an intermediate.
            pass.requiresIntermediateTexture=false;
            pass.renderPassEvent=RenderPassEvent.AfterRenderingGbuffer+3;

            customAoActive=true;
            compositePass.Material=material;
            compositePass.AoOnly=aoDebug;compositePass.ContactOnly=contactDebug;
            compositePass.GiOnly=settings.giDebugView==GiDebugView.DiffuseSources?2:((settings.giDebugView==GiDebugView.ScreenSpaceGiOnly||settings.giDebugView==GiDebugView.VoxelGiOnly)?1:0);
            renderer.EnqueuePass(pass);
            // Ordinary AO is consumed during opaque shading; it needs no color copy.
            if(giAvailable || voxelAvailable || aoDebug || compositePass.GiOnly>0) renderer.EnqueuePass(compositePass);
        }

        protected override void Dispose(bool disposing) {
            voxels?.Dispose();voxels=null;
            ResetAo();RenderPipelineManager.endCameraRendering-=EndCamera;
            CoreUtils.Destroy(material);
        }

        sealed class ContactFrame : ContextItem {
            public TextureHandle Shadow;
            public override void Reset() {Shadow=TextureHandle.nullHandle;}
        }
        // Full-resolution sun visibility from a short march toward the sun through the depth buffer. Opaque station
        // materials multiply it into the main light's shadow (StationLighting.hlsl).
        sealed class ContactPass : ScriptableRenderPass {
            public Material Material;
            public Vector4 Parameters,Settings;
            sealed class Data {public Material Material;public TextureHandle Depth,Normals;public Vector4 Parameters,Settings,Light;}
            sealed class BlurData {public Material Material;public TextureHandle Depth,Raw;}
            public ContactPass() {ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Normal);requiresIntermediateTexture=false;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
                var resources=frameData.Get<UniversalResourceData>();
                var camera=frameData.Get<UniversalCameraData>();
                var lights=frameData.Get<UniversalLightData>();
                if(!resources.cameraDepthTexture.IsValid()||!resources.cameraNormalsTexture.IsValid()||lights.mainLightIndex<0)return;
                Vector3 toSun=-lights.visibleLights[lights.mainLightIndex].localToWorldMatrix.GetColumn(2);
                var desc=camera.cameraTargetDescriptor;
                desc.msaaSamples=1;desc.depthBufferBits=0;desc.depthStencilFormat=GraphicsFormat.None;desc.graphicsFormat=GraphicsFormat.R8_UNorm;
                var raw=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station sun contact shadows (raw)",false,FilterMode.Point);
                var target=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station sun contact shadows",false,FilterMode.Point);
                using(var builder=graph.AddRasterRenderPass<Data>("Station sun contact shadows",out var data)) {
                    data.Material=Material;data.Depth=resources.cameraDepthTexture;data.Normals=resources.cameraNormalsTexture;
                    data.Parameters=Parameters;data.Settings=Settings;data.Light=toSun.normalized;
                    builder.UseTexture(data.Depth);builder.UseTexture(data.Normals);
                    builder.SetRenderAttachment(raw,0,AccessFlags.Write);
                    builder.SetRenderFunc((Data d,RasterGraphContext ctx)=> {
                        d.Material.SetTexture(DepthId,d.Depth);d.Material.SetTexture(NormalsId,d.Normals);
                        d.Material.SetVector(ContactParametersId,d.Parameters);d.Material.SetVector(ContactSettingsId,d.Settings);
                        d.Material.SetVector(ContactLightId,d.Light);
                        Blitter.BlitTexture(ctx.cmd,d.Depth,new Vector4(1,1,0,0),d.Material,Contact);
                    });
                }
                using(var builder=graph.AddRasterRenderPass<BlurData>("Station sun contact shadow blur",out var data)) {
                    data.Material=Material;data.Depth=resources.cameraDepthTexture;data.Raw=raw;
                    builder.UseTexture(data.Depth);builder.UseTexture(raw);
                    builder.SetRenderAttachment(target,0,AccessFlags.Write);
                    builder.AllowPassCulling(false);
                    builder.AllowGlobalStateModification(true);builder.SetGlobalTextureAfterPass(target,ContactTextureId);
                    builder.SetRenderFunc((BlurData d,RasterGraphContext ctx)=> {
                        d.Material.SetTexture(ContactRawId,d.Raw);d.Material.SetTexture(DepthId,d.Depth);
                        Blitter.BlitTexture(ctx.cmd,d.Raw,new Vector4(1,1,0,0),d.Material,ContactBlur);
                        ctx.cmd.SetGlobalVector(ContactGlobalId,new Vector4(1,0,0,0));
                    });
                }
                frameData.Create<ContactFrame>().Shadow=target;
            }
        }

        sealed class CombinedFrame : ContextItem {
            public TextureHandle Filtered,Receiver,Ao;
            public override void Reset() {Filtered=Receiver=Ao=TextureHandle.nullHandle;}
        }
        sealed class CompositePass : ScriptableRenderPass {
            public Material Material;
            public bool AoOnly,ContactOnly; public int GiOnly;
            sealed class Data {public Material Material; public TextureHandle Color,Filtered,Receiver,Ao;public bool AoOnly; public int GiOnly;}
            public CompositePass() {renderPassEvent=RenderPassEvent.AfterRenderingOpaques+2;requiresIntermediateTexture=true;}
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData) {
                if(!frameData.Contains<CombinedFrame>())return;
                var combined=frameData.Get<CombinedFrame>();
                if(!combined.Filtered.IsValid())return;
                var resources=frameData.Get<UniversalResourceData>();
                var desc=frameData.Get<UniversalCameraData>().cameraTargetDescriptor;
                desc.depthBufferBits=0;desc.depthStencilFormat=GraphicsFormat.None;desc.msaaSamples=1;
                var output=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station combined GI composite",false);
                using(var builder=graph.AddRasterRenderPass<Data>("Station combined GI composite",out var data)) {
                    data.Material=Material;data.Color=resources.activeColorTexture;data.Filtered=combined.Filtered;data.Receiver=combined.Receiver;
                    data.AoOnly=AoOnly;data.GiOnly=GiOnly;data.Ao=combined.Ao;
                    // The contact view shows the sun visibility mask through the AO diagnostic (white where lit).
                    if(ContactOnly)data.Ao=frameData.Contains<ContactFrame>()&&frameData.Get<ContactFrame>().Shadow.IsValid()?
                        frameData.Get<ContactFrame>().Shadow:graph.defaultResources.whiteTexture;
                    builder.UseTexture(data.Ao);
                    builder.UseTexture(data.Color);builder.UseTexture(data.Filtered);builder.UseTexture(data.Receiver);
                    builder.SetRenderAttachment(output,0,AccessFlags.Write);
                    builder.SetRenderFunc((Data d,RasterGraphContext ctx)=> {
                        d.Material.SetTexture(FilteredId,d.Filtered);d.Material.SetTexture(ReceiverId,d.Receiver);
                        d.Material.SetTexture(DebugAoId,d.Ao);
                        d.Material.SetInt(GiOnlyId,d.GiOnly);
                        Blitter.BlitTexture(ctx.cmd,d.Color,new Vector4(1,1,0,0),d.Material,d.AoOnly?ShowAo:CompositeGi);
                    });
                }
                resources.cameraColor=output;
            }
        }

        sealed class GiPass : ScriptableRenderPass
        {
            public Material Material;
            public Vector4 Parameters;
            public Vector4 SliceParameters;
            public GiDebugView DebugView;
            public Vector4 AoParameters;
            public float DirectAo;
            static readonly ShaderTagId SourceTag=new ShaderTagId("StationGiSource");

            sealed class SourceData {public RendererListHandle List;}
            sealed class TraceData {
                public Material Material;
                public TextureHandle Source,Depth,Normals;
                public Vector4 Parameters,Size,SliceParameters;
                public Vector4 AoParameters;
                public bool HasGi;
            }
            sealed class CompositeData {
                public Material Material;
                public TextureHandle Delta,Depth,Normals;
                public Vector4 Size;
                public bool HasGi;
                public float DirectAo;
            }
            public GiPass() {
                renderPassEvent=RenderPassEvent.AfterRenderingOpaques+2;
                ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Normal);
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                var resources=frameData.Get<UniversalResourceData>();
                var camera=frameData.Get<UniversalCameraData>();
                var rendering=frameData.Get<UniversalRenderingData>();
                var lights=frameData.Get<UniversalLightData>();
                if(!resources.cameraDepthTexture.IsValid() || !resources.cameraNormalsTexture.IsValid()) return;
                var desc=camera.cameraTargetDescriptor;
                desc.msaaSamples=1; desc.depthBufferBits=0; desc.depthStencilFormat=GraphicsFormat.None;
                desc.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;
                bool hasGi=Parameters.x>0||Parameters.z>0;
                TextureHandle source=graph.defaultResources.blackTexture,receiver=graph.defaultResources.blackTexture;
                if(hasGi) {
                    source=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station GI diffuse source",true,FilterMode.Point);
                    receiver=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station GI receiver",true,FilterMode.Point);
                    var drawing=RenderingUtils.CreateDrawingSettings(SourceTag,rendering,camera,lights,camera.defaultOpaqueSortFlags);
                    var filtering=new FilteringSettings(RenderQueueRange.opaque,camera.camera.cullingMask);
                    var list=graph.CreateRendererList(new RendererListParams(rendering.cullResults,drawing,filtering));
                    // Forward/Forward+ without depth priming writes its prepass into the
                    // sampled depth texture; the opaque attachment is still empty here.
                    // Deferred and depth-primed paths instead use the active attachment.
                    // Redraw visibility in a private depth target. Forward+ can
                    // supply a partial depth prepass before opaque rendering.
                    var sourceDepthDesc=new TextureDesc(desc.width,desc.height){name="Station source depth",depthBufferBits=DepthBits.Depth32,clearBuffer=true};
                    var sourceDepth=graph.CreateTexture(sourceDepthDesc);
                    using(var builder=graph.AddRasterRenderPass<SourceData>("Station combined diffuse sources",out var data)) {
                        data.List=list;
                        builder.UseRendererList(list);
                        // The source shader reads direct-light shadows, never scene depth,
                        // AO or SDF GI. Do not also bind the prepass depth as a sampled
                        // global while it is attached for the Equal depth test.
                        if(resources.mainShadowsTexture.IsValid())builder.UseTexture(resources.mainShadowsTexture);
                        if(resources.additionalShadowsTexture.IsValid())builder.UseTexture(resources.additionalShadowsTexture);
                        if(frameData.Contains<ContactFrame>()&&frameData.Get<ContactFrame>().Shadow.IsValid())builder.UseTexture(frameData.Get<ContactFrame>().Shadow);
                        builder.SetRenderAttachment(source,0,AccessFlags.Write);
                        builder.SetRenderAttachment(receiver,1,AccessFlags.Write);
                        builder.SetRenderAttachmentDepth(sourceDepth,AccessFlags.ReadWrite);
                        builder.SetRenderFunc((SourceData d,RasterGraphContext ctx)=>ctx.cmd.DrawRendererList(d.List));
                    }
                }
                desc.width=(desc.width+1)/2; desc.height=(desc.height+1)/2;

                // Preserve the negative background sentinel in a signed half float.
                // AO-only needs one channel instead of four (2 versus 8 bytes/pixel).
                desc.graphicsFormat=hasGi?GraphicsFormat.R16G16B16A16_SFloat:GraphicsFormat.R16_SFloat;
                var delta=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station screen GI AO trace",false,FilterMode.Point);
                var size=new Vector4(desc.width,desc.height,1f/desc.width,1f/desc.height);
                using(var builder=graph.AddRasterRenderPass<TraceData>("Station combined GI AO trace",out var data)) {
                    data.Material=Material;data.Source=source;
                    data.Depth=resources.cameraDepthTexture;data.Normals=resources.cameraNormalsTexture;
                    data.Parameters=Parameters;data.Size=size;data.SliceParameters=SliceParameters;
                    data.AoParameters=AoParameters;data.HasGi=hasGi;
                    builder.UseTexture(source);
                    builder.UseTexture(data.Depth);builder.UseTexture(data.Normals);
                    builder.SetRenderAttachment(delta,0,AccessFlags.Write);
                    builder.SetRenderFunc((TraceData d,RasterGraphContext ctx)=> {
                        d.Material.SetTexture(SourceId,d.Source);
                        d.Material.SetTexture(DepthId,d.Depth);d.Material.SetTexture(NormalsId,d.Normals);
                        d.Material.SetVector(ParametersId,d.Parameters);
                        d.Material.SetVector(SizeId,d.Size);
                        d.Material.SetVector(SliceParametersId,d.SliceParameters);
                        d.Material.SetVector(AoParametersId,d.AoParameters);
                        Blitter.BlitTexture(ctx.cmd,d.Source,new Vector4(1,1,0,0),d.Material,d.HasGi?Trace:TraceAo);
                    });
                }
                {
                    var fullDesc=camera.cameraTargetDescriptor;
                    fullDesc.msaaSamples=1;fullDesc.depthBufferBits=0;fullDesc.depthStencilFormat=GraphicsFormat.None;
                    fullDesc.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;
                    var filtered=hasGi?UniversalRenderer.CreateRenderGraphTexture(graph,fullDesc,"Station filtered screen GI",false,FilterMode.Point):graph.defaultResources.blackTexture;
                    fullDesc.graphicsFormat=GraphicsFormat.R8_UNorm;
                    var ao=UniversalRenderer.CreateRenderGraphTexture(graph,fullDesc,"Station combined screen AO",false,FilterMode.Bilinear);
                    using(var builder=graph.AddRasterRenderPass<CompositeData>("Station combined GI AO filter",out var data)) {
                        data.Material=Material;data.Delta=delta;data.Depth=resources.cameraDepthTexture;
                        data.Normals=resources.cameraNormalsTexture;data.Size=size;data.DirectAo=DirectAo;data.HasGi=hasGi;
                        builder.UseTexture(delta);builder.UseTexture(data.Depth);builder.UseTexture(data.Normals);
                        if(hasGi)builder.SetRenderAttachment(filtered,0,AccessFlags.Write);
                        builder.SetRenderAttachment(ao,hasGi?1:0,AccessFlags.Write);
                        builder.AllowGlobalStateModification(true);builder.SetGlobalTextureAfterPass(ao,AoTextureId);
                        builder.SetRenderFunc((CompositeData d,RasterGraphContext ctx)=> {
                            d.Material.SetTexture(DeltaId,d.Delta);d.Material.SetTexture(DepthId,d.Depth);
                            d.Material.SetTexture(NormalsId,d.Normals);d.Material.SetVector(SizeId,d.Size);
                            Blitter.BlitTexture(ctx.cmd,d.Delta,new Vector4(1,1,0,0),d.Material,d.HasGi?FilterGiAo:FilterAo);
                            ctx.cmd.SetGlobalVector(AmbientOcclusionId,new Vector4(1,0,0,Mathf.Clamp01(d.DirectAo)));
                            ctx.cmd.EnableShaderKeyword("_SCREEN_SPACE_OCCLUSION");
                            ctx.cmd.EnableShaderKeyword("_STATION_SCREEN_OCCLUSION");
                        });
                    }
                    var combined=frameData.Create<CombinedFrame>();combined.Filtered=DebugView==GiDebugView.DiffuseSources?source:filtered;combined.Receiver=receiver;combined.Ao=ao;
                    return;
                }
            }
        }
    }
}
