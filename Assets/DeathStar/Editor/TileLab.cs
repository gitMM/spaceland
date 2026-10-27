using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DeathStar.EditorTools
{
    // Headless review of the settlement tiles, phase 1 (massing): builds the floating station and the nine
    // specimens in a preview scene and renders them: the whole station, the fragment, a trench run, the underside,
    // a grazing look across the outer surface, an end face, a straight look at a big area of relief (seams), and
    // every specimen from its lit side and from behind. Writes DeathStarRuns/Tiles/.
    //   report                                       everything plus report.md
    //   snap view=<Station|Fragment|Trench|Under|Surface|EndFace|Seams>
    //   snap family=<Name> [back=1]
    //   snap x= y= z= tx= ty= tz= [ux= uy= uz=] [fov=]
    public static class TileLab
    {
        const int W=1600,H=900;
        static string Dir=>Path.GetFullPath(Path.Combine(Application.dataPath,"../DeathStarRuns/Tiles"));
        static readonly string[] Shaders={"DeathStar/Tile Surface","DeathStar/Tile Glass","DeathStar/Space Sky"};

        static readonly (string name,Func<TileGallery.View> view,string what)[] Views=
        {
            ("station",()=>TileGallery.Station,"the whole stage: fragment and the row of nine specimens"),
            ("fragment",()=>TileGallery.Fragment,"the station fragment at construction distance (a cube is ~90 px)"),
            ("trench",()=>TileGallery.Trench,"down the trench: habitat bands on one wall, power banks on the other, bridges, pits"),
            ("under",()=>TileGallery.Under,"the underside: power banks, a turret and a tower hanging the other way"),
            ("surface",()=>TileGallery.Surface,"grazing across the outer surface: vaults, turrets, the command tower"),
            ("end",()=>TileGallery.EndFace,"the end face: habitat bands running round a corner"),
            ("dish",()=>TileGallery.Dish,"the near face: a 2 x 2 Command block raises one big dish"),
            ("hangars",()=>TileGallery.HangarEnd,"the near end: 2 x 2 Storage blocks open two big hangar mouths"),
            ("craft",()=>TileGallery.CraftView,"a shuttle on the 2 x 2 pad at the end of the cantilever"),
            ("pod",()=>TileGallery.PodView,"a pod docked on a single pad between the tower turrets"),
            ("seams",()=>TileGallery.Seams,"a wide area of plain hull relief: no tile seams should show"),
        };

        public static string Run(string args)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return "ERROR the editor is in play mode";
            var words=(args??"").Split(new[]{' '},StringSplitOptions.RemoveEmptyEntries);
            string mode=words.Length>0?words[0]:"report";
            var opts=new Dictionary<string,string>();
            foreach(var w in words.Skip(1)){int i=w.IndexOf('=');if(i>0)opts[w.Substring(0,i)]=w.Substring(i+1);}
            foreach(var name in Shaders)
            {
                var shader=Shader.Find(name);
                if(shader==null)return "ERROR missing shader "+name;
                if(ShaderUtil.ShaderHasError(shader))
                    return "ERROR shader "+name+": "+string.Join(" | ",ShaderUtil.GetShaderMessages(shader).Select(m=>m.message+" ("+m.file+":"+m.line+")"));
            }
            var lib=TileGalleryMenu.EnsureLibrary();
            Directory.CreateDirectory(Dir);
            using var rig=new Rig(lib);
            if(mode=="warmup"){var t=rig.Shoot(TileGallery.Fragment,320,180);UnityEngine.Object.DestroyImmediate(t);return "DONE warmup";}
            if(mode=="snap")return Snap(rig,opts);
            return Report(rig);
        }

        static float F(Dictionary<string,string> o,string k,float d)=>o.TryGetValue(k,out var s)&&float.TryParse(s,NumberStyles.Float,CultureInfo.InvariantCulture,out var v)?v:d;

        static string Snap(Rig rig,Dictionary<string,string> o)
        {
            TileGallery.View view;
            if(o.TryGetValue("view",out var vn))
            {
                int i=Array.FindIndex(Views,v=>string.Equals(v.name,vn,StringComparison.OrdinalIgnoreCase));
                if(i<0)return "ERROR unknown view "+vn+" (one of "+string.Join(", ",Views.Select(v=>v.name))+")";
                view=Views[i].view();
            }
            else if(o.TryGetValue("family",out var name))
            {
                int i=rig.Stage.specimens.FindIndex(s=>string.Equals(s.family.ToString(),name,StringComparison.OrdinalIgnoreCase));
                if(i<0)return "ERROR unknown family "+name+" (one of "+string.Join(", ",TileGallery.Families)+")";
                view=TileGallery.SpecimenView(rig.Stage.specimens[i],F(o,"back",0)>0.5f);
            }
            else view=new TileGallery.View(new Vector3(F(o,"x",0),F(o,"y",60),F(o,"z",-60)),new Vector3(F(o,"tx",30),F(o,"ty",5),F(o,"tz",20)),
                new Vector3(F(o,"ux",0),F(o,"uy",1),F(o,"uz",0)),F(o,"fov",40));
            string path=Path.Combine(Dir,"snap.png");
            var tex=rig.Shoot(view,W,H);Save(tex,path);UnityEngine.Object.DestroyImmediate(tex);
            return "DONE "+path;
        }

        static string Report(Rig rig)
        {
            var total=Stopwatch.StartNew();
            foreach(var f in Directory.GetFiles(Dir,"*.png"))File.Delete(f);
            var md=new StringBuilder();var checks=new List<(bool ok,string text)>();
            var stage=rig.Stage;
            md.AppendLine("# Settlement tiles — phase 1 (massing) lab");
            md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Pictures {W}x{H}, rendered at twice the size and averaged down.");
            md.AppendLine();
            md.AppendLine($"Stage: {stage.structure.cells.Count} cells, {stage.tiles} with visible geometry, {stage.triangles:N0} triangles with every tile near ({stage.farTriangles:N0} with every tile at its far level, armour plain), built in {stage.buildMs:0} ms.");
            md.AppendLine();
            md.AppendLine("Triangles per view with each tile at the level of detail its LOD group picks (all tiles, before frustum culling):");
            md.AppendLine();
            md.AppendLine("| View | Near tiles | Far tiles | Triangles |");
            md.AppendLine("|---|---:|---:|---:|");
            foreach(var (name,view,_) in Views)
            {
                var (near,far,tris)=Drawn(stage,view());
                md.AppendLine($"| {name} | {near} | {far} | {tris:N0} |");
            }
            checks.Add((stage.farTriangles<stage.triangles*0.6f,$"the far level of detail drops the carving: {stage.farTriangles/1000}k against {stage.triangles/1000}k triangles"));
            md.AppendLine();
            md.AppendLine("## Specimens (the family tile only, in its specimen setting)");
            md.AppendLine();
            md.AppendLine("| Family | Orientation (axis, side) | Triangles | Size (m) |");
            md.AppendLine("|---|---|---:|---|");
            foreach(var sp in stage.specimens)
            {
                stage.stats.TryGetValue(sp.family,out var st);var size=st.bounds.size;
                var cell=stage.structure.cells[sp.TileCell];
                md.AppendLine($"| {TileGallery.Title(sp.family)} | {cell.axis}, {cell.side} | {st.triangles:N0} | {size.x:0.0} × {size.y:0.0} × {size.z:0.0} |");
                checks.Add((st.triangles>0,$"{sp.family} builds"));
                float big=Mathf.Max(size.x,Mathf.Max(size.y,size.z));
                checks.Add((big<=13f,$"{sp.family} stays proportionate to its 5 m cube (≤13 m in any direction): {big:0.0} m"));
            }
            checks.Add((stage.buildMs<15000,$"stage builds in under 15 s ({stage.buildMs:0} ms)"));
            md.AppendLine();

            var shots=new List<(string file,string what)>();
            var timings=new List<double>();
            foreach(var (name,view,what) in Views)
            {
                var sw=Stopwatch.StartNew();var tex=rig.Shoot(view(),W,H);timings.Add(sw.Elapsed.TotalMilliseconds);
                Save(tex,Path.Combine(Dir,name+".png"));shots.Add((name+".png",what));
                if(name=="fragment")
                {
                    float mean=Mean(tex);
                    checks.Add((mean>0.03f&&mean<0.6f,$"fragment picture is neither black nor blown out (mean {mean:0.000})"));
                }
                UnityEngine.Object.DestroyImmediate(tex);
            }
            foreach(var (name,view,what) in Views.Where(v=>v.name=="station"||v.name=="fragment"||v.name=="trench"||v.name=="surface"))
            {
                var tex=rig.Shoot(view(),W,H,true);Save(tex,Path.Combine(Dir,name+"-coded.png"));UnityEngine.Object.DestroyImmediate(tex);
                shots.Add((name+"-coded.png","the same, colour-coded by family"));
            }
            const int cw=600,ch=338,cols=4;
            foreach(var (sheet,back,coded,what) in new[]{("specimens.png",false,false,"each family from its lit side"),("specimens-coded.png",false,true,"the same, colour-coded"),("specimens-back.png",true,false,"each family from behind")})
            {
                int rows=(stage.specimens.Count+cols-1)/cols;
                var tex=new Texture2D(cw*cols,ch*rows,TextureFormat.RGBA32,false);
                tex.SetPixels32(new Color32[cw*cols*ch*rows]);
                for(int i=0;i<stage.specimens.Count;i++)
                {
                    var sw=Stopwatch.StartNew();var t=rig.Shoot(TileGallery.SpecimenView(stage.specimens[i],back),cw,ch,coded);timings.Add(sw.Elapsed.TotalMilliseconds);
                    tex.SetPixels(i%cols*cw,(rows-1-i/cols)*ch,cw,ch,t.GetPixels());
                    if(!back&&!coded)Save(t,Path.Combine(Dir,"family-"+stage.specimens[i].family.ToString().ToLowerInvariant()+".png"));
                    UnityEngine.Object.DestroyImmediate(t);
                }
                tex.Apply();Save(tex,Path.Combine(Dir,sheet));UnityEngine.Object.DestroyImmediate(tex);
                shots.Add((sheet,what+": left to right, top to bottom: Habitat, Industrial, Power, Life Support / Storage, Defense, Command, Corridor / Landing Pad, Conduit, Hull; each specimen sits in a different orientation"));
            }

            md.AppendLine("## Pictures");
            md.AppendLine();
            foreach(var (file,what) in shots)md.AppendLine($"- `{file}`: {what}");
            md.AppendLine("- `family-<name>.png`: each specimen on its own");
            md.AppendLine();
            md.AppendLine("Colour code: "+string.Join(", ",TileGallery.Families.Select(f=>$"{TileGallery.Title(f)} {Swatch(TileLook.FamilyColor(f))}"))+".");
            md.AppendLine();
            timings.Sort();
            md.AppendLine($"Render (2x supersampled, editor): median {timings[timings.Count/2]:0} ms per picture. Lab total {total.Elapsed.TotalSeconds:0.0} s.");
            md.AppendLine();
            md.AppendLine("## Checks");
            md.AppendLine();
            foreach(var (ok,text) in checks)md.AppendLine($"- {(ok?"PASS":"FAIL")} {text}");
            File.WriteAllText(Path.Combine(Dir,"report.md"),md.ToString());
            int fails=checks.Count(c=>!c.ok);
            string summary=$"Tile lab: {checks.Count-fails}/{checks.Count} checks pass, {shots.Count} pictures in {Dir} ({total.Elapsed.TotalSeconds:0.0} s; {stage.triangles/1000}k triangles near, {stage.farTriangles/1000}k far, fragment view draws {Drawn(stage,TileGallery.Fragment).tris/1000}k)";
            if(fails>0)summary+="\n"+string.Join("\n",checks.Where(c=>!c.ok).Select(c=>"FAIL "+c.text));
            File.WriteAllText(Path.Combine(Dir,"last.txt"),summary+"\n");
            return "DONE "+summary;
        }

        // Which level each tile's LOD group picks for a view (Unity's rule: bounds size over the frustum height at its
        // distance, times the LOD bias), and the triangles that draws.
        static (int near,int far,long tris) Drawn(TileGallery.Stage stage,TileGallery.View v)
        {
            int near=0,far=0;long tris=0;
            float k=2*Mathf.Tan(v.fov*0.5f*Mathf.Deg2Rad);
            foreach(var g in stage.root.GetComponentsInChildren<LODGroup>())
            {
                var lods=g.GetLODs();
                float d=Vector3.Distance(v.position,g.transform.TransformPoint(g.localReferencePoint));
                float rel=g.size*QualitySettings.lodBias/(k*Mathf.Max(d,0.01f));
                bool isNear=rel>=lods[0].screenRelativeTransitionHeight;
                if(isNear)near++;else far++;
                var r=(isNear?lods[0]:lods[1]).renderers[0];
                tris+=TileGallery.Triangles(r.GetComponent<MeshFilter>().sharedMesh);
            }
            foreach(var r in stage.root.GetComponentsInChildren<MeshRenderer>())
                if(r.GetComponentInParent<LODGroup>()==null)tris+=TileGallery.Triangles(r.GetComponent<MeshFilter>().sharedMesh);
            return (near,far,tris);
        }

        static string Swatch(Color c)=>c.a<0.5f?"grey":"#"+ColorUtility.ToHtmlStringRGB(c.gamma);
        static void Save(Texture2D t,string path)=>File.WriteAllBytes(path,t.EncodeToPNG());
        static float Mean(Texture2D t){var px=t.GetPixels32();double s=0;foreach(var p in px)s+=(p.r*0.2126+p.g*0.7152+p.b*0.0722)/255.0;return (float)(s/px.Length);}

        sealed class Rig:IDisposable
        {
            readonly Scene scene;
            readonly Camera cam;
            readonly TileLook.Saved environment;
            readonly bool asyncCompile;
            public readonly TileGallery.Stage Stage;

            public Rig(TileLibrary lib)
            {
                asyncCompile=ShaderUtil.allowAsyncCompilation;ShaderUtil.allowAsyncCompilation=false;
                scene=EditorSceneManager.NewPreviewScene();
                var holder=new GameObject("Tile Lab");SceneManager.MoveGameObjectToScene(holder,scene);
                Stage=TileGallery.Build(holder.transform,lib,false);
                var camGo=new GameObject("Lab Camera");camGo.transform.SetParent(holder.transform,false);
                cam=camGo.AddComponent<Camera>();cam.scene=scene;cam.enabled=false;
                TileGalleryMenu.SetUpCamera(cam,lib);
                environment=TileLook.ApplyEnvironment(lib);
                Shader.SetGlobalFloat("_TileClay",0);
                var warm=Shoot(TileGallery.Fragment,64,36);   // a new preview scene's first render draws only the sky
                UnityEngine.Object.DestroyImmediate(warm);
            }

            public Texture2D Shoot(TileGallery.View v,int w,int h,bool coded=false)
            {
                float overlay=Shader.GetGlobalFloat("_TileOverlay");
                Shader.SetGlobalFloat("_TileOverlay",coded?1:0);
                cam.transform.SetPositionAndRotation(v.position,Quaternion.LookRotation(v.target-v.position,v.up));
                cam.fieldOfView=v.fov;
                cam.farClipPlane=Vector3.Distance(v.position,v.target)+200f;   // also the shadow distance
                var big=RenderTexture.GetTemporary(w*2,h*2,24,RenderTextureFormat.ARGB32);big.filterMode=FilterMode.Bilinear;
                var small=RenderTexture.GetTemporary(w,h,0,RenderTextureFormat.ARGB32);
                cam.targetTexture=big;cam.Render();cam.targetTexture=null;
                Graphics.Blit(big,small);
                var prev=RenderTexture.active;RenderTexture.active=small;
                var tex=new Texture2D(w,h,TextureFormat.RGBA32,false);tex.ReadPixels(new Rect(0,0,w,h),0,0);tex.Apply();
                RenderTexture.active=prev;
                RenderTexture.ReleaseTemporary(big);RenderTexture.ReleaseTemporary(small);
                Shader.SetGlobalFloat("_TileOverlay",overlay);
                return tex;
            }

            public void Dispose()
            {
                Stage.Destroy();
                EditorSceneManager.ClosePreviewScene(scene);
                TileLook.Restore(environment);
                ShaderUtil.allowAsyncCompilation=asyncCompile;
            }
        }
    }
}
