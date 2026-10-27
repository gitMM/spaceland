using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Family is authored; orientation is derived. No world direction, camera or edit history enters the score.
    public static class SettlementSolver
    {
        public static uint Seed(Vector3Int c,int salt=0)
        {
            uint h=unchecked((uint)c.x*73856093u^(uint)c.y*19349663u^(uint)c.z*83492791u^(uint)salt*2654435761u);
            h^=h>>16;h*=0x7feb352du;h^=h>>15;h*=0x846ca68bu;return h^(h>>16);
        }

        public static TileStructure Solve(BlockConstruction model)
        {
            var result=new TileStructure{automatic=true};
            var remaining=new HashSet<Vector3Int>(model.Cells);
            var sorted=new List<Vector3Int>(remaining);sorted.Sort(BlockConstruction.Compare);
            foreach(var start in sorted)
            {
                if(!remaining.Remove(start))continue;
                var family=model.Family(start);
                var component=new List<Vector3Int>{start};
                for(int i=0;i<component.Count;i++)foreach(var d in TileStructure.Directions)
                {
                    var q=component[i]+d;
                    if(remaining.Contains(q)&&model.Family(q)==family){remaining.Remove(q);component.Add(q);}
                }
                // All 24 proper cube rotations are candidates. Aggregate the neighbourhood over the family
                // component so the last quarter of a pad and both ends of a vault agree without relaxation/WFC.
                int best=int.MinValue;uint tie=0;TilePlacement chosen=default;
                foreach(var axis in TileStructure.Directions)foreach(var side in TileStructure.Directions)
                {
                    if(Vector3.Dot(axis,side)!=0)continue;
                    var third=TileStructure.Cross(side,axis);int score=0;
                    foreach(var c in component)
                    {
                        bool Same(Vector3Int d)=>model.Contains(c+d)&&model.Family(c+d)==family;
                        int Count(Vector3Int d)=>(Same(d)?1:0)+(Same(-d)?1:0);
                        int along=Count(axis),across=Count(side),row=Count(third);
                        bool front=model.Solid(c+axis),back=model.Solid(c-axis);
                        int support=back&&!Same(-axis)?1:0;
                        if(family==TileFamily.Corridor)
                            score+=along*24+(front?4:0)+(back?4:0);
                        else if(family==TileFamily.Conduit)
                            score+=support*20-along*32+(across+row)*8;
                        else
                        {
                            score+=(!front?4:0)+support*12;
                            if(family==TileFamily.Defense||family==TileFamily.Hull)
                                score+=along*6;
                            else score+=(across+row)*8-along*12;
                            // On an axial face, local y=side and local x=third. Hangars merge along y;
                            // vaults run along x. Other families can use either in-plane direction.
                            if(family==TileFamily.Storage)score+=across*3;
                            if(family==TileFamily.LifeSupport)score+=row*3;
                        }
                    }
                    uint rank=Seed(start,Direction(axis)*6+Direction(side)+1);
                    if(score>best||(score==best&&rank>tie))
                    {best=score;tie=rank;chosen=new TilePlacement(family,axis,side);}
                }
                bool spatial=family==TileFamily.Conduit&&component.Exists(c=>Vector3.Dot(c-start,chosen.axis)!=0);
                foreach(var c in component)result.cells[c]=new TilePlacement(family,chosen.axis,chosen.side,spatial,start);
            }
            return result;
        }
        static int Direction(Vector3Int d)=>System.Array.IndexOf(TileStructure.Directions,d);

        // Geometry reads face neighbours, edge diagonals and 2x2 corner occupancy. Component changes can
        // reorient distant cells; include those and a two-cell halo for connection frames and relief cutouts.
        public static HashSet<Vector3Int> Dirty(TileStructure before,TileStructure after,IEnumerable<Vector3Int> edited)
        {
            var changed=new HashSet<Vector3Int>(edited);
            foreach(var p in before.cells)if(!after.TryGet(p.Key,out var q)||!p.Value.SameAs(q))changed.Add(p.Key);
            foreach(var p in after.cells)if(!before.TryGet(p.Key,out var q)||!p.Value.SameAs(q))changed.Add(p.Key);
            var dirty=new HashSet<Vector3Int>();
            foreach(var c in changed)for(int x=-2;x<=2;x++)for(int y=-2;y<=2;y++)for(int z=-2;z<=2;z++)
                dirty.Add(c+new Vector3Int(x,y,z));
            return dirty;
        }
    }
}
