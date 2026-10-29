using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // The core families of the modular settlement (doc/modular-settlement-tile-design.md), plus landing pads for
    // small craft, conduits for big pipe runs and trusses for open space frames. Saves store the number: append only.
    public enum TileFamily { Habitat, Industrial, Power, LifeSupport, Storage, Defense, Command, Corridor, Pad, Conduit, Hull, Truss }

    // Settlement tiles, massing level. A tile is a 5 m cube of armour; every exposed face gets the station-wide
    // greeble relief (GreebleField) plus its family's large form. Faces against solid neighbours are not built, so
    // cubes merge with no seams. Forms follow the face and the tile's own axes, never a world up. Window rows follow the
    // tile's axis in two 2.5 m bands and line up across the station; inside, a Habitat or Corridor is one room with no up
    // (TileBuilder.Interior.cs) whose openings are those rows.
    //   Hull        the relief; small windows and round EVA hatches on some faces
    //   Habitat     two rows of windows under angled visors on faces across its axis; rows join along neighbours
    //   Industrial  a terraced pit with a gantry and a control tower on the main face; asymmetric masses elsewhere
    //   Power       regular banks of thick fins; rows run on across neighbours
    //   LifeSupport half-cylinder vaults with rings; neighbours join them into long vaults
    //   Storage     a deep bay between sloped lips on the main face, merging into one hangar slot; ribbed elsewhere
    //   Defense     sloped glacis on every face; a turret with long barrels on the main face
    //   Command     a stepped tower with a sloped bridge window, sensor globes and a mast; an upper window row elsewhere
    //   Corridor    an open ribbed tube with portholes; a docking collar and hatch where it ends in space
    //   Pad         a round landing pad on struts with an airlock hatch in its centre; 2 x 2 pads make one big pad
    //   Conduit     a bundle of big pipes on saddles, turning on elbows and plugging into the tiles it reaches
    //   Truss       an open space frame of heavy box chords and cast nodes; solid faces under it are bearing plates
    // Towers and turret mounts get angled buttresses where they stand on a surface.
    public sealed partial class TileBuilder
    {
        public const float Cell=5f,Half=2.5f;
        // Triangles a tile may have (doc/curved-grid-tiles-plan.md): near, with its carving, and far, past ~110 m.
        public const int NearBudget=8000,FarBudget=2500;
        // Flat armour is carved into plates (TileMesh.Carving), in courses along the tile's axis; round forms and the
        // walls under the relief stay smooth.
        static readonly Finish Armor=new Finish(TileSlot.Paint,TilePattern.Armour,Color.white,0f);
        static readonly Finish Smooth=new Finish(TileSlot.Paint,TilePattern.Plain,Color.white,0f);
        static readonly Finish Deep=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.62f,0.62f,0.62f),0f);
        static readonly Finish Relief=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.97f,0.97f,0.97f),0f);
        // Deck window rows along the tile's axis: sill to head, the same on every family so rows meet.
        const float LowSill=-1.6f,LowHead=-0.65f,HighSill=0.9f,HighHead=1.85f;
        // A wall gets two decks of windows only if it is this tall in metres; otherwise one row at mid height.
        const float TwoDecks=4.2f;
        // Equipment size classes (L, M, S) as fractions of the authored size.
        public static readonly float[] SizeClasses={1f,0.78f,0.6f};

        readonly TileStructure s;
        readonly CurvedTileMesh curved;
        readonly Dictionary<(Vector3Int,Vector3Int),List<Rect>> fieldHoles=new Dictionary<(Vector3Int,Vector3Int),List<Rect>>();
        public TileBuilder(TileStructure structure,CurvedTileMesh curved=null){s=structure;this.curved=curved;}

        static uint Hash(Vector3Int c){uint h=(uint)(c.x*73856093)^(uint)(c.y*19349663)^(uint)(c.z*83492791);h^=h>>16;h*=0x7feb352dU;h^=h>>15;h*=0x846ca68bU;h^=h>>16;return h;}
        static float Rand(uint seed,int k){uint h=seed^(uint)(k*0x9E3779B1U);h^=h>>16;h*=0x7feb352dU;h^=h>>15;h*=0x846ca68bU;h^=h>>16;return (h&0xFFFFFF)/16777216f;}
        static int AxisOf(Vector3Int d)=>d.x!=0?0:d.y!=0?1:2;

        readonly struct Equipment:System.IDisposable
        {
            readonly TileMesh m;readonly TileMesh.FixedPart part;
            public Equipment(TileMesh m,TileMesh.FixedPart part){this.m=m;this.part=part;}
            public void Dispose(){m.Pop();part.Dispose();}
        }

        // One face being built, in its own frame: x right and y up as seen from outside, z into the tile.
        sealed class Face
        {
            public TileMesh m;public TileStructure s;public Vector3Int cell,n,x,y;public TilePlacement p;public bool primary,axial;public uint seed;
            public Vector2 metric=Vector2.one;public CurvedTileMesh curved;
            // Station point of (u,v) on this face.
            public Vector3 At(float u,float v)=>TileStructure.Center(cell)+(Vector3)n*Half+(Vector3)x*u+(Vector3)y*v;
            // A free-standing part built at true size round (u,v); dispose the scope when the part is done.
            public TileMesh.FixedPart Fixed(string name,float u,float v)=>m.BeginFixed(name,At(u,v));
            // Metres per station metre along x and y round (u,v), for parts set into the wall; 1 on flat stages.
            public Vector2 Local(float u,float v)=>curved==null?Vector2.one:curved.Room(At(u,v)-(Vector3)n*0.01f,x,y)/Cell;
            // Equipment comes in three true sizes: the largest whose span (metres along x and y) fits the face round
            // (u,v), less a margin. cells is 2 for a 2 x 2 block. Flat stages always take the largest.
            public float Fit(float u,float v,Vector2 span,float margin=0.6f,int cells=1)
            {
                if(curved==null)return 1;
                var room=curved.Room(At(u,v)-(Vector3)n*0.01f,x,y)*cells;
                foreach(float k in SizeClasses)if(span.x*k<=room.x-margin&&span.y*k<=room.y-margin)return k;
                return SizeClasses[SizeClasses.Length-1];
            }
            // The mean edge length in metres of cell q's face on this side, along the lattice step d lying in it; 5 on
            // flat stages.
            public float Span(Vector3Int q,Vector3Int d)
            {
                if(curved==null)return Cell;
                int a=AxisOf(n),u=AxisOf(d),v=3-a-u;
                Vector3 C(int i,int j){var k=q;k[a]+=n[a]>0?1:0;k[u]+=i;k[v]+=j;return curved.Corner(k);}
                return ((C(1,0)-C(0,0)).magnitude+(C(1,1)-C(0,1)).magnitude)*0.5f;
            }
            // Equipment: a free-standing part at true size in the largest class that fits, scaled about its foot
            // (pivot, in this frame). The part's name carries its class (L, M or S).
            public Equipment Equip(string name,float u,float v,Vector3 pivot,Vector2 span,float margin=0.6f,int cells=1)
            {
                float k=Fit(u,v,span,margin,cells);
                var part=Fixed(name+(k>0.9f?" L":k>0.7f?" M":" S"),u,v);
                m.PushScale(pivot,k);
                return new Equipment(m,part);
            }
            public readonly List<Rect> recess=new List<Rect>(),bare=new List<Rect>();
            public float Rand(int k)=>TileBuilder.Rand(seed^(uint)((n.x+2)*7+(n.y+2)*31+(n.z+2)*131),k);
            public bool Cont(Vector3Int step)=>s.Continues(cell,p,n,step);
            public void Box(in Finish f,float u0,float v0,float u1,float v1,float o0,float o1,float chamfer=0.04f)=>
                m.Box(f,new Vector3((u0+u1)*0.5f,(v0+v1)*0.5f,-(o0+o1)*0.5f),new Vector3(u1-u0,v1-v0,o1-o0),chamfer);
            public static Vector3 P(float u,float v,float o)=>new Vector3(u,v,-o);
            // A pane of glass on any plane in this frame, facing outward, its height along up.
            public void Pane(Vector3 center,Vector3 outward,Vector3 up,float w,float h)
            {
                m.Push(center,Quaternion.LookRotation(-outward,up));m.Glass(new Rect(-w*0.5f,-h*0.5f,w,h),-0.015f);m.Pop();
            }
            // Whether this main face is one of a 2 x 2 block of the same tile, all showing this face. Blocks pair
            // cells on even station coordinates, so every tile of a block agrees on it. c is the block's centre in
            // this frame; the anchor tile (even on both axes) builds the pieces the block shares.
            public bool Block2(out Vector2 c,out bool anchor)
            {
                int ax=AxisOf(x),ay=AxisOf(y);
                int stepX=(cell[ax]&1)==0?1:-1,stepY=(cell[ay]&1)==0?1:-1;
                var dx=Vector3Int.zero;dx[ax]=stepX;var dy=Vector3Int.zero;dy[ay]=stepY;
                c=new Vector2(stepX*x[ax]*Half,stepY*y[ay]*Half);anchor=stepX>0&&stepY>0;
                var place=p;var face=n;var st=s;
                bool Same(Vector3Int q)=>st.TryGet(q,out var o)&&o.SameAs(place)&&!st.Covered(q+face)&&st.Primary(q,o)==face&&!st.special.ContainsKey(q);
                return primary&&!st.special.ContainsKey(cell)&&Same(cell+dx)&&Same(cell+dy)&&Same(cell+dx+dy);
            }
        }

        public TileMesh Build(Vector3Int cell)
        {
            var m=new TileMesh{stationOffset=TileStructure.Center(cell),cellHalf=Half,retainWarpData=s.automatic,nearBudget=NearBudget,farBudget=FarBudget};
            if(!s.TryGet(cell,out var p))return m;
            m.seed=Rand(Hash(cell),0);
            // armour courses stack along the tile's axis, so they wrap round a tower
            m.courseAxis=AxisOf(p.axis);m.courseSide=AxisOf(p.side);
            m.overlay=TileLook.FamilyColor(p.family);
            if(p.family==TileFamily.Corridor){Corridor(m,cell,p);return m;}
            if(p.family==TileFamily.Conduit){Conduit(m,cell,p);return m;}
            if(p.family==TileFamily.Truss){Truss(m,cell,p);return m;}
            if(curved!=null&&s.special.TryGetValue(cell,out var shape)){Special(m,cell,p,shape);return m;}
            var primary=s.Primary(cell,p);
            var place=p;
            Faces(m,cell,(i,n)=>{Face face=default;m.Detail(t=>{var built=Forms(t,cell,place,n,primary);if(t==m)face=built;});return face;});
            return m;
        }

        // Every open face of a cell: forms(i, n) builds face i's forms under its surface frame and leaves the face's
        // frame pushed; then the wall round their recesses and the station relief clear of the face's holes.
        void Faces(TileMesh m,Vector3Int cell,System.Func<int,Vector3Int,Face> forms)
        {
            var c=TileStructure.Center(cell);
            for(int i=0;i<6;i++)
            {
                var n=TileStructure.Directions[i];
                if(s.Solid(cell+n))continue;
                m.surfaceFrame=new Vector4(n.x,n.y,n.z,Vector3.Dot(n,c)+Half);
                var face=forms(i,n);
                m.Wall(Smooth,Whole,face.recess);
                m.Pop();
                int a=AxisOf(n),ua=(a+1)%3,va=(a+2)%3;
                var holes=FaceHoles(cell,n,face);
                var gf=new GreebleField.Face{axis=a,sign=n[a],plane=c[a]+n[a]*Half,window=new Rect(c[ua]-Half,c[va]-Half,Cell,Cell),
                    holes=holes,origin=c,continues=(u,v)=>ReliefContinues(cell,n,c,ua,va,u,v)};
                GreebleField.Build(m,gf,Relief);
            }
        }

        // Relief carries on past this face's edge if the neighbour there shows the same plane with relief at (u,v).
        bool ReliefContinues(Vector3Int cell,Vector3Int n,Vector3 c,int ua,int va,float u,float v)
        {
            var d=Vector3Int.zero;
            if(u<c[ua]-Half)d[ua]=-1;else if(u>c[ua]+Half)d[ua]=1;else if(v<c[va]-Half)d[va]=-1;else d[va]=1;
            var next=cell+d;
            if(!s.Solid(next)||s.Solid(next+n))return false;
            foreach(var r in Holes(next,n))if(u>r.xMin&&u<r.xMax&&v>r.yMin&&v<r.yMax)return false;
            return true;
        }
        List<Rect> Holes(Vector3Int cell,Vector3Int n)=>fieldHoles.TryGetValue((cell,n),out var list)?list:FaceHoles(cell,n,null);
        // Station rects on face n of cell the relief stays clear of, kept for the neighbours: a wedge's or apex's own
        // (SpecialHoles), else those the family forms report. face: the forms built there, or null to run them dry.
        List<Rect> FaceHoles(Vector3Int cell,Vector3Int n,Face face)
        {
            List<Rect> list;
            if(curved!=null&&s.special.TryGetValue(cell,out var shape))list=SpecialHoles(cell,n,shape);
            else
            {
                if(face==null){s.TryGet(cell,out var p);face=Forms(new TileMesh{dry=true},cell,p,n,s.Primary(cell,p));face.m.Pop();}
                int a=AxisOf(n);list=StationRects(face,TileStructure.Center(cell),(a+1)%3,(a+2)%3);
            }
            return fieldHoles[(cell,n)]=list;
        }
        static List<Rect> StationRects(Face f,Vector3 c,int ua,int va)
        {
            var list=new List<Rect>();
            Vector3 o=c+(Vector3)f.n*Half,x=f.x,y=f.y;
            foreach(var group in new[]{f.recess,f.bare})foreach(var r in group)
            {
                Vector3 a=o+x*r.xMin+y*r.yMin,b=o+x*r.xMax+y*r.yMax;
                list.Add(Rect.MinMaxRect(Mathf.Min(a[ua],b[ua]),Mathf.Min(a[va],b[va]),Mathf.Max(a[ua],b[ua]),Mathf.Max(a[va],b[va])));
            }
            return list;
        }

        // Family form on one exposed face. Leaves the face frame pushed for the caller's wall.
        Face Forms(TileMesh m,Vector3Int cell,in TilePlacement p,Vector3Int n,Vector3Int primary)
        {
            bool axial=n==p.axis||n==-p.axis;
            var f=new Face{m=m,s=s,cell=cell,p=p,n=n,y=axial?p.side:p.axis,primary=n==primary,axial=axial,seed=Hash(cell),curved=curved};
            f.x=TileStructure.Cross(f.y,-n);
            if(curved!=null)
            {
                // A whole coplanar frontage uses one metric, so joined rows have identical sill/head positions
                // and pane pitch on both sides of a cell boundary even where the sphere compresses the wall.
                var at=TileStructure.Center(p.anchor);
                at+=(Vector3)n*(Vector3.Dot(TileStructure.Center(cell)-at,n)+Half);
                f.metric=new Vector2(Mathf.Max(0.3f,curved.Scale(at,f.x)),Mathf.Max(0.3f,curved.Scale(at,f.y)));
            }
            m.Push((Vector3)n*Half,Quaternion.LookRotation(-(Vector3)n,(Vector3)f.y));
            // A corridor or conduit arriving at this face takes it over: a socket for the corridor's collar, a
            // manifold for the pipes.
            if(s.TryGet(cell+n,out var nb))
            {
                if(nb.family==TileFamily.Corridor&&s.CorridorLinks(cell+n,nb).Contains(-n)){Socket(f);return f;}
                if(nb.family==TileFamily.Conduit&&ConduitLinks(cell+n,nb).Contains(-n)){Port(f,nb);return f;}
                if(nb.family==TileFamily.Truss){Bearing(f);return f;}
            }
            switch(p.family)
            {
                case TileFamily.Habitat:if(!axial)Habitat(f);break;
                case TileFamily.Industrial:Industrial(f);break;
                case TileFamily.Power:Power(f);break;
                case TileFamily.LifeSupport:LifeSupport(f);break;
                case TileFamily.Storage:Storage(f);break;
                case TileFamily.Defense:Defense(f);break;
                case TileFamily.Command:Command(f);break;
                case TileFamily.Pad:Pad(f);break;
                case TileFamily.Hull:Hull(f);break;
            }
            bool tower=p.family==TileFamily.Defense||p.family==TileFamily.Command||(p.family==TileFamily.Hull&&Column(cell,p));
            if(tower&&!axial)Buttresses(f);
            if(p.family==TileFamily.Hull&&!tower)Fillets(f);
            return f;
        }

        static readonly Rect Whole=new Rect(-Half,-Half,Cell,Cell);

        // ---------------------------------------------------------------- shared forms

        // Eye-level windows on one deck, set 0.4 m into the armour between deep mullions on the station's 1.25 m
        // pitch, so rows on neighbours line up; an angled visor over them and a ledge under. Where the row doesn't
        // carry on into the same family it stops short of the edge, leaving a pier.
        // whole: the only row on the wall, free to use either half of it.
        static void WindowRow(Face f,float v0,float v1,bool visor=true,bool whole=false)
        {
            float mid=(v0+v1)*0.5f,h=(v1-v0)/f.metric.y,ky=1/f.metric.y;
            v0=Mathf.Max(whole||mid<0?-Half+0.1f:0.1f,mid-h*0.5f);
            v1=Mathf.Min(!whole&&mid<0?-0.1f:Half-0.1f,mid+h*0.5f);
            bool l=f.Cont(-f.x),r=f.Cont(f.x);
            float u0=l?-Half:-Half+0.6f/f.metric.x,u1=r?Half:Half-0.6f/f.metric.x;
            const float depth=0.4f;float mull=0.11f/f.metric.x;
            int panes=Mathf.Max(1,Mathf.CeilToInt(Cell*f.metric.x/1.25f));float pitch=Cell/panes;
            var band=Rect.MinMaxRect(u0,v0,u1,v1);
            f.recess.Add(band);
            f.m.Reveal(Armor,band,depth,(l?0:1)|(r?0:2)|4|8);
            for(int k=0;k<panes;k++)
            {
                float a=Mathf.Max(-Half+k*pitch+mull,u0),b=Mathf.Min(-Half+(k+1)*pitch-mull,u1);
                if(b-a>0.2f)f.m.Glass(Rect.MinMaxRect(a,v0,b,v1),depth);
            }
            for(int k=0;k<=panes;k++)
            {
                float center=-Half+k*pitch,a=Mathf.Max(center-mull,u0),b=Mathf.Min(center+mull,u1);
                if(b-a>0.01f)f.Box(Armor,a,v0,b,v1,-depth,0,0);
            }
            float e0=l?-Half:Mathf.Max(-Half,u0-0.2f),e1=r?Half:Mathf.Min(Half,u1+0.2f);
            f.Box(Armor,e0,v0-0.28f*ky,e1,v0,0,0.22f,0);
            float top=v1;
            if(visor)
            {
                var g=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    float u=(i&1)!=0?e1:e0;bool tip=(i&4)!=0,high=(i&2)!=0;
                    g[i]=Face.P(u,tip?(high?v1+0.16f*ky:v1+0.04f*ky):(high?v1+0.38f*ky:v1),tip?0.45f:0);
                }
                f.m.Hexa(Armor,g);top=v1+0.38f*ky;
            }
            f.bare.Add(Rect.MinMaxRect(e0,v0-0.28f*ky,e1,top));
        }

        // A round hatch: a raised collar round a slightly proud door disc, at (u,v) on the face.
        static void Hatch(Face f,float u,float v,float r)
        {
            using(f.Fixed("hatch",u,v))HatchGeometry(f,u,v,r);
        }
        static void HatchGeometry(Face f,float u,float v,float r)
        {
            var o=new Vector3(u,v,0);
            f.m.Lathe(Smooth,o,Vector3.back,new[]{new Vector2(r+0.22f,0),new Vector2(r+0.22f,0.32f),new Vector2(r,0.32f),new Vector2(r,0)},24,false);
            f.m.Lathe(Smooth,o,Vector3.back,new[]{new Vector2(0,0),new Vector2(r,0),new Vector2(r,0.12f),new Vector2(0,0.12f)},24,false);
            f.Box(Armor,u-0.06f,v-r*0.7f,u+0.06f,v+r*0.7f,0.12f,0.2f,0.01f);
            f.bare.Add(new Rect(u-r-0.3f,v-r-0.3f,2*r+0.6f,2*r+0.6f));
        }

        // Angled buttress fins where this face meets a surface it stands on (a concave edge).
        void Buttresses(Face f)
        {
            foreach(var (w,a,step) in Edges(f))
            {
                if(!s.Solid(f.cell+f.n+step))continue;
                // w points from the edge towards the face centre, a runs along the edge
                foreach(float pos in new[]{-1.3f,1.3f})
                {
                    var g=new Vector3[8];
                    for(int i=0;i<8;i++)
                    {
                        float side=(i&1)!=0?0.17f:-0.17f;
                        Vector2 e=-w*Half+a*(pos+side);
                        bool up=(i&2)!=0,outward=(i&4)!=0;
                        var q=up?e+w*2.3f:e;
                        g[i]=Face.P(q.x,q.y,outward&&!up?1.5f:0);
                    }
                    f.m.Hexa(Armor,g);
                }
            }
        }
        // The four edges of a face: w points from the edge to the face centre, a runs along it, step crosses it.
        static (Vector2 w,Vector2 a,Vector3Int step)[] Edges(Face f)=>
            new[]{(Vector2.up,Vector2.right,-f.y),(Vector2.down,Vector2.right,f.y),(Vector2.right,Vector2.up,-f.x),(Vector2.left,Vector2.up,f.x)};

        // A sloped fillet along each concave edge where this face rises from a surface, so walls are battered
        // instead of meeting the surface in a hard step. Neighbours with the same edge continue it.
        void Fillets(Face f)
        {
            const float L=1.3f;
            foreach(var (w,a,step) in Edges(f))
            {
                if(!s.Solid(f.cell+f.n+step))continue;
                var g=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    float along=(i&1)!=0?Half:-Half;bool up=(i&2)!=0,outward=(i&4)!=0;
                    Vector2 e=-w*Half+a*along;
                    var q=up?e+w*L:e;
                    g[i]=Face.P(q.x,q.y,outward&&!up?L:0);
                }
                f.m.Hexa(Armor,g);
                Vector2 c0=-w*Half-a*Half,c1=-w*Half+w*L+a*Half;
                f.bare.Add(Rect.MinMaxRect(Mathf.Min(c0.x,c1.x),Mathf.Min(c0.y,c1.y),Mathf.Max(c0.x,c1.x),Mathf.Max(c0.y,c1.y)));
            }
        }

        // Where a corridor runs into this face: an octagonal socket ring its collar plugs into.
        // True size and matched to the corridor's collar, which reads the same face's room.
        void Socket(Face f)
        {
            float collar=CollarRadius(FaceRoom(f.cell,f.n));
            var outer=Octagon(collar+0.3f);var inner=Octagon(collar-0.03f);
            using(f.Fixed("socket",0,0))
            {
                f.m.Prism(Armor,outer,Vector3.zero,Vector3.right,Vector3.up,Vector3.back,0,0.5f,false);
                f.m.Annulus(Armor,inner,outer,Vector3.zero,Vector3.right,Vector3.up,Vector3.back,0.5f,1);
            }
            f.bare.Add(Whole);
            // Open into a room: the tube runs on through the wall. The hole, a little over the tube's true size, lies
            // inside the collar.
            if(Opens(f.cell,f.cell+f.n))
            {
                var k=f.Local(0,0);const float h=TubeInner+0.1f;
                f.recess.Add(Rect.MinMaxRect(-h/k.x,-h/k.y,h/k.x,h/k.y));
            }
        }

        // Where a conduit plugs into this face: a manifold block round the pipe ends.
        static void Port(Face f,in TilePlacement conduit)
        {
            if(conduit.spatial)
            {
                using(f.Fixed("port",0,0))f.Box(Armor,-1.4f,-1.4f,1.4f,1.4f,0,0.5f);
                f.bare.Add(new Rect(-1.4f,-1.4f,2.8f,2.8f));return;
            }
            Vector3 A=conduit.axis,L=Vector3.Cross(A,-(Vector3)f.n);
            Vector3 c=(Vector3)f.n*Half+A*(BedHeight+0.15f),x=f.x,y=f.y;
            float cx=Vector3.Dot(c,x),cy=Vector3.Dot(c,y);
            float hx=Mathf.Abs(Vector3.Dot(L,x))*1.45f+Mathf.Abs(Vector3.Dot(A,x))*0.95f;
            float hy=Mathf.Abs(Vector3.Dot(L,y))*1.45f+Mathf.Abs(Vector3.Dot(A,y))*0.95f;
            using(f.Fixed("port",cx,cy))f.Box(Armor,cx-hx,cy-hy,cx+hx,cy+hy,0,0.5f,0.06f);
            f.bare.Add(Rect.MinMaxRect(cx-hx,cy-hy,cx+hx,cy+hy));
        }

        bool Column(Vector3Int cell,in TilePlacement p)
        {
            var t=p.Third;
            foreach(var d in new[]{p.side,-p.side,t,-t})if(s.Solid(cell+d))return false;
            return true;
        }

        // ---------------------------------------------------------------- families

        static void Hull(Face f)
        {
            if(!f.axial&&f.Rand(3)<0.5f)
            {
                // Set into the wall, so sized by the local metric rather than anchored; a narrow face has room for one.
                var k=f.Local(0,HighSill+0.4f);float kx=1/k.x,ky=1/k.y;
                foreach(float u in Cell*k.x<3.2f?new[]{0f}:new[]{-1.25f,1.25f})
                {
                    var w=new Rect(u-0.55f*kx,HighSill+0.1f,1.1f*kx,0.6f*ky);
                    f.recess.Add(w);f.m.Reveal(Armor,w,0.5f);f.m.Glass(w,0.5f);
                    f.Box(Armor,w.xMin-0.15f*kx,w.yMax,w.xMax+0.15f*kx,w.yMax+0.18f*ky,0,0.35f,0.02f);
                    f.bare.Add(Rect.MinMaxRect(w.xMin-0.15f*kx,w.yMin,w.xMax+0.15f*kx,w.yMax+0.18f*ky));
                }
            }
            if(f.Rand(4)<0.2f)Hatch(f,f.Rand(5)<0.5f?-1.2f:1.2f,-1.2f,0.7f);
        }

        // Taller panes than the standard row: homes look out more than they look in.
        static void Habitat(Face f)
        {
            if(Cell*f.metric.y<TwoDecks){WindowRow(f,-0.75f,0.75f,true,true);return;}
            WindowRow(f,-1.9f,-0.4f);
            WindowRow(f,0.6f,2.1f);
        }

        // Main face: a terraced pit, a gantry across it and a control tower with a window band. Other faces: heavy
        // asymmetric masses.
        static void Industrial(Face f)
        {
            float sx=f.Rand(1)<0.5f?-1:1,sy=f.Rand(2)<0.5f?-1:1;
            if(f.primary)
            {
                // The control tower keeps its size in the corner; the pit takes what is left.
                var k=f.Local(-sx*1.65f,-sy*1.65f);float kx=1/k.x,ky=1/k.y;
                var tc=new Vector2(-sx*(Half-0.85f*kx),-sy*(Half-0.85f*ky));
                var tower=new Rect(tc.x-0.65f*kx,tc.y-0.65f*ky,1.3f*kx,1.3f*ky);
                float px=sx>0?tower.xMax+0.05f*kx:tower.xMin-0.05f*kx,py=sy>0?tower.yMax+0.05f*ky:tower.yMin-0.05f*ky;
                var pit=Rect.MinMaxRect(Mathf.Min(px,sx*2.05f),Mathf.Min(py,sy*2.05f),Mathf.Max(px,sx*2.05f),Mathf.Max(py,sy*2.05f));
                var inner=Rect.MinMaxRect(pit.xMin+0.55f,pit.yMin+0.55f,pit.xMax-0.55f,pit.yMax-0.55f);
                f.recess.Add(pit);
                f.m.Reveal(Armor,pit,1.0f);
                f.m.Push(new Vector3(0,0,1.0f),Quaternion.identity);
                if(inner.width>0.4f&&inner.height>0.4f){f.m.Wall(Armor,pit,new List<Rect>{inner});f.m.Reveal(Armor,inner,1.7f);f.m.Back(Deep,inner,1.7f);}
                else f.m.Wall(Armor,pit,new List<Rect>());
                f.m.Pop();
                float gy=pit.center.y;
                f.Box(Armor,pit.xMin-0.35f,gy-0.22f,pit.xMax+0.35f,gy+0.22f,0.6f,1.05f);
                f.Box(Armor,pit.xMin-0.35f,gy-0.32f,pit.xMin,gy+0.32f,0,1.05f);
                f.Box(Armor,pit.xMax,gy-0.32f,pit.xMax+0.35f,gy+0.32f,0,1.05f);
                f.bare.Add(Rect.MinMaxRect(pit.xMin-0.35f,gy-0.32f,pit.xMin,gy+0.32f));
                f.bare.Add(Rect.MinMaxRect(pit.xMax,gy-0.32f,pit.xMax+0.35f,gy+0.32f));
                using(f.Fixed("control tower",tc.x,tc.y))
                {
                    var cab=new Rect(tc.x-0.65f,tc.y-0.65f,1.3f,1.3f);
                    f.Box(Armor,cab.xMin,cab.yMin,cab.xMax,cab.yMax,0,2.5f,0.08f);
                    f.Box(Armor,cab.xMin-0.12f,cab.yMin-0.12f,cab.xMax+0.12f,cab.yMax+0.12f,0,0.5f,0.05f);
                    f.Pane(Face.P(cab.xMin,tc.y,2.0f),Vector3.left,Vector3.back,1.0f,0.4f);
                    f.Pane(Face.P(cab.xMax,tc.y,2.0f),Vector3.right,Vector3.back,1.0f,0.4f);
                    f.Pane(Face.P(tc.x,cab.yMin,2.0f),Vector3.down,Vector3.back,1.0f,0.4f);
                    f.Pane(Face.P(tc.x,cab.yMax,2.0f),Vector3.up,Vector3.back,1.0f,0.4f);
                }
                f.bare.Add(Rect.MinMaxRect(tower.xMin-0.12f*kx,tower.yMin-0.12f*ky,tower.xMax+0.12f*kx,tower.yMax+0.12f*ky));
                return;
            }
            void Mass(float u0,float v0,float u1,float v1,float o)
            {
                float a=sx*u0,b=sx*u1,c=sy*v0,d=sy*v1;
                var r=Rect.MinMaxRect(Mathf.Min(a,b),Mathf.Min(c,d),Mathf.Max(a,b),Mathf.Max(c,d));
                f.Box(Armor,r.xMin,r.yMin,r.xMax,r.yMax,0,o,0.07f);f.bare.Add(r);
            }
            Mass(-2.25f,0.3f,0.15f,2.2f,1.2f);
            Mass(0.55f,0.65f,1.85f,1.95f,2.0f);
            Mass(0.2f,-2.25f,2.0f,-0.2f,0.7f);
            Mass(-1.85f,-1.75f,-0.75f,-0.65f,0.45f);
            Mass(0.15f,1.1f,0.55f,1.5f,0.85f);
        }

        // Thick fins at a fixed pitch on a base slab; rows continue across neighbours, end beams where they stop.
        // A 2 x 2 block becomes a reactor dome between two radiator wings.
        static void Power(Face f)
        {
            if(f.Block2(out var c,out bool anchor)){Reactor(f,c,anchor);return;}
            bool dn=f.Cont(-f.y),up=f.Cont(f.y);
            f.Box(Armor,-Half,-Half,Half,Half,0,0.2f,0);
            float v0=dn?-Half:-2.2f,v1=up?Half:2.2f;
            // Fins keep their thickness and roughly 0.83 m pitch; the count follows the frontage's width.
            int fins=Mathf.Max(2,Mathf.RoundToInt(Cell*f.metric.x/0.83f));float t=0.11f/f.metric.x;
            for(int k=0;k<fins;k++){float u=-Half+(k+0.5f)*Cell/fins;f.Box(Armor,u-t,v0,u+t,v1,0.2f,1.8f,0);}
            if(!dn)f.Box(Armor,-Half,-Half,Half,-2.1f,0.2f,2.0f,0.04f);
            if(!up)f.Box(Armor,-Half,2.1f,Half,Half,0.2f,2.0f,0.04f);
            f.Box(Armor,-Half,-0.2f,Half,0.2f,1.6f,1.95f,0);
            f.bare.Add(Whole);
        }

        // Life Support in a row: half-cylinder vaults with rings, domed where the row stops. On its own: upright
        // tanks against faces across its axis, domed tank tops on the others. A 2 x 2 block: one big sphere tank.
        static void LifeSupport(Face f)
        {
            if(f.Block2(out var c,out bool anchor)){SphereTank(f,c,anchor);return;}
            bool l=f.Cont(-f.x),r=f.Cont(f.x);
            f.bare.Add(Whole);
            if(!l&&!r)
            {
                // Tanks are equipment: one size class for the group, anchored on the face centre.
                using(f.Equip("tanks",0,0,Vector3.zero,f.axial?new Vector2(4.2f,4.2f):new Vector2(4.54f,4.6f)))
                {
                    if(f.axial)
                    {
                        var top=new List<Vector2>();
                        for(int k=0;k<=6;k++){float t=k/6f*Mathf.PI*0.5f;top.Add(new Vector2(0.9f*Mathf.Cos(t),0.6f*Mathf.Sin(t)));}
                        foreach(float u in new[]{-1.2f,1.2f})foreach(float v in new[]{-1.2f,1.2f})
                        {
                            f.m.Cylinder(Smooth,Face.P(u,v,0),Face.P(u,v,0.5f),0.9f,20,false,false);
                            f.m.Lathe(Smooth,Face.P(u,v,0.5f),Vector3.back,top,20,true);
                        }
                        return;
                    }
                    const float tr=0.95f,tcap=0.7f;
                    var tdome=new List<Vector2>();
                    for(int k=0;k<=6;k++){float t=k/6f*Mathf.PI*0.5f;tdome.Add(new Vector2(tr*Mathf.Cos(t),tcap*Mathf.Sin(t)));}
                    var band=new[]{new Vector2(tr,0),new Vector2(tr+0.12f,0),new Vector2(tr+0.12f,0.22f),new Vector2(tr,0.22f)};
                    foreach(float u in new[]{-1.2f,1.2f})
                    {
                        f.m.Lathe(Smooth,new Vector3(u,-1.6f,0),Vector3.up,new[]{new Vector2(tr,0),new Vector2(tr,3.2f)},20,false,Vector3.right,Mathf.PI);
                        f.m.Lathe(Smooth,new Vector3(u,1.6f,0),Vector3.up,tdome,20,true,Vector3.right,Mathf.PI);
                        f.m.Lathe(Smooth,new Vector3(u,-1.6f,0),Vector3.down,tdome,20,true,Vector3.left,Mathf.PI);
                        foreach(float v in new[]{-0.6f,0.5f})f.m.Lathe(Smooth,new Vector3(u,v,0),Vector3.up,band,20,false,Vector3.right,Mathf.PI);
                    }
                    return;
                }
            }
            const float R=1.85f,cap=1.0f;
            float x0=l?-Half:-Half+cap,x1=r?Half:Half-cap;
            f.m.Lathe(Smooth,new Vector3(x0,0,0),Vector3.right,new[]{new Vector2(R,0),new Vector2(R,x1-x0)},28,false,Vector3.down,Mathf.PI);
            var dome=new List<Vector2>();
            for(int k=0;k<=6;k++){float t=k/6f*Mathf.PI*0.5f;dome.Add(new Vector2(R*Mathf.Cos(t),cap*Mathf.Sin(t)));}
            if(!l)f.m.Lathe(Smooth,new Vector3(x0,0,0),Vector3.left,dome,28,true,Vector3.up,Mathf.PI);
            if(!r)f.m.Lathe(Smooth,new Vector3(x1,0,0),Vector3.right,dome,28,true,Vector3.down,Mathf.PI);
            // Rings keep their width and 1.25 m pitch along the vault; the count follows the frontage's width.
            float rw=0.17f/f.metric.x;
            var ring=new[]{new Vector2(R,0),new Vector2(R+0.17f,0),new Vector2(R+0.17f,2*rw),new Vector2(R,2*rw)};
            int rings=Mathf.Max(1,Mathf.RoundToInt(Cell*f.metric.x/1.25f));
            for(int k=0;k<rings;k++)
            {
                float u=-Half+(k+0.5f)*Cell/rings;
                if(u-rw>=x0-1e-3f&&u+rw<=x1+1e-3f)f.m.Lathe(Smooth,new Vector3(u-rw,0,0),Vector3.right,ring,28,false,Vector3.down,Mathf.PI);
            }
            float f0=l?-Half:-Half+0.2f,f1=r?Half:Half-0.2f;
            f.Box(Armor,f0,R-0.1f,f1,2.3f,0,0.45f,0);
            f.Box(Armor,f0,-2.3f,f1,-R+0.1f,0,0.45f,0);
        }

        // Main face: a deep bay between lips that slope in towards it, merging with neighbours into one slot; a
        // 2 x 2 block opens one big hangar mouth. Other faces: heavy ribs.
        static void Storage(Face f)
        {
            if(f.primary)
            {
                f.bare.Add(Whole);
                if(f.Block2(out var c,out _)){BigHangar(f,c);return;}
                bool dn=f.Cont(-f.y)&&f.s.Primary(f.cell-f.y,f.p)==f.n,up=f.Cont(f.y)&&f.s.Primary(f.cell+f.y,f.p)==f.n;
                float v0=dn?-Half:-2.05f,v1=up?Half:2.05f;
                var bay=Rect.MinMaxRect(-1.45f,v0,1.45f,v1);
                f.recess.Add(bay);
                f.m.Reveal(Armor,bay,3.4f,1|2|(dn?0:4)|(up?0:8));
                f.m.Back(Deep,bay,3.4f);
                Lip(f,-2.4f,-1.45f,-2.4f,-1.85f,-Half,Half,true,0.9f);
                Lip(f,1.45f,2.4f,1.85f,2.4f,-Half,Half,true,0.9f);
                if(!dn)Lip(f,-Half,-2.05f,-Half,-2.25f,-1.45f,1.45f,false,0.9f);
                if(!up)Lip(f,2.05f,Half,2.25f,Half,-1.45f,1.45f,false,0.9f);
                return;
            }
            int ribs=Mathf.Max(1,Mathf.RoundToInt(Cell*f.metric.x/1.25f));float rib=0.26f/f.metric.x;
            for(int k=0;k<ribs;k++){float u=-Half+(k+0.5f)*Cell/ribs;f.Box(Armor,u-rib,-Half,u+rib,Half,0,0.5f,0);}
            f.bare.Add(Whole);
        }
        // Sloped lip: a0..a1 wide at the wall, t0..t1 at its top, b0..b1 along it; alongV puts a across the face.
        static void Lip(Face f,float a0,float a1,float t0,float t1,float b0,float b1,bool alongV,float height)
        {
            var g=new Vector3[8];
            for(int i=0;i<8;i++)
            {
                bool top=(i&4)!=0;float a=(i&1)!=0?(top?t1:a1):(top?t0:a0),b=(i&2)!=0?b1:b0;
                g[i]=alongV?Face.P(a,b,top?height:0):Face.P(b,a,top?height:0);
            }
            f.m.Hexa(Armor,g);
        }

        // Defense: a tower whose armour courses step in towards its main face, like the trench model's turret
        // towers; the main face carries the turret, its barrels along the tile's side.
        static void Defense(Face f)
        {
            if(f.primary)
            {
                f.Box(Armor,-2.3f,-2.3f,2.3f,2.3f,0,0.35f,0.06f);
                f.Box(Armor,-1.7f,-1.7f,1.7f,1.7f,0.35f,0.7f,0.05f);
                // The turret is equipment: true size in its class, standing on the plinth; its barrels may overhang.
                using var turret=f.Equip("turret",0,0,Face.P(0,0,0.7f),new Vector2(2.3f,2.3f),1.2f);
                const float d=-0.68f;
                f.m.Cylinder(Smooth,Face.P(0,0,1.38f+d),Face.P(0,0,1.85f+d),1.15f,8,true,true,0.05f);
                var h=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    bool top=(i&4)!=0;float e=top?0.75f:0.95f;
                    float v=(i&2)!=0?(top?0.2f:0.85f):(top?-0.85f:-1.0f);
                    h[i]=Face.P((i&1)!=0?e:-e,v,(top?2.75f:1.85f)+d);
                }
                f.m.Hexa(Armor,h);
                f.Box(Armor,-0.55f,0.72f,0.55f,1.1f,1.95f+d,2.6f+d,0.04f);
                f.Box(Armor,-0.6f,-1.3f,0.6f,-0.95f,1.85f+d,2.35f+d,0.03f);
                foreach(float u in new[]{-0.27f,0.27f})
                {
                    f.m.Cylinder(Smooth,Face.P(u,1.05f,2.27f+d),Face.P(u,4.3f,2.27f+d),0.1f,10,false,true,0.01f);
                    f.m.Cylinder(Smooth,Face.P(u,3.95f,2.27f+d),Face.P(u,4.35f,2.27f+d),0.15f,10,true,true,0.02f);
                }
                f.bare.Add(Whole);return;
            }
            else if(!f.axial)
            {
                for(int k=0;k<3;k++)
                {
                    float v0=-Half+k*1.62f,v1=v0+1.5f,o0=0.95f-0.28f*k,o1=0.3f-0.08f*k;
                    var g=new Vector3[8];
                    for(int i=0;i<8;i++)
                    {
                        bool high=(i&2)!=0,front=(i&4)!=0;
                        g[i]=Face.P((i&1)!=0?2.42f:-2.42f,high?v1:v0,front?(high?o1:o0):0);
                    }
                    f.m.Hexa(Armor,g);
                }
            }
            else f.Box(Armor,-2.3f,-2.3f,2.3f,2.3f,0,0.3f,0.06f);
            f.bare.Add(Whole);
        }

        // Main face: stepped tower, neck, a bridge with sloped window walls, sensor globes, mast; a 2 x 2 block
        // raises one big dish instead. Other faces: an upper window row over setback plates.
        static void Command(Face f)
        {
            if(f.primary)
            {
                f.bare.Add(Whole);
                if(f.Block2(out var c,out bool anchor)){BigDish(f,c,anchor);return;}
                f.Box(Armor,-2.25f,-2.25f,2.25f,2.25f,0,1.1f,0.12f);
                f.Box(Armor,-1.55f,-1.55f,1.55f,1.55f,1.1f,2.05f,0.1f);
                // Neck, bridge, globes and mast are true size in their class, standing on the stepped tower.
                using var bridge=f.Equip("command top",0,0,Face.P(0,0,2.05f),new Vector2(2.1f,4.9f),0);
                f.Box(Armor,-0.75f,-0.75f,0.75f,0.75f,2.05f,4.0f,0.06f);
                var g=new Vector3[8];
                for(int i=0;i<8;i++){bool upper=(i&4)!=0;float e=upper?0.7f:1.05f;g[i]=Face.P((i&1)!=0?e:-e,(i&2)!=0?2.45f:-2.45f,upper?4.9f:4.0f);}
                f.m.Hexa(Armor,g);
                foreach(float sgn in new[]{-1f,1f})
                {
                    var outward=new Vector3(sgn*0.9f,0,-0.35f).normalized;var up=new Vector3(-sgn*0.35f,0,-0.9f).normalized;
                    f.Pane(Face.P(sgn*0.875f,0,4.45f),outward,up,4.3f,0.5f);
                }
                f.Box(Armor,-0.6f,-1.25f,0.6f,1.25f,4.9f,5.2f,0.05f);
                foreach(float v in new[]{-1.75f,1.75f})
                {
                    f.m.Cylinder(Smooth,Face.P(0,v,4.9f),Face.P(0,v,5.1f),0.22f,12,false,true,0.02f);
                    f.m.Sphere(Smooth,Face.P(0,v,5.55f),0.5f,20);
                }
                f.Box(Armor,-0.07f,-0.07f,0.07f,0.07f,5.2f,7.4f,0);
                return;
            }
            if(!f.axial)WindowRow(f,HighSill,HighHead);
            float top=f.axial?2.3f:0.55f;
            f.Box(Armor,-2.3f,-2.3f,2.3f,top,0,0.35f,0.06f);
            f.Box(Armor,-1.6f,-1.6f,1.6f,top-0.4f,0.35f,0.7f,0.05f);
            f.bare.Add(Rect.MinMaxRect(-2.3f,-2.3f,2.3f,top));
        }

        // ---------------------------------------------------------------- 2 x 2 landmarks
        // Every tile of the block lays a quiet plinth; the anchor builds the landmark centred on the block (c), at
        // true size in the largest class the block has room for (span in metres).
        static Equipment Landmark(Face f,Vector2 c,string name,float span)=>
            f.Equip(name,c.x,c.y,Face.P(c.x,c.y,0.3f),new Vector2(span,span),0,2);

        // Command: a 9 m dish on a pedestal and yoke, tilted along the face's y, with a feed on a tripod.
        static void BigDish(Face f,Vector2 c,bool anchor)
        {
            f.Box(Armor,-Half,-Half,Half,Half,0,0.3f,0);
            if(!anchor)return;
            using var dish=Landmark(f,c,"dish",9f);
            f.m.Cylinder(Smooth,Face.P(c.x,c.y,0.3f),Face.P(c.x,c.y,1.0f),2.2f,16,false,true,0.08f);
            f.m.Cylinder(Smooth,Face.P(c.x,c.y,1.0f),Face.P(c.x,c.y,2.8f),1.0f,16,false,true,0.06f);
            var hub=Face.P(c.x,c.y,3.1f);
            foreach(float s in new[]{-1f,1f})f.m.Box(Armor,hub+new Vector3(s*0.95f,0,0.35f),new Vector3(0.32f,0.7f,1.5f),0.04f);
            var aim=(Vector3.back+Vector3.up*0.6f).normalized;
            const float R=4.5f,depth=1.25f;
            f.m.Dish(Smooth,hub+aim*0.2f,aim,R,depth,36);
            var feed=hub+aim*3.9f;
            f.m.Box(Armor,feed,Vector3.one*0.55f,0.06f);
            Vector3 u1=Vector3.Cross(aim,Vector3.right).normalized,u2=Vector3.Cross(aim,u1);
            for(int k=0;k<3;k++)
            {
                float a=k*Mathf.PI*2/3+0.5f;
                var rim=hub+aim*(0.2f+depth*0.64f)+(u1*Mathf.Cos(a)+u2*Mathf.Sin(a))*R*0.8f;
                f.m.Cylinder(Smooth,rim,feed,0.07f,6,false,false);
            }
        }

        // Power: a reactor dome on a ring drum between two ribbed radiator wings.
        static void Reactor(Face f,Vector2 c,bool anchor)
        {
            f.Box(Armor,-Half,-Half,Half,Half,0,0.3f,0);
            f.bare.Add(Whole);
            if(!anchor)return;
            using var reactor=Landmark(f,c,"reactor",9.4f);
            var o=Face.P(c.x,c.y,0.3f);
            f.m.Lathe(Smooth,o,Vector3.back,new[]{new Vector2(0,0),new Vector2(4.2f,0),new Vector2(4.2f,0.8f),new Vector2(3.7f,0.8f)},36,false);
            var dome=new List<Vector2>();
            for(int k=0;k<=10;k++){float t=k/10f*Mathf.PI*0.5f;dome.Add(new Vector2(3.7f*Mathf.Cos(t),0.8f+3.1f*Mathf.Sin(t)));}
            f.m.Lathe(Smooth,o,Vector3.back,dome,36,true);
            f.m.Lathe(Smooth,o,Vector3.back,new[]{new Vector2(3.55f,1.6f),new Vector2(3.72f,1.6f),new Vector2(3.72f,1.9f),new Vector2(3.4f,2.2f)},36,false);
            f.m.Cylinder(Smooth,Face.P(c.x,c.y,3.8f),Face.P(c.x,c.y,4.5f),0.8f,16,false,true,0.05f);
            foreach(float s in new[]{-1f,1f})
            {
                float v=c.y+s*4.7f;
                f.Box(Armor,c.x-4.6f,v-0.12f,c.x+4.6f,v+0.12f,0.3f,6.2f,0);
                for(int k=0;k<6;k++){float u=c.x-4.6f+k*1.84f;f.Box(Armor,u-0.08f,v-0.3f,u+0.08f,v+0.3f,0.3f,6.2f,0);}
                f.Box(Armor,c.x-4.7f,v-0.35f,c.x+4.7f,v+0.35f,6.0f,6.35f,0.04f);
            }
        }

        // Life Support: a big sphere tank on a drum, with an equator band.
        static void SphereTank(Face f,Vector2 c,bool anchor)
        {
            f.Box(Armor,-Half,-Half,Half,Half,0,0.3f,0);
            f.bare.Add(Whole);
            if(!anchor)return;
            using var tank=Landmark(f,c,"sphere tank",7.4f);
            var o=Face.P(c.x,c.y,0.3f);
            f.m.Lathe(Smooth,o,Vector3.back,new[]{new Vector2(0,0),new Vector2(3.2f,0),new Vector2(3.2f,1.2f),new Vector2(0,1.2f)},32,false);
            f.m.Sphere(Smooth,Face.P(c.x,c.y,3.8f),3.5f,36);
            f.m.Lathe(Smooth,o,Vector3.back,new[]{new Vector2(3.48f,3.32f),new Vector2(3.7f,3.32f),new Vector2(3.7f,3.68f),new Vector2(3.48f,3.68f)},36,false);
        }

        // Storage: one hangar mouth across the block, each tile cutting its quarter and its stretch of sloped lip.
        static void BigHangar(Face f,Vector2 c)
        {
            float sx=Mathf.Sign(c.x),sy=Mathf.Sign(c.y);
            var bay=Rect.MinMaxRect(c.x-4.1f,c.y-3.5f,c.x+4.1f,c.y+3.5f);
            var part=Rect.MinMaxRect(Mathf.Max(bay.xMin,-Half),Mathf.Max(bay.yMin,-Half),Mathf.Min(bay.xMax,Half),Mathf.Min(bay.yMax,Half));
            int sides=(part.xMin>-Half+1e-3f?1:0)|(part.xMax<Half-1e-3f?2:0)|(part.yMin>-Half+1e-3f?4:0)|(part.yMax<Half-1e-3f?8:0);
            f.recess.Add(part);
            f.m.Reveal(Armor,part,4.4f,sides);
            f.m.Back(Deep,part,4.4f);
            // lip along the side of the mouth (across x) and along its end (across y), sloping in towards it
            float ex=-sx*1.6f,ey=-sy*1.0f;
            Lip(f,Mathf.Min(ex,-sx*Half),Mathf.Max(ex,-sx*Half),sx>0?-Half:2.05f,sx>0?-2.05f:Half,-Half,Half,true,1.1f);
            Lip(f,Mathf.Min(ey,-sy*Half),Mathf.Max(ey,-sy*Half),sy>0?-Half:1.45f,sy>0?-1.45f:Half,Mathf.Min(ex,sx*Half),Mathf.Max(ex,sx*Half),false,1.1f);
        }

        // Landing pad for small craft: a round deck on a pedestal and angled struts, a rim, and an airlock hatch in
        // the centre so a pod docks straight onto it. A 2 x 2 block makes one 9 m pad, each tile building its
        // quarter. Faces across its axis carry a window row (the pad's control room). The deck takes the largest size
        // class whose deck (4.4 m in L, 9.1 m for a 2 x 2 block) overhangs the face by at most 0.4 m a side, so a pad
        // fits on every face. The hatch and the pedestal under it stay true size.
        const float PadOverhang=0.4f;
        static void Pad(Face f)
        {
            if(!f.primary){if(!f.axial)WindowRow(f,HighSill,HighHead);return;}
            bool big=f.Block2(out var c,out _);
            var q=f.cell;var dx=f.x*(c.x<0?-1:1);var dy=f.y*(c.y<0?-1:1);
            float room=Mathf.Min(f.Span(q,dx),f.Span(q,dy)),deck=4.4f;
            if(big)
            {
                // the block's width along each side, on its narrower row
                float wx=Mathf.Min(f.Span(q,dx)+f.Span(q+dx,dx),f.Span(q+dy,dx)+f.Span(q+dx+dy,dx));
                float wy=Mathf.Min(f.Span(q,dy)+f.Span(q+dy,dy),f.Span(q+dx,dy)+f.Span(q+dx+dy,dy));
                room=Mathf.Min(wx,wy);deck=9.1f;
            }
            float k=SizeClasses[SizeClasses.Length-1];
            foreach(float cls in SizeClasses)if(deck*cls<=room+2*PadOverhang+1e-3f){k=cls;break;}
            string size=k>0.9f?" L":k>0.7f?" M":" S";
            using(big?f.Fixed("pad quarter"+size,c.x,c.y):f.Fixed("pad"+size,0,0))PadGeometry(f,big,c,k);
        }
        static void PadGeometry(Face f,bool big,Vector2 c,float k)
        {
            f.bare.Add(Whole);
            const float deck=0.7f,top=0.95f;
            var axis=Vector3.back;
            if(big)
            {
                float sx=Mathf.Sign(c.x),sy=Mathf.Sign(c.y);
                var o=new Vector3(c.x,c.y,0);
                var start=sx==-sy?new Vector3(-sx,0,0):new Vector3(0,-sy,0);
                const float arc=Mathf.PI*0.5f;float R=4.55f*k;
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(0,0),new Vector2(1.3f,0),new Vector2(1.3f,deck)},12,false,start,arc);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(0,deck),new Vector2(R,deck),new Vector2(R,top),new Vector2(0,top)},24,false,start,arc);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(R,top),new Vector2(R,top+0.18f),new Vector2(R-0.35f,top+0.18f),new Vector2(R-0.35f,top)},24,false,start,arc);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(1.3f,top),new Vector2(1.3f,top+0.1f),new Vector2(1.05f,top+0.1f),new Vector2(1.05f,top)},12,false,start,arc);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(0,top),new Vector2(1.0f,top),new Vector2(1.0f,top+0.04f),new Vector2(0,top+0.04f)},12,false,start,arc);
                Strut(f,c,new Vector2(-sx,-sy).normalized,1.4f,Mathf.Max(3.3f*k,2f),deck);
                return;
            }
            {
                float R=2.2f*k;
                var o=Vector3.zero;
                f.m.Cylinder(Smooth,Face.P(0,0,0),Face.P(0,0,deck),0.85f,16,false,false);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(0,deck),new Vector2(R,deck),new Vector2(R,top),new Vector2(0,top)},28,false);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(R,top),new Vector2(R,top+0.16f),new Vector2(R-0.28f,top+0.16f),new Vector2(R-0.28f,top)},28,false);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(0.85f,top),new Vector2(0.85f,top+0.1f),new Vector2(0.65f,top+0.1f),new Vector2(0.65f,top)},20,false);
                f.m.Lathe(Smooth,o,axis,new[]{new Vector2(0,top),new Vector2(0.62f,top),new Vector2(0.62f,top+0.04f),new Vector2(0,top+0.04f)},20,false);
                foreach(var d in new[]{new Vector2(1,1),new Vector2(-1,1),new Vector2(1,-1),new Vector2(-1,-1)})Strut(f,Vector2.zero,d.normalized,0.7f,Mathf.Max(1.75f*k,1.3f),deck);
            }
        }
        // Angled strut from near the pad's foot out to the underside of its deck.
        static void Strut(Face f,Vector2 from,Vector2 dir,float r0,float r1,float deck)
        {
            var side=new Vector2(-dir.y,dir.x)*0.13f;
            var g=new Vector3[8];
            for(int i=0;i<8;i++)
            {
                bool top=(i&4)!=0,outer=(i&2)!=0;
                float r=top?(outer?r1:r1-0.5f):(outer?r0+0.5f:r0);
                var q=from+dir*r+((i&1)!=0?side:-side);
                g[i]=Face.P(q.x,q.y,top?deck:0);
            }
            f.m.Hexa(Armor,g);
        }

        // Open ribbed tube along the tile's axis with portholes on two sides; collars where it meets something,
        // a docking collar with a round hatch where it ends in space.
        // Corridors and conduits keep a true-size cross-section (TileMesh.BeginTube); their length follows the cells.
        // Parts with a length along the run (ribs, porthole rims, collars) are squeezed back by the run's stretch so
        // they keep their true length too.
        float RunStretch(Vector3Int cell,Vector3 A)=>curved==null?1:curved.Scale(TileStructure.Center(cell),A);
        // Metres of room across a cell's face round its centre, for the collar and socket that meet there; both sides of
        // the face derive the same value.
        float FaceRoom(Vector3Int cell,Vector3 n)
        {
            if(curved==null)return Cell;
            int axis=AxisOf(Vector3Int.RoundToInt(n));var u=Vector3.zero;u[(axis+1)%3]=1;var v=Vector3.zero;v[(axis+2)%3]=1;
            var room=curved.Room(TileStructure.Center(cell)+n*(Half-0.01f),u,v);return Mathf.Min(room.x,room.y);
        }
        // Collar and socket radius (octagon apothem): 2.15 m where the face has room, down to 1.6 m round the 1.4 m tube.
        static float CollarRadius(float room)=>Mathf.Clamp(room*0.5f-0.15f,1.6f,2.15f);
        static void AlongRun(TileMesh m,Vector3 pivot,Vector3 A,float stretch)
        {
            var k=Vector3.one;k[AxisOf(Vector3Int.RoundToInt(A))]=1/stretch;
            m.PushMatrix(Matrix4x4.Translate(pivot)*Matrix4x4.Scale(k)*Matrix4x4.Translate(-pivot));
        }
        void Corridor(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            var links=s.CorridorLinks(cell,p);
            if(links.Count!=2||links[0]!=-links[1]){CorridorJunction(m,cell,p,links);return;}
            var run=links[0];var side=Vector3.Dot(run,p.side)==0?p.side:p.Third;
            Vector3 A=run,B=side,C=TileStructure.Cross(side,run);
            float stretch=RunStretch(cell,A),k=1/stretch;
            using var tube=m.BeginTube("corridor",TileStructure.Center(cell),A);
            var oct=Octagon(1.4f);
            // Ribs keep their 0.34 m width and a pitch near 1.25 m; portholes sit between them.
            var rib=Octagon(Mathf.Clamp(FaceRoom(cell,A)*0.5f-0.05f,1.5f,1.75f));
            int ribs=Mathf.Max(1,Mathf.RoundToInt(Cell*stretch/1.25f));float pitch=Cell/ribs;
            // The two sides with portholes are open under the glass, so the tube's interior shows through it.
            var ports=new float[ribs-1];for(int r=1;r<ribs;r++)ports[r-1]=-Half+r*pitch;
            m.Prism(Armor,oct,Vector3.zero,B,C,A,-Half,Half,false,PortholeEdges);
            foreach(int i in new[]{0,4})TubeSide(m,Armor,oct,i,B,C,A,-Half,Half,ports,PortholeHalf*k,PortholeHalf,false);
            for(int r=0;r<ribs;r++)
            {
                float t=-Half+(r+0.5f)*pitch;
                m.Prism(Armor,rib,Vector3.zero,B,C,A,t-0.17f*k,t+0.17f*k,false);
                m.Annulus(Armor,oct,rib,Vector3.zero,B,C,A,t-0.17f*k,-1);m.Annulus(Armor,oct,rib,Vector3.zero,B,C,A,t+0.17f*k,1);
            }
            foreach(float sb in new[]{-1f,1f})for(int r=1;r<ribs;r++)
            {
                var o=B*sb*1.4f+A*(-Half+r*pitch);
                AlongRun(m,o,A,stretch);
                m.Lathe(Smooth,o,B*sb,new[]{new Vector2(0.44f,0),new Vector2(0.44f,0.12f),new Vector2(0.34f,0.12f),new Vector2(0.34f,0)},20,false);
                m.Push(o,Quaternion.LookRotation(-B*sb,C));m.Glass(new Rect(-0.24f,-0.24f,0.48f,0.48f),-0.005f);m.Pop();
                m.Pop();
            }
            foreach(int e in new[]{-1,1})
            {
                var next=cell+run*e;
                if(s.TryGet(next,out var q)&&q.family==TileFamily.Corridor&&s.CorridorLinks(next,q).Contains(-run*e))continue;
                var collar=Octagon(CollarRadius(FaceRoom(cell,A*e)));
                float a0=e>0?Half-0.6f*k:-Half,a1=e>0?Half:-Half+0.6f*k;
                bool open=!s.Solid(next);
                m.Prism(Armor,collar,Vector3.zero,B,C,A,a0,a1,open);
                m.Annulus(Armor,oct,collar,Vector3.zero,B,C,A,e>0?a0:a1,-e);
                if(open)
                {
                    var face=A*e*Half;
                    AlongRun(m,face,A,stretch);
                    m.Lathe(Smooth,face,A*e,new[]{new Vector2(1.35f,0),new Vector2(1.35f,0.3f),new Vector2(1.1f,0.3f),new Vector2(1.1f,0)},24,false);
                    m.Lathe(Smooth,face,A*e,new[]{new Vector2(0,0),new Vector2(1.1f,0),new Vector2(1.1f,0.12f),new Vector2(0,0.12f)},24,false);
                    m.Pop();
                }
            }
        }
        void CorridorJunction(TileMesh m,Vector3Int cell,in TilePlacement p,List<Vector3Int> links)
        {
            // A compact pressure junction with half-tubes to every occupied neighbour. Empty corners remain open.
            var center=TileStructure.Center(cell);Vector3 up=p.axis;
            // rigid all through, so the room inside it (TileBuilder.Interior.cs) stays inside it on a bent cell
            // faces a branch leaves through stay plain: their carving would stand across the branch inside
            int plain=0;foreach(var d in links){int ax=AxisOf(d);plain|=1<<(ax*2+(d[ax]>0?1:0));}
            using(m.BeginRigid("junction",center,new Vector4(up.x,up.y,up.z,Vector3.Dot(center,up))))
                m.Box(Armor,Vector3.zero,Vector3.one*2.8f,0.28f,plain);
            foreach(var d in links)
            {
                Vector3 a=d,b=Vector3.Dot(d,p.side)==0?p.side:p.Third,c=Vector3.Cross(b,a);
                float k=1/RunStretch(cell,a);
                using var branch=m.BeginTube("corridor branch",center,a);
                var oct=Octagon(1.4f);var rib=Octagon(1.75f);
                var next=cell+d;bool collared=!(s.TryGet(next,out var q)&&q.family==TileFamily.Corridor);
                float collar0=collared?Half-0.45f*k:Half;
                // The tube from inside the box and its rib, placed by true distance from the centre, so they clear the
                // room in the box (TileBuilder.Interior.cs) however the run is bent; a run too short for the rib has none.
                float a0=Mathf.Min(Along(cell,a,1.36f),collar0),r0=Along(cell,a,1.65f),r1=Along(cell,a,1.95f);
                m.Prism(Armor,oct,Vector3.zero,b,c,a,a0,Half,false);
                if(r1<collar0-0.05f)
                {
                    m.Prism(Armor,rib,Vector3.zero,b,c,a,r0,r1,false);
                    m.Annulus(Armor,oct,rib,Vector3.zero,b,c,a,r0,-1);
                    m.Annulus(Armor,oct,rib,Vector3.zero,b,c,a,r1,1);
                }
                if(!collared)continue;
                var collar=Octagon(CollarRadius(FaceRoom(cell,a)));
                m.Prism(Armor,collar,Vector3.zero,b,c,a,Half-0.45f*k,Half,!s.Solid(next));
                m.Annulus(Armor,oct,collar,Vector3.zero,b,c,a,Half-0.45f*k,-1);
            }
        }
        // Station metres along a from the cell's centre to the point of the run's spine that lies metres from the centre
        // on the curved grid (metres itself on flat stages).
        float Along(Vector3Int cell,Vector3 a,float metres)
        {
            if(curved==null)return metres;
            var c=TileStructure.Center(cell);var o=curved.Map(c);float lo=0,hi=Cell;
            for(int i=0;i<20;i++){float mid=(lo+hi)*0.5f;if((curved.Map(c+a*mid)-o).magnitude<metres)lo=mid;else hi=mid;}
            return (lo+hi)*0.5f;
        }
        static Vector2[] Octagon(float r)
        {
            float k=r*0.4142f;
            return new[]{new Vector2(r,-k),new Vector2(r,k),new Vector2(k,r),new Vector2(-k,r),new Vector2(-r,k),new Vector2(-r,-k),new Vector2(-k,-r),new Vector2(k,-r)};
        }

        // A bundle of two big pipes and a small one lying on the surface below the tile (its -axis), on saddles.
        // It links to neighbouring conduits with the same bed; a single link runs straight through. Two links at a
        // right angle make an elbow, three or more a junction block. Ends plug into solid tiles with a collar, or
        // stop in space with a blind flange.
        static readonly (float lat,float lift,float r)[] Bundle={(-0.62f,0,0.55f),(0.62f,0,0.55f),(0,0.66f,0.28f)};
        const float BedHeight=-1.45f;
        void Conduit(TileMesh m,Vector3Int cell,in TilePlacement p)
        {
            Vector3 A=p.axis;
            var links=ConduitLinks(cell,p);
            if(p.spatial){SpatialConduit(m,cell,p,links);return;}
            bool bed=s.Solid(cell-p.axis);
            // Pipes run as tubes on the bed line; an elbow's torus stays in station space, so a flange at each end
            // covers the step to the true-size pipes of its neighbours.
            var spine=TileStructure.Center(cell)+A*BedHeight;
            bool elbow=links.Count==2&&links[0]!=-links[1];
            if(elbow)
            {
                Vector3 c1=links[0],c2=links[1],turn=(c1+c2)*Half;
                foreach(var (lat,lift,r) in Bundle)
                {
                    // lateral offsets lie across the run; measured along c2 where the bundle enters through c1
                    float R=Half-lat;
                    var axis=Vector3.Cross(A,-c2)==-c1?A:-A;
                    m.Torus(Smooth,turn+A*(BedHeight+lift),axis,-c2,Mathf.PI*0.5f,R,r,20,12);
                }
                if(bed)Saddle(m,A,(c1+c2)*(Half-Half*0.7071f),Vector3.Normalize(c1+c2));
                if(curved!=null)foreach(var d in links)
                {
                    Vector3 dir=d,lateral=Vector3.Cross(A,dir);float k=1/RunStretch(cell,dir);
                    using var tube=m.BeginTube("conduit flange",spine,dir);
                    foreach(var (lat,lift,r) in Bundle){var a=lateral*lat+A*(BedHeight+lift)+dir*Half;m.Cylinder(Smooth,a-dir*0.3f*k,a,r+0.15f,16,true,true,0.02f);}
                }
            }
            else
            {
                foreach(var d in links)
                {
                    Vector3 dir=d,lateral=Vector3.Cross(A,dir);float k=1/RunStretch(cell,dir);
                    using(m.BeginTube("conduit",spine,dir))foreach(var (lat,lift,r) in Bundle)
                    {
                        var a=lateral*lat+A*(BedHeight+lift);
                        m.Cylinder(Smooth,a,a+dir*Half,r,16,false,false);
                        m.Cylinder(Smooth,a+dir*(1.25f-0.08f*k),a+dir*(1.25f+0.08f*k),r+0.09f,16,true,true,0.02f);
                    }
                    if(bed)Saddle(m,A,dir*1.25f,lateral);
                }
                if(links.Count>2)
                    using(m.BeginFixed("conduit junction",spine,new Vector4(A.x,A.y,A.z,Vector3.Dot(spine,A))))m.Box(Armor,A*(BedHeight+0.25f),Vector3.one*1.9f,0.1f);
            }
            foreach(var d in links)
            {
                if(s.TryGet(cell+d,out var q)&&q.family==TileFamily.Conduit&&q.axis==p.axis)continue;
                Vector3 dir=d,lateral=Vector3.Cross(A,dir);float k=1/RunStretch(cell,dir);
                bool plug=s.Solid(cell+d);
                using var tube=m.BeginTube("conduit end",spine,dir);
                foreach(var (lat,lift,r) in Bundle)
                {
                    var a=lateral*lat+A*(BedHeight+lift)+dir*Half;
                    // A plug ends its collar on the face of the port the tile builds; a blind end closes in space
                    if(plug)m.Cylinder(Smooth,a-dir*0.8f*k,a-dir*0.5f*k,r+0.22f,16,true,true,0.03f);
                    else m.Cylinder(Smooth,a-dir*0.3f*k,a,r+0.12f,16,true,true,0.03f);
                }
            }
        }
        // Directions a conduit runs: to neighbouring conduits on the same bed; one link runs straight through,
        // none runs along the tile's side.
        List<Vector3Int> ConduitLinks(Vector3Int cell,in TilePlacement p)
        {
            var t3=p.Third;var links=new List<Vector3Int>();var plane=new[]{p.side,-p.side,t3,-t3};
            foreach(var d in p.spatial?TileStructure.Directions:plane)
                if(s.TryGet(cell+d,out var q)&&q.family==TileFamily.Conduit&&q.axis==p.axis)links.Add(d);
            if(p.spatial)foreach(var d in TileStructure.Directions)if(s.Solid(cell+d)&&!links.Contains(d))links.Add(d);
            int joined=links.Count;
            if(links.Count==0){links.Add(p.side);links.Add(-p.side);}
            else if(links.Count==1)links.Add(-links[0]);
            // A tethered group (see SettlementSolver) reaches for the solid tiles beside it: a run end turns into one
            // instead of stopping in space, a lone cell runs from one, and a turn branches into each. Straight runs and
            // junctions stay as they are, so pipes along a wall don't plug in at every cell.
            if(p.tether&&!p.spatial)
            {
                var solid=System.Array.FindAll(plane,d=>s.Solid(cell+d));
                if(joined==0&&solid.Length>0&&!s.Solid(cell+p.side)&&!s.Solid(cell-p.side))
                {links.Clear();links.Add(solid[0]);links.Add(-solid[0]);}
                else if(joined==1&&!s.Solid(cell+links[1])&&System.Array.Exists(solid,d=>d!=links[1]))
                    links[1]=System.Array.Find(solid,d=>d!=links[1]);
                else if(joined==2&&links[0]!=-links[1])
                    foreach(var d in solid)if(!links.Contains(d))links.Add(d);
            }
            return links;
        }
        void SpatialConduit(TileMesh m,Vector3Int cell,in TilePlacement p,List<Vector3Int> links)
        {
            var center=TileStructure.Center(cell);Vector3 up=p.axis;
            using(m.BeginFixed("conduit junction",center,new Vector4(up.x,up.y,up.z,Vector3.Dot(center,up))))
                m.Box(Armor,Vector3.zero,Vector3.one*1.7f,0.12f);
            foreach(var d in links)
            {
                // Endpoints use the same unsigned cross-section on both sides of a shared face.
                int axis=AxisOf(d);var u=Vector3.zero;u[(axis+1)%3]=1;var v=Vector3.zero;v[(axis+2)%3]=1;
                bool connected=s.TryGet(cell+d,out var q)&&q.family==TileFamily.Conduit;
                float k=1/RunStretch(cell,d);
                using var tube=m.BeginTube("conduit",center,d);
                foreach(var (lat,lift,r) in Bundle)
                {
                    var offset=u*lat+v*lift;var end=offset+(Vector3)d*Half;
                    m.Cylinder(Smooth,offset,end,r,16,false,!connected);
                    m.Cylinder(Smooth,end-(Vector3)d*0.3f*k,end,r+0.12f,16,true,true,0.02f);
                }
            }
        }
        // Saddle under the bundle down to the bed face, across the run.
        static void Saddle(TileMesh m,Vector3 A,Vector3 at,Vector3 across)
        {
            float bottom=-Half,topH=BedHeight;
            var center=at+A*((bottom+topH)*0.5f);
            var rot=Quaternion.LookRotation(Vector3.Cross(across,A),A);
            m.Push(center,rot);m.Box(Armor,Vector3.zero,new Vector3(2.7f,topH-bottom,0.32f),0.03f);m.Pop();
        }

        // 1.8 m reference person in a work suit, feet at the origin, standing along +Y.
        public static TileMesh Figure(int seed)
        {
            var m=new TileMesh();
            var boots=new Finish(TileSlot.Dark,TilePattern.Plain,Color.white,0);
            var suit=new Finish(TileSlot.Accent,TilePattern.Plain,seed%2==0?new Color(0.82f,0.36f,0.1f):new Color(0.62f,0.64f,0.66f),0);
            foreach(float s in new[]{-1f,1f})
            {
                m.Box(suit,new Vector3(s*0.1f,0.5f,0),new Vector3(0.15f,0.72f,0.17f),0.04f);
                m.Box(boots,new Vector3(s*0.1f,0.07f,-0.02f),new Vector3(0.16f,0.14f,0.24f),0.03f);
                m.Box(suit,new Vector3(s*0.28f,1.13f,0),new Vector3(0.1f,0.58f,0.12f),0.035f);
            }
            m.Box(suit,new Vector3(0,0.93f,0),new Vector3(0.36f,0.2f,0.21f),0.05f);
            m.Box(suit,new Vector3(0,1.23f,0),new Vector3(0.44f,0.48f,0.24f),0.07f);
            m.Box(new Finish(TileSlot.Trim),new Vector3(0,1.28f,0.15f),new Vector3(0.3f,0.36f,0.08f),0.03f);
            m.Sphere(new Finish(TileSlot.Paint,TilePattern.Plain,new Color(1.1f,1.1f,1.08f),0),new Vector3(0,1.67f,0),0.13f,12);
            return m;
        }
    }
}
