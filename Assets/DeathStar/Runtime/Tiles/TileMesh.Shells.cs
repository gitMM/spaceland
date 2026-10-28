using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // The far level's station relief. GreebleField builds each face's relief as geometry for the near and mid levels
    // and records it here; the far level leaves that geometry out (as it does Detail parts) and draws a shell in its
    // place: over each free rect of the face, a box from the face up to the relief's top, with sides only where the
    // relief stops (round holes, and at the face's edge where it doesn't carry on into the neighbour). TileSurface
    // traces the relief inside it (the Relief slot, TileGreeble.hlsl). Shell vertices carry uv1 the rect in station
    // (u, v), uv2 (axis * 2 + 1 if the face looks along +axis, the top, the side bits), uv6 their station position and
    // the face plane.
    public sealed partial class TileMesh
    {
        struct Shell
        {
            public int axis,sign;public float plane,top;
            public List<(Rect r,int sides)> cells;     // sides: 1 u min, 2 u max, 4 v min, 8 v max
            public Vector4 frame,anchor;public Finish finish;
        }
        readonly List<Shell> shells=new List<Shell>();

        // Where a face's relief begins.
        public (int v,int[] i) ReliefStart()=>(positions.Count,IndexCounts());
        // Where it ends: the far level leaves it out and draws a shell over cells instead, in the relief's finish.
        public void ReliefEnd((int v,int[] i) start,int axis,int sign,float plane,List<(Rect r,int sides)> cells,in Finish finish)
        {
            if(dry||coarse>0||inDetail||positions.Count==start.v||cells.Count==0)return;
            float top=0;
            for(int v=start.v;v<positions.Count;v++)top=Mathf.Max(top,sign*(positions[v][axis]+stationOffset[axis]-plane));
            if(top<0.01f)return;
            int jobsNow=jobs.Count;
            details.Add((start.v,positions.Count,jobsNow,jobsNow,start.i,IndexCounts()));
            shells.Add(new Shell{axis=axis,sign=sign,plane=plane,top=top+0.005f,cells=cells,frame=surfaceFrame,anchor=metricAnchor,
                finish=new Finish(TileSlot.Relief,finish.pattern,finish.tint,finish.wear)});
        }

        // A box standing on an axis-aligned face (normal axis, sign; at station coordinate plane) over rect r in station
        // (u, v), from h0 to h1 off the face: its top, and its sides where sides says (1 u min, 2 u max, 4 v min, 8 v max).
        // origin: the station position of this mesh's origin.
        public void FaceBox(in Finish f,int axis,int sign,float plane,Vector3 origin,Rect r,float h0,float h1,int sides)
        {
            int ua=(axis+1)%3,va=(axis+2)%3;
            Vector3 P(float u,float v,float h){var p=Vector3.zero;p[ua]=u;p[va]=v;p[axis]=plane+sign*h;return p-origin;}
            Vector3 n=Vector3.zero;n[axis]=sign;
            Vector3 eu=Vector3.zero;eu[ua]=1;Vector3 ev=Vector3.zero;ev[va]=1;
            Quad(f,P(r.xMin,r.yMin,h1),P(r.xMax,r.yMin,h1),P(r.xMax,r.yMax,h1),P(r.xMin,r.yMax,h1),n,eu);
            if((sides&1)!=0)Quad(f,P(r.xMin,r.yMin,h0),P(r.xMin,r.yMax,h0),P(r.xMin,r.yMax,h1),P(r.xMin,r.yMin,h1),-eu,ev);
            if((sides&2)!=0)Quad(f,P(r.xMax,r.yMin,h0),P(r.xMax,r.yMax,h0),P(r.xMax,r.yMax,h1),P(r.xMax,r.yMin,h1),eu,ev);
            if((sides&4)!=0)Quad(f,P(r.xMin,r.yMin,h0),P(r.xMax,r.yMin,h0),P(r.xMax,r.yMin,h1),P(r.xMin,r.yMin,h1),-ev,eu);
            if((sides&8)!=0)Quad(f,P(r.xMin,r.yMax,h0),P(r.xMax,r.yMax,h0),P(r.xMax,r.yMax,h1),P(r.xMin,r.yMax,h1),ev,eu);
        }

        // Draws the recorded shells into the far mesh, adding each vertex to map as its own; data gets each vertex's
        // uv1, uv2 and uv6, to set as the mesh is built.
        void EmitShells(TileMesh far,List<int> map,List<(int index,Vector4 uv1,Vector4 uv2,Vector4 uv6)> data)
        {
            foreach(var s in shells)
            {
                far.surfaceFrame=s.frame;far.metricAnchor=s.anchor;
                foreach(var (r,sides) in s.cells)
                {
                    int v0=far.positions.Count;
                    far.FaceBox(s.finish,s.axis,s.sign,s.plane,stationOffset,r,0,s.top,sides);
                    var uv1=new Vector4(r.xMin,r.yMin,r.xMax,r.yMax);var uv2=new Vector4(s.axis*2+(s.sign>0?1:0),s.top,sides,0);
                    for(int v=v0;v<far.positions.Count;v++)
                    {
                        var p=far.positions[v]+stationOffset;
                        map.Add(-1);data.Add((v,uv1,uv2,new Vector4(p.x,p.y,p.z,s.plane)));
                    }
                }
            }
        }
    }
}
