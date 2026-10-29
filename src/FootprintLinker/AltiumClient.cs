using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;

namespace FootprintLinker;

public sealed partial class AltiumClient(HttpClient http)
{
    public const int PageSize = 100;
    public const string CategoriesQuery = """
        query LensCategories($first: Int!, $after: String, $workspace: String!) {
          desLibrary(workspaceUrl: $workspace, args: {allComponentRevisions: false}) {
            name components(first: 1) { totalCount }
            componentTypes(first: $first, after: $after) {
              nodes { componentTypeId name path }
              pageInfo { hasNextPage endCursor }
            }
          }
        }
        """;
    public const string ComponentsQuery = """
        query LensComponents($first: Int!, $after: String, $workspace: String!) {
          desLibrary(workspaceUrl: $workspace, args: {allComponentRevisions: false}) {
            components(first: $first, after: $after) {
              totalCount pageInfo { hasNextPage endCursor }
              nodes {
                id name revisionId description
                folder { path }
                componentType { componentTypeId name path }
                manufacturerParts { companyName partNumber }
              }
            }
          }
        }
        """;
    public const string DetailsQuery = """
        query LensComponentDetails($id: ID!) {
          desComponentById(id: $id) {
            id
            details { parameters { name value } itemParameters { name value } }
          }
        }
        """;

    public static Uri Endpoint(string input)
    {
        input = input.Trim();
        if (!input.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) input = "https://" + input;
        if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
            !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment) ||
            !uri.IsDefaultPort || !(uri.Host.EndsWith(".altium.com", StringComparison.OrdinalIgnoreCase) ||
                                    uri.Host.EndsWith(".altium365.com", StringComparison.OrdinalIgnoreCase)))
            throw new AppException("invalidEndpoint");
        if (uri.AbsolutePath is not ("/" or "/api/graphql" or "/api/graphql/")) throw new AppException("invalidEndpoint");
        return new UriBuilder(uri) { Path = "/api/graphql" }.Uri;
    }

    private async Task<JsonElement> Query(Uri endpoint, string token, string query, object variables, CancellationToken ct, bool readOnly = true)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new { query, variables });
            HttpResponseMessage response;
            try { response = await http.SendAsync(request, ct); }
            catch (OperationCanceledException) when (!ct.IsCancellationRequested) { throw new AppException("apiTimeout"); }
            catch (HttpRequestException) { throw new AppException("apiNetwork"); }
            using (response)
            {
                if (readOnly && (response.StatusCode is HttpStatusCode.TooManyRequests or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout) && attempt < 4)
                {
                    var retryAfter = response.Headers.RetryAfter;
                    var delay = retryAfter?.Delta ?? (retryAfter?.Date - DateTimeOffset.UtcNow) ?? TimeSpan.FromSeconds(Math.Pow(2, attempt + 1));
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 1, 30)), ct);
                    continue;
                }
                if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden) throw new AppException("apiAuth");
                if (!response.IsSuccessStatusCode) throw new AppException("apiHttp", ((int)response.StatusCode).ToString());
                JsonDocument document;
                try { document = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(ct), cancellationToken: ct); }
                catch (JsonException) { throw new AppException("apiFormat"); }
                using (document)
                {
                    var root = document.RootElement;
                    // Reject even partial GraphQL responses. Never mistake access errors for an empty library.
                    if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                        throw new AppException("apiGraphql");
                    if (!root.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Object) throw new AppException("apiFormat");
                    return data.Clone();
                }
            }
        }
    }

    public async Task<WorkspaceConnection> Connect(string slot, ConnectRequest input, CancellationToken ct)
    {
        var endpoint = Endpoint(input.Endpoint);
        var token = input.Token.Trim();
        if (token.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) token = token[7..].Trim();
        if (token.Length < 8 || token.Any(char.IsWhiteSpace)) throw new AppException("invalidToken");
        var categories = new List<Category>();
        var seen = new HashSet<string>();
        string? after = null;
        string libraryName = "";
        var count = 0;
        do
        {
            var data = await Query(endpoint, token, CategoriesQuery, new { first = PageSize, after, workspace = Origin(endpoint) }, ct);
            var library = Required(data, "desLibrary");
            libraryName = Text(library, "name");
            count = Required(library, "components").GetProperty("totalCount").GetInt32();
            var connection = Required(library, "componentTypes");
            foreach (var category in Nodes(connection)) categories.Add(ParseCategory(category)!);
            after = NextCursor(connection, seen);
        } while (after is not null);
        var name = string.IsNullOrWhiteSpace(input.Name) ? libraryName : input.Name.Trim();
        if (string.IsNullOrWhiteSpace(name)) name = endpoint.Host.Split('.')[0];
        return new(new(slot, name, endpoint.AbsoluteUri, count, categories.DistinctBy(x => x.Id).OrderBy(x => x.Path).ToArray()), token);
    }

    private async Task<(Component[] Components, InventoryDiagnostics Diagnostics, string Fingerprint)> ReadInventory(
        WorkspaceConnection workspace, Action<ScanProgress> progress, CancellationToken ct, string phase)
    {
        var byId = new Dictionary<string, Component>(StringComparer.Ordinal);
        var seen = new HashSet<string>();
        string? after = null;
        var endpoint = new Uri(workspace.Info.Endpoint);
        var reportedTotals = new List<int>();
        var changedComponents = new HashSet<string>(StringComparer.Ordinal);
        int pages = 0, rowsRead = 0;
        do
        {
            var data = await Query(endpoint, workspace.Token, ComponentsQuery,
                new { first = PageSize, after, workspace = Origin(endpoint) }, ct);
            var connection = Required(Required(data, "desLibrary"), "components");
            var pageTotal = connection.GetProperty("totalCount").GetInt32();
            if (pageTotal < 0) throw new AppException("apiFormat");
            pages++;
            reportedTotals.Add(pageTotal);
            var previousCount = byId.Count;
            foreach (var node in Nodes(connection))
            {
                var id = Text(node, "id");
                if (string.IsNullOrWhiteSpace(id)) throw new AppException("apiFormat");
                rowsRead++;
                // A row repeated across API pages is the same component, not a library duplicate.
                var component = new Component(id, Text(node, "name"), Text(node, "revisionId"), Text(node, "description"),
                    node.TryGetProperty("componentType", out var category) ? ParseCategory(category) : null,
                    node.TryGetProperty("folder", out var folder) ? Text(folder, "path") : "", [], [],
                    Required(node, "manufacturerParts").EnumerateArray().Select(p => new PartChoice(Text(p, "companyName"), Text(p, "partNumber"))).ToArray());
                if (byId.TryGetValue(id, out var previous) && (previous.Name != component.Name || previous.Revision != component.Revision ||
                    previous.Description != component.Description || previous.Category != component.Category || previous.Folder != component.Folder ||
                    !previous.PartChoices.SequenceEqual(component.PartChoices))) changedComponents.Add(id);
                byId[id] = component;
            }
            progress(new(workspace.Info.Slot, phase, byId.Count, pageTotal));
            after = NextCursor(connection, seen);
            if (after is not null && byId.Count == previousCount) throw new AppException("apiPaginationStalled");
        } while (after is not null);

        // Cursor termination defines the traversal. totalCount is retained as coverage evidence,
        // never used to discard already-read data or to invent an extra page/cursor.
        var totals = reportedTotals.Distinct().ToArray();
        var warnings = new List<string>();
        if (totals.Length > 1) warnings.Add("reportedTotalChanged");
        if (totals.Any(total => total != byId.Count)) warnings.Add("reportedCountMismatch");
        if (changedComponents.Count > 0) warnings.Add("componentChanged");
        var components = byId.Values.ToArray();
        var canonical = components.OrderBy(c => c.Id, StringComparer.Ordinal).Select(c => new {
            c.Id, c.Name, c.Revision, c.Description, c.Category, c.Folder,
            Parts = c.PartChoices.OrderBy(p => p.Manufacturer, StringComparer.Ordinal).ThenBy(p => p.Mpn, StringComparer.Ordinal)
        });
        var fingerprint = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(canonical)));
        return (components, new(workspace.Info.Slot, workspace.Info.Name, pages, rowsRead,
            byId.Count, rowsRead - byId.Count, totals, warnings), fingerprint);
    }

    public async Task<IReadOnlyList<Component>> Load(WorkspaceConnection workspace, Action<ScanProgress> progress, CancellationToken ct,
        Action<InventoryDiagnostics>? inventoryReport = null)
    {
        var inventory = await ReadInventory(workspace, progress, ct, "listing");
        var diagnostics = inventory.Diagnostics;
        // A stable under-reported counter is informational only after a second complete,
        // independent cursor traversal returns the same IDs and comparison-relevant metadata.
        if (diagnostics.ReportedTotals.Count == 1 && diagnostics.DistinctComponents > diagnostics.ReportedTotals[0] &&
            diagnostics.Warnings.SequenceEqual(new[] { "reportedCountMismatch" }))
        {
            try
            {
                var verification = await ReadInventory(workspace, progress, ct, "verifying");
                var matches = inventory.Fingerprint == verification.Fingerprint &&
                    diagnostics.ReportedTotals.SequenceEqual(verification.Diagnostics.ReportedTotals) &&
                    verification.Diagnostics.Warnings.SequenceEqual(new[] { "reportedCountMismatch" });
                diagnostics = diagnostics with {
                    Warnings = matches ? [] : diagnostics.Warnings.Append("inventoryVerificationMismatch").ToArray(),
                    Verification = new(matches, verification.Diagnostics.Pages, verification.Components.Length, verification.Diagnostics.ReportedTotals),
                    Notices = matches ? ["apiCountVerified"] : []
                };
            }
            catch (AppException ex) when (ex.Code != "apiAuth")
            {
                diagnostics = diagnostics with { Warnings = diagnostics.Warnings.Append("inventoryVerificationFailed").ToArray(),
                    Verification = new(false, 0, 0, [], ex.Code) };
            }
        }
        inventoryReport?.Invoke(diagnostics);
        var components = inventory.Components;
        var endpoint = new Uri(workspace.Info.Endpoint);

        var hydrated = new Component[components.Length];
        var completed = 0;
        // Small bounded concurrency avoids overwhelming workspace throttling.
        await Parallel.ForEachAsync(Enumerable.Range(0, components.Length), new ParallelOptions { MaxDegreeOfParallelism = 4, CancellationToken = ct }, async (i, token) =>
        {
            var component = components[i];
            try
            {
                var data = await Query(endpoint, workspace.Token, DetailsQuery, new { id = component.Id }, token);
                var node = Required(data, "desComponentById");
                if (Text(node, "id") != component.Id) throw new AppException("apiFormat");
                var details = Required(node, "details");
                hydrated[i] = component with { Parameters = Parameters(details, "parameters"), ItemParameters = Parameters(details, "itemParameters") };
            }
            catch (AppException ex) when (ex.Code != "apiAuth") { hydrated[i] = component with { DetailError = ex.Code }; }
            progress(new(workspace.Info.Slot, "details", Interlocked.Increment(ref completed), components.Length));
        });
        return hydrated;
    }

    private static string Origin(Uri endpoint) => endpoint.GetLeftPart(UriPartial.Authority);
    private static IReadOnlyList<Parameter> Parameters(JsonElement details, string name) => Required(details, name)
        .EnumerateArray().Select(p => new Parameter(Text(p, "name"), Text(p, "value"))).ToArray();
    private static IEnumerable<JsonElement> Nodes(JsonElement connection) => Required(connection, "nodes").EnumerateArray();
    private static JsonElement Required(JsonElement parent, string property)
    {
        if (parent.ValueKind != JsonValueKind.Object || !parent.TryGetProperty(property, out var result) || result.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
            throw new AppException("apiFormat");
        return result;
    }
    private static string Text(JsonElement element, string property) => element.ValueKind == JsonValueKind.Object &&
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : "";
    private static Category? ParseCategory(JsonElement element) => element.ValueKind != JsonValueKind.Object ? null :
        new(Text(element, "componentTypeId"), Text(element, "name"), Text(element, "path"));
    private static string? NextCursor(JsonElement connection, HashSet<string> seen)
    {
        var pageInfo = Required(connection, "pageInfo");
        if (!pageInfo.GetProperty("hasNextPage").GetBoolean()) return null;
        var cursor = Text(pageInfo, "endCursor");
        if (string.IsNullOrWhiteSpace(cursor)) throw new AppException("apiCursorMissing");
        if (!seen.Add(cursor)) throw new AppException("apiCursorRepeated");
        return cursor;
    }
}
