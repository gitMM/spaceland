using System;using System.Linq;using System.IO;using System.Collections.Generic;
using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;
using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine.SceneManagement;
using DeathStar;using DeathStar.EditorTools;
public static class VerifyUnifiedLighting {
 public static string RemoveSsao() {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  int removed=0;
  foreach(string guid in AssetDatabase.FindAssets("t:ScriptableRendererData")) {
   var data=AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(AssetDatabase.GUIDToAssetPath(guid));
   removed+=StationLightingMenu.RemoveUnitySsao(data);
  }
  AssetDatabase.SaveAssets();return "Removed "+removed+" Unity SSAO feature subassets";
 }
 public static string Run() {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var checks=new List<string>();void Check(bool ok,string label){if(!ok)throw new Exception(label);checks.Add("PASS "+label);}
  var s=StationLightingSettings.Current;string saved=JsonUtility.ToJson(s);
  var lib=AssetDatabase.LoadAssetAtPath<TileLibrary>("Assets/DeathStar/Settings/TileLibrary.asset");
  var environment=TileLook.ApplyEnvironment(lib);var scene=EditorSceneManager.NewPreviewScene();
  var profiles=new List<VolumeProfile>();
  try {
   var pipelines=AssetDatabase.FindAssets("t:ScriptableRendererData").Select(g=>AssetDatabase.GUIDToAssetPath(g)).ToArray();
   Check(pipelines.All(p=>!AssetDatabase.LoadAllAssetsAtPath(p).Any(o=>o&&o.GetType().FullName=="UnityEngine.Rendering.Universal.ScreenSpaceAmbientOcclusion")),"No Unity SSAO feature remains in project renderer assets");
   var renderer=AssetDatabase.LoadAssetAtPath<UniversalRendererData>("Assets/DeathStar/Settings/StationRenderer.asset");
   Check(renderer.rendererFeatures.Count==1&&renderer.rendererFeatures[0] is StationScreenGiFeature&&renderer.rendererFeatures[0].isActive,"Station renderer has only the active custom SSILVB feature");
   Check(AssetDatabase.GetAssetPath(GraphicsSettings.currentRenderPipeline)=="Assets/DeathStar/Settings/StationPipeline.asset","Active quality uses the unified station pipeline");
   var rigs=new List<StationLightingRig>();
   foreach(string name in new[]{"Foundry","Gallery","Lab"}) {
    var root=new GameObject(name);SceneManager.MoveGameObjectToScene(root,scene);
    var lightRoot=new GameObject("Lights");lightRoot.transform.SetParent(root.transform,false);TileLook.AddLights(lightRoot.transform);
    var rig=lightRoot.GetComponent<StationLightingRig>();rig.Apply();rigs.Add(rig);
    // Deliberately create this after the first Apply: the previous cached array missed it.
    var volume=root.AddComponent<Volume>();var profile=ScriptableObject.CreateInstance<VolumeProfile>();profiles.Add(profile);
    profile.Add<ColorAdjustments>();profile.Add<Bloom>();volume.sharedProfile=profile;
   }
   s.sunFrom=new Vector3(.7f,.2f,-.4f);s.sunIntensity=2.3f;s.sunColor=new Color(.8f,.7f,.6f);
   s.planetIntensity=.37f;s.planetColor=new Color(.2f,.4f,.8f);s.ambientColor=new Color(.04f,.05f,.06f);s.reflections=.42f;
   s.exposure=.8f;s.contrast=9;s.saturation=-7;s.bloom=.23f;s.bloomThreshold=1.6f;s.bloomScatter=.4f;
   s.shadowSoftness=1.6f;s.receivedShadowStrength=.83f;s.ssaoStrength=.6f;s.ssaoDirectStrength=.2f;s.emission=.7f;
   foreach(var rig in rigs)rig.Apply();
   Check(rigs.All(r=>Mathf.Approximately(r.sun.intensity,2.3f)&&r.sun.color==s.sunColor&&Vector3.Dot(-r.sun.transform.forward,s.sunFrom.normalized)>.999f),"All three rigs follow shared sun direction, color and intensity");
   Check(rigs.All(r=>Mathf.Approximately(r.planet.intensity,.37f)&&r.planet.color==s.planetColor),"All three rigs follow shared planet lighting");
   Check(profiles.All(p=>p.TryGet<ColorAdjustments>(out var g)&&Mathf.Approximately(g.postExposure.value,.8f)&&Mathf.Approximately(g.contrast.value,9)&&Mathf.Approximately(g.saturation.value,-7)),"Late-created volumes follow shared exposure, contrast and saturation");
   Check(profiles.All(p=>p.TryGet<Bloom>(out var b)&&Mathf.Approximately(b.intensity.value,.23f)&&Mathf.Approximately(b.threshold.value,1.6f)&&Mathf.Approximately(b.scatter.value,.4f)),"All three rigs follow shared bloom controls");
   Check(Mathf.Approximately(RenderSettings.reflectionIntensity,.42f)&&RenderSettings.customReflectionTexture==s.Reflection&&(RenderSettings.ambientLight-s.ambientColor.gamma).maxColorComponent<.001f,"Ambient and reflections follow the same profile");
   Check(Vector3.Distance((Vector3)lib.sky.GetVector("_SunDir"),s.sunFrom.normalized)<.001f,"Sky direction follows the sun");
   Check(Mathf.Approximately(Shader.GetGlobalVector("_StationDirectLighting").w,1.6f)&&Mathf.Approximately(Shader.GetGlobalVector("_StationScreenOcclusion").x,.6f)&&Mathf.Approximately(Shader.GetGlobalFloat("_StationEmission"),.7f),"Shared shadow, custom AO and emission globals update");
   foreach(var name in new[]{"DeathStar/Tile Surface","DeathStar/Tile Glass","DeathStar/Space Sky"})Check(!ShaderUtil.ShaderHasError(Shader.Find(name)),name+" shader has no errors");
   string result=string.Join("\n",checks);Directory.CreateDirectory("DeathStarRuns/Lighting");File.WriteAllText("DeathStarRuns/Lighting/unified-checks.txt",result);return result;
  }finally {
   EditorSceneManager.ClosePreviewScene(scene);foreach(var p in profiles){foreach(var c in p.components)UnityEngine.Object.DestroyImmediate(c);UnityEngine.Object.DestroyImmediate(p);}
   JsonUtility.FromJsonOverwrite(saved,s);s.ApplyGlobals();s.ApplyPost(lib.post);TileLook.ConfigureSky(lib.sky);TileLook.Restore(environment);
  }
 }
}
