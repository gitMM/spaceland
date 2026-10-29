using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    // The magnetic-boot walker in the interiors, scripted headless (tools/tile-lab.sh walk), on the flat review station
    // and the curved Foundry example: walk across a home's deck into its window wall and on up it; push off from the
    // deck through the hatch into the next home; push off from a home through its socket into the corridor's tube and
    // walk along it. Checks it lands where it should and never leaves the interiors; shoots frames from its eye.
    // Writes DeathStarRuns/Tiles/walk/.
    public static partial class TileLab
    {
        static string WalkDir=>Path.Combine(Dir,"walk");
        const float WalkDt=1f/60;

        static string WalkCheck(Rig rig)
        {
            Directory.CreateDirectory(WalkDir);
            foreach(var f in Directory.GetFiles(WalkDir,"*.png"))File.Delete(f);
            var checks=new List<(bool ok,string text)>();var shots=new List<(string file,string what)>();var notes=new List<string>();
            var stage=rig.Stage;
            Walks(rig,"flat",stage.interiors,TileGallery.Room.position,checks,shots,notes);
            bool shown=rig.ShowStage(false);
            var root=new GameObject("Walk foundry");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var view=new FoundrySettlement(root.transform,rig.Library.materials,rig.Library.clearGlass);
            try
            {
                view.Rebuild(SettlementExamples.Foundry());TileLook.AddLights(root.transform);
                Walks(rig,"curved",view.Interiors,Vector3.zero,checks,shots,notes);
            }
            finally{view.Dispose();Object.DestroyImmediate(root);rig.ShowStage(shown);}

            var md=new StringBuilder();
            md.AppendLine("# Magnetic-boot walk");md.AppendLine();md.AppendLine("## Pictures");md.AppendLine();
            foreach(var (file,what) in shots)md.AppendLine($"- `{file}`: {what}");
            md.AppendLine();md.AppendLine("## Checks");md.AppendLine();
            foreach(var (ok,text) in checks)md.AppendLine($"- {(ok?"PASS":"FAIL")} {text}");
            foreach(var n in notes)md.AppendLine("- "+n);
            File.WriteAllText(Path.Combine(WalkDir,"report.md"),md.ToString());
            int fails=checks.Count(c=>!c.ok);
            string summary=$"Walk lab: {checks.Count-fails}/{checks.Count} checks pass, {shots.Count} pictures in {WalkDir}";
            if(fails>0)summary+="\n"+string.Join("\n",checks.Where(c=>!c.ok).Select(c=>"FAIL "+c.text).Concat(notes));
            return "DONE "+summary;
        }

        static void Walks(Rig rig,string where,StationInteriors interiors,Vector3 near,List<(bool ok,string text)> checks,List<(string file,string what)> shots,List<string> notes)
        {
            interiors.colliders=true;
            if(!interiors.Spawn(near,out var feet,out var up,out var forward,out var home,out var hatch)){checks.Add((false,$"{where}: a home to start in"));return;}
            interiors.Clear();interiors.ShowAround(feet,16);Physics.SyncTransforms();
            var go=new GameObject("Walker");SceneManager.MoveGameObjectToScene(go,rig.PreviewScene);
            var w=go.AddComponent<MagBootWalker>();
            void Shot(string name,string what)
            {
                var v=new TileGallery.View(w.ViewPosition,w.ViewPosition+w.ViewRotation*Vector3.forward,w.ViewRotation*Vector3.up,75);
                var t=rig.Shoot(v,W,H);Save(t,Path.Combine(WalkDir,$"{where}-{name}.png"));Object.DestroyImmediate(t);
                shots.Add(($"{where}-{name}.png",$"{where}: {what}"));
            }
            // Runs steps until done or the time is up; the cells the walker's middle passes through; whether it ever
            // left the interiors.
            (bool done,HashSet<Vector3Int> cells,bool lost) Run(Vector2 move,float seconds,System.Func<bool> done,bool push=false)
            {
                var cells=new HashSet<Vector3Int>();bool lost=false;
                for(int i=0;i<seconds/WalkDt;i++)
                {
                    w.Step(move,Vector2.zero,push&&i==0,false,WalkDt);w.Ease(WalkDt);
                    if(interiors.CellAt(w.transform.position+w.transform.up*0.9f,out var c))cells.Add(c);else lost=true;
                    if(done())return (true,cells,lost);
                }
                return (false,cells,lost);
            }
            try
            {
                // 1. Across the deck into the window wall, then on up it.
                w.Place(feet,up,forward);Shot("start","standing on a home's deck, facing its windows");
                bool climbed=false;
                var a=Run(new Vector2(0,1),6,()=>{if(w.Last=="climb")climbed=true;return climbed;});
                var after=Run(new Vector2(0,1),1.2f,()=>false);
                float turned=Vector3.Angle(up,w.transform.up);
                if(!climbed)
                {
                    var ps=rig.PreviewScene.GetPhysicsScene();var wp=w.transform.position;var wu=w.transform.up;
                    string Probe(float h){return ps.Raycast(wp+wu*h,w.transform.forward,out var r,3f,w.mask)?$"{h}: {r.distance:0.00} m, normal·up {Vector3.Dot(r.normal,wu):0.00} ({r.collider.name})":$"{h}: nothing";}
                    notes.Add($"{where}: no climb; moved {Vector3.Distance(wp,feet):0.00} m, last {w.Last}, floating {w.Floating}, up·start {Vector3.Dot(wu,up):0.00}; ahead at "+Probe(0.5f)+"; "+Probe(1f)+"; "+Probe(1.6f));
                }
                checks.Add((climbed&&turned>60&&!a.lost&&!after.lost,$"{where}: walks across the deck into the window wall and on up it (turned {turned:0}°, stayed inside: {!a.lost&&!after.lost})"));
                w.Ease(1);Shot("climbed","after walking up onto the window wall");

                // 2. From the deck, push off through the hatch into the next home.
                w.Place(feet,up,forward);w.LookAt(interiors.FaceCentre(home,hatch));
                bool started=false;
                var b=Run(Vector2.zero,8,()=>{if(w.Floating)started=true;return started&&!w.Floating;},true);
                bool there=interiors.CellAt(w.transform.position+w.transform.up*0.9f,out var landed)&&landed!=home;
                checks.Add((b.done&&there&&b.cells.Contains(home+hatch)&&!b.lost,$"{where}: pushes off through the hatch and lands in the next home (landed in {landed}, passed {string.Join(" ",b.cells)})"));
                w.Ease(1);Shot("hatch-landed","after drifting through the hatch and landing");

                // 3. From a home, push off through its socket into the corridor's tube; then walk along it.
                Vector3Int tube=default,entered=default;bool found=false;
                foreach(var c in interiors.Cells.ToList())
                    if(!found&&interiors.Structure.cells[c].family==TileFamily.Corridor)
                        foreach(var d in TileStructure.Directions)
                            if(!found&&interiors.Structure.TryGet(c+d,out var q)&&q.family==TileFamily.Habitat&&interiors.Opens(c,c+d)){tube=c;entered=c+d;found=true;}
                if(!found){notes.Add($"{where}: no corridor opening into a home among the built rooms");return;}
                var into=tube-entered;var n=interiors.FaceNormal(entered,into);
                var side=Vector3.Cross(n,Vector3.right).sqrMagnitude>0.1f?Vector3.Cross(n,Vector3.right).normalized:Vector3.Cross(n,Vector3.up).normalized;
                // floating in the home's middle, head towards side
                w.Place(interiors.Centre(entered)-side*w.eyeHeight,side,n);w.LookAt(interiors.FaceCentre(tube,into));
                started=false;
                var c3=Run(Vector2.zero,8,()=>{if(w.Floating)started=true;return started&&!w.Floating;},true);
                bool inTube=c3.cells.Contains(tube);
                checks.Add((c3.done&&inTube&&!c3.lost,$"{where}: pushes off through the home's socket into the corridor's tube and lands (passed {string.Join(" ",c3.cells)})"));
                w.Ease(1);Shot("tube-landed","landed in the corridor's tube");
                // Walking along a straight run (a junction's floor is mostly the openings of its branches: people drift
                // across it).
                var tp=interiors.Structure.cells[tube];var links=interiors.Structure.CorridorLinks(tube,tp);
                if(links.Count==2&&links[0]==-links[1])
                {
                    var walk=Run(new Vector2(0,1),3,()=>false);
                    checks.Add((!walk.lost&&!w.Floating,$"{where}: walks on in the tube without leaving it or losing its footing (last: {w.Last}, cells {string.Join(" ",walk.cells)})"));
                    w.Ease(1);Shot("tube-walk","walking on in the tube");
                }
                else notes.Add($"{where}: {tube} is a junction; walking on in it is not checked");
            }
            finally{Object.DestroyImmediate(go);interiors.Clear();interiors.colliders=false;}
        }
    }
}
