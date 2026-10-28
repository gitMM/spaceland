using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    public enum GiDebugView { Normal, ScreenSpaceGiOnly, ScreenSpaceAoOnly, DiffuseSources, VoxelGiOnly=5 }

    [CreateAssetMenu(menuName="Death Star/Lighting settings")]
    public sealed class StationLightingSettings : ScriptableObject
    {
        static StationLightingSettings current;
        public static void Reload() { current=Resources.Load<StationLightingSettings>("StationLighting/MainLighting"); }
        public static StationLightingSettings Current {
            get {
                if(!current) current=Resources.Load<StationLightingSettings>("StationLighting/MainLighting");
                if(!current) {current=CreateInstance<StationLightingSettings>();current.hideFlags=HideFlags.HideAndDontSave;}
                return current;
            }
        }
        [Header("Sun")]
        public Vector3 sunFrom=new Vector3(-.62f,.55f,-.56f);
        public Color sunColor=new Color(1,.97f,.92f);
        [Min(0)] public float sunIntensity=3.6f;
        [Range(0,1)] public float receivedShadowStrength=1;
        [Range(1,3)] public float shadowSoftness=1.25f;
        [Header("Planet")]
        public Vector3 planetFrom=new Vector3(.55f,-.38f,.74f);
        public Color planetColor=new Color(.45f,.62f,.95f);
        [Min(0)] public float planetIntensity=.6f;
        [Header("Ambient (linear)")]
        public bool ambientLight=true;
        public Color ambientColor=new Color(.07f,.072f,.078f);
        [Header("Habitat paint")]
        public bool habitatTintEnabled=true;
        [Tooltip("Physical diffuse tint, also used by screen-space bounce sources.")]
        public Color habitatTint=new Color(1f,.045f,.16f);
        [Header("World-space voxel bounce")]
        public bool voxelGi=true;
        [Range(0,2)] public float voxelGiStrength=1;
        [Range(5,80),Tooltip("World-space reach in metres, including off-screen geometry.")]
        public float voxelGiDistance=40;
        [Tooltip("64 or 128. Bounds fit the station; the full gallery uses about 1.5 m voxels at 128. Cached between construction/light changes.")]
        public int voxelGiResolution=128;
        public bool voxelGiSecondBounce=true;
        [Header("Screen-space bounce")]
        public bool screenSpaceGi=true;
        [InspectorName("Bounce Strength"),Range(0,1)] public float screenGiStrength=.5f;
        [InspectorName("Bounce Distance (m)"),Range(.1f,30)]
        [Tooltip("Maximum distance between a receiver and a visible bounce source. Try 5–10 m for nearby station walls. Hidden and off-screen surfaces cannot contribute.")]
        public float screenGiRadius=.8f;
        [InspectorName("Sampling Directions"),Range(2,12)]
        [Tooltip("More directions reduce directional gaps. Cost grows with directions × samples per side × 2. Try 6 directions with 24 samples for broader bounce.")]
        public int screenGiSlices=4;
        [InspectorName("Samples Per Side"),Range(4,32)]
        [Tooltip("Samples along each side of a direction. Increase when extending bounce distance to reduce sparse sampling. Higher values cost more GPU time.")]
        public int screenGiSliceSteps=8;
        [InspectorName("Assumed Surface Thickness (m)"),Range(.01f,3)]
        [Tooltip("Depth represented behind a visible surface. Controls occlusion and light leaks, not bounce distance. Keep thin armour thin; increasing this can over-darken gaps.")]
        public float screenGiSurfaceThickness=.12f;
        [Header("Occlusion")]
        public bool screenSpaceAmbientOcclusion=true;
        [InspectorName("Occlusion Strength"),Range(0,1)] public float ssaoStrength=1;
        [InspectorName("Direct Light Occlusion"),Range(0,1)] public float ssaoDirectStrength=.1f;
        [InspectorName("AO Distance (m)"),Range(.05f,5)]
        [Tooltip("Independent occlusion reach. Keep around 0.5–1 m for contacts while testing larger bounce distances. AO and bounce share the sampling budget.")]
        public float screenGiAoRadius=.3f;
        [InspectorName("AO Intensity"),Range(0,3)] public float screenGiAoIntensity=.6f;
        [Header("Materials and image")]
        [Min(0)] public float emission=1;
        [Min(0)] public float reflections=1;
        public float exposure=.2f;
        [Range(-100,100)] public float contrast=14, saturation=-4;
        [Min(0)] public float bloom=.35f;
        [Min(0)] public float bloomThreshold=1;
        [Range(0,1)] public float bloomScatter=.65f;
        [InspectorName("Lighting Debug View")]
        [Tooltip("Screen Space Gi Only isolates bounce; Screen Space Ao Only isolates occlusion. Return to Normal to see the combined lighting.")]
        public GiDebugView giDebugView;
        Cubemap environment;
        int environmentHash;
        public Cubemap Reflection {
            get {
                int hash=System.HashCode.Combine(sunFrom,sunColor,sunIntensity,planetFrom,planetColor,planetIntensity);
                if(!environment || hash!=environmentHash) {
                    if(environment) {if(Application.isPlaying)Destroy(environment);else DestroyImmediate(environment);}
                    environment=TileLook.MakeReflection();environment.hideFlags=HideFlags.HideAndDontSave;environmentHash=hash;
                }
                return environment;
            }
        }
        void OnDisable(){if(environment){if(Application.isPlaying)Destroy(environment);else DestroyImmediate(environment);}}
        void OnValidate(){
            #if UNITY_EDITOR
            UnityEditor.EditorApplication.QueuePlayerLoopUpdate();UnityEditor.SceneView.RepaintAll();
            #endif
        }
        public void ApplyGlobals() {
            Shader.SetGlobalFloat("_StationLightingControlled",1);
            Shader.SetGlobalVector("_StationDirectLighting",new Vector4(1,1,receivedShadowStrength,shadowSoftness));
            Shader.SetGlobalVector("_StationScreenOcclusion",new Vector4(screenSpaceAmbientOcclusion?ssaoStrength:0,ssaoDirectStrength,0,0));
            Shader.SetGlobalFloat("_StationAmbient",ambientLight?1:0);
            Shader.SetGlobalFloat("_StationEmission",emission);
            Shader.SetGlobalVector("_StationHabitatFamily",TileLook.FamilyColor(TileFamily.Habitat));
            Shader.SetGlobalVector("_StationHabitatTint",new Vector4(habitatTint.r,habitatTint.g,habitatTint.b,habitatTintEnabled?1:0));
        }
        public void ApplyPost(VolumeProfile p) {
            if(!p)return;
            if(p.TryGet<ColorAdjustments>(out var g)){g.postExposure.Override(exposure);g.contrast.Override(contrast);g.saturation.Override(saturation);}
            if(p.TryGet<Bloom>(out var b)){b.intensity.Override(bloom);b.threshold.Override(bloomThreshold);b.scatter.Override(bloomScatter);}
        }
    }
}
