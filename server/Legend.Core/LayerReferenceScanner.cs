using System.Xml.Linq;

namespace Legend.Core;

public static class LayerReferenceScanner
{
    public static IReadOnlyList<string> FindViolations(string repoRoot)
    {
        if (string.IsNullOrWhiteSpace(repoRoot) || !Directory.Exists(repoRoot))
            throw new DirectoryNotFoundException($"Repository root not found: {repoRoot}");

        var actual = new Dictionary<string, IReadOnlyCollection<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (string project in Legend.Contracts.EngineLayerRules.AllowedProjectReferences.Keys)
        {
            string csproj = FindProjectFile(repoRoot, project);
            actual[project] = ReadProjectReferences(csproj);
        }

        var violations = new List<string>();
        violations.AddRange(Compare(actual, Legend.Contracts.EngineLayerRules.AllowedProjectReferences));
        violations.AddRange(FindUnregisteredServerProjects(repoRoot));
        return violations;
    }

    public static IReadOnlyList<string> Compare(
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> actual,
        IReadOnlyDictionary<string, IReadOnlySet<string>> allowed)
    {
        var violations = new List<string>();
        foreach (var pair in actual)
        {
            if (!allowed.TryGetValue(pair.Key, out var permitted))
            {
                violations.Add($"{pair.Key}: project is not in the layer allow list.");
                continue;
            }

            foreach (string reference in pair.Value.OrderBy(name => name, StringComparer.OrdinalIgnoreCase))
            {
                if (!permitted.Contains(reference))
                    violations.Add($"{pair.Key} -> {reference}");
            }
        }

        return violations;
    }

    private static IReadOnlyList<string> FindUnregisteredServerProjects(string repoRoot)
    {
        string serverDir = Path.Combine(repoRoot, "server");
        if (!Directory.Exists(serverDir))
            return new[] { "server/ directory is missing." };

        var violations = new List<string>();
        foreach (string file in Directory.EnumerateFiles(serverDir, "*.csproj", SearchOption.AllDirectories))
        {
            string name = Path.GetFileNameWithoutExtension(file);
            if (Legend.Contracts.EngineLayerRules.ExemptServerProjects.Contains(name))
                continue;
            if (!Legend.Contracts.EngineLayerRules.AllowedProjectReferences.ContainsKey(name))
                violations.Add($"{name}: server project is not registered in EngineLayerRules.");
        }

        return violations;
    }

    private static string FindProjectFile(string repoRoot, string projectName)
    {
        string fileName = projectName + ".csproj";
        string? found = Directory.EnumerateFiles(repoRoot, fileName, SearchOption.AllDirectories)
            .FirstOrDefault(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal));
        if (found is null)
            throw new FileNotFoundException($"Project file {fileName} was not found under {repoRoot}.");
        return found;
    }

    public static IReadOnlyList<string> ReadProjectReferences(string csprojPath)
    {
        var document = XDocument.Load(csprojPath);
        return document.Descendants()
            .Where(element => element.Name.LocalName == "ProjectReference")
            .Select(element => element.Attribute("Include")?.Value)
            .Where(include => !string.IsNullOrWhiteSpace(include))
            .Select(include => Path.GetFileNameWithoutExtension(include!.Replace('\\', Path.DirectorySeparatorChar)))
            .ToArray();
    }
}
