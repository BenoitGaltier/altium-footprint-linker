namespace FootprintLinker;

public sealed record CsvRow(int Line, string Item, string Description, string Supplier, string SupplierName, string SupplierItemNo, string Footprint);
public sealed record LibraryFolder(string Id, string Path, bool UseNamingScheme = false);
public sealed record LibraryFile(string Path, string RelativePath, string Sha256, string[] Names);
public sealed record LibraryCatalog(LibraryFile[] Files, string[] Warnings);
public sealed record PlanRow(string Id, CsvRow Csv, string? ComponentId, string? ComponentName, string? Revision,
    string Status, string? Reason, LibraryFile? File, string? InternalName, string[] ExistingFootprints, int Matches,
    LibraryFolder? Destination = null, string? DestinationSource = null);
public sealed record ImportPlan(string Id, DateTimeOffset CreatedAt, string Endpoint, string Directory, string MatchField,
    PlanRow[] Rows, InventoryDiagnostics Diagnostics, string[] Warnings);
public sealed record AnalyzeRequest(string Csv, string Directory, string MatchField = "AU_ID");
public sealed record ApplyRequest(string PlanId, string[] RowIds);
public sealed record JournalEntry(string Key, string Endpoint, string FolderId, string Stage, string? FootprintId, string Name, DateTimeOffset UpdatedAt);
public sealed record ApplyResult(string RowId, string Status, string? FootprintId, string? Error);
public sealed record RecoveryRequest(string Key, string FootprintId);
public sealed record StoredState(int FormatVersion, WorkspaceConnection? Connection, string? Directory, string? FolderId,
    Dictionary<string, JournalEntry> Journal, string MatchField = "AU_ID");
public sealed record DirectoryPickRequest(string? Directory = null);
public sealed record DirectoryPickResult(string? Directory, bool Cancelled);
public sealed record PreferencesRequest(string MatchField);
