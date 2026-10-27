using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using DeathStar;
public static class DeathStarRuntimeCheck
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static async Task Wait(DeathStarExperiment p)
    {
        float deadline=Time.realtimeSinceStartup+15;
        do {await Task.Delay(40);if(Time.realtimeSinceStartup>deadline)throw new Exception("Regeneration timed out");}while(p.IsBuilding);
    }
    public static async Task<string> Run()
    {
        var p=UnityEngine.Object.FindFirstObjectByType<DeathStarExperiment>();
        Application.runInBackground=true;await Wait(p);
        int initial=p.Generation;
        Vector3 seam=new Vector3(1,0.25f,1).normalized;
        int cell=CubeSphere.FromDirection(seam,p.resolution);
        int touched=p.PaintDirection(seam,PaintTool.Raise,3);p.EndStroke();await Wait(p);
        Require(touched>4 && p.GetEdit(cell).steps==1,"Seam brush did not raise expected cells");
        Require(p.Generation>initial,"Painting did not publish a generation");
        p.UndoStroke();await Wait(p);Require(p.GetEdit(cell).steps==0,"Undo failed");
        p.RedoStroke();await Wait(p);Require(p.GetEdit(cell).steps==1,"Redo failed");
        p.ResetPaint();await Wait(p);Require(p.GetEdit(cell).steps==0,"Reset failed");
        int beforeStroke=p.Generation;
        for(int i=0;i<14;i++)
        {
            p.PaintDirection(new Vector3(1,0.25f+i*0.012f,1).normalized,PaintTool.Raise,1);
            await Task.Delay(35);
        }
        Require(p.Generation>beforeStroke,"Continuous painting starved regeneration");
        p.EndStroke();await Wait(p);p.ResetPaint();await Wait(p);
        p.NewSeed();await Wait(p);
        Require(p.Pick(new Ray(p.viewer.transform.position,-p.viewer.transform.position.normalized),out var direction),"Skin picking failed");
        Require(Mathf.Abs(direction.magnitude-1)<0.001f,"Invalid picked direction");
        double[] samples=new double[5];
        for(int i=0;i<samples.Length;i++){p.RequestRegeneration();await Wait(p);samples[i]=p.LastGenerationMs;}
        p.seed=1977;p.RequestRegeneration();await Wait(p);p.Home();
        string result=$"PASS: live seam paint ({touched} cells), whole generation swap, continuous-stroke regeneration, undo, redo, reset, seed change and skin picking. Rebuild wall latency ms: {string.Join(", ",samples)}. GPU resident {p.GpuBytes} bytes. Device {SystemInfo.graphicsDeviceName}. These are Editor end-to-end latencies, not GPU timings.";
        Directory.CreateDirectory("DeathStarRuns");File.WriteAllText("DeathStarRuns/runtime-validation.txt",result);
        return result;
    }
}
