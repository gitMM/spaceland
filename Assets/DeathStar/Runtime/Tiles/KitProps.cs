using System.Collections.Generic;
using UnityEngine;

namespace DeathStar
{
    // The small-object kit of doc/close-up-detail-plan.md (layer 3): ten props that stand on hull plates near the
    // player. Each is built from TileMesh primitives with chunky chamfers, so the close-up surface gives it bevels,
    // chips and creases like the tiles (the edge pass stands in for a baked curvature mask). Frame: the plate's normal
    // is +y, the prop's base at y=0, its long side along x, centred on the origin. Three levels: 0 as built, 1 the
    // tiles' coarse far level (fewer sides, no chamfers), 2 a block or two.
    public static class KitProps
    {
        public const int Count=10,Levels=3;
        public static readonly string[] Names={"Vent","Hatch","Junction box","Pipe piece","Cable run","Light","Grab bar","Valve","Fin block","Access panel"};
        static readonly Finish Paint=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.82f,0.82f,0.84f),0f);
        static readonly Finish Dark=new Finish(TileSlot.Dark,TilePattern.Plain,Color.white,0f);
        static readonly Finish Metal=new Finish(TileSlot.Metal,TilePattern.Plain,new Color(0.8f,0.8f,0.8f),0f);
        static readonly Finish Trim=new Finish(TileSlot.Trim,TilePattern.Plain,Color.white,0f);
        static readonly Finish Lamp=new Finish(TileSlot.Light,TilePattern.Plain,new Color(1f,0.78f,0.5f),0f);
        static readonly Finish Warn=new Finish(TileSlot.Accent,TilePattern.Hazard,new Color(0.75f,0.55f,0.1f),0f);

        // Where pipes and cables plug in (KitRoutes): a point and the way out along the plate, in the prop's frame, and
        // whether it takes a pipe (else a cable).
        public static readonly (Vector3 at,Vector3 way,bool pipe)[][] Sockets=
        {
            new[]{(new Vector3(-0.25f,0.03f,0),Vector3.left,false)},                                         // vent
            new[]{(new Vector3(0.3f,0.02f,0),Vector3.right,false)},                                          // hatch
            new[]{(new Vector3(-0.08f,0.08f,-0.2f),Vector3.back,true),(new Vector3(0.08f,0.08f,-0.2f),Vector3.back,false)},  // junction box
            new[]{(new Vector3(-0.4f,0.1f,0),Vector3.left,true),(new Vector3(0.4f,0.1f,0),Vector3.right,true)},              // pipe piece
            new[]{(new Vector3(-0.35f,0.035f,0),Vector3.left,false),(new Vector3(0.35f,0.035f,0),Vector3.right,false)},      // cable run
            new[]{(new Vector3(-0.14f,0.03f,0),Vector3.left,false)},                                         // light
            new (Vector3,Vector3,bool)[0],                                                                   // grab bar
            new[]{(new Vector3(-0.09f,0.05f,0),Vector3.left,true),(new Vector3(0.09f,0.05f,0),Vector3.right,true)},          // valve
            new[]{(new Vector3(0.2f,0.04f,0),Vector3.right,false)},                                          // fin block
            new[]{(new Vector3(-0.23f,0.015f,0),Vector3.left,false)},                                        // access panel
        };

        static Mesh[,] meshes;
        // Footprint (x, z) and height of each prop at level 0, for fitting, folding and colliders.
        public static Vector3[] Size {get;private set;}
        public static Mesh Get(int prop,int level){Build();return meshes[prop,level];}
        static void Build()
        {
            if(meshes!=null&&meshes[0,0]!=null)return;
            meshes=new Mesh[Count,Levels];Size=new Vector3[Count];
            for(int p=0;p<Count;p++)for(int l=0;l<Levels;l++)
            {
                var m=new TileMesh{coarse=l==1?1:0};
                Prop(m,p,l);
                var mesh=m.ToMesh("Kit "+Names[p]+" "+l);mesh.hideFlags=HideFlags.HideAndDontSave;meshes[p,l]=mesh;
                if(l==0)Size[p]=mesh.bounds.size;
            }
        }

        static void Prop(TileMesh m,int prop,int level)
        {
            bool block=level==2;
            switch(prop)
            {
                case 0:   // vent: a framed box with louvre slats
                    m.Box(Paint,new Vector3(0,0.045f,0),new Vector3(0.5f,0.09f,0.34f),0.02f);
                    if(block)break;
                    m.Box(Dark,new Vector3(0,0.092f,0),new Vector3(0.42f,0.006f,0.26f));
                    for(int i=0;i<5;i++)m.Box(Metal,new Vector3(0,0.1f,-0.1f+i*0.05f),new Vector3(0.4f,0.016f,0.022f),0.005f);
                    break;
                case 1:   // hatch: a low plate with a frame, hinges and a handle
                    m.Box(Paint,new Vector3(0,0.02f,0),new Vector3(0.6f,0.04f,0.6f),0.015f);
                    if(block)break;
                    m.Box(Trim,new Vector3(0,0.05f,0),new Vector3(0.5f,0.02f,0.5f),0.01f);
                    foreach(float z in new[]{-0.16f,0.16f})m.Box(Metal,new Vector3(-0.3f,0.04f,z),new Vector3(0.06f,0.05f,0.08f),0.01f);
                    m.Box(Metal,new Vector3(0.18f,0.075f,0),new Vector3(0.03f,0.03f,0.16f),0.008f);
                    break;
                case 2:   // junction box: a box with a lid seam and two cable glands
                    m.Box(Paint,new Vector3(0,0.11f,0),new Vector3(0.36f,0.22f,0.28f),0.02f);
                    if(block)break;
                    m.Box(Paint,new Vector3(0,0.225f,0),new Vector3(0.38f,0.03f,0.3f),0.012f);
                    foreach(float x in new[]{-0.08f,0.08f})m.Cylinder(Dark,new Vector3(x,0.08f,-0.14f),new Vector3(x,0.08f,-0.2f),0.022f,10,true,true,0.004f);
                    m.Box(Warn,new Vector3(0,0.14f,0.141f),new Vector3(0.12f,0.06f,0.004f));
                    break;
                case 3:   // pipe piece: a pipe on two saddles, flanged at both ends
                    if(block){m.Box(Metal,new Vector3(0,0.09f,0),new Vector3(0.8f,0.13f,0.13f));break;}
                    m.Cylinder(Metal,new Vector3(-0.4f,0.1f,0),new Vector3(0.4f,0.1f,0),0.06f,16,true,true,0.006f);
                    foreach(float x in new[]{-0.36f,0.36f})m.Cylinder(Trim,new Vector3(x-0.02f,0.1f,0),new Vector3(x+0.02f,0.1f,0),0.08f,16,true,true,0.006f);
                    foreach(float x in new[]{-0.2f,0.2f})m.Box(Dark,new Vector3(x,0.03f,0),new Vector3(0.06f,0.06f,0.15f),0.01f);
                    break;
                case 4:   // cable run: clamps holding a pair of cables
                    if(block){m.Box(Dark,new Vector3(0,0.03f,0),new Vector3(0.7f,0.05f,0.1f));break;}
                    foreach(float z in new[]{-0.025f,0.025f})m.Cylinder(Dark,new Vector3(-0.35f,0.035f,z),new Vector3(0.35f,0.035f,z),0.018f,10,true,true);
                    foreach(float x in new[]{-0.25f,0f,0.25f})m.Box(Metal,new Vector3(x,0.035f,0),new Vector3(0.04f,0.07f,0.11f),0.008f);
                    break;
                case 5:   // light: a housing with a glowing lens behind a cage
                    m.Box(Paint,new Vector3(0,0.03f,0),new Vector3(0.28f,0.06f,0.14f),0.012f);
                    m.Box(Lamp,new Vector3(0,0.065f,0),new Vector3(0.22f,0.012f,0.09f));
                    if(block)break;
                    foreach(float x in new[]{-0.07f,0f,0.07f})m.Box(Dark,new Vector3(x,0.08f,0),new Vector3(0.012f,0.02f,0.12f),0.003f);
                    break;
                case 6:   // grab bar: two posts and a bar
                    if(block){m.Box(Metal,new Vector3(0,0.06f,0),new Vector3(0.44f,0.1f,0.04f));break;}
                    foreach(float x in new[]{-0.2f,0.2f}){m.Box(Trim,new Vector3(x,0.01f,0),new Vector3(0.06f,0.02f,0.06f),0.006f);m.Cylinder(Metal,new Vector3(x,0.02f,0),new Vector3(x,0.1f,0),0.016f,10,false,true);}
                    m.Cylinder(Metal,new Vector3(-0.21f,0.1f,0),new Vector3(0.21f,0.1f,0),0.016f,10,true,true);
                    break;
                case 7:   // valve: a base, a stem and a hand wheel with spokes
                    m.Box(Paint,new Vector3(0,0.04f,0),new Vector3(0.18f,0.08f,0.18f),0.015f);
                    if(block){m.Box(Metal,new Vector3(0,0.13f,0),new Vector3(0.26f,0.04f,0.26f));break;}
                    m.Cylinder(Metal,new Vector3(0,0.08f,0),new Vector3(0,0.15f,0),0.02f,10,false,true);
                    m.Torus(Warn,new Vector3(0,0.15f,0),Vector3.up,Vector3.right,Mathf.PI*2,0.12f,0.014f,20,8);
                    foreach(float a in new[]{0f,60f,120f}){m.Push(new Vector3(0,0.15f,0),Quaternion.Euler(0,a,0));m.Box(Metal,Vector3.zero,new Vector3(0.24f,0.014f,0.014f));m.Pop();}
                    break;
                case 8:   // fin block: a block with cooling fins
                    m.Box(Dark,new Vector3(0,0.04f,0),new Vector3(0.4f,0.08f,0.26f),0.015f);
                    if(block){m.Box(Metal,new Vector3(0,0.11f,0),new Vector3(0.36f,0.06f,0.22f));break;}
                    for(int i=0;i<6;i++)m.Box(Metal,new Vector3(-0.15f+i*0.06f,0.11f,0),new Vector3(0.014f,0.06f,0.22f),0.004f);
                    break;
                default:  // access panel: a raised plate with an inset door, two buttons and a status light
                    m.Box(Paint,new Vector3(0,0.015f,0),new Vector3(0.46f,0.03f,0.32f),0.012f);
                    if(block)break;
                    m.Box(Trim,new Vector3(-0.04f,0.035f,0),new Vector3(0.28f,0.012f,0.24f),0.006f);
                    foreach(float z in new[]{-0.06f,0.06f})m.Cylinder(Dark,new Vector3(0.16f,0.03f,z),new Vector3(0.16f,0.05f,z),0.018f,10,false,true,0.004f);
                    m.Box(Lamp,new Vector3(0.16f,0.04f,0.12f),new Vector3(0.02f,0.02f,0.02f));
                    break;
            }
        }
    }
}
