using UnityEngine;

namespace DeathStar
{
    // Family kits for wedge and apex cells (doc: Ridges, keels and crowns), batch A. Other families build the Hull kit
    // (TileBuilder.Shapes.cs). Parts are in metres in rigid local frames placed on the cell's real corners: x across
    // the ridge, y up out of the roof, z along the ridge (FrameAt). Runs share sections with same-family neighbours.
    //   Habitat  crest: a glazed A-frame gallery lit from inside; crown: a three-sided glazed lantern on a deck;
    //            gable: a glazed triangular window wall; slope: the deck window rows
    //   Defense  crest: a battlement rail with a point-defence turret on every other cell; crown: a bastion of three
    //            turrets on a stepped triangular base; gable: a heavy gun port; slope: sloped casemates with gun slits
    //   Command  crest: a phased-array spine, sensor panels angled to both sides; crown: a spire with three dishes and a
    //            beacon; gable: an antenna mast with a globe; slope: a bridge window strip
    public sealed partial class TileBuilder
    {
        static readonly Finish Panel=new Finish(TileSlot.Dark);
        static readonly Finish Slit=new Finish(TileSlot.Recess);

        // spine: the lattice axis the run follows, so parts can pace themselves along it (cell[spine]).
        void CrestFor(TileMesh m,TileFamily family,Vector3Int cell,int spine,in Section s0,in Section s1,bool join0,bool join1)
        {
            switch(family)
            {
                case TileFamily.Habitat:Gallery(m,s0,s1,join0,join1);break;
                case TileFamily.Defense:Battlement(m,s0,s1,join0,join1,(cell.x+cell.y+cell.z&1)==0);break;
                case TileFamily.Command:SensorSpine(m,s0,s1,join0,join1);break;
                case TileFamily.Power:FinSpine(m,s0,s1,join0,join1,cell[spine]);break;
                case TileFamily.LifeSupport:Greenhouse(m,s0,s1,join0,join1);break;
                case TileFamily.Storage:Monorail(m,s0,s1,join0,join1,Hash(cell));break;
                case TileFamily.Pad:Cradle(m,s0,s1,join0,join1,(cell[spine]&1)==0&&join1);break;
                default:Crest(m,s0,s1,join0,join1);break;
            }
        }
        static void GableFor(TileMesh m,TileFamily family,in Section s,Vector3 outward)
        {
            switch(family)
            {
                case TileFamily.Habitat:Gable(m,s,outward);GlassGable(m,s,outward);break;
                case TileFamily.Defense:Gable(m,s,outward);GunPort(m,s,outward);break;
                case TileFamily.Command:Gable(m,s,outward);Mast(m,s);break;
                case TileFamily.Power:Gable(m,s,outward);Transformer(m,s,outward);break;
                case TileFamily.LifeSupport:Gable(m,s,outward);RoundWindow(m,s,outward);break;
                case TileFamily.Storage:HangarDoor(m,s,outward);break;
                case TileFamily.Pad:Gable(m,s,outward);LightBar(m,s,outward);break;
                default:Gable(m,s,outward);break;
            }
        }
        static void HubFor(TileMesh m,TileFamily family,Vector3 o,Vector3 up,Vector3[] arms)
        {
            switch(family)
            {
                case TileFamily.Habitat:Lantern(m,o,up,arms);break;
                case TileFamily.Defense:Bastion(m,o,up,arms);break;
                case TileFamily.Command:Spire(m,o,up,arms);break;
                case TileFamily.Power:Triskelion(m,o,up,arms);break;
                case TileFamily.LifeSupport:Dome(m,o,up,arms);break;
                case TileFamily.Storage:Silos(m,o,up,arms);break;
                case TileFamily.Pad:MooringMast(m,o,up,arms);break;
                default:Hub(m,o,up);break;
            }
        }
        // Families whose slopes carry a form of their own (left bare of relief); the rest get a hatch.
        static bool SlopeForm(TileFamily family)=>family!=TileFamily.Hull&&family!=TileFamily.Industrial;
        void SlopeFor(Face f,TileFamily family)
        {
            f.metric=f.Local(0,0);
            switch(family)
            {
                case TileFamily.Habitat:Habitat(f);break;
                case TileFamily.Command:WindowRow(f,HighSill,HighHead);break;
                case TileFamily.Defense:Casemates(f);break;
                case TileFamily.Power:Radiators(f);break;
                case TileFamily.LifeSupport:Tanks(f);break;
                case TileFamily.Storage:Racks(f);break;
                case TileFamily.Pad:Service(f);break;
                default:Hatch(f,0,0,0.6f);break;
            }
        }

        // A rigid frame at t between two sections: x across, y up, z along from s0 towards s1 (right-handed).
        static Matrix4x4 FrameAt(in Section s0,in Section s1,float t)
        {
            var up=Vector3.Lerp(s0.up,s1.up,t).normalized;var x=Vector3.Lerp(s0.across,s1.across,t);x=(x-up*Vector3.Dot(x,up)).normalized;
            var z=Vector3.Cross(x,up);if(Vector3.Dot(z,s1.crest-s0.crest)<0){x=-x;z=-z;}
            return Frame(Vector3.Lerp(s0.crest,s1.crest,t),x,up,z);
        }
        static Matrix4x4 Frame(Vector3 o,Vector3 x,Vector3 y,Vector3 z)
        {
            var f=Matrix4x4.identity;f.SetColumn(0,x);f.SetColumn(1,y);f.SetColumn(2,z);f.SetColumn(3,new Vector4(o.x,o.y,o.z,1));return f;
        }
        // A frame on a crown's hub: y up, x towards the first arm.
        static Matrix4x4 HubFrame(Vector3 o,Vector3 up,Vector3 arm)
        {
            var x=(arm-up*Vector3.Dot(arm,up)).normalized;return Frame(o,x,up,Vector3.Cross(x,up));
        }
        // A frame on a gable: z out of it, y up the roof's cross-section, x across.
        static Matrix4x4 GableFrame(in Section s,Vector3 outward)
        {
            var y=(s.up-outward*Vector3.Dot(s.up,outward)).normalized;var x=Vector3.Cross(y,outward);
            return Frame((s.keel+s.eaveA+s.eaveB)/3+outward*0.12f,x,y,outward);
        }
        static void Pane(TileMesh m,Vector3 center,Vector3 outward,Vector3 up,float w,float h)
        {
            m.Push(center,Quaternion.LookRotation(-outward,up));m.Glass(new Rect(-w*0.5f,-h*0.5f,w,h),-0.015f);m.Pop();
        }

        // ---------------------------------------------------------------- Habitat

        static readonly Vector2[] GallerySill={new Vector2(-1.4f,-0.35f),new Vector2(1.4f,-0.35f),new Vector2(1.4f,0.4f),new Vector2(-1.4f,0.4f)};
        static readonly Vector2[] GalleryGlass={new Vector2(-1.25f,0.4f),new Vector2(1.25f,0.4f),new Vector2(0,1.8f)};
        static readonly Vector2[] GalleryRidge={new Vector2(-0.16f,1.68f),new Vector2(0.16f,1.68f),new Vector2(0.16f,1.92f),new Vector2(-0.16f,1.92f)};
        // A walk along the ridge under a glazed A-frame: a sill band, glazed slopes in bays near 1.25 m with mullions,
        // a ridge beam; closed with an armoured end where the run stops.
        static void Gallery(TileMesh m,in Section s0,in Section s1,bool join0,bool join1)
        {
            Loft(m,Armor,s0,s1,GallerySill,0,1,!join0,!join1);
            Loft(m,Armor,s0,s1,GalleryRidge,0,1,!join0,!join1);
            Loft(m,Armor,s0,s1,GalleryGlass,0,1,!join0,!join1,false);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));int bays=Mathf.Max(1,Mathf.RoundToInt(length/1.25f));
            float slope=new Vector2(1.25f,1.4f).magnitude;
            for(int k=0;k<bays;k++)
            {
                m.PushMatrix(FrameAt(s0,s1,(k+0.5f)/bays));
                foreach(float side in new[]{-1f,1f})
                {
                    var outward=new Vector3(side*1.4f,1.25f,0).normalized;var upSlope=new Vector3(-side*1.25f,1.4f,0).normalized;
                    Pane(m,new Vector3(side*0.625f,1.1f,0)+outward*0.02f,outward,upSlope,length/bays-0.14f,slope-0.2f);
                }
                m.Pop();
            }
            // mullions on bay boundaries; a shared end belongs to the cell before it
            for(int k=join0?1:0;k<=bays;k++)
            {
                m.PushMatrix(FrameAt(s0,s1,k/(float)bays));
                foreach(float side in new[]{-1f,1f})
                {
                    var upSlope=new Vector3(-side*1.25f,1.4f,0).normalized;
                    m.Push(new Vector3(side*0.625f,1.1f,0),Quaternion.LookRotation(Vector3.forward,upSlope));
                    m.Box(Armor,Vector3.zero,new Vector3(0.14f,slope,0.12f),0.02f);m.Pop();
                }
                m.Pop();
            }
        }
        // Glazing in strips near 1.25 m wide inside the armour plate on a triangular end, each strip as tall as the
        // triangle allows.
        static void GlassGable(TileMesh m,in Section s,Vector3 outward)
        {
            var frame=GableFrame(s,outward);var inv=frame.inverse;
            Vector2 L(Vector3 p){var q=inv.MultiplyPoint3x4(p);return new Vector2(q.x,q.y);}
            var g=L((s.keel+s.eaveA+s.eaveB)/3);
            Vector2 In(Vector3 p)=>g+(L(p)-g)*0.72f;
            Vector2 k=In(s.keel),a=In(s.eaveA),b=In(s.eaveB);
            if(a.x>b.x){var t=a;a=b;b=t;}
            float Top(float x)=>Mathf.Lerp(a.y,b.y,Mathf.InverseLerp(a.x,b.x,x));
            float Bottom(float x)=>x<k.x?Mathf.Lerp(k.y,a.y,Mathf.InverseLerp(k.x,a.x,x)):Mathf.Lerp(k.y,b.y,Mathf.InverseLerp(k.x,b.x,x));
            int strips=Mathf.Max(1,Mathf.RoundToInt((b.x-a.x)/1.25f));float w=(b.x-a.x)/strips;
            m.PushMatrix(frame);
            for(int i=0;i<strips;i++)
            {
                float x0=a.x+i*w+0.06f,x1=a.x+(i+1)*w-0.06f;
                float y0=Mathf.Max(Bottom(x0),Bottom(x1))+0.05f,y1=Mathf.Min(Top(x0),Top(x1))-0.05f;
                if(y1-y0<0.4f||x1-x0<0.3f)continue;
                var r=Rect.MinMaxRect(x0,y0,x1,y1);
                m.Push(Vector3.zero,Quaternion.LookRotation(-Vector3.forward,Vector3.up));m.Glass(new Rect(-r.xMax,r.yMin,r.width,r.height),-0.02f);m.Pop();
            }
            m.Pop();
        }
        // On a crown: a deck on a pedestal round a three-sided glazed lantern under a triangular roof.
        static void Lantern(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            m.PushMatrix(HubFrame(o,up,arms[0]));
            // a pedestal down into the crown (its surface falls away fast from the corner) under a 1.75 m deck
            m.Lathe(Smooth,new Vector3(0,-0.9f,0),Vector3.up,new[]{new Vector2(1.2f,0),new Vector2(1.2f,0.8f),new Vector2(1.75f,0.9f),new Vector2(1.75f,1.2f),new Vector2(0,1.2f)},32,false);
            var corners=new Vector3[3];for(int i=0;i<3;i++){float t=(i*120f+60f)*Mathf.Deg2Rad;corners[i]=new Vector3(Mathf.Cos(t),0,Mathf.Sin(t))*1.2f;}
            for(int i=0;i<3;i++)
            {
                Vector3 p=corners[i],q=corners[(i+1)%3],mid=(p+q)*0.5f,outward=new Vector3(mid.x,0,mid.z).normalized;
                Pane(m,mid+Vector3.up*1.25f+outward*0.02f,outward,Vector3.up,(q-p).magnitude-0.3f,1.4f);
                m.Box(Armor,p+Vector3.up*1.25f,new Vector3(0.2f,1.7f,0.2f),0.03f);
            }
            var tri=new Vector2[3];for(int i=0;i<3;i++)tri[i]=new Vector2(corners[i].x,corners[i].z)*1.12f;
            m.Prism(Armor,tri,Vector3.zero,Vector3.right,Vector3.forward,Vector3.up,2.1f,2.35f,true);
            m.Cylinder(RunningLight,new Vector3(0,2.35f,0),new Vector3(0,2.5f,0),0.15f,12,false,true);
            m.Pop();
        }

        // ---------------------------------------------------------------- Defense

        static readonly Vector2[] BattlementBody={new Vector2(-0.85f,-0.35f),new Vector2(0.85f,-0.35f),new Vector2(0.72f,0.45f),new Vector2(-0.72f,0.45f)};
        // A crenellated rail along the ridge, and on every other cell a point-defence turret, its twin barrels across.
        static void Battlement(TileMesh m,in Section s0,in Section s1,bool join0,bool join1,bool turret)
        {
            Loft(m,Armor,s0,s1,BattlementBody,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));int merlons=Mathf.Max(1,Mathf.RoundToInt(length/0.9f));
            for(int k=0;k<merlons;k++)
            {
                m.PushMatrix(FrameAt(s0,s1,(k+0.5f)/merlons));
                foreach(float side in new[]{-1f,1f})m.Box(Armor,new Vector3(side*0.55f,0.62f,0),new Vector3(0.32f,0.36f,Mathf.Min(0.55f,length/merlons*0.6f)),0.03f);
                m.Pop();
            }
            if(!turret)return;
            m.PushMatrix(FrameAt(s0,s1,0.5f));
            Turret(m,new Vector3(0,0.45f,0),Vector3.right,0.8f);
            m.Pop();
        }
        // A point-defence turret standing at foot, its twin barrels aimed along aim (tilted up), scaled by size.
        static void Turret(TileMesh m,Vector3 foot,Vector3 aim,float size)
        {
            m.PushScale(foot,size);
            m.Cylinder(Smooth,foot,foot+Vector3.up*0.35f,0.75f,10,false,true,0.04f);
            var forward=(aim+Vector3.up*0.45f).normalized;
            m.Push(foot+Vector3.up*0.75f,Quaternion.LookRotation(new Vector3(aim.x,0,aim.z).normalized,Vector3.up));
            m.Box(Armor,Vector3.zero,new Vector3(1.0f,0.6f,1.1f),0.06f);
            m.Pop();
            var right=Vector3.Cross(Vector3.up,aim).normalized;
            foreach(float s in new[]{-0.2f,0.2f})
            {
                var root=foot+Vector3.up*0.8f+aim*0.45f+right*s;
                m.Cylinder(Smooth,root,root+forward*1.9f,0.07f,8,false,true,0.01f);
                m.Cylinder(Smooth,root+forward*1.7f,root+forward*1.95f,0.11f,8,true,true,0.01f);
            }
            m.Pop();
        }
        // A heavy gun port on a triangular end: an armoured mantlet and a long barrel out of it.
        static void GunPort(TileMesh m,in Section s,Vector3 outward)
        {
            m.PushMatrix(GableFrame(s,outward));
            m.Box(Armor,new Vector3(0,0.35f,0.35f),new Vector3(1.9f,1.3f,0.7f),0.1f);
            m.Cylinder(Smooth,new Vector3(0,0.35f,0.7f),new Vector3(0,0.35f,1.1f),0.42f,14,false,true,0.03f);
            m.Cylinder(Smooth,new Vector3(0,0.35f,1.1f),new Vector3(0,0.35f,3.6f),0.2f,14,false,true,0.02f);
            m.Cylinder(Smooth,new Vector3(0,0.35f,3.3f),new Vector3(0,0.35f,3.75f),0.3f,14,true,true,0.03f);
            m.Pop();
        }
        // On a crown: a stepped triangular base with a turret at each corner, aimed out between the ridges.
        static void Bastion(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            m.PushMatrix(HubFrame(o,up,arms[0]));
            var big=new Vector2[3];var small=new Vector2[3];
            for(int i=0;i<3;i++){float t=(i*120f+60f)*Mathf.Deg2Rad;var d=new Vector2(Mathf.Cos(t),Mathf.Sin(t));big[i]=d*2.6f;small[i]=d*1.9f;}
            m.Prism(Armor,big,Vector3.zero,Vector3.right,Vector3.forward,Vector3.up,-0.3f,0.5f,true);
            m.Prism(Armor,small,Vector3.zero,Vector3.right,Vector3.forward,Vector3.up,0.5f,1.0f,true);
            for(int i=0;i<3;i++)
            {
                var d=new Vector3(small[i].x,0,small[i].y).normalized;
                Turret(m,d*1.15f+Vector3.up*1.0f,d,0.7f);
            }
            m.Pop();
        }
        // Sloped casemates down a slope, each with a dark gun slit.
        static void Casemates(Face f)
        {
            for(int k=0;k<2;k++)
            {
                float v0=-Half+0.2f+k*2.45f,v1=v0+2.1f,o0=0.9f,o1=0.3f;
                var g=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    bool high=(i&2)!=0,front=(i&4)!=0;
                    g[i]=Face.P((i&1)!=0?2.2f:-2.2f,high?v1:v0,front?(high?o1:o0):0);
                }
                f.m.Hexa(Armor,g);
                f.Box(Slit,-1.4f,v0+0.55f,1.4f,v0+0.72f,o0-0.05f,o0+0.03f,0);
            }
            f.bare.Add(Whole);
        }

        // ---------------------------------------------------------------- Command

        static readonly Vector2[] SensorBody={new Vector2(-0.6f,-0.35f),new Vector2(0.6f,-0.35f),new Vector2(0.4f,0.55f),new Vector2(-0.4f,0.55f)};
        // A spine carrying flat sensor panels tilted out to both sides, near 1.25 m apart.
        static void SensorSpine(TileMesh m,in Section s0,in Section s1,bool join0,bool join1)
        {
            Loft(m,Armor,s0,s1,SensorBody,0,1,!join0,!join1);
            float length=Mathf.Max(0.5f,Vector3.Distance(s0.crest,s1.crest));int panels=Mathf.Max(1,Mathf.RoundToInt(length/1.25f));
            for(int k=0;k<panels;k++)
            {
                m.PushMatrix(FrameAt(s0,s1,(k+0.5f)/panels));
                foreach(float side in new[]{-1f,1f})
                {
                    var normal=new Vector3(side*0.57f,0.82f,0);
                    m.Box(Armor,new Vector3(side*0.42f,0.62f,0),new Vector3(0.18f,0.3f,0.18f),0.02f);
                    m.Push(new Vector3(side*0.85f,0.95f,0),Quaternion.LookRotation(Vector3.forward,normal));
                    m.Box(Panel,Vector3.zero,new Vector3(0.95f,0.06f,Mathf.Min(1.05f,length/panels-0.15f)),0.01f);
                    m.Pop();
                }
                m.Pop();
            }
        }
        // An antenna mast up from the crest at a triangular end, a globe on top.
        static void Mast(TileMesh m,in Section s)
        {
            var foot=s.crest;var up=s.up;
            m.Cylinder(Smooth,foot,foot+up*0.6f,0.3f,12,false,true,0.03f);
            m.Cylinder(Smooth,foot+up*0.6f,foot+up*4.0f,0.08f,8,false,true,0.01f);
            m.Sphere(Smooth,foot+up*4.3f,0.42f,16);
        }
        // On a crown: a tapering spire with three dishes on struts, aimed out along the ridges, and a beacon on top.
        static void Spire(TileMesh m,Vector3 o,Vector3 up,Vector3[] arms)
        {
            m.PushMatrix(HubFrame(o,up,arms[0]));
            m.Lathe(Smooth,new Vector3(0,-0.3f,0),Vector3.up,new[]{new Vector2(1.1f,0),new Vector2(1.1f,0.9f),new Vector2(0.6f,1.5f),new Vector2(0.32f,5.0f),new Vector2(0.12f,5.8f),new Vector2(0,5.9f)},20,false);
            m.Cylinder(RunningLight,new Vector3(0,5.6f,0),new Vector3(0,5.85f,0),0.14f,12,false,true);
            var inv=HubFrame(o,up,arms[0]).inverse;
            foreach(var arm in arms)
            {
                var d=inv.MultiplyVector(arm);d.y=0;d.Normalize();
                var hub=d*1.9f+Vector3.up*2.7f;
                m.Push(d*0.95f+Vector3.up*2.7f,Quaternion.LookRotation(d,Vector3.up));m.Box(Armor,Vector3.zero,new Vector3(0.18f,0.18f,1.5f),0.02f);m.Pop();
                var aim=(d*0.75f+Vector3.up*0.66f).normalized;
                m.Dish(Smooth,hub,aim,0.95f,0.32f,24);
                m.Cylinder(Smooth,hub,hub+aim*0.9f,0.04f,6,false,true);
            }
            m.Pop();
        }
    }
}
