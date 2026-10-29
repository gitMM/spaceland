using UnityEngine;

namespace DeathStar
{
    // Family kits for wedge and apex cells, batch B (see TileBuilder.Kits.cs for the frames and dispatch).
    //   Power        crest: a spine of tall radiator fins, alternately tilted, their heights in a wave along the run;
    //                crown: three radiator wings in a Y round a glowing core; gable: a transformer with warning
    //                lights; slope: radiator panels lying on it
    //   LifeSupport  crest: a glazed greenhouse barrel on ribs, domed where the run stops; crown: a faceted dome over a
    //                windowed drum; gable: a round window; slope: squat tanks
    //   Storage      crest: a monorail with clamped containers, some bays empty; crown: three silos under a gantry;
    //                gable: a hangar door; slope: racked crates
    //   Pad          crest: a docking cradle, a pod held on its side over every pair of cells; crown: a mooring mast
    //                with a pod docked nose-in at the top; gable: an approach light bar; slope: hoses and lights
    public sealed partial class TileBuilder
    {
        static readonly Finish Glow=new Finish(TileSlot.Light,TilePattern.Plain,new Color(0.45f,0.8f,1f),0f);
        static readonly Finish Warning=new Finish(TileSlot.Light,TilePattern.Plain,new Color(1f,0.15f,0.08f),0f);
        static readonly Finish[] Containers=
        {
            new Finish(TileSlot.Paint,TilePattern.Panels,new Color(0.85f,0.5f,0.3f),0.3f),
            new Finish(TileSlot.Paint,TilePattern.Panels,new Color(0.45f,0.62f,0.66f),0.3f),
            new Finish(TileSlot.Paint,TilePattern.Panels,new Color(0.82f,0.8f,0.74f),0.3f),
        };

        // ---------------------------------------------------------------- Power

        static readonly Vector2[] FinBase={new Vector2(-0.7f,-0.35f),new Vector2(0.7f,-0.35f),new Vector2(0.55f,0.3f),new Vector2(-0.55f,0.3f)};
        // Fins near 0.6 m apart on a low spine; heights follow a wave along the run (run: the cell's place on it).
        static void FinSpine(TileMesh m,in Section s0,in Section s1,bool join0,bool join1,int run)
        {
            Loft(m,Armor,s0,s1,FinBase,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));int fins=Mathf.Max(2,Mathf.RoundToInt(length/0.6f));
            for(int k=0;k<fins;k++)
            {
                float t=(k+0.5f)/fins,h=1.5f+0.6f*Mathf.Sin((run+t)*1.7f);
                m.PushMatrix(FrameAt(s0,s1,t));
                m.Push(new Vector3(0,0.3f,0),Quaternion.Euler(0,0,(k&1)==0?10:-10));
                m.Box(Armor,new Vector3(0,h*0.5f,0),new Vector3(1.5f,h,0.09f),0.02f);
                m.Box(Armor,new Vector3(0,h,0),new Vector3(1.6f,0.08f,0.16f),0.02f);
                m.Pop();m.Pop();
            }
        }
        // A transformer box on its insulators, with warning lights, standing on the gable plate.
        static void Transformer(TileMesh m,in Section s,Vector3 outward)
        {
            m.PushMatrix(GableFrame(s,outward));
            m.Box(Armor,new Vector3(0,0.2f,0.45f),new Vector3(1.6f,1.1f,0.9f),0.06f);
            foreach(float x in new[]{-0.5f,0f,0.5f})
            {
                m.Cylinder(Smooth,new Vector3(x,0.75f,0.45f),new Vector3(x,1.25f,0.45f),0.09f,8,false,true,0.01f);
                m.Cylinder(Smooth,new Vector3(x,0.85f,0.45f),new Vector3(x,0.9f,0.45f),0.16f,8,true,true);
            }
            foreach(float x in new[]{-0.72f,0.72f})m.Box(Warning.Mounted(Vector3.forward),new Vector3(x,0.62f,0.92f),new Vector3(0.1f,0.1f,0.06f));
            m.Pop();
        }
        // On a crown: a core with a glowing band, and a tall ribbed radiator wing out along each ridge.
        static void Triskelion(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            var frame=HubFrame(o,up,arms[0]);var inv=frame.inverse;
            m.PushMatrix(frame);
            m.Cylinder(Armor,new Vector3(0,-0.3f,0),new Vector3(0,3.2f,0),0.75f,12,false,true,0.06f);
            m.Cylinder(Glow,new Vector3(0,1.6f,0),new Vector3(0,2.1f,0),0.8f,16,false,false);
            m.Cylinder(Smooth,new Vector3(0,3.2f,0),new Vector3(0,4.2f,0),0.2f,8,false,true,0.02f);
            foreach(var arm in arms)
            {
                var d=inv.MultiplyVector(arm);d.y=0;d.Normalize();
                m.Push(d*1.85f+Vector3.up*1.2f,Quaternion.LookRotation(d,Vector3.up));
                m.Box(Armor,Vector3.zero,new Vector3(0.12f,2.6f,2.3f),0.02f);
                for(int k=0;k<4;k++)m.Box(Armor,new Vector3(0,0,-0.85f+k*0.57f),new Vector3(0.24f,2.7f,0.08f));
                m.Pop();
            }
            m.Pop();
        }
        // Radiator panels lying on a slope, ribbed across.
        static void Radiators(Face f)
        {
            foreach(float u in new[]{-1.45f,0f,1.45f})
            {
                f.Box(Armor,u-0.6f,-2.1f,u+0.6f,2.1f,0,0.12f,0.02f);
                for(int k=0;k<7;k++){float v=-1.8f+k*0.6f;f.Box(Armor,u-0.6f,v-0.04f,u+0.6f,v+0.04f,0.12f,0.2f,0);}
            }
            f.bare.Add(Whole);
        }

        // ---------------------------------------------------------------- Life Support

        static readonly Vector2[] GreenhouseSill={new Vector2(-1.3f,-0.35f),new Vector2(1.3f,-0.35f),new Vector2(1.3f,0.2f),new Vector2(-1.3f,0.2f)};
        // A glazed half-barrel along the ridge on a sill, arched ribs near 1.25 m apart, a half dome where the run stops.
        static void Greenhouse(TileMesh m,in Section s0,in Section s1,bool join0,bool join1)
        {
            const float R=1.15f,lift=0.2f;const int sides=6;
            Loft(m,Armor,s0,s1,GreenhouseSill,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));int bays=Mathf.Max(1,Mathf.RoundToInt(length/1.25f));
            float chord=2*R*Mathf.Sin(Mathf.PI/sides*0.5f);
            for(int k=0;k<bays;k++)
            {
                m.PushMatrix(FrameAt(s0,s1,(k+0.5f)/bays));
                for(int i=0;i<sides;i++)
                {
                    float a=Mathf.PI*(i+0.5f)/sides;var d=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0);
                    Pane(m,d*R*Mathf.Cos(Mathf.PI/sides*0.5f)+Vector3.up*lift,d,Vector3.forward,chord-0.06f,length/bays-0.12f);
                }
                m.Pop();
            }
            var rib=new[]{new Vector2(R-0.04f,-0.07f),new Vector2(R+0.1f,-0.07f),new Vector2(R+0.1f,0.07f),new Vector2(R-0.04f,0.07f)};
            for(int k=join0?1:0;k<=bays;k++)
            {
                m.PushMatrix(FrameAt(s0,s1,k/(float)bays));
                m.Lathe(Armor,Vector3.up*lift,Vector3.forward,rib,sides,false,Vector3.right,Mathf.PI);
                m.Pop();
            }
            var dome=new Vector2[5];for(int i=0;i<5;i++){float a=i/4f*Mathf.PI*0.5f;dome[i]=new Vector2(R*Mathf.Cos(a),R*0.8f*Mathf.Sin(a));}
            if(!join0){m.PushMatrix(FrameAt(s0,s1,0));m.Lathe(Smooth,Vector3.up*lift,Vector3.back,dome,sides,true,Vector3.left,Mathf.PI);m.Pop();}
            if(!join1){m.PushMatrix(FrameAt(s0,s1,1));m.Lathe(Smooth,Vector3.up*lift,Vector3.forward,dome,sides,true,Vector3.right,Mathf.PI);m.Pop();}
        }
        // A round window framed in the gable plate.
        static void RoundWindow(TileMesh m,in Section s,Vector3 outward)
        {
            m.PushMatrix(GableFrame(s,outward));
            m.Lathe(Smooth,new Vector3(0,0.2f,0),Vector3.forward,new[]{new Vector2(0.95f,0),new Vector2(0.95f,0.16f),new Vector2(0.75f,0.16f),new Vector2(0.75f,0)},24,false);
            Pane(m,new Vector3(0,0.2f,0.02f),Vector3.forward,Vector3.up,1.06f,1.06f);
            m.Pop();
        }
        // On a crown: a drum ringed with windows under a faceted dome, a light on top.
        static void Dome(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            m.PushMatrix(HubFrame(o,up,arms[0]));
            const int sides=8;const float R=1.9f;
            m.Lathe(Armor,new Vector3(0,-0.4f,0),Vector3.up,new[]{new Vector2(R,0),new Vector2(R,1.4f)},sides,false);
            for(int i=0;i<sides;i++)
            {
                float a=(i+0.5f)/sides*Mathf.PI*2;var d=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a));
                Pane(m,d*(R*Mathf.Cos(Mathf.PI/sides)+0.02f)+Vector3.up*0.6f,d,Vector3.up,2*R*Mathf.Sin(Mathf.PI/sides)-0.25f,0.6f);
            }
            var dome=new Vector2[4];for(int i=0;i<4;i++){float a=i/3f*Mathf.PI*0.5f;dome[i]=new Vector2(R*Mathf.Cos(a),1.0f+R*0.85f*Mathf.Sin(a));}
            m.Lathe(Panel,Vector3.zero,Vector3.up,dome,sides,false);
            m.Cylinder(Glow,new Vector3(0,1.0f+R*0.85f,0),new Vector3(0,1.25f+R*0.85f,0),0.18f,10,false,true);
            m.Pop();
        }
        // Squat tanks standing on a slope.
        static void Tanks(Face f)
        {
            foreach(float u in new[]{-1.2f,1.2f})foreach(float v in new[]{-1.2f,1.2f})
            {
                f.m.Cylinder(Smooth,Face.P(u,v,0),Face.P(u,v,0.7f),0.75f,16,false,true,0.05f);
                f.m.Cylinder(Smooth,Face.P(u,v,0.7f),Face.P(u,v,0.85f),0.3f,10,false,true,0.02f);
            }
            f.bare.Add(Whole);
        }

        // ---------------------------------------------------------------- Storage

        static readonly Vector2[] RailBeam={new Vector2(-0.3f,0.75f),new Vector2(0.3f,0.75f),new Vector2(0.3f,1.0f),new Vector2(-0.3f,1.0f)};
        // A beam on posts along the ridge carrying 2.4 m containers in clamps; a hashed few bays stay empty.
        static void Monorail(TileMesh m,in Section s0,in Section s1,bool join0,bool join1,uint seed)
        {
            Loft(m,Armor,s0,s1,RailBeam,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));int bays=Mathf.Max(1,Mathf.FloorToInt(length/2.7f));
            for(int k=0;k<bays;k++)
            {
                float t=(k+0.5f)/bays;
                m.PushMatrix(FrameAt(s0,s1,t));
                m.Box(Armor,new Vector3(0,0.2f,0),new Vector3(0.3f,1.1f,0.3f),0.03f);
                if(Rand(seed,k)>0.25f)
                {
                    var paint=Containers[(int)(Rand(seed,k+7)*Containers.Length)%Containers.Length];
                    m.Box(paint,new Vector3(0,1.75f,0),new Vector3(1.3f,1.3f,2.4f),0.04f);
                    foreach(float z in new[]{-0.8f,0.8f})m.Box(Armor,new Vector3(0,1.05f,z),new Vector3(1.4f,0.12f,0.2f),0.02f);
                }
                m.Pop();
            }
        }
        // A hangar door across the triangular end: a dark opening big enough for a pod, framed in armour.
        static void HangarDoor(TileMesh m,in Section s,Vector3 outward)
        {
            m.PushMatrix(GableFrame(s,outward));
            m.Box(Armor,new Vector3(0,0.75f,0.05f),new Vector3(3.4f,2.5f,0.25f),0.05f);
            m.Box(Slit,new Vector3(0,0.75f,0.2f),new Vector3(2.8f,2.0f,0.06f));
            for(int k=0;k<5;k++)m.Box(Armor,new Vector3(0,-0.15f+k*0.45f,0.25f),new Vector3(2.8f,0.05f,0.04f));
            m.Pop();
        }
        // On a crown: three silos round the corner under a triangular gantry.
        static void Silos(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            m.PushMatrix(HubFrame(o,up,arms[0]));
            var at=new Vector3[3];
            for(int i=0;i<3;i++)
            {
                float a=(i*120f+60f)*Mathf.Deg2Rad;at[i]=new Vector3(Mathf.Cos(a),0,Mathf.Sin(a))*1.25f;
                m.Cylinder(Smooth,at[i]-Vector3.up*0.4f,at[i]+Vector3.up*3.0f,0.8f,16,false,false,0.04f);
                var cap=new[]{new Vector2(0.8f,0),new Vector2(0.65f,0.3f),new Vector2(0.2f,0.45f),new Vector2(0,0.45f)};
                m.Lathe(Smooth,at[i]+Vector3.up*3.0f,Vector3.up,cap,16,true);
            }
            for(int i=0;i<3;i++)
            {
                Vector3 p=at[i]+Vector3.up*3.6f,q=at[(i+1)%3]+Vector3.up*3.6f;
                m.Push((p+q)*0.5f,Quaternion.LookRotation(q-p,Vector3.up));m.Box(Armor,Vector3.zero,new Vector3(0.18f,0.18f,(q-p).magnitude+0.2f),0.02f);m.Pop();
                m.Box(Armor,at[i]+Vector3.up*3.3f,new Vector3(0.14f,0.6f,0.14f));
            }
            m.Pop();
        }
        // Crates racked on a slope.
        static void Racks(Face f)
        {
            for(int i=0;i<3;i++)for(int j=0;j<3;j++)
            {
                float u=-1.5f+i*1.5f,v=-1.5f+j*1.5f,h=0.5f+0.25f*((i+j)%2);
                f.Box(Armor,u-0.6f,v-0.6f,u+0.6f,v+0.6f,0,h,0.03f);
            }
            f.bare.Add(Whole);
        }

        // ---------------------------------------------------------------- Pad

        // Cradle arms rising from both sides of the ridge, a gangway along it to a hatch on the crest; a pod lies in
        // the cradle over each pair of cells (pod: this cell starts a pair, so the pod sits on the shared end).
        static void Cradle(TileMesh m,in Section s0,in Section s1,bool join0,bool join1,bool pod)
        {
            Loft(m,Armor,s0,s1,FinBase,0,1,!join0,!join1);
            foreach(float t in new[]{0.25f,0.75f})
            {
                m.PushMatrix(FrameAt(s0,s1,t));
                foreach(float side in new[]{-1f,1f})
                {
                    var g=new Vector3[8];
                    for(int i=0;i<8;i++)
                    {
                        bool outer=(i&1)!=0,top=(i&2)!=0,front=(i&4)!=0;
                        float x=top?(outer?1.55f:1.35f):(outer?1.2f:0.9f);
                        g[i]=new Vector3(side*x,top?2.2f:0.1f,front?0.18f:-0.18f);
                    }
                    m.Hexa(Armor,g);
                    m.Box(Smooth,new Vector3(side*1.3f,2.25f,0),new Vector3(0.5f,0.12f,0.5f),0.03f);
                }
                m.Pop();
            }
            m.PushMatrix(FrameAt(s0,s1,0.5f));
            m.Lathe(Smooth,new Vector3(0,0.3f,0),Vector3.up,new[]{new Vector2(0.75f,0),new Vector2(0.75f,0.22f),new Vector2(0.58f,0.22f),new Vector2(0.58f,0)},20,false);
            m.Box(Armor,new Vector3(0,1.0f,0),new Vector3(0.6f,0.08f,1.6f),0.02f);
            m.Pop();
            if(!pod)return;
            m.PushMatrix(FrameAt(s0,s1,1));
            m.Push(new Vector3(0,1.15f,0),Quaternion.identity);Craft.Pod(m);m.Pop();
            m.Pop();
        }
        // An approach light bar across the triangular end.
        static void LightBar(TileMesh m,in Section s,Vector3 outward)
        {
            m.PushMatrix(GableFrame(s,outward));
            m.Box(Armor,new Vector3(0,0.9f,0.12f),new Vector3(3.2f,0.18f,0.22f),0.02f);
            for(int k=0;k<7;k++)m.Box(RunningLight.Mounted(Vector3.forward),new Vector3(-1.35f+k*0.45f,0.9f,0.25f),new Vector3(0.14f,0.1f,0.05f));
            m.Pop();
        }
        // On a crown: a mooring mast with a docking ring at the top and a pod docked nose-in.
        static void MooringMast(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            m.PushMatrix(HubFrame(o,up,arms[0]));
            m.Lathe(Smooth,new Vector3(0,-0.4f,0),Vector3.up,new[]{new Vector2(1.2f,0),new Vector2(1.2f,0.6f),new Vector2(0.5f,1.0f),new Vector2(0,1.0f)},20,false);
            m.Cylinder(Armor,new Vector3(0,0.6f,0),new Vector3(0,5.6f,0),0.28f,10,false,true,0.03f);
            m.Lathe(Smooth,new Vector3(0,5.6f,0),Vector3.up,new[]{new Vector2(1.0f,0),new Vector2(1.0f,0.35f),new Vector2(0.75f,0.35f),new Vector2(0.75f,0)},24,false);
            m.Cylinder(Warning,new Vector3(0,5.95f,0),new Vector3(0,6.05f,0),1.02f,24,false,false);
            // the pod's nose (its +z) down into the ring, its belly towards the hub frame's x
            var podFrame=Frame(new Vector3(0,7.85f,0),Vector3.Cross(Vector3.right,Vector3.down),Vector3.right,Vector3.down);
            m.PushMatrix(podFrame);Craft.Pod(m);m.Pop();
            m.Pop();
        }
        // Service hoses lying across a slope and landing lights along its edge.
        static void Service(Face f)
        {
            foreach(float v in new[]{-0.9f,0.4f})f.m.Cylinder(Smooth,Face.P(-2.1f,v,0.18f),Face.P(2.1f,v+0.6f,0.18f),0.16f,10,true,true,0.02f);
            for(int k=0;k<5;k++)f.Box(RunningLight,-2.0f+k,-2.2f,-1.85f+k,-2.05f,0,0.08f,0);
            f.Box(Armor,-0.5f,1.4f,0.5f,2.1f,0,0.5f,0.04f);
            f.bare.Add(Whole);
        }
    }
}
