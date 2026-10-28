using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace DeathStar
{
    public enum GiDebugView { Normal, ScreenSpaceGiOnly, ScreenSpaceAoOnly, DiffuseSources }

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
        [Header("Screen-space bounce")]
        public bool screenSpaceGi=true;
        [Range(0,1)] public float screenGiStrength=.5f;
        [Range(.1f,2)] public float screenGiRadius=.8f;
        [Range(2,8)] public int screenGiSlices=4;
        [Range(4,16)] public int screenGiSliceSteps=8;
        [Range(.01f,.5f)] public float screenGiSurfaceThickness=.12f;
        [Header("Occlusion")]
        public bool screenSpaceAmbientOcclusion=true;
        [InspectorName("Occlusion Strength"),Range(0,1)] public float ssaoStrength=1;
        [InspectorName("Direct Light Occlusion"),Range(0,1)] public float ssaoDirectStrength=.1f;
        [Range(.05f,1)] public float screenGiAoRadius=.3f;
        [Range(0,3)] public float screenGiAoIntensity=.6f;
        [Header("Materials and image")]
        [Min(0)] public float emission=1;
        [Min(0)] public float reflections=1;
        public float exposure=.2f;
        [Range(-100,100)] public float contrast=14, saturation=-4;
        [Min(0)] public float bloom=.35f;
        [Min(0)] public float bloomThreshold=1;
        [Range(0,1)] public float bloomScatter=.65f;
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
        }
        public void ApplyPost(VolumeProfile p) {
            if(!p)return;
            if(p.TryGet<ColorAdjustments>(out var g)){g.postExposure.Override(exposure);g.contrast.Override(contrast);g.saturation.Override(saturation);}
            if(p.TryGet<Bloom>(out var b)){b.intensity.Override(bloom);b.threshold.Override(bloomThreshold);b.scatter.Override(bloomScatter);}
        }
    }
}
