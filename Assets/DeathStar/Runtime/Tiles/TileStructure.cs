using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // A tile in the lattice. There is no world up: axis and side are the tile's own +Y and +X in station space,
    // any of the 24 ways a cube can sit.
    public readonly struct TilePlacement
    {
        public readonly TileFamily family;
        public readonly Vector3Int axis,side;
        public readonly bool spatial;
        public readonly Vector3Int anchor;
        public TilePlacement(TileFamily family,Vector3Int axis,Vector3Int side,bool spatial=false,Vector3Int anchor=default)
        {this.family=family;this.axis=axis;this.side=side;this.spatial=spatial;this.anchor=anchor;}
        public Vector3Int Third=>TileStructure.Cross(side,axis);
        public bool SameAs(in TilePlacement o)=>family==o.family&&axis==o.axis&&side==o.side&&spatial==o.spatial&&anchor==o.anchor;
    }

    // Cells of a station. Cell c is a 5 m cube centred on c*5 in station space. Corridors and conduits are open
    // connectors, not solid: the faces next to them stay exposed.
    public sealed class TileStructure
    {
        public bool automatic;
        public readonly Dictionary<Vector3Int,TilePlacement> cells=new Dictionary<Vector3Int,TilePlacement>();
        // Wedge and apex cells of a curved build (BlockConstruction.Shape): no 2 x 2 landmark includes them.
        public readonly HashSet<Vector3Int> special=new HashSet<Vector3Int>();
        public static readonly Vector3Int[] Directions={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,new Vector3Int(0,0,1),new Vector3Int(0,0,-1)};

        public static Vector3 Center(Vector3Int c)=>(Vector3)c*TileBuilder.Cell;
        public static Vector3Int Cross(Vector3Int a,Vector3Int b)=>new Vector3Int(a.y*b.z-a.z*b.y,a.z*b.x-a.x*b.z,a.x*b.y-a.y*b.x);

        public bool Solid(Vector3Int c)=>cells.TryGetValue(c,out var p)&&p.family!=TileFamily.Corridor&&p.family!=TileFamily.Conduit;
        public bool TryGet(Vector3Int c,out TilePlacement p)=>cells.TryGetValue(c,out p);
        public void Set(Vector3Int c,TileFamily f,Vector3Int axis,Vector3Int side)=>cells[c]=new TilePlacement(f,axis,side);
        public void Fill(Vector3Int a,Vector3Int b,TileFamily f,Vector3Int axis,Vector3Int side)
        {
            for(int x=Mathf.Min(a.x,b.x);x<=Mathf.Max(a.x,b.x);x++)for(int y=Mathf.Min(a.y,b.y);y<=Mathf.Max(a.y,b.y);y++)
                for(int z=Mathf.Min(a.z,b.z);z<=Mathf.Max(a.z,b.z);z++)Set(new Vector3Int(x,y,z),f,axis,side);
        }
        public void Clear(Vector3Int a,Vector3Int b)
        {
            for(int x=Mathf.Min(a.x,b.x);x<=Mathf.Max(a.x,b.x);x++)for(int y=Mathf.Min(a.y,b.y);y<=Mathf.Max(a.y,b.y);y++)
                for(int z=Mathf.Min(a.z,b.z);z<=Mathf.Max(a.z,b.z);z++)cells.Remove(new Vector3Int(x,y,z));
        }

        // The face a tile shows its main form on: its own axis if open, else the first open side.
        public Vector3Int Primary(Vector3Int c,in TilePlacement p)
        {
            // A stack of Defense cells is one tower. Only the exposed tip carries the turret.
            if(automatic&&p.family==TileFamily.Defense)return p.axis;
            if(!Solid(c+p.axis))return p.axis;
            var t=p.Third;
            foreach(var d in new[]{p.side,-p.side,t,-t})if(!Solid(c+d))return d;
            return -p.axis;
        }
        // Whether the neighbour across an in-plane step carries the same family form on the same face.
        public bool Continues(Vector3Int c,in TilePlacement p,Vector3Int face,Vector3Int step)
        {
            var d=c+step;
            return TryGet(d,out var q)&&q.SameAs(p)&&!Solid(d+face);
        }

        public List<Vector3Int> CorridorLinks(Vector3Int c,in TilePlacement p)
        {
            var links=new List<Vector3Int>();
            if(automatic)foreach(var d in Directions)if(cells.ContainsKey(c+d))links.Add(d);
            if(links.Count==0){links.Add(p.axis);links.Add(-p.axis);}
            else if(links.Count==1)links.Add(-links[0]);
            return links;
        }

        // ---- the review station
        public readonly struct Specimen
        {
            public readonly TileFamily family;public readonly Vector3Int origin,up,side;
            public Specimen(TileFamily f,Vector3Int o,Vector3Int up,Vector3Int side){family=f;origin=o;this.up=up;this.side=side;}
            public Vector3Int TileCell=>origin+up;
        }

        public static readonly Vector3Int X=Vector3Int.right,Y=Vector3Int.up,Z=new Vector3Int(0,0,1);

        // A floating fragment of station hull, 80 x 30 x 60 m: a base slab with a trench along its near side; a
        // stepped superstructure of four tiers whose fronts carry rows of habitat bands, power banks on its flanks
        // and vaults on its back terrace, crowned by a command tower; three slab towers with turrets on the near
        // edge; two hangar slots in one end; a cantilever with a turret pointing sideways out of the other; power
        // banks, turrets, a keel and an upside-down tower underneath. Plus nine specimens, one per family, each on a
        // 3 x 3 hull slab in a different orientation.
        public static TileStructure Showcase(List<Specimen> specimens)
        {
            var s=new TileStructure();var nZ=new Vector3Int(0,0,-1);
            Vector3Int V(int x,int y,int z)=>new Vector3Int(x,y,z);
            s.Fill(V(0,0,0),V(15,1,11),TileFamily.Hull,Y,X);
            s.Clear(V(0,1,2),V(15,1,3));                                                    // trench
            s.Fill(V(1,2,4),V(14,2,10),TileFamily.Hull,Y,X);                                // tiers
            s.Fill(V(3,3,5),V(12,3,9),TileFamily.Hull,Y,X);
            s.Fill(V(5,4,6),V(10,4,8),TileFamily.Hull,Y,X);
            s.Fill(V(1,2,4),V(14,2,4),TileFamily.Habitat,Y,X);                              // band rows on the tier fronts
            s.Fill(V(3,3,5),V(12,3,5),TileFamily.Habitat,Y,X);
            s.Fill(V(5,4,6),V(10,4,6),TileFamily.Habitat,Y,X);
            s.Fill(V(0,1,4),V(15,1,4),TileFamily.Habitat,Y,X);                              // and down the trench's far wall
            s.Fill(V(1,2,5),V(1,2,9),TileFamily.Power,Y,X);s.Fill(V(14,2,5),V(14,2,9),TileFamily.Power,Y,X);   // flanks
            s.Fill(V(3,2,10),V(12,2,10),TileFamily.LifeSupport,Y,Z);                        // back terrace vaults
            s.Fill(V(7,5,7),V(8,5,7),TileFamily.Hull,Y,X);s.Set(V(7,6,7),TileFamily.Command,Y,X);   // crown
            foreach(int x in new[]{2,7,12}){s.Fill(V(x,2,0),V(x,4,0),TileFamily.Hull,Y,X);s.Set(V(x,5,0),TileFamily.Defense,Y,nZ);}
            s.Set(V(4,0,2),TileFamily.Industrial,Y,X);s.Set(V(10,0,3),TileFamily.Industrial,Y,Z);  // pits in the trench floor
            s.Set(V(5,1,2),TileFamily.Corridor,Z,X);s.Set(V(5,1,3),TileFamily.Corridor,Z,X);       // bridges
            s.Set(V(11,1,2),TileFamily.Corridor,Z,X);s.Set(V(11,1,3),TileFamily.Corridor,Z,X);
            s.Fill(V(0,0,6),V(0,1,9),TileFamily.Storage,-X,Z);                              // hangar slots in the near end
            s.Fill(V(15,2,6),V(17,2,7),TileFamily.Hull,Y,X);s.Set(V(18,2,6),TileFamily.Defense,X,Y);   // cantilever
            s.Set(V(13,2,7),TileFamily.Industrial,Y,X);s.Set(V(11,3,8),TileFamily.Industrial,Y,Z);
            s.Fill(V(3,-1,3),V(6,-1,5),TileFamily.Power,-Y,X);                             // underneath
            s.Fill(V(5,-1,8),V(11,-1,8),TileFamily.Hull,-Y,X);
            s.Set(V(9,-1,9),TileFamily.Defense,-Y,Z);s.Set(V(13,-1,2),TileFamily.Defense,-Y,X);
            s.Set(V(2,-1,9),TileFamily.Command,-Y,X);
            // Landing pads: singles on the near edge between the towers and on a tier, a 2 x 2 pad on the cantilever,
            // one on the underside.
            s.Set(V(4,1,0),TileFamily.Pad,Y,X);s.Set(V(9,1,0),TileFamily.Pad,Y,X);s.Set(V(13,2,8),TileFamily.Pad,Y,X);
            s.Fill(V(16,2,6),V(17,2,7),TileFamily.Pad,Y,X);
            s.Set(V(12,0,10),TileFamily.Pad,-Y,X);
            // A conduit from a power block along the near strip, round an elbow and over the trench into the habitats.
            s.Set(V(0,2,1),TileFamily.Power,Y,X);
            s.Fill(V(1,2,1),V(12,2,1),TileFamily.Conduit,Y,X);
            s.Fill(V(12,2,2),V(12,2,3),TileFamily.Conduit,Y,Z);
            // and one along the underside keel
            s.Fill(V(5,-1,7),V(11,-1,7),TileFamily.Conduit,-Y,X);
            // 2 x 2 landmarks: a big dish on the near face between the pads, a reactor dome inside the underside power block (it is a
            // 2 x 2 block of its own), a sphere tank on the near strip; the hangar slots already form two big mouths.
            s.Fill(V(10,0,0),V(11,1,0),TileFamily.Command,nZ,X);
            s.Fill(V(14,1,0),V(15,1,1),TileFamily.LifeSupport,Y,X);

            // Specimens, lit from the sun's side whatever their orientation.
            var fam=(TileFamily[])System.Enum.GetValues(typeof(TileFamily));
            var ups=new[]{(Y,X),(-X,Z),(new Vector3Int(0,0,-1),Y),(Y,Z),(-X,-Y),(new Vector3Int(0,0,-1),-X),(Y,-X),(-X,Y),(Y,X),(new Vector3Int(0,0,-1),-Y),(new Vector3Int(0,0,-1),X)};
            for(int i=0;i<fam.Length;i++)
            {
                var (up,side)=ups[i];var third=Cross(side,up);
                var o=new Vector3Int(-8+(i%6)*6,2,-16-(i/6)*7);
                for(int a=-1;a<=1;a++)for(int b=-1;b<=1;b++)s.Set(o+side*a+third*b,TileFamily.Hull,up,side);
                // A corridor specimen lies along the slab; a conduit turns a corner on it; everything else stands on it.
                if(fam[i]==TileFamily.Corridor)s.Set(o+up,fam[i],side,up);
                else if(fam[i]==TileFamily.Conduit){s.Set(o+up,fam[i],up,side);s.Set(o+up-side,fam[i],up,side);s.Set(o+up+third,fam[i],up,side);}
                else s.Set(o+up,fam[i],up,side);
                specimens?.Add(new Specimen(fam[i],o,up,side));
            }
            return s;
        }
    }
}
