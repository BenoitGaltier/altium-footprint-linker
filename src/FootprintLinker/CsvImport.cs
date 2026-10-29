using System.Text;

namespace FootprintLinker;

public static class CsvImport
{
    public static readonly string[] Headers = ["Item", "Description", "Supplier", "Supplier Name", "Supplier Item No", "footprint"];
    public static CsvRow[] Parse(string text)
    {
        if (text.Length > 10_000_000) throw new AppException("csvTooLarge");
        text = text.TrimStart('\uFEFF');
        char? declared = null;
        if (text.StartsWith("sep=", StringComparison.OrdinalIgnoreCase) && text.Length > 5)
        { declared = text[4]; text = text[(text.IndexOf('\n') + 1)..]; }
        var separator = declared ?? new[] { ',', ';', '\t', '|' }.FirstOrDefault(c =>
        {
            try { return Records(text, c).First().Values.Select(v => v.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase).IsSupersetOf(Headers); }
            catch (AppException) { return false; }
        });
        if (separator == '\0') throw new AppException("csvHeaders");
        var records = Records(text, separator).ToArray();
        if (records.Length < 2) throw new AppException("csvEmpty");
        var header = records[0].Values.Select(x => x.Trim()).ToArray();
        if (header.Distinct(StringComparer.OrdinalIgnoreCase).Count() != header.Length ||
            !Headers.All(h => header.Contains(h, StringComparer.OrdinalIgnoreCase))) throw new AppException("csvHeaders");
        int Index(string name) => Array.FindIndex(header, h => h.Equals(name, StringComparison.OrdinalIgnoreCase));
        var rows = new List<CsvRow>();
        foreach (var record in records.Skip(1))
        {
            if (record.Values.Length != header.Length) throw new AppException("csvColumns", record.Line.ToString());
            string Value(string name) => record.Values[Index(name)].Trim();
            rows.Add(new(record.Line, Value("Item"), Value("Description"), Value("Supplier"), Value("Supplier Name"), Value("Supplier Item No"), Value("footprint")));
            if (rows.Count > 50_000) throw new AppException("csvTooLarge");
        }
        return rows.ToArray();
    }
    private sealed record Record(int Line, string[] Values);
    private static IEnumerable<Record> Records(string text, char delimiter)
    {
        var values = new List<string>(); var cell = new StringBuilder();
        bool quoted = false, closed = false; int line = 1, start = 1;
        for (int i = 0; i <= text.Length; i++)
        {
            var end = i == text.Length; char ch = end ? '\n' : text[i];
            if (quoted)
            {
                if (end) throw new AppException("csvQuotes", start.ToString());
                if (ch == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { cell.Append('"'); i++; }
                    else { quoted = false; closed = true; }
                }
                else { cell.Append(ch); if (ch == '\n') line++; }
                continue;
            }
            if (ch == '"')
            { if (cell.Length != 0 || closed) throw new AppException("csvQuotes", line.ToString()); quoted = true; continue; }
            if (ch == delimiter || ch is '\r' or '\n')
            {
                values.Add(cell.ToString()); cell.Clear(); closed = false;
                if (ch != delimiter)
                {
                    if (values.Any(v => !string.IsNullOrWhiteSpace(v))) yield return new(start, values.ToArray());
                    values.Clear();
                    if (ch == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
                    line++; start = line;
                }
            }
            else if (closed && !char.IsWhiteSpace(ch)) throw new AppException("csvQuotes", line.ToString());
            else if (!closed) cell.Append(ch);
        }
    }
}
