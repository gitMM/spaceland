using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    // Greybox interiors (tools/tile-lab.sh interior). Flat: the homes in the trench's far wall and the corridor bridging
    // the trench into them, built round a spot inside (StationInteriors) and shot from inside at eye height standing on
    // different walls (magnetic boots, no up), and from outside with and without them. Curved: the Foundry example's two
    // rows of homes and the junction that enters them, bent onto the sphere grid, shot the same way, and the Station
    // example's homes and corridors on sheared cells checked. Checks: every interior builds, its build time, that nothing
    // inside covers a window pane or porthole, and that rays from inside a room meet its walls, its glass or the rooms
    // and tubes it opens into. Writes DeathStarRuns/Tiles/interior/.
    public static partial class TileLab
    {
        static string InteriorDir=>Path.Combine(Dir,"interior");
        const float Eye=1.65f;

        static readonly (string name,TileGallery.View view,string what)[] InteriorViews=
        {
            ("room",TileGallery.Room,"in a home, standing on its deck: two window rows and the hatch to the next home"),
            ("overhead",new TileGallery.View(new Vector3(31.8f,7.4f-Eye,21.9f),new Vector3(27.6f,5.9f,18.2f),Vector3.down,75),
                "the same home walked onto its overhead: the window rows turn over"),
            ("wall",new TileGallery.View(new Vector3(32f,5f,22.4f-Eye),new Vector3(20f,5f,20.5f),new Vector3(0,0,-1),75),
                "standing on the back wall: hatches line up through the row of homes"),
            ("hatch-up",new TileGallery.View(new Vector3(26.4f,2.6f+Eye,21.4f),new Vector3(25f,9.5f,19.6f),Vector3.up,75),
                "from the deck of the home the corridor enters, up through the hatch into the home above"),
            ("socket",new TileGallery.View(new Vector3(26.4f,2.6f+Eye,21.6f),new Vector3(25f,5f,17.5f),Vector3.up,75),
                "the corridor's tube opening through the home's socket"),
            ("corridor",new TileGallery.View(new Vector3(25f,3.8f+Eye,8.2f),new Vector3(25f,5.2f,20f),Vector3.up,75),
                "down the corridor tube into the home: portholes either side, lamps in the corners"),
            ("corridor-back",new TileGallery.View(new Vector3(25f,3.8f+Eye,16.3f),new Vector3(25f,5.2f,6f),Vector3.up,75),
                "back along the tube to the sealed bulkhead against the hull"),
            ("outside",new TileGallery.View(new Vector3(37f,6.2f,8.5f),new Vector3(28f,5.6f,17.5f),Vector3.up,50),
                "outside, interiors built: the windows show the real rooms"),
            ("tube",new TileGallery.View(new Vector3(30.5f,6.8f,9.5f),new Vector3(25f,5f,11f),Vector3.up,50),
                "the corridor from outside: the portholes look into the tube"),
        };

        static string InteriorCheck(Rig rig)
        {
            var total=Stopwatch.StartNew();
            Directory.CreateDirectory(InteriorDir);
            foreach(var f in Directory.GetFiles(InteriorDir,"*.png"))File.Delete(f);
            var md=new StringBuilder();var checks=new List<(bool ok,string text)>();var shots=new List<(string file,string what)>();var notes=new List<string>();
            var stage=rig.Stage;var interiors=stage.interiors;
            if(interiors==null)return "ERROR the gallery stage has no interiors";
            md.AppendLine("# Greybox interiors");md.AppendLine();
            md.AppendLine($"Rooms are one open cube with no up (magnetic boots): inner walls {TileBuilder.Shell} m in from an exposed face, "+
                $"{TileBuilder.Partition} m from a shared one; hatches {2*TileBuilder.HatchRadius:0.0} m across, the corridor tube {2*TileBuilder.TubeInner:0.0} m across its flats.");
            md.AppendLine();

            // Flat stage.
            BuildTimes("gallery",new TileBuilder(stage.structure),null,stage.structure,checks,md);
            var centre=new Vector3(28f,5f,16f);
            interiors.ShowAround(centre,11f);
            checks.Add((interiors.Count>=8,$"gallery: rooms round the spot inside are built ({interiors.Count} in {interiors.BuildMs:0} ms)"));
            RoomChecks("gallery",interiors,stage.structure,c=>TileStructure.Center(c),(c,d)=>(TileStructure.Center(c)+(Vector3)d*TileBuilder.Half,(Vector3)d),400,checks,notes);

            // People for scale: one on a home's deck, one on its overhead, one in the corridor.
            var figures=new GameObject("Interior figures");SceneManager.MoveGameObjectToScene(figures,rig.PreviewScene);
            var meshes=new List<Mesh>();
            void Person(int seed,Vector3 feet,Vector3 up,Vector3 facing)
            {
                var mesh=TileBuilder.Figure(seed).ToMesh("Figure");meshes.Add(mesh);
                var go=new GameObject("Person");go.transform.SetParent(figures.transform,false);
                go.transform.SetPositionAndRotation(feet,Quaternion.LookRotation(Vector3.ProjectOnPlane(facing,up),up));
                go.AddComponent<MeshFilter>().sharedMesh=mesh;go.AddComponent<MeshRenderer>().sharedMaterials=rig.Library.materials;
            }
            // (the people round the trench go before the curved examples, which bring their own)
            void Clear(){Object.DestroyImmediate(figures);figures=new GameObject("Interior figures");SceneManager.MoveGameObjectToScene(figures,rig.PreviewScene);}
            void Shot(string name,TileGallery.View view,string what,bool exposure=false)
            {
                var t=rig.Shoot(view,W,H);Save(t,Path.Combine(InteriorDir,name+".png"));shots.Add((name+".png",what));
                if(exposure)
                {
                    float mean=Mean(t);
                    checks.Add((mean>0.03f&&mean<0.6f,$"the {name} picture is neither black nor blown out (mean {mean:0.000})"));
                }
                Object.DestroyImmediate(t);
            }
            try
            {
                Person(0,new Vector3(29.4f,2.6f,19.4f),Vector3.up,new Vector3(1,0,0.6f));
                Person(1,new Vector3(29.0f,7.4f,21.2f),Vector3.down,new Vector3(1,0,-0.4f));
                Person(1,new Vector3(25.3f,3.8f,13.2f),Vector3.up,new Vector3(0,0,-1));
                foreach(var (name,view,what) in InteriorViews)Shot(name,view,what,name=="room");
                interiors.Clear();
                var fake=InteriorViews.First(v=>v.name=="outside");
                Shot("outside-fake",fake.view,"the same, interiors dropped: the windows' fake rooms (Tile Glass)");
            }
            finally{interiors.Clear();Clear();}

            // Curved grid: the Foundry example for pictures and checks, the Station example (sheared cells round the
            // cube's edges and corners) for checks.
            bool shown=rig.ShowStage(false);
            try
            {
                CurvedInteriors(rig,"Foundry",SettlementExamples.Foundry(),true,checks,notes,md,(n,v,w,e)=>Shot("foundry-"+n,v,w,e),Person);
                CurvedInteriors(rig,"Station",SettlementExamples.Station(),false,checks,notes,md,null,null);
            }
            finally{rig.ShowStage(shown);Object.DestroyImmediate(figures);foreach(var m in meshes)Object.DestroyImmediate(m);}

            md.AppendLine("## Pictures");md.AppendLine();
            foreach(var (file,what) in shots)md.AppendLine($"- `{file}`: {what}");
            md.AppendLine();md.AppendLine("## Checks");md.AppendLine();
            foreach(var (ok,text) in checks)md.AppendLine($"- {(ok?"PASS":"FAIL")} {text}");
            if(notes.Count>0){md.AppendLine();foreach(var n in notes)md.AppendLine("- "+n);}
            File.WriteAllText(Path.Combine(InteriorDir,"report.md"),md.ToString());
            int fails=checks.Count(c=>!c.ok);
            string summary=$"Interior lab: {checks.Count-fails}/{checks.Count} checks pass, {shots.Count} pictures in {InteriorDir} ({total.Elapsed.TotalSeconds:0.0} s)";
            if(fails>0)summary+="\n"+string.Join("\n",checks.Where(c=>!c.ok).Select(c=>"FAIL "+c.text).Concat(notes));
            return "DONE "+summary;
        }

        // Every interior cell of a structure built, meshed (and bent) on its own: all build, and how long they take.
        static void BuildTimes(string where,TileBuilder builder,CurvedTileMesh warp,TileStructure s,List<(bool ok,string text)> checks,StringBuilder md)
        {
            var times=new List<double>();int tris=0,cells=0,empty=0;
            foreach(var pair in s.cells)
            {
                if(!builder.Interior(pair.Key))continue;
                var sw=Stopwatch.StartNew();var t=builder.BuildInterior(pair.Key);var mesh=t.ToMesh("interior");if(warp!=null)warp.Bend(mesh,pair.Key);
                times.Add(sw.Elapsed.TotalMilliseconds);
                int n=TileGallery.Triangles(mesh);tris+=n;cells++;if(n==0)empty++;
                Object.DestroyImmediate(mesh);
            }
            times.Sort();
            double median=times.Count>0?times[times.Count/2]:0,max=times.Count>0?times[times.Count-1]:0;
            checks.Add((cells>0&&empty==0,$"{where}: every home and corridor with an interior builds it ({cells} cells, {empty} empty)"));
            checks.Add((max<10,$"{where}: an interior builds and meshes in under 10 ms (median {median:0.00} ms, max {max:0.00} ms)"));
            md.AppendLine($"{where}: {cells} interior cells, {tris/Mathf.Max(1,cells)} triangles each on average; build median {median:0.00} ms, max {max:0.00} ms.");
            md.AppendLine();
        }

        // The checks on built interiors. Panes: from each pane of a room's tile straight in, nothing of the interior
        // covers the opening behind it. Closure: a ray from a room's centre meets its own interior or tile within 20 m,
        // or leaves through one of its openings (a disc a little wider than the hatch or tube, at the face's centre:
        // faceOf gives its centre and outward normal); one that does neither has leaked out between walls.
        static void RoomChecks(string where,StationInteriors interiors,TileStructure s,Func<Vector3Int,Vector3> centreOf,Func<Vector3Int,Vector3Int,(Vector3 c,Vector3 n)> faceOf,
            int raysPerRoom,List<(bool ok,string text)> checks,List<string> notes)
        {
            var mine=new Dictionary<Vector3Int,List<Vector3>>();var tileTris=new Dictionary<Vector3Int,List<Vector3>>();int noted=0;
            List<Vector3> TileOf(Vector3Int c)
            {
                if(tileTris.TryGetValue(c,out var l))return l;
                l=new List<Vector3>();var r=interiors.Tile(c);
                if(r!=null&&r.Length>0&&r[0]!=null)AddTriangles(l,r[0].GetComponent<MeshFilter>().sharedMesh,r[0].transform,-1);
                return tileTris[c]=l;
            }
            foreach(var cell in interiors.Cells)
            {
                var go=interiors.Get(cell);if(go==null)continue;
                var list=mine[cell]=new List<Vector3>();AddTriangles(list,go.GetComponent<MeshFilter>().sharedMesh,go.transform,-1);
            }
            int panes=0,hidden=0;
            foreach(var cell in mine.Keys)
            {
                var r=interiors.Tile(cell);if(r==null||r.Length==0)continue;
                var near=r[0];var tile=near.GetComponent<MeshFilter>().sharedMesh;var v=tile.vertices;var nv=tile.normals;var g=tile.GetIndices((int)TileSlot.Glass);
                // each pane is a quad of six indices; their mean is its centre
                for(int i=0;i+5<g.Length;i+=6)
                {
                    var sum=Vector3.zero;for(int j=0;j<6;j++)sum+=v[g[i+j]];
                    var p=near.transform.TransformPoint(sum/6);var n=near.transform.TransformDirection(nv[g[i]]).normalized;panes++;
                    if(FirstHit(mine[cell],p-n*0.01f,-n,0.6f)<float.MaxValue){hidden++;if(noted++<8)notes.Add($"{where}: covered pane in {cell} at {p:F2}");}
                }
            }
            checks.Add((panes>0&&hidden==0,$"{where}: every window pane and porthole opens into its room at its centre ({panes} panes, {hidden} covered inside)"));
            int rays=0,leaks=0,rooms=0,intrusions=0;var rnd=new System.Random(7);var leakCells=new HashSet<Vector3Int>();var intruded=new HashSet<Vector3Int>();
            foreach(var cell in mine.Keys)
            {
                rooms++;
                var own=new List<Vector3>(mine[cell]);own.AddRange(TileOf(cell));
                var portals=new List<(Vector3 c,Vector3 n,float r)>();
                foreach(var d in TileStructure.Directions)
                    if(interiors.Opens(cell,cell+d))
                    {
                        bool tube=s.cells[cell].family==TileFamily.Corridor||s.cells[cell+d].family==TileFamily.Corridor;
                        var (c,n)=faceOf(cell,d);portals.Add((c,n,(tube?TileBuilder.TubeInner:TileBuilder.HatchRadius)*1.1f));
                    }
                var o=centreOf(cell);
                for(int k=0;k<raysPerRoom;k++)
                {
                    var dir=new Vector3((float)rnd.NextDouble()*2-1,(float)rnd.NextDouble()*2-1,(float)rnd.NextDouble()*2-1);
                    if(dir.sqrMagnitude<0.01f||dir.sqrMagnitude>1){k--;continue;}
                    dir.Normalize();rays++;
                    // a corridor's own outside showing in front of its tube's walls (front faces: back faces aren't drawn)
                    float wall=s.cells[cell].family==TileFamily.Corridor?FirstHit(mine[cell],o,dir,20):float.MaxValue;
                    if(wall<float.MaxValue&&FirstHit(TileOf(cell),o,dir,wall-0.05f,true)<float.MaxValue)
                    {intrusions++;intruded.Add(cell);if(noted++<8)notes.Add($"{where}: outside shows inside {cell} towards {dir:F2}");}
                    if(FirstHit(own,o,dir,20)<float.MaxValue)continue;
                    bool through=false;
                    foreach(var (c,n,r) in portals)
                    {
                        float dn=Vector3.Dot(dir,n);if(dn<1e-4f)continue;
                        float t=Vector3.Dot(c-o,n)/dn;if(t>0&&(o+dir*t-c).magnitude<=r)through=true;
                    }
                    if(through)continue;
                    leaks++;leakCells.Add(cell);
                    if(noted++<8)notes.Add($"{where}: escape from {cell} towards {dir:F2}");
                }
            }
            checks.Add((rays>0&&leaks==0,$"{where}: rooms are closed but for their glass, hatches and tubes ({rays} rays from {rooms} cells, {leaks} escape"+
                (leaks>0?" from "+string.Join(", ",leakCells):"")+")"));
            checks.Add((intrusions==0,$"{where}: nothing of a corridor's outside shows inside its tube ({intrusions} rays meet it before a wall"+
                (intrusions>0?", in "+string.Join(", ",intruded):"")+")"));
        }

        // A curved example: built as the Foundry builds it, its interiors timed and checked; with pictures, rooms round
        // a home in its lower row are shot from inside and outside.
        static void CurvedInteriors(Rig rig,string name,BlockConstruction model,bool pictures,List<(bool ok,string text)> checks,List<string> notes,StringBuilder md,
            Action<string,TileGallery.View,string,bool> shot,Action<int,Vector3,Vector3,Vector3> person)
        {
            var root=new GameObject(name+" interiors");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var view=new FoundrySettlement(root.transform,rig.Library.materials,rig.Library.clearGlass);
            try
            {
                view.Rebuild(model);TileLook.AddLights(root.transform);
                var s=view.Structure;var warp=new CurvedTileMesh(model);var builder=new TileBuilder(s,warp);
                BuildTimes(name,builder,warp,s,checks,md);
                Vector3 W(Vector3Int c,Vector3 offset)=>warp.Map(TileStructure.Center(c)+offset);
                Vector3 Normal(Vector3Int c,Vector3 n)
                {
                    var q=TileStructure.Center(c)+n*TileBuilder.Half;int a=n.x!=0?0:n.y!=0?1:2;var u=Vector3.zero;u[(a+1)%3]=0.02f;var v=Vector3.zero;v[(a+2)%3]=0.02f;
                    return Vector3.Cross(warp.Map(q+u)-warp.Map(q-u),warp.Map(q+v)-warp.Map(q-v)).normalized*n[a];
                }
                (Vector3,Vector3) FaceOf(Vector3Int c,Vector3Int d)=>(W(c,(Vector3)d*TileBuilder.Half),Normal(c,d));
                var interiors=view.Interiors;
                if(!pictures)
                {
                    interiors.ShowAround(Vector3.zero,100000);
                    RoomChecks(name,interiors,s,c=>W(c,Vector3.zero),FaceOf,150,checks,notes);
                    int open=0,closed=0;
                    foreach(var pair in s.cells)foreach(var d in TileStructure.Directions)
                        if(builder.Interior(pair.Key)&&builder.Interior(pair.Key+d)){if(builder.Opens(pair.Key,pair.Key+d))open++;else closed++;}
                    md.AppendLine($"{name}: {interiors.Count} interiors; between neighbouring interiors {open/2} openings, {closed/2} closed (a room too small for the opening, or not linked).");
                    md.AppendLine();
                    return;
                }
                // A home with a wall of windows (across its axis, onto open space) and a hatch to another home; a corridor
                // that opens into a home.
                Vector3Int next=default,home=default,window=default,tube=default,entered=default;bool found=false,joined=false;
                foreach(var pair in s.cells)
                {
                    var c=pair.Key;var q=pair.Value;
                    if(!found&&q.family==TileFamily.Habitat&&builder.Interior(c))
                    {
                        Vector3Int w=default,h=default;bool hasW=false,hasH=false;
                        foreach(var d in TileStructure.Directions)
                        {
                            if(!hasW&&d!=q.axis&&d!=-q.axis&&!s.TryGet(c+d,out _)){w=d;hasW=true;}
                            if(!hasH&&builder.Opens(c,c+d)&&s.cells[c+d].family==TileFamily.Habitat){h=c+d;hasH=true;}
                        }
                        if(hasW&&hasH){next=c;window=w;home=h;found=true;}
                    }
                    if(!joined&&q.family==TileFamily.Corridor)
                        foreach(var d in TileStructure.Directions)
                            if(!joined&&s.TryGet(c+d,out var o)&&o.family==TileFamily.Habitat&&builder.Opens(c,c+d)){tube=c;entered=c+d;joined=true;}
                }
                checks.Add((found&&joined,$"{name}: a home with windows and a hatch, and a corridor opening into a home, exist ({next}, {tube} into {entered})"));
                if(!found||!joined)return;
                interiors.ShowAround(W(next,Vector3.zero),12);
                RoomChecks(name,interiors,s,c=>W(c,Vector3.zero),FaceOf,300,checks,notes);
                interiors.ShowAround(W(entered,Vector3.zero),12);

                float Inset(Vector3Int c,Vector3 n)=>s.Solid(c+Vector3Int.RoundToInt(n))?TileBuilder.Partition:TileBuilder.Shell;
                Vector3 Floor(Vector3Int c,Vector3 n)=>W(c,n*TileBuilder.Half)-Normal(c,n)*Inset(c,n);
                // Standing on the wall floor of cell c at eye height, backed off from the wall towards, looking at the
                // centre of the face look.
                TileGallery.View Stand(Vector3Int c,Vector3 floor,Vector3 towards,Vector3 look,float fov=75)
                {
                    var up=-Normal(c,floor);var eye=Floor(c,floor)+up*Eye-Normal(c,towards)*1.1f;
                    return new TileGallery.View(eye,W(c,look*TileBuilder.Half)+up*0.2f,up,fov);
                }
                // A floor for cell c across the direction d.
                Vector3 Across(Vector3Int c,Vector3Int d){var q=s.cells[c];return d!=q.axis&&d!=-q.axis?-(Vector3)q.axis:-(Vector3)q.side;}
                Vector3 deck=-(Vector3)s.cells[next].axis,sideways=(Vector3)(home-next);
                person(0,Floor(next,deck)+Normal(next,window)*1.2f,-Normal(next,deck),-(Vector3)window);
                person(1,Floor(next,-deck)+Normal(next,window)*1.2f-Normal(next,sideways)*0.8f,Normal(next,deck),(Vector3)window);
                shot("room",Stand(next,deck,window,(Vector3)window+sideways*0.6f),"curved: in a home on its deck, its window rows and the hatch to the next home",true);
                shot("overhead",Stand(next,-deck,window,(Vector3)window+sideways*0.6f),"curved: the same home walked onto its overhead",false);
                shot("hatch",Stand(next,deck,sideways,sideways),"curved: through the hatch into the next home",false);
                var into=(Vector3)(entered-tube);var tp=s.cells[tube];var tubeUp=Vector3.Dot(into,tp.axis)==0?(Vector3)tp.axis:(Vector3)tp.side;
                shot("tube",new TileGallery.View(W(tube,-into*0.8f),W(entered,into),(W(tube,tubeUp)-W(tube,Vector3.zero)).normalized,75),
                    "curved: along the corridor's tube into the home it enters",false);
                var back=(Vector3)(tube-entered);
                shot("socket",Stand(entered,Across(entered,tube-entered),back,back),"curved: the tube opening into the home",false);
                var outward=Normal(next,window);var upNext=-Normal(next,deck);
                shot("outside",new TileGallery.View(W(next,(Vector3)window*TileBuilder.Half)+outward*8f+upNext*2.5f+(W(home,Vector3.zero)-W(next,Vector3.zero))*0.5f,
                    W(next,(Vector3)window*TileBuilder.Half),upNext,50),"curved: outside the home's windows, interiors built",false);
            }
            finally{view.Dispose();Object.DestroyImmediate(root);}
        }

        // Appends the world-space triangles of a mesh (one submesh, or all for -1) as vertex triples.
        static void AddTriangles(List<Vector3> into,Mesh mesh,Transform t,int submesh)
        {
            var v=mesh.vertices;var idx=submesh<0?mesh.triangles:mesh.GetIndices(submesh);
            foreach(int i in idx)into.Add(t.TransformPoint(v[i]));
        }
        // Distance along dir (normalized or not: in its units) to the nearest triangle hit within max, or MaxValue.
        // front: only triangles facing the ray (their winding seen clockwise from o, as Unity draws them).
        static float FirstHit(List<Vector3> tris,Vector3 o,Vector3 dir,float max,bool front=false)
        {
            float len=dir.magnitude;if(len<1e-6f)return float.MaxValue;var d=dir/len;float best=float.MaxValue;
            for(int i=0;i<tris.Count;i+=3)
            {
                Vector3 a=tris[i],e1=tris[i+1]-a,e2=tris[i+2]-a,p=Vector3.Cross(d,e2);float det=Vector3.Dot(e1,p);
                if(Mathf.Abs(det)<1e-9f||front&&Vector3.Dot(Vector3.Cross(e1,e2),d)>0)continue;
                float inv=1/det;var s=o-a;float u=Vector3.Dot(s,p)*inv;if(u<0||u>1)continue;
                var q=Vector3.Cross(s,e1);float w=Vector3.Dot(d,q)*inv;if(w<0||u+w>1)continue;
                float t=Vector3.Dot(e2,q)*inv;if(t>1e-4f&&t<=max&&t<best)best=t;
            }
            return best;
        }
    }
}
