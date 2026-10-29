using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    public static partial class TileLab
    {
        static void ApplyVoxelLights(Rig rig)
        {
            foreach(var lighting in rig.Stage.root.GetComponentsInChildren<StationLightingRig>(true))lighting.Apply();
        }

        static string VoxelGiCheck(Rig rig)
        {
            string dir=Path.Combine(Dir,"voxel-gi");Directory.CreateDirectory(dir);
            var s=StationLightingSettings.Current;
            string saved=JsonUtility.ToJson(s);
            var md=new StringBuilder("# Voxel GI validation\n\n");
            var failures=new List<string>();
            var cleanup=new List<Object>();
            void Check(bool ok,string message){md.AppendLine((ok?"PASS ":"FAIL ")+message+"\n");if(!ok)failures.Add(message);}
            Texture2D Shot(string name,TileGallery.View view) {
                var t=rig.Shoot(view,960,540);Save(t,Path.Combine(dir,name+".png"));return t;
            }
            try {
                var stage=rig.Stage;s.voxelGi=true;s.habitatTintEnabled=true;s.giDebugView=GiDebugView.Normal;
                ApplyVoxelLights(rig);StationVoxelGi.Invalidate();
                // Settle the newly populated preview scene before comparing settings.
                s.voxelGi=false;var warm=rig.Shoot(TileGallery.Trench,960,540);Object.DestroyImmediate(warm);
                s.voxelGi=false;var off=Shot("trench-screen-only",TileGallery.Trench);
                s.voxelGi=true;var on=Shot("trench-combined",TileGallery.Trench);
                Check(ChangedPixels(off,on)>.001f,$"Combined GI changes {ChangedPixels(off,on):P2} of trench pixels by >32/765 RGB levels.");
                Object.DestroyImmediate(off);Object.DestroyImmediate(on);
                s.giDebugView=GiDebugView.VoxelGiOnly;var voxel=Shot("trench-voxel-only",TileGallery.Trench);Object.DestroyImmediate(voxel);
                s.giDebugView=GiDebugView.ScreenSpaceGiOnly;var screen=Shot("trench-screen-gi-only",TileGallery.Trench);
                Check(Mean(screen)>.0001f,"Existing screen-space GI still produces nonzero bounce.");
                // Compare the same temporal sample/history state, not successive jitter phases.
                StationScreenGiFeature.ResetHistory();Object.DestroyImmediate(screen);
                screen=rig.Shoot(TileGallery.Trench,960,540);
                StationScreenGiFeature.ResetHistory();
                s.voxelGi=false;var screenAgain=rig.Shoot(TileGallery.Trench,960,540);
                Check(screen.GetPixels32().SequenceEqual(screenAgain.GetPixels32()),"Isolated screen-space GI is pixel-identical with the voxel setting on/off (no feedback).");
                Object.DestroyImmediate(screen);Object.DestroyImmediate(screenAgain);s.voxelGi=true;
                s.giDebugView=GiDebugView.Normal;
                md.AppendLine("Gallery volume: "+StationVoxelGi.Diagnostics+"\n");
                // End-to-end synchronous renders at 1920x1080 (lab downsamples to 960x540).
                // Includes camera submission, GPU completion, readback and lab downsample; not a GPU timestamp.
                var times=new[]{new List<double>(),new List<double>()};
                int builds=StationVoxelGi.BuildCount;
                for(int i=0;i<14;i++) {
                    s.voxelGi=(i%2)==1;
                    var timer=System.Diagnostics.Stopwatch.StartNew();var image=rig.Shoot(TileGallery.Trench,960,540);
                    double elapsed=timer.Elapsed.TotalMilliseconds;Object.DestroyImmediate(image);
                    if(i>=4)times[i%2].Add(elapsed);
                }
                foreach(var list in times)list.Sort();
                md.AppendLine($"1920×1080 synchronous Camera.Render + readback median (5 interleaved samples after warmup): SSGI {times[0][2]:0.00} ms; SSGI + voxels {times[1][2]:0.00} ms; delta {times[1][2]-times[0][2]:0.00} ms.\n");
                Check(builds==StationVoxelGi.BuildCount,"Static frames and toggling GI reuse the cached volume.");
                var trench=TileGallery.Trench;
                var moved=rig.Shoot(new TileGallery.View(trench.position+Vector3.right,trench.target,trench.up,trench.fov),320,180);Object.DestroyImmediate(moved);
                Check(builds==StationVoxelGi.BuildCount,"Moving the camera reuses the world-space volume.");
                StationVoxelGi.Invalidate();
                var unchanged=rig.Shoot(TileGallery.Trench,320,180);Object.DestroyImmediate(unchanged);
                Check(builds==StationVoxelGi.BuildCount,"Invalidation without a source change reuses the volume.");
                int oldMask=rig.CullingMask;rig.CullingMask=oldMask^(1<<31);
                var maskChanged=rig.Shoot(TileGallery.Trench,320,180);Object.DestroyImmediate(maskChanged);
                Check(builds==StationVoxelGi.BuildCount,"Changing an unused camera layer reuses the volume.");
                rig.CullingMask=oldMask;
                float oldSun=s.sunIntensity;s.sunIntensity*=.99f;StationVoxelGi.Invalidate();
                var relit=rig.Shoot(TileGallery.Trench,320,180);Object.DestroyImmediate(relit);
                md.AppendLine("Relighting reuses source geometry: "+StationVoxelGi.Diagnostics+"\n");
                s.sunIntensity=oldSun;StationVoxelGi.Invalidate();
                // Controlled world-space test uses the same red habitat paint and station shader,
                // at two and four 5 m tile distances. No screen-space data enters these probes.
                rig.ShowStage(false);
                var root=new GameObject("Voxel GI test");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);cleanup.Add(root);
                var mat=new Material(Shader.Find("DeathStar/Tile Surface"));TileLook.Configure(mat,TileSlot.Paint);cleanup.Add(mat);
                GameObject Wall(string name,Vector3 at,Vector3 scale,bool red) {
                    var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.name=name;go.transform.SetParent(root.transform,false);
                    go.transform.position=at;go.transform.localScale=scale;
                    var mesh=Object.Instantiate(go.GetComponent<MeshFilter>().sharedMesh);cleanup.Add(mesh);
                    var tint=red?(Vector4)TileLook.FamilyColor(TileFamily.Habitat):Vector4.zero;
                    mesh.SetUVs(3,Enumerable.Repeat(tint,mesh.vertexCount).ToList());
                    mesh.colors=Enumerable.Repeat(Color.white,mesh.vertexCount).ToArray();
                    go.GetComponent<MeshFilter>().sharedMesh=mesh;go.GetComponent<MeshRenderer>().sharedMaterial=mat;return go;
                }
                var redWall=Wall("Red habitat paint, 2 by 2 tiles",new Vector3(-.25f,0,0),new Vector3(.5f,10,10),true);
                Wall("Neutral receiver, four tiles away",new Vector3(20.25f,0,0),new Vector3(.5f,10,10),false);
                s.voxelGi=true;s.screenSpaceGi=false;s.voxelGiDistance=40;s.sunFrom=new Vector3(1,1,0).normalized;s.sunIntensity=7.2f;
                s.planetIntensity=0;s.emission=0;s.voxelGiSecondBounce=false;s.giDebugView=GiDebugView.VoxelGiOnly;
                TileLook.AddLights(root.transform);StationVoxelGi.Invalidate();
                // Camera looks away from the red source, which is behind it.
                var view=new TileGallery.View(new Vector3(12,0,0),new Vector3(20,0,0),Vector3.up,45);
                var imageOffscreen=Shot("offscreen-source-20m",view);Object.DestroyImmediate(imageOffscreen);
                var points=new[]{new Vector3(5,0,0),new Vector3(10,0,0),new Vector3(19.9f,0,0)};
                var probes=StationVoxelGi.Last.Probe(points,Vector3.left);
                for(int i=0;i<points.Length;i++)Check(probes[i].x>.001f&&probes[i].x>probes[i].y*3,
                    $"Red habitat irradiance at {points[i].x:0.0} m: RGB {probes[i].x:0.00000}, {probes[i].y:0.00000}, {probes[i].z:0.00000}.");
                var returnPoint=new[]{new Vector3(5,0,0)};
                int fixtureBuilds=StationVoxelGi.BuildCount;
                s.voxelGiDistance=39;
                var reach=rig.Shoot(view,320,180);Object.DestroyImmediate(reach);
                Check(fixtureBuilds==StationVoxelGi.BuildCount,"Single-bounce reach adjustment does not rebuild radiance.");
                s.voxelGiDistance=40;
                // Slot positions matter even when the same material remains in the list.
                var redRenderer=redWall.GetComponent<MeshRenderer>();
                redRenderer.sharedMaterials=new Material[]{null,mat};StationVoxelGi.Invalidate();
                var missing=rig.Shoot(view,320,180);Object.DestroyImmediate(missing);
                var noSource=StationVoxelGi.Last.Probe(returnPoint,Vector3.left)[0];
                Check(noSource.x<probes[0].x*.1f,"Moving paint to an unused material slot removes its bounce.");
                redRenderer.sharedMaterials=new[]{mat};StationVoxelGi.Invalidate();
                var restored=rig.Shoot(view,320,180);Object.DestroyImmediate(restored);
                var redMesh=redWall.GetComponent<MeshFilter>().sharedMesh;
                redMesh.colors=Enumerable.Repeat(Color.black,redMesh.vertexCount).ToArray();StationVoxelGi.Invalidate(true);
                var recolored=rig.Shoot(view,320,180);Object.DestroyImmediate(recolored);
                var blackSource=StationVoxelGi.Last.Probe(returnPoint,Vector3.left)[0];
                Check(blackSource.x<probes[0].x*.1f,"Explicit mesh-data invalidation picks up in-place vertex color edits.");
                redMesh.colors=Enumerable.Repeat(Color.white,redMesh.vertexCount).ToArray();StationVoxelGi.Invalidate(true);
                var whiteAgain=rig.Shoot(view,320,180);Object.DestroyImmediate(whiteAgain);
                var recovered=StationVoxelGi.Last.Probe(returnPoint,Vector3.left)[0];
                Check(Mathf.Abs(recovered.x-probes[0].x)<.001f,"Restoring the source restores its original red irradiance.");
                var single=StationVoxelGi.Last.Probe(returnPoint,Vector3.right)[0];
                s.voxelGiSecondBounce=true;StationVoxelGi.Invalidate();
                var twice=Shot("two-bounces-20m",view);Object.DestroyImmediate(twice);
                var twiceProbe=StationVoxelGi.Last.Probe(returnPoint,Vector3.right)[0];
                Check(twiceProbe.x>single.x+.0001f,$"Second bounce returns additional red from the neutral receiver: {single.x:0.00000} -> {twiceProbe.x:0.00000}.");
                s.voxelGiSecondBounce=false;
                // Opaque, unlit blocker must suppress distant red light.
                var blocker=Wall("Black occluding wall",new Vector3(10,0,0),new Vector3(1,24,24),false);
                var black=new Material(mat);black.SetVector("_BaseColor",Vector4.zero);cleanup.Add(black);
                blocker.GetComponent<MeshRenderer>().sharedMaterial=black;
                StationVoxelGi.Invalidate();var blocked=Shot("blocked-source-20m",view);Object.DestroyImmediate(blocked);
                var shadow=StationVoxelGi.Last.Probe(new[]{points[2]},Vector3.left)[0];
                Check(shadow.x<probes[2].x*.25f,$"Blocker reduces 20 m red irradiance to {shadow.x:0.00000} ({shadow.x/Mathf.Max(probes[2].x,1e-6f):P1} of clear path).");
                md.AppendLine("Fixture volume: "+StationVoxelGi.Diagnostics+"\n");
                blocker.SetActive(false);s.voxelGiResolution=64;StationVoxelGi.Invalidate();
                var low=Shot("offscreen-source-64",view);Object.DestroyImmediate(low);
                var lowProbe=StationVoxelGi.Last.Probe(new[]{points[2]},Vector3.left)[0];
                Check(StationVoxelGi.Diagnostics.StartsWith("64³")&&lowProbe.x>.001f&&lowProbe.x>lowProbe.y*3,
                    $"64³ mode retains distant red transport: RGB {lowProbe.x:0.00000}, {lowProbe.y:0.00000}, {lowProbe.z:0.00000}.");
            } finally {
                JsonUtility.FromJsonOverwrite(saved,s);
                foreach(var item in cleanup)if(item)Object.DestroyImmediate(item);
                rig.ShowStage(true);ApplyVoxelLights(rig);StationVoxelGi.Invalidate();
            }
            File.WriteAllText(Path.Combine(dir,"report.md"),md.ToString());
            return (failures.Count==0?"DONE ":"ERROR ")+Path.Combine(dir,"report.md")+"\n"+(failures.Count==0?"PASS all voxel GI checks":"FAIL "+string.Join("; ",failures));
        }
    }
}
