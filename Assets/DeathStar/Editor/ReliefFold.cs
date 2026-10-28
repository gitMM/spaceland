using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace DeathStar.EditorTools
{
    // The far-range fold of doc/close-up-detail-plan.md: once the armour's plates have sunk into their face (the relief
    // trace's lowering, TileRelief.hlsl), their average effect stays in the face's shading. Measured once, offline, from
    // the full carving layout (a port of ReliefHeight) over many faces, and written to Shaders/TileReliefFold.hlsl:
    //   sun visibility as a curve over the sun's height above the face (the plates' shadows on each other),
    //   cavity occlusion (sky the gaps, grooves and plate sides hide),
    //   the albedo share of the seams and grooves seen from above,
    //   the spread of the plates' side normals, as roughness.
    public static class ReliefFold
    {
        const float Gap=0.014f,Cut=0.03f,Zone=8f,Top=0.26f;
        static readonly Vector3 ZoneOffset=new Vector3(3.1f,1.7f,5.9f);
        public static readonly float[] Elevations={2,5,10,15,20,30,45,60,75,90};

        static float H01(int a,int b,int c,int k)
        {
            uint h=(uint)a*73856093u^(uint)b*19349663u^(uint)c*83492791u^(uint)k*2654435761u;
            h^=h>>16;h*=0x7feb352dU;h^=h>>15;h*=0x846ca68bU;h^=h>>16;
            return (h&0xFFFFFF)/16777216f;
        }
        static bool Edge(int k,int row,int key,bool calm)=>((k%4)+4)%4==0||H01(k,row,key,0)<(calm?0.2f:0.45f);

        // ReliefHeight (TileRelief.hlsl) for a face whose courses run along x and stack along y, normal z; key as the
        // shader decodes it. Kept line for line with the HLSL.
        public static float Height(Vector3 s,int key)
        {
            const int ax=2,ua=0,va=1;
            float u=s[ua],v=s[va];
            var zone=new Vector3(Mathf.Floor((s.x+ZoneOffset.x)/Zone),Mathf.Floor((s.y+ZoneOffset.y)/Zone),Mathf.Floor((s.z+ZoneOffset.z)/Zone));
            bool calm=H01((int)zone.x,(int)zone.y,(int)zone.z,77)<0.4f;
            float sc=calm?2:1,rowH=0.62f*sc,cellW=0.31f*sc;
            float zx0=zone[ua]*Zone-ZoneOffset[ua],zy0=zone[va]*Zone-ZoneOffset[va];
            if(u<zx0+Gap||u>zx0+Zone-Gap||v<zy0+Gap||v>zy0+Zone-Gap)return 0;
            int zkey=key*2+(calm?1:0);
            int row=Mathf.FloorToInt(v/rowH);
            float r0=row*rowH,stag=row*0.47f*sc;
            int k=Mathf.FloorToInt((u+stag)/cellW);
            for(int i=0;i<4&&!Edge(k,row,zkey,calm);i++)k--;
            int k2=k+1;
            for(int j=0;j<4&&!Edge(k2,row,zkey,calm);j++)k2++;
            var r=new Vector4(k*cellW-stag+Gap,r0+Gap,k2*cellW-stag-Gap,r0+rowH-Gap);
            if(u<r.x||u>r.z||v<r.y||v>r.w)return 0;
            float w=r.z-r.x,hgt=r.w-r.y;
            float t=H01(k,row,zkey,1),h=0.04f+0.03f*Mathf.Floor(H01(k,row,zkey,2)*3);
            var odds=calm?new Vector4(0.58f,0.70f,0.82f,0.90f):new Vector4(0.30f,0.46f,0.56f,0.60f);
            float odds4=calm?0.95f:0.80f;
            var p=new Vector2(u,v);
            if(t<odds.x)return h;
            if(t<odds.y)
            {
                float c=Mathf.Min(Mathf.Lerp(0.12f,0.26f,H01(k,row,zkey,3))*sc,Mathf.Min(w,hgt)*0.45f);
                int corner=((int)(H01(k,row,zkey,4)*4))&3;
                var at=corner==0?new Vector2(r.x,r.y):corner==1?new Vector2(r.z,r.y):corner==2?new Vector2(r.z,r.w):new Vector2(r.x,r.w);
                return Mathf.Abs(p.x-at.x)+Mathf.Abs(p.y-at.y)<c?0:h;
            }
            if(t<odds.w)
            {
                float thickness=w*hgt/(w+hgt);
                if(thickness<=0.27f)return h;
                bool hatchy=t>=odds.z,hatch=hatchy&&Mathf.Min(w,hgt)>=0.5f&&Mathf.Max(w,hgt)>=0.8f;
                float small=hatchy?Mathf.Min(w,hgt):thickness*1.4f;
                float fw=Mathf.Clamp(small*0.12f,0.05f,0.12f),top=h+0.03f;
                float edge=Mathf.Min(Mathf.Min(p.x-r.x,r.z-p.x),Mathf.Min(p.y-r.y,r.w-p.y));
                if(edge<fw)return top;
                if(edge<fw+Gap)return 0;
                return hatch?0.03f:0.012f;
            }
            bool turn=hgt>w;
            var q=turn?new Vector2(p.y,p.x):p;
            var rr=turn?new Vector4(r.y,r.x,r.w,r.z):r;
            float rw=rr.z-rr.x,rh=rr.w-rr.y;
            if(t<odds4)
            {
                if(rw<=rh+0.22f*sc)return h;
                bool first=H01(k,row,zkey,7)<0.5f;float lo=h+0.05f,hi=h+0.12f;
                float a=Mathf.Lerp(rr.x+0.1f*sc,rr.z-rh-0.1f*sc,H01(k,row,zkey,5));
                bool rising=H01(k,row,zkey,6)<0.5f;
                float d=rising?((q.x-a)-(q.y-rr.y))*0.7071f:((q.x-a)-(rr.w-q.y))*0.7071f;
                if(Mathf.Abs(d)<Cut)return 0;
                return d<0?(first?lo:hi):(first?hi:lo);
            }
            {
                if(rw<=0.6f*sc||rh<0.26f*sc+2*Cut)return h;
                bool first=H01(k,row,zkey,10)<0.5f;float lo=h+0.05f,hi=h+0.12f;
                float ya=Mathf.Lerp(rr.y+0.13f*sc,rr.w-0.13f*sc,H01(k,row,zkey,8));
                float yb=ya<(rr.y+rr.w)*0.5f?Mathf.Min(ya+0.24f*sc,rr.w-0.1f*sc):Mathf.Max(ya-0.24f*sc,rr.y+0.1f*sc);
                float run=Mathf.Abs(yb-ya),xa=Mathf.Lerp(rr.x+0.1f*sc,rr.z-0.1f*sc-run,H01(k,row,zkey,9)),xb=xa+run;
                float jog=q.x<=xa?ya:q.x>=xb?yb:ya+(q.x-xa)*(yb-ya)/Mathf.Max(run,1e-4f);
                float d=q.y-jog;
                if(Mathf.Abs(d)<Cut)return 0;
                return d<0?(first?lo:hi):(first?hi:lo);
            }
        }

        // Whether anything stands above the ray from p (x, y on the face at plane offset c, z the height over it) along
        // dir within the slab.
        static bool Blocked(Vector3 p,float c,Vector3 dir,int key)
        {
            if(dir.z<=1e-4f)return true;
            float reach=Mathf.Min((Top-p.z)/dir.z,4f);int steps=Mathf.CeilToInt(reach/0.02f);
            for(int i=1;i<=steps;i++)
            {
                var q=p+dir*(reach*i/steps);
                if(Height(new Vector3(q.x,q.y,c),key)>q.z+0.002f)return true;
            }
            return false;
        }

        // Measures the fold over random points on faces with random layouts, and writes the include.
        public static string Measure(string path,int points=1500)
        {
            var random=new System.Random(1977);
            float R()=>(float)random.NextDouble();
            var sun=new double[Elevations.Length];double cavity=0,albedo=0,spread=0;
            for(int i=0;i<points;i++)
            {
                int key=random.Next(-40000,40000);
                var s=new Vector3(R()*400-200,R()*400-200,R()*400-200);
                float h=Height(s,key);var p=new Vector3(s.x,s.y,h+0.001f);float c=s.z;
                // seams and grooves: the face itself showing, darkened as the trace darkens it
                albedo+=h<=0?0.78:1;
                // sun visibility at each elevation, over a random azimuth
                for(int e=0;e<Elevations.Length;e++)
                {
                    float el=Elevations[e]*Mathf.Deg2Rad,az=R()*Mathf.PI*2;
                    var dir=new Vector3(Mathf.Cos(el)*Mathf.Cos(az),Mathf.Cos(el)*Mathf.Sin(az),Mathf.Sin(el));
                    if(!Blocked(p,c,dir,key))sun[e]+=1;
                }
                // cavity: cosine-weighted sky over 8 directions
                int open=0;
                for(int k=0;k<8;k++)
                {
                    float r1=R(),r2=R()*Mathf.PI*2,sr=Mathf.Sqrt(r1);
                    var dir=new Vector3(sr*Mathf.Cos(r2),sr*Mathf.Sin(r2),Mathf.Sqrt(1-r1));
                    if(!Blocked(p,c,dir,key))open++;
                }
                cavity+=open/8.0;
                // side walls seen at 45 degrees: a step in height within a centimetre along a random direction
                float a=R()*Mathf.PI*2;var off=new Vector3(Mathf.Cos(a),Mathf.Sin(a),0)*0.01f;
                if(Mathf.Abs(Height(s+off,key)-h)>0.01f)spread+=1;
            }
            var text=new StringBuilder();
            var inv=CultureInfo.InvariantCulture;
            text.AppendLine("// Generated by DeathStar.EditorTools.ReliefFold (tools/tile-lab.sh closeup part=fold): the armour carving's average");
            text.AppendLine("// effect, measured over "+points+" points of random faces, for TileSurface to keep once the plates have sunk.");
            text.AppendLine("#ifndef DEATHSTAR_TILE_RELIEF_FOLD_INCLUDED");
            text.AppendLine("#define DEATHSTAR_TILE_RELIEF_FOLD_INCLUDED");
            text.Append("static const float ReliefFoldElevation["+Elevations.Length+"] = {");
            for(int e=0;e<Elevations.Length;e++)text.Append((e>0?", ":"")+Elevations[e].ToString("0",inv));
            text.AppendLine("};   // degrees");
            text.Append("static const float ReliefFoldSun["+Elevations.Length+"] = {");
            for(int e=0;e<Elevations.Length;e++)text.Append((e>0?", ":"")+(sun[e]/points).ToString("0.000",inv));
            text.AppendLine("};   // sun visibility");
            text.AppendLine("static const float ReliefFoldCavity = "+(cavity/points).ToString("0.000",inv)+";");
            text.AppendLine("static const float ReliefFoldAlbedo = "+(albedo/points).ToString("0.000",inv)+";");
            text.AppendLine("static const float ReliefFoldSpread = "+(spread/points).ToString("0.000",inv)+";   // share of steps: roughness");
            text.AppendLine("float ReliefFoldSunAt(float degrees)");
            text.AppendLine("{");
            text.AppendLine("    if (degrees <= ReliefFoldElevation[0]) return ReliefFoldSun[0] * saturate(degrees / ReliefFoldElevation[0]);");
            text.AppendLine("    [unroll] for (int i = 1; i < "+Elevations.Length+"; i++)");
            text.AppendLine("        if (degrees <= ReliefFoldElevation[i])");
            text.AppendLine("            return lerp(ReliefFoldSun[i - 1], ReliefFoldSun[i], (degrees - ReliefFoldElevation[i - 1]) / (ReliefFoldElevation[i] - ReliefFoldElevation[i - 1]));");
            text.AppendLine("    return ReliefFoldSun["+(Elevations.Length-1)+"];");
            text.AppendLine("}");
            text.AppendLine("#endif");
            File.WriteAllText(path,text.ToString().Replace("\r\n","\n"));
            var summary=new StringBuilder("sun");
            for(int e=0;e<Elevations.Length;e++)summary.Append($" {Elevations[e]:0}°:{sun[e]/points:0.00}");
            summary.Append($"; cavity {cavity/points:0.00}; albedo {albedo/points:0.00}; steps {spread/points:0.00}");
            return summary.ToString();
        }
    }
}
