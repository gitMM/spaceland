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
            if(mode=="validate")return "DONE "+SettlementValidation.Run()+"\n"+SettlementValidation.FoundryInteraction(lib)+"\n"+BlockFoundryValidation.Run();
            using var rig=new Rig(lib);
            if(mode=="warmup"){var t=rig.Shoot(TileGallery.Fragment,320,180);UnityEngine.Object.DestroyImmediate(t);return "DONE warmup";}
            if(mode=="snap")return Snap(rig,opts);
            if(mode=="foundry"||mode=="limit"||mode=="truss")
            {
                var checks=new List<(bool ok,string text)>();var shots=new List<(string file,string what)>();
                List<string> notes;
                if(mode=="foundry")notes=FoundryReport(rig,checks,shots);
                else
                {
                    notes=new List<string>();rig.Stage.root.SetActive(false);
                    try{if(mode=="limit")LimitReport(rig,checks,shots,notes);else TrussReport(rig,checks,shots,notes);}finally{rig.Stage.root.SetActive(true);}
                }
                return "DONE "+string.Join("\n",checks.Select(c=>(c.ok?"PASS ":"FAIL ")+c.text).Concat(notes));
            }
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
            checks.Add((stage.triangles<=BudgetStageTriangles,$"stage triangles within budget: {stage.triangles:N0} (≤ {BudgetStageTriangles:N0})"));
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
                shots.Add((sheet,what+": left to right, top to bottom: Habitat, Industrial, Power, Life Support / Storage, Defense, Command, Corridor / Landing Pad, Conduit, Hull, Truss; each specimen sits in a different orientation"));
            }

            var foundryNotes=FoundryReport(rig,checks,shots);
            md.AppendLine("## Foundry: cell shapes and rebuild time");
            md.AppendLine();
            foreach(var note in foundryNotes)md.AppendLine("- "+note);
            md.AppendLine();
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
        static string Split(FoundrySettlement.Timings t,int n=1)=>
            $"solve {t.solve/n:0.0}, forms and relief {t.forms/n:0.0}, carving and meshes {t.meshes/n:0.0}, bend {t.bend/n:0.0}, scene objects {t.objects/n:0.0} ms";
        static string ShapeCounts(BlockConstruction model)
        {
            var counts=new SortedDictionary<string,int>();int gables=0;
            foreach(var c in model.Cells)
            {
                var s=model.Shape(c);var e=model.Exposure(c,out bool gable);if(gable)gables++;
                string key=s.kind==CellShape.Box?(s.Small?"small box":"box"):(s.kind+" "+e).ToLowerInvariant();
                counts[key]=counts.TryGetValue(key,out var n)?n+1:1;
            }
            return string.Join(", ",counts.Select(p=>$"{p.Key} {p.Value}"))+$"; {gables} with an open gable";
        }
        // Phase 0 baseline (doc/curved-grid-tiles-plan.md) and the budgets later phases are held to.
        // Editor timings vary up to ~25% between runs (slowest just after a recompile, or after the full stage in a
        // report), so budgets sit 25-30% over Phase 0: a full Foundry rebuild 1.94 s; single edits p50 0.62 s,
        // p95 0.97-1.15 s.
        const double BudgetFoundryMs=1940*1.25,BudgetEditP50Ms=800,BudgetEditP95Ms=1450;
        const long BudgetStageTriangles=(long)(947310*1.1);

        // Every fixed-size part of a full rebuild (measureFixedParts) comes out at its authored size, within 5%.
        static void ScaleCheck(string example,FoundrySettlement view,List<(bool ok,string text)> checks,List<string> notes)
        {
            var sizes=new SortedDictionary<string,(int n,float min,float max)>();
            foreach(var (name,ratio) in view.fixedPartSizes)
            {
                (int n,float min,float max) r=sizes.TryGetValue(name,out var o)?o:(0,float.MaxValue,0f);
                sizes[name]=(r.n+1,Mathf.Min(r.min,ratio),Mathf.Max(r.max,ratio));
            }
            notes.Add($"{example} example, fixed-size parts (true size over authored size): "+
                string.Join(", ",sizes.Select(p=>$"{p.Key} ×{p.Value.n} {p.Value.min:0.00}–{p.Value.max:0.00}")));
            var off=sizes.Where(p=>p.Value.min<0.95f||p.Value.max>1.05f).Select(p=>p.Key).ToList();
            checks.Add((off.Count==0,$"{example}: {sizes.Values.Sum(v=>v.n)} fixed-size parts come out within 5% of their authored size"+
                (off.Count>0?" (outside: "+string.Join(", ",off)+")":"")));
        }

        // Corridor and conduit tubes (not elbows, which stay in station space): at every face a run leaves through,
        // the cross-section comes out at its authored width, and two tubes meeting there share their vertices. Uses
        // the far mesh, whose armour is plain quads.
        static void ConnectorCheck(string example,BlockConstruction model,List<(bool ok,string text)> checks,List<string> notes)
        {
            var structure=SettlementSolver.Solve(model);var warp=new CurvedTileMesh(model);var builder=new TileBuilder(structure,warp);
            var sides=new Dictionary<(Vector3Int,int),List<(TileFamily family,List<Vector3> ring)>>();
            float minW=float.MaxValue,maxW=0;int rings=0;
            foreach(var pair in structure.cells)
            {
                var cell=pair.Key;var p=pair.Value;
                if(p.family!=TileFamily.Corridor&&p.family!=TileFamily.Conduit)continue;
                var source=builder.Build(cell);if(source.VertexCount==0||source.tubeParts.Count==0)continue;
                if(source.tubeParts.Exists(t=>t.name=="conduit flange"))continue;
                var (near,far)=source.ToMeshes("connector check");var mesh=far??near;
                var anchors=new List<Vector4>();mesh.GetUVs(5,anchors);
                var before=mesh.vertices;warp.Bend(near,cell);if(far!=null)warp.Bend(far,cell,near,source.SharedVertices);var after=mesh.vertices;
                foreach(var d in TileStructure.Directions)
                {
                    // Tube vertices only (anchor w 2): saddle feet on a conduit's bed face stretch with the cell.
                    var ring=new List<int>();
                    for(int i=0;i<before.Length;i++)if(anchors[i].w>1.5f&&Mathf.Abs(Vector3.Dot(before[i],d)-TileBuilder.Half)<1e-4f)ring.Add(i);
                    if(ring.Count<3)continue;
                    float sw=0,ww=0;
                    foreach(int a in ring)foreach(int b in ring){float s=(before[a]-before[b]).sqrMagnitude;if(s>sw){sw=s;ww=(after[a]-after[b]).magnitude;}}
                    float ratio=ww/Mathf.Max(1e-4f,Mathf.Sqrt(sw));minW=Mathf.Min(minW,ratio);maxW=Mathf.Max(maxW,ratio);rings++;
                    var other=cell+d;int axis=d.x!=0?0:d.y!=0?1:2;var key=(BlockConstruction.Compare(cell,other)<0?cell:other,axis);
                    if(!sides.TryGetValue(key,out var list))sides[key]=list=new List<(TileFamily,List<Vector3>)>();
                    list.Add((p.family,ring.Select(i=>after[i]).ToList()));
                }
                UnityEngine.Object.DestroyImmediate(near);if(far!=null)UnityEngine.Object.DestroyImmediate(far);
            }
            // A corridor beside a conduit isn't meant to line up: only joints within one family.
            float gap=0;int joints=0;
            foreach(var list in sides.Values)
            {
                if(list.Count!=2||list[0].family!=list[1].family)continue;joints++;
                foreach(var (a,b) in new[]{(list[0].ring,list[1].ring),(list[1].ring,list[0].ring)})
                    foreach(var v in a)gap=Mathf.Max(gap,b.Min(w=>(w-v).magnitude));
            }
            if(rings==0){notes.Add($"{example} example: no corridor or conduit tubes");return;}
            notes.Add($"{example} example, connectors: {rings} tube ends at {minW:0.000}–{maxW:0.000} of their authored width; {joints} joints between tubes, widest gap {gap*1000:0.00} mm");
            checks.Add((minW>=0.98f&&maxW<=1.02f,$"{example}: corridor and conduit cross-sections within 2% of true size ({minW:0.000}–{maxW:0.000})"));
            checks.Add((gap<=0.001f,$"{example}: tubes meeting across a face line up within 1 mm ({joints} joints, widest gap {gap*1000:0.00} mm)"));
        }

        // Wedge and apex cells build their shape's kit (a crest on every ridge, a hub on every crown, a keel strip and
        // gable plates where open) and no box family forms (slope hatches aside).
        static void KitCheck(string example,BlockConstruction model,List<(bool ok,string text)> checks,List<string> notes)
        {
            var structure=SettlementSolver.Solve(model);var builder=new TileBuilder(structure,new CurvedTileMesh(model));
            var counts=new SortedDictionary<string,int>();int cells=0,missing=0,forms=0;
            foreach(var cell in structure.special.Keys)
            {
                if(!structure.TryGet(cell,out var p)||p.family==TileFamily.Corridor||p.family==TileFamily.Conduit)continue;
                var exposure=model.Exposure(cell,out bool gable);if(exposure==CellExposure.Buried)continue;
                var mesh=builder.Build(cell);cells++;
                foreach(var part in mesh.kitParts)counts[part.name]=counts.TryGetValue(part.name,out var n)?n+1:1;
                bool Has(string name)=>mesh.kitParts.Exists(k=>k.name==name);
                if(exposure==CellExposure.Ridge&&!Has("crest")||exposure==CellExposure.Crown&&!Has("crown")||
                   exposure==CellExposure.Keel&&!Has("keel")||gable&&!Has("gable"))missing++;
                if(mesh.fixedParts.Exists(f=>f.name!="hatch"))forms++;
            }
            notes.Add($"{example} example, kits on {cells} open wedge and apex cells: "+string.Join(", ",counts.Select(c=>$"{c.Key} {c.Value}")));
            checks.Add((missing==0&&forms==0,$"{example}: every open wedge and apex cell builds its kit and no box forms ({missing} missing a part, {forms} with box forms)"));
        }

        // Ten additions on open faces, each removed again, through the same TryEdit and incremental rebuild as a click.
        static (string text,double p50,double p95) EditTimings(ref BlockConstruction model,FoundrySettlement view)
        {
            var open=new SortedSet<Vector3Int>(Comparer<Vector3Int>.Create(BlockConstruction.Compare));
            foreach(var c in model.Cells)foreach(var step in BlockConstruction.Steps)if(model.CanAdd(c+step))open.Add(c+step);
            var picks=open.ToList();var ms=new List<double>();var sum=new FoundrySettlement.Timings();
            for(int i=0;i<10;i++)
            {
                var cell=picks[i*picks.Count/10];
                foreach(bool remove in new[]{false,true})
                {
                    var sw=Stopwatch.StartNew();
                    if(!model.TryEdit(cell,remove,out var next,TileGallery.Families[i%TileGallery.Families.Length]))break;
                    view.Rebuild(next,next.ChangedCells);ms.Add(sw.Elapsed.TotalMilliseconds);sum.Add(view.LastTimings);model=next;
                }
            }
            if(ms.Count==0)return ("Edits: none succeeded",double.MaxValue,double.MaxValue);
            var sorted=ms.OrderBy(v=>v).ToList();double P(double q)=>sorted[Mathf.Clamp(Mathf.CeilToInt((float)(q*sorted.Count))-1,0,sorted.Count-1)];
            return ($"Edits ({ms.Count}: 10 additions on open faces, each removed again): p50 {P(0.5):0} ms, p95 {P(0.95):0} ms, max {sorted[sorted.Count-1]:0} ms; "+
                $"{sum.tiles/(float)ms.Count:0.0} tiles rebuilt per edit; per edit {Split(sum,ms.Count)}",P(0.5),P(0.95));
        }
        static List<string> FoundryReport(Rig rig,List<(bool ok,string text)> checks,List<(string file,string what)> shots)
        {
            var notes=new List<string>();
            checks.Add((true,SettlementValidation.Run()));
            checks.Add((true,SettlementValidation.FoundryInteraction(rig.Library)));
            rig.Stage.root.SetActive(false);
            var root=new GameObject("Foundry lab");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var model=SettlementExamples.Foundry();var view=new FoundrySettlement(root.transform,rig.Library.materials);
            try
            {
                var timer=Stopwatch.StartNew();view.measureFixedParts=true;view.Rebuild(model);view.measureFixedParts=false;
                checks.Add((view.MeshCount>0,$"Foundry: {model.Count} family cells, {view.MeshCount} visible tiles, built in {timer.ElapsedMilliseconds} ms"));
                var full=view.LastTimings;
                notes.Add($"Foundry example, cells: {ShapeCounts(model)}");
                notes.Add($"Foundry example, full rebuild: {full.Total:0} ms for {full.tiles} tiles ({full.Total/Mathf.Max(1,full.tiles):0.0} ms per tile): {Split(full)}");
                checks.Add((full.Total<=BudgetFoundryMs,$"Foundry full rebuild within budget: {full.Total:0} ms (≤ {BudgetFoundryMs:0})"));
                ScaleCheck("Foundry",view,checks,notes);
                ConnectorCheck("Foundry",model,checks,notes);
                KitCheck("Foundry",model,checks,notes);
                TileLook.AddLights(root.transform);
                var bounds=view.Bounds;var center=bounds.center;float radius=bounds.extents.magnitude;
                var warp=new CurvedTileMesh(model);
                var views=new[]{
                    ("foundry",new TileGallery.View(center+new Vector3(-0.8f,0.65f,-1).normalized*radius*2.9f,center,Vector3.up,40),"automatically solved families on the Foundry sphere grid"),
                    ("foundry-trench",new TileGallery.View(center+new Vector3(-28,20,-37),warp.Map(new Vector3(-3,1,-3)*5),Vector3.up,48),"joined habitats, bridge tubes, tower and pad deck"),
                    ("foundry-under",new TileGallery.View(center+new Vector3(-0.8f,-0.9f,-0.7f).normalized*radius*2.8f,center,Vector3.down,42),"the same rules underneath; no world up"),
                    ("foundry-back",new TileGallery.View(center+new Vector3(0.9f,0.6f,1).normalized*radius*2.8f,center,Vector3.up,42),"vault row, conduit bend and solid bridge arch")};
                foreach(var (name,camera,description) in views)
                {
                    var t=rig.Shoot(camera,320,180);UnityEngine.Object.DestroyImmediate(t);
                    t=rig.Shoot(camera,W,H);Save(t,Path.Combine(Dir,name+".png"));UnityEngine.Object.DestroyImmediate(t);
                    shots.Add((name+".png",description));
                }
                view.SetColorCoding(true);
                // Global overlay stays off: exercise the same per-renderer control as the Foundry's C key.
                var coded=rig.Shoot(views[0].Item2,W,H);Save(coded,Path.Combine(Dir,"foundry-coded.png"));
                int coloredRenderers=SettlementValidation.CheckDebugRenderers(root,true);
                view.SetColorCoding(false);
                var uncoded=rig.Shoot(views[0].Item2,W,H);
                float changed=ChangedPixels(uncoded,coded);
                checks.Add((changed>0.005f,$"Foundry debug colours visibly change {changed:P1} of pixels with the global switch off; {coloredRenderers} near/far renderers retain correct family data"));
                UnityEngine.Object.DestroyImmediate(uncoded);UnityEngine.Object.DestroyImmediate(coded);
                shots.Add(("foundry-coded.png","family choices on the same curved geometry"));
                foreach(var lod in root.GetComponentsInChildren<LODGroup>())lod.ForceLOD(1);
                uncoded=rig.Shoot(views[0].Item2,W,H);view.SetColorCoding(true);
                coded=rig.Shoot(views[0].Item2,W,H);Save(coded,Path.Combine(Dir,"foundry-coded-far.png"));
                changed=ChangedPixels(uncoded,coded);checks.Add((changed>0.005f,$"Foundry far LOD debug colours visibly change {changed:P1} of pixels"));
                UnityEngine.Object.DestroyImmediate(uncoded);UnityEngine.Object.DestroyImmediate(coded);
                view.SetColorCoding(false);SettlementValidation.CheckDebugRenderers(root,false);
                foreach(var lod in root.GetComponentsInChildren<LODGroup>())lod.ForceLOD(-1);
                shots.Add(("foundry-coded-far.png","same family colours with every tile forced to its far LOD"));
                view.SetDebugView(DebugView.Shape);
                var shaped=rig.Shoot(views[0].Item2,W,H);Save(shaped,Path.Combine(Dir,"foundry-shapes.png"));UnityEngine.Object.DestroyImmediate(shaped);
                view.SetDebugView(DebugView.Off);
                shots.Add(("foundry-shapes.png","cell shapes: grey box, red box with an edge under 3.2 m, orange wedge, magenta apex"));
                var (editText,p50,p95)=EditTimings(ref model,view);notes.Add(editText);
                checks.Add((p50<=BudgetEditP50Ms&&p95<=BudgetEditP95Ms,$"Single edits within budget: p50 {p50:0} ms (≤ {BudgetEditP50Ms:0}), p95 {p95:0} ms (≤ {BudgetEditP95Ms:0})"));
                var padTarget=warp.Map(new Vector3(0.5f,1.5f,-3.5f)*5);
                var padView=new TileGallery.View(padTarget+new Vector3(-10,14,-15),padTarget,Vector3.up,43);
                var complete=rig.Shoot(padView,W,H);Save(complete,Path.Combine(Dir,"foundry-pad-complete.png"));UnityEngine.Object.DestroyImmediate(complete);
                if(!model.TryEdit(new Vector3Int(1,1,-4),true,out var partial))throw new InvalidOperationException("Lab pad removal failed");
                view.Rebuild(partial,partial.ChangedCells);
                var incomplete=rig.Shoot(padView,W,H);Save(incomplete,Path.Combine(Dir,"foundry-pad-incomplete.png"));UnityEngine.Object.DestroyImmediate(incomplete);
                shots.Add(("foundry-pad-complete.png","four family cells form one pad with a shared hatch"));
                shots.Add(("foundry-pad-incomplete.png","removing a quarter restores three separate pads"));
            }
            finally{view.Dispose();UnityEngine.Object.DestroyImmediate(root);}
            try{RidgesReport(rig,checks,shots,notes);LimitReport(rig,checks,shots,notes);StationReport(rig,checks,shots,notes);TrussReport(rig,checks,shots,notes);}
            finally{rig.Stage.root.SetActive(true);}
            return notes;
        }
        // The whole station: every family on every shape (SettlementExamples.Station).
        static void StationReport(Rig rig,List<(bool ok,string text)> checks,List<(string file,string what)> shots,List<string> notes)
        {
            var root=new GameObject("Station lab");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var model=SettlementExamples.Station();var view=new FoundrySettlement(root.transform,rig.Library.materials);
            try
            {
                view.measureFixedParts=true;view.Rebuild(model);view.measureFixedParts=false;var full=view.LastTimings;
                notes.Add($"Station example, cells: {ShapeCounts(model)}");
                notes.Add($"Station example, full rebuild: {full.Total:0} ms for {full.tiles} tiles ({full.Total/Mathf.Max(1,full.tiles):0.0} ms per tile): {Split(full)}");
                ScaleCheck("Station",view,checks,notes);
                KitCheck("Station",model,checks,notes);
                var landmarks=new[]{"dish","reactor","sphere tank","pad quarter"};
                var missing=landmarks.Where(l=>!view.fixedPartSizes.Exists(p=>p.name.StartsWith(l))).ToList();
                checks.Add((missing.Count==0,"Station: the 2 x 2 landmarks build (dish, reactor, sphere tank, big pad)"+(missing.Count>0?"; missing "+string.Join(", ",missing):"")));
                TileLook.AddLights(root.transform);
                var warp=new CurvedTileMesh(model);
                var bounds=view.Bounds;var center=bounds.center;float radius=bounds.extents.magnitude;
                var lit=(new Vector3(-1,1,-1).normalized+new Vector3(0.35f,-0.1f,-0.3f)).normalized;
                var front=new TileGallery.View(center+lit*radius*1.95f,center,Vector3.up,42);
                var back=new TileGallery.View(center-lit*radius*1.95f,center,Vector3.up,42);
                var t=rig.Shoot(front,320,180);UnityEngine.Object.DestroyImmediate(t);
                t=rig.Shoot(front,W,H);Save(t,Path.Combine(Dir,"station.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("station.png","the whole station from its lit side: every family on ridges, crowns and faces"));
                foreach(var (mode,file,what) in new[]{(DebugView.Family,"station-coded.png","the same, coloured by family"),(DebugView.Shape,"station-shapes.png","the same, coloured by cell shape")})
                {
                    view.SetDebugView(mode);t=rig.Shoot(front,W,H);Save(t,Path.Combine(Dir,file));UnityEngine.Object.DestroyImmediate(t);shots.Add((file,what));
                }
                view.SetDebugView(DebugView.Off);
                t=rig.Shoot(back,W,H);Save(t,Path.Combine(Dir,"station-back.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("station-back.png","the far side, lit by the planet"));
                const int R=SettlementExamples.StationRadius;
                var spike=warp.Map((Vector3)new Vector3Int(-1,-1,1)*(R-1)*5);
                t=rig.Shoot(new TileGallery.View(spike*0.15f+Vector3.up*3,spike,Vector3.up,70),W,H);Save(t,Path.Combine(Dir,"station-inside.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("station-inside.png","inside the hollow: the spike under the Life Support crown"));
            }
            finally{view.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        // Trusses on a cap round a cube corner (SettlementExamples.Truss): a girder ridge with a jib, a lattice-tower
        // crown, a tower and boom, a frame on the surface, a pit and a bridge.
        static void TrussReport(Rig rig,List<(bool ok,string text)> checks,List<(string file,string what)> shots,List<string> notes)
        {
            var root=new GameObject("Truss lab");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var model=SettlementExamples.Truss();var view=new FoundrySettlement(root.transform,rig.Library.materials);
            try
            {
                view.Rebuild(model);var full=view.LastTimings;
                notes.Add($"Truss example, cells: {ShapeCounts(model)}");
                notes.Add($"Truss example, full rebuild: {full.Total:0} ms for {full.tiles} tiles ({full.Total/Mathf.Max(1,full.tiles):0.0} ms per tile): {Split(full)}");
                KitCheck("Truss",model,checks,notes);
                // Every truss cell builds a frame; none builds a fixed-size part; triangles per truss tile.
                var structure=SettlementSolver.Solve(model);var builder=new TileBuilder(structure,new CurvedTileMesh(model));
                int cells=0,empty=0,fixedParts=0,most=0;long total=0;
                foreach(var pair in structure.cells)
                {
                    if(pair.Value.family!=TileFamily.Truss)continue;
                    var mesh=builder.Build(pair.Key);cells++;total+=mesh.TriangleCount;most=Mathf.Max(most,mesh.TriangleCount);
                    if(!mesh.kitParts.Exists(k=>k.name=="truss"))empty++;
                    fixedParts+=mesh.fixedParts.Count;
                }
                notes.Add($"Truss example: {cells} truss tiles, {total/Mathf.Max(1,cells):N0} triangles each on average, {most:N0} at most");
                checks.Add((cells>0&&empty==0&&fixedParts==0,$"Truss: every truss tile builds its frame and no fixed-size parts ({empty} without a frame, {fixedParts} fixed parts)"));
                checks.Add((most<=12000,$"Truss: at most 12k triangles in a truss tile ({most:N0})"));
                TileLook.AddLights(root.transform);
                const int R=SettlementExamples.RidgeRadius;
                var warp=new CurvedTileMesh(model);
                Vector3 At(float x,float y,float z)=>warp.Map(Vector3.Scale(new Vector3(x,y,z),new Vector3(-5,5,-5)));
                var crown=At(R,R,R);var outward=crown.normalized;
                var side=Vector3.Cross(outward,Vector3.up).normalized;
                var cap=new TileGallery.View(crown+(outward+side*0.7f).normalized*80,crown-outward*10,Vector3.up,48);
                var t=rig.Shoot(cap,320,180);UnityEngine.Object.DestroyImmediate(t);
                t=rig.Shoot(cap,W,H);Save(t,Path.Combine(Dir,"truss.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("truss.png","trusses on a cap round a cube corner: the x ridge a girder to a lattice-tower crown, a tower with a boom, a frame on the surface, a pit and a bridge"));
                view.SetDebugView(DebugView.Family);t=rig.Shoot(cap,W,H);Save(t,Path.Combine(Dir,"truss-coded.png"));UnityEngine.Object.DestroyImmediate(t);view.SetDebugView(DebugView.Off);
                shots.Add(("truss-coded.png","the same by family: trusses yellow"));
                var towerTop=crown+outward*8;
                t=rig.Shoot(new TileGallery.View(towerTop+(outward*0.35f+side*0.8f+Vector3.Cross(side,outward)*0.45f).normalized*26,towerTop,outward,50),W,H);
                Save(t,Path.Combine(Dir,"truss-crown.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("truss-crown.png","the crown: a lattice tower where the three girders meet, platform, mast and beacon"));
                Vector3 r0=At(SettlementExamples.RidgeLow-1.5f,R,R),r1=At(2,R,R),mid=(r0+r1)*0.5f,ridgeUp=mid.normalized;
                var ridgeAcross=Vector3.Cross(ridgeUp,(r1-r0).normalized).normalized;
                t=rig.Shoot(new TileGallery.View(mid+ridgeUp*12+ridgeAcross*16,mid+ridgeUp*1.5f,ridgeUp,50),W,H);
                Save(t,Path.Combine(Dir,"truss-ridge.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("truss-ridge.png","along the x ridge: the triangular girder on the truss wedges, the end flange and the jib out over the cut edge"));
                var tw=SettlementExamples.TrussTower;var tower=At(tw.x+1,tw.y+1.5f,tw.z);var towerUp=At(tw.x,tw.y,tw.z).normalized;
                var towerSide=Vector3.Cross(towerUp,Vector3.forward).normalized;
                t=rig.Shoot(new TileGallery.View(tower+towerUp*8+towerSide*18-Vector3.Cross(towerSide,towerUp)*10,tower,towerUp,50),W,H);
                Save(t,Path.Combine(Dir,"truss-boom.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("truss-boom.png","a three-cell truss tower with a cantilevered boom, the 2 x 2 frame on the surface beyond"));
                // Close-ups, each looking at a target with the surface's outward normal up: from where (side), how far
                // and how high.
                TileGallery.View Close(Vector3 target,Vector3 toward,float dist,float height)
                {
                    var n=target.normalized;var s=(toward-target);s=(s-n*Vector3.Dot(s,n)).normalized;
                    return new TileGallery.View(target+s*dist+n*height,target,n,50);
                }
                var details=new[]
                {
                    Close(At(R+0.5f,2,2.5f),At(R+0.5f,-1,5),9,11),
                    Close(At(R+2,-1,1),At(R+2,-4,1.5f),16,4),
                    Close(At(2.5f,1.5f,R+1),At(-1,-2,R+1),13,8),
                    Close(At(SettlementExamples.RidgeLow-2,R+0.5f,R+0.5f),At(SettlementExamples.RidgeLow-2,R-3,R+3),11,5),
                };
                var detailSheet=new Texture2D(W,H,TextureFormat.RGBA32,false);
                for(int i=0;i<details.Length;i++)
                {
                    t=rig.Shoot(details[i],W/2,H/2);
                    detailSheet.SetPixels(i%2*(W/2),(1-i/2)*(H/2),W/2,H/2,t.GetPixels());UnityEngine.Object.DestroyImmediate(t);
                }
                detailSheet.Apply();Save(detailSheet,Path.Combine(Dir,"truss-details.png"));UnityEngine.Object.DestroyImmediate(detailSheet);
                shots.Add(("truss-details.png","close up: a two-cell truss pit in the skin on bearing plates; a truss bridge between Hull piers / a 2 x 2 frame on the surface leaning on a Habitat wall; the jib at the girder's cut end"));
            }
            finally{view.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        // The cap at the build limit: the flattest ridges and crown, and the narrowest cells holding a corridor and pads.
        static void LimitReport(Rig rig,List<(bool ok,string text)> checks,List<(string file,string what)> shots,List<string> notes)
        {
            var root=new GameObject("Limit lab");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var model=SettlementExamples.Limit();var view=new FoundrySettlement(root.transform,rig.Library.materials);
            try
            {
                view.measureFixedParts=true;view.Rebuild(model);view.measureFixedParts=false;var full=view.LastTimings;
                notes.Add($"Limit example, cells: {ShapeCounts(model)}");
                notes.Add($"Limit example, full rebuild: {full.Total:0} ms for {full.tiles} tiles: {Split(full)}");
                float narrow=float.MaxValue;
                foreach(var c in SettlementExamples.LimitCorridor.Concat(SettlementExamples.LimitPads))
                    narrow=Mathf.Min(narrow,model.Shape(SettlementExamples.RidgeCell(c.x,c.y,c.z)).minEdge);
                notes.Add($"Limit example: the corridor and pads stand in cells as narrow as {narrow:0.00} m");
                ScaleCheck("Limit",view,checks,notes);
                ConnectorCheck("Limit",model,checks,notes);
                KitCheck("Limit",model,checks,notes);
                var small=new[]{"pad M","pad S","pad quarter M","pad quarter S"};
                checks.Add((view.fixedPartSizes.Exists(p=>small.Contains(p.name)),"Limit: pads on narrow faces take a smaller size class"));
                TileLook.AddLights(root.transform);
                const int R=SettlementExamples.LimitRadius,LowCorner=SettlementExamples.LimitLow;
                var warp=new CurvedTileMesh(model);
                Vector3 At(float x,float y,float z)=>warp.Map(Vector3.Scale(new Vector3(x,y,z),new Vector3(-5,5,-5)));
                // At shell 63 the cap is nearly flat: seen from above, its three ridges read as a cube's edges, so
                // look along the surface instead.
                var crown=At(R+0.5f,R+0.5f,R+0.5f);var outward=crown.normalized;
                var corner=At(LowCorner,R+0.5f,LowCorner);var toward=(corner-crown-outward*Vector3.Dot(corner-crown,outward)).normalized;
                var cap=new TileGallery.View(crown+outward*14-toward*30,(crown+corner)*0.5f,outward,55);
                var t=rig.Shoot(cap,320,180);UnityEngine.Object.DestroyImmediate(t);
                t=rig.Shoot(cap,W,H);Save(t,Path.Combine(Dir,"limit.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("limit.png","the cap at the build limit (shell 63), along its nearly flat surface: crests on the ridges, a corridor node on the crown"));
                // From above, by family and by shape: the corridor runs beside the x ridge, the pads stand on red small boxes.
                var run0=At(58,62,R+0.5f);var run1=At(61,62,R+0.5f);var mid=At(58.5f,60.5f,R+0.5f);var up=mid.normalized;
                var along=(run1-run0-up*Vector3.Dot(run1-run0,up)).normalized;
                var plan=new TileGallery.View(mid+up*34,mid,along,50);
                foreach(var (mode,file,what) in new[]{(DebugView.Family,"limit-plan-coded.png","the narrowest cells from above, by family: corridor (pink) beside the x ridge, two single pads and a 2 x 2 pad"),
                    (DebugView.Shape,"limit-plan-shapes.png","the same by cell shape: the corridor and pads stand on red small boxes")})
                {
                    view.SetDebugView(mode);t=rig.Shoot(plan,W,H);Save(t,Path.Combine(Dir,file));UnityEngine.Object.DestroyImmediate(t);shots.Add((file,what));
                }
                view.SetDebugView(DebugView.Off);
                // Close up, looking down the run into its 2.4 m slot beside the x ridge, plain and by family.
                var across=Vector3.Cross(up,along);var tube=(run0+run1)*0.5f-up*2;
                var close=new TileGallery.View(tube+up*8+across*2-along*9,tube+along*2,up,50);
                var sheet=new Texture2D(W,H,TextureFormat.RGBA32,false);
                for(int i=0;i<2;i++)
                {
                    view.SetDebugView(i==0?DebugView.Off:DebugView.Family);t=rig.Shoot(close,W/2,H);
                    sheet.SetPixels(i*(W/2),0,W/2,H,t.GetPixels());UnityEngine.Object.DestroyImmediate(t);
                }
                view.SetDebugView(DebugView.Off);
                sheet.Apply();Save(sheet,Path.Combine(Dir,"limit-narrow.png"));UnityEngine.Object.DestroyImmediate(sheet);
                shots.Add(("limit-narrow.png","close up down the corridor run in the 2.4 m cells beside the x ridge, plain and by family"));
            }
            finally{view.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        // The cap round a cube corner: every family on a ridge, a crown, a keel inside, a half-covered roof, gables.
        static void RidgesReport(Rig rig,List<(bool ok,string text)> checks,List<(string file,string what)> shots,List<string> notes)
        {
            var root=new GameObject("Ridges lab");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var model=SettlementExamples.Ridges();var view=new FoundrySettlement(root.transform,rig.Library.materials);
            try
            {
                view.measureFixedParts=true;view.Rebuild(model);view.measureFixedParts=false;var full=view.LastTimings;
                notes.Add($"Ridges example, cells: {ShapeCounts(model)}");
                notes.Add($"Ridges example, full rebuild: {full.Total:0} ms for {full.tiles} tiles ({full.Total/Mathf.Max(1,full.tiles):0.0} ms per tile): {Split(full)}");
                TileLook.AddLights(root.transform);
                const int R=SettlementExamples.RidgeRadius;
                var warp=new CurvedTileMesh(model);
                Vector3 At(int x,int y,int z)=>warp.Map((Vector3)SettlementExamples.RidgeCell(x,y,z)*5);
                var crown=At(R,R,R);var outward=crown.normalized;
                var side=Vector3.Cross(outward,Vector3.up).normalized;
                var cap=new TileGallery.View(crown+(outward+side*0.7f).normalized*80,crown-outward*10,Vector3.up,48);
                var keel=At(R-1,R-1,1);
                var inside=new TileGallery.View(keel*0.25f+side*6,keel,Vector3.up,60);
                var t=rig.Shoot(cap,320,180);UnityEngine.Object.DestroyImmediate(t);
                var plain=rig.Shoot(cap,W,H);Save(plain,Path.Combine(Dir,"ridges.png"));
                shots.Add(("ridges.png","three ridges meet at a crown above a cube corner, one two-cell run per family (today's box tiles, before the shape kits)"));
                view.SetDebugView(DebugView.Shape);
                var shaped=rig.Shoot(cap,W,H);Save(shaped,Path.Combine(Dir,"ridges-shapes.png"));
                view.SetDebugView(DebugView.Off);
                float changed=ChangedPixels(plain,shaped);
                checks.Add((changed>0.005f,$"Ridges shape view visibly changes {changed:P1} of pixels"));
                UnityEngine.Object.DestroyImmediate(plain);UnityEngine.Object.DestroyImmediate(shaped);
                shots.Add(("ridges-shapes.png","the same in the shape view: wedges orange, the crown magenta, small boxes red"));
                t=rig.Shoot(inside,W,H);Save(t,Path.Combine(Dir,"ridges-keel.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("ridges-keel.png","from inside the cap: a run of wedges under the z ridge shows its sharp keel"));
                // Each comparison tile from the same place relative to its own outer face (-x on this cap): 2 x 2, the
                // face-centre cell left and the cell beside a ridge right, Defense above and Command below.
                var sheet=new Texture2D(W,H,TextureFormat.RGBA32,false);
                for(int i=0;i<SettlementExamples.ScaleCells.Length;i++)
                {
                    var (y,z,_)=SettlementExamples.ScaleCells[i];var cell=(Vector3)SettlementExamples.RidgeCell(R,y,z)*5;
                    var face=warp.Map(cell+Vector3.left*2.5f);var up=(face-warp.Map(cell)).normalized;
                    var across=(Vector3.forward-up*Vector3.Dot(Vector3.forward,up)).normalized;
                    t=rig.Shoot(new TileGallery.View(face+up*7+across*9,face+up*1.8f,up,50),W/2,H/2);
                    sheet.SetPixels(i%2*(W/2),(1-i/2)*(H/2),W/2,H/2,t.GetPixels());UnityEngine.Object.DestroyImmediate(t);
                }
                sheet.Apply();Save(sheet,Path.Combine(Dir,"foundry-scale.png"));UnityEngine.Object.DestroyImmediate(sheet);
                shots.Add(("foundry-scale.png","Defense (top) and Command (bottom) on a cube-face-centre cell (left) and on a smaller cell beside a ridge (right), each seen from the same place relative to its face: turret, bridge and globes keep their true size in a smaller class"));
                // Close-up across the z ridge's corridor and conduit runs (cells z -2..1), the ridge's outward normal up.
                Vector3 r0=At(R,R,-2),r1=At(R,R,1),mid=(r0+r1)*0.5f,ridgeUp=mid.normalized;
                var ridgeAcross=Vector3.Cross(ridgeUp,(r1-r0).normalized).normalized;
                t=rig.Shoot(new TileGallery.View(mid+ridgeUp*15+ridgeAcross*11,mid+ridgeUp*0.5f,ridgeUp,50),W,H);
                Save(t,Path.Combine(Dir,"ridges-connectors.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("ridges-connectors.png","a corridor run and a conduit run along a ridge: true-size tubes, collars and pipes"));
                // Close-up of the crown, where the three ridges' crests meet at its hub.
                var crownPoint=At(R,R,R)+outward*2.5f;
                t=rig.Shoot(new TileGallery.View(crownPoint+(outward*0.8f+side*0.6f+Vector3.Cross(side,outward)*0.3f).normalized*20,crownPoint,outward,45),W,H);
                Save(t,Path.Combine(Dir,"ridges-crown.png"));UnityEngine.Object.DestroyImmediate(t);
                shots.Add(("ridges-crown.png","the crown: a hub and beacon where the three ridges' crests meet"));
                ScaleCheck("Ridges",view,checks,notes);
                ConnectorCheck("Ridges",model,checks,notes);
                KitCheck("Ridges",model,checks,notes);
                // Each run on the ridges close up, in RidgeRuns order (x ridge, y ridge, z ridge; four runs each).
                var runSheet=new Texture2D(W,H,TextureFormat.RGBA32,false);int idx=0;
                for(int axis=0;axis<3;axis++)for(int run=0;run<4;run++,idx++)
                {
                    var ca=new Vector3Int(R,R,R);ca[axis]=SettlementExamples.RidgeLow+run*2;var cb=ca;cb[axis]+=1;
                    Vector3 pa=At(ca.x,ca.y,ca.z),pb=At(cb.x,cb.y,cb.z),runMid=(pa+pb)*0.5f,runUp=runMid.normalized;
                    var runAcross=Vector3.Cross(runUp,(pb-pa).normalized).normalized;
                    t=rig.Shoot(new TileGallery.View(runMid+runUp*8+runAcross*7,runMid+runUp*1.2f,runUp,55),W/4,H/3);
                    runSheet.SetPixels(idx%4*(W/4),(2-idx/4)*(H/3),W/4,H/3,t.GetPixels());UnityEngine.Object.DestroyImmediate(t);
                }
                runSheet.Apply();Save(runSheet,Path.Combine(Dir,"ridges-sheet.png"));UnityEngine.Object.DestroyImmediate(runSheet);
                shots.Add(("ridges-sheet.png","every ridge run close up: x ridge Habitat, Defense, Command, Power; y ridge Hull, Storage, Pad, Industrial; z ridge Corridor, Conduit, Life Support, Truss"));
                // The crown in each family with a kit of its own, rebuilt in place.
                var crowns=new[]{TileFamily.Hull,TileFamily.Habitat,TileFamily.Defense,TileFamily.Command,TileFamily.Power,TileFamily.LifeSupport,TileFamily.Storage,TileFamily.Pad,TileFamily.Industrial,TileFamily.Truss};
                var crownSheet=new Texture2D(W,H,TextureFormat.RGBA32,false);crownSheet.SetPixels32(new Color32[W*H]);
                var crownView=new TileGallery.View(crownPoint+(outward*0.8f+side*0.6f+Vector3.Cross(side,outward)*0.3f).normalized*20,crownPoint,outward,45);
                for(int i=0;i<crowns.Length;i++)
                {
                    view.Rebuild(SettlementExamples.Ridges(crowns[i]),new[]{SettlementExamples.RidgeCell(R,R,R)});
                    t=rig.Shoot(crownView,W/4,H/3);
                    crownSheet.SetPixels(i%4*(W/4),(2-i/4)*(H/3),W/4,H/3,t.GetPixels());UnityEngine.Object.DestroyImmediate(t);
                }
                crownSheet.Apply();Save(crownSheet,Path.Combine(Dir,"crowns-sheet.png"));UnityEngine.Object.DestroyImmediate(crownSheet);
                shots.Add(("crowns-sheet.png","the crown in each family, left to right and top to bottom: Hull, Habitat, Defense, Command / Power, Life Support, Storage, Landing Pad / Industrial, Truss (lattice tower)"));
            }
            finally{view.Dispose();UnityEngine.Object.DestroyImmediate(root);}
        }
        static void Save(Texture2D t,string path)=>File.WriteAllBytes(path,t.EncodeToPNG());
        static float Mean(Texture2D t){var px=t.GetPixels32();double s=0;foreach(var p in px)s+=(p.r*0.2126+p.g*0.7152+p.b*0.0722)/255.0;return (float)(s/px.Length);}
        static float ChangedPixels(Texture2D a,Texture2D b)
        {
            var aa=a.GetPixels32();var bb=b.GetPixels32();int changed=0;
            for(int i=0;i<aa.Length;i++)if(Mathf.Abs(aa[i].r-bb[i].r)+Mathf.Abs(aa[i].g-bb[i].g)+Mathf.Abs(aa[i].b-bb[i].b)>32)changed++;
            return changed/(float)aa.Length;
        }

        sealed class Rig:IDisposable
        {
            readonly Scene scene;
            readonly Camera cam;
            readonly TileLook.Saved environment;
            readonly bool asyncCompile;
            public readonly TileGallery.Stage Stage;
            public Scene PreviewScene=>scene;
            public readonly TileLibrary Library;

            public Rig(TileLibrary lib)
            {
                Library=lib;
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
