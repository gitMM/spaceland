using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace DeathStar
{
    public struct BlockFace
    {
        public Vector3Int cell;
        public int side;
        public BlockFace(Vector3Int cell, int side) { this.cell = cell; this.side = side; }
    }

    public sealed class BlockMeshData
    {
        public readonly List<Vector3> vertices = new List<Vector3>();
        public readonly List<Vector3> normals = new List<Vector3>();
        public readonly List<Vector2> uv = new List<Vector2>();
        public readonly List<Color> colors = new List<Color>();
        public readonly List<int> triangles = new List<int>();
        public readonly List<BlockFace> faces = new List<BlockFace>();
        public const int TrianglesPerFace = 8;

        public void AddFace(BlockConstruction model, Vector3Int cell, int side, Func<Vector3Int,Vector3> corner=null)
        {
            int start = vertices.Count;
            if(corner==null)corner=model.Corner;
            Vector3 a=corner(cell+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[side,0]));
            Vector3 b=corner(cell+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[side,1]));
            Vector3 c=corner(cell+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[side,2]));
            Vector3 d=corner(cell+BlockConstruction.CornerOffset(BlockConstruction.FaceCorners[side,3]));
            uint hash = BuildPlanetPlan.Hash(unchecked((uint)(cell.x * 73856093 ^ cell.y * 19349663 ^ cell.z * 83492791 ^ side * 71)));
            var color = new Color(0.42f + (hash & 255) / 255f * 0.25f, ((hash >> 8) & 255) / 255f, ((hash >> 16) & 255) / 255f, 1);
            for (int y = 0; y <= 2; y++) for (int x = 0; x <= 2; x++)
            {
                float u = x * 0.5f, v = y * 0.5f;
                vertices.Add(Vector3.Lerp(Vector3.Lerp(a, b, u), Vector3.Lerp(d, c, u), v));
                normals.Add(Vector3.Cross(Vector3.Lerp(b-a,c-d,v), Vector3.Lerp(d-a,c-b,u)).normalized);
                uv.Add(new Vector2(u,v)); colors.Add(color);
            }
            for (int y = 0; y < 2; y++) for (int x = 0; x < 2; x++)
            {
                int i = start + y * 3 + x;
                triangles.Add(i); triangles.Add(i+1); triangles.Add(i+4);
                triangles.Add(i); triangles.Add(i+4); triangles.Add(i+3);
            }
            faces.Add(new BlockFace(cell,side));
        }

        public void Apply(Mesh mesh)
        {
            mesh.Clear(); mesh.indexFormat = vertices.Count > 65535 ? IndexFormat.UInt32 : IndexFormat.UInt16;
            mesh.SetVertices(vertices); mesh.SetNormals(normals); mesh.SetUVs(0,uv); mesh.SetColors(colors);
            mesh.SetTriangles(triangles,0); mesh.RecalculateBounds();
        }

        public static BlockMeshData Build(BlockConstruction model, IEnumerable<Vector3Int> cells, Func<Vector3Int,Vector3> corner=null)
        {
            var data = new BlockMeshData();
            foreach (var cell in cells)
                for (int side = 0; side < 6; side++)
                    if (!model.Contains(cell + BlockConstruction.Steps[side])) data.AddFace(model,cell,side,corner);
            return data;
        }
    }
}
