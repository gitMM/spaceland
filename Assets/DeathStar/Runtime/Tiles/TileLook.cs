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
            Shader.SetGlobalTexture("_TileMetalTex",MetalTexture);
            if(RenderSettings.skybox)ConfigureSky(RenderSettings.skybox);
        }
        public static void Restore(Saved s)
        {
            RenderSettings.ambientMode=s.mode;RenderSettings.ambientSkyColor=s.sky;RenderSettings.ambientEquatorColor=s.equator;
            RenderSettings.ambientGroundColor=s.ground;RenderSettings.ambientLight=s.flat;RenderSettings.ambientProbe=s.probe;RenderSettings.defaultReflectionMode=s.reflectionMode;
            RenderSettings.customReflectionTexture=s.reflection;RenderSettings.skybox=s.skybox;RenderSettings.fog=s.fog;RenderSettings.reflectionIntensity=s.reflectionIntensity;
        }

        // The painted metal base of the close-up surface (doc/close-up-detail-plan.md, layer 1): a tiling 1.5 m square,
        // low in contrast, so it supports the pixel-art look instead of fighting it. R: albedo around 0.5 (soft paint
        // patches, fine grain, sparse dark and pale specks; nothing directional, there is no down), G: roughness around
        // 0.5, B and A: a faint normal (x, y).
        // Linear, built once; TileSurface samples it with hashed tiling so the repeat doesn't show.
        static Texture2D metal;
        public static Texture2D MetalTexture
        {
            get
            {
                if(metal)return metal;
                const int N=256;
                metal=new Texture2D(N,N,TextureFormat.RGBA32,true,true){name="Tile Metal",wrapMode=TextureWrapMode.Repeat,filterMode=FilterMode.Trilinear,anisoLevel=8,hideFlags=HideFlags.HideAndDontSave};
                float Hash(int x,int y,int k){uint h=(uint)(x&(N-1))*73856093u^(uint)(y&(N-1))*19349663u^(uint)k*83492791u;h^=h>>13;h*=0x5bd1e995u;h^=h>>15;return (h&0xffff)/65535f;}
                // Value noise with period N on a lattice of `cells` cells.
                float Noise(float x,float y,int cells,int k)
                {
                    float s=N/(float)cells;x/=s;y/=s;int ix=Mathf.FloorToInt(x),iy=Mathf.FloorToInt(y);float fx=x-ix,fy=y-iy;
                    fx=fx*fx*(3-2*fx);fy=fy*fy*(3-2*fy);
                    float H(int a,int b)=>Hash(((a%cells)+cells)%cells*(N/cells),((b%cells)+cells)%cells*(N/cells),k);
                    return Mathf.Lerp(Mathf.Lerp(H(ix,iy),H(ix+1,iy),fx),Mathf.Lerp(H(ix,iy+1),H(ix+1,iy+1),fx),fy);
                }
                var height=new float[N*N];var px=new Color32[N*N];
                for(int y=0;y<N;y++)for(int x=0;x<N;x++)
                {
                    float blotch=Noise(x,y,4,1)*0.55f+Noise(x,y,8,2)*0.3f+Noise(x,y,16,3)*0.15f;
                    height[y*N+x]=blotch;
                }
                for(int y=0;y<N;y++)for(int x=0;x<N;x++)
                {
                    int i=y*N+x;
                    float blotch=Noise(x,y,4,1)*0.55f+Noise(x,y,8,2)*0.3f+Noise(x,y,16,3)*0.15f,grain=Noise(x,y,64,6);
                    float h=Hash(x,y,5),dark=h>0.988f?1:0,pale=h<0.006f?1:0;
                    float albedo=0.5f+0.14f*(blotch-0.5f)+0.05f*(grain-0.5f)-0.16f*dark+0.08f*pale;
                    float rough=0.5f+0.35f*(Noise(x,y,8,7)-0.5f)+0.1f*dark;
                    float dx=height[y*N+((x+1)&(N-1))]-height[y*N+((x+N-1)&(N-1))],dy=height[((y+1)&(N-1))*N+x]-height[((y+N-1)&(N-1))*N+x];
                    var n=new Vector3(-dx*4,-dy*4,1).normalized;
                    px[i]=new Color32(B(albedo),B(rough),B(0.5f+0.5f*n.x),B(0.5f+0.5f*n.y));
                }
                metal.SetPixels32(px);metal.Apply(true,true);
                return metal;
                static byte B(float v)=>(byte)Mathf.Clamp(Mathf.RoundToInt(v*255),0,255);
            }
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
