using System;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Collections.Generic;
using UnityEngine;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;
using DeathStar;using DeathStar.EditorTools;
public static class VerifyScreenBounce {
 public static Task<string> Run()=>Capture("after");
 static async Task<string> Capture(string name) {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var s=StationLightingSettings.Current;var saved=JsonUtility.ToJson(s);var lib=AssetDatabase.LoadAssetAtPath<TileLibrary>("Assets/DeathStar/Settings/TileLibrary.asset");
  var env=TileLook.ApplyEnvironment(lib);var scene=EditorSceneManager.NewPreviewScene();var meshes=new List<Mesh>();bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
  try {
   s.sunFrom=new Vector3(1,1,0);s.sunIntensity=3;s.planetIntensity=0;s.screenSpaceGi=true;s.screenGiStrength=1;s.screenGiRadius=5;s.screenGiSlices=6;s.screenGiSliceSteps=24;s.giDebugView=GiDebugView.Normal;s.habitatTintEnabled=true;

   var root=new GameObject("Screen bounce regression");SceneManager.MoveGameObjectToScene(root,scene);
   var structure=new TileStructure();structure.Fill(new Vector3Int(0,0,0),new Vector3Int(1,0,1),TileFamily.Hull,Vector3Int.up,Vector3Int.right);
   structure.Fill(new Vector3Int(-1,0,0),new Vector3Int(-1,1,1),TileFamily.Habitat,Vector3Int.right,Vector3Int.up);
   var builder=new TileBuilder(structure);
   foreach(var cell in structure.cells.Keys){var go=new GameObject(cell.ToString());go.transform.SetParent(root.transform,false);go.transform.localPosition=TileStructure.Center(cell);var mesh=builder.Build(cell).ToMesh(cell.ToString());meshes.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=lib.materials;}
   var lights=new GameObject("Lights");lights.transform.SetParent(root.transform,false);TileLook.AddLights(lights.transform);
   var camGo=new GameObject("Camera");camGo.transform.SetParent(root.transform,false);var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.scene=scene;TileGalleryMenu.SetUpCamera(cam,lib);cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;cam.transform.position=new Vector3(19,15,-21);cam.transform.LookAt(new Vector3(0,2,2));
   var volume=root.AddComponent<UnityEngine.Rendering.Volume>();volume.isGlobal=true;volume.sharedProfile=lib.post;
   Directory.CreateDirectory("DeathStarRuns/BounceRepair");
   async Task<Color[]> Shot(string suffix){s.ApplyGlobals();s.ApplyPost(lib.post);lights.GetComponent<StationLightingRig>().Apply();await Task.Delay(80);var rt=RenderTexture.GetTemporary(640,360,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var old=RenderTexture.active;cam.targetTexture=rt;try{cam.Render();RenderTexture.active=rt;var raw=new Texture2D(640,360,TextureFormat.RGBAFloat,false,true);raw.ReadPixels(new Rect(0,0,640,360),0,0);raw.Apply();var pixels=raw.GetPixels();var png=new Texture2D(640,360,TextureFormat.RGBA32,false);png.SetPixels(pixels.Select(c=>c.gamma).ToArray());png.Apply();File.WriteAllBytes("DeathStarRuns/BounceRepair/"+name+"-"+suffix+".png",png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(png);return pixels;}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}}
   await Shot("warmup");s.screenGiStrength=0;var off=await Shot("off");s.screenGiStrength=1;var on=await Shot("on");s.giDebugView=GiDebugView.ScreenSpaceGiOnly;var bounce=await Shot("bounce");
   var receiver=off;
   int neutralRed=Enumerable.Range(0,bounce.Length).Count(i=>receiver[i].r>.04f&&receiver[i].r<receiver[i].g*1.2f&&Mathf.Abs(receiver[i].r-receiver[i].g)<.05f&&Mathf.Abs(receiver[i].r-receiver[i].b)<.08f&&bounce[i].r>.001f&&bounce[i].r>bounce[i].g*1.3f);
   int changed=Enumerable.Range(0,on.Length).Count(i=>on[i].r-off[i].r>.002f);int red=bounce.Count(c=>c.r>.005f&&c.r>c.g*1.5f);
   var result="Neutral receiver red pixels="+neutralRed+"; Changed pixels="+changed+"; red bounce pixels="+red+"; bounce maximum="+bounce.Max(c=>c.r)+"; bounce mean="+bounce.Average(c=>c.r);
   File.WriteAllText("DeathStarRuns/BounceRepair/"+name+"-result.txt",result);if(name=="after"&&(neutralRed<100||changed<500))throw new Exception("Visible bounce regression: "+result);return result;
  }finally{EditorSceneManager.ClosePreviewScene(scene);foreach(var m in meshes)UnityEngine.Object.DestroyImmediate(m);JsonUtility.FromJsonOverwrite(saved,s);s.ApplyGlobals();s.ApplyPost(lib.post);TileLook.Restore(env);TileLook.ConfigureSky(lib.sky);ShaderUtil.allowAsyncCompilation=async;}
 }
}
