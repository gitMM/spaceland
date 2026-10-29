using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    public static partial class TileLab
    {
        // Render sequence tests: source changes, disocclusion, separate cameras,
        // resolution changes, and AO/GI independence. Reads actual GPU history counts.
        static string ScreenGiCheck(Rig rig)
        {
            string dir=Path.Combine(Dir,"screen-gi");Directory.CreateDirectory(dir);
            var s=StationLightingSettings.Current;string saved=JsonUtility.ToJson(s);
            var cleanup=new List<Object>();var checks=new List<(bool ok,string text)>();var notes=new StringBuilder();
            var cameraData=rig.Camera.GetUniversalAdditionalCameraData();bool post=cameraData.renderPostProcessing;
            var view=new TileGallery.View(new Vector3(.85f,.7f,1.3f),new Vector3(0,.17f,.05f),Vector3.up,45);
            const int width=640,height=480;
            Texture2D Shot(string name=null)
            {
                s.ApplyGlobals();var t=rig.Shoot(view,width,height,scale:1);cleanup.Add(t);
                if(name!=null)Save(t,Path.Combine(dir,name+".png"));return t;
            }
            float Difference(Texture2D a,Texture2D b)
            {
                var x=a.GetPixels();var y=b.GetPixels();float sum=0;
                for(int i=0;i<x.Length;i++)sum+=Mathf.Abs(x[i].r-y[i].r)+Mathf.Abs(x[i].g-y[i].g)+Mathf.Abs(x[i].b-y[i].b);
                return sum/(x.Length*3);
            }
            float PatchRoughness(Texture2D t)
            {
                var uv=rig.Camera.WorldToViewportPoint(new Vector3(0,0,.3f));
                int cx=Mathf.RoundToInt(uv.x*width),cy=Mathf.RoundToInt(uv.y*height);float sum=0;
                for(int y=cy-16;y<=cy+16;y++)for(int x=cx-20;x<=cx+20;x++) {
                    float c=t.GetPixel(x,y).grayscale;
                    float avg=(t.GetPixel(x-1,y).grayscale+t.GetPixel(x+1,y).grayscale+t.GetPixel(x,y-1).grayscale+t.GetPixel(x,y+1).grayscale)*.25f;
                    sum+=Mathf.Abs(c-avg);
                }
                return sum/(33*41);
            }
            float LedgeMean(Texture2D t) {
                var uv=rig.Camera.WorldToViewportPoint(new Vector3(0,0,.3f));
                int cx=Mathf.RoundToInt(uv.x*width),cy=Mathf.RoundToInt(uv.y*height);float sum=0;
                for(int y=cy-16;y<=cy+16;y++)for(int x=cx-20;x<=cx+20;x++)sum+=t.GetPixel(x,y).grayscale;
                return sum/(33*41);
            }
            (float mean,float max) HistoryCount(Camera target=null)
            {
                if(!target)target=rig.Camera;
                foreach(var feature in Resources.FindObjectsOfTypeAll<StationScreenGiFeature>()) {
                    var pass=typeof(StationScreenGiFeature).GetField("denoisePass",BindingFlags.NonPublic|BindingFlags.Instance)?.GetValue(feature);
                    if(pass==null)continue;
                    var histories=(IDictionary)pass.GetType().GetField("histories",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(pass);
                    if(!histories.Contains(target))continue;
                    var h=histories[target];var type=h.GetType();
                    var handles=(RTHandle[])type.GetField("Color").GetValue(h);int index=(int)type.GetField("Index").GetValue(h);
                    var rt=handles[index]?.rt;if(!rt)continue;
                    var previous=RenderTexture.active;RenderTexture.active=rt;
                    var image=new Texture2D(rt.width,rt.height,TextureFormat.RGBAFloat,false,true);
                    image.ReadPixels(new Rect(0,0,rt.width,rt.height),0,0);image.Apply();RenderTexture.active=previous;
                    float sum=0,max=0;int count=0;
                    foreach(var c in image.GetPixels())if(c.a>0){sum+=c.a;max=Mathf.Max(max,c.a);count++;}
                    Object.DestroyImmediate(image);return(sum/Mathf.Max(count,1),max);
                }
                return(0,0);
            }
            try {
                s.sunIntensity=0;s.planetIntensity=0;s.ambientLight=false;s.voxelGi=false;
                s.screenSpaceGi=true;s.screenGiStrength=1;s.screenGiRadius=1;s.emission=1;
                s.screenGiSlices=8;s.screenGiSliceSteps=24;s.closeUpSurface=false;s.fineDetail=false;s.reliefTrace=false;
                s.contactShadows=false;s.giDebugView=GiDebugView.ScreenSpaceGiOnly;cameraData.renderPostProcessing=false;
                var root=new GameObject("GI reconstruction fixture");cleanup.Add(root);SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
                GameObject Box(string name,Vector3 at,Vector3 size) {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=at;go.transform.localScale=size;
                    var mesh=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);cleanup.Add(mesh);
                    mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();go.GetComponent<MeshFilter>().sharedMesh=mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterial=rig.Library.materials[(int)TileSlot.Paint];return go;
                }
                Box("Wall",new Vector3(0,.5f,-.04f),new Vector3(1.6f,1,.08f));
                Box("Ledge",new Vector3(0,-.04f,.45f),new Vector3(1.6f,.08f,1.3f));
                var blocker=Box("Moving occluder",new Vector3(0,.18f,.38f),new Vector3(.18f,.36f,.1f));blocker.SetActive(false);
                foreach(float x in new[]{-.25f,.25f}) {
                    var lamp=new GameObject("Caged lamp");lamp.transform.SetParent(root.transform,false);lamp.transform.localPosition=new Vector3(x,.22f,0);
                    lamp.transform.localRotation=Quaternion.LookRotation(Vector3.right,Vector3.forward);
                    lamp.AddComponent<MeshFilter>().sharedMesh=KitProps.Get(5,0);lamp.AddComponent<MeshRenderer>().sharedMaterials=rig.Library.materials;
                }
                TileLook.AddLights(root.transform);
                s.screenGiTemporal=false;s.screenGiDenoise=false;Shot();var raw=Shot("raw");
                s.screenGiDenoise=true;var spatial=Shot("spatial");
                s.screenGiTemporal=true;StationScreenGiFeature.ResetHistory();var first=Shot("first-frame");
                var initial=HistoryCount();checks.Add((initial.max<=1.01f&&initial.max>.9f,$"Fresh history starts at one sample: max {initial.max:F2}."));
                Texture2D settled=first;
                for(int i=0;i<20;i++)settled=Shot();
                Save(settled,Path.Combine(dir,"settled.png"));
                var stable=HistoryCount();checks.Add((stable.mean>2&&stable.max>=8,$"Stationary surfaces accumulate actual GPU history: mean {stable.mean:F2}, max {stable.max:F2}."));
                float rawRough=PatchRoughness(raw),filteredRough=PatchRoughness(settled);
                checks.Add((filteredRough<rawRough,$"Smooth ledge high-frequency residual decreases: {rawRough:F6} -> {filteredRough:F6}."));
                checks.Add((LedgeMean(settled)>.01f,$"Denoising retains lamp illumination on the ledge: {LedgeMean(settled):F5}."));
                SaveSheet(new List<Texture2D>{raw,spatial,settled},3,Path.Combine(dir,"comparison.png"));

                s.screenGiAoRadius=.15f;StationScreenGiFeature.ResetHistory();var aoNear=Shot();
                s.screenGiAoRadius=3;StationScreenGiFeature.ResetHistory();var aoFar=Shot();
                float aoDelta=Difference(aoNear,aoFar);checks.Add((aoDelta==0,$"Changing only AO radius leaves isolated GI pixel-identical: mean difference {aoDelta:F8}."));
                s.giDebugView=GiDebugView.ScreenSpaceAoOnly;s.screenGiRadius=.2f;var giNear=Shot();
                s.screenGiRadius=5;var giFar=Shot();float giDelta=Difference(giNear,giFar);
                checks.Add((giDelta==0,$"Changing only GI radius leaves isolated AO pixel-identical: mean difference {giDelta:F8}."));
                s.screenGiRadius=1;s.giDebugView=GiDebugView.ScreenSpaceGiOnly;
                for(int i=0;i<8;i++)Shot();
                s.emission=0;var off=Shot("lamp-off");var offHistory=HistoryCount();
                checks.Add((LedgeMean(off)<.001f&&offHistory.max<=1.01f,$"Turning emission off clears lighting history immediately: ledge {LedgeMean(off):F6}, history {offHistory.max:F2}."));
                s.emission=1;for(int i=0;i<8;i++)Shot();
                var oldView=view;
                view=new TileGallery.View(view.position+new Vector3(-.08f,.03f,0),view.target,view.up,view.fov);
                Shot("camera-move");var moved=HistoryCount();
                checks.Add((moved.max>2,$"Small camera movement reuses history on matching surfaces: max {moved.max:F2}."));
                blocker.SetActive(true);for(int i=0;i<8;i++)Shot();
                blocker.SetActive(false);var reveal=Shot("disocclusion");
                StationScreenGiFeature.ResetHistory();var revealFresh=Shot("disocclusion-fresh");
                float revealDelta=Difference(reveal,revealFresh);
                checks.Add((revealDelta<.025f,$"Removed occluder does not leave a large history trail: whole-frame mean difference from fresh {revealDelta:F6}."));
                view=new TileGallery.View(new Vector3(-2,1,2),view.target,view.up,view.fov);Shot("camera-cut");
                checks.Add((HistoryCount().max<=1.01f,"Large camera cut resets history."));
                view=oldView;Shot();
                var resized=rig.Shoot(view,320,240,scale:1);cleanup.Add(resized);
                checks.Add((HistoryCount().max<=1.01f,"Resolution change resets history."));
                using(var other=new Rig(rig.Library)) {
                    other.Camera.scene=rig.PreviewScene;
                    var otherShot=other.Shoot(view,width,height,scale:1);cleanup.Add(otherShot);
                    checks.Add((HistoryCount(other.Camera).max<=1.01f&&HistoryCount(other.Camera).max>.9f,"A second camera starts its own independent history."));
                }
                Shot();checks.Add((HistoryCount().max<=1.01f,"Returning to the original resolution starts fresh after another camera renders."));
                var hdr=rig.ShootHdr(view,320,240);checks.Add((hdr.bad==0,$"HDR output contains no NaN or infinity ({hdr.bad})."));
                // Interleaved native 1080p captures include submission, GPU completion
                // and readback. These are end-to-end timings, not GPU timestamps.
                var times=new[]{new List<double>(),new List<double>()};
                for(int i=0;i<14;i++) {
                    bool enabled=(i&1)==1;s.screenGiTemporal=enabled;s.screenGiDenoise=enabled;
                    var timer=System.Diagnostics.Stopwatch.StartNew();
                    var timed=rig.Shoot(view,1920,1080,scale:1);double elapsed=timer.Elapsed.TotalMilliseconds;Object.DestroyImmediate(timed);
                    if(i>=4)times[i&1].Add(elapsed);
                }
                foreach(var list in times)list.Sort();
                notes.AppendLine($"\n1920 x 1080 synchronous render + readback, five interleaved samples after warmup: temporal/spatial disabled {times[0][2]:F2} ms; temporal + spatial {times[1][2]:F2} ms; difference {times[1][2]-times[0][2]:F2} ms. Both use 8 directions x 24 samples per side, separately for GI and AO. Includes CPU and readback; not isolated GPU timings.\n");
                notes.AppendLine(SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType);
            }
            finally {
                JsonUtility.FromJsonOverwrite(saved,s);cameraData.renderPostProcessing=post;
                foreach(var item in cleanup)if(item)Object.DestroyImmediate(item);
                StationScreenGiFeature.ResetHistory();s.ApplyGlobals();
            }
            var report=new StringBuilder("# Screen GI reconstruction\n\n640 x 480, native resolution. Actual caged lamps, no direct/ambient/voxel lighting or bloom. Comparison: unfiltered, spatial only, spatial + temporal after 20 additional renders.\n\n");
            foreach(var (ok,message) in checks)report.AppendLine((ok?"PASS ":"FAIL ")+message);
            report.Append(notes);
            string path=Path.Combine(dir,"report.md");File.WriteAllText(path,report.ToString());
            return(checks.All(c=>c.ok)?"DONE ":"ERROR ")+path+"\n"+report;
        }
    }
}
