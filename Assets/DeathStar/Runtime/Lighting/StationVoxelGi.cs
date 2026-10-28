using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DeathStar
{
    // Cache coordination and publication. Sources.cs owns CPU geometry;
    // Gpu.cs owns the temporary solve resources and mip construction.
    public sealed partial class StationVoxelGi : IDisposable
    {
        const int MaxTriangles=65535*64; // D3D dispatch limit; fits the 22-bit owner index.
        const double SceneCheckInterval=.5;
        static int revision,sourceRevision;
        static readonly int RadianceId=Shader.PropertyToID("_StationVoxelRadiance");
        static readonly int MinimumId=Shader.PropertyToID("_StationVoxelMin");
        static readonly int ParametersId=Shader.PropertyToID("_StationVoxelParams");
        static readonly int GridId=Shader.PropertyToID("_StationVoxelGrid");
        static readonly int StepsId=Shader.PropertyToID("_StationVoxelSteps");
        static readonly int StepCountId=Shader.PropertyToID("_StationVoxelStepCount");

        // Replaced meshes are detected by ID. In-place vertex/color edits need
        // source eviction as well as a scene check.
        public static void Invalidate(bool meshDataChanged=false){revision++;if(meshDataChanged)sourceRevision++;}
        public static string Diagnostics {get;private set;}="Not built";
        public static int BuildCount {get;private set;}
        public static StationVoxelGi Last {get;private set;}
        public bool IsReady=>radiance && radiance.IsCreated();

        ComputeShader compute;
        RenderTexture radiance;
        Vector4 minimum,parameters,grid;
        readonly Vector4[] steps=new Vector4[32];
        Vector4 stepKey;
        int stepCount;
        int sceneHandle=-1,seenRevision=-1,seenSourceRevision=-1,sceneHash,settingsHash,mask;
        double nextCheck;

        public bool Prepare(Camera camera,StationLightingSettings settings,Material material)
        {
            material.SetVector(ParametersId,Vector4.zero);
            if(!settings.voxelGi || settings.voxelGiStrength<=0 || !SystemInfo.supportsComputeShaders ||
                !SystemInfo.supports3DTextures || !SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.ARGBHalf))return false;
            if(!compute)compute=Resources.Load<ComputeShader>("StationLighting/StationVoxel");
            if(!compute)return false;

            Scene scene=camera.scene;
            if(!scene.IsValid())scene=SceneManager.GetActiveScene();
            bool force=seenSourceRevision!=sourceRevision;
            if(force){sourceCache.Clear();seenSourceRevision=sourceRevision;}
            int nextSettings=SettingsHash(settings);
            bool changed=scene.handle!=sceneHandle || force || (radiance && !radiance.IsCreated());
            if(changed || seenRevision!=revision || mask!=camera.cullingMask || nextSettings!=settingsHash ||
                Time.realtimeSinceStartupAsDouble>=nextCheck) {
                int next=Collect(scene,camera.cullingMask,settings);
                changed|=next!=sceneHash || nextSettings!=settingsHash;
                sceneHash=next;settingsHash=nextSettings;sceneHandle=scene.handle;
                seenRevision=revision;mask=camera.cullingMask;
                nextCheck=Time.realtimeSinceStartupAsDouble+SceneCheckInterval;
            }
            if(changed) {
                try {Build(settings);}
                catch(Exception error) {
                    // A failed volume must not break the existing screen-space pass.
                    ClearVolume();Diagnostics="Voxel GI unavailable: "+error.Message;Debug.LogException(error);
                }
            }
            if(!IsReady)return false;
            parameters.z=Mathf.Clamp(settings.voxelGiStrength,0,2);
            parameters.y=Mathf.Clamp(settings.voxelGiDistance,5,80);
            parameters.w=settings.screenSpaceGi?Mathf.Max(0,settings.screenGiRadius):0;
            UpdateSteps();
            material.SetTexture(RadianceId,radiance);
            material.SetVector(MinimumId,minimum);material.SetVector(ParametersId,parameters);material.SetVector(GridId,grid);
            material.SetVectorArray(StepsId,steps);material.SetInt(StepCountId,stepCount);
            Last=this;
            return true;
        }

        static int SettingsHash(StationLightingSettings s)
        {
            var h=new HashCode();h.Add(s.voxelGiResolution>=128?128:64);h.Add(s.voxelGiSecondBounce);
            // With one bounce, distance changes only the final gather.
            if(s.voxelGiSecondBounce)h.Add(Mathf.Clamp(s.voxelGiDistance,5,80));
            h.Add(s.sunFrom);h.Add(s.sunColor);h.Add(s.sunIntensity);h.Add(s.receivedShadowStrength);
            h.Add(s.planetFrom);h.Add(s.planetColor);h.Add(s.planetIntensity);
            h.Add(s.defaultTileTint);h.Add(s.habitatTintEnabled);if(s.habitatTintEnabled)h.Add(s.habitatTint);h.Add(s.emission);
            return h.ToHashCode();
        }

        void UpdateSteps()
        {
            var key=new Vector4(parameters.x,parameters.y,parameters.w,minimum.w);
            if(stepKey.Equals(key))return;
            stepKey=key;stepCount=0;
            for(float distance=parameters.x*1.5f;stepCount<steps.Length&&distance<parameters.y;) {
                float diameter=Mathf.Max(parameters.x*2,distance*.5f);
                float lod=Mathf.Clamp(Mathf.Log(diameter/parameters.x,2)-1,0,grid.y);
                float margin=.5f*Mathf.Pow(2,Mathf.Ceil(lod))*grid.x;
                float blend=parameters.w>0?Mathf.InverseLerp(parameters.w*.75f,Mathf.Max(parameters.w,.0001f),distance):1;
                blend=blend*blend*(3-2*blend);
                steps[stepCount++]=new Vector4(distance*minimum.w,lod,margin,blend);
                distance+=diameter*.5f;
            }
        }

        void Build(StationLightingSettings s)
        {
            var watch=System.Diagnostics.Stopwatch.StartNew();
            if(renderers.Count==0){ClearVolume();Diagnostics="No station geometry in this camera scene";return;}
            if(!PrepareSources(s,out int count,out int refreshed)) {
                ClearVolume();Diagnostics=$"Voxel GI skipped: more than {MaxTriangles:N0} coarse triangles";
                Debug.LogWarning(Diagnostics);return;
            }
            if(count==0){ClearVolume();Diagnostics="No supported station triangles";return;}
            Bounds bounds=renderers[0].bounds;
            foreach(var r in renderers)bounds.Encapsulate(r.bounds);
            int resolution=s.voxelGiResolution>=128?128:64;
            float width=Mathf.Max(25,Mathf.Max(bounds.size.x,Mathf.Max(bounds.size.y,bounds.size.z))+4);
            float voxel=width/(resolution-4);width=voxel*resolution;
            Vector3 center=bounds.center;
            center=new Vector3(Mathf.Round(center.x/voxel),Mathf.Round(center.y/voxel),Mathf.Round(center.z/voxel))*voxel;
            minimum=new Vector4(center.x-width*.5f,center.y-width*.5f,center.z-width*.5f,1/width);
            parameters=new Vector4(voxel,Mathf.Clamp(s.voxelGiDistance,5,80),1,0);
            grid=new Vector4(2f/resolution,Mathf.Log(resolution/2,2),0,0);
            // Build into a separate texture and publish only a completed submission.
            using(var solve=new SolveResources(resolution,count)) {
                UploadSources(solve.Triangles,count);
                Solve(s,solve,resolution,count);
                Release(ref radiance);radiance=solve.TakeRadiance();
            }
            BuildCount++;
            Diagnostics=$"{resolution}³, {voxel:0.###} m/voxel, {width:0.0} m bounds, {count:N0} coarse triangles; {refreshed}/{renderers.Count} sources refreshed; CPU build/submit {watch.Elapsed.TotalMilliseconds:0.0} ms; cached directional radiance {resolution*resolution*resolution*6*8.0/7/1048576:0.0} MiB; builds {BuildCount}";
        }

        void ClearVolume(){Release(ref radiance);if(Last==this)Last=null;}
        static void Release(ref RenderTexture texture){if(texture){texture.Release();CoreUtils.Destroy(texture);}texture=null;}
        public void Dispose()
        {
            ClearVolume();sourceCache.Clear();renderers.Clear();sources.Clear();
            sceneHandle=-1;seenRevision=seenSourceRevision=-1;
        }

        // Explicit lab-only synchronous readback; never part of the frame path.
        public Vector4[] Probe(Vector3[] points,Vector3 normal)
        {
            if(!IsReady)throw new InvalidOperationException("Cannot probe an unavailable voxel volume");
            if(points==null)throw new ArgumentNullException(nameof(points));
            if(points.Length==0)return Array.Empty<Vector4>();
            if(normal.sqrMagnitude<1e-8f)throw new ArgumentException("Probe normal must be nonzero",nameof(normal));
            var queries=new Vector4[points.Length*2];
            for(int i=0;i<points.Length;i++){queries[i*2]=points[i];queries[i*2+1]=normal;}
            using var input=new ComputeBuffer(queries.Length,16);input.SetData(queries);
            using var output=new ComputeBuffer(points.Length,16);
            int kernel=compute.FindKernel("Probe");compute.SetInt("_QueryCount",points.Length);
            BindGrid();compute.SetBuffer(kernel,"_Queries",input);compute.SetBuffer(kernel,"_ProbeResults",output);
            compute.SetTexture(kernel,RadianceId,radiance);compute.Dispatch(kernel,(points.Length+63)/64,1,1);
            var result=new Vector4[points.Length];output.GetData(result);return result;
        }
    }
}
