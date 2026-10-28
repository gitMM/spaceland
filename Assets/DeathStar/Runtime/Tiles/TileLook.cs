using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    // Materials, lights and environment of the tile gallery, shared by the scene and the headless lab so both look
    // the same. Colours here are linear.
    public static class TileLook
    {
        // Space: one hard sun and a planet. Nothing here assumes an up; directions are just station-space vectors.
        public static Vector3 KeyFrom=>StationLightingSettings.Current.sunFrom.normalized;
        public static Color KeyColor=>StationLightingSettings.Current.sunColor;
        public static float KeyIntensity=>StationLightingSettings.Current.sunIntensity;
        // Planetshine: weak, cool, from the planet's side of the sky.
        public static Vector3 RimFrom=>StationLightingSettings.Current.planetFrom.normalized;
        public static Color RimColor=>StationLightingSettings.Current.planetColor;
        public static float RimIntensity=>StationLightingSettings.Current.planetIntensity;
        static readonly Color EnvDark=new Color(0.03f,0.032f,0.036f);

        // Planning-overlay colours (linear). Hull stays plain grey.
        // Foundry shape view: grey box, red box with an edge under 3.2 m (its parts take smaller size classes), orange
        // wedge, magenta apex.
        public static Color ShapeColor(in ShapeInfo s)=>s.kind switch
        {
            CellShape.Wedge=>new Color(1f,0.5f,0.1f),
            CellShape.Apex=>new Color(1f,0.2f,0.85f),
            _=>s.Small?new Color(0.95f,0.12f,0.12f):new Color(0.44f,0.44f,0.44f)
        };
        public static Color FamilyColor(TileFamily f)=>f switch
        {
            TileFamily.Habitat=>new Color(1f,0.72f,0.22f),
            TileFamily.Industrial=>new Color(1f,0.38f,0.12f),
            TileFamily.Power=>new Color(0.95f,0.16f,0.16f),
            TileFamily.LifeSupport=>new Color(0.3f,0.85f,0.3f),
            TileFamily.Storage=>new Color(0.2f,0.75f,0.8f),
            TileFamily.Defense=>new Color(0.72f,0.3f,1f),
            TileFamily.Command=>new Color(0.25f,0.45f,1f),
            TileFamily.Corridor=>new Color(1f,0.45f,0.75f),
            TileFamily.Pad=>new Color(0.6f,1f,0.12f),
            TileFamily.Conduit=>new Color(0.55f,0.32f,0.18f),
            TileFamily.Truss=>new Color(1f,0.95f,0.15f),
            _=>Color.clear
        };

        public static void Configure(Material m,TileSlot slot)
        {
            switch(slot)
            {
                case TileSlot.Paint:Surface(m,new Color(0.36f,0.36f,0.365f),0f,0.28f,Color.black);break;
                case TileSlot.Trim:Surface(m,new Color(0.2f,0.205f,0.21f),0.05f,0.34f,Color.black);break;
                case TileSlot.Dark:Surface(m,new Color(0.055f,0.057f,0.06f),0.1f,0.35f,Color.black);break;
                case TileSlot.Metal:Surface(m,new Color(0.6f,0.6f,0.62f),0.9f,0.55f,Color.black);break;
                case TileSlot.Recess:Surface(m,new Color(0.006f,0.006f,0.007f),0f,0.15f,Color.black);break;
                case TileSlot.Light:Surface(m,new Color(0.01f,0.01f,0.01f),0f,0.5f,new Color(5f,5f,5f));break;
                case TileSlot.Accent:Surface(m,new Color(1f,1f,1f),0f,0.32f,Color.black);break;
                case TileSlot.Glass:
                    m.SetVector("_GlassTint",new Vector4(0.46f,0.5f,0.52f,1));
                    m.SetVector("_RoomColor",new Vector4(1f,0.76f,0.52f,1));
                    m.SetFloat("_RoomLight",3.4f);m.SetFloat("_RoomDepth",2.8f);m.SetFloat("_Reflection",1f);
                    break;
            }
        }
        static void Surface(Material m,Color baseColor,float metallic,float smoothness,Color emission)
        {
            m.SetVector("_BaseColor",(Vector4)baseColor);m.SetFloat("_Metallic",metallic);m.SetFloat("_Smoothness",smoothness);
            m.SetVector("_Emission",(Vector4)emission);m.SetFloat("_PatternStrength",1f);
        }
        public static void ConfigureSky(Material m)
        {
            m.SetVector("_Zenith",new Vector4(0.004f,0.005f,0.009f,1));m.SetVector("_Nadir",new Vector4(0.004f,0.005f,0.009f,1));
            m.SetVector("_Band",new Vector4(0.03f,0.034f,0.06f,1));m.SetFloat("_Stars",1.4f);
            m.SetVector("_SunDir",KeyFrom);m.SetVector("_PlanetDir",RimFrom);m.SetFloat("_PlanetSize",Mathf.Sin(24f*Mathf.Deg2Rad));
            m.SetVector("_PlanetColor",new Vector4(0.32f,0.36f,0.42f,1));m.SetVector("_Atmosphere",new Vector4(0.25f,0.5f,1.1f,1));
        }

        public static Light AddLights(Transform parent)
        {
            var key=new GameObject("Key Light").AddComponent<Light>();key.transform.SetParent(parent,false);
            key.type=LightType.Directional;key.color=KeyColor;key.intensity=KeyIntensity;
            key.transform.rotation=Quaternion.LookRotation(-KeyFrom);
            key.shadows=LightShadows.Soft;key.shadowStrength=1f;
            var rim=new GameObject("Rim Light").AddComponent<Light>();rim.transform.SetParent(parent,false);
            rim.type=LightType.Directional;rim.color=RimColor;rim.intensity=RimIntensity;
            rim.transform.rotation=Quaternion.LookRotation(-RimFrom);rim.shadows=LightShadows.None;
            var rig=parent.gameObject.AddComponent<StationLightingRig>();rig.sun=key;rig.planet=rim;rig.Apply();
            return key;
        }

        public struct Saved
        {
            public AmbientMode mode;public Color sky,equator,ground,flat;public SphericalHarmonicsL2 probe;
            public DefaultReflectionMode reflectionMode;public Texture reflection;public Material skybox;public bool fog;public float reflectionIntensity;
        }
        public static Saved ApplyEnvironment(TileLibrary library)
        {
            var saved=new Saved{mode=RenderSettings.ambientMode,sky=RenderSettings.ambientSkyColor,equator=RenderSettings.ambientEquatorColor,
                ground=RenderSettings.ambientGroundColor,flat=RenderSettings.ambientLight,probe=RenderSettings.ambientProbe,reflectionMode=RenderSettings.defaultReflectionMode,
                reflection=RenderSettings.customReflectionTexture,skybox=RenderSettings.skybox,fog=RenderSettings.fog,reflectionIntensity=RenderSettings.reflectionIntensity};
            if(library!=null){StationLightingSettings.Current.ApplyPost(library.post);RenderSettings.skybox=library.sky;}
            ApplyLightingEnvironment();
            RenderSettings.fog=false;
            return saved;
        }
        public static void ApplyLightingEnvironment()
        {
            var settings=StationLightingSettings.Current;settings.ApplyGlobals();
            RenderSettings.reflectionIntensity=settings.reflections;
            RenderSettings.ambientMode=AmbientMode.Flat;
            RenderSettings.ambientLight=settings.ambientColor.gamma;
            var probe=new SphericalHarmonicsL2();probe.AddAmbientLight(settings.ambientColor);
            RenderSettings.ambientProbe=probe;
            RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;RenderSettings.customReflectionTexture=settings.Reflection;
            if(RenderSettings.skybox)ConfigureSky(RenderSettings.skybox);
        }
        public static void Restore(Saved s)
        {
            RenderSettings.ambientMode=s.mode;RenderSettings.ambientSkyColor=s.sky;RenderSettings.ambientEquatorColor=s.equator;
            RenderSettings.ambientGroundColor=s.ground;RenderSettings.ambientLight=s.flat;RenderSettings.ambientProbe=s.probe;RenderSettings.defaultReflectionMode=s.reflectionMode;
            RenderSettings.customReflectionTexture=s.reflection;RenderSettings.skybox=s.skybox;RenderSettings.fog=s.fog;RenderSettings.reflectionIntensity=s.reflectionIntensity;
        }

        // Dark environment for reflections, with a hot spot where the sun is and a glow where the planet is.
        public static Cubemap MakeReflection(int size=64)
        {
            var cube=new Cubemap(size,TextureFormat.RGBAHalf,true){name="Tile Reflection"};
            var px=new Color[size*size];
            for(int f=0;f<6;f++)
            {
                for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                    px[y*size+x]=Environment(Direction((CubemapFace)f,(x+0.5f)/size*2f-1f,(y+0.5f)/size*2f-1f));
                cube.SetPixels(px,(CubemapFace)f);
            }
            cube.Apply(true);
            return cube;
        }
        static Color Environment(Vector3 d)
        {
            Color c=EnvDark;
            c+=KeyColor*(2.2f*KeyIntensity/3.6f)*Mathf.Pow(Mathf.Max(0f,Vector3.Dot(d,KeyFrom)),40f);
            c+=RimColor*(0.35f*RimIntensity/0.6f)*Mathf.Pow(Mathf.Max(0f,Vector3.Dot(d,RimFrom)),6f);
            c.a=1;return c;
        }
        static Vector3 Direction(CubemapFace f,float u,float v)
        {
            switch(f)
            {
                case CubemapFace.PositiveX:return new Vector3(1f,-v,-u).normalized;
                case CubemapFace.NegativeX:return new Vector3(-1f,-v,u).normalized;
                case CubemapFace.PositiveY:return new Vector3(u,1f,v).normalized;
                case CubemapFace.NegativeY:return new Vector3(u,-1f,-v).normalized;
                case CubemapFace.PositiveZ:return new Vector3(u,-v,1f).normalized;
                default:return new Vector3(-u,-v,-1f).normalized;
            }
        }
    }
}
