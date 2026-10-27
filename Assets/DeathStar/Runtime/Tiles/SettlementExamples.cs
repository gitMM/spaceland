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

        // The same kind of cap at the build limit (shell 63, 315 m out), where cells are narrowest (2.4 m round the
        // corner) and the ridges' folded roofs flattest. Every family runs along a ridge as in Ridges, and the crown is a
        // corridor node. In the narrow cells beside the x ridge: a corridor along it, so its tube spans the narrow way,
        // two single pads and a 2 x 2 pad, which take smaller size classes.
        public const int LimitRadius=63,LimitLow=LimitRadius-8;
        public static readonly Vector3Int[] LimitCorridor={new Vector3Int(58,62,63),new Vector3Int(59,62,63),new Vector3Int(60,62,63),new Vector3Int(61,62,63)};
        public static readonly Vector3Int[] LimitPads={new Vector3Int(60,60,63),new Vector3Int(57,60,63),
            new Vector3Int(55,63,55),new Vector3Int(56,63,55),new Vector3Int(55,63,56),new Vector3Int(56,63,56)};
        public static BlockConstruction Limit()
        {
            const int R=LimitRadius,L=LimitLow;
            var cells=new Dictionary<Vector3Int,TileFamily>();
            for(int x=L;x<=R;x++)for(int y=L;y<=R;y++)for(int z=L;z<=R;z++)
                if(Mathf.Max(x,Mathf.Max(y,z))>=R-1)cells[RidgeCell(x,y,z)]=TileFamily.Hull;
            for(int axis=0;axis<3;axis++)for(int run=0;run<4;run++)for(int k=0;k<2;k++)
            {var c=new Vector3Int(R,R,R);c[axis]=L+run*2+k;cells[RidgeCell(c.x,c.y,c.z)]=RidgeRuns[axis][run];}
            cells[RidgeCell(R,R,R)]=TileFamily.Corridor;
            foreach(var c in LimitCorridor)cells[RidgeCell(c.x,c.y,c.z)]=TileFamily.Corridor;
            foreach(var c in LimitPads)cells[RidgeCell(c.x,c.y,c.z)]=TileFamily.Pad;
            return BlockConstruction.FromFamilies(cells,BlockConstruction.SeedGuide);
        }

        // A whole station: a sphere grown from the seed, two layers thick and hollow, with every family on every shape.
        //   ridges   all 12, each in one family: all eleven, with Hull twice
        //   crowns   the 8 corners in the 8 other solid families, each joined by its own family's ridge, and a sunken
        //            Hull crown: the inner corner bared by a pit beside the Habitat crown
        //   faces    2 x 2 landmarks at five face centres (dish, reactor, sphere tank, hangar, big pad); window bands,
        //            a vault row, a fin row and a storage row; turret towers, a command tower, industrial pits, single
        //            pads; a corridor out into space; a conduit on saddles from a power block to a habitat
        //   others   a wall half-covering a Hull ridge's roof; a gap in the other Hull ridge, opening two gables, and
        //            a keel under it inside; a spike inside under the Life Support crown; small boxes near the
        //            diagonals and buried wedges and apexes in the inner layer come with the sphere
        // Face cells are given as (face axis, face sign, u, v, layer): u along axis+1, v along axis+2, layer 0 the
        // outer shell, 1 and 2 standing on it.
        public const int StationRadius=5;
        public static readonly (int axis,int s1,int s2,TileFamily family)[] StationRidges=
        {
            (0,1,1,TileFamily.Hull),(0,1,-1,TileFamily.Habitat),(0,-1,1,TileFamily.Storage),(0,-1,-1,TileFamily.Pad),
            (1,1,1,TileFamily.Defense),(1,1,-1,TileFamily.LifeSupport),(1,-1,1,TileFamily.Industrial),(1,-1,-1,TileFamily.Command),
            (2,1,1,TileFamily.Hull),(2,1,-1,TileFamily.Corridor),(2,-1,1,TileFamily.Power),(2,-1,-1,TileFamily.Conduit),
        };
        public static readonly (Vector3Int corner,TileFamily family)[] StationCrowns=
        {
            (new Vector3Int(-1,1,-1),TileFamily.Command),(new Vector3Int(1,1,-1),TileFamily.Habitat),(new Vector3Int(-1,1,1),TileFamily.Power),
            (new Vector3Int(1,1,1),TileFamily.Defense),(new Vector3Int(-1,-1,-1),TileFamily.Pad),(new Vector3Int(1,-1,1),TileFamily.Storage),
            (new Vector3Int(1,-1,-1),TileFamily.Industrial),(new Vector3Int(-1,-1,1),TileFamily.LifeSupport),
        };
        public static BlockConstruction Station()
        {
            const int R=StationRadius;
            var cells=new Dictionary<Vector3Int,TileFamily>();
            for(int x=-R;x<=R;x++)for(int y=-R;y<=R;y++)for(int z=-R;z<=R;z++)
                if(Mathf.Max(Mathf.Abs(x),Mathf.Max(Mathf.Abs(y),Mathf.Abs(z)))>=R-1)cells[new Vector3Int(x,y,z)]=TileFamily.Hull;
            // Ridge k runs along axis k; s1 and s2 are the signs of the other two axes, (k+1)%3 and (k+2)%3.
            Vector3Int Ridge(int k,int s1,int s2,int t,int o1=0,int o2=0){var c=Vector3Int.zero;c[k]=t;c[(k+1)%3]=s1*(R+o1);c[(k+2)%3]=s2*(R+o2);return c;}
            foreach(var (k,s1,s2,family) in StationRidges)for(int t=-(R-1);t<=R-1;t++)cells[Ridge(k,s1,s2,t)]=family;
            foreach(var (corner,family) in StationCrowns)cells[corner*R]=family;
            void Put(int axis,int sign,int u,int v,int layer,TileFamily family)
            {var c=Vector3Int.zero;c[axis]=sign*(R+layer);c[(axis+1)%3]=u;c[(axis+2)%3]=v;cells[c]=family;}
            void Block(int axis,int sign,TileFamily family){for(int u=0;u<2;u++)for(int v=0;v<2;v++)Put(axis,sign,u,v,0,family);}
            // +y: big dish, a window band, a turret tower, a pit
            Block(1,1,TileFamily.Command);
            for(int u=-3;u<=2;u++)Put(1,1,u,-3,0,TileFamily.Habitat);
            Put(1,1,3,3,1,TileFamily.Hull);Put(1,1,3,3,2,TileFamily.Defense);Put(1,1,-3,3,0,TileFamily.Industrial);
            // -y: reactor, a conduit on saddles from a power block to a habitat, a pad
            Block(1,-1,TileFamily.Power);
            Put(1,-1,-4,-3,1,TileFamily.Power);for(int u=-3;u<=1;u++)Put(1,-1,u,-3,1,TileFamily.Conduit);Put(1,-1,2,-3,1,TileFamily.Habitat);
            Put(1,-1,3,3,0,TileFamily.Pad);
            // +x: sphere tank, a vault row, a corridor out into space with its docking collar
            Block(0,1,TileFamily.LifeSupport);
            for(int u=-1;u<=1;u++)Put(0,1,u,-3,0,TileFamily.LifeSupport);
            Put(0,1,-3,3,1,TileFamily.Corridor);Put(0,1,-3,3,2,TileFamily.Corridor);
            // -x: hangar mouth, a storage row, a command tower
            Block(0,-1,TileFamily.Storage);
            for(int u=-2;u<=0;u++)Put(0,-1,u,3,0,TileFamily.Storage);
            Put(0,-1,-3,-3,0,TileFamily.Command);
            // +z: big pad, single pads, a window band
            Block(2,1,TileFamily.Pad);
            Put(2,1,-3,-3,0,TileFamily.Pad);Put(2,1,3,-3,0,TileFamily.Pad);
            for(int u=-2;u<=2;u++)Put(2,1,u,3,0,TileFamily.Habitat);
            // -z: pits, a fin row, a turret tower
            Put(2,-1,-2,-2,0,TileFamily.Industrial);Put(2,-1,2,-2,0,TileFamily.Industrial);
            for(int u=-1;u<=1;u++)Put(2,-1,u,3,0,TileFamily.Power);
            Put(2,-1,-3,3,1,TileFamily.Hull);Put(2,-1,-3,3,2,TileFamily.Defense);
            // A wall half-covering the first Hull ridge's roof.
            for(int t=-1;t<=0;t++)cells[Ridge(0,1,1,t,1,0)]=TileFamily.Hull;
            // A gap in the second Hull ridge (two gables), and a keel under it inside: the inner layer cleared beside
            // the wedges below.
            cells.Remove(Ridge(2,1,1,0));
            for(int t=-3;t<=-1;t++){cells.Remove(Ridge(2,1,1,t,-2,-1));cells.Remove(Ridge(2,1,1,t,-1,-2));}
            // A pit beside the Habitat crown bares the inner corner below it: a sunken Hull crown.
            var pit=new Vector3Int(1,1,-1);
            for(int a=0;a<3;a++){var c=pit*(R-1);c[a]=pit[a]*R;cells.Remove(c);}
            // Inside, under the Life Support crown, the inner layer cleared round the inner corner leaves a spike.
            var spike=new Vector3Int(-1,-1,1);
            for(int a=0;a<3;a++){var c=spike*(R-1);c[a]=spike[a]*(R-2);cells.Remove(c);}
            return BlockConstruction.FromFamilies(cells,BlockConstruction.SeedGuide);
        }
    }
}
