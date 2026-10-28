using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object=UnityEngine.Object;

namespace DeathStar.EditorTools
{
    public static partial class TileLab
    {
        // Paired throughput test of the old/new gather on identical live radiance.
        // Timed batches end with a one-element GPU readback, not Editor timestamps.
        static string VoxelPerformance(Rig rig)
        {
            var settings=StationLightingSettings.Current;
            string saved=JsonUtility.ToJson(settings);
            var material=new Material(Shader.Find("Hidden/DeathStar/Screen GI"));
            var md=new StringBuilder("# Voxel cone throughput\n\n");
            try {
                settings.voxelGi=true;settings.voxelGiStrength=1;settings.voxelGiDistance=40;
                settings.voxelGiResolution=128;settings.voxelGiSecondBounce=true;settings.giDebugView=GiDebugView.Normal;
                var stage=rig.Stage;ApplyVoxelLights(rig);StationVoxelGi.Invalidate();
                var shot=rig.Shoot(TileGallery.Trench,320,180);Object.DestroyImmediate(shot);
                if(StationVoxelGi.Last==null||!StationVoxelGi.Last.Prepare(rig.Camera,settings,material))return "ERROR no radiance volume";
                var shader=AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/DeathStar/Editor/VoxelConeReference.compute");
                var messages=ShaderUtil.GetComputeShaderMessages(shader);
                if(messages.Any(m=>m.severity==UnityEditor.Rendering.ShaderCompilerMessageSeverity.Error))
                    return "ERROR "+string.Join("; ",messages.Select(m=>m.message));
                var queries=new List<Vector4>();var positions=new List<Vector3>();var normals=new List<Vector3>();
                // Sample actual station mesh surfaces, including differently oriented curved tiles.
                foreach(var filter in stage.root.GetComponentsInChildren<MeshFilter>()) {
                    var mesh=filter.sharedMesh;if(!mesh||!mesh.isReadable)continue;
                    positions.Clear();normals.Clear();mesh.GetVertices(positions);mesh.GetNormals(normals);
                    if(positions.Count!=normals.Count)continue;
                    var matrix=filter.transform.localToWorldMatrix;var normalMatrix=matrix.inverse.transpose;
                    int stride=Mathf.Max(1,positions.Count/48);
                    for(int i=0;i<positions.Count&&queries.Count<65536;i+=stride) {
                        var normal=normalMatrix.MultiplyVector(normals[i]).normalized;if(normal.sqrMagnitude<.5f)continue;
                        queries.Add(matrix.MultiplyPoint3x4(positions[i]));queries.Add(normal);
                    }
                }
                int count=queries.Count/2;
                using var input=new ComputeBuffer(queries.Count,16);input.SetData(queries);
                using var output=new ComputeBuffer(count,16);
                int reference=shader.FindKernel("Reference"),optimized=shader.FindKernel("Optimized");
                shader.SetInt("_Count",count);
                void BindVolume() {
                    foreach(string name in new[]{"_StationVoxelMin","_StationVoxelParams","_StationVoxelGrid"})shader.SetVector(name,material.GetVector(name));
                    shader.SetVectorArray("_StationVoxelSteps",material.GetVectorArray("_StationVoxelSteps"));
                    shader.SetInt("_StationVoxelStepCount",material.GetInt("_StationVoxelStepCount"));
                    foreach(int kernel in new[]{reference,optimized}) {
                        shader.SetTexture(kernel,"_StationVoxelRadiance",material.GetTexture("_StationVoxelRadiance"));
                        shader.SetBuffer(kernel,"_Queries",input);shader.SetBuffer(kernel,"_Results",output);
                    }
                }
                var expected=new Vector4[count];var actual=new Vector4[count];
                bool Compare(string label) {
                    shader.Dispatch(reference,(count+63)/64,1,1);output.GetData(expected);
                    shader.Dispatch(optimized,(count+63)/64,1,1);output.GetData(actual);
                    double error=0,energy=0;float maxError=0;
                    for(int i=0;i<count;i++)for(int channel=0;channel<3;channel++) {
                        float e=Mathf.Abs(actual[i][channel]-expected[i][channel]);error+=e;energy+=expected[i][channel];maxError=Mathf.Max(maxError,e);
                    }
                    double relative=error/Math.Max(energy,1e-8);
                    bool matches=relative<.001&&maxError<.01&&energy>.001;
                    md.AppendLine($"{(matches?"PASS":"FAIL")} {label}, {count:N0} station surface probes: aggregate relative RGB error {relative:P4}, maximum channel error {maxError:0.000000}.\n");
                    return matches;
                }
                BindVolume();bool equivalent=Compare("Default two-bounce volume");
                const int repeats=64;
                using var baseline=new CommandBuffer{name="Reference voxel cone batch"};
                using var candidate=new CommandBuffer{name="Optimized voxel cone batch"};
                for(int i=0;i<repeats;i++) {
                    baseline.DispatchCompute(shader,reference,(count+63)/64,1,1);
                    candidate.DispatchCompute(shader,optimized,(count+63)/64,1,1);
                }
                var fence=new Vector4[1];var oldTimes=new List<double>();var newTimes=new List<double>();
                double TimeBatch(CommandBuffer commands) {
                    var timer=System.Diagnostics.Stopwatch.StartNew();Graphics.ExecuteCommandBuffer(commands);
                    output.GetData(fence,0,0,1);return timer.Elapsed.TotalMilliseconds/repeats;
                }
                TimeBatch(baseline);TimeBatch(candidate);
                for(int i=0;i<9;i++) {
                    if((i&1)==0){oldTimes.Add(TimeBatch(baseline));newTimes.Add(TimeBatch(candidate));}
                    else {newTimes.Add(TimeBatch(candidate));oldTimes.Add(TimeBatch(baseline));}
                }
                oldTimes.Sort();newTimes.Sort();
                md.AppendLine($"Same-volume paired batches, {repeats} dispatches per batch, 9 samples with alternating order: reference median {oldTimes[4]:0.000} ms/dispatch; optimized {newTimes[4]:0.000} ms/dispatch; change {(newTimes[4]/oldTimes[4]-1):P1}.\n");
                md.AppendLine($"Reference range {oldTimes[0]:0.000}–{oldTimes[8]:0.000} ms; optimized {newTimes[0]:0.000}–{newTimes[8]:0.000} ms.\n");
                md.AppendLine("These are synchronized cone-kernel throughput measurements including amortized submission/readback, not GPU timestamps or whole-frame timings. Both kernels use the same radiance, query positions, normals and reach; volume rebuilding is outside the timed region.\n");
                md.AppendLine(SystemInfo.graphicsDeviceName+" / "+SystemInfo.graphicsDeviceType+"\n");
                // Also exercise schedule changes that do not necessarily rebuild radiance.
                settings.voxelGiSecondBounce=false;settings.screenSpaceGi=true;
                foreach(int resolution in new[]{64,128})foreach(var test in new[]{(reach:5f,handoff:0f),(reach:40f,handoff:8f),(reach:80f,handoff:8f)}) {
                    settings.voxelGiResolution=resolution;settings.voxelGiDistance=test.reach;settings.screenGiRadius=test.handoff;
                    if(!StationVoxelGi.Last.Prepare(rig.Camera,settings,material))throw new InvalidOperationException("No test volume");
                    BindVolume();equivalent&=Compare($"{resolution}³ / reach {test.reach} m / handoff {test.handoff} m");
                }
                string path=Path.Combine(Dir,"voxel-gi","throughput.md");Directory.CreateDirectory(Path.GetDirectoryName(path));File.WriteAllText(path,md.ToString());
                return (equivalent?"DONE ":"ERROR ")+path+"\n"+md;
            } finally {JsonUtility.FromJsonOverwrite(saved,settings);ApplyVoxelLights(rig);StationVoxelGi.Invalidate();Object.DestroyImmediate(material);}
        }
    }
}
