using Station.Scenarios;

namespace Station.Runner;

public sealed record ConstructionGenerationFiles(string Scenario, string Catalog, string Manifest);
public static class ConstructionGeneration
{
    public static ConstructionGenerationFiles Write(LoadedScenario canonical, ulong seed, ConstructionVariant variant, string output)
    {
        var generated = ConstructionGenerator.Generate(canonical.ScenarioJson, canonical.CatalogJson, seed, variant);
        string directory = Path.GetFullPath(output); var data = generated.Scenario.Scenario; data.InitialState.Catalog = "catalog.json";
        // Validate the exact serialized output before creating files.
        string scenarioJson = JsonData.Write(data); _ = new LoadedScenario(scenarioJson, generated.Scenario.CatalogJson, "generated-output");
        var paths = new ConstructionGenerationFiles(Path.Combine(directory, "scenario.json"), Path.Combine(directory, "catalog.json"), Path.Combine(directory, "manifest.json"));
        bool positive = variant is ConstructionVariant.Expansion or ConstructionVariant.DelayedReport or ConstructionVariant.NeedInterruption;
        var manifest = new { schemaVersion = 1, generator = "construction/v1", seed, variant, sourceContentDigest = canonical.ContentDigest, generatedContentDigest = generated.Scenario.ContentDigest,
            transformedInputs = new { generated.WorkerId, generated.MaterialResource, generated.Site, generated.Depot, generated.Terminal, generated.Builders, generated.WorkTicks, generated.TravelTicks, generated.Latency, generated.Axis, generated.Sign },
            preconditions = new { population = data.InitialState.Actors.Count, initialUsableBerths = data.InitialState.Growth!.BelievedCompletedBerths, knownMaterial = 4, actualMaterial = variant == ConstructionVariant.NoStock ? 0 : 4,
                legalSite = variant != ConstructionVariant.NoLegalSite, qualifiedWorkers = variant == ConstructionVariant.NoQualifiedWorker ? 0 : generated.Builders, needsEnabled = true, hungerWorkingRateOverride = variant == ConstructionVariant.NeedInterruption ? 100000 : 60 },
            expectedProperty = positive ? "One finite construction; four material consumed once; actual/believed capacity reaches sixteen with belief changing only on received evidence." :
                variant == ConstructionVariant.NoStock ? "One reservation; ordinary worker rejection; no physical or believed completion." : "No expansion; existing physical and believed capacity remains unchanged.",
            comparisonNormalization = "Compare capacities, conservation, event ordering, one-project count and accepted-report deduplication. Do not require equal timestamps or digests across transformed inputs. Checkpoint and concurrency comparisons use identical inputs and require exact state/trace equality.",
            files = paths };
        Directory.CreateDirectory(directory); File.WriteAllText(paths.Scenario, scenarioJson); File.WriteAllText(paths.Catalog, generated.Scenario.CatalogJson); File.WriteAllText(paths.Manifest, JsonData.Write(manifest)); return paths;
    }
}
