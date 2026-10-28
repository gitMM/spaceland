using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using DeathStar;

public static class BlockFoundryPlayCheck
{
    static void Require(bool test,string message){if(!test)throw new Exception(message);}
    public static async Task<object> Run()
    {
        bool background=Application.runInBackground;Application.runInBackground=true;
        try
        {
            var foundry=UnityEngine.Object.FindFirstObjectByType<BlockFoundry>();
            Require(foundry!=null&&foundry.Model!=null,"Builder failed to initialize.");
            int frame=Time.frameCount;await Task.Delay(250);
            Require(Time.frameCount>frame,"Player loop is not advancing.");
            foundry.NewBuild();
            Require(foundry.Model.Count==1&&foundry.ExposedFaces==6,"Wrong initial geometry.");
            int picks=0;
            for(int side=0;side<6;side++)
            {
                Vector3 direction=BlockConstruction.Steps[side];
                var ray=new Ray(Vector3.one*0.5f+direction*4,-direction);
                Require(foundry.Pick(ray,out var hit,out _)&&hit.cell==Vector3Int.zero&&hit.side==side,"Seed face pick failed: "+side);
                picks++;
            }
            Require(foundry.EditFace(new BlockFace(Vector3Int.zero,0),false),"Face placement failed.");
            Require(foundry.Model.Count==2&&foundry.ExposedFaces==10,"Placement mesh did not update.");
            foundry.Undo();Require(foundry.Model.Count==1&&foundry.ExposedFaces==6,"Undo failed.");
            foundry.Redo();Require(foundry.Model.Count==2&&foundry.ExposedFaces==10,"Redo failed.");
            Require(foundry.EditFace(new BlockFace(Vector3Int.right,0),true),"Removal failed.");
            Require(foundry.Model.Count==1,"Removal did not change occupancy.");
            // Grow across positive and negative chunk boundaries, then validate the exposed skin.
            for(int i=0;i<10;i++)Require(foundry.EditFace(new BlockFace(new Vector3Int(i,0,0),0),false),"Positive chunk growth failed.");
            for(int i=0;i<10;i++)Require(foundry.EditFace(new BlockFace(new Vector3Int(-i,0,0),1),false),"Negative chunk growth failed.");
            Require(foundry.Model.Count==21&&foundry.ExposedFaces==86,"Chunk seams left duplicate/missing faces.");
            foundry.LoadExample(2);
            Require(foundry.Model.Count==712,"Orbital shell occupancy incorrect.");
            // All exterior exposed triangles should pick back to the originating face, including across chunks.
            foreach(var cell in foundry.Model.Cells)
            for(int side=0;side<6;side++)
            {
                if(foundry.Model.Contains(cell+BlockConstruction.Steps[side]))continue;
                var p=foundry.Model.FacePoint(cell,side,0.33f,0.31f);
                var du=foundry.Model.FacePoint(cell,side,0.34f,0.31f)-p;
                var dv=foundry.Model.FacePoint(cell,side,0.33f,0.32f)-p;
                var normal=Vector3.Cross(du,dv).normalized;
                // The bilinear face uses a triangulated approximation; use a small but sufficient offset.
                var ray=new Ray(p+normal*0.15f,-normal);
                Require(foundry.Pick(ray,out var face,out _)&&face.cell==cell&&face.side==side,"Deformed face picking failed: "+cell+" / "+side);
                picks++;
            }
            // Two quick edits on the sphere, then undo both back to its exact corners.
            foundry.LoadExample(3);
            var beforeEdits=foundry.Model.Capture();
            BlockFace editFace=default;bool selected=false;
            foreach(var cell in foundry.Model.Cells)
            {
                if(cell.x!=3||cell.y!=0||cell.z!=0)continue;
                editFace=new BlockFace(cell,0);selected=true;break;
            }
            Require(selected&&foundry.EditFace(editFace,false),"Sphere attachment failed.");
            Require(foundry.EditFace(new BlockFace(new Vector3Int(4,0,0),0),false),"Rapid follow-up edit failed.");
            foundry.Undo();foundry.Undo();Require(JsonUtility.ToJson(foundry.Model.Capture())==JsonUtility.ToJson(beforeEdits),"Undo lost exact corner positions.");
            foundry.Redo();Require(foundry.Model.GeometryIsValid(),"Redo restored invalid deformation.");
            foundry.NewBuild();foundry.FrameBuild(true);
            await Task.Delay(150);
            // Drive the same Input System path used by a real click, rather than calling EditFace.
            Vector3 screen=foundry.viewer.WorldToScreenPoint(new Vector3(0.35f,0.4f,0));
            Require(foundry.Pick(foundry.viewer.ScreenPointToRay(screen),out _,out _),"Screen-space seed pick missed.");
            var mouse=InputSystem.AddDevice<Mouse>("BlockFoundryTestMouse");
            try
            {
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y)});
                await Task.Delay(60);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y),buttons=1});
                await Task.Delay(100);
                InputSystem.QueueStateEvent(mouse,new MouseState{position=new Vector2(screen.x,screen.y)});
                await Task.Delay(60);
                Require(foundry.Model.Count==2,"Actual pointer input failed to add a block.");
            }
            finally{InputSystem.RemoveDevice(mouse);}
            foundry.NewBuild();foundry.FrameBuild(true);
            return new {status="PASS",picks,framesAdvanced=Time.frameCount-frame,checks="Six-sided picking, placement/removal, exact corner undo/redo, chunk seams, hollow sphere, every deformed exposed face, actual Input System click",blocks=foundry.Model.Count};
        }
        finally{Application.runInBackground=background;}
    }
}
