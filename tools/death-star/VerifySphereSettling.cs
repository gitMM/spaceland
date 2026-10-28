using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DeathStar;
using DeathStar.EditorTools;
using UnityEngine;
// Fresh-session check: replaces the live build with a sphere grown around its seed.
public static class SphereSettlingPlayCheck
{
    public static async Task<object> Run()
    {
        var f=UnityEngine.Object.FindFirstObjectByType<BlockFoundry>();
        if(f==null||f.Model==null)throw new Exception("Builder is not playing.");
        f.NewBuild();
        var cells=new List<Vector3Int>();
        for(int z=-2;z<=2;z++)for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)cells.Add(new Vector3Int(x,y,z));
        cells.Sort((a,b)=>{int d=a.sqrMagnitude.CompareTo(b.sqrMagnitude);return d!=0?d:BlockConstruction.Compare(a,b);});
        foreach(var cell in cells)
        {
            if(f.Model.Contains(cell))continue;bool added=false;var before=f.Model.Capture();
            for(int side=0;side<6;side++)
            {
                var neighbour=cell-BlockConstruction.Steps[side];if(!f.Model.Contains(neighbour))continue;
                if(f.EditFace(new BlockFace(neighbour,side),false)){added=true;break;}
            }
            if(!added)throw new Exception("Placement failed: "+cell);
            BlockFoundryValidation.Unchanged(before,f.Model);
        }
        await Task.Yield();
        float error=0;
        foreach(var key in f.Model.CornerKeys)
            if(key.x==-2||key.y==-2||key.z==-2||key.x==3||key.y==3||key.z==3)
                error=Mathf.Max(error,Mathf.Abs((f.Model.Corner(key)-Vector3.one*0.5f).magnitude-2.5f));
        if(error>0.0001f||!f.Model.GeometryIsValid())throw new Exception("Live sphere invalid: "+error);
        f.FrameBuild(true);
        return new {status="PASS",blocks=f.Model.Count,maxRadialError=error,existingCornerMovement=0};
    }
}