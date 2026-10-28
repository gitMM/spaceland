using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;

namespace DeathStar.EditorTools
{
    public static class StationLightingMenu
    {
        [MenuItem("Death Star/Lighting settings")]
        public static void SelectSettings() {Selection.activeObject=StationLightingSettings.Current;EditorGUIUtility.PingObject(Selection.activeObject);}

        [MenuItem("Death Star/Install unified lighting")]
        public static void Install() {
            if(EditorApplication.isPlaying)throw new System.InvalidOperationException("Install lighting in Edit mode.");
            const string settings="Assets/DeathStar/Resources/StationLighting/MainLighting.asset";
            if(!AssetDatabase.LoadAssetAtPath<StationLightingSettings>(settings))
                AssetDatabase.CreateAsset(ScriptableObject.CreateInstance<StationLightingSettings>(),settings);
            StationLightingSettings.Reload();
            const string rendererPath="Assets/DeathStar/Settings/StationRenderer.asset";
            const string pipelinePath="Assets/DeathStar/Settings/StationPipeline.asset";
            if(!AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath))AssetDatabase.CopyAsset("Assets/Settings/PC_Renderer.asset",rendererPath);
            var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>(rendererPath);
            RemoveUnitySsao(renderer);
            StationScreenGiFeature gi=null;
            foreach(var feature in renderer.rendererFeatures)if(feature is StationScreenGiFeature found)gi=found;
            if(!gi){gi=ScriptableObject.CreateInstance<StationScreenGiFeature>();gi.name="Station bounce and occlusion";AssetDatabase.AddObjectToAsset(gi,renderer);renderer.rendererFeatures.Add(gi);}
            gi.SetActive(true);SyncFeatureMap(renderer);renderer.SetDirty();EditorUtility.SetDirty(renderer);
            bool newPipeline=!AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if(newPipeline)AssetDatabase.CopyAsset("Assets/Settings/PC_RPAsset.asset",pipelinePath);
            var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(pipelinePath);
            if(newPipeline) {
                // Reserve detail for building-distance views without dropping
                // shadows on the rest of the station when the camera pulls back.
                pipeline.mainLightShadowmapResolution=4096;
                pipeline.shadowDistance=600;
                pipeline.shadowCascadeCount=4;
                pipeline.cascade4Split=new Vector3(.025f,.1f,.3f);
                EditorUtility.SetDirty(pipeline);
            }
            var serialized=new SerializedObject(pipeline);serialized.FindProperty("m_RendererDataList").GetArrayElementAtIndex(0).objectReferenceValue=renderer;serialized.ApplyModifiedPropertiesWithoutUndo();
            GraphicsSettings.defaultRenderPipeline=pipeline;
            QualitySettings.renderPipeline=pipeline;
            AssetDatabase.SaveAssets();
            SelectSettings();
        }

        // Delete the built-in feature and its subasset, including disabled or
        // orphaned copies. Keep the station's profile-controlled SSILVB feature.
        public static int RemoveUnitySsao(ScriptableRendererData renderer) {
            string path=AssetDatabase.GetAssetPath(renderer);
            var obsolete=AssetDatabase.LoadAllAssetsAtPath(path).OfType<ScriptableRendererFeature>()
                .Where(f=>f.GetType().FullName=="UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion").ToArray();
            foreach(var feature in obsolete){renderer.rendererFeatures.Remove(feature);Object.DestroyImmediate(feature,true);}
            SyncFeatureMap(renderer);
            if(obsolete.Length>0){renderer.SetDirty();EditorUtility.SetDirty(renderer);}
            return obsolete.Length;
        }
        static void SyncFeatureMap(ScriptableRendererData renderer) {
            var serialized=new SerializedObject(renderer);var map=serialized.FindProperty("m_RendererFeatureMap");
            map.arraySize=renderer.rendererFeatures.Count;
            for(int i=0;i<renderer.rendererFeatures.Count;i++) {
                long id=0;var feature=renderer.rendererFeatures[i];
                if(feature)AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature,out string _,out id);
                map.GetArrayElementAtIndex(i).longValue=id;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
