using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering.Universal;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    public static partial class TileLab
    {
        static string LocalLightCheck(Rig rig)
        {
            string dir=Path.Combine(Dir,"local-lights");Directory.CreateDirectory(dir);
            var s=StationLightingSettings.Current;string saved=JsonUtility.ToJson(s);
            var cleanup=new List<Object>();var checks=new List<(bool ok,string text)>();
            var data=rig.Camera.GetUniversalAdditionalCameraData();bool post=data.renderPostProcessing;
            var view=new TileGallery.View(new Vector3(.85f,.7f,1.3f),new Vector3(0,.17f,.05f),Vector3.up,45);
            const int w=640,h=480;
            Texture2D Shot(string name) {s.ApplyGlobals();var image=rig.Shoot(view,w,h,false,1);cleanup.Add(image);Save(image,Path.Combine(dir,name+".png"));return image;}
            float Sample(Texture2D image,Vector3 p) {
                var uv=rig.Camera.WorldToViewportPoint(p);int x=Mathf.RoundToInt(uv.x*w),y=Mathf.RoundToInt(uv.y*h);float sum=0;
                for(int j=-3;j<=3;j++)for(int i=-3;i<=3;i++)sum+=image.GetPixel(Mathf.Clamp(x+i,0,w-1),Mathf.Clamp(y+j,0,h-1)).grayscale;
                return sum/49;
            }
            try {
                // Final bent meshes can have any orientation, unlike the authored prop mesh.
                var lens=KitProps.Get(5,0);var original=StationLocalLights.Extract(lens,(int)TileSlot.Light)[0];
                var rotated=Object.Instantiate(lens);cleanup.Add(rotated);
                var rotation=Quaternion.Euler(23,47,19);var offset=new Vector3(2,3,4);var vertices=rotated.vertices;
                for(int i=0;i<vertices.Length;i++)vertices[i]=rotation*vertices[i]+offset;rotated.vertices=vertices;
                var bent=StationLocalLights.Extract(rotated,(int)TileSlot.Light)[0];
                float alignment=Mathf.Abs(Vector3.Dot((bent.b-bent.a).normalized,rotation*(original.b-original.a).normalized));
                float centreError=Vector3.Distance((bent.a+bent.b)*.5f,rotation*((original.a+original.b)*.5f)+offset);
                checks.Add((alignment>.999f&&centreError<.001f,$"Rotated/bent strip aligns with its lens: cosine {alignment:F6}, centre error {centreError:F6} m."));
                checks.Add((original.color.r>original.color.g&&original.color.g>original.color.b,"Lamp light retains the warm vertex tint."));
                s.sunIntensity=0;s.planetIntensity=0;s.ambientLight=false;s.voxelGi=false;s.screenSpaceGi=false;
                s.screenSpaceAmbientOcclusion=false;s.contactShadows=false;s.emission=1;s.reflections=0;
                s.closeUpSurface=false;s.fineDetail=false;s.reliefTrace=false;s.kitProps=false;
                s.localLightStrength=.8f;s.localLightRange=1;s.localLightDistance=45;s.localLightBudget=512;s.localLampScreenGi=0;
                s.giDebugView=GiDebugView.Normal;data.renderPostProcessing=false;
                var root=new GameObject("Local lamp fixture");cleanup.Add(root);SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
                GameObject Box(string name,Vector3 at,Vector3 size) {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=at;go.transform.localScale=size;
                    var mesh=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);cleanup.Add(mesh);
                    var colors=new Color[mesh.vertexCount];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;mesh.colors=colors;
                    go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=rig.Library.materials[(int)TileSlot.Paint];return go;
                }
                Box("Wall",new Vector3(0,.5f,-.04f),new Vector3(1.6f,1,.08f));
                Box("Ledge",new Vector3(0,-.04f,.45f),new Vector3(1.6f,.08f,1.3f));
                var sources=new List<StationLocalLightSource>();
                foreach(float x in new[]{-.25f,.25f}) {
                    var lamp=new GameObject("Caged lamp");lamp.transform.SetParent(root.transform,false);lamp.transform.localPosition=new Vector3(x,.22f,0);
                    lamp.transform.localRotation=Quaternion.LookRotation(Vector3.right,Vector3.forward);
                    lamp.AddComponent<MeshFilter>().sharedMesh=KitProps.Get(5,0);lamp.AddComponent<MeshRenderer>().sharedMaterials=rig.Library.materials;
                    var source=lamp.AddComponent<StationLocalLightSource>();source.clipToMountingPlane=true;sources.Add(source);
                }
                TileLook.AddLights(root.transform);
                var warm=rig.Shoot(view,320,180,false,1);Object.DestroyImmediate(warm);
                var p=new Vector3(0,0,.18f);
                s.localLights=false;var off=Shot("01-off");float dark=Sample(off,p);
                s.localLights=true;var on=Shot("02-on");float lit=Sample(on,p);
                checks.Add((lit>dark+.03f,$"Local lamps light the ledge with GI, ambient, sun and bloom disabled: {dark:F5} -> {lit:F5}."));
                checks.Add((StationLocalLights.VisibleCount==2,$"Two connected lamp lenses produce two strip sources: {StationLocalLights.VisibleCount}."));
                var repeat=Shot("03-repeat");float drift=0;var aa=on.GetPixels32();var bb=repeat.GetPixels32();
                for(int i=0;i<aa.Length;i++)drift+=Mathf.Abs(aa[i].r-bb[i].r)+Mathf.Abs(aa[i].g-bb[i].g)+Mathf.Abs(aa[i].b-bb[i].b);
                drift/=aa.Length*3*255;
                checks.Add((drift<.001f,$"Repeated static frame mean change: {drift:F7}."));
                s.localLightStrength=.4f;var half=Shot("04-half");float middle=Sample(half,p);
                checks.Add((middle>dark+.01f&&middle<lit-.01f,$"Intensity control changes illumination: half {middle:F5}, full {lit:F5}."));
                s.localLightStrength=.8f;
                view=new TileGallery.View(new Vector3(.9f,.72f,1.32f),new Vector3(0,.17f,.05f),Vector3.up,45);
                var moved=Shot("05-camera-moved");float movedLight=Sample(moved,p);
                checks.Add((Mathf.Abs(movedLight-lit)<.035f,$"Same ledge after camera motion: {lit:F5} -> {movedLight:F5}."));
                view=new TileGallery.View(new Vector3(0,1,.46f),new Vector3(0,0,.46f),Vector3.forward,24);
                var hidden=Shot("06-offscreen-lamps");float hiddenLight=Sample(hidden,new Vector3(0,0,.4f));
                var bulb=rig.Camera.WorldToViewportPoint(new Vector3(.25f,.22f,.065f));
                bool outside=bulb.z<0||bulb.x<0||bulb.x>1||bulb.y<0||bulb.y>1;
                checks.Add((outside&&hiddenLight>.02f,$"Off-screen bulbs still illuminate visible floor: {hiddenLight:F5}; bulb outside view {outside}."));
                view=new TileGallery.View(new Vector3(0,.65f,-1),new Vector3(0,.05f,-.12f),Vector3.up,38);
                var behind=Shot("06b-behind-mount");float behindOn=Sample(behind,new Vector3(0,0,-.12f));
                s.localLights=false;var behindOff=Shot("06c-behind-mount-off");float behindDark=Sample(behindOff,new Vector3(0,0,-.12f));s.localLights=true;
                checks.Add((Mathf.Abs(behindOn-behindDark)<.005f,$"Mounting plane blocks light behind the kit's wall: {behindDark:F5} -> {behindOn:F5}."));
                view=new TileGallery.View(new Vector3(.18f,.4f,.5f),new Vector3(0,0,.18f),Vector3.up,65);
                var inside=Shot("06d-camera-inside-volume");float insideLight=Sample(inside,p);
                checks.Add((insideLight>dark+.03f,$"Camera inside the light volume retains illumination: {insideLight:F5}."));
                view=new TileGallery.View(new Vector3(.85f,.7f,1.3f),new Vector3(0,.17f,.05f),Vector3.up,45);
                foreach(var source in sources)source.enabled=false;
                var removed=Shot("07-sources-disabled");float gone=Sample(removed,p);
                checks.Add((Mathf.Abs(gone-dark)<.005f,$"Disabling sources removes illumination without residual history: {gone:F5}."));
                foreach(var source in sources)source.enabled=true;
                // Inspect the actual source pass: these lamps contribute no noisy SSGI emission by default.
                s.screenSpaceGi=true;s.screenGiStrength=1;s.giDebugView=GiDebugView.DiffuseSources;
                s.localLampScreenGi=1;var fullSource=Shot("08-gi-emission-on");
                s.localLampScreenGi=0;var zeroSource=Shot("09-gi-emission-off");
                float fullSum=0,zeroSum=0;foreach(var c in fullSource.GetPixels())fullSum+=c.grayscale;foreach(var c in zeroSource.GetPixels())zeroSum+=c.grayscale;
                checks.Add((fullSum>zeroSum+1&&zeroSum<fullSum*.1f,$"Lamp SSGI contribution can be removed independently: image sum {fullSum:F2} -> {zeroSum:F2}."));
                s.screenSpaceGi=false;s.giDebugView=GiDebugView.Normal;
                var hdr=rig.ShootHdr(view,320,240);checks.Add((hdr.bad==0,$"HDR output finite; maximum {hdr.max:F3}."));
                SaveSheet(new List<Texture2D>{off,on,half,moved,hidden,removed},3,Path.Combine(dir,"comparison.png"));
                root.SetActive(false);
                LocalMountChecks(rig,dir,checks,cleanup);
            }
            finally {
                JsonUtility.FromJsonOverwrite(saved,s);data.renderPostProcessing=post;
                foreach(var item in cleanup)if(item)Object.DestroyImmediate(item);s.ApplyGlobals();
            }
            var report=new StringBuilder("# Local lamp lighting\n\nActual caged kit lamps on a painted wall and ledge. No sun, ambient, voxel GI or bloom. Comparison: off / on / half strength; moved camera / bulbs off screen / disabled sources.\n\n");
            bool passed=true;foreach(var (ok,text) in checks){report.AppendLine((ok?"PASS ":"FAIL ")+text);passed&=ok;}
            string path=Path.Combine(dir,"report.md");File.WriteAllText(path,report.ToString());return (passed?"DONE ":"ERROR ")+path+"\n"+report;
        }
    }
}
