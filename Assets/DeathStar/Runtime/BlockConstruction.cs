using System;
using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // Legacy v3/v4 shape types only; current cells carry settlement families on the fixed sphere grid.
    public enum BlockType { Round, Square }
    // The original fixed guide maps lattice shells onto a sphere. Flat/Tapered
    // remain readable for legacy saves; new builds use Rounded.
    public enum BlockShape { Flat, Rounded, Tapered }
    [Serializable]
    public struct BlockGuide
    {
        public BlockShape shape;
        public float strength;
        public Vector3 center,extents;
        public Vector3 Map(Vector3 p)
        {
            Vector3 half=Vector3.Max(extents,Vector3.one*0.5f),d=p-center;
            if(shape==BlockShape.Rounded)
            {
                var q=new Vector3(d.x/half.x,d.y/half.y,d.z/half.z);
                float factor=q.magnitude>0.00001f?Mathf.Max(Mathf.Abs(q.x),Mathf.Max(Mathf.Abs(q.y),Mathf.Abs(q.z)))/q.magnitude:1;
                return center+d*Mathf.Lerp(1,factor,strength);
            }
            if(shape==BlockShape.Tapered)
            {
                float scale=Mathf.Lerp(1,Mathf.Lerp(1.25f,0.4f,Mathf.Clamp01((d.y/half.y+1)*0.5f)),strength);
                return center+new Vector3(d.x*scale,d.y,d.z*scale);
            }
            return p;
        }
    }
    // The sphere map folds the cells on the cube's diagonals: two adjacent faces (wedge, along a cube edge) or three
    // (apex, at a cube corner) flatten into one curved roof. Everything else is a box, however sheared.
    public enum CellShape { Box, Wedge, Apex }
    // How a special cell meets open space. Ridge/Crown: the whole roof (a nearly flat band or triangle) is open. Half:
    // part of it is covered. Keel/Spike: the roof is covered and the slopes are open, so they meet in a sharp 46-60°
    // edge (wedge) or point (apex) sticking into open space. Box cells report Box.
    public enum CellExposure { Box, Buried, Ridge, Half, Keel, Crown, Spike }
    public readonly struct ShapeInfo
    {
        public const float SmallEdge=3.2f;
        public readonly CellShape kind;
        public readonly int spine;              // wedge: the lattice axis along its ridge; otherwise -1
        public readonly byte roof;              // merged faces, bit i for BlockConstruction.Steps[i]
        public readonly float minEdge,maxEdge;  // metres
        public ShapeInfo(CellShape kind,int spine,byte roof,float minEdge,float maxEdge)
        {this.kind=kind;this.spine=spine;this.roof=roof;this.minEdge=minEdge;this.maxEdge=maxEdge;}
        public bool Small=>kind==CellShape.Box&&minEdge<SmallEdge;
    }
    [Serializable]
    public struct BlockCorner { public Vector3Int key; public Vector3 position; }
    [Serializable]
    public sealed class BlockSave
    {
        public int version=6;
        public BlockGuide guide;
        public Vector3Int[] cells;
        public BlockType[] types;
        public TileFamily[] families;
        public BlockCorner[] corners;
    }

    public sealed class BlockConstruction
    {
        public const int MaxBlocks=32768,CoordinateLimit=64;
        public const float MaxEditMovement=0.08f;
        public const int InfluenceSteps=6;
        public static readonly Vector3Int[] Steps={Vector3Int.right,Vector3Int.left,Vector3Int.up,Vector3Int.down,Vector3Int.forward,Vector3Int.back};
        public static readonly int[,] FaceCorners={{1,3,7,5},{4,6,2,0},{2,6,7,3},{4,0,1,5},{5,7,6,4},{0,2,3,1}};
        HashSet<Vector3Int> cells=new HashSet<Vector3Int>();
        Dictionary<Vector3Int,TileFamily> families=new Dictionary<Vector3Int,TileFamily>();
        Dictionary<Vector3Int,Vector3> corners=new Dictionary<Vector3Int,Vector3>();
        Dictionary<Vector3Int,ShapeInfo> shapes;
        // The guide a new build grows from: centred on the seed cell.
        public static BlockGuide SeedGuide=>new BlockGuide{shape=BlockShape.Rounded,strength=1,center=Vector3.one*0.5f,extents=Vector3.one*0.5f};
        BlockGuide guide=SeedGuide;
        public BlockGuide Guide=>guide;
        public bool RelaxationPending=>false;
        public IEnumerable<Vector3Int> Cells=>cells;
        public IEnumerable<Vector3Int> CornerKeys=>corners.Keys;
        public int Count=>cells.Count;
        public float LastMaxMovement {get;private set;}
        public float LastSafetyScale {get;private set;}=1;
        public string LastError {get;private set;}
        public HashSet<Vector3Int> ChangedCells {get;private set;}=new HashSet<Vector3Int>();
        public BlockConstruction()
        {
            cells.Add(Vector3Int.zero);
            for(int i=0;i<8;i++){var key=CornerOffset(i);corners[key]=guide.Map(key);}
        }
        public static int Compare(Vector3Int a,Vector3Int b)=>a.x!=b.x?a.x.CompareTo(b.x):a.y!=b.y?a.y.CompareTo(b.y):a.z.CompareTo(b.z);
        public static bool InRange(Vector3Int p)=>p.x>=-CoordinateLimit&&p.y>=-CoordinateLimit&&p.z>=-CoordinateLimit&&p.x<CoordinateLimit&&p.y<CoordinateLimit&&p.z<CoordinateLimit;
        public static Vector3Int CornerOffset(int corner)=>new Vector3Int(corner&1,(corner>>1)&1,(corner>>2)&1);
        public bool Contains(Vector3Int p)=>cells.Contains(p);
        public TileFamily Family(Vector3Int p)=>families.TryGetValue(p,out var f)?f:TileFamily.Hull;
        public bool Solid(Vector3Int p)=>Contains(p)&&Family(p)!=TileFamily.Corridor&&Family(p)!=TileFamily.Conduit;
        public bool HasCorner(Vector3Int key)=>corners.ContainsKey(key);
        public Vector3 Corner(Vector3Int key)=>corners.TryGetValue(key,out var p)?p:(Vector3)key;
        public bool CanAdd(Vector3Int p)
        {
            if(cells.Count>=MaxBlocks||!InRange(p)||cells.Contains(p))return false;
            foreach(var step in Steps)if(cells.Contains(p+step))return true;
            return false;
        }
        public bool Add(Vector3Int p)=>ApplyEdit(p,false);
        public bool Add(Vector3Int p,TileFamily family)
        {
            if(!TryEdit(p,false,out var next,family))return false;
            Adopt(next);return true;
        }
        public bool Remove(Vector3Int p)=>ApplyEdit(p,true);
        bool ApplyEdit(Vector3Int p,bool remove)
        {
            if(!TryEdit(p,remove,out var next))return false;
            Adopt(next);return true;
        }
        void Adopt(BlockConstruction next)
        {
            cells=next.cells;families=next.families;corners=next.corners;ChangedCells=next.ChangedCells;
            LastMaxMovement=next.LastMaxMovement;LastSafetyScale=next.LastSafetyScale;LastError=null;
            guide=next.guide;shapes=next.shapes;
        }
        // Corners never move while their cell exists, so a cell's shape is fixed until it is removed.
        public ShapeInfo Shape(Vector3Int cell)
        {
            if(!cells.Contains(cell))return default;
            shapes??=new Dictionary<Vector3Int,ShapeInfo>();
            if(!shapes.TryGetValue(cell,out var s)){s=ShapeOf(cell);shapes.Add(cell,s);}
            return s;
        }
        public const float FoldDegrees=40;
        ShapeInfo ShapeOf(Vector3Int cell)
        {
            var p=new Vector3[8];var center=Vector3.zero;
            for(int i=0;i<8;i++){p[i]=Corner(cell+CornerOffset(i))*TileBuilder.Cell;center+=p[i]*0.125f;}
            var n=new Vector3[6];
            for(int f=0;f<6;f++)
            {
                Vector3 a=p[FaceCorners[f,0]],b=p[FaceCorners[f,1]],c=p[FaceCorners[f,2]],d=p[FaceCorners[f,3]];
                n[f]=Vector3.Cross(c-a,d-b).normalized;
                if(Vector3.Dot(n[f],(a+b+c+d)*0.25f-center)<0)n[f]=-n[f];
            }
            // Adjacent faces whose normals nearly agree have folded into one roof (on spheres: under 35° or over 50°).
            int roof=0,merged=0,axes=0;float limit=Mathf.Cos(FoldDegrees*Mathf.Deg2Rad);
            for(int f=0;f<6;f++)for(int g=f+1;g<6;g++)if(f>>1!=g>>1&&Vector3.Dot(n[f],n[g])>limit)roof|=1<<f|1<<g;
            for(int f=0;f<6;f++)if((roof>>f&1)!=0){merged++;axes|=1<<(f>>1);}
            var kind=merged>=3?CellShape.Apex:merged==2?CellShape.Wedge:CellShape.Box;
            int spine=kind==CellShape.Wedge?(axes==3?2:axes==5?1:0):-1;
            float min=float.MaxValue,max=0;
            for(int i=0;i<8;i++)for(int a=0;a<3;a++)if((i>>a&1)==0)
            {float e=(p[i|1<<a]-p[i]).magnitude;min=Mathf.Min(min,e);max=Mathf.Max(max,e);}
            return new ShapeInfo(kind,spine,(byte)roof,min,max);
        }
        public CellExposure Exposure(Vector3Int cell,out bool gable)
        {
            gable=false;var s=Shape(cell);
            if(!cells.Contains(cell)||s.kind==CellShape.Box)return CellExposure.Box;
            int roofs=0,roofOpen=0,slopeOpen=0;
            for(int f=0;f<6;f++)
            {
                bool open=!Solid(cell+Steps[f]);
                if((s.roof>>f&1)!=0){roofs++;if(open)roofOpen++;}
                else if(f>>1==s.spine)gable|=open;
                else if(open)slopeOpen++;
            }
            bool wedge=s.kind==CellShape.Wedge;
            if(roofOpen==roofs)return wedge?CellExposure.Ridge:CellExposure.Crown;
            if(roofOpen>0)return CellExposure.Half;
            if(slopeOpen>0)return wedge?CellExposure.Keel:CellExposure.Spike;
            return CellExposure.Buried;
        }
        // Preview and commit use the same result. Hovering never changes the live construction. Every family fits
        // every cell: parts too big for a narrow cell take a smaller size class (TileBuilder.SizeClasses).
        public bool TryEdit(Vector3Int edit,bool remove,out BlockConstruction next,TileFamily family=TileFamily.Hull)
        {
            next=null;LastError=null;
            if(!Enum.IsDefined(typeof(TileFamily),family)){LastError="Unknown tile family.";return false;}
            if(remove?(Count<=1||!Contains(edit)):!CanAdd(edit)){LastError="No available block placement.";return false;}
            var candidate=new BlockConstruction{cells=new HashSet<Vector3Int>(cells),families=new Dictionary<Vector3Int,TileFamily>(families),corners=new Dictionary<Vector3Int,Vector3>(corners),guide=guide,
                shapes=shapes==null?null:new Dictionary<Vector3Int,ShapeInfo>(shapes)};
            candidate.ChangedCells=new HashSet<Vector3Int>{edit};foreach(var step in Steps)candidate.ChangedCells.Add(edit+step);
            if(remove){candidate.cells.Remove(edit);candidate.families.Remove(edit);candidate.PruneCorners(edit);candidate.shapes?.Remove(edit);}
            else
            {
                candidate.cells.Add(edit);
                candidate.families[edit]=family;
                for(int i=0;i<8;i++)
                {
                    var key=edit+CornerOffset(i);
                    if(!candidate.corners.ContainsKey(key))candidate.corners.Add(key,guide.Map(key));
                }
                if(!ValidGeometry(candidate.cells,candidate.corners,new[]{edit}))
                {LastError="This gap cannot be filled safely on the sphere grid.";return false;}
            }
            next=candidate;return true;
        }
        // A face's size in metres: its mean extent along each of the two lattice axes it spans.
        public Vector2 FaceSize(Vector3Int cell,int face)
        {
            Vector3 P(int i)=>Corner(cell+CornerOffset(FaceCorners[face,i]))*TileBuilder.Cell;
            Vector3 a=P(0),b=P(1),c=P(2),d=P(3);
            return new Vector2(((b-a).magnitude+(c-d).magnitude)*0.5f,((d-a).magnitude+(c-b).magnitude)*0.5f);
        }
        // "Box 4.6 × 4.8 m", "Wedge ridge 5.3 × 2.7 m": what a cell is and the size of one of its faces.
        public string Describe(Vector3Int cell,int face)
        {
            var s=Shape(cell);var e=Exposure(cell,out _);var size=FaceSize(cell,face);
            string kind=s.kind==CellShape.Box?(s.Small?"Small box":"Box"):$"{s.kind} {e.ToString().ToLowerInvariant()}";
            return $"{kind} {size.x:0.0} × {size.y:0.0} m";
        }
        // Compatibility for the previous solver's callers. There is no body refit.
        public bool TryRelax(out BlockConstruction next){next=null;return false;}
        public BlockConstruction SettleForEdit()=>this;
        void PruneCorners(Vector3Int removed)
        {
            for(int i=0;i<8;i++)
            {
                var key=removed+CornerOffset(i);bool used=false;
                for(int j=0;j<8;j++)if(cells.Contains(key-CornerOffset(j))){used=true;break;}
                if(!used)corners.Remove(key);
            }
        }
        static BlockGuide FitGuide(IEnumerable<Vector3Int> input)
        {
            var all=new List<Vector3Int>(input);
            var bounds=new Bounds((Vector3)all[0]+Vector3.one*0.5f,Vector3.one);
            foreach(var cell in all){bounds.Encapsulate(cell);bounds.Encapsulate((Vector3)cell+Vector3.one);}
            return new BlockGuide{shape=BlockShape.Rounded,strength=1,center=bounds.center,extents=Vector3.Max(bounds.extents,Vector3.one*0.5f)};
        }
        // Without a guide, the sphere is fitted to the cells' bounds; pass SeedGuide to match a build grown from the seed.
        public static BlockConstruction FromCells(IEnumerable<Vector3Int> input,BlockGuide? fixedGuide=null)
        {
            var all=new HashSet<Vector3Int>(input);
            if(all.Count==0||all.Count>MaxBlocks)throw new ArgumentException("Unsupported block count.");
            var model=new BlockConstruction{cells=all,guide=fixedGuide??FitGuide(all)};model.corners.Clear();
            foreach(var cell in all)
            {
                if(!InRange(cell))throw new ArgumentException("Block outside build bounds.");
                for(int i=0;i<8;i++){var key=cell+CornerOffset(i);model.corners[key]=model.guide.Map(key);}
            }
            if(!model.GeometryIsValid())throw new ArgumentException("The sphere guide produced invalid cells.");
            model.ChangedCells=new HashSet<Vector3Int>(all);return model;
        }
        public static BlockConstruction FromFamilies(IReadOnlyDictionary<Vector3Int,TileFamily> input,BlockGuide? fixedGuide=null)
        {
            var model=FromCells(input.Keys,fixedGuide);
            foreach(var pair in input)
            {
                if(!Enum.IsDefined(typeof(TileFamily),pair.Value))throw new ArgumentException("Unknown tile family.");
                model.families[pair.Key]=pair.Value;
            }
            return model;
        }
        public Vector3 Point(Vector3Int cell,Vector3 uvw)
        {
            Vector3 p=Vector3.zero;
            for(int i=0;i<8;i++)
            {
                var o=CornerOffset(i);float w=(o.x==0?1-uvw.x:uvw.x)*(o.y==0?1-uvw.y:uvw.y)*(o.z==0?1-uvw.z:uvw.z);
                p+=Corner(cell+o)*w;
            }
            return p;
        }
        public Vector3 FacePoint(Vector3Int cell,int face,float u,float v)
        {
            Vector3 a=Corner(cell+CornerOffset(FaceCorners[face,0])),b=Corner(cell+CornerOffset(FaceCorners[face,1]));
            Vector3 c=Corner(cell+CornerOffset(FaceCorners[face,2])),d=Corner(cell+CornerOffset(FaceCorners[face,3]));
            return Vector3.Lerp(Vector3.Lerp(a,b,u),Vector3.Lerp(d,c,u),v);
        }
        public const float MinScaledJacobian=0.001f;
        public bool GeometryIsValid()=>ValidGeometry(cells,corners,cells);
        static bool ValidGeometry(HashSet<Vector3Int> occupancy,Dictionary<Vector3Int,Vector3> points,IEnumerable<Vector3Int> check)
        {
            var p=new Vector3[8];
            foreach(var cell in check)
            {
                if(!occupancy.Contains(cell))continue;
                for(int i=0;i<8;i++)if(!points.TryGetValue(cell+CornerOffset(i),out p[i])||!Finite(p[i]))return false;
                for(int z=0;z<3;z++)for(int y=0;y<3;y++)for(int x=0;x<3;x++)
                {
                    float u=x*0.5f,v=y*0.5f,w=z*0.5f;
                    Vector3 dx=Vector3.Lerp(Vector3.Lerp(p[1]-p[0],p[3]-p[2],v),Vector3.Lerp(p[5]-p[4],p[7]-p[6],v),w);
                    Vector3 dy=Vector3.Lerp(Vector3.Lerp(p[2]-p[0],p[3]-p[1],u),Vector3.Lerp(p[6]-p[4],p[7]-p[5],u),w);
                    Vector3 dz=Vector3.Lerp(Vector3.Lerp(p[4]-p[0],p[5]-p[1],u),Vector3.Lerp(p[6]-p[2],p[7]-p[3],u),v);
                    float determinant=Vector3.Dot(dx,Vector3.Cross(dy,dz));
                    // A wedge's or apex's folded roof flattens as the shell grows, so its outer corner's Jacobian
                    // falls towards zero (scaled by the edges: 0.0033 at the build limit). Reject foldovers and
                    // collapse, not flatness: the scaled Jacobian stays positive and no edge shrinks under 0.1 cell.
                    float edges=dx.magnitude*dy.magnitude*dz.magnitude;
                    if(!float.IsFinite(determinant)||determinant<MinScaledJacobian*edges||
                       Mathf.Min(dx.sqrMagnitude,Mathf.Min(dy.sqrMagnitude,dz.sqrMagnitude))<0.01f)return false;
                }
            }
            return true;
        }
        static bool Finite(Vector3 p)=>float.IsFinite(p.x)&&float.IsFinite(p.y)&&float.IsFinite(p.z);
        public BlockSave Capture()
        {
            var blocks=new List<Vector3Int>(cells);blocks.Sort(Compare);
            var keys=new List<Vector3Int>(corners.Keys);keys.Sort(Compare);
            var vertices=new BlockCorner[keys.Count];for(int i=0;i<keys.Count;i++)vertices[i]=new BlockCorner{key=keys[i],position=corners[keys[i]]};
            var palette=new TileFamily[blocks.Count];for(int i=0;i<blocks.Count;i++)palette[i]=Family(blocks[i]);
            return new BlockSave{guide=guide,cells=blocks.ToArray(),families=palette,corners=vertices};
        }
        public void Restore(BlockSave save)
        {
            if(save==null||(save.version<1||save.version>6)||save.cells==null||save.cells.Length==0||save.cells.Length>MaxBlocks)
                throw new ArgumentException("Unsupported or empty construction file.");
            if((save.version==3||save.version==4)&&(save.types==null||save.types.Length!=save.cells.Length))throw new ArgumentException("Incomplete block types.");
            if(save.version>=6&&(save.families==null||save.families.Length!=save.cells.Length))throw new ArgumentException("Incomplete tile families.");
            var next=new BlockConstruction();next.cells.Clear();next.corners.Clear();
            var needed=new HashSet<Vector3Int>();
            foreach(var cell in save.cells)
            {
                if(!InRange(cell)||!next.cells.Add(cell))throw new ArgumentException("Invalid or duplicate block coordinate.");
                for(int i=0;i<8;i++)needed.Add(cell+CornerOffset(i));
            }
            if(save.version>=6)for(int i=0;i<save.cells.Length;i++)
            {
                if(!Enum.IsDefined(typeof(TileFamily),save.families[i]))throw new ArgumentException("Unknown tile family.");
                next.families[save.cells[i]]=save.families[i];
            }
            // Read old typed saves without changing their stored geometry. New
            // placements and new saves use only the sphere mapping.
            if(save.version==3||save.version==4)
                foreach(var type in save.types)
                    if(type!=BlockType.Round&&type!=BlockType.Square)throw new ArgumentException("Unknown legacy block type.");
            var fitCells=new List<Vector3Int>();
            for(int i=0;i<save.cells.Length;i++)
                if(save.version!=3||save.types[i]==BlockType.Round)fitCells.Add(save.cells[i]);
            if(fitCells.Count==0)fitCells.AddRange(save.cells);
            next.guide=save.version==1||save.version>=4?save.guide:FitGuide(fitCells);
            if(save.version==1||save.version>=4)
            {
                var g=save.guide;
                if(!Enum.IsDefined(typeof(BlockShape),g.shape)||!Finite(g.center)||!Finite(g.extents)||!float.IsFinite(g.strength)||g.strength<0||g.strength>1||
                    g.extents.x<0.5f||g.extents.y<0.5f||g.extents.z<0.5f||g.extents.x>CoordinateLimit||g.extents.y>CoordinateLimit||g.extents.z>CoordinateLimit||
                    Mathf.Abs(g.center.x)>CoordinateLimit||Mathf.Abs(g.center.y)>CoordinateLimit||Mathf.Abs(g.center.z)>CoordinateLimit)
                    throw new ArgumentException("Invalid legacy shape guide.");
            }
            if(save.version==1)foreach(var key in needed)next.corners[key]=next.guide.Map(key);
            else
            {
                if(save.corners==null||save.corners.Length!=needed.Count)throw new ArgumentException("Incomplete shared corner data.");
                foreach(var vertex in save.corners)
                {
                    if(!needed.Contains(vertex.key)||!Finite(vertex.position)||vertex.position.magnitude>512||next.corners.ContainsKey(vertex.key))
                        throw new ArgumentException("Invalid or duplicate shared corner.");
                    next.corners.Add(vertex.key,vertex.position);
                }
            }
            if(!next.GeometryIsValid())throw new ArgumentException("Construction contains folded or collapsed cells.");
            Adopt(next);
        }
    }
}
