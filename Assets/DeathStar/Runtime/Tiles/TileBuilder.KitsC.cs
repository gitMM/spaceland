using UnityEngine;

namespace DeathStar
{
    // Family kits for wedge and apex cells, batch C (see TileBuilder.Kits.cs for the frames and dispatch).
    //   Industrial  crest: twin crane rails on a bed, a travelling gantry crane on every other cell; crown: a tripod
    //               derrick with lattice legs and a boom; gable: a loading bay door and a jib crane; slope: a conveyor
    //               along the eave with chutes
    //   Hull        slope: armour courses falling from the crest in chevrons, and a maintenance hatch
    public sealed partial class TileBuilder
    {
        static readonly Finish Hazard=new Finish(TileSlot.Paint,TilePattern.Hazard,new Color(0.95f,0.75f,0.25f),0.4f);

        // A beam of thickness w between two points, in the current frame.
        static void Strut(TileMesh m,Vector3 a,Vector3 b,float w,in Finish f)
        {
            var d=b-a;if(d.sqrMagnitude<1e-6f)return;
            var up=Mathf.Abs(Vector3.Dot(d.normalized,Vector3.up))>0.95f?Vector3.forward:Vector3.up;
            m.Push((a+b)*0.5f,Quaternion.LookRotation(d,up));m.Box(f,Vector3.zero,new Vector3(w,w,d.magnitude),0.01f);m.Pop();
        }

        // ---------------------------------------------------------------- Industrial

        static readonly Vector2[] RailBed={new Vector2(-1.25f,-0.35f),new Vector2(1.25f,-0.35f),new Vector2(1.1f,0.1f),new Vector2(-1.1f,0.1f)};
        static readonly Vector2[] RailL={new Vector2(-1.0f,0.1f),new Vector2(-0.84f,0.1f),new Vector2(-0.84f,0.3f),new Vector2(-1.0f,0.3f)};
        static readonly Vector2[] RailR={new Vector2(0.84f,0.1f),new Vector2(1.0f,0.1f),new Vector2(1.0f,0.3f),new Vector2(0.84f,0.3f)};
        // Twin crane rails on a bed along the ridge, and on every other cell (run: its place along it) a gantry crane
        // straddling them, its trolley's hook let down over one side.
        static void CraneRails(TileMesh m,in Section s0,in Section s1,bool join0,bool join1,int run)
        {
            Loft(m,Armor,s0,s1,RailBed,0,1,!join0,!join1);
            Loft(m,Smooth,s0,s1,RailL,0,1,!join0,!join1);Loft(m,Smooth,s0,s1,RailR,0,1,!join0,!join1);
            if((run&1)!=0)return;
            m.PushMatrix(FrameAt(s0,s1,0.5f));
            foreach(float side in new[]{-1f,1f})
            {
                m.Box(Hazard,new Vector3(side*0.92f,0.42f,0),new Vector3(0.36f,0.24f,1.0f),0.03f);
                Strut(m,new Vector3(side*0.92f,0.5f,-0.35f),new Vector3(side*1.4f,2.5f,0),0.2f,Armor);
                Strut(m,new Vector3(side*0.92f,0.5f,0.35f),new Vector3(side*1.4f,2.5f,0),0.2f,Armor);
            }
            m.Box(Hazard,new Vector3(0,2.6f,0),new Vector3(3.3f,0.32f,0.45f),0.03f);
            m.Box(Armor,new Vector3(0.65f,2.3f,0),new Vector3(0.6f,0.35f,0.6f),0.04f);
            m.Cylinder(Smooth,new Vector3(0.65f,2.1f,0),new Vector3(0.65f,1.05f,0),0.03f,6,false,false);
            m.Box(Hazard,new Vector3(0.65f,0.95f,0),new Vector3(0.25f,0.22f,0.25f),0.02f);
            m.Pop();
        }
        // A loading bay door on the triangular end, and a jib crane beside it reaching out over the bay.
        static void LoadingBay(TileMesh m,in Section s,Vector3 outward)
        {
            m.PushMatrix(GableFrame(s,outward));
            m.Box(Armor,new Vector3(-0.4f,0.6f,0.05f),new Vector3(2.4f,2.0f,0.2f),0.04f);
            m.Box(Slit,new Vector3(-0.4f,0.55f,0.17f),new Vector3(2.0f,1.6f,0.05f));
            m.Box(Hazard,new Vector3(-0.4f,1.48f,0.17f),new Vector3(2.0f,0.14f,0.07f));
            m.Cylinder(Armor,new Vector3(1.35f,-0.4f,0.35f),new Vector3(1.35f,2.3f,0.35f),0.12f,8,false,true,0.02f);
            m.Box(Hazard,new Vector3(1.35f,2.25f,1.2f),new Vector3(0.18f,0.2f,2.0f),0.02f);
            Strut(m,new Vector3(1.35f,1.6f,0.4f),new Vector3(1.35f,2.15f,1.5f),0.08f,Armor);
            m.Cylinder(Smooth,new Vector3(1.35f,2.15f,2.0f),new Vector3(1.35f,1.2f,2.0f),0.025f,6,false,false);
            m.Box(Armor,new Vector3(1.35f,1.1f,2.0f),new Vector3(0.22f,0.2f,0.22f),0.02f);
            m.Pop();
        }
        // On a crown: a tripod derrick, lattice legs down to the three seams, a boom out along one of them.
        static void Derrick(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            var frame=HubFrame(o,up,arms[0]);var inv=frame.inverse;
            m.PushMatrix(frame);
            var feet=new Vector3[3];var apex=new Vector3(0,6.2f,0);
            for(int i=0;i<3;i++){var d=inv.MultiplyVector(arms[i]);d.y=0;feet[i]=d.normalized*2.1f;}
            for(int i=0;i<3;i++)
            {
                m.Box(Armor,feet[i]+Vector3.up*0.1f,new Vector3(0.7f,0.5f,0.7f),0.05f);
                Strut(m,feet[i]+Vector3.up*0.3f,apex,0.22f,Armor);
                foreach(float h in new[]{0.3f,0.6f})
                {
                    Vector3 p=Vector3.Lerp(feet[i]+Vector3.up*0.3f,apex,h),q=Vector3.Lerp(feet[(i+1)%3]+Vector3.up*0.3f,apex,h);
                    Strut(m,p,q,0.1f,Armor);
                    Strut(m,p,Vector3.Lerp(feet[(i+1)%3]+Vector3.up*0.3f,apex,h+0.3f),0.07f,Armor);
                }
            }
            m.Box(Hazard,apex,new Vector3(0.6f,0.5f,0.6f),0.05f);
            var tip=apex+feet[0].normalized*3.6f-Vector3.up*0.6f;
            Strut(m,apex,tip,0.18f,Hazard);
            m.Cylinder(Smooth,tip,tip-Vector3.up*2.6f,0.03f,6,false,false);
            m.Box(Armor,tip-Vector3.up*2.75f,new Vector3(0.3f,0.3f,0.3f),0.03f);
            m.Cylinder(RunningLight,apex+Vector3.up*0.25f,apex+Vector3.up*0.45f,0.12f,10,false,true);
            m.Pop();
        }
        // A conveyor along the slope's upper edge, two chutes running down from it.
        static void Conveyor(Face f)
        {
            f.Box(Armor,-2.35f,1.45f,2.35f,2.2f,0,0.42f,0.04f);
            f.Box(Smooth,-2.35f,1.55f,2.35f,2.1f,0.42f,0.48f,0);
            for(int k=0;k<6;k++){float u=-2.0f+k*0.8f;f.Box(Hazard,u-0.05f,1.45f,u+0.05f,2.2f,0.42f,0.5f,0);}
            foreach(float u in new[]{-1.2f,1.2f})
            {
                var g=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    bool right=(i&1)!=0,top=(i&2)!=0,high=(i&4)!=0;
                    g[i]=Face.P(u+(right?0.35f:-0.35f),high?1.45f:-1.9f,top?(high?0.42f:0.12f):0);
                }
                f.m.Hexa(Armor,g);
            }
            f.bare.Add(Whole);
        }

        // ---------------------------------------------------------------- Hull

        // Two armour courses down the slope, each a chevron pointing at the crest, and a hatch below them.
        static void Chevrons(Face f)
        {
            for(int k=0;k<2;k++)
            {
                float v0=-0.7f+k*1.55f,v1=v0+1.15f;
                foreach(float side in new[]{-1f,1f})
                {
                    var g=new Vector3[8];
                    for(int i=0;i<8;i++)
                    {
                        bool outer=(i&1)!=0,top=(i&2)!=0,high=(i&4)!=0;
                        float u=outer?side*2.4f:0,v=(high?v1:v0)-(outer?0.55f:0);
                        g[side>0?i:i^1]=Face.P(u,v,top?0.16f:0);
                    }
                    f.m.Hexa(Armor,g);
                }
            }
            Hatch(f,0,-1.75f,0.5f);
            f.bare.Add(Whole);
        }
    }
}
