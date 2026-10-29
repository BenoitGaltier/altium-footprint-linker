namespace FootprintLinker;

public static class Planner
{
    public static string? AuId(IEnumerable<Parameter> revision, IEnumerable<Parameter> item)
    {
        var current = revision.Where(p => p.Name.Trim().Equals("AU_ID", StringComparison.OrdinalIgnoreCase)).ToArray();
        var values = (current.Length > 0 ? current : item.Where(p => p.Name.Trim().Equals("AU_ID", StringComparison.OrdinalIgnoreCase)))
            .Select(p => p.Value.Trim()).Distinct(StringComparer.Ordinal).ToArray();
        return values.Length == 1 && values[0].Length > 0 ? values[0] : null;
    }
    public static string FootprintName(string name) => name.EndsWith(".PcbLib", StringComparison.OrdinalIgnoreCase) ? name[..^7] : name;
    public static PlanRow[] Build(CsvRow[] csv, IReadOnlyList<Component> components, LibraryCatalog catalog)
    {
        var byAu = components.Where(c => c.DetailError is null).Select(c => (Component: c, Au: AuId(c.Parameters, c.ItemParameters)))
            .Where(c => c.Au is not null).ToLookup(c => c.Au!, c => c.Component, StringComparer.Ordinal);
        var seen = new HashSet<(string, string)>(); var result = new List<PlanRow>();
        foreach (var row in csv)
        {
            var name = FootprintName(row.Footprint);
            var candidates = catalog.Files.SelectMany(f => f.Names.Where(n => n.Equals(name, StringComparison.OrdinalIgnoreCase)).Select(n => (File: f, Name: n))).ToArray();
            string? problem = row.Item.Length == 0 ? "emptyItem" : name.Length == 0 ? "emptyFootprint" :
                !seen.Add((row.Item, name.ToUpperInvariant())) ? "duplicateCsvRow" : candidates.Length == 0 ? "fileNotFound" : candidates.Length > 1 ? "ambiguousFile" : null;
            var matches = byAu[row.Item].ToArray();
            if (problem is not null || matches.Length == 0)
            { result.Add(new(Guid.NewGuid().ToString("N"), row, null, null, null, "blocked", problem ?? "componentNotFound", null, null, [], matches.Length)); continue; }
            foreach (var component in matches)
                result.Add(new(Guid.NewGuid().ToString("N"), row, component.Id, component.Name, component.Revision, "ready", null, candidates[0].File, candidates[0].Name, [], matches.Length));
        }
        return result.ToArray();
    }
}
