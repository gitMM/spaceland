using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace DeathStar.EditorTools
{
    // Close-up detail checks (doc/close-up-detail-plan.md): the gallery stage at fixed distances from a surface, with
    // a feature off and on side by side, written to DeathStarRuns/Tiles/closeup/ with a markdown summary per part.
    //   closeup part=contact    sun contact shadows at 20, 60 and 150 m under a low sun
    //   closeup part=surface    the close-up surface (edge bevels, highlights, chips, creases) at 1, 5 and 20 m, and
    //                           what the edge data costs in vertex memory and build time
    //   closeup part=detail     fine detail and the painted metal at 1 and 5 m, and shimmer while the camera creeps
    //                           sideways at 20 m (unsupersampled frames, detail off and on)
    //   closeup part=kit        props near the player at 3 and 8 m, a walk over the hull (pops: grow-in against a
    //                           hard ring edge), and what the kit costs a frame
    //   closeup part=relief     the relief trace against the mesh carving from 20 to 250 m, the far level as it was
    //                           (plain armour), and the triangles each view draws with the mid level against without
    //   closeup part=fold       measure the carving's average effect once its plates have sunk (TileReliefFold.hlsl)
    //   closeup part=wear       Edge Wear Width at 1, 4 and 10, close up
    //   closeup part=bevel      Bevel Width 0.5, 1 and 3 rounded, and 3 chopped (Bevel Roundness 0), close up
    //   closeup part=reliefview the relief view (carving orange, traced plates cyan) over gallery views and two close spots
    //   closeup part=nan        Foundry (bent) tiles at 12, 30, 60 and 150 m (far level): no NaN or infinite pixels, each feature off in turn
    //   closeup part=orbit      the station from 200 m to orbit: tiles against chunks, each against tiles rendered at
    //                           16 samples a pixel
    //   closeup part=greeble    the station relief in the far level, traced in its shells, against the mid level's
    //                           mesh relief and the plain face, 110-400 m; no NaN; triangles and a rough frame cost
    //   closeup part=precision  close spots with the stage at the origin and moved 4 km away: the same picture (fine
    //                           detail used to turn to speckle far from the origin)
    public static partial class TileLab
    {
        static string CloseDir=>Path.Combine(Dir,"closeup");

        // Where the close-up cameras look: the surface a ray meets (from, to). The camera stands back toward from,
        // or toward back where the ray meets the surface square on, elevation degrees above it.
        // Specimen spots look at the family tile from its gallery view.
        static List<(string name,Vector3 from,Vector3 to,Vector3 back,float elevation)> Spots(Rig rig,params string[] names)
        {
            var all=new List<(string name,Vector3 from,Vector3 to,Vector3 back,float elevation)>
            {
                ("top",new Vector3(28,80,18),new Vector3(28,0,18),new Vector3(-1,0,-0.6f),30f),
                ("trench",TileGallery.Trench.position,TileGallery.Trench.target,Vector3.zero,24f),
            };
            foreach(var sp in rig.Stage.specimens)
            {
                if(sp.family!=TileFamily.Hull&&sp.family!=TileFamily.Defense&&sp.family!=TileFamily.Command)continue;
                var v=TileGallery.SpecimenView(sp,false);
                all.Add((sp.family.ToString().ToLowerInvariant(),v.position,v.target,Vector3.zero,35f));
            }
            return all.FindAll(a=>Array.IndexOf(names,a.name)>=0);
        }

        static string CloseUp(Rig rig,Dictionary<string,string> o)
        {
            if(!o.TryGetValue("part",out var part))part="contact";
            Directory.CreateDirectory(CloseDir);
            var md=new StringBuilder();var checks=new List<(bool ok,string text)>();
            var settings=StationLightingSettings.Current;
            var snapshot=JsonUtility.ToJson(settings);   // every part may change any setting; all come back after
            try
            {
                switch(part)
                {
                    case "contact":ContactCheck(rig,md,checks);break;
                    case "surface":SurfaceCheck(rig,md,checks);break;
                    case "detail":DetailCheck(rig,md,checks);break;
                    case "kit":KitCheck(rig,md,checks);break;
                    case "relief":ReliefCheck(rig,md,checks);break;
                    case "orbit":OrbitCheck(rig,md,checks);break;
                    case "precision":PrecisionCheck(rig,md,checks);break;
                    case "greeble":GreebleCheck(rig,md,checks);break;
                    case "wear":
                        SettingSheet(rig,"wear-width.png",()=>settings.edgeWearWidth=1,()=>settings.edgeWearWidth=4,()=>settings.edgeWearWidth=10);
                        md.AppendLine("# Edge wear width");md.AppendLine();md.AppendLine("Columns: Edge Wear Width 1, 4, 10. Rows: command and hull at 2 m.");
                        break;
                    case "bevel":
                        SettingSheet(rig,"bevel-width.png",()=>settings.bevelWidth=.5f,()=>settings.bevelWidth=1,()=>settings.bevelWidth=3,
                            ()=>{settings.bevelWidth=3;settings.bevelRoundness=0;});
                        md.AppendLine("# Bevel width");md.AppendLine();md.AppendLine("Columns: Bevel Width 0.5, 1, 3 rounded, 3 chopped (Bevel Roundness 0). Rows: command and hull at 2 m.");
                        break;
                    case "reliefview":
                    {
                        // The relief view over a few gallery views and two close spots: carving orange, traced plates cyan.
                        settings.reliefDebug=true;settings.kitProps=false;ApplyLights(rig);   // (builds the stage)
                        foreach(var (name,view,_) in Views)
                        {
                            if(name!="fragment"&&name!="surface"&&name!="trench"&&name!="station")continue;
                            var t=rig.Shoot(view(),W,H);Save(t,Path.Combine(CloseDir,"reliefview-"+name+".png"));UnityEngine.Object.DestroyImmediate(t);
                        }
                        foreach(var spot in Spots(rig,"defense","command"))
                        {
                            if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir))continue;
                            var t=rig.Shoot(Stand(hit,normal,dir,25,spot.elevation),W,H);Save(t,Path.Combine(CloseDir,$"reliefview-{spot.name}-25m.png"));UnityEngine.Object.DestroyImmediate(t);
                        }
                        md.AppendLine("# Relief view");md.AppendLine();md.AppendLine("Carving built as geometry orange, plates traced by the shader cyan (dimmer as they sink), the face between traced plates blue, everything else grey.");
                        break;
                    }
                    case "nan":
                    {
                        // A Foundry seed with a Defense and a Habitat tile beside it, seen from about 30 m (the Foundry's own view).
                        var root=new GameObject("Probe");UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
                        var row=new Dictionary<Vector3Int,TileFamily>{{Vector3Int.zero,TileFamily.Defense},{new Vector3Int(1,0,0),TileFamily.Habitat},{new Vector3Int(-1,0,0),TileFamily.Hull}};
                        var view=new FoundrySettlement(root.transform,rig.Library.materials);
                        try
                        {
                            view.Rebuild(BlockConstruction.FromFamilies(row));TileLook.AddLights(root.transform);
                            var b=view.Bounds;
                            foreach(float d in new[]{12f,30f,60f,150f})
                            {
                                var v=new TileGallery.View(b.center+new Vector3(-0.4f,0.5f,-1).normalized*d,b.center,Vector3.up,40);
                                var groups=root.GetComponentsInChildren<LODGroup>();
                                var r0=rig.ShootHdr(v,800,450);
                                foreach(var g in groups)g.fadeMode=LODFadeMode.None;var r1=rig.ShootHdr(v,800,450);foreach(var g in groups)g.fadeMode=LODFadeMode.CrossFade;
                                settings.reliefTrace=false;var r2=rig.ShootHdr(v,800,450);settings.reliefTrace=true;
                                settings.closeUpSurface=false;settings.fineDetail=false;var r3=rig.ShootHdr(v,800,450);settings.closeUpSurface=true;settings.fineDetail=true;
                                settings.kitProps=false;var r4=rig.ShootHdr(v,800,450);settings.kitProps=true;
                                settings.contactShadows=false;var r5=rig.ShootHdr(v,800,450);settings.contactShadows=true;
                                string F((float max,float bad,float hot) r)=>$"max {r.max:0.0} bad {r.bad:P2} hot {r.hot:P2}";
                                checks.Add((r0.bad==0,$"Foundry tiles at {d:0} m: no NaN or infinite pixels ({F(r0)}; without the relief trace {F(r2)}, the close-up surface {F(r3)}, the kit {F(r4)}, contact shadows {F(r5)}, crossfade {F(r1)})"));
                            }
                        }
                        finally{view.Dispose();UnityEngine.Object.DestroyImmediate(root);}
                        break;
                    }
                    case "fold":
                    {
                        string path=Path.GetFullPath(Path.Combine(Application.dataPath,"DeathStar/Shaders/TileReliefFold.hlsl"));
                        var clock=System.Diagnostics.Stopwatch.StartNew();
                        string summary=ReliefFold.Measure(path);
                        AssetDatabase.ImportAsset("Assets/DeathStar/Shaders/TileReliefFold.hlsl",ImportAssetOptions.ForceUpdate);
                        AssetDatabase.ImportAsset("Assets/DeathStar/Shaders/TileSurface.shader",ImportAssetOptions.ForceUpdate);
                        md.AppendLine("# Relief fold");md.AppendLine();md.AppendLine($"Measured in {clock.Elapsed.TotalSeconds:0.0} s: {summary}. Written to Shaders/TileReliefFold.hlsl.");
                        checks.Add((true,summary));
                        break;
                    }
                    default:return "ERROR unknown part "+part+" (contact, surface, detail)";
                }
            }
            finally
            {
                JsonUtility.FromJsonOverwrite(snapshot,settings);
                ForceLevel(rig.Stage.root.GetComponentsInChildren<LODGroup>(),-1);
                ApplyLights(rig);
            }
            md.AppendLine();md.AppendLine("## Checks");md.AppendLine();
            foreach(var (ok,text) in checks)md.AppendLine((ok?"- PASS ":"- FAIL ")+text);
            File.WriteAllText(Path.Combine(CloseDir,part+".md"),md.ToString());
            var lines=new List<string>();foreach(var (ok,text) in checks)lines.Add((ok?"PASS ":"FAIL ")+text);
            return "DONE "+Path.Combine(CloseDir,part+".md")+"\n"+string.Join("\n",lines);
        }

        static void ApplyLights(Rig rig)
        {
            foreach(var r in rig.Stage.root.GetComponentsInChildren<StationLightingRig>(true))r.Apply();
        }

        // Low sun from the side of the camera's line of sight, elevation degrees above the surface.
        static Vector3 SideSun(Vector3 normal,Vector3 dir,float elevation)
        {
            var side=Vector3.Cross(normal,Vector3.ProjectOnPlane(dir,normal)).normalized;
            float e=elevation*Mathf.Deg2Rad;
            return (normal*Mathf.Sin(e)+side*Mathf.Cos(e)).normalized;
        }

        static void ContactCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;
            float[] distances={20,60,150};
            md.AppendLine("# Sun contact shadows");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Sun 12° above each surface, from the side. Length {settings.contactLengthNear:0.##} m near to {settings.contactLengthFar:0.##} m at {settings.contactFarDistance:0} m, {settings.contactSteps} steps, thickness {settings.contactThickness:0.##} m.");
            md.AppendLine();
            md.AppendLine("Columns: off, on, the contact mask (white where the sun reaches). Rows: 20, 60, 150 m.");md.AppendLine();
            md.AppendLine("| Spot | Distance | Pixels changed | Mean darkening (of 255) |");
            md.AppendLine("|---|---:|---:|---:|");
            foreach(var spot in Spots(rig,"top","trench"))
            {
                string name=spot.name;
                if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir)){checks.Add((false,$"{name}: the spot's ray meets the stage"));continue;}
                settings.sunFrom=SideSun(normal,dir,12);ApplyLights(rig);
                var sheet=new List<Texture2D>();
                foreach(float d in distances)
                {
                    var v=Stand(hit,normal,dir,d,spot.elevation);
                    settings.contactShadows=false;var off=rig.Shoot(v,W,H);
                    settings.contactShadows=true;var on=rig.Shoot(v,W,H);
                    settings.giDebugView=GiDebugView.ContactShadowsOnly;var mask=rig.Shoot(v,W,H);settings.giDebugView=GiDebugView.Normal;
                    Save(off,Path.Combine(CloseDir,$"contact-{name}-{d:0}m-off.png"));Save(on,Path.Combine(CloseDir,$"contact-{name}-{d:0}m-on.png"));
                    var (changed,darker)=Darkening(off,on);
                    md.AppendLine($"| {name} | {d:0} m | {changed:P1} | {darker:0.00} |");
                    // They matter most in the middle band; up close the shadow map is already sharp.
                    if(d==60)checks.Add((changed>0.005f,$"{name} at 60 m: contact shadows darken some of the picture ({changed:P1} of pixels)"));
                    checks.Add((changed<0.15f,$"{name} at {d:0} m: contact shadows only touch contacts, no wholesale darkening ({changed:P1} of pixels)"));
                    sheet.Add(off);sheet.Add(on);sheet.Add(mask);
                }
                SaveSheet(sheet,3,Path.Combine(CloseDir,$"contact-{name}.png"));
                foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
            }
        }

        static void SurfaceCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;
            float[] distances={1,5,20};
            md.AppendLine("# Close-up surface");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Wear {settings.edgeWear:0.##}, highlight {settings.edgeHighlight:0.##}, crease {settings.creaseDarkness:0.##}. Sun 25° above each surface, from the side.");
            md.AppendLine();
            md.AppendLine("Columns: off, on, difference ×4. Rows: 1, 5, 20 m.");md.AppendLine();
            md.AppendLine("| Spot | Distance | Pixels changed |");
            md.AppendLine("|---|---:|---:|");
            foreach(var spot in Spots(rig,"top","hull","defense","command"))
            {
                string name=spot.name;
                if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir)){checks.Add((false,$"{name}: the spot's ray meets the stage"));continue;}
                settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
                var sheet=new List<Texture2D>();
                foreach(float d in distances)
                {
                    var v=Stand(hit,normal,dir,d,spot.elevation);
                    settings.closeUpSurface=false;var off=rig.Shoot(v,W,H);
                    settings.closeUpSurface=true;var on=rig.Shoot(v,W,H);
                    Save(off,Path.Combine(CloseDir,$"surface-{name}-{d:0}m-off.png"));Save(on,Path.Combine(CloseDir,$"surface-{name}-{d:0}m-on.png"));
                    var (changed,_)=Darkening(off,on);
                    md.AppendLine($"| {name} | {d:0} m | {changed:P1} |");
                    if(d<10)checks.Add((changed>0.01f,$"{name} at {d:0} m: the close-up surface shows ({changed:P1} of pixels change)"));
                    sheet.Add(off);sheet.Add(on);sheet.Add(Difference(off,on,4));
                }
                SaveSheet(sheet,3,Path.Combine(CloseDir,$"surface-{name}.png"));
                foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
            }

            // Cost: the edges add no vertices or triangles, only wider vertices and build time.
            var stage=rig.Stage;
            long vertices=0,bytes=0;int stride=0;
            foreach(var mesh in stage.meshes)
            {
                if(mesh==null||mesh.name.EndsWith(" far"))continue;
                vertices+=mesh.vertexCount;stride=0;
                for(int sIdx=0;sIdx<mesh.vertexBufferCount;sIdx++)stride+=mesh.GetVertexBufferStride(sIdx);
                bytes+=(long)mesh.vertexCount*stride;
            }
            var holder=new GameObject("Edges off");
            TileMesh.BuildEdges=false;
            TileGallery.Stage plain;
            try{plain=TileGallery.Build(holder.transform,rig.Library,false);}finally{TileMesh.BuildEdges=true;}
            TileMesh.EdgeTicks=0;var again=new GameObject("Edges on");var timed=TileGallery.Build(again.transform,rig.Library,false);
            double edgeMs=TileMesh.EdgeTicks*1000.0/System.Diagnostics.Stopwatch.Frequency,onMs=timed.buildMs;
            timed.Destroy();UnityEngine.Object.DestroyImmediate(again);
            double plainMs=plain.buildMs;plain.Destroy();UnityEngine.Object.DestroyImmediate(holder);
            var stats=TileMesh.EdgeStats;
            md.AppendLine();md.AppendLine("## Budget");md.AppendLine();
            md.AppendLine($"Gallery stage, near meshes: {stage.triangles:N0} triangles and {vertices:N0} vertices, unchanged by the edges.");
            md.AppendLine($"Vertex memory {bytes/1048576.0:0.0} MiB ({stride} bytes a vertex). The edge data widens uv0 to four floats and uv1 and uv2 from two to four: 24 bytes a vertex, {vertices*24/1048576.0:0.0} MiB for the stage.");
            md.AppendLine($"Build time {onMs:0} ms with edges ({edgeMs:0} ms of it finding them, Burst), {plainMs:0} ms without.");
            md.AppendLine($"Edges found since the editor loaded: {stats[1]:N0} convex, {stats[2]:N0} soft, {stats[3]:N0} concave, {stats[0]:N0} with nothing (seams, flat joins, open ends).");
            checks.Add((stage.triangles<=BudgetStageTriangles,$"stage triangles within budget: {stage.triangles:N0}"));
            checks.Add((edgeMs<onMs*0.3,$"finding edges takes under 30% of the stage build ({edgeMs:0} of {onMs:0} ms)"));
        }

        // The far level's station relief: traced in its shells (TileGreeble.hlsl) against the mid level's mesh relief.
        static void GreebleCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;var stage=rig.Stage;
            var groups=stage.root.GetComponentsInChildren<LODGroup>();
            void Force(int level)=>ForceLevel(groups,level);
            settings.kitProps=false;
            md.AppendLine("# Station relief in the far level");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Traced in shells; it sinks into the face from {settings.greebleLowerNear:0} to {settings.greebleLowerFar:0} m. Sun 25° above each surface, from the side.");
            md.AppendLine();
            md.AppendLine($"Stage: {stage.triangles:N0} triangles near, {stage.farTriangles:N0} far (with the relief as geometry the far level was about 315,000).");
            md.AppendLine();
            md.AppendLine("Columns: the mid level (relief as geometry), the far level (relief traced), the far level with the trace off (plain face), difference mid/far ×4. Pixels changed against the mid level.");
            md.AppendLine();md.AppendLine("| Spot | Distance | Traced | Plain |");md.AppendLine("|---|---:|---:|---:|");
            {
                foreach(var spot in Spots(rig,"top","trench","hull"))
                {
                    if(!Aim(stage.root,spot,out var hit,out var normal,out var dir)){checks.Add((false,$"{spot.name}: the spot's ray meets the stage"));continue;}
                    settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
                    var sheet=new List<Texture2D>();
                    foreach(float d in new[]{110f,160f,250f,400f})
                    {
                        var v=Stand(hit,normal,dir,d,spot.elevation);
                        Force(1);var mesh=rig.Shoot(v,W,H);
                        Force(2);var traced=rig.Shoot(v,W,H);
                        settings.greebleTrace=false;var plain=rig.Shoot(v,W,H);settings.greebleTrace=true;
                        float a=Darkening(mesh,traced).changed,b=Darkening(mesh,plain).changed;
                        md.AppendLine($"| {spot.name} | {d:0} m | {a:P1} | {b:P1} |");
                        if(d<=250)checks.Add((a<b,$"{spot.name} at {d:0} m: the traced relief is closer to the mesh than the plain face ({a:P1} of pixels differ against {b:P1})"));
                        var hdr=rig.ShootHdr(v,800,450);
                        checks.Add((hdr.bad==0,$"{spot.name} at {d:0} m, far level: no NaN or infinite pixels (bad {hdr.bad:P2})"));
                        sheet.Add(mesh);sheet.Add(traced);sheet.Add(plain);sheet.Add(Difference(mesh,traced,4));
                    }
                    SaveSheet(sheet,4,Path.Combine(CloseDir,$"greeble-{spot.name}.png"));
                    foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
                }
                // The relief view over the station, every tile far: traced relief cyan.
                settings.reliefDebug=true;Force(2);ApplyLights(rig);
                foreach(var (name,view,_) in Views)
                {
                    if(name!="fragment"&&name!="station")continue;
                    var t=rig.Shoot(view(),W,H);Save(t,Path.Combine(CloseDir,$"greeble-view-{name}.png"));UnityEngine.Object.DestroyImmediate(t);
                }
                settings.reliefDebug=false;ApplyLights(rig);
                // A rough cost: frame time (render and read back) of the station view, every tile far, trace on and off
                // in turn so the editor's drift falls on both alike.
                var sv=Array.Find(Views,x=>x.name=="station").view();
                var times=new[]{new List<double>(),new List<double>()};
                for(int k=0;k<13;k++)for(int on=1;on>=0;on--)
                {
                    settings.greebleTrace=on==1;
                    var clock=System.Diagnostics.Stopwatch.StartNew();var t=rig.Shoot(sv,W,H,false,1);
                    if(k>0)times[on].Add(clock.Elapsed.TotalMilliseconds);   // (the first pair warms up)
                    UnityEngine.Object.DestroyImmediate(t);
                }
                double withTrace=Median(times[1]),without=Median(times[0]);
                md.AppendLine();md.AppendLine($"Station view, every tile far, {W} × {H}: {withTrace:0.0} ms a frame with the trace, {without:0.0} ms without, {withTrace-without:0.0} ms for the trace (render and read back, medians of 12 taken in turn; rough).");
            }
        }

        // Every tile at one level of detail (-1: back to its own pick); a forced level doesn't dither against another.
        static void ForceLevel(LODGroup[] groups,int level)
        {
            foreach(var g in groups){g.fadeMode=level<0?LODFadeMode.CrossFade:LODFadeMode.None;g.ForceLOD(level<0?-1:Mathf.Min(level,g.lodCount-1));}
        }
        static double Median(List<double> l){l.Sort();return l[l.Count/2];}
        // A sheet of close shots, command and hull at 2 m with the sun from the side, a column for each variant (each sets
        // the settings its column shows).
        static void SettingSheet(Rig rig,string file,params Action[] variants)
        {
            var settings=StationLightingSettings.Current;settings.kitProps=false;
            var sheet=new List<Texture2D>();
            foreach(var spot in Spots(rig,"command","hull"))
            {
                if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir))continue;
                settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
                foreach(var set in variants){set();sheet.Add(rig.Shoot(Stand(hit,normal,dir,2,spot.elevation),W,H));}
            }
            SaveSheet(sheet,variants.Length,Path.Combine(CloseDir,file));
            foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
        }

        // The same close spots with the stage where it is and moved about 4 km off the origin, camera with it: nothing
        // in the surface may depend on how far the world is from the origin.
        static void PrecisionCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;var root=rig.Stage.root.transform;
            settings.kitProps=false;settings.closeUpSurface=true;settings.fineDetail=true;
            var offset=new Vector3(2400,1700,-2900);var home=root.position;
            md.AppendLine("# Far from the origin");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Columns: the stage where it is, moved {offset.magnitude:0} m away (camera with it), difference ×4. Mean difference (0-255) and pixels off by more than 16.");
            md.AppendLine();md.AppendLine("| Spot | Distance | Mean | Off |");md.AppendLine("|---|---:|---:|---:|");
            var sheet=new List<Texture2D>();
            try
            {
                foreach(var spot in Spots(rig,"top","hull","defense","command"))
                {
                    root.position=home;
                    if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir))continue;
                    settings.sunFrom=SideSun(normal,dir,25);
                    foreach(float d in new[]{0.7f,2f})
                    {
                        var v=Stand(hit,normal,dir,d,spot.elevation);
                        root.position=home;StationVoxelGi.Invalidate();ApplyLights(rig);var near=rig.Shoot(v,W,H);
                        root.position=home+offset;StationVoxelGi.Invalidate();ApplyLights(rig);
                        var far=rig.Shoot(new TileGallery.View(v.position+offset,v.target+offset,v.up,v.fov),W,H);
                        var (mean,off)=Error(far,near);
                        md.AppendLine($"| {spot.name} | {d:0.0} m | {mean:0.00} | {off:P2} |");
                        checks.Add((mean<1.5f&&off<0.005f,$"{spot.name} at {d:0.0} m looks the same {offset.magnitude:0} m from the origin (mean {mean:0.00}, {off:P2} of pixels off)"));
                        sheet.Add(near);sheet.Add(far);sheet.Add(Difference(near,far,4));
                    }
                }
            }
            finally{root.position=home;StationVoxelGi.Invalidate();ApplyLights(rig);}
            SaveSheet(sheet,3,Path.Combine(CloseDir,"precision.png"));
            foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
        }

        static void DetailCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;
            md.AppendLine("# Fine detail and painted metal");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Density {settings.detailDensity:0.##}, painted metal {settings.paintedMetal:0.##}; edges on throughout. Sun 25° above each surface, from the side.");
            md.AppendLine();
            md.AppendLine("Columns: off, on, difference ×4. Rows: 1, 5 m. Shimmer: the camera creeps sideways a quarter pixel a frame at 20 m (800 × 450, so a pixel is about 3 cm there); each frame's aliasing is its difference from the same frame supersampled 4 × 4, and shimmer is the share of pixels whose aliasing changes visibly (more than 8 of 255) from one frame to the next, with the close-up surface and detail all off and all on.");
            md.AppendLine();
            md.AppendLine("| Spot | 1 m changed | 5 m changed | Shimmer off | Shimmer on |");
            md.AppendLine("|---|---:|---:|---:|---:|");
            settings.closeUpSurface=true;
            foreach(var spot in Spots(rig,"top","hull","defense","command"))
            {
                string name=spot.name;
                if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir)){checks.Add((false,$"{name}: the spot's ray meets the stage"));continue;}
                settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
                var sheet=new List<Texture2D>();var changed=new List<float>();
                foreach(float d in new[]{1f,5f})
                {
                    var v=Stand(hit,normal,dir,d,spot.elevation);
                    settings.fineDetail=false;var off=rig.Shoot(v,W,H);
                    settings.fineDetail=true;var on=rig.Shoot(v,W,H);
                    Save(off,Path.Combine(CloseDir,$"detail-{name}-{d:0}m-off.png"));Save(on,Path.Combine(CloseDir,$"detail-{name}-{d:0}m-on.png"));
                    changed.Add(Darkening(off,on).changed);
                    sheet.Add(off);sheet.Add(on);sheet.Add(Difference(off,on,4));
                }
                SaveSheet(sheet,3,Path.Combine(CloseDir,$"detail-{name}.png"));
                foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
                // Shimmer: frame-to-frame change on a slow sideways creep, the same path with detail off and on.
                var far=Stand(hit,normal,dir,20,spot.elevation);
                var shimmer=new float[2];
                for(int pass=0;pass<2;pass++){settings.fineDetail=pass==1;settings.closeUpSurface=pass==1;shimmer[pass]=Shimmer(rig,far);}
                settings.closeUpSurface=true;settings.fineDetail=true;
                md.AppendLine($"| {name} | {changed[0]:P1} | {changed[1]:P1} | {shimmer[0]:P2} | {shimmer[1]:P2} |");
                checks.Add((changed[0]>0.005f,$"{name} at 1 m: fine detail shows ({changed[0]:P1} of pixels change)"));
                checks.Add((shimmer[1]<=shimmer[0]*1.25f+0.001f,$"{name} at 20 m: no extra shimmer with the close-up surface and detail on ({shimmer[1]:P2} of pixels flicker against {shimmer[0]:P2} off)"));
            }
        }
        // The camera creeps sideways a quarter pixel a frame (at 800 x 450); each frame's aliasing is its difference
        // from the same frame supersampled 4 x 4. Shimmer: the share of pixels whose aliasing changes visibly (by more
        // than 8 of 255) from one frame to the next.
        static float Shimmer(Rig rig,TileGallery.View view)
        {
            var look=(view.target-view.position).normalized;var right=Vector3.Cross(view.up,look).normalized;
            float pixel=2*Vector3.Distance(view.position,view.target)*Mathf.Tan(view.fov*0.5f*Mathf.Deg2Rad)/(H/2);
            float[] last=null;double sum=0;const int frames=8;
            for(int f=0;f<frames;f++)
            {
                var shift=right*(f*0.25f*pixel);var v=new TileGallery.View(view.position+shift,view.target+shift,view.up,view.fov);
                var raw=rig.Shoot(v,W/2,H/2,false,1);var reference=rig.Shoot(v,W/2,H/2,false,4);
                var error=Residual(raw,reference);
                UnityEngine.Object.DestroyImmediate(raw);UnityEngine.Object.DestroyImmediate(reference);
                if(last!=null){int n=0;for(int i=0;i<error.Length;i++)if(Mathf.Abs(error[i]-last[i])>8)n++;sum+=n/(double)error.Length;}
                last=error;
            }
            return (float)(sum/(frames-1));
        }
        // Shimmer at 20 m with each part of the close-up surface on its own (diagnostic).
        static void ShimmerBreakdown(Rig rig,StringBuilder md,List<(bool ok,string text)> checks,string name)
        {
            var settings=StationLightingSettings.Current;
            var saved=(settings.edgeWear,settings.edgeHighlight,settings.creaseDarkness,settings.paintedMetal,settings.detailDensity);
            var spot=Spots(rig,name)[0];
            Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir);
            settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
            var far=Stand(hit,normal,dir,20,spot.elevation);
            md.AppendLine("# Shimmer breakdown, "+name);md.AppendLine();
            void Row(string what,bool edges,bool detail,float wear,float highlight,float crease,float paint,float density)
            {
                settings.closeUpSurface=edges;settings.fineDetail=detail;settings.edgeWear=wear;settings.edgeHighlight=highlight;
                settings.creaseDarkness=crease;settings.paintedMetal=paint;settings.detailDensity=density;
                md.AppendLine($"- {what}: {Shimmer(rig,far):P2} of pixels flicker");
            }
            try
            {
                Row("everything off",false,false,0,0,0,0,0);
                Row("bevels only",true,false,0,0,0,0,0);
                Row("bevels and highlight",true,false,0,saved.edgeHighlight,0,0,0);
                Row("bevels and wear",true,false,saved.edgeWear,0,0,0,0);
                Row("bevels and creases",true,false,0,0,saved.creaseDarkness,0,0);
                Row("painted metal only",true,true,0,0,0,saved.paintedMetal,0);
                Row("fine detail only",true,true,0,0,0,0,saved.detailDensity);
                Row("everything",true,true,saved.edgeWear,saved.edgeHighlight,saved.creaseDarkness,saved.paintedMetal,saved.detailDensity);
            }
            finally{(settings.edgeWear,settings.edgeHighlight,settings.creaseDarkness,settings.paintedMetal,settings.detailDensity)=saved;}
        }

        static void KitCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;
            var kit=rig.Stage.root.GetComponent<StationKit>();
            md.AppendLine("# Kit near the player");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Density {settings.kitDensity:0.##}, ring {settings.kitInnerRadius:0} to {settings.kitOuterRadius:0} m. Sun 25° above each surface, from the side.");
            md.AppendLine();md.AppendLine("Columns: off, on, difference ×4. Rows: 3, 8 m.");md.AppendLine();
            foreach(var spot in Spots(rig,"top","hull","defense"))
            {
                if(!Aim(rig.Stage.root,spot,out var hit,out var normal,out var dir)){checks.Add((false,$"{spot.name}: the spot's ray meets the stage"));continue;}
                settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
                var sheet=new List<Texture2D>();
                foreach(float d in new[]{3f,8f})
                {
                    var v=Stand(hit,normal,dir,d,spot.elevation);
                    settings.kitProps=false;var off=rig.Shoot(v,W,H);settings.kitProps=true;var on=rig.Shoot(v,W,H);
                    Save(on,Path.Combine(CloseDir,$"kit-{spot.name}-{d:0}m.png"));
                    sheet.Add(off);sheet.Add(on);sheet.Add(Difference(off,on,4));
                }
                SaveSheet(sheet,3,Path.Combine(CloseDir,$"kit-{spot.name}.png"));
                foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
            }
            checks.Add((kit!=null&&kit.Drawn>0,$"props drawn near the camera ({(kit!=null?kit.Drawn:0)} props and routes in the last view)"));
            checks.Add((kit!=null&&kit.Routes>0,$"pipes and cables join props ({(kit!=null?kit.Routes:0)} routes on the stage)"));

            // A walk: the player walks away over the hull top at 1.4 m/s, 30 frames a second, from 12 to 22 m from a
            // patch of props, so they cross the ring's edge band. An observer camera holds still 4 m from the patch, so
            // every change between frames comes from the ring (props shrinking to flat, plates taking their folded
            // darkening). A pop is a pixel jumping more than 24 of 255 between frames.
            var top=Spots(rig,"top")[0];Aim(rig.Stage.root,top,out var start,out var up,out var heading);
            settings.sunFrom=SideSun(up,heading,25);ApplyLights(rig);
            var walk=Vector3.ProjectOnPlane(-top.back,up).normalized;
            const int frames=214;
            float inner=settings.kitInnerRadius,outer=settings.kitOuterRadius;
            var watch=Stand(start,up,-walk,4,35,50);
            var walker=new GameObject("Walker").transform;walker.SetParent(rig.Stage.root.transform,false);
            var savedCentre=kit.ringCentre;kit.ringCentre=walker;
            (float worst,float mean) Pops(float a,float b)
            {
                settings.kitInnerRadius=a;settings.kitOuterRadius=b;
                float worst=0;double sum=0;Texture2D last=null;
                for(int f=0;f<frames;f++)
                {
                    walker.position=start+up*1.7f-walk*(12+1.4f*f/30f);
                    var t=rig.Shoot(watch,W/2,H/2,false,1);
                    if(last!=null)
                    {
                        var d=Residual(t,last);int n=0;for(int i=0;i<d.Length;i++)if(Mathf.Abs(d[i])>24)n++;
                        float share=n/(float)d.Length;worst=Mathf.Max(worst,share);sum+=share;
                        UnityEngine.Object.DestroyImmediate(last);
                    }
                    last=t;
                }
                UnityEngine.Object.DestroyImmediate(last);
                settings.kitInnerRadius=inner;settings.kitOuterRadius=outer;
                return (worst,(float)(sum/(frames-1)));
            }
            (float worst,float mean) grown,hard;
            try{grown=Pops(inner,outer);hard=Pops(outer-0.01f,outer);}
            finally{kit.ringCentre=savedCentre;UnityEngine.Object.DestroyImmediate(walker.gameObject);}
            md.AppendLine("## Walk");md.AppendLine();
            md.AppendLine($"The player walks {1.4f*frames/30f:0.0} m in {frames} frames, from 12 to 22 m from a patch of props; an observer holds still 4 m from the patch (800 × 450). Pixels jumping more than 24 of 255 between frames: worst frame {grown.worst:P3}, mean {grown.mean:P3} with the grow-in band; {hard.worst:P3} and {hard.mean:P3} with a hard edge at {outer:0} m.");
            checks.Add((grown.worst<0.0005f,$"walking: no pops with the grow-in band (worst frame {grown.worst:P3} of pixels jump)"));
            checks.Add((hard.worst>0.002f&&grown.worst<hard.worst*0.25f,$"walking: a hard ring edge would pop here ({hard.worst:P3}) and the grow-in band doesn't ({grown.worst:P3})"));
            TileGallery.View Frame(int f){var eye=start+up*1.7f+walk*(1.4f*f/30f-2f);return new TileGallery.View(eye,eye+walk*6-up*2.2f,up,60);}

            // Frame cost, CPU side in the editor: the kit's gather and draw submission, and the whole render.
            var times=new List<double>[2]{new List<double>(),new List<double>()};var draws=new List<double>();int drawn=0;
            for(int f=0;f<frames;f+=5)for(int on=0;on<2;on++)
            {
                settings.kitProps=on==1;var sw=System.Diagnostics.Stopwatch.StartNew();var t=rig.Shoot(Frame(f),W/2,H/2,false,1);
                times[on].Add(sw.Elapsed.TotalMilliseconds);UnityEngine.Object.DestroyImmediate(t);
                if(on==1){draws.Add(kit.DrawMs);drawn=Math.Max(drawn,kit.Drawn);}
            }
            settings.kitProps=true;
            double offMs=Median(times[0]),onMs=Median(times[1]),drawMs=Median(draws);
            md.AppendLine();md.AppendLine("## Frame cost");md.AppendLine();
            md.AppendLine($"Editor, median CPU wall time per 800 × 450 frame including readback, alternating: {offMs:0.0} ms without the kit, {onMs:0.0} ms with it; the kit's own gather and submission {drawMs:0.00} ms (median), for up to {drawn} props.");
            checks.Add((drawMs<2,$"the kit's gather and submission stays under 2 ms ({drawMs:0.00} ms)"));
        }

        static void ReliefCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;var stage=rig.Stage;
            var groups=stage.root.GetComponentsInChildren<LODGroup>();
            // A forced level must not dither against the one the group would pick.
            void Force(int level)=>ForceLevel(groups,level);
            md.AppendLine("# Middle-range relief trace");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. Plates sink into the face from {settings.reliefLowerNear:0} to {settings.reliefLowerFar:0} m. Sun 25° above each surface, from the side.");
            md.AppendLine();
            md.AppendLine("Columns: the mesh carving (every tile near), the trace (every tile mid), the far level as it was (plain armour, no trace), difference mesh/trace ×4. Rows: 20, 40, 80, 150, 250 m.");
            md.AppendLine();md.AppendLine("| Spot | Distance | Trace against mesh: pixels changed | Plain against mesh |");md.AppendLine("|---|---:|---:|---:|");
            settings.kitProps=false;
            foreach(var spot in Spots(rig,"defense","command","top"))
            {
                if(!Aim(stage.root,spot,out var hit,out var normal,out var dir)){checks.Add((false,$"{spot.name}: the spot's ray meets the stage"));continue;}
                settings.sunFrom=SideSun(normal,dir,25);ApplyLights(rig);
                var sheet=new List<Texture2D>();
                foreach(float d in new[]{20f,40f,80f,150f,250f})
                {
                    var v=Stand(hit,normal,dir,d,spot.elevation);
                    Force(0);var mesh=rig.Shoot(v,W,H);
                    Force(1);var trace=rig.Shoot(v,W,H);
                    settings.reliefTrace=false;Force(2);var plain=rig.Shoot(v,W,H);settings.reliefTrace=true;
                    float a=Darkening(mesh,trace).changed,b=Darkening(mesh,plain).changed;
                    md.AppendLine($"| {spot.name} | {d:0} m | {a:P1} | {b:P1} |");
                    if(d>=40&&d<=150)checks.Add((a<b,$"{spot.name} at {d:0} m: the trace is closer to the carving than plain armour ({a:P1} of pixels differ against {b:P1})"));
                    Save(trace,Path.Combine(CloseDir,$"relief-{spot.name}-{d:0}m-trace.png"));Save(mesh,Path.Combine(CloseDir,$"relief-{spot.name}-{d:0}m-mesh.png"));
                    sheet.Add(mesh);sheet.Add(trace);sheet.Add(plain);sheet.Add(Difference(mesh,trace,4));
                }
                SaveSheet(sheet,4,Path.Combine(CloseDir,$"relief-{spot.name}.png"));
                foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
            }
            Force(-1);
            md.AppendLine();md.AppendLine("## Triangles");md.AppendLine();
            md.AppendLine("Per gallery view, with each tile at the level its LOD group picks: carving to 110 m as before, against the mid level from about 30 m.");
            md.AppendLine();md.AppendLine("| View | Before | Now | Near / mid / far tiles now |");md.AppendLine("|---|---:|---:|---|");
            long before=0,now=0;
            foreach(var (name,view,_) in Views)
            {
                var old=Drawn(stage,view(),true);var cur=Drawn(stage,view());
                before+=old.tris;now+=cur.tris;
                md.AppendLine($"| {name} | {old.tris:N0} | {cur.tris:N0} | {cur.near} / {cur.mid} / {cur.far} |");
            }
            md.AppendLine();md.AppendLine($"All views together: {before:N0} before, {now:N0} now ({1-now/(double)before:P0} fewer).");
            checks.Add((now<before,$"the mid level saves triangles over the gallery views ({before:N0} to {now:N0})"));
        }

        static void OrbitCheck(Rig rig,StringBuilder md,List<(bool ok,string text)> checks)
        {
            var settings=StationLightingSettings.Current;var stage=rig.Stage;
            var chunks=stage.root.GetComponent<StationChunks>();
            if(chunks==null){checks.Add((false,"the stage has chunks"));return;}
            var clock=System.Diagnostics.Stopwatch.StartNew();chunks.Bake();double bakeMs=clock.Elapsed.TotalMilliseconds;
            settings.kitProps=false;
            md.AppendLine("# From 200 m to orbit");md.AppendLine();
            md.AppendLine($"{DateTime.Now:yyyy-MM-dd HH:mm}. {chunks.ChunkCount} chunks of 8 × 8 × 8 cells, {chunks.Triangles:N0} triangles in all, baked in {bakeMs/1000:0.0} s; they replace their tiles beyond {chunks.switchDistance:0} m.");
            md.AppendLine();
            md.AppendLine("Columns: reference (tiles, 16 samples a pixel), tiles, chunks. Mean difference from the reference (0-255) and pixels off by more than 16.");
            md.AppendLine();md.AppendLine("| Distance | Tiles: mean, off | Chunks: mean, off | Triangles: tiles, chunks |");md.AppendLine("|---:|---:|---:|---:|");
            var centre=new Vector3(55,8,25);var dir=new Vector3(-0.55f,0.45f,-0.7f).normalized;
            settings.sunFrom=new Vector3(-0.62f,0.55f,-0.56f);ApplyLights(rig);
            var sheet=new List<Texture2D>();
            foreach(float d in new[]{200f,400f,800f,1600f})
            {
                var v=new TileGallery.View(centre+dir*d,centre,Vector3.up,30);
                chunks.force=0;var reference=rig.Shoot(v,W/2,H/2,false,4);var tiles=rig.Shoot(v,W/2,H/2,false,1);
                chunks.force=1;var merged=rig.Shoot(v,W/2,H/2,false,1);chunks.force=-1;
                var (tm,to)=Error(tiles,reference);var (cm,co)=Error(merged,reference);
                var (_,_,_,tris)=Drawn(stage,v);
                md.AppendLine($"| {d:0} m | {tm:0.0}, {to:P1} | {cm:0.0}, {co:P1} | {tris:N0}, {chunks.Triangles:N0} |");
                if(d>=800)checks.Add((cm<=tm*1.5f+1,$"at {d:0} m the chunks stay as close to the reference as the tiles ({cm:0.0} against {tm:0.0})"));
                sheet.Add(reference);sheet.Add(tiles);sheet.Add(merged);
            }
            SaveSheet(sheet,3,Path.Combine(CloseDir,"orbit.png"));
            foreach(var t in sheet)UnityEngine.Object.DestroyImmediate(t);
            checks.Add((chunks.Triangles<stage.farTriangles/20,$"chunks draw a small share of the far level's triangles ({chunks.Triangles:N0} against {stage.farTriangles:N0})"));
        }
        // Mean absolute difference (0-255) and the share of pixels off by more than 16.
        static (float mean,float off) Error(Texture2D a,Texture2D b)
        {
            var pa=a.GetPixels32();var pb=b.GetPixels32();double s=0;int off=0;
            for(int i=0;i<pa.Length;i++)
            {
                float d=(Mathf.Abs(pa[i].r-pb[i].r)+Mathf.Abs(pa[i].g-pb[i].g)+Mathf.Abs(pa[i].b-pb[i].b))/3f;
                s+=d;if(d>16)off++;
            }
            return ((float)(s/pa.Length),off/(float)pa.Length);
        }

        // Per pixel, the aliased picture's brightness less the supersampled one's (0-255 scale).
        static float[] Residual(Texture2D raw,Texture2D reference)
        {
            var pa=raw.GetPixels32();var pb=reference.GetPixels32();var o=new float[pa.Length];
            for(int i=0;i<pa.Length;i++)o[i]=(pa[i].r+pa[i].g+pa[i].b-pb[i].r-pb[i].g-pb[i].b)/3f;
            return o;
        }

        // Pixels whose colour changed visibly, and how much darker the second picture is on average (0-255).
        static (float changed,float darker) Darkening(Texture2D a,Texture2D b)
        {
            var pa=a.GetPixels32();var pb=b.GetPixels32();int changed=0;double darker=0;
            for(int i=0;i<pa.Length;i++)
            {
                int d=Mathf.Abs(pa[i].r-pb[i].r)+Mathf.Abs(pa[i].g-pb[i].g)+Mathf.Abs(pa[i].b-pb[i].b);
                if(d>12)changed++;
                darker+=Mathf.Max(0,(pa[i].r+pa[i].g+pa[i].b-pb[i].r-pb[i].g-pb[i].b)/3f);
            }
            return (changed/(float)pa.Length,(float)(darker/pa.Length));
        }
        static Texture2D Difference(Texture2D a,Texture2D b,float gain)
        {
            var pa=a.GetPixels32();var pb=b.GetPixels32();var o=new Color32[pa.Length];
            for(int i=0;i<pa.Length;i++)
            {
                float d=(Mathf.Abs(pa[i].r-pb[i].r)+Mathf.Abs(pa[i].g-pb[i].g)+Mathf.Abs(pa[i].b-pb[i].b))/3f*gain;
                byte v=(byte)Mathf.Clamp(d,0,255);o[i]=new Color32(v,v,v,255);
            }
            var t=new Texture2D(a.width,a.height,TextureFormat.RGBA32,false);t.SetPixels32(o);t.Apply();return t;
        }
        // Pictures in rows of cols, each scaled to half size.
        static void SaveSheet(List<Texture2D> pictures,int cols,string path)
        {
            if(pictures.Count==0)return;
            int w=pictures[0].width/2,h=pictures[0].height/2,rows=(pictures.Count+cols-1)/cols;
            var sheet=new Texture2D(w*cols,h*rows,TextureFormat.RGBA32,false);
            sheet.SetPixels32(new Color32[w*cols*h*rows]);
            for(int i=0;i<pictures.Count;i++)
            {
                var src=pictures[i];var px=new Color[w*h];
                for(int y=0;y<h;y++)for(int x=0;x<w;x++)
                    px[y*w+x]=(src.GetPixel(2*x,2*y)+src.GetPixel(2*x+1,2*y)+src.GetPixel(2*x,2*y+1)+src.GetPixel(2*x+1,2*y+1))*0.25f;
                sheet.SetPixels(i%cols*w,(rows-1-i/cols)*h,w,h,px);
            }
            sheet.Apply();Save(sheet,path);UnityEngine.Object.DestroyImmediate(sheet);
        }

        // The first surface a spot's ray meets: point, normal facing the ray, and the direction the camera looks along
        // (the ray's, or toward -back where the ray meets the surface square on).
        static bool Aim(GameObject root,(string name,Vector3 from,Vector3 to,Vector3 back,float elevation) spot,out Vector3 point,out Vector3 normal,out Vector3 dir)
        {
            dir=(spot.to-spot.from).normalized;
            bool hit=Raycast(root,new Ray(spot.from,dir),out point,out normal);
            if(hit&&spot.back!=Vector3.zero)dir=-spot.back.normalized;
            return hit;
        }
        // Ray against the near meshes of everything on the stage (far levels left out).
        static bool Raycast(GameObject root,Ray ray,out Vector3 point,out Vector3 normal)
        {
            float best=float.MaxValue;point=normal=default;
            foreach(var r in root.GetComponentsInChildren<MeshRenderer>())
            {
                if(r.gameObject.name=="Far"||!r.enabled)continue;
                if(!r.bounds.IntersectRay(ray,out float enter)||enter>best)continue;
                var mesh=r.GetComponent<MeshFilter>()?.sharedMesh;if(mesh==null)continue;
                var toLocal=r.transform.worldToLocalMatrix;var toWorld=r.transform.localToWorldMatrix;
                Vector3 o=toLocal.MultiplyPoint3x4(ray.origin),d=toLocal.MultiplyVector(ray.direction);
                var v=mesh.vertices;var t=mesh.triangles;
                for(int i=0;i<t.Length;i+=3)
                {
                    Vector3 a=v[t[i]],e1=v[t[i+1]]-a,e2=v[t[i+2]]-a;
                    var p=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,p);
                    if(Mathf.Abs(det)<1e-9f)continue;
                    float inv=1/det;var s=o-a;float u=Vector3.Dot(s,p)*inv;if(u<0||u>1)continue;
                    var q=Vector3.Cross(s,e1);float w=Vector3.Dot(d,q)*inv;if(w<0||u+w>1)continue;
                    float k=Vector3.Dot(e2,q)*inv;if(k<=1e-4f)continue;
                    var hit=toWorld.MultiplyPoint3x4(o+d*k);float dist=Vector3.Distance(hit,ray.origin);
                    if(dist>=best)continue;
                    best=dist;point=hit;normal=toWorld.MultiplyVector(Vector3.Cross(e1,e2)).normalized;
                }
            }
            if(best==float.MaxValue)return false;
            if(Vector3.Dot(normal,ray.direction)>0)normal=-normal;
            return true;
        }
        // A camera distance metres from a surface point, elevation degrees above the surface, on the side dir came from.
        static TileGallery.View Stand(Vector3 point,Vector3 normal,Vector3 dir,float distance,float elevation,float fov=40)
        {
            var back=Vector3.ProjectOnPlane(-dir,normal);
            if(back.sqrMagnitude<1e-6f)back=Vector3.ProjectOnPlane(Vector3.forward,normal);
            back.Normalize();
            float e=elevation*Mathf.Deg2Rad;
            var position=point+(normal*Mathf.Sin(e)+back*Mathf.Cos(e))*distance;
            var look=(point-position).normalized;
            var up=Mathf.Abs(Vector3.Dot(look,Vector3.up))<0.95f?Vector3.up:normal;
            return new TileGallery.View(position,point,up,fov);
        }
    }
}
