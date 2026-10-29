namespace FootprintLinker;

public static class Planner
{
    public static string ValidateMatchField(string? field)
    {
        var value = field?.Trim();
        if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || value.Any(char.IsControl)) throw new AppException("invalidMatchField");
        return value;
    }
    public static string? MatchValue(IEnumerable<Parameter> revision, IEnumerable<Parameter> item, string field = "AU_ID")
    {
        var current = revision.Where(p => p.Name.Trim().Equals(field, StringComparison.OrdinalIgnoreCase)).ToArray();
        var values = (current.Length > 0 ? current : item.Where(p => p.Name.Trim().Equals(field, StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 1 && values[0].Length > 0 ? values[0] : null;
    }
    public static string FootprintName(string name) => name.EndsWith(".PcbLib", StringComparison.OrdinalIgnoreCase) ? name[..^7] : name;
    private static string[] Segments(string path) => path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
        .SkipWhile(p => new[] { "Components", "Models", "Footprints", "Component Library", "Library" }.Contains(p, StringComparer.OrdinalIgnoreCase)).ToArray();
    private static int SuffixScore(string[] source, string[] target)
    {
        int count = 0;
        while (count < Math.Min(source.Length, target.Length) && source[^(count + 1)].Equals(target[^(count + 1)], StringComparison.OrdinalIgnoreCase)) count++;
        return count;
    }
    public static (LibraryFile? File, string? Name, string? Error) FindFile(CsvRow row, LibraryCatalog catalog)
    {
        var name = FootprintName(row.Footprint);
        var candidates = catalog.Files.SelectMany(f => f.Names.Where(n => n.Equals(name, StringComparison.OrdinalIgnoreCase))
            .Select(n => (File: f, Name: n))).ToArray();
        if (candidates.Length == 0) return (null, null, "fileNotFound");
        if (candidates.Length == 1) return (candidates[0].File, candidates[0].Name, null);
        return (null, null, "ambiguousFile");
    }
    public static PlanRow[] Build(CsvRow[] csv, IReadOnlyList<Component> components, LibraryCatalog catalog, string matchField = "AU_ID")
    {
        var byAu = components.Where(c => c.DetailError is null).Select(c => (Component: c, Au: MatchValue(c.Parameters, c.ItemParameters, matchField)))
            .Where(c => c.Au is not null).ToLookup(c => c.Au!, c => c.Component, StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>(); var result = new List<PlanRow>();
        foreach (var row in csv)
        {
            var name = FootprintName(row.Footprint);
            string? problem = row.Item.Length == 0 ? "emptyItem" : name.Length == 0 ? "emptyFootprint" :
                !seen.Add((row.Item, name.ToUpperInvariant())) ? "duplicateCsvRow" : null;
            var matches = byAu[row.Item].ToArray();
            if (matches.Length == 0)
            { result.Add(new(Guid.NewGuid().ToString("N"), row, null, null, null, "blocked", problem ?? "componentNotFound", null, null, [], matches.Length)); continue; }
            foreach (var component in matches)
            {
                var file = FindFile(row, catalog); var reason = problem ?? file.Error;
                result.Add(new(Guid.NewGuid().ToString("N"), row, component.Id, component.Name, component.Revision,
                    reason is null ? "ready" : "blocked", reason, file.File, file.Name, [], matches.Length));
            }
        }
        return result.ToArray();
    }
    public static (LibraryFolder? Folder, string Source) Destination(ComponentDetails component, IReadOnlyList<LibraryFolder> folders)
    {
        var byId = folders.ToDictionary(f => f.Id, StringComparer.Ordinal);
        if (component.Footprints.FirstOrDefault()?.FolderId is { } first && byId.TryGetValue(first, out var primary))
            return (primary, "defaultFootprint");
        var existing = component.Footprints.Select(f => f.FolderId).OfType<string>()
            .Where(byId.ContainsKey).Distinct(StringComparer.Ordinal).ToArray();
        if (existing.Length == 1) return (byId[existing[0]], "existingFootprints");
        if (existing.Length > 1) return (null, "destinationAmbiguous");
        var source = Segments(component.FolderPath);
        int Score(LibraryFolder folder)
        {
            return SuffixScore(source, Segments(folder.Path));
        }
        var scored = folders.Select(f => (Folder: f, Score: Score(f))).ToArray();
        var best = scored.Length == 0 ? 0 : scored.Max(f => f.Score);
        var matches = scored.Where(f => f.Score == best && best > 0).ToArray();
        return matches.Length == 1 ? (matches[0].Folder, "componentFolder") : (null, matches.Length > 1 ? "destinationAmbiguous" : "destinationNotFound");
    }
}
