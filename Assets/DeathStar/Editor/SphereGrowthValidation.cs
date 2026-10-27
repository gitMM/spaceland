using System;
using System.Collections.Generic;
using UnityEngine;
namespace DeathStar.EditorTools
{
    public static class SphereGrowthValidation
    {
        public static object Run()
        {
            var cells=new List<Vector3Int>();
            for(int z=-2;z<=2;z++)for(int y=-2;y<=2;y++)for(int x=-2;x<=2;x++)cells.Add(new Vector3Int(x,y,z));
            cells.Sort((a,b)=>{int d=a.sqrMagnitude.CompareTo(b.sqrMagnitude);return d!=0?d:BlockConstruction.Compare(a,b);});
            var model=new BlockConstruction();
            foreach(var cell in cells)
            {
                if(model.Contains(cell))continue;
                var before=model.Capture();
                if(!model.Add(cell))throw new Exception("Fixed sphere growth rejected "+cell+": "+model.LastError);
                BlockFoundryValidation.Unchanged(before,model);
            }
            float error=0;
            foreach(var key in model.CornerKeys)
                if(key.x==-2||key.y==-2||key.z==-2||key.x==3||key.y==3||key.z==3)
                    error=Mathf.Max(error,Mathf.Abs((model.Corner(key)-Vector3.one*0.5f).magnitude-2.5f));
            if(error>0.0001f||!model.GeometryIsValid())throw new Exception("Fixed guide failed to produce a valid sphere: "+error);
            return new {blocks=model.Count,maxRadialError=error,existingCornerMovement=0};
        }
    }
}