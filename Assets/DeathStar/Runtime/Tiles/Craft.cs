using UnityEngine;

namespace DeathStar
{
    // Small craft for judging scale, not tiles: a two-seat pod that docks on a single landing pad and a shuttle for
    // a 2 x 2 pad or a hangar mouth. Forward +Z, up +Y, belly at y=0 with a docking hatch ring under the centre, so
    // a craft set on a pad sits over the pad's airlock.
    public static class Craft
    {
        static readonly Finish Shell=new Finish(TileSlot.Trim,TilePattern.Plain,new Color(1.3f,1.3f,1.32f),0f);
        static readonly Finish Panel=new Finish(TileSlot.Paint,TilePattern.Plain,new Color(0.9f,0.9f,0.92f),0f);
        static readonly Finish Dark=new Finish(TileSlot.Dark,TilePattern.Plain,Color.white,0f);
        static readonly Finish Stripe=new Finish(TileSlot.Accent,TilePattern.Plain,new Color(0.62f,0.16f,0.08f),0f);

        // Hexahedron with a bottom (y0, x0..x1, z zb0..zb1) and a narrower top (y1, xt0..xt1, z zt0..zt1).
        static Vector3[] Wedge(float x0,float x1,float y0,float y1,float zb0,float zb1,float zt0,float zt1,float xt0,float xt1)
        {
            var g=new Vector3[8];
            for(int i=0;i<8;i++)
            {
                bool right=(i&1)!=0,top=(i&2)!=0,front=(i&4)!=0;
                g[i]=new Vector3(top?(right?xt1:xt0):(right?x1:x0),top?y1:y0,top?(front?zt1:zt0):(front?zb1:zb0));
            }
            return g;
        }
        static void Pane(TileMesh m,Vector3 center,Vector3 outward,Vector3 up,float w,float h)
        {
            m.Push(center,Quaternion.LookRotation(-outward,up));m.Glass(new Rect(-w*0.5f,-h*0.5f,w,h),-0.015f);m.Pop();
        }
        static void HatchRing(TileMesh m,float r,float height)=>
            m.Lathe(Dark,Vector3.zero,Vector3.up,new[]{new Vector2(r+0.18f,0),new Vector2(r+0.18f,height),new Vector2(r,height),new Vector2(r,0)},20,false);

        // About 6 m long, 2.6 m wide: a sloped-nose cabin with a window, an engine block with two nozzles, skids.
        public static TileMesh Pod()
        {
            var m=new TileMesh();
            m.Hexa(Shell,Wedge(-1.3f,1.3f,0.45f,1.95f,-2.3f,1.8f,-2.0f,0.6f,-1.0f,1.0f));
            var n=new Vector3(0,1.2f,1.5f).normalized;var up=new Vector3(0,1.5f,-1.2f).normalized;
            Pane(m,new Vector3(0,1.25f,1.18f)+n*0.01f,n,up,1.3f,0.75f);
            m.Box(Panel,new Vector3(0,2.05f,-0.8f),new Vector3(1.4f,0.2f,2.0f),0.05f);
            m.Box(Stripe,new Vector3(0,1.2f,-0.6f),new Vector3(2.36f,0.18f,1.6f),0.02f);
            m.Box(Dark,new Vector3(0,1.15f,-2.65f),new Vector3(2.0f,1.1f,0.7f),0.08f);
            foreach(float x in new[]{-0.55f,0.55f})m.Cylinder(Dark,new Vector3(x,1.15f,-3.0f),new Vector3(x,1.15f,-3.45f),0.36f,14,true,false,0.03f);
            foreach(float x in new[]{-1.0f,1.0f})
            {
                m.Box(Dark,new Vector3(x,0.06f,-0.2f),new Vector3(0.18f,0.12f,3.4f),0.03f);
                foreach(float z in new[]{-1.2f,0.9f})m.Box(Dark,new Vector3(x*0.95f,0.3f,z),new Vector3(0.12f,0.42f,0.12f),0.02f);
            }
            HatchRing(m,0.62f,0.45f);
            return m;
        }

        // About 11 m long with a 10 m span: sloped-nose hull, cockpit window, wings angled down, tail fin, three
        // nozzles, three legs.
        public static TileMesh Shuttle()
        {
            var m=new TileMesh();
            m.Hexa(Shell,Wedge(-1.8f,1.8f,0.9f,3.2f,-5.0f,4.2f,-4.5f,2.2f,-1.35f,1.35f));
            var n=new Vector3(0,2.0f,2.3f).normalized;var up=new Vector3(0,2.3f,-2.0f).normalized;
            Pane(m,new Vector3(0,2.15f,3.12f)+n*0.01f,n,up,2.0f,0.9f);
            foreach(float sgn in new[]{-1f,1f})
            {
                var g=new Vector3[8];
                for(int i=0;i<8;i++)
                {
                    bool tip=(i&1)!=0,top=(i&2)!=0,front=(i&4)!=0;
                    g[i]=new Vector3(sgn*(tip?5.0f:1.6f),tip?(top?0.95f:0.7f):(top?1.75f:1.3f),tip?(front?-1.6f:-3.6f):(front?1.0f:-3.2f));
                }
                m.Hexa(Panel,g);
                m.Box(Dark,new Vector3(sgn*5.0f,0.82f,-2.6f),new Vector3(0.3f,0.5f,2.4f),0.04f);
                m.Box(Stripe,new Vector3(sgn*1.58f,2.1f,0.4f),new Vector3(0.06f,0.22f,2.4f),0.01f);
            }
            m.Box(Dark,new Vector3(0,1.9f,-5.4f),new Vector3(3.0f,1.6f,1.0f),0.1f);
            foreach(float x in new[]{-0.9f,0f,0.9f})m.Cylinder(Dark,new Vector3(x,1.9f,-5.9f),new Vector3(x,1.9f,-6.4f),0.42f,14,true,false,0.03f);
            m.Box(Panel,new Vector3(0,3.35f,-1.5f),new Vector3(1.6f,0.3f,4.5f),0.06f);
            m.Hexa(Panel,Wedge(-0.1f,0.1f,3.45f,5.2f,-4.6f,-1.8f,-4.9f,-3.7f,-0.07f,0.07f));
            foreach(var leg in new[]{new Vector3(0,0,2.6f),new Vector3(-1.3f,0,-3.0f),new Vector3(1.3f,0,-3.0f)})
            {
                m.Cylinder(Dark,leg+Vector3.up*0.12f,leg+Vector3.up*0.95f,0.12f,10,false,false);
                m.Box(Dark,leg+Vector3.up*0.06f,new Vector3(0.55f,0.12f,0.55f),0.03f);
            }
            HatchRing(m,0.75f,0.9f);
            return m;
        }
    }
}
