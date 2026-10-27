using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace DeathStar.EditorTools
{
    public static class BlockFoundryValidation
    {
        static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
        static string Json(BlockConstruction model)=>JsonUtility.ToJson(model.Capture());
        static List<Vector3Int> Box(int n)
        {
            var result=new List<Vector3Int>();
            for(int z=0;z<n;z++)for(int y=0;y<n;y++)for(int x=0;x<n;x++)result.Add(new Vector3Int(x,y,z));
            return result;
        }
        public static void Unchanged(BlockSave before,BlockConstruction after)
        {
            foreach(var vertex in before.corners)
                if(after.HasCorner(vertex.key))Require(after.Corner(vertex.key)==vertex.position,"An existing corner moved: "+vertex.key);
            Require(JsonUtility.ToJson(before.guide)==JsonUtility.ToJson(after.Guide),"An edit refitted the sphere guide.");
        }
        public static string Run()
        {
            foreach(var name in new[]{"DeathStar/Block Hull","DeathStar/Block Preview"})
            {var shader=Shader.Find(name);Require(shader!=null&&!ShaderUtil.ShaderHasError(shader),"Shader failed: "+name);}
            var cells=Box(8);var model=BlockConstruction.FromCells(cells);
            var originalGuide=new BlockGuide{shape=BlockShape.Rounded,strength=1,center=Vector3.one*4,extents=Vector3.one*4};
            foreach(var key in model.CornerKeys)Require(model.Corner(key)==originalGuide.Map(key),"Original sphere mapping was not restored.");
            Require(model.GeometryIsValid(),"Sphere contains folded cells.");
            // New placements must follow the same guide without moving the existing sphere.
            int edits=0;
            for(int x=8;x<13;x++)
            {
                var before=model.Capture();string json=Json(model);
                Require(model.TryEdit(new Vector3Int(x,3,3),false,out var preview),"Arm placement failed: "+x);
                Require(Json(model)==json,"Preview mutated live geometry.");
                Unchanged(before,preview);Require(preview.GeometryIsValid(),"Arm folded a cell.");
                model=preview;edits++;
            }
            var priorRemoval=model.Capture();Require(model.Remove(new Vector3Int(12,3,3)),"Arm removal failed.");Unchanged(priorRemoval,model);
            int seams=0;foreach(var cell in model.Cells)for(int side=0;side<6;side+=2)
            {
                var neighbour=cell+BlockConstruction.Steps[side];if(!model.Contains(neighbour))continue;
                var a=new HashSet<Vector3>();var b=new HashSet<Vector3>();
                for(int i=0;i<4;i++)
                {
                    a.Add(model.Corner(cell+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[side,i])));
                    b.Add(model.Corner(neighbour+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[side^1,i])));
                }
                Require(a.SetEquals(b),"Attachment seam has a gap.");seams++;
            }
            var mesh=BlockMeshData.Build(model,model.Cells);
            Require(mesh.triangles.Count==mesh.faces.Count*BlockMeshData.TrianglesPerFace*3,"Triangle ownership mismatch.");
            for(int i=0;i<mesh.triangles.Count;i+=3)
            {
                int a=mesh.triangles[i],b=mesh.triangles[i+1],c=mesh.triangles[i+2];
                Require(Vector3.Dot(Vector3.Cross(mesh.vertices[b]-mesh.vertices[a],mesh.vertices[c]-mesh.vertices[a]),mesh.normals[a])>0,"Invalid winding.");
            }
            var saved=model.Capture();var loaded=new BlockConstruction();loaded.Restore(saved);
            Require(Json(loaded)==Json(model),"v5 round trip lost geometry or guide.");
            foreach(int version in new[]{2,3,4})
            {
                var old=model.Capture();old.version=version;if(version>=3){old.types=new BlockType[old.cells.Length];old.types[0]=BlockType.Square;}
                loaded.Restore(old);
                foreach(var vertex in old.corners)Require(loaded.Corner(vertex.key)==vertex.position,"Migration moved existing geometry.");
            }
            loaded.Restore(new BlockSave{version=1,cells=cells.ToArray(),guide=originalGuide});
            foreach(var key in loaded.CornerKeys)Require(loaded.Corner(key)==originalGuide.Map(key),"v1 guide import changed shape.");
            string stable=Json(loaded);var bad=loaded.Capture();bad.guide.center.x=float.NaN;
            bool rejected=false;try{loaded.Restore(bad);}catch(ArgumentException){rejected=true;}
            Require(rejected&&stable==Json(loaded),"Invalid guide load damaged live state.");
            bad=loaded.Capture();bad.version=4;bad.types=new BlockType[bad.cells.Length];bad.types[0]=(BlockType)99;rejected=false;
            try{loaded.Restore(bad);}catch(ArgumentException){rejected=true;}
            Require(rejected&&stable==Json(loaded),"Invalid type load damaged live state.");
            var growth=SphereGrowthValidation.Run();
            return $"PASS: exact original sphere map; {edits} sphere-grid edits with zero existing-corner movement; {seams} shared faces; winding; v5 round trip; v1/v2/v3/v4 migration; invalid load atomicity; fixed-guide sphere growth. {growth}";
        }
    }
}