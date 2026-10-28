using System;using System.IO;using System.Linq;using System.Threading.Tasks;using System.Collections.Generic;using System.Text;
using UnityEngine;using UnityEngine.Rendering.Universal;using UnityEngine.SceneManagement;using UnityEditor;using UnityEditor.SceneManagement;
using DeathStar;using DeathStar.EditorTools;
public static class VerifyBounceDistance {
 public static async Task<string> Run() {
  if(EditorApplication.isPlayingOrWillChangePlaymode)throw new Exception("Edit mode required");
  var s=StationLightingSettings.Current;var saved=JsonUtility.ToJson(s);var lib=AssetDatabase.LoadAssetAtPath<TileLibrary>("Assets/DeathStar/Settings/TileLibrary.asset");
  var env=TileLook.ApplyEnvironment(lib);var scene=EditorSceneManager.NewPreviewScene();var owned=new List<UnityEngine.Object>();bool async=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
  const int W=800,H=450;string dir="DeathStarRuns/BounceDistance";Directory.CreateDirectory(dir);
  try {
   s.sunFrom=new Vector3(1,1,0);s.sunIntensity=3;s.planetIntensity=0;s.screenSpaceGi=true;s.screenGiStrength=1;s.screenGiSlices=6;s.screenGiSliceSteps=24;s.screenGiSurfaceThickness=.261f;
   s.screenSpaceAmbientOcclusion=false;s.giDebugView=GiDebugView.ScreenSpaceGiOnly;s.habitatTintEnabled=true;s.habitatTint=new Color(1,.045f,.16f);s.emission=0;
   var root=new GameObject("Bounce distance measurement");SceneManager.MoveGameObjectToScene(root,scene);
   var mat=new Material(Shader.Find("DeathStar/Tile Surface"));TileLook.Configure(mat,TileSlot.Paint);owned.Add(mat);
   void Add(string name,TileMesh tile){var go=new GameObject(name);go.transform.SetParent(root.transform,false);var mesh=tile.ToMesh(name);owned.Add(mesh);go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=Enumerable.Repeat(mat,8).ToArray();}
   var floor=new TileMesh();floor.Quad(new Finish(TileSlot.Paint),new Vector3(0,0,-8),new Vector3(20,0,-8),new Vector3(20,0,8),new Vector3(0,0,8),Vector3.up,Vector3.right);Add("Neutral receiver: 20 m",floor);
   var wall=new TileMesh{overlay=TileLook.FamilyColor(TileFamily.Habitat)};wall.Quad(new Finish(TileSlot.Paint),new Vector3(0,0,-8),new Vector3(0,10,-8),new Vector3(0,10,8),new Vector3(0,0,8),Vector3.right,Vector3.up);Add("Red source wall",wall);
   var lights=new GameObject("Lights");lights.transform.SetParent(root.transform,false);TileLook.AddLights(lights.transform);
   var camGo=new GameObject("Measurement camera");camGo.transform.SetParent(root.transform,false);var cam=camGo.AddComponent<Camera>();cam.enabled=false;cam.scene=scene;TileGalleryMenu.SetUpCamera(cam,lib);
   cam.GetUniversalAdditionalCameraData().renderPostProcessing=false;cam.GetUniversalAdditionalCameraData().antialiasing=AntialiasingMode.None;
   cam.clearFlags=CameraClearFlags.SolidColor;cam.backgroundColor=Color.black;cam.aspect=(float)W/H;cam.transform.position=new Vector3(26,20,-29);cam.transform.LookAt(new Vector3(7,3,0));cam.fieldOfView=45;
   var bands=new[]{(1f,3f),(3f,5f),(5f,8f),(8f,12f),(12f,18f)};
   var mask=new int[W*H];Array.Fill(mask,-1);var plane=new Plane(Vector3.up,Vector3.zero);
   for(int y=0;y<H;y++)for(int x=0;x<W;x++){var ray=cam.ViewportPointToRay(new Vector3((x+.5f)/W,(y+.5f)/H));if(!plane.Raycast(ray,out float t))continue;var p=ray.GetPoint(t);if(Mathf.Abs(p.z)>4)continue;for(int b=0;b<bands.Length;b++)if(p.x>=bands[b].Item1&&p.x<bands[b].Item2){mask[y*W+x]=b;break;}}
   async Task<Color[]> Shot(float radius,string tag){s.screenGiRadius=radius;s.ApplyGlobals();lights.GetComponent<StationLightingRig>().Apply();await Task.Delay(80);var rt=RenderTexture.GetTemporary(W,H,24,RenderTextureFormat.ARGBHalf,RenderTextureReadWrite.Linear);var old=RenderTexture.active;cam.targetTexture=rt;
    try{cam.Render();RenderTexture.active=rt;var raw=new Texture2D(W,H,TextureFormat.RGBAFloat,false,true);raw.ReadPixels(new Rect(0,0,W,H),0,0);raw.Apply();var pixels=raw.GetPixels();var png=new Texture2D(W,H,TextureFormat.RGBA32,false);png.SetPixels(pixels.Select(c=>c.gamma).ToArray());png.Apply();File.WriteAllBytes(dir+"/"+tag+".png",png.EncodeToPNG());UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(png);return pixels;}finally{cam.targetTexture=null;RenderTexture.active=old;RenderTexture.ReleaseTemporary(rt);}}
   await Shot(.8f,"warmup");var report=new StringBuilder("Bounce-only, linear RGB; fixed camera, 6 directions, 24 steps/side, thickness 0.261 m.\nSource x=0; receiver floor x=0..20; measurements use floor |z|<4.\n");
   foreach(float radius in new[]{.8f,5f,10f,20f,30f}){var pixels=await Shot(radius,"reach-"+radius.ToString(System.Globalization.CultureInfo.InvariantCulture));report.AppendLine("Reach "+radius+" m:");
    for(int b=0;b<bands.Length;b++){var ids=Enumerable.Range(0,mask.Length).Where(i=>mask[i]==b).ToArray();report.AppendLine($"  {bands[b].Item1}-{bands[b].Item2} m: mean red={ids.Average(i=>pixels[i].r):F6}, green={ids.Average(i=>pixels[i].g):F6}, lit={ids.Count(i=>pixels[i].r>.0005f)}/{ids.Length}");}}
   File.WriteAllText(dir+"/results.txt",report.ToString());return report.ToString();
  }finally{EditorSceneManager.ClosePreviewScene(scene);foreach(var o in owned)UnityEngine.Object.DestroyImmediate(o);JsonUtility.FromJsonOverwrite(saved,s);s.ApplyGlobals();s.ApplyPost(lib.post);TileLook.Restore(env);TileLook.ConfigureSky(lib.sky);ShaderUtil.allowAsyncCompilation=async;}
 }
}
