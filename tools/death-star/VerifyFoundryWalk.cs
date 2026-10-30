using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DeathStar;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

// Run in Play mode via unity command run_script --file tools/death-star/VerifyFoundryWalk.cs --entry VerifyFoundryWalk.Run.
public static class VerifyFoundryWalk
{
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static StationWalker Walker(Scene scene)=>scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationWalker>()).Single(w=>w.isActiveAndEnabled);
    static void CheckGrounded(StationWalker w,string label)
    {
        for(int i=0;i<60;i++)w.Frame(Vector2.zero,Vector2.zero,false,0,false,1f/120);
        var start=w.transform.position;var up=w.transform.up;var forward=w.transform.forward;
        // Looking upwards and holding vertical thrust must still walk along the floor, never fly.
        w.Frame(Vector2.zero,new Vector2(0,65),false,0,false,0);
        for(int i=0;i<120;i++)w.Frame(new Vector2(0,0.3f),Vector2.zero,false,1,false,1f/120);
        Require(w.State==StationWalker.Mode.Walk,label+" is not grounded: "+w.State+" / "+w.Below());
        var delta=w.transform.position-start;
        Require(Vector3.ProjectOnPlane(delta,w.transform.up).magnitude>0.15f,label+" did not walk across floor.");
        var before=w.transform.position;
        for(int i=0;i<60;i++)w.Frame(Vector2.zero,Vector2.zero,false,0,false,1f/120);
        Require(Vector3.Distance(before,w.transform.position)<0.03f,label+" drifted after releasing movement.");
        // Bent floors and steps can change height. Compare the same walk looking level: gaze/vertical thrust must
        // not alter its path, while both walks must actually finish grounded.
        w.Place(start,up,forward);
        for(int i=0;i<120;i++)w.Frame(new Vector2(0,0.3f),Vector2.zero,false,0,false,1f/120);
        Require(w.State==StationWalker.Mode.Walk&&Vector3.Distance(before,w.transform.position)<0.01f,label+" gaze or float input changed the walking path.");
    }
    // The seed and the hub are zero-g: thrust moves the body, and it stays clear of the hatches' walls.
    static void CheckFloating(StationWalker w,string label)
    {
        for(int i=0;i<60;i++)w.Frame(Vector2.zero,Vector2.zero,false,0,false,1f/120);
        Require(w.State==StationWalker.Mode.Float,label+" is not floating: "+w.State);
        var start=w.transform.position;
        for(int i=0;i<120;i++)w.Frame(Vector2.zero,Vector2.zero,false,1,false,1f/120);
        Require(w.State==StationWalker.Mode.Float&&Vector3.Dot(w.transform.position-start,w.transform.up)>0.1f,label+" did not float up on thrust.");
    }
    // Focused gravity regression in an isolated preview scene; does not enter Play mode or inject input.
    public static string Grounding()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var root=new GameObject("Grounded foundry check");SceneManager.MoveGameObjectToScene(root,scene);
        var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var hull=new Material(Shader.Find("DeathStar/Block Hull"));var preview=new Material(Shader.Find("DeathStar/Block Preview"));
        var f=root.AddComponent<BlockFoundry>();f.viewer=cameraObject.AddComponent<Camera>();f.hullMaterial=hull;f.previewMaterial=preview;
        try
        {
            f.Initialize();f.Settlement.Interiors.colliders=true;
            Require(f.EnterWalk(Vector3Int.zero),"Seed entry failed.");CheckFloating(Walker(scene),"Seed");f.ExitWalk();
            f.LoadExample(4);
            var interiors=f.Settlement.Interiors;
            var hub=f.Settlement.Structure.cells.First(p=>interiors.Hub(p.Key)&&TileBuilder.HasInterior(p.Value.family)).Key;
            Require(f.EnterWalk(hub),"Hub entry failed.");CheckFloating(Walker(scene),"Hub");f.ExitWalk();
            int homes=0;
            foreach(var cell in f.Settlement.Structure.cells.Keys.Where(interiors.SpecialRoom).ToList())
            {
                Require(f.EnterWalk(cell),"Wedge or apex entry failed: "+cell);var w=Walker(scene);
                for(int i=0;i<120;i++)w.Frame(Vector2.zero,Vector2.zero,false,0,false,1f/120);
                Require(w.State==StationWalker.Mode.Walk,"Floating wedge or apex "+cell);f.ExitWalk();homes++;
            }
            foreach(var cell in f.Settlement.Structure.cells.Where(p=>p.Value.family==TileFamily.Habitat&&!interiors.Hub(p.Key)).Select(p=>p.Key))
            {
                Require(f.EnterWalk(cell),"Home entry failed.");var w=Walker(scene);
                CheckGrounded(w,"Home "+cell);f.ExitWalk();homes++;
            }
            return $"PASS: seed and hub float on thrust; {homes} homes, wedges and apexes grounded; entry/exit cleanup.";
        }
        finally
        {
            Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(hull);Object.DestroyImmediate(preview);
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }
    public static async Task<string> Run()
    {
        Require(Application.isPlaying,"Run this check in Play mode.");
        var scene=SceneManager.CreateScene("Foundry walk checks",new CreateSceneParameters(LocalPhysicsMode.Physics3D));
        bool background=Application.runInBackground;Application.runInBackground=true;
        var inputFocus=InputSystem.settings.backgroundBehavior;
        var inputRoute=InputSystem.settings.editorInputBehaviorInPlayMode;
        InputSystem.settings.backgroundBehavior=InputSettings.BackgroundBehavior.IgnoreFocus;
        InputSystem.settings.editorInputBehaviorInPlayMode=InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
        var hull=new Material(Shader.Find("DeathStar/Block Hull"));
        var preview=new Material(Shader.Find("DeathStar/Block Preview"));
        var root=new GameObject("Foundry walk verification");SceneManager.MoveGameObjectToScene(root,scene);
        var cameraObject=new GameObject("Camera");SceneManager.MoveGameObjectToScene(cameraObject,scene);
        var camera=cameraObject.AddComponent<Camera>();camera.fieldOfView=42;
        var f=root.AddComponent<BlockFoundry>();f.enabled=false;f.viewer=camera;f.hullMaterial=hull;f.previewMaterial=preview;
        var cursor=Cursor.lockState;bool visible=Cursor.visible;
        Keyboard keyboard=null;Mouse mouse=null;
        try
        {
            f.Initialize(SettlementExamples.Foundry());
            var interiors=f.Settlement.Interiors;interiors.colliders=true;
            var homes=f.Settlement.Structure.cells.Where(p=>p.Value.family==TileFamily.Habitat&&!interiors.Hub(p.Key)).Select(p=>p.Key).ToArray();
            int entered=0;
            foreach(var cell in homes)
            {
                var position=camera.transform.position;var rotation=camera.transform.rotation;
                var lens=camera.lensShift;float fov=camera.fieldOfView;
                Require(f.EnterWalk(cell),"Entry failed: "+cell);
                var w=scene.GetRootGameObjects().SelectMany(g=>g.GetComponentsInChildren<StationWalker>()).Single(w=>w.isActiveAndEnabled);
                Require(interiors.CellAt(w.EyePosition,out var actual)&&actual==cell,"Entered wrong tile: "+cell+" -> "+actual);
                Require(camera.lensShift==Vector2.zero&&camera.fieldOfView==75,"Walking camera not centered.");
                Require(root.GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Hull chunk")).All(c=>!c.enabled),"Picking hull blocks walking.");
                var feet=w.transform.position;
                for(int i=0;i<120;i++)w.Frame(Vector2.zero,Vector2.zero,false,0,false,1f/120);
                Require(w.State==StationWalker.Mode.Walk,"Home is floating: "+cell);
                Require(Vector3.Distance(feet,w.transform.position)<0.4f,"Spawn fell through floor or was pushed out: "+cell+" "+feet+" -> "+w.transform.position);
                f.ExitWalk();
                Require(!f.IsWalking&&camera.transform.position==position&&Quaternion.Angle(camera.transform.rotation,rotation)<0.01f,"Exit did not restore pose.");
                Require(camera.lensShift==lens&&camera.fieldOfView==fov,"Exit did not restore projection.");
                Require(root.GetComponentsInChildren<MeshCollider>().Where(c=>c.name.StartsWith("Hull chunk")).All(c=>c.enabled),"Picking not restored.");
                Require(Cursor.lockState==cursor&&Cursor.visible==visible,"Cursor not restored.");
                entered++;
            }
            Require(entered>0,"No homes tested.");
            Require(!f.EnterWalk(new Vector3Int(999,999,999))&&!f.IsWalking,"Missing cell should reject entry.");
            var unsupported=f.Settlement.Structure.cells.FirstOrDefault(p=>!TileBuilder.HasInterior(p.Value.family));
            if(!TileBuilder.HasInterior(unsupported.Value.family))Require(!f.EnterWalk(unsupported.Key),"Non-room accepted entry.");
            var hub=f.Settlement.Structure.cells.First(p=>interiors.Hub(p.Key)&&TileBuilder.HasInterior(p.Value.family)).Key;
            Require(f.EnterWalk(hub),"Hub entry failed.");CheckFloating(Walker(scene),"Hub");f.ExitWalk();

            // Same input routing as the game: point at a visible tile, I, then Esc. No construction edit may occur.
            keyboard=InputSystem.AddDevice<Keyboard>();mouse=InputSystem.AddDevice<Mouse>();
            await Task.Delay(100);
            var cellForInput=homes[0];var centre=interiors.Centre(cellForInput);
            var outward=(centre-f.BuildBounds.center).normalized;
            camera.transform.position=centre+outward*35;camera.transform.LookAt(centre);
            var screen=camera.WorldToScreenPoint(centre);
            var showHud=typeof(BlockFoundry).GetField("showHud",BindingFlags.NonPublic|BindingFlags.Instance);showHud.SetValue(f,false);
            Require(f.Pick(camera.ScreenPointToRay(screen),out var face,out _),"Input ray missed.");
            f.enabled=true;
            int revision=f.Revision;
            InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y)});
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.I));await Task.Delay(100);
            Require(f.IsWalking,"I input did not enter tile "+face.cell+" current="+(Keyboard.current==keyboard)+" held="+keyboard.iKey.isPressed+" pointer="+mouse.position.ReadValue()+" focus="+Application.isFocused);
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.F,Key.C,Key.Home));
            InputSystem.QueueStateEvent(mouse,new MouseState{buttons=1});await Task.Delay(100);
            Require(f.IsWalking&&f.Revision==revision,"Builder input leaked into walk mode.");
            InputSystem.QueueStateEvent(keyboard,new KeyboardState(Key.Escape));await Task.Delay(100);
            Require(!f.IsWalking,"Escape input did not exit.");
            Require(f.EnterWalk(homes[0]),"Reentry failed.");f.LoadExample(4);Require(!f.IsWalking,"Rebuild left stale walker.");
            f.NewBuild();
            Require(f.EnterWalk(Vector3Int.zero),"Seed entry failed.");CheckFloating(Walker(scene),"Seed");f.ExitWalk();
            f.LoadExample(4);
            var cursorBeforeDisable=Cursor.lockState;bool visibleBeforeDisable=Cursor.visible;
            Require(f.EnterWalk(homes[0]),"Reentry after rebuild failed.");f.enabled=false;
            Require(!f.IsWalking&&Cursor.lockState==cursorBeforeDisable&&Cursor.visible==visibleBeforeDisable,"Disable did not restore the cursor state from entry.");
            return $"PASS: {entered} grounded home spawns; hub and seed floating; invalid tile; I/Esc input; builder input isolation; camera/cursor/picking restoration; rebuild/disable cleanup.";
        }
        finally
        {
            if(keyboard!=null)InputSystem.RemoveDevice(keyboard);if(mouse!=null)InputSystem.RemoveDevice(mouse);
            Object.DestroyImmediate(root);Object.DestroyImmediate(cameraObject);
            Object.DestroyImmediate(hull);Object.DestroyImmediate(preview);await SceneManager.UnloadSceneAsync(scene);Application.runInBackground=background;
            InputSystem.settings.backgroundBehavior=inputFocus;
            InputSystem.settings.editorInputBehaviorInPlayMode=inputRoute;
            Cursor.lockState=cursor;Cursor.visible=visible;
        }
    }
}
