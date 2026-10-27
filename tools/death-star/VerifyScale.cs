using System;
using System.IO;
using System.Threading.Tasks;
using UnityEngine;
using DeathStar;
public static class DeathStarScaleCheck
{
    static async Task Wait(DeathStarExperiment p)
    {
        float deadline=Time.realtimeSinceStartup+20;
        do{await Task.Delay(50);if(Time.realtimeSinceStartup>deadline)throw new Exception("Generation timeout");}while(p.IsBuilding);
    }
    public static async Task<string> Run()
    {
        var p=UnityEngine.Object.FindFirstObjectByType<DeathStarExperiment>();
        try
        {
            p.enabled=false;await Task.Delay(80);p.resolution=256;p.enabled=true;await Wait(p);
            if(p.CellCount!=393216)throw new Exception("Wrong high resolution cell count");
            double[] samples=new double[5];
            for(int i=0;i<5;i++){p.RequestRegeneration();await Wait(p);samples[i]=p.LastGenerationMs;}
            p.SetView(45,35,40);await Task.Delay(1200);
            string report=$"256 resolution PASS: {p.CellCount} cells; {p.GpuBytes} resident GPU bytes; warm regeneration wall ms {string.Join(", ",samples)}; close seam/corner visible {p.VisibleTops}/{p.VisibleWalls}/{p.VisibleBoxes}.";
            Directory.CreateDirectory("DeathStarRuns");File.WriteAllText("DeathStarRuns/scale-validation.txt",report);return report;
        }
        finally
        {
            p.enabled=false;p.resolution=128;p.seed=1977;p.enabled=true;
        }
    }
}
