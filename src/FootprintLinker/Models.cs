namespace FootprintLinker;

public sealed record Category(string Id, string Name, string Path);
public sealed record Parameter(string Name, string Value);
public sealed record PartChoice(string Manufacturer, string Mpn);
public sealed record Component(
    string Id, string Name, string Revision, string Description, Category? Category,
    string Folder, IReadOnlyList<Parameter> Parameters, IReadOnlyList<Parameter> ItemParameters,
    IReadOnlyList<PartChoice> PartChoices, string? DetailError = null);
public sealed record WorkspaceInfo(string Slot, string Name, string Endpoint, int ComponentCount, IReadOnlyList<Category> Categories, bool Restored = false);
public sealed record WorkspaceConnection(WorkspaceInfo Info, string Token);
public sealed record ConnectRequest(string Name, string Endpoint, string Token);
public sealed record InventoryVerification(bool Matches, int Pages, int DistinctComponents,
    IReadOnlyList<int> ReportedTotals, string? ErrorCode = null);
public sealed record InventoryDiagnostics(string Workspace, string WorkspaceName, int Pages, int RowsRead,
    int DistinctComponents, int RepeatedRows, IReadOnlyList<int> ReportedTotals, IReadOnlyList<string> Warnings,
    InventoryVerification? Verification = null, IReadOnlyList<string>? Notices = null);
public sealed record ScanProgress(string Workspace, string Phase, int Loaded, int Total);
public sealed class AppException(string code, string? detail = null, int status = 400) : Exception(detail ?? code)
{
    public string Code { get; } = code;
    public string? Detail { get; } = detail;
    public int Status { get; } = status;
}
