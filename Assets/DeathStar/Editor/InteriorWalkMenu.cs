using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeathStar.EditorTools
{
    public static class InteriorWalkMenu
    {
        public const string ScenePath="Assets/DeathStar/Scenes/InteriorWalk.unity";

        // Opens the interior walk scene (made on first use: a camera and InteriorWalk); press Play to walk.
        [MenuItem("Death Star/Open Interior Walk",false,22)]
        public static void Open()
        {
            if(EditorApplication.isPlaying){Debug.LogWarning("Stop Play mode before opening the Interior Walk.");return;}
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            CreateScene(TileGalleryMenu.EnsureLibrary());EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Single);
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
                var cameraObject=new GameObject("Walker Camera"){tag="MainCamera"};
                var camera=cameraObject.AddComponent<Camera>();TileGalleryMenu.SetUpCamera(camera,lib);
                camera.fieldOfView=75;camera.nearClipPlane=0.05f;camera.farClipPlane=400;
                cameraObject.AddComponent<AudioListener>();
                var walk=new GameObject("Interior Walk").AddComponent<InteriorWalk>();
                walk.enabled=false;walk.library=lib;walk.viewer=camera;walk.enabled=true;
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
