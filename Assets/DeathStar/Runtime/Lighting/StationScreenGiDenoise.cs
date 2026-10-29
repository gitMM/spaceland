using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    public sealed partial class StationScreenGiFeature
    {
        // Also used by deterministic headless comparisons and callers making camera cuts.
        static int historyRevision;
        public static void ResetHistory() => historyRevision++;

        sealed class DenoisePass : ScriptableRenderPass, IDisposable
        {
            const int Geometry=9, Temporal=10, Spatial=11, Upsample=12;
            static readonly int GeometryId=Shader.PropertyToID("_GiGeometry");
            static readonly int HistoryId=Shader.PropertyToID("_GiHistory");
            static readonly int HistoryGeometryId=Shader.PropertyToID("_GiHistoryGeometry");
            static readonly int MotionId=Shader.PropertyToID("_GiMotion");
            static readonly int PreviousVpId=Shader.PropertyToID("_GiPreviousVP");
            static readonly int PreviousViewId=Shader.PropertyToID("_GiPreviousView");
            static readonly int CurrentVpId=Shader.PropertyToID("_GiCurrentVP");
            static readonly int TemporalId=Shader.PropertyToID("_GiTemporalParameters");
            static readonly int StrideId=Shader.PropertyToID("_GiFilterStride");
            static readonly int BlitTextureId=Shader.PropertyToID("_BlitTexture");
            static readonly int BlitScaleBiasId=Shader.PropertyToID("_BlitScaleBias");
            static readonly MaterialPropertyBlock properties=new();
            public Material Material;
            public int SampleIndex => temporal?current.Sequence%256:0;
            bool temporal,spatial;
            History current;
            readonly Dictionary<Camera,History> histories=new();
            readonly List<Camera> stale=new();

            sealed class History : IDisposable
            {
                public readonly RTHandle[] Color=new RTHandle[2],Geometry=new RTHandle[2];
                public int Index,Sequence,Signature,Revision,LastFrame=-1;
                public bool Valid;
                public double LastTime;
                public Vector3 Position;
                public Quaternion Rotation;
                public Matrix4x4 View,VP,Projection;
                public void Dispose(){for(int i=0;i<2;i++){Color[i]?.Release();Geometry[i]?.Release();Color[i]=Geometry[i]=null;}Valid=false;}
            }

            public DenoisePass()
            {
                renderPassEvent=RenderPassEvent.AfterRenderingOpaques+1;
                ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Normal);
            }
            public void Skip(Camera camera){if(histories.TryGetValue(camera,out var h)){h.Valid=false;h.Sequence=0;}}
            public void Prepare(Camera camera,StationLightingSettings s)
            {
                double now=Time.realtimeSinceStartupAsDouble;
                stale.Clear();
                foreach(var pair in histories)if(!pair.Key||now-pair.Value.LastTime>10)stale.Add(pair.Key);
                foreach(var key in stale){histories[key].Dispose();histories.Remove(key);}
                if(!histories.TryGetValue(camera,out current))histories.Add(camera,current=new History());
                var hash=new HashCode();
                hash.Add(s.screenGiTemporal);hash.Add(s.screenGiDenoise);hash.Add(s.screenGiRadius);hash.Add(s.screenGiStrength);
                hash.Add(s.screenGiSlices);hash.Add(s.screenGiSliceSteps);hash.Add(s.screenGiSurfaceThickness);
                hash.Add(s.sunFrom);hash.Add(s.sunColor);hash.Add(s.sunIntensity);hash.Add(s.receivedShadowStrength);hash.Add(s.shadowSoftness);
                hash.Add(s.planetFrom);hash.Add(s.planetColor);hash.Add(s.planetIntensity);hash.Add(s.emission);hash.Add(s.localLights);hash.Add(s.localLampScreenGi);
                hash.Add(s.defaultTileTint);hash.Add(s.habitatTintEnabled);hash.Add(s.habitatTint);
                hash.Add(s.closeUpSurface);hash.Add(s.bevelWidth);hash.Add(s.bevelRoundness);hash.Add(s.reliefTrace);hash.Add(s.greebleTrace);
                hash.Add(s.contactShadows);hash.Add(s.contactStrength);hash.Add(s.contactLengthNear);hash.Add(s.contactLengthFar);hash.Add(s.contactThickness);
                hash.Add(s.giDebugView);hash.Add(camera.cullingMask);hash.Add(camera.scene.handle);
                if(s.giDebugView!=GiDebugView.ScreenSpaceGiOnly){hash.Add(s.voxelGi);hash.Add(s.voxelGiStrength);hash.Add(s.voxelGiDistance);hash.Add(StationVoxelGi.BuildCount);}
                int signature=hash.ToHashCode();
                bool cut=Vector3.Distance(current.Position,camera.transform.position)>2 || Quaternion.Angle(current.Rotation,camera.transform.rotation)>30;
                if(signature!=current.Signature||current.Revision!=historyRevision||cut||
                    (current.LastFrame>=0 && Time.frameCount>current.LastFrame+1)||now-current.LastTime>1) {
                    current.Valid=false;current.Sequence=0;
                }
                current.Signature=signature;current.Revision=historyRevision;current.LastTime=now;
                temporal=s.screenGiTemporal;spatial=s.screenGiDenoise;
                ConfigureInput(ScriptableRenderPassInput.Depth|ScriptableRenderPassInput.Normal|
                    (temporal&&Application.isPlaying?ScriptableRenderPassInput.Motion:ScriptableRenderPassInput.None));
            }
            public void Dispose(){foreach(var h in histories.Values)h.Dispose();histories.Clear();}

            sealed class Data
            {
                public Material Material;
                public TextureHandle Input,Depth,Normals,Geometry,History,HistoryGeometry,Motion;
                public Vector4 Size,Temporal;
                public Matrix4x4 PreviousVP,PreviousView,CurrentVP;
                public int Pass,Stride;
            }
            static void Draw(Data d,RasterGraphContext ctx)
            {
                // Snapshot bindings per draw. Repeated draws of the same material pass
                // must not see a later pass's mutable material textures or stride.
                properties.Clear();
                properties.SetTexture(BlitTextureId,d.Input);properties.SetVector(BlitScaleBiasId,new Vector4(1,1,0,0));
                properties.SetTexture(DepthId,d.Depth);
                properties.SetTexture(NormalsId,d.Normals);properties.SetVector(SizeId,d.Size);
                if(d.Geometry.IsValid())properties.SetTexture(GeometryId,d.Geometry);
                if(d.Pass==Temporal) {
                    properties.SetTexture(HistoryId,d.History);properties.SetTexture(HistoryGeometryId,d.HistoryGeometry);
                    properties.SetTexture(MotionId,d.Motion);properties.SetVector(TemporalId,d.Temporal);
                    properties.SetMatrix(PreviousVpId,d.PreviousVP);properties.SetMatrix(PreviousViewId,d.PreviousView);
                    properties.SetMatrix(CurrentVpId,d.CurrentVP);
                }
                properties.SetInteger(StrideId,d.Stride);
                CoreUtils.DrawFullScreen(ctx.cmd,d.Material,properties,d.Pass);
            }
            public override void RecordRenderGraph(RenderGraph graph,ContextContainer frameData)
            {
                if(!frameData.Contains<CombinedFrame>())return;
                var combined=frameData.Get<CombinedFrame>();if(!combined.Raw.IsValid())return;
                var resources=frameData.Get<UniversalResourceData>();
                var cameraData=frameData.Get<UniversalCameraData>();var camera=cameraData.camera;
                var h=current;
                var desc=cameraData.cameraTargetDescriptor;
                desc.msaaSamples=1;desc.depthBufferBits=0;desc.depthStencilFormat=GraphicsFormat.None;
                desc.width=(int)combined.Size.x;desc.height=(int)combined.Size.y;desc.graphicsFormat=GraphicsFormat.R16G16B16A16_SFloat;
                desc.useMipMap=false;desc.autoGenerateMips=false;
                var geometry=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station GI geometric guides",false,FilterMode.Point);
                void Filter(string name,TextureHandle input,TextureHandle output,int pass,int stride=0)
                {
                    using var builder=graph.AddRasterRenderPass<Data>(name,out var d);
                    d.Material=Material;d.Input=input;d.Depth=resources.cameraDepthTexture;d.Normals=resources.cameraNormalsTexture;
                    d.Size=combined.Size;d.Pass=pass;d.Stride=stride;
                    d.Geometry=TextureHandle.nullHandle;
                    builder.UseTexture(d.Input);builder.UseTexture(d.Depth);builder.UseTexture(d.Normals);
                    if(pass!=Geometry){d.Geometry=geometry;builder.UseTexture(geometry);}
                    builder.SetRenderAttachment(output,0,AccessFlags.Write);
                    builder.SetRenderFunc((Data data,RasterGraphContext ctx)=>Draw(data,ctx));
                }
                Filter("Station GI geometric guides",combined.Raw,geometry,Geometry);
                TextureHandle signal=combined.Raw;
                // All reconstruction targets are textures. URP 17's public GPU-matrix
                // helper is a no-op outside compatibility mode, so construct it explicitly.
                var vp=GL.GetGPUProjectionMatrix(cameraData.GetProjectionMatrix(),true)*cameraData.GetViewMatrix();
                if(temporal) {
                    bool allocated=false;
                    for(int i=0;i<2;i++) {
                        allocated|=RenderingUtils.ReAllocateHandleIfNeeded(ref h.Color[i],desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Station GI history "+i);
                        allocated|=RenderingUtils.ReAllocateHandleIfNeeded(ref h.Geometry[i],desc,FilterMode.Point,TextureWrapMode.Clamp,name:"Station GI history geometry "+i);
                    }
                    // Resolution and projection changes invalidate both geometry and radiance history.
                    if(allocated||h.Projection!=camera.projectionMatrix)h.Valid=false;
                    int read=h.Index,write=1-read;
                    var history=graph.ImportTexture(h.Color[read]);var historyGeometry=graph.ImportTexture(h.Geometry[read]);
                    var next=graph.ImportTexture(h.Color[write]);var nextGeometry=graph.ImportTexture(h.Geometry[write]);
                    using(var builder=graph.AddRasterRenderPass<Data>("Station GI temporal accumulation",out var d)) {
                        d.Material=Material;d.Input=signal;d.Depth=resources.cameraDepthTexture;d.Normals=resources.cameraNormalsTexture;
                        d.Geometry=geometry;d.History=history;d.HistoryGeometry=historyGeometry;
                        // Camera.Render may run several times within one Unity frame; Unity's motion vectors then
                        // describe the wrong pair of renders. Matrix reprojection is exact for those static fixtures.
                        bool motion=Application.isPlaying&&h.LastFrame==Time.frameCount-1&&resources.motionVectorColor.IsValid();
                        d.Motion=motion?resources.motionVectorColor:graph.defaultResources.blackTexture;
                        d.Temporal=new Vector4(h.Valid?1:0,motion?1:0,16,0);d.Size=combined.Size;
                        d.PreviousVP=h.VP;d.PreviousView=h.View;d.CurrentVP=vp;d.Pass=Temporal;
                        builder.UseTexture(d.Input);builder.UseTexture(d.Depth);builder.UseTexture(d.Normals);builder.UseTexture(geometry);
                        builder.UseTexture(history);builder.UseTexture(historyGeometry);builder.UseTexture(d.Motion);
                        builder.SetRenderAttachment(next,0,AccessFlags.Write);builder.SetRenderAttachment(nextGeometry,1,AccessFlags.Write);
                        builder.AllowPassCulling(false);
                        builder.SetRenderFunc((Data data,RasterGraphContext ctx)=>Draw(data,ctx));
                    }
                    signal=next;h.Index=write;h.Valid=true;
                } else {h.Valid=false;h.Sequence=0;}
                if(spatial)for(int i=0;i<3;i++) {
                    var output=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station GI spatial "+i,false,FilterMode.Point);
                    Filter("Station GI edge-aware filter "+i,signal,output,Spatial,1<<i);signal=output;
                }
                desc.width=cameraData.cameraTargetDescriptor.width;desc.height=cameraData.cameraTargetDescriptor.height;
                var full=UniversalRenderer.CreateRenderGraphTexture(graph,desc,"Station reconstructed GI",false,FilterMode.Point);
                Filter("Station GI depth-aware upsample",signal,full,Upsample);
                combined.Filtered=full;
                h.View=cameraData.GetViewMatrix();h.VP=vp;h.Projection=camera.projectionMatrix;
                h.Position=camera.transform.position;h.Rotation=camera.transform.rotation;h.LastFrame=Time.frameCount;h.Sequence++;
            }
        }
    }
}
