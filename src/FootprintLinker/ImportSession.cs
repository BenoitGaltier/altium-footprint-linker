using System.Security.Cryptography;
using System.Text;

namespace FootprintLinker;

public sealed class ImportJob(string kind)
{
    private readonly object sync = new();
    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string Kind { get; } = kind;
    public bool Running { get { lock (sync) return status == "running"; } }
    public CancellationTokenSource Cancellation { get; } = new();
    private string status = "running", phase = "starting";
    private int loaded, total; private string? error; private object? result;
    public void Progress(string value, int count, int maximum) { lock (sync) { phase = value; loaded = count; total = maximum; } }
    public void Complete(object value) { lock (sync) { result = value; status = "completed"; } }
    public void Fail(string code) { lock (sync) { error = code; status = code == "cancelled" ? "cancelled" : "failed"; } }
    public object Snapshot() { lock (sync) return new { Id, Kind, Status = status, Phase = phase, Loaded = loaded, Total = total, Error = error, Result = result }; }
}

public sealed class ImportSession
{
    private readonly AltiumClient altium;
    private readonly LocalStore? store;
    private readonly IHostApplicationLifetime lifetime;
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly object sync = new();
    private StoredState state = new(1, null, null, null, []);
    private LibraryFolder[] folders = [];
    private bool connected;
    private ImportPlan? plan; private ImportJob? job; private string? storageError;
    public ImportSession(AltiumClient altium, IHostApplicationLifetime lifetime, LocalStore? store = null)
    {
        this.altium = altium; this.store = store; this.lifetime = lifetime;
        try { if (store?.Load() is { } loaded) state = loaded with { FolderId = null }; }
        catch (AppException ex) { storageError = ex.Code; }
    }
    public object Current()
    {
        lock (sync) return new { Connection = state.Connection?.Info, HasSavedToken = state.Connection is not null,
            state.Directory, state.MatchField, Connected = connected, Job = job?.Snapshot(), StorageError = storageError,
            DataDirectory = store?.DirectoryPath, Journal = state.Journal.Values.ToArray() };
    }
    private void Idle() { if (job?.Running == true) throw new AppException("jobRunning", status: 409); }
    private void Persist()
    {
        if (storageError is not null) throw new AppException(storageError);
        store?.Save(state);
    }
    public async Task<object> Connect(ConnectRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Idle();
            if (string.IsNullOrWhiteSpace(request.Token))
            {
                if (state.Connection is null) throw new AppException("invalidToken");
                if (AltiumClient.Endpoint(request.Endpoint).AbsoluteUri != state.Connection.Info.Endpoint) throw new AppException("savedTokenMismatch");
                request = request with { Token = state.Connection.Token };
            }
            var connected = await altium.Connect("workspace", request, ct);
            var availableFolders = await altium.Folders(connected, ct);
            lock (sync)
            {
                var old = state; state = state with { Connection = connected };
                try { Persist(); } catch { state = old; throw; }
                folders = availableFolders; this.connected = true; plan = null;
            }
            return connected.Info;
        }
        finally { gate.Release(); }
    }
    public async Task<object> Analyze(AnalyzeRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Idle(); var connection = state.Connection ?? throw new AppException("connectFirst");
            if (!connected) throw new AppException("connectFirst");
            var matchField = Planner.ValidateMatchField(request.MatchField);
            var csv = CsvImport.Parse(request.Csv); var directory = Path.GetFullPath(request.Directory.Trim());
            if (!System.IO.Directory.Exists(directory)) throw new AppException("directoryMissing");
            lock (sync)
            {
                var old = state; state = state with { Directory = directory, MatchField = matchField, FolderId = null };
                try { Persist(); } catch { state = old; throw; }
                job?.Cancellation.Dispose(); job = new("analyze"); plan = null;
            }
            _ = RunAnalysis(job, connection, csv, directory, matchField);
            return job.Snapshot();
        }
        finally { gate.Release(); }
    }
    public async Task SetPreferences(PreferencesRequest request, CancellationToken ct)
    {
        var field = Planner.ValidateMatchField(request.MatchField);
        await gate.WaitAsync(ct);
        try
        {
            Idle(); lock (sync)
            {
                var previous = state; state = state with { MatchField = field };
                try { Persist(); } catch { state = previous; throw; }
                if (previous.MatchField != field) plan = null;
            }
        }
        finally { gate.Release(); }
    }
    private async Task RunAnalysis(ImportJob current, WorkspaceConnection connection, CsvRow[] csv, string directory, string matchField)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(current.Cancellation.Token, lifetime.ApplicationStopping);
        try
        {
            current.Progress("indexing", 0, 0);
            var catalog = await Task.Run(() => PcbLibrary.Catalog(directory, linked.Token), linked.Token);
            InventoryDiagnostics? diagnostics = null;
            var components = await altium.Load(connection, p => current.Progress(p.Phase, p.Loaded, p.Total), linked.Token, d => diagnostics = d);
            var warnings = catalog.Warnings.ToList();
            if (components.Any(c => c.DetailError is not null)) warnings.Add("unreadParameters");
            if (diagnostics is null) throw new AppException("apiFormat");
            warnings.AddRange(diagnostics.Warnings);
            var rows = Planner.Build(csv, components, catalog, matchField);
            var matched = rows.Where(r => r.ComponentId is not null).Select(r => r.ComponentId!).Distinct().ToArray();
            var details = new Dictionary<string, ComponentDetails>();
            foreach (var id in matched)
            {
                details[id] = await altium.Details(connection, id, linked.Token);
                current.Progress("matching", details.Count, matched.Length);
            }
            rows = rows.Select(r =>
            {
                if (r.ComponentId is null) return r;
                var detail = details[r.ComponentId];
                if (Planner.MatchValue(detail.Parameters, detail.ItemParameters, matchField) != r.Csv.Item || detail.Revision != r.Revision)
                    return r with { Status = "blocked", Reason = "componentChanged" };
                var destination = Planner.Destination(detail, folders);
                r = r with { ExistingFootprints = detail.Footprints.Select(f => f.Name).ToArray(), Destination = destination.Folder,
                    DestinationSource = destination.Source };
                if (r.Status != "ready") return r;
                var key = destination.Folder is null ? null : AssetKey(connection, destination.Folder.Id, r);
                var importedId = key is null ? null : state.Journal.GetValueOrDefault(key)?.FootprintId;
                bool linkedAlready = detail.Footprints.Any(f => f.Id == importedId || f.Name.Equals(r.InternalName, StringComparison.OrdinalIgnoreCase));
                return r with { Status = linkedAlready ? "alreadyLinked" : destination.Folder is null ? "blocked" : "ready",
                    Reason = !linkedAlready && destination.Folder is null ? destination.Source : null };
            }).ToArray();
            var result = new ImportPlan(Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow, connection.Info.Endpoint, directory, matchField, rows, diagnostics, warnings.ToArray());
            lock (sync) plan = result;
            current.Complete(result);
        }
        catch (OperationCanceledException) { current.Fail("cancelled"); }
        catch (AppException ex) { current.Fail(ex.Code); }
        catch (Exception) { current.Fail("analysisFailed"); }
    }
    public async Task<object> Apply(ApplyRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Idle();
            if (store is null) throw new AppException("persistenceRequired");
            var connection = state.Connection ?? throw new AppException("connectFirst");
            var selectedPlan = plan;
            if (selectedPlan is null || selectedPlan.Id != request.PlanId || selectedPlan.Endpoint != connection.Info.Endpoint) throw new AppException("planExpired", status: 409);
            var ids = request.RowIds.ToHashSet(StringComparer.Ordinal);
            var selected = selectedPlan.Rows.Where(r => ids.Contains(r.Id)).ToArray();
            if (selected.Length == 0 || selected.Length != ids.Count || selected.Any(r => r.Status != "ready" || r.Destination is null)) throw new AppException("invalidSelection");
            if (selectedPlan.Warnings.Length > 0) throw new AppException("partialPlan", status: 409);
            folders = await altium.Folders(connection, ct);
            Persist();
            lock (sync) { job?.Cancellation.Dispose(); job = new("apply"); }
            _ = RunApply(job, connection, selectedPlan, selected);
            return job.Snapshot();
        }
        finally { gate.Release(); }
    }
    public static string AssetKey(WorkspaceConnection connection, string folderId, PlanRow row) => Convert.ToHexString(SHA256.HashData(
        Encoding.UTF8.GetBytes(connection.Info.Endpoint + "\0" + folderId + "\0" + row.File!.Sha256 + "\0" + row.InternalName)));
    private void Record(string key, string stage, string? id, string name, string folderId)
    {
        lock (sync)
        {
            var existing = state.Journal.GetValueOrDefault(key);
            state.Journal[key] = new(key, existing?.Endpoint ?? state.Connection!.Info.Endpoint, existing?.FolderId ?? folderId, stage, id, name, DateTimeOffset.UtcNow);
            Persist(); // Must reach disk before a remote mutation begins.
        }
    }
    private async Task RunApply(ImportJob current, WorkspaceConnection connection, ImportPlan sourcePlan, PlanRow[] selected)
    {
        var results = new List<ApplyResult>();
        try
        {
            // A user's Cancel stops between rows. In-flight writes finish and are verified.
            foreach (var row in selected)
            {
                if (current.Cancellation.IsCancellationRequested || lifetime.ApplicationStopping.IsCancellationRequested) break;
                string? footprintId = null;
                try
                {
                    current.Progress("importing", results.Count, selected.Length);
                    var before = await altium.Details(connection, row.ComponentId!, lifetime.ApplicationStopping);
                    if (Planner.MatchValue(before.Parameters, before.ItemParameters, sourcePlan.MatchField) != row.Csv.Item || before.Revision != row.Revision) throw new AppException("componentChanged");
                    var destination = Planner.Destination(before, folders);
                    if (destination.Folder is null || destination.Folder.Id != row.Destination!.Id) throw new AppException("destinationChanged");
                    var folder = destination.Folder;
                    var file = row.File!;
                    if (!Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file.Path))).Equals(file.Sha256, StringComparison.Ordinal)) throw new AppException("fileChanged");
                    var key = AssetKey(connection, folder.Id, row);
                    var previous = state.Journal.GetValueOrDefault(key);
                    footprintId = previous?.FootprintId;
                    if (before.Footprints.Any(f => f.Id == footprintId || f.Name.Equals(row.InternalName, StringComparison.OrdinalIgnoreCase)))
                    { results.Add(new(row.Id, "alreadyLinked", footprintId, null)); continue; }
                    if (footprintId is null)
                    {
                        if (previous?.Stage == "creating") throw new AppException("creationOutcomeUnknown");
                        var bytes = PcbLibrary.Extract(file.Path, row.InternalName!);
                        // Check again after extraction to close the scan/extract file-change window.
                        if (Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(file.Path))) != file.Sha256) throw new AppException("fileChanged");
                        var filename = "footprint-" + file.Sha256[..16] + ".PcbLib";
                        var fileId = await altium.Upload(connection, bytes, filename, lifetime.ApplicationStopping);
                        Record(key, "creating", null, row.InternalName!, folder.Id);
                        try { footprintId = await altium.Create(connection, folder.Id, row.InternalName!, fileId, filename, file.Sha256, lifetime.ApplicationStopping, folder.UseNamingScheme); }
                        catch (AppException ex) when (ex.Code == "apiMutationRejected") { Record(key, "rejected", null, row.InternalName!, folder.Id); throw; }
                        catch (Exception ex) when (ex is AppException or HttpRequestException or OperationCanceledException)
                        { throw new AppException("creationOutcomeUnknown"); }
                        Record(key, "created", footprintId, row.InternalName!, folder.Id);
                    }
                    var linkKey = key + ":" + row.ComponentId;
                    var link = state.Journal.GetValueOrDefault(linkKey);
                    // Re-read after an import and before append; another user may have linked it meanwhile.
                    before = await altium.Details(connection, row.ComponentId!, lifetime.ApplicationStopping);
                    if (Planner.MatchValue(before.Parameters, before.ItemParameters, sourcePlan.MatchField) != row.Csv.Item || before.Revision != row.Revision) throw new AppException("componentChanged");
                    if (Planner.Destination(before, folders).Folder?.Id != folder.Id) throw new AppException("destinationChanged");
                    if (!before.Footprints.Any(f => f.Id == footprintId))
                    {
                        if (link?.Stage == "adding") throw new AppException("linkOutcomeUnknown");
                        Record(linkKey, "adding", footprintId, row.InternalName!, folder.Id);
                        try { await altium.Add(connection, row.ComponentId!, footprintId, lifetime.ApplicationStopping); }
                        catch (AppException ex) when (ex.Code == "apiMutationRejected") { Record(linkKey, "rejected", footprintId, row.InternalName!, folder.Id); throw; }
                        catch (Exception ex) when (ex is AppException or HttpRequestException or OperationCanceledException)
                        {
                            var reconciled = await altium.Details(connection, row.ComponentId!, lifetime.ApplicationStopping);
                            if (!reconciled.Footprints.Any(f => f.Id == footprintId)) throw new AppException("linkOutcomeUnknown");
                        }
                    }
                    ComponentDetails after = before;
                    for (int attempt = 0; attempt < 4; attempt++)
                    {
                        after = await altium.Details(connection, row.ComponentId!, lifetime.ApplicationStopping);
                        if (after.Footprints.Any(f => f.Id == footprintId)) break;
                        await Task.Delay(TimeSpan.FromSeconds(attempt + 1), lifetime.ApplicationStopping);
                    }
                    if (!after.Footprints.Any(f => f.Id == footprintId) || before.Footprints.Any(f => !after.Footprints.Any(a => a.Id == f.Id)) ||
                        (before.Footprints.Length > 0 && after.Footprints.FirstOrDefault()?.Id != before.Footprints[0].Id)) throw new AppException("verificationFailed");
                    Record(linkKey, "complete", footprintId, row.InternalName!, folder.Id);
                    results.Add(new(row.Id, "imported", footprintId, null));
                }
                catch (AppException ex) { results.Add(new(row.Id,
                    ex.Code is "creationOutcomeUnknown" or "linkOutcomeUnknown" or "verificationFailed" ? "unknown" : "failed", footprintId, ex.Code)); }
                catch (OperationCanceledException) { results.Add(new(row.Id, "unknown", footprintId, "operationInterrupted")); break; }
                catch (Exception) { results.Add(new(row.Id, "failed", footprintId, "importFailed")); }
            }
            current.Progress("finished", results.Count, selected.Length);
            lock (sync) plan = null;
            current.Complete(new { Plan = sourcePlan, Results = results, Cancelled = results.Count < selected.Length, Requested = selected.Length });
        }
        catch (Exception) { lock (sync) plan = null; current.Fail("importFailed"); }
    }
    public ImportJob Job(string id) { lock (sync) return job?.Id == id ? job : throw new AppException("jobNotFound", status: 404); }
    public async Task Recover(RecoveryRequest request, CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            Idle(); var entry = state.Journal.GetValueOrDefault(request.Key);
            if (entry is null || entry.Stage is not ("creating" or "adding")) throw new AppException("recoveryInvalid");
            var connection = state.Connection ?? throw new AppException("connectFirst");
            if (entry.Endpoint != connection.Info.Endpoint) throw new AppException("recoveryMismatch");
            if (request.FootprintId == "ABSENT") Record(request.Key, "manuallyVerifiedAbsent", entry.FootprintId, entry.Name, entry.FolderId);
            else
            {
                await altium.VerifyFootprint(connection, request.FootprintId, entry.Name, entry.FolderId, ct);
                if (entry.Stage == "adding")
                {
                    var componentId = request.Key[(request.Key.IndexOf(':') + 1)..];
                    var details = await altium.Details(connection, componentId, ct);
                    if (!details.Footprints.Any(f => f.Id == request.FootprintId)) throw new AppException("recoveryMismatch");
                }
                Record(request.Key, "manuallyVerifiedPresent", request.FootprintId, entry.Name, entry.FolderId);
            }
            lock (sync) plan = null;
        }
        finally { gate.Release(); }
    }
    public async Task Forget(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try { Idle(); lock (sync) { store?.Clear(); state = new(1, null, null, null, []); folders = []; connected = false; plan = null; storageError = null; } }
        finally { gate.Release(); }
    }
}
