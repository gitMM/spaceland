using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    public static partial class TileLab
    {
        // Tests the actual play-mode scheduling path without entering play mode, and compares
        // complete source data rather than just counts. Also retains the old gather as a benchmark.
        static string LightingCacheCheck(Rig rig)
        {
            const BindingFlags Private=BindingFlags.NonPublic|BindingFlags.Instance;
            var checks=new List<(bool ok,string text)>();var notes=new List<string>();
            var s=StationLightingSettings.Current;string saved=JsonUtility.ToJson(s);
            var stage=rig.Stage;rig.ShowStage(false);
            var root=new GameObject("Lighting cache checks");SceneManager.MoveGameObjectToScene(root,rig.PreviewScene);
            var go=GameObject.CreatePrimitive(PrimitiveType.Cube);go.transform.SetParent(root.transform,false);
            var renderer=go.GetComponent<MeshRenderer>();var filter=go.GetComponent<MeshFilter>();
            var mesh=Object.Instantiate(filter.sharedMesh);filter.sharedMesh=mesh;
            var material=new Material(Shader.Find("DeathStar/Tile Surface"));TileLook.Configure(material,TileSlot.Light);renderer.sharedMaterial=material;
            var source=go.AddComponent<StationLocalLightSource>();source.materialSlot=0;
            var actual=new List<StationLocalLights.LightData>();var expected=new List<StationLocalLights.LightData>();
            var legacy=new LegacyLamp(source);Mesh replacement=null;Material giMaterial=null;VolumeProfile profile=null;
            var camera=rig.Camera;camera.transform.SetPositionAndRotation(new Vector3(0,1,-3),Quaternion.identity);
            int mask=camera.cullingMask;var cameraScene=camera.scene;
            void Compare(string label) {
                actual.Clear();expected.Clear();StationLocalLights.Gather(camera,actual);legacy.Gather(camera,expected);
                checks.Add((SameLamps(actual,expected),label+": cached and reference lamp data agree ("+actual.Count+")."));
            }
            try {
                Compare("Initial and repeated gather");Compare("Warm cache");
                material.SetVector("_Emission",new Vector4(2,.3f,.1f,0));Compare("Shared material emission edit");
                root.transform.SetPositionAndRotation(Vector3.right,Quaternion.Euler(21,37,13));root.transform.localScale=new Vector3(2,.7f,1.3f);
                Compare("Parent rotation, translation and nonuniform scale");
                source.clipToMountingPlane=true;source.mountingNormal=Vector3.forward;Compare("Mounting plane edit");
                replacement=Object.Instantiate(mesh);filter.sharedMesh=replacement;Compare("Mesh replacement");
                var vertices=replacement.vertices;for(int i=0;i<vertices.Length;i++)vertices[i].x*=.7f;replacement.vertices=vertices;replacement.RecalculateBounds();
                StationLocalLights.Invalidate(replacement);Compare("In-place mesh change and invalidation");
                renderer.sharedMaterials=new Material[]{null,material};Compare("Null and shifted material slots");
                renderer.sharedMaterials=new[]{material};source.enabled=false;Compare("Disabled source");source.enabled=true;
                go.layer=30;camera.cullingMask&=~(1<<30);Compare("Camera layer exclusion");camera.cullingMask=mask;go.layer=0;
                camera.scene=default;Compare("Default camera excludes preview-scene sources");camera.scene=cameraScene;
                camera.transform.position=new Vector3(0,1,-200);Compare("Moved camera outside source reach");camera.transform.position=new Vector3(0,1,-3);

                var sunGo=new GameObject("Sun");sunGo.transform.SetParent(root.transform,false);var sun=sunGo.AddComponent<Light>();sun.type=LightType.Directional;
                var lighting=root.AddComponent<StationLightingRig>();lighting.sun=sun;lighting.Apply();
                root.transform.rotation=Quaternion.Euler(0,80,0);
                var update=typeof(StationLightingRig).GetMethod("Update",Private);update.Invoke(lighting,null);
                checks.Add((Vector3.Dot(sun.transform.forward,-s.sunFrom.normalized)>.99999f,"Parent rotation restores the authored sun direction immediately."));
                profile=ScriptableObject.CreateInstance<VolumeProfile>();var volume=root.AddComponent<Volume>();volume.sharedProfile=profile;
                var bloom=profile.Add<Bloom>();typeof(StationLightingRig).GetField("reconcileAt",Private).SetValue(lighting,0d);update.Invoke(lighting,null);
                checks.Add((Mathf.Approximately(bloom.intensity.value,s.bloom),"Fallback discovers a newly added volume and profile."));
                s.bloom+=.1f;update.Invoke(lighting,null);checks.Add((Mathf.Approximately(bloom.intensity.value,s.bloom),"Scripted lighting edits apply immediately."));
                root.SetActive(false);rig.ShowStage(true);

                // Exercise the real feature entry point with Scene-view camera data.
                // A null renderer deliberately fails if any custom pass is enqueued.
                var sceneFeature=ScriptableObject.CreateInstance<StationScreenGiFeature>();
                try {
                    sceneFeature.Create();
                    using var context=new ContextContainer();
                    var sceneCamera=context.Create<UniversalCameraData>();sceneCamera.camera=camera;sceneCamera.cameraType=CameraType.SceneView;
                    var data=(RenderingData)Activator.CreateInstance(typeof(RenderingData),BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{context},null);
                    Shader.SetGlobalFloat("_StationLocalLightActive",1);
                    Shader.SetGlobalVector("_StationContactShadowParams",Vector4.one);
                    int sceneBuilds=StationVoxelGi.BuildCount;
                    sceneFeature.AddRenderPasses(null,ref data);
                    checks.Add((StationVoxelGi.BuildCount==sceneBuilds&&Shader.GetGlobalFloat("_StationLocalLightActive")==0&&Shader.GetGlobalVector("_StationContactShadowParams")==Vector4.zero,
                        "Scene-view feature skips custom lighting passes and clears camera-specific shadow state."));
                } finally {sceneFeature.Dispose();Object.DestroyImmediate(sceneFeature);}

                // Use the full stage so a 0.75 ms slice really suspends mid-scan.
                using(var voxels=new StationVoxelGi()) {
                    var type=typeof(StationVoxelGi);var collect=type.GetMethod("Collect",Private);var slice=type.GetMethod("CollectSlice",Private);
                    var cancel=type.GetMethod("CancelScan",Private);var hash=type.GetField("scanHash",Private);
                    int reference=(int)collect.Invoke(voxels,new object[]{rig.PreviewScene,camera.cullingMask,s});
                    double now=Time.realtimeSinceStartupAsDouble;bool done=(bool)slice.Invoke(voxels,new object[]{rig.PreviewScene,camera.cullingMask,s,now});
                    checks.Add((!done,"Large-stage fallback scan suspends across frames."));
                    var times=new List<double>();int slices=1;
                    while(!done&&slices<1024) {
                        var watch=Stopwatch.StartNew();done=(bool)slice.Invoke(voxels,new object[]{rig.PreviewScene,camera.cullingMask,s,now+slices/120d});
                        times.Add(watch.Elapsed.TotalMilliseconds);slices++;
                    }
                    checks.Add((done&&(int)hash.GetValue(voxels)==reference,"Sliced and synchronous scans produce the same stage key."));
                    notes.Add($"Cold-source scan: {slices} slices; maximum slice {times.DefaultIfEmpty().Max():0.000} ms (includes initial extraction).");
                    // Warm snapshots measure checking, with changed-source extraction out of the baseline.
                    times.Clear();now=Time.realtimeSinceStartupAsDouble;done=false;slices=0;
                    while(!done&&slices<120){var watch=Stopwatch.StartNew();done=(bool)slice.Invoke(voxels,new object[]{rig.PreviewScene,camera.cullingMask,s,now+slices/120d});times.Add(watch.Elapsed.TotalMilliseconds);slices++;}
                    times.Sort();notes.Add($"Warm scan: {slices} slices; median {times[times.Count/2]:0.000} ms, maximum {times.Last():0.000} ms.");
                    slice.Invoke(voxels,new object[]{rig.PreviewScene,camera.cullingMask,s,now});cancel.Invoke(voxels,null);
                    checks.Add((type.GetField("backgroundScan",Private).GetValue(voxels)==null,"Cancellation disposes a suspended scan."));
                    var slow=new SlowScan();type.GetField("backgroundScan",Private).SetValue(voxels,slow);
                    type.GetField("scanStarted",Private).SetValue(voxels,now-2);
                    done=(bool)slice.Invoke(voxels,new object[]{rig.PreviewScene,camera.cullingMask,s,now});
                    checks.Add((!done&&!slow.disposed&&slow.steps<100,"Old scans retain their slice budget instead of draining at a deadline."));cancel.Invoke(voxels,null);

                    giMaterial=new Material(Shader.Find("Hidden/DeathStar/Screen GI"));s.voxelGi=true;
                    var prepare=type.GetMethod("PrepareForMode",Private);
                    void FinishPending() {
                        for(int i=0;i<500;i++) {
                            prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                            if(type.GetField("backgroundScan",Private).GetValue(voxels)==null)return;
                        }
                        throw new InvalidOperationException("Source scan failed to make progress");
                    }
                    bool ready=(bool)prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    checks.Add((ready,"Incremental Prepare path initializes a valid volume."));
                    slow=new SlowScan();type.GetField("backgroundScan",Private).SetValue(voxels,slow);
                    type.GetField("scanStarted",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble-2);
                    type.GetField("lastPrepare",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble);
                    prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    checks.Add((!slow.disposed&&slow.steps>0&&slow.steps<100,"A continuously observed scan older than one second advances without restart or synchronous catch-up."));cancel.Invoke(voxels,null);
                    type.GetField("nextCheck",Private).SetValue(voxels,0d);
                    prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    StationVoxelGi.NotifyChanged(); // Must not restart an in-progress scan indefinitely.
                    int builds=StationVoxelGi.BuildCount;FinishPending();
                    checks.Add((StationVoxelGi.BuildCount==builds,"Notification without changed geometry does not rebuild radiance."));
                    type.GetField("nextCheck",Private).SetValue(voxels,0d);
                    int notifications=0;
                    for(;notifications<500;notifications++) {
                        StationVoxelGi.NotifyChanged();prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                        if(type.GetField("backgroundScan",Private).GetValue(voxels)==null)break;
                    }
                    checks.Add((notifications<500&&StationVoxelGi.BuildCount==builds,
                        "Continuous notifications do not restart an active scan or rebuild unchanged geometry."));
                    var fault=new FailedScan();type.GetField("backgroundScan",Private).SetValue(voxels,fault);
                    bool logging=UnityEngine.Debug.unityLogger.logEnabled;bool failed;
                    try {UnityEngine.Debug.unityLogger.logEnabled=false;failed=!(bool)prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});}
                    finally {UnityEngine.Debug.unityLogger.logEnabled=logging;}
                    checks.Add((failed&&fault.disposed&&voxels.IsReady&&(bool)type.GetField("retryBuild",Private).GetValue(voxels),
                        "An exception in a suspended source scan keeps the old volume, disposes the scan and schedules retry."));
                    type.GetField("nextRetry",Private).SetValue(voxels,0d);
                    prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    checks.Add((StationVoxelGi.BuildCount==builds+1&&!(bool)type.GetField("retryBuild",Private).GetValue(voxels),"A pending failed-build retry rebuilds and clears only after success."));
                    var suspended=new FailedScan();type.GetField("backgroundScan",Private).SetValue(voxels,suspended);
                    s.voxelGi=false;bool disabled=!(bool)prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});s.voxelGi=true;
                    checks.Add((disabled&&suspended.disposed&&type.GetField("backgroundScan",Private).GetValue(voxels)==null,"Disabling voxel GI disposes its suspended scan."));
                    suspended=new FailedScan();type.GetField("backgroundScan",Private).SetValue(voxels,suspended);
                    type.GetField("lastPrepare",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble-2);
                    ready=(bool)prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    checks.Add((ready&&suspended.disposed&&!(bool)type.GetField("retryBuild",Private).GetValue(voxels),"Resuming after a long render gap discards the old snapshot without executing it."));
                    cancel.Invoke(voxels,null);

                    // A completed background snapshot must be published as captured,
                    // even if its source moves again while waiting for the solve cadence.
                    var cache=(System.Collections.IDictionary)type.GetField("sourceCache",Private).GetValue(voxels);
                    var moving=stage.root.GetComponentsInChildren<MeshRenderer>().First(r=>cache.Contains(r.GetInstanceID()));
                    var original=moving.transform.position;
                    try {
                        moving.transform.position=original+Vector3.right*.2f;StationVoxelGi.NotifyChanged();
                        type.GetField("nextCheck",Private).SetValue(voxels,0d);
                        type.GetField("nextDynamicRefresh",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble+10);
                        prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                        // Change an already-captured source while the rest is still scanning.
                        for(int i=0;i<500;i++) {
                            var cached=cache[moving.GetInstanceID()];
                            if(moving.bounds.Equals((Bounds)cached.GetType().GetField("bounds").GetValue(cached)))break;
                            prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                        }
                        bool midScan=type.GetField("backgroundScan",Private).GetValue(voxels)!=null;
                        int capturedChanges=(int)type.GetField("scanChanges",Private).GetValue(voxels);
                        moving.transform.position=original+Vector3.right*.3f;StationVoxelGi.NotifyChanged();
                        builds=StationVoxelGi.BuildCount;FinishPending();
                        bool held=(bool)type.GetField("scanReady",Private).GetValue(voxels)&&StationVoxelGi.BuildCount==builds;
                        var snapshot=cache[moving.GetInstanceID()];var captured=(Bounds)snapshot.GetType().GetField("bounds").GetValue(snapshot);
                        int capturedHash=(int)hash.GetValue(voxels);
                        moving.transform.position=original+Vector3.right*.4f;StationVoxelGi.NotifyChanged();
                        type.GetField("nextDynamicRefresh",Private).SetValue(voxels,0d);
                        prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                        var published=cache[moving.GetInstanceID()];
                        checks.Add((held&&StationVoxelGi.BuildCount==builds+1&&(int)type.GetField("sceneHash",Private).GetValue(voxels)==capturedHash&&
                            captured.Equals((Bounds)published.GetType().GetField("bounds").GetValue(published)),
                            "Changed-source snapshot waits for the solve cadence and publishes without re-reading newer geometry."));
                        checks.Add((midScan&&(int)type.GetField("seenChanges",Private).GetValue(voxels)==capturedChanges,
                            "Changes after capture and while awaiting publication remain pending after the older snapshot publishes."));
                        // Let the solve cadence expire, leaving the idle fallback deliberately
                        // far away: only the retained notification can start this follow-up.
                        type.GetField("nextCheck",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble+10);
                        type.GetField("nextDynamicRefresh",Private).SetValue(voxels,0d);FinishPending();
                        var latest=cache[moving.GetInstanceID()];
                        checks.Add((!captured.Equals((Bounds)latest.GetType().GetField("bounds").GetValue(latest)),"The next scan detects movement that followed the published snapshot."));
                        bool gapsPublish=true;
                        for(int gap=0;gap<2;gap++) {
                            cancel.Invoke(voxels,null);
                            moving.transform.position=original+Vector3.right*(.6f+gap*.2f);
                            type.GetField("nextCheck",Private).SetValue(voxels,0d);
                            type.GetField("nextDynamicRefresh",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble+10);
                            prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                            type.GetField("lastPrepare",Private).SetValue(voxels,Time.realtimeSinceStartupAsDouble-2);
                            builds=StationVoxelGi.BuildCount;prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                            var refreshed=cache[moving.GetInstanceID()];
                            gapsPublish&=StationVoxelGi.BuildCount==builds+1&&type.GetField("backgroundScan",Private).GetValue(voxels)==null&&
                                moving.bounds.Equals((Bounds)refreshed.GetType().GetField("bounds").GetValue(refreshed));
                        }
                        checks.Add((gapsPublish,"Repeated long gaps between camera renders still publish changed geometry instead of restarting forever."));
                        // Complete an unchanged scan and check that no ordinary next
                        // frame resumes scanning until a real idle interval has passed.
                        type.GetField("nextCheck",Private).SetValue(voxels,0d);FinishPending();
                        double finished=Time.realtimeSinceStartupAsDouble;
                        double due=(double)type.GetField("nextCheck",Private).GetValue(voxels);
                        prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                        checks.Add((due-finished>.2&&type.GetField("backgroundScan",Private).GetValue(voxels)==null,
                            "An unchanged completed scan leaves a quarter-second idle gap instead of immediately starting again."));
                    } finally {moving.transform.position=original;StationVoxelGi.NotifyChanged();cancel.Invoke(voxels,null);}
                    type.GetField("nextCheck",Private).SetValue(voxels,0d);prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    camera.cullingMask=0;bool empty=(bool)prepare.Invoke(voxels,new object[]{camera,s,giMaterial,true});
                    checks.Add((!empty&&type.GetField("backgroundScan",Private).GetValue(voxels)==null,"Mask change cancels the scan and never publishes the previous mask's volume."));camera.cullingMask=mask;
                }

                var room=TileGallery.Room;camera.transform.SetPositionAndRotation(room.position,Quaternion.LookRotation(room.target-room.position,room.up));
                stage.interiors.ShowAround(room.position,15);stage.root.GetComponent<StationKit>().Draw(camera,room.position);
                var subscribers=(Action<Camera,List<StationLocalLights.LightData>>)typeof(StationLocalLights).GetField("Collect",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
                var oldCalls=new List<Action<Camera,List<StationLocalLights.LightData>>>();
                foreach(var callback in subscribers.GetInvocationList())oldCalls.Add(callback.Target is StationLocalLightSource src?new LegacyLamp(src).Gather:(Action<Camera,List<StationLocalLights.LightData>>)callback);
                void Old(){expected.Clear();foreach(var callback in oldCalls)callback(camera,expected);}
                void New(){actual.Clear();StationLocalLights.Gather(camera,actual);}
                Old();New();checks.Add((SameLamps(actual,expected),"Full-stage cached gather preserves all reference lamps and their order."));
                var oldTimes=new List<double>();var newTimes=new List<double>();
                void Measure(Action action,List<double> values){var watch=Stopwatch.StartNew();for(int i=0;i<8;i++)action();values.Add(watch.Elapsed.TotalMilliseconds/8);}
                for(int i=0;i<15;i++){if(i%2==0){Measure(Old,oldTimes);Measure(New,newTimes);}else{Measure(New,newTimes);Measure(Old,oldTimes);}}
                oldTimes.Sort();newTimes.Sort();notes.Add($"Paired full-stage gather, 15 alternating batches of eight: reference {oldTimes[7]:0.000} ms; cached {newTimes[7]:0.000} ms; {actual.Count} lamps before per-lamp filtering.");
            }
            finally {
                camera.cullingMask=mask;camera.scene=cameraScene;JsonUtility.FromJsonOverwrite(saved,s);
                Object.DestroyImmediate(root);Object.DestroyImmediate(mesh);if(replacement)Object.DestroyImmediate(replacement);
                Object.DestroyImmediate(material);if(profile)Object.DestroyImmediate(profile);if(giMaterial)Object.DestroyImmediate(giMaterial);
                rig.ShowStage(true);s.ApplyGlobals();StationVoxelGi.Invalidate();
            }
            string dir=Path.Combine(Dir,"lighting-cache");Directory.CreateDirectory(dir);
            string report=string.Join("\n",checks.Select(c=>(c.ok?"PASS ":"FAIL ")+c.text))+"\n\n"+string.Join("\n",notes);
            File.WriteAllText(Path.Combine(dir,"report.md"),report);
            return (checks.All(c=>c.ok)?"DONE ":"ERROR ")+Path.Combine(dir,"report.md")+"\n"+report;
        }
        static bool SameLamps(List<StationLocalLights.LightData> a,List<StationLocalLights.LightData> b)
        {
            if(a.Count!=b.Count)return false;
            for(int i=0;i<a.Count;i++)if((a[i].a-b[i].a).sqrMagnitude>1e-9f||(a[i].b-b[i].b).sqrMagnitude>1e-9f||
                (a[i].color-b[i].color).sqrMagnitude>1e-9f||(a[i].plane-b[i].plane).sqrMagnitude>1e-9f)return false;
            return true;
        }
        sealed class SlowScan:IEnumerator<int>
        {
            public int steps;public bool disposed;public int Current=>0;object System.Collections.IEnumerator.Current=>Current;
            public bool MoveNext(){var watch=Stopwatch.StartNew();while(watch.Elapsed.TotalMilliseconds<.2){}return ++steps<100;}
            public void Reset()=>throw new NotSupportedException();public void Dispose()=>disposed=true;
        }
        sealed class FailedScan:IEnumerator<int>
        {
            public bool disposed;public int Current=>0;object System.Collections.IEnumerator.Current=>Current;
            public bool MoveNext()=>throw new InvalidOperationException("Injected source scan failure");
            public void Reset()=>throw new NotSupportedException();public void Dispose()=>disposed=true;
        }
        sealed class LegacyLamp
        {
            readonly StationLocalLightSource source;readonly MeshFilter filter;readonly MeshRenderer surface;readonly Transform transform;readonly GameObject owner;
            readonly List<Material> materials=new();
            public LegacyLamp(StationLocalLightSource source){this.source=source;owner=source.gameObject;transform=source.transform;filter=source.GetComponent<MeshFilter>();surface=source.GetComponent<MeshRenderer>();}
            public void Gather(Camera camera,List<StationLocalLights.LightData> lights)
            {
                if(!source.isActiveAndEnabled||!StationLocalLights.VisibleTo(camera,owner)||!surface||!surface.enabled||!filter)return;
                var settings=StationLightingSettings.Current;var scale=transform.lossyScale;
                float reach=settings.localLightDistance+1.5f*settings.localLightRange*Mathf.Max(Mathf.Abs(scale.x),Mathf.Max(Mathf.Abs(scale.y),Mathf.Abs(scale.z)));
                if(surface.bounds.SqrDistance(camera.transform.position)>reach*reach)return;
                surface.GetSharedMaterials(materials);int slot=source.materialSlot;
                if(slot<0||slot>=materials.Count||!materials[slot]||!materials[slot].HasProperty("_Emission"))return;
                var plane=Vector4.zero;if(source.clipToMountingPlane){var n=transform.TransformDirection(source.mountingNormal).normalized;plane=new Vector4(n.x,n.y,n.z,-Vector3.Dot(transform.position,n));}
                StationLocalLights.Append(lights,filter.sharedMesh,slot,transform.localToWorldMatrix,materials[slot].GetVector("_Emission"),1,plane);
            }
        }
    }
}

