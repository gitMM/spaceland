// Entry point for tools/tile-lab.sh, run through `unity command run_script`. Reflection, because the Death Star
// assemblies are not auto-referenced.
public static class TileLabEntry
{
    public static string Run(string args)
    {
        var t = System.Type.GetType("DeathStar.EditorTools.TileLab, DeathStar.Editor");
        if (t == null) return "ERROR DeathStar.Editor is not loaded (compile errors?)";
        return (string)t.GetMethod("Run").Invoke(null, new object[] { args });
    }
}
