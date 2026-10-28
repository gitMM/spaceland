using System.Linq;
using UnityEditor;
using UnityEngine;
public static class VoxelGiDiagnostics
{
    public static string Run() {
        var shader=Shader.Find("Hidden/DeathStar/Screen GI");
        var compute=Resources.Load<ComputeShader>("StationLighting/StationVoxel");
        var messages=ShaderUtil.GetShaderMessages(shader).Select(m=>m.severity+" "+m.message+" "+m.file+":"+m.line);
        var cm=ShaderUtil.GetComputeShaderMessages(compute).Select(m=>m.severity+" "+m.message+" "+m.file+":"+m.line);
        var t=System.Type.GetType("DeathStar.StationVoxelGi, DeathStar.Runtime");
        return string.Join("\n",messages.Concat(cm))+"\n"+t?.GetProperty("Diagnostics")?.GetValue(null);
    }
}
