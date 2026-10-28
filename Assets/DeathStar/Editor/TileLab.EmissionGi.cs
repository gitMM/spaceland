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
        // Use the actual caged lamp mesh/material. With direct light, ambient,
        // voxel GI and post-processing absent, only emission can light the ledge.
        static string EmissionGiCheck(Rig rig)
        {
            string dir=Path.Combine(Dir,"emission-gi");Directory.CreateDirectory(dir);
            var settings=StationLightingSettings.Current;
            string saved=JsonUtility.ToJson(settings);
            var cleanup=new List<Object>();
            var checks=new List<(bool ok,string text)>();
            var cameraData=rig.Camera.GetUniversalAdditionalCameraData();
            bool post=cameraData.renderPostProcessing;
            var view=new TileGallery.View(new Vector3(.85f,.7f,1.3f),new Vector3(0,.17f,.05f),Vector3.up,45);
            const int width=960,height=640;
            Texture2D Shot(string name)
            {
                settings.ApplyGlobals();
                var image=rig.Shoot(view,width,height);cleanup.Add(image);
                Save(image,Path.Combine(dir,name+".png"));return image;
            }
            float Ledge(Texture2D image)
            {
                var uv=rig.Camera.WorldToViewportPoint(new Vector3(0,0,.18f));
                int x=Mathf.RoundToInt(uv.x*width),y=Mathf.RoundToInt(uv.y*height);
                float sum=0;
                for(int j=-8;j<=8;j++)for(int i=-8;i<=8;i++)sum+=image.GetPixel(x+i,y+j).grayscale;
                return sum/(17*17);
            }
            try
            {
                settings.sunIntensity=0;settings.planetIntensity=0;settings.ambientLight=false;
                settings.voxelGi=false;settings.screenSpaceGi=true;settings.screenGiStrength=1;
                settings.screenGiRadius=1;settings.emission=1;
                settings.closeUpSurface=false;settings.fineDetail=false;settings.reliefTrace=false;
                settings.contactShadows=false;settings.giDebugView=GiDebugView.ScreenSpaceGiOnly;
                cameraData.renderPostProcessing=false;
                var root=new GameObject("Screen GI emission fixture");cleanup.Add(root);
                SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
                GameObject Box(string name,Vector3 at,Vector3 size)
                {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform,false);
                    go.transform.localPosition=at;go.transform.localScale=size;
                    var mesh=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);cleanup.Add(mesh);
                    var colors=new Color[mesh.vertexCount];for(int i=0;i<colors.Length;i++)colors[i]=Color.white;
                    mesh.colors=colors;go.GetComponent<MeshFilter>().sharedMesh=mesh;
                    go.GetComponent<MeshRenderer>().sharedMaterial=rig.Library.materials[(int)TileSlot.Paint];return go;
                }
                Box("Wall",new Vector3(0,.5f,-.04f),new Vector3(1.6f,1,.08f));
                Box("Ledge",new Vector3(0,-.04f,.45f),new Vector3(1.6f,.08f,1.3f));
                foreach(float x in new[]{-.25f,.25f})
                {
                    var lamp=new GameObject("Caged lamp");lamp.transform.SetParent(root.transform,false);
                    lamp.transform.localPosition=new Vector3(x,.22f,0);
                    lamp.transform.localRotation=Quaternion.LookRotation(Vector3.right,Vector3.forward);
                    lamp.AddComponent<MeshFilter>().sharedMesh=KitProps.Get(5,0);
                    lamp.AddComponent<MeshRenderer>().sharedMaterials=rig.Library.materials;
                }
                TileLook.AddLights(root.transform);
                // Settle the fixture before paired captures.
                var warm=rig.Shoot(view,320,180);Object.DestroyImmediate(warm);
                settings.emission=0;var dark=Shot("emission-off-gi-only");
                settings.emission=1;var lit=Shot("emission-on-gi-only");
                float off=Ledge(dark),on=Ledge(lit);
                checks.Add((off<.001f,$"Unlit fixture has no GI on the ledge: {off:F5}."));
                checks.Add((on>off+.01f,$"Lamp emission reaches the ledge: {off:F5} -> {on:F5} (display RGB mean, no post-processing)."));
                settings.emission=.5f;var half=Shot("half-emission-gi-only");
                float middle=Ledge(half);
                checks.Add((middle>off+.005f&&middle<on-.005f,$"Emission control changes transported light: half {middle:F5}, full {on:F5}."));
                settings.emission=1;settings.giDebugView=GiDebugView.Normal;
                settings.screenSpaceGi=false;var giOff=Shot("screen-gi-off");
                settings.screenSpaceGi=true;var giOn=Shot("screen-gi-on");
                float disabled=Ledge(giOff),enabled=Ledge(giOn);
                checks.Add((enabled>disabled+.01f,$"Normal rendering lights the ledge only with screen GI: {disabled:F5} -> {enabled:F5}. Bloom and voxel GI are disabled."));
                SaveSheet(new List<Texture2D>{giOff,giOn,lit},3,Path.Combine(dir,"comparison.png"));
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(saved,settings);
                cameraData.renderPostProcessing=post;
                foreach(var item in cleanup)if(item)Object.DestroyImmediate(item);
                settings.ApplyGlobals();
            }
            var report=new StringBuilder("# Screen-space lamp emission\n\nColumns in comparison.png: screen GI off, on, isolated GI. Actual kit lamps and tile paint, 1 m reach. No direct light, ambient, voxel GI or post-processing.\n\n");
            bool passed=true;
            foreach(var (ok,text) in checks){report.AppendLine((ok?"PASS ":"FAIL ")+text);passed&=ok;}
            string path=Path.Combine(dir,"report.md");File.WriteAllText(path,report.ToString());
            return (passed?"DONE ":"ERROR ")+path+"\n"+report;
        }
    }
}
