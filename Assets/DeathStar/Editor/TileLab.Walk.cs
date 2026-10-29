using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    // The station walker in the interiors, scripted headless (tools/tile-lab.sh walk) through its per-frame path, on the
    // flat review station and the curved Foundry example: walk across a home's floor to its windows; walk through a
    // sliding door into the next home; shoot up a lift with Space in half a second, walk across its opening at the
    // floor's level and shoot back down; climb the stair from a home through its socket onto a corridor's walkway; float
    // in the zero-g hub; walk and ride the same at 24, 30 and 144 fps. Checks it stays upright (its up within a few
    // degrees of out from the core) and never turns sharply, lands where it should and never leaves the interiors;
    // shoots frames from its eye.
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
            md.AppendLine("# Station walk");md.AppendLine();md.AppendLine("## Pictures");md.AppendLine();
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
            if(!interiors.Spawn(near,out var feet,out var up,out var forward,out var home,out var door)){checks.Add((false,$"{where}: a home with a door to start in"));return;}
            interiors.Clear();interiors.ShowAround(feet,16);Physics.SyncTransforms();
            var go=new GameObject("Walker");SceneManager.MoveGameObjectToScene(go,rig.PreviewScene);
            var w=go.AddComponent<StationWalker>();w.interiors=interiors;
            void Shot(string name,string what)
            {
                var v=new TileGallery.View(w.ViewPosition,w.ViewPosition+w.ViewRotation*Vector3.forward,w.ViewRotation*Vector3.up,75);
                var t=rig.Shoot(v,W,H);Save(t,Path.Combine(WalkDir,$"{where}-{name}.png"));Object.DestroyImmediate(t);
                shots.Add(($"{where}-{name}.png",$"{where}: {what}"));
            }
            Vector3 Mid()=>w.transform.position+w.transform.up*0.9f;
            // Runs frames of frame seconds (StationWalker.Frame, as in play; space pressed on the first) until done or the
            // time is up: the cells the walker's middle passes through, whether it ever left the interiors, the most its up
            // ever stood off out from the core (while not floating), the most it turned in one frame (degrees) and the time
            // taken. trace: the walker's modes as they changed, with the time (s).
            var trace=new StringBuilder();
            (bool done,HashSet<Vector3Int> cells,bool lost,float tilt,float turn,float time) Run(Vector2 move,float vertical,float seconds,System.Func<bool> done,bool space=false,float frame=WalkDt)
            {
                var cells=new HashSet<Vector3Int>();bool lost=false;float tilt=0,turn=0;trace.Clear();var last=(StationWalker.Mode)(-1);
                int frames=Mathf.RoundToInt(seconds/frame);
                for(int i=0;i<frames;i++)
                {
                    if(w.State!=last&&trace.Length<600){trace.Append($"{w.State}@{i*frame:0.00} ({w.Below()}) ");last=w.State;}
                    var before=w.transform.up;
                    w.Frame(move,Vector2.zero,space&&i==0,vertical,false,frame);w.Ease(frame);interiors.TickDoors(w.transform.position+w.transform.up,frame);
                    turn=Mathf.Max(turn,Vector3.Angle(before,w.transform.up));
                    var u=interiors.Up(Mid());if(w.State!=StationWalker.Mode.Float&&u!=Vector3.zero)tilt=Mathf.Max(tilt,Vector3.Angle(u,w.transform.up));
                    if(interiors.CellAt(Mid(),out var c))cells.Add(c);else lost=true;
                    if(done())return (true,cells,lost,tilt,turn,(i+1)*frame);
                }
                return (false,cells,lost,tilt,turn,frames*frame);
            }
            bool In(Vector3Int cell)=>interiors.CellAt(Mid(),out var c)&&c==cell;
            try
            {
                // 1. Across the floor to the windows: it stays on its floor, upright.
                w.Place(feet,up,forward);Shot("start","standing on a home's floor");
                var a=Run(new Vector2(0,1),0,4,()=>false);
                // the gap to the floor straight under the feet (floors curve a little on the curved grid)
                float off=float.NaN;
                if(rig.PreviewScene.GetPhysicsScene().Raycast(w.transform.position+w.transform.up*0.5f,-w.transform.up,out var under,1.5f,w.mask))off=0.5f-under.distance;
                if(a.lost||w.State!=StationWalker.Mode.Walk||a.tilt>=3)notes.Add($"{where}: across the floor: {trace}; up off out by {Vector3.Angle(w.transform.up,interiors.Up(Mid())):0.0}° at the end");
                checks.Add((!a.lost&&w.State==StationWalker.Mode.Walk&&a.tilt<3&&Mathf.Abs(off)<0.05f,
                    $"{where}: walks across the floor to the far wall and stays on it, upright ({w.State}, {off:0.00} m off the floor, up within {a.tilt:0.0}° of out from the core, turned at most {a.turn:0.00}° a frame)"));
                w.Ease(1);Shot("walked","after walking to the far wall");

                // 2. Through the sliding door into the next home.
                w.Place(feet,up,forward);
                interiors.DoorAt(home,door,out var doorMid,out _,out _,out _);
                w.LookAt(doorMid+(w.EyePosition-w.transform.position)*0.3f);
                Shot("door","facing the sliding door to the next home");
                var b=Run(new Vector2(0,1),0,6,()=>In(home+door));
                string doorTrace=trace.ToString();
                var c2=Run(new Vector2(0,1),0,0.8f,()=>false);
                if(!b.done||w.State!=StationWalker.Mode.Walk)notes.Add($"{where}: through the door: {doorTrace} then {trace}");
                checks.Add((b.done&&!b.lost&&!c2.lost&&w.State==StationWalker.Mode.Walk&&Mathf.Max(b.tilt,c2.tilt)<3&&Mathf.Max(b.turn,c2.turn)<1,
                    $"{where}: walks through the sliding door into the next home ({w.State}, passed {string.Join(" ",b.cells)}; up within {Mathf.Max(b.tilt,c2.tilt):0.0}°, turned at most {Mathf.Max(b.turn,c2.turn):0.00}° a frame)"));
                w.Ease(1);Shot("through-door","through the door, in the next home");

                // 3. A lift: Space looking up shoots it from its first floor to the next in half a second; walking across the
                // opening there keeps to the floor's level; Space looking down shoots it back.
                var lift=interiors.Lifts.FirstOrDefault(l=>l.stops.Count>=2&&l.stops.Take(2).All(s=>interiors.CellAt(s+interiors.Up(s)*0.9f,out var sc)&&interiors.Get(sc)!=null));
                if(lift==null)notes.Add($"{where}: no lift with two stops among the built rooms");
                else
                {
                    var s0=lift.stops[0];var s1=lift.stops[1];var u0=interiors.Up(s0);var u1=interiors.Up(s1);
                    interiors.CellAt(s1+u1*0.9f,out var upper);
                    var side=Vector3.Cross(u0,Mathf.Abs(u0.y)<0.9f?Vector3.up:Vector3.right).normalized;
                    float Height(Vector3 stop)=>Vector3.Dot(w.transform.position-stop,interiors.Up(stop));
                    w.Place(s0,u0,side);w.LookAt(s1+u1*2f);Shot("lift","on a lift's pad, looking up the lift");
                    var upRide=Run(Vector2.zero,0,1.5f,()=>w.State==StationWalker.Mode.Walk&&Mathf.Abs(Height(s1))<0.02f,true);
                    if(!upRide.done)notes.Add($"{where}: lift up: {trace}; {Height(s1):+0.00;-0.00} m from the next floor");
                    checks.Add((upRide.done&&!upRide.lost&&upRide.time<=0.55f,
                        $"{where}: Space looking up shoots up the lift to the next floor ({upRide.time:0.00} s, {Height(s1):+0.00;-0.00} m from it, {w.State}, turned at most {upRide.turn:0.00}° a frame)"));
                    w.Ease(1);Shot("lift-top","arrived at the next floor up the lift");

                    // across the opening, from one side of it to the other
                    float worst=0;bool fell=false;var across=Vector3.ProjectOnPlane(side,u1).normalized;
                    w.Place(s1-across*1.3f,u1,across);
                    // how far the feet stand off the floor's real surface carried across the opening, and their largest jump
                    // (from the second frame: the first settles the placed feet onto the curved floor)
                    float jump=0;var was=w.transform.position;bool first=true;
                    var walkOver=Run(new Vector2(0,1),0,1.8f,()=>{
                        var p=w.transform.position;var u=w.transform.up;if(first){first=false;was=p;}
                        worst=Mathf.Max(worst,Mathf.Abs(Vector3.Dot(interiors.FloorUnder(upper,p)-p,u)));
                        jump=Mathf.Max(jump,Mathf.Abs(Vector3.Dot(p-was,u)));was=p;
                        if(w.State!=StationWalker.Mode.Walk)fell=true;return false;});
                    if(fell)notes.Add($"{where}: across the opening: {trace}");
                    checks.Add((!fell&&!walkOver.lost&&worst<0.01f&&jump<0.01f&&In(upper),
                        $"{where}: walks across a lift's opening as if it were floor (at most {worst*100:0.0} cm off the floor, up or down {jump*1000:0.0} mm a frame at most, {w.State} throughout: {!fell}, in {upper}: {In(upper)})"));
                    w.Ease(1);Shot("over-lift","walked across the lift's opening");

                    w.Place(s1,u1,side);w.LookAt(s0-u0*1f);
                    var downRide=Run(Vector2.zero,0,1.5f,()=>w.State==StationWalker.Mode.Walk&&Mathf.Abs(Height(s0))<0.02f,true);
                    if(!downRide.done)notes.Add($"{where}: lift down: {trace}; {Height(s0):+0.00;-0.00} m from the floor below");
                    checks.Add((downRide.done&&!downRide.lost&&downRide.time<=0.55f,
                        $"{where}: Space looking down shoots down the lift to the floor below ({downRide.time:0.00} s, {Height(s0):+0.00;-0.00} m from it, {w.State})"));
                    w.Ease(1);Shot("lift-down","back down the lift");

                    // the same ride in frames of 1/24 s: the same arrival
                    w.Place(s0,u0,side);w.LookAt(s1+u1*2f);
                    var slow=Run(Vector2.zero,0,1.5f,()=>w.State==StationWalker.Mode.Walk&&Mathf.Abs(Height(s1))<0.02f,true,1f/24);
                    checks.Add((slow.done&&slow.time<=0.55f+1f/24,$"{where}: the lift's ride takes the same at 24 fps ({slow.time:0.00} s, {Height(s1):+0.00;-0.00} m from the floor)"));
                }

                // Frame rate: the same walk across a floor in frames of 1/30 s and 1/144 s ends in the same place.
                var ends=new List<Vector3>();
                foreach(float frame in new[]{1f/30,1f/144})
                {
                    w.Place(feet,up,forward);w.LookAt(doorMid+(w.EyePosition-w.transform.position)*0.3f);
                    Run(new Vector2(0.3f,1),0,2,()=>false,false,frame);ends.Add(w.transform.position);
                }
                float apart=Vector3.Distance(ends[0],ends[1]);
                checks.Add((apart<0.02f,$"{where}: walking is the same at 30 and 144 fps (2 s apart by {apart*100:0.0} cm)"));

                // 4. Up the stair from a home through its socket onto the corridor's walkway.
                Vector3Int tube=default,entered=default;bool found=false;
                foreach(var c in interiors.Cells.ToList())
                    if(!found&&interiors.Structure.cells[c].family==TileFamily.Corridor)
                        foreach(var d in TileStructure.Directions)
                            if(!found&&interiors.Structure.TryGet(c+d,out var q)&&q.family==TileFamily.Habitat&&interiors.Way(c,c+d)==TileBuilder.Way.Tube&&!interiors.Hub(c+d))
                            {tube=c;entered=c+d;found=true;}
                if(!found)notes.Add($"{where}: no corridor entering a home through a socket among the built rooms");
                else
                {
                    var into=tube-entered;var floor=interiors.Floor(entered);var fu=-interiors.FaceNormal(entered,interiors.Down(entered));
                    var socket=interiors.FaceCentre(entered,into);
                    // from the floor's middle, which the stair stops short of (over a lift's opening, if there is one)
                    var start=floor;
                    w.Place(start,fu,socket-start);Shot("stairs","in a home, facing the stair up to a corridor's socket");
                    var climb=Run(new Vector2(0,1),0,8,()=>In(tube));
                    if(!climb.done)notes.Add($"{where}: the stair: {trace}; at {w.transform.position:F2}, the socket at {socket:F2}");
                    var on=Run(new Vector2(0,1),0,0.6f,()=>false);
                    float rise=Vector3.Dot(w.transform.position-floor,fu);
                    checks.Add((climb.done&&!climb.lost&&w.State==StationWalker.Mode.Walk&&rise>1,
                        $"{where}: climbs the stair through the socket onto the corridor's walkway ({w.State}, {rise:0.00} m above the home's floor, passed {string.Join(" ",climb.cells)})"));
                    w.Ease(1);Shot("walkway","on the corridor's walkway");
                }

                // 5. The hub: floating.
                var hub=interiors.Cells.Where(c=>interiors.Hub(c)&&interiors.Get(c)!=null).Take(1).ToList();
                if(hub.Count>0)
                {
                    // drifting slowly across the hub room from its middle
                    var spot=interiors.Centre(hub[0]);
                    w.Place(spot-Vector3.up*0.9f,Vector3.up,Vector3.forward);
                    var floated=Run(new Vector2(0,0.3f),0,1,()=>false);
                    checks.Add((w.State==StationWalker.Mode.Float&&!floated.lost,$"{where}: floats in the zero-g hub {hub[0]} ({w.State}, moved {Vector3.Distance(w.transform.position+Vector3.up*0.9f,spot):0.00} m)"));
                    w.Ease(1);Shot("hub","floating in the hub");
                }
            }
            finally{Object.DestroyImmediate(go);interiors.Clear();interiors.colliders=false;}
        }
    }
}
