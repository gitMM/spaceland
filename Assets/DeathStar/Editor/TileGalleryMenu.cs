using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DeathStar.EditorTools
{
    public static class TileGalleryMenu
    {
        public const string ScenePath="Assets/DeathStar/Scenes/TileGallery.unity";
        const string MaterialDir="Assets/DeathStar/Materials/Tiles",SettingsDir="Assets/DeathStar/Settings";
        const string LibraryPath=SettingsDir+"/TileLibrary.asset",ReflectionPath=SettingsDir+"/TileReflection.asset",PostPath=SettingsDir+"/TileGalleryPost.asset";

        [MenuItem("Death Star/Open Tile Gallery",false,20)]
        public static void Open()
        {
            if(EditorApplication.isPlaying){Debug.LogWarning("Stop Play mode before opening the Tile Gallery.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            CreateScene(EnsureLibrary());EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        }

        [MenuItem("Death Star/Run Tile Lab",false,21)]
        public static void RunLab()=>Debug.Log(TileLab.Run("report"));

        // Creates the tile materials, sky, reflection and grading if missing, and sets them from TileLook every time so
        // the code stays the single source of the look.
        public static TileLibrary EnsureLibrary()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Cannot update tile assets in Play mode.");
            Directory.CreateDirectory(MaterialDir);Directory.CreateDirectory(SettingsDir);
            var lib=AssetDatabase.LoadAssetAtPath<TileLibrary>(LibraryPath);
            if(lib==null){lib=ScriptableObject.CreateInstance<TileLibrary>();AssetDatabase.CreateAsset(lib,LibraryPath);}
            if(lib.materials==null||lib.materials.Length!=TileMesh.SlotCount)lib.materials=new Material[TileMesh.SlotCount];
            for(int i=0;i<TileMesh.SlotCount;i++)
            {
                var slot=(TileSlot)i;
                var m=Material($"{MaterialDir}/Tile{slot}.mat",slot==TileSlot.Glass?"DeathStar/Tile Glass":"DeathStar/Tile Surface");
                TileLook.Configure(m,slot);EditorUtility.SetDirty(m);lib.materials[i]=m;
            }
            lib.sky=Material(MaterialDir+"/TileSky.mat","DeathStar/Space Sky");TileLook.ConfigureSky(lib.sky);EditorUtility.SetDirty(lib.sky);
            var cube=TileLook.MakeReflection();
            var reflection=AssetDatabase.LoadAssetAtPath<Cubemap>(ReflectionPath);
            if(reflection==null){AssetDatabase.CreateAsset(cube,ReflectionPath);reflection=cube;}
            else{EditorUtility.CopySerialized(cube,reflection);UnityEngine.Object.DestroyImmediate(cube);EditorUtility.SetDirty(reflection);}
            lib.reflection=reflection;
            var post=AssetDatabase.LoadAssetAtPath<VolumeProfile>(PostPath);
            if(post==null){post=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(post,PostPath);}
            ConfigurePost(post);lib.post=post;
            EditorUtility.SetDirty(lib);AssetDatabase.SaveAssets();
            return lib;
        }

        static void ConfigurePost(VolumeProfile p)
        {
            T Get<T>() where T:VolumeComponent
            {
                if(p.TryGet<T>(out var c))return c;
                c=p.Add<T>(true);c.hideFlags=HideFlags.HideInInspector|HideFlags.HideInHierarchy;AssetDatabase.AddObjectToAsset(c,p);
                return c;
            }
            Get<Tonemapping>().mode.Override(TonemappingMode.ACES);
            var grade=Get<ColorAdjustments>();grade.postExposure.Override(0.2f);grade.contrast.Override(14f);grade.saturation.Override(-4f);
            var bloom=Get<Bloom>();bloom.threshold.Override(1.0f);bloom.intensity.Override(0.35f);bloom.scatter.Override(0.65f);
            var vignette=Get<Vignette>();vignette.intensity.Override(0.24f);vignette.smoothness.Override(0.45f);
            StationLightingSettings.Current.ApplyPost(p);
            foreach(var c in p.components)EditorUtility.SetDirty(c);
            EditorUtility.SetDirty(p);
        }

        static Material Material(string path,string shaderName)
        {
            var shader=Shader.Find(shaderName);
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader unavailable: "+shaderName);
            var m=AssetDatabase.LoadAssetAtPath<Material>(path);
            if(m==null){m=new Material(shader);AssetDatabase.CreateAsset(m,path);}
            else if(m.shader!=shader)m.shader=shader;
            return m;
        }

        public static void SetUpCamera(Camera camera,TileLibrary lib)
        {
            camera.fieldOfView=38;camera.nearClipPlane=0.1f;camera.farClipPlane=260;
            camera.clearFlags=CameraClearFlags.Skybox;
            var sky=camera.GetComponent<Skybox>();if(sky==null)sky=camera.gameObject.AddComponent<Skybox>();sky.material=lib.sky;
            var data=camera.GetUniversalAdditionalCameraData();
            data.renderPostProcessing=true;data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality=AntialiasingQuality.High;
        }

        public static void CreateScene(TileLibrary lib)
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Cannot create scene assets in Play mode.");
            if(File.Exists(ScenePath))return;
            Directory.CreateDirectory("Assets/DeathStar/Scenes");
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var cameraObject=new GameObject("Tile Gallery Camera"){tag="MainCamera"};
                var camera=cameraObject.AddComponent<Camera>();SetUpCamera(camera,lib);
                var v=TileGallery.Fragment;
                camera.transform.SetPositionAndRotation(v.position,Quaternion.LookRotation(v.target-v.position,v.up));
                cameraObject.AddComponent<AudioListener>();
                var gallery=new GameObject("Tile Gallery").AddComponent<TileGallery>();
                gallery.enabled=false;gallery.library=lib;gallery.viewer=camera;gallery.enabled=true;
                TileLook.ApplyEnvironment(lib);
                EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            }
            finally
            {
                if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);
            }
        }
    }
}
