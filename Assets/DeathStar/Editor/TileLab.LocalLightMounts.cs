using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    public static partial class TileLab
    {
        // Exercise the meshes produced by the real builders, including all LOD copies and
        // curved placement branches. Keep isolated examples for actual receiver renders.
        static void LocalMountChecks(Rig rig,string dir,List<(bool ok,string text)> checks,List<Object> cleanup)
        {
            var fixtures=new Dictionary<string,(Mesh mesh,int lamp,float separation)>();
            void Audit(string name,TileStructure structure,CurvedTileMesh warp)
            {
                var builder=new TileBuilder(structure,warp);int exterior=0,interior=0,missing=0,backwards=0;
                var failures=new List<string>();
                void Inspect(Mesh mesh,string kind,Vector3 centre,bool candidate)
                {
                    if(!mesh)return;
                    var lamps=StationLocalLights.Extract(mesh,(int)TileSlot.Light);
                    for(int i=0;i<lamps.Length;i++) {
                        var lamp=lamps[i];var n=(Vector3)lamp.plane;
                        if(kind=="interior")interior++;else exterior++;
                        if(n.sqrMagnitude<.99f){missing++;if(failures.Count<8)failures.Add(mesh.name);}
                        if(kind=="interior"&&Vector3.Dot(n,centre)+lamp.plane.w<-.01f)backwards++;
                        if(!candidate||n.sqrMagnitude<.99f)continue;
                        var p=(lamp.a+lamp.b)*.5f;float separation=100;
                        for(int j=0;j<lamps.Length;j++)if(j!=i)separation=Mathf.Min(separation,Vector3.Distance(p,(lamps[j].a+lamps[j].b)*.5f));
                        string key=(warp==null?"flat-":"curved-")+kind;
                        if(!fixtures.TryGetValue(key,out var best)||separation>best.separation) {
                            // Keep the original: cloning a Mesh intentionally doesn't clone CPU metadata.
                            fixtures[key]=(mesh,i,separation);
                        }
                    }
                }
                foreach(var pair in structure.cells) {
                    var source=builder.Build(pair.Key);if(source.VertexCount==0)continue;
                    var (near,far)=source.ToMeshes(name+" "+pair.Value.family+" "+pair.Key);
                    if(warp!=null){warp.Bend(near,pair.Key);if(far)warp.Bend(far,pair.Key,near,source.FarFromNear);if(source.Mid)warp.Bend(source.Mid,pair.Key,near,source.MidFromNear);}
                    var centre=warp==null?Vector3.zero:warp.Map(TileStructure.Center(pair.Key));
                    Inspect(near,"tile",centre,true);Inspect(far,"tile",centre,false);Inspect(source.Mid,"tile",centre,false);
                    cleanup.Add(near);if(far)Object.DestroyImmediate(far);if(source.Mid)Object.DestroyImmediate(source.Mid);
                    if(!builder.Interior(pair.Key))continue;
                    var room=builder.BuildInterior(pair.Key).ToMesh(name+" interior "+pair.Key);if(warp!=null)warp.Bend(room,pair.Key);
                    Inspect(room,"interior",centre,true);cleanup.Add(room);
                }
                checks.Add((missing==0,$"{name}: mounting planes on all {exterior} exterior/LOD and {interior} interior lamps; missing {missing}. "+string.Join(", ",failures)));
                checks.Add((backwards==0,$"{name}: interior planes face into their room/tube; reversed {backwards}."));
            }
            Audit("Gallery",TileStructure.Showcase(new List<TileStructure.Specimen>()),null);
            foreach(var example in Examples){var model=example.model();Audit(example.name,SettlementSolver.Solve(model),new CurvedTileMesh(model));}

            // One object can hold differently mounted lamps. Transform the whole source,
            // including nonuniform scale, and render both sides of a chosen real lens.
            var root=new GameObject("Mount plane receiver test");cleanup.Add(root);SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var sourceObject=new GameObject("Actual generated lamps");sourceObject.transform.SetParent(root.transform,false);
            var filter=sourceObject.AddComponent<MeshFilter>();var renderer=sourceObject.AddComponent<MeshRenderer>();renderer.sharedMaterials=rig.Library.materials;
            renderer.forceRenderingOff=true; // Hide the casing to make the rear-side receiver observable.
            var sourceComponent=sourceObject.AddComponent<StationLocalLightSource>();
            sourceObject.transform.SetPositionAndRotation(new Vector3(2,3,4),Quaternion.Euler(23,47,19));sourceObject.transform.localScale=new Vector3(1.1f,.9f,1.2f);
            var board=new GameObject("Receiver");board.transform.SetParent(root.transform,false);
            var receiver=new TileMesh();receiver.Box(new Finish(TileSlot.Paint,TilePattern.Plain,Color.white,0),Vector3.zero,new Vector3(.6f,.6f,.02f));
            var receiverMesh=receiver.ToMesh("Mount receiver");cleanup.Add(receiverMesh);
            board.AddComponent<MeshFilter>().sharedMesh=receiverMesh;board.AddComponent<MeshRenderer>().sharedMaterials=rig.Library.materials;
            var settings=StationLightingSettings.Current;var images=new List<Texture2D>();
            foreach(var pair in fixtures) {
                filter.sharedMesh=pair.Value.mesh;
                var submitted=new List<StationLocalLights.LightData>();
                StationLocalLights.Append(submitted,filter.sharedMesh,(int)TileSlot.Light,sourceObject.transform.localToWorldMatrix,Color.white);
                var lamp=submitted[pair.Value.lamp];Vector3 n=lamp.plane;
                Vector3 centre=((Vector3)lamp.a+(Vector3)lamp.b)*.5f;
                var mount=centre-n*(Vector3.Dot(centre,n)+lamp.plane.w);
                var side=Vector3.Cross(n,Mathf.Abs(n.y)<.9f?Vector3.up:Vector3.right).normalized;
                var up=Vector3.Cross(n,side);
                float[] illumination=new float[2];
                for(int k=0;k<2;k++) {
                    float sign=k==0?1:-1;
                    float front=Mathf.Max(Vector3.Dot(lamp.a,n),Vector3.Dot(lamp.b,n))+lamp.plane.w+.22f;
                    var p=mount+n*(k==0?front:-.22f);var facing=-n*sign;
                    board.transform.SetPositionAndRotation(p-facing*.01f,Quaternion.LookRotation(facing,up));
                    var view=new TileGallery.View(p+facing*.7f+side*.12f,p,up,45);
                    float Sample(Texture2D image){var uv=rig.Camera.WorldToViewportPoint(p);return image.GetPixel(Mathf.RoundToInt(uv.x*320),Mathf.RoundToInt(uv.y*240)).grayscale;}
                    settings.localLights=false;settings.ApplyGlobals();var off=rig.Shoot(view,320,240,false,1);cleanup.Add(off);float dark=Sample(off);
                    settings.localLights=true;settings.ApplyGlobals();var on=rig.Shoot(view,320,240,false,1);cleanup.Add(on);illumination[k]=Sample(on)-dark;
                    Save(on,Path.Combine(dir,"mount-"+pair.Key+(k==0?"-front":"-behind")+".png"));images.Add(on);
                    if(k==1) {
                        // Negative control: same geometry and normals, only CPU plane metadata absent.
                        var unrestricted=Object.Instantiate(filter.sharedMesh);cleanup.Add(unrestricted);filter.sharedMesh=unrestricted;
                        var leaked=rig.Shoot(view,320,240,false,1);cleanup.Add(leaked);float withoutPlane=Sample(leaked)-dark;
                        checks.Add((withoutPlane>.02f,$"{pair.Key}: removing mounting metadata reproduces rear leakage ({withoutPlane:F5})."));
                        filter.sharedMesh=pair.Value.mesh;
                    }
                }
                checks.Add((illumination[0]>.02f&&Mathf.Abs(illumination[1])<.005f,
                    $"{pair.Key}: actual mesh source, no object plane override, transformed front receives {illumination[0]:F5}, behind receives {illumination[1]:F5}."));
            }
            checks.Add((fixtures.Count==4&&!sourceComponent.clipToMountingPlane,"Rendered flat and curved exterior/interior sources using their per-lamp planes."));
            SaveSheet(images,2,Path.Combine(dir,"mount-comparison.png"));
        }
    }
}
