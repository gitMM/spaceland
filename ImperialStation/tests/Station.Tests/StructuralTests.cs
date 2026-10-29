using System.Diagnostics;
using System.Reflection;
using Station.Contracts;
using Station.Core;
using Station.Mind;
using Xunit;

namespace Station.Tests;

public sealed class StructuralTests
{
    [Fact]
    public void T00_AssemblyReferencesEnforceIsolation()
    {
        var mind = typeof(TravelEstimates).Assembly;
        Assert.All(mind.GetReferencedAssemblies(), a => Assert.True(a.Name is "Station.Contracts" or "netstandard" or "System.Runtime", a.FullName));
        foreach (var assembly in new[] { mind, typeof(World).Assembly, typeof(ActionIntent).Assembly })
        {
            Assert.All(assembly.GetReferencedAssemblies(), a => Assert.False(a.Name!.StartsWith("Unity", StringComparison.Ordinal) || a.Name.StartsWith("System.Net", StringComparison.Ordinal), a.FullName));
            Assert.Equal(".NETStandard,Version=v2.1", assembly.GetCustomAttribute<System.Runtime.Versioning.TargetFrameworkAttribute>()!.FrameworkName);
        }
        string project = File.ReadAllText(Path.Combine(Fixtures.Root,"src/Station.Mind/Station.Mind.csproj"));
        Assert.DoesNotContain("Station.Core", project);
    }
    [Fact]
    public void T00_NoFixtureNamesOrForbiddenFileAccessInSimulation()
    {
        var forbidden = new[] { "movement_exact", "exact_fixture", "detour_fixture", "transfer_fixture", "generated_grid", "FixtureCourier", "GeneratedCourier", "File.Read", "File.Write", "Console.", "HttpClient", "Dictionary<", "HashSet<" };
        foreach (string project in new[] { "Station.Contracts", "Station.Core", "Station.Mind" })
        foreach (string path in Directory.EnumerateFiles(Path.Combine(Fixtures.Root, "src", project), "*.cs", SearchOption.AllDirectories).Where(p => !p.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar)))
        {
            string source = File.ReadAllText(path).Replace("SortedDictionary<", "SortedMap<", StringComparison.Ordinal);
            foreach (string word in forbidden) Assert.DoesNotContain(word, source, StringComparison.Ordinal);
        }
    }
    [Fact]
    public async Task T00_DeliberatelyBannedCallFailsCompilation()
    {
        string directory = Path.Combine(Fixtures.Root, "artifacts", "analyzer-probe"); Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "Probe.csproj"), "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><TargetFramework>netstandard2.1</TargetFramework></PropertyGroup></Project>");
        File.WriteAllText(Path.Combine(directory, "Probe.cs"), "public static class Probe { public static System.DateTime Read() => System.DateTime.UtcNow; }");
        File.WriteAllText(Path.Combine(directory, "NuGet.Config"), "<configuration><packageSources><clear /></packageSources></configuration>");
        var start = new ProcessStartInfo("dotnet") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        foreach (string arg in new[] { "build", "Probe.csproj", "-c", "Release", "-m:1", "-p:UseSharedCompilation=false", "-p:NuGetAudit=false", "-p:RestorePackagesPath=" + Path.Combine(Fixtures.Root, ".nuget", "packages"), "--verbosity", "minimal" }) start.ArgumentList.Add(arg);
        start.Environment["DOTNET_CLI_HOME"] = Path.Combine(Fixtures.Root, ".dotnet");
        using var process = Process.Start(start)!; var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); } catch { process.Kill(true); throw; }
        string output = await stdout + await stderr; File.WriteAllText(Path.Combine(directory, "build.log"), output);
        Assert.NotEqual(0, process.ExitCode); Assert.Contains("error RS0030", output); Assert.Contains("UtcNow", output);
    }
}
