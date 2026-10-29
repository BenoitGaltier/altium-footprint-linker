using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace FootprintLinker;

public sealed record FootprintRef(string Id, string Name, string? FolderId = null, string? FolderPath = null);
public sealed record ComponentDetails(string Id, string Name, string Revision, Parameter[] Parameters, Parameter[] ItemParameters, FootprintRef[] Footprints,
    string FolderPath = "");

public sealed partial class AltiumClient
{
    public const string FoldersQuery = """
        query LinkerFolders($workspace: String!) {
          desLibrary(workspaceUrl: $workspace) { folders { folderId path folderType itemNamingSchemeTemplate parent { folderId } } }
        }
        """;
    public const string FullDetailsQuery = """
        query LinkerDetails($id: ID!) {
          desComponentById(id: $id) {
            id name revisionId folder { path } details {
              parameters { name value } itemParameters { name value }
              footprints { id name folder { folderId path } }
            }
          }
        }
        """;
    public const string CreateQuery = """
        mutation LinkerCreate($input: DesCreateFootprintInput!) {
          desCreateFootprint(input: $input) { footprintId errors { message } }
        }
        """;
    public const string AddQuery = """
        mutation LinkerAdd($input: DesAddFootprintToComponentInput!) {
          desAddFootprintToComponent(input: $input) { errors { message } }
        }
        """;
    public async Task<LibraryFolder[]> Folders(WorkspaceConnection workspace, CancellationToken ct)
    {
        var data = await Query(new(workspace.Info.Endpoint), workspace.Token, FoldersQuery, new { workspace = Origin(new(workspace.Info.Endpoint)) }, ct);
        var all = Required(Required(data, "desLibrary"), "folders").EnumerateArray().ToArray();
        var byId = all.ToDictionary(f => Text(f, "folderId"), StringComparer.Ordinal);
        bool UsesScheme(JsonElement folder)
        {
            var seen = new HashSet<string>();
            while (seen.Add(Text(folder, "folderId")))
            {
                if (Text(folder, "itemNamingSchemeTemplate").Length > 0) return true;
                if (!folder.TryGetProperty("parent", out var parent) || !byId.TryGetValue(Text(parent, "folderId"), out folder)) break;
            }
            return false;
        }
        return all
            .Where(f => Text(f, "folderType") == "PCB_COMPONENT_LIBRARY")
            .Select(f => new LibraryFolder(Text(f, "folderId"), Text(f, "path"), UsesScheme(f))).OrderBy(f => f.Path).ToArray();
    }
    public async Task<ComponentDetails> Details(WorkspaceConnection workspace, string id, CancellationToken ct)
    {
        var data = await Query(new(workspace.Info.Endpoint), workspace.Token, FullDetailsQuery, new { id }, ct);
        var node = Required(data, "desComponentById"); var details = Required(node, "details");
        if (Text(node, "id") != id) throw new AppException("apiFormat");
        return new(id, Text(node, "name"), Text(node, "revisionId"), Parameters(details, "parameters").ToArray(),
            Parameters(details, "itemParameters").ToArray(), Required(details, "footprints").EnumerateArray()
                .Select(f => new FootprintRef(Text(f, "id"), Text(f, "name"),
                    f.TryGetProperty("folder", out var folder) ? Text(folder, "folderId") : null,
                    f.TryGetProperty("folder", out var folderPath) ? Text(folderPath, "path") : null)).ToArray(),
            node.TryGetProperty("folder", out var componentFolder) ? Text(componentFolder, "path") : "");
    }
    public async Task<string> Upload(WorkspaceConnection workspace, byte[] bytes, string filename, CancellationToken ct)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(workspace.Info.Endpoint), "/api/files/File/Upload"));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", workspace.Token);
        using var form = new MultipartFormDataContent();
        var content = new ByteArrayContent(bytes); content.Headers.ContentType = new("application/octet-stream");
        form.Add(content, "file", filename); request.Content = form;
        try
        {
            using var response = await http.SendAsync(request, ct);
            if (response.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden) throw new AppException("apiAuth");
            if (!response.IsSuccessStatusCode) throw new AppException("uploadFailed", ((int)response.StatusCode).ToString());
            var id = (await response.Content.ReadAsStringAsync(ct)).Trim().Trim('"');
            if (id.Length is < 1 or > 512 || id.Any(c => char.IsControl(c) || c is '<' or '>' or '{' or '}')) throw new AppException("apiFormat");
            return id;
        }
        catch (HttpRequestException) { throw new AppException("apiNetwork"); }
    }
    public async Task<string> Create(WorkspaceConnection workspace, string folderId, string name, string fileId, string filename, string sha256, CancellationToken ct, bool useNamingScheme = false)
    {
        var data = await Query(new(workspace.Info.Endpoint), workspace.Token, CreateQuery, new { input = new {
            workspaceUrl = Origin(new(workspace.Info.Endpoint)), folderId, name = useNamingScheme ? null : name, comment = name,
            description = "Imported by Footprint Linker",
            files = new[] { new { fileId, relativePath = "Released/" + filename } },
            parameters = new[] { new { name = "FootprintLinker_SHA256", value = sha256 }, new { name = "FootprintLinker_SourceName", value = name } }
        } }, ct, readOnly: false);
        var payload = Required(data, "desCreateFootprint"); CheckPayload(payload);
        var id = Text(payload, "footprintId"); if (id.Length == 0) throw new AppException("apiFormat"); return id;
    }
    public async Task Add(WorkspaceConnection workspace, string componentId, string footprintId, CancellationToken ct)
    {
        // Omitting addAtIndex appends; index 0 would replace the default footprint.
        var data = await Query(new(workspace.Info.Endpoint), workspace.Token, AddQuery, new { input = new { componentId, footprintId } }, ct, readOnly: false);
        CheckPayload(Required(data, "desAddFootprintToComponent"));
    }
    private static void CheckPayload(JsonElement payload)
    {
        if (Required(payload, "errors").GetArrayLength() != 0) throw new AppException("apiMutationRejected");
    }
    public async Task VerifyFootprint(WorkspaceConnection workspace, string id, string name, string folderId, CancellationToken ct)
    {
        const string query = "query LinkerVerify($id: ID!) { desFootprintById(id: $id) { id name comment folder { folderId } } }";
        var data = await Query(new(workspace.Info.Endpoint), workspace.Token, query, new { id }, ct);
        var node = Required(data, "desFootprintById");
        if (Text(node, "id") != id || Text(Required(node, "folder"), "folderId") != folderId || !(Text(node, "name").Equals(name, StringComparison.OrdinalIgnoreCase) || Text(node, "comment").Equals(name, StringComparison.OrdinalIgnoreCase)))
            throw new AppException("recoveryMismatch");
    }
}
