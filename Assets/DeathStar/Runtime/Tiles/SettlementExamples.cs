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
    }
}
