namespace FootprintLinker.Tests;

internal sealed class FakeDirectoryPicker : IDirectoryPicker
{
    public DirectoryPickResult Result { get; set; } = new(null, true);
    public string? InitialDirectory { get; private set; }
    public Task<DirectoryPickResult> Pick(string? initialDirectory, CancellationToken ct)
    { ct.ThrowIfCancellationRequested(); InitialDirectory = initialDirectory; return Task.FromResult(Result); }
}
