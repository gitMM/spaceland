using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Authored occupancy and families only. The lab and Foundry run the same solver and curved renderer.
    public static class SettlementExamples
    {
        public static BlockConstruction Foundry()
        {
            var cells=new Dictionary<Vector3Int,TileFamily>();
            void Put(int x,int y,int z,TileFamily f)=>cells[new Vector3Int(x,y,z)]=f;
            void Fill(int x0,int y0,int z0,int x1,int y1,int z1,TileFamily f)
            {for(int x=x0;x<=x1;x++)for(int y=y0;y<=y1;y++)for(int z=z0;z<=z1;z++)Put(x,y,z,f);}
            Fill(-4,-1,-3,3,0,3,TileFamily.Hull);
            for(int x=-4;x<=3;x++)cells.Remove(new Vector3Int(x,0,-1));
            Fill(-3,1,0,2,1,2,TileFamily.Hull);
            Fill(-2,2,1,1,2,2,TileFamily.Hull);
            Fill(-3,1,0,2,1,0,TileFamily.Habitat);
            Fill(-4,0,0,3,0,0,TileFamily.Habitat);
            Fill(-2,2,2,1,2,2,TileFamily.LifeSupport);
            Fill(-4,1,0,-4,1,2,TileFamily.Power);
            Fill(-4,-1,1,-4,0,2,TileFamily.Storage);
            Fill(0,0,-4,1,0,-4,TileFamily.Hull);
            Fill(0,1,-4,1,1,-3,TileFamily.Pad);
            Fill(-3,1,-3,-3,2,-3,TileFamily.Hull);Put(-3,3,-3,TileFamily.Defense);
            Put(0,3,1,TileFamily.Command);
            Put(2,1,2,TileFamily.Industrial);
            foreach(int x in new[]{-2,2})Put(x,0,-1,TileFamily.Corridor);
            // A solid bridge with a clear opening under the span; concave corners produce arch haunches.
            Put(2,1,3,TileFamily.Hull);Put(2,3,3,TileFamily.Defense);
            Fill(1,2,3,3,2,3,TileFamily.Hull);Put(3,1,3,TileFamily.Hull);
            Fill(-2,1,3,0,1,3,TileFamily.Conduit);Put(0,1,2,TileFamily.Conduit);
            Put(2,-2,1,TileFamily.Defense);Put(-2,-2,2,TileFamily.Command);
            return BlockConstruction.FromFamilies(cells);
        }

        // A two-layer cap of a sphere grown from the seed, round one cube corner: three ridges on the cube's edges meet
        // at a crown. Every family sits on a ridge in a two-cell run; a run of wedges under the z ridge shows its keel
        // on the inside, and a short wall half-covers the roof of the y ridge's first run. The cut edge shows gables.
        // Coordinates below are for the (+x, +y, +z) corner; RidgeCell mirrors them to the corner facing the key light.
        public const int RidgeRadius=6,RidgeLow=-2;
        public static Vector3Int RidgeCell(int x,int y,int z)=>new Vector3Int(-x,y,-z);
        public static readonly TileFamily[][] RidgeRuns=
        {
            new[]{TileFamily.Habitat,TileFamily.Defense,TileFamily.Command,TileFamily.Power},          // along x
            new[]{TileFamily.Hull,TileFamily.Storage,TileFamily.Pad,TileFamily.Industrial},            // along y
            new[]{TileFamily.Corridor,TileFamily.Conduit,TileFamily.LifeSupport,TileFamily.Habitat},   // along z
        };
        public static readonly (int y,int z,TileFamily family)[] ScaleCells=
            {(0,0,TileFamily.Defense),(RidgeRadius-1,2,TileFamily.Defense),(0,3,TileFamily.Command),(RidgeRadius-1,4,TileFamily.Command)};
        public static BlockConstruction Ridges(TileFamily crown=TileFamily.Command)
        {
            const int R=RidgeRadius;
            var cells=new Dictionary<Vector3Int,TileFamily>();
            // Two layers, so single family cells have backing and face outward as in a real build.
            for(int x=RidgeLow;x<=R;x++)for(int y=RidgeLow;y<=R;y++)for(int z=RidgeLow;z<=R;z++)
                if(Mathf.Max(x,Mathf.Max(y,z))>=R-1)cells[RidgeCell(x,y,z)]=TileFamily.Hull;
            for(int axis=0;axis<3;axis++)for(int run=0;run<4;run++)for(int k=0;k<2;k++)
            {var c=new Vector3Int(R,R,R);c[axis]=RidgeLow+run*2+k;cells[RidgeCell(c.x,c.y,c.z)]=RidgeRuns[axis][run];}
            cells[RidgeCell(R,R,R)]=crown;
            // Clearing the inner layer beside the wedges under the z ridge opens their keel.
            for(int z=0;z<3;z++){cells.Remove(RidgeCell(R-2,R-1,z));cells.Remove(RidgeCell(R-1,R-2,z));}
            for(int y=RidgeLow;y<RidgeLow+2;y++)cells[RidgeCell(R+1,y,R)]=TileFamily.Hull;
            // Scale comparison: Defense and Command on a cube-face-centre cell and on a smaller cell beside a ridge.
            foreach(var (y,z,f) in ScaleCells)cells[RidgeCell(R,y,z)]=f;
            return BlockConstruction.FromFamilies(cells,BlockConstruction.SeedGuide);
        }
    }
}
