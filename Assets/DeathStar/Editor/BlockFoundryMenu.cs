using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace DeathStar.EditorTools
{
    public static class BlockFoundryMenu
    {
        public const string ScenePath = "Assets/DeathStar/Scenes/BlockFoundry.unity";

        [MenuItem("Death Star/Open Block Foundry",false,0)]
        public static void Open()
        {
            if(EditorApplication.isPlaying){Debug.LogWarning("Stop Play mode before opening Block Foundry.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            CreateScene();EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
        }

        [MenuItem("Death Star/Create Block Foundry Scene",false,1)]
        public static void CreateScene()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Cannot create scene assets in Play mode.");
            if(File.Exists(ScenePath))return;
            Directory.CreateDirectory("Assets/DeathStar/Scenes");
            Directory.CreateDirectory("Assets/DeathStar/Materials");
            Material hull=Material("Assets/DeathStar/Materials/BlockHull.mat","DeathStar/Block Hull");
            Material preview=Material("Assets/DeathStar/Materials/BlockPreview.mat","DeathStar/Block Preview");
            var previous=SceneManager.GetActiveScene();
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                var cameraObject=new GameObject("Block Foundry Camera"){tag="MainCamera"};
                var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=42;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(0.017f,0.026f,0.041f);
                camera.nearClipPlane=0.03f;camera.farClipPlane=1000;
                camera.transform.position=new Vector3(-4,3,-6);camera.transform.LookAt(Vector3.one*0.5f);
                var data=camera.GetUniversalAdditionalCameraData();
                data.antialiasing=AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                data.renderPostProcessing=true;data.requiresColorOption=CameraOverrideOption.Off;data.requiresDepthOption=CameraOverrideOption.Off;
                cameraObject.AddComponent<AudioListener>();
                var root=new GameObject("Block Foundry");
                var foundry=root.AddComponent<BlockFoundry>();foundry.viewer=camera;foundry.hullMaterial=hull;foundry.previewMaterial=preview;
                RenderSettings.skybox=null;RenderSettings.fog=false;
                EditorSceneManager.SaveScene(scene,ScenePath);AssetDatabase.SaveAssets();
            }
            finally
            {
                if(previous.IsValid()&&previous.isLoaded)SceneManager.SetActiveScene(previous);
                EditorSceneManager.CloseScene(scene,true);
            }
        }
        static Material Material(string path,string shaderName)
        {
            var material=AssetDatabase.LoadAssetAtPath<Material>(path);if(material!=null)return material;
            var shader=Shader.Find(shaderName);
            if(shader==null||ShaderUtil.ShaderHasError(shader))throw new InvalidOperationException("Shader unavailable: "+shaderName);
            material=new Material(shader);AssetDatabase.CreateAsset(material,path);return material;
        }
        [MenuItem("Death Star/Validate Block Foundry",false,2)]
        public static void Validate()=>Debug.Log(BlockFoundryValidation.Run());
    }
}
