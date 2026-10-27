using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DeathStar.EditorTools
{
    public static class DeathStarMenu
    {
        public const string Root="Assets/DeathStar";
        public const string ScenePath=Root+"/Scenes/DeathStar.unity";

        [MenuItem("Death Star/Open Experiment")]
        public static void Open()
        {
            if(EditorApplication.isPlaying) {Debug.LogWarning("Stop Play mode before opening the Death Star scene.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(!File.Exists(ScenePath))CreateScene();
            EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        }

        [MenuItem("Death Star/Create Experiment Scene")]
        public static void CreateScene()
        {
            if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Cannot create assets or replace scenes during Play mode.");
            if(File.Exists(ScenePath)){Debug.Log("Death Star scene already exists: "+ScenePath);return;}
            Directory.CreateDirectory(Root+"/Scenes");Directory.CreateDirectory(Root+"/Materials");Directory.CreateDirectory(Root+"/Settings");
            var shader=Shader.Find("DeathStar/Industrial Surface");
            if(shader==null)throw new System.InvalidOperationException("Death Star surface shader has not imported.");
            var compute=AssetDatabase.LoadAssetAtPath<ComputeShader>(Root+"/Shaders/PlanetGeometry.compute");
            if(compute==null)throw new System.InvalidOperationException("Death Star compute shader has not imported.");
            string matPath=Root+"/Materials/IndustrialSurface.mat";
            var material=AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if(material==null){material=new Material(shader){name="Industrial Surface"};AssetDatabase.CreateAsset(material,matPath);}
            string postPath=Root+"/Settings/DeathStarPost.asset";
            var post=AssetDatabase.LoadAssetAtPath<VolumeProfile>(postPath);
            if(post==null)
            {
                post=ScriptableObject.CreateInstance<VolumeProfile>();AssetDatabase.CreateAsset(post,postPath);
                var tone=post.Add<Tonemapping>(true);tone.mode.Override(TonemappingMode.ACES);
                var grade=post.Add<ColorAdjustments>(true);grade.postExposure.Override(0.7f);grade.contrast.Override(12);grade.saturation.Override(-20);
                var bloom=post.Add<Bloom>(true);bloom.threshold.Override(1.1f);bloom.intensity.Override(0.12f);
                var vignette=post.Add<Vignette>(true);vignette.intensity.Override(0.17f);vignette.smoothness.Override(0.55f);
                foreach(var component in post.components){component.hideFlags=HideFlags.HideInInspector|HideFlags.HideInHierarchy;AssetDatabase.AddObjectToAsset(component,post);}
            }
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var cameraObject=new GameObject("Death Star Orbit Camera"){tag="MainCamera"};
                var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=43;camera.clearFlags=CameraClearFlags.SolidColor;
                camera.backgroundColor=new Color(0.008f,0.012f,0.021f);camera.nearClipPlane=0.3f;camera.farClipPlane=1800;
                camera.transform.position=new Vector3(260,200,490);camera.transform.LookAt(Vector3.zero);
                var data=camera.GetUniversalAdditionalCameraData();data.renderPostProcessing=true;
                data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
                cameraObject.AddComponent<AudioListener>();
                var volume=new GameObject("Death Star Grading").AddComponent<Volume>();volume.isGlobal=true;volume.sharedProfile=post;
                var experiment=new GameObject("Death Star Experiment").AddComponent<DeathStarExperiment>();
                experiment.geometry=compute;experiment.surface=material;experiment.viewer=camera;
                RenderSettings.skybox=null;RenderSettings.fog=false;
                RenderSettings.ambientMode=AmbientMode.Flat;RenderSettings.ambientLight=new Color(0.12f,0.14f,0.18f);
                EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            }
            finally
            {
                if(previous.IsValid() && previous.isLoaded)SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);
            }
            Debug.Log("Created isolated Death Star experiment: "+ScenePath);
        }

        [MenuItem("Death Star/Validate Topology and GPU Grammar")]
        public static void Validate()=>Debug.Log(DeathStarValidation.Run());
    }
}
