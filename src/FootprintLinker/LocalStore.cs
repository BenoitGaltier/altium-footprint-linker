using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FootprintLinker;

public interface IDataProtector
{
    byte[] Protect(byte[] data);
    byte[] Unprotect(byte[] data);
}

public sealed class WindowsDataProtector : IDataProtector
{
    [StructLayout(LayoutKind.Sequential)]
    private struct Blob { public int Length; public IntPtr Data; }
    [DllImport("crypt32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptProtectData(ref Blob input, string description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("crypt32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CryptUnprotectData(ref Blob input, IntPtr description, IntPtr entropy,
        IntPtr reserved, IntPtr prompt, uint flags, out Blob output);
    [DllImport("kernel32.dll")]
    private static extern IntPtr LocalFree(IntPtr memory);

    public byte[] Protect(byte[] data) => Transform(data, true);
    public byte[] Unprotect(byte[] data) => Transform(data, false);
    private static byte[] Transform(byte[] data, bool encrypt)
    {
        if (!OperatingSystem.IsWindows()) throw new AppException("storagePlatform");
        var input = new Blob { Length = data.Length, Data = Marshal.AllocHGlobal(data.Length) };
        Blob output = default;
        try
        {
            Marshal.Copy(data, 0, input.Data, data.Length);
            // No LOCAL_MACHINE flag: Windows binds the ciphertext to this user account.
            var success = encrypt
                ? CryptProtectData(ref input, "Footprint Linker local state", IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output)
                : CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 1, out output);
            if (!success) throw new AppException("storageCrypto");
            var result = new byte[output.Length];
            Marshal.Copy(output.Data, result, 0, result.Length);
            return result;
        }
        finally
        {
            if (input.Data != IntPtr.Zero) { Marshal.Copy(new byte[input.Length], 0, input.Data, input.Length); Marshal.FreeHGlobal(input.Data); }
            if (output.Data != IntPtr.Zero) { if (!encrypt) Marshal.Copy(new byte[output.Length], 0, output.Data, output.Length); LocalFree(output.Data); }
        }
    }
}

public sealed class LocalStore : IDisposable
{
    private static readonly byte[] Header = Encoding.ASCII.GetBytes("FLINK1\0");
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IDataProtector protector;
    private readonly FileStream lease;
    private readonly object sync = new();
    public string DirectoryPath { get; }
    public string FilePath => Path.Combine(DirectoryPath, "state.dat");
    public LocalStore(string directory, IDataProtector? protector = null)
    {
        DirectoryPath = Path.GetFullPath(directory);
        this.protector = protector ?? new WindowsDataProtector();
        try
        {
            Directory.CreateDirectory(DirectoryPath);
            lease = new FileStream(Path.Combine(DirectoryPath, "session.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException) { throw new AppException("storageInUse"); }
        catch (UnauthorizedAccessException) { throw new AppException("storageWrite"); }
    }
    public StoredState? Load()
    {
        lock (sync)
        {
            if (!File.Exists(FilePath)) return null;
            byte[]? plain = null;
            try
            {
                var file = File.ReadAllBytes(FilePath);
                if (!file.AsSpan().StartsWith(Header)) throw new AppException("storageRead");
                plain = protector.Unprotect(file[Header.Length..]);
                var state = JsonSerializer.Deserialize<StoredState>(plain, Json) ?? throw new AppException("storageRead");
                if (state.FormatVersion != 1) throw new AppException("storageVersion");
                ValidateConnection(state.Connection, "workspace");
                if (state.Journal is null) throw new AppException("storageRead");
                return state;
            }
            catch (JsonException) { throw new AppException("storageRead"); }
            catch (IOException) { throw new AppException("storageRead"); }
            catch (UnauthorizedAccessException) { throw new AppException("storageRead"); }
            finally { if (plain is not null) CryptographicOperations.ZeroMemory(plain); }
        }
    }
    private static void ValidateConnection(WorkspaceConnection? connection, string slot)
    {
        if (connection is null) return;
        if (connection.Info.Slot != slot || string.IsNullOrWhiteSpace(connection.Token) ||
            AltiumClient.Endpoint(connection.Info.Endpoint).AbsoluteUri != connection.Info.Endpoint) throw new AppException("storageRead");
    }
    public void Save(StoredState state)
    {
        lock (sync)
        {
            byte[]? plain = null;
            var temporary = Path.Combine(DirectoryPath, ".state-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                plain = JsonSerializer.SerializeToUtf8Bytes(state, Json);
                var encrypted = protector.Protect(plain);
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                { stream.Write(Header); stream.Write(encrypted); stream.Flush(true); }
                if (File.Exists(FilePath)) File.Replace(temporary, FilePath, FilePath + ".bak", true);
                else File.Move(temporary, FilePath);
            }
            catch (IOException) { throw new AppException("storageWrite"); }
            catch (UnauthorizedAccessException) { throw new AppException("storageWrite"); }
            finally
            {
                if (plain is not null) CryptographicOperations.ZeroMemory(plain);
                try { File.Delete(temporary); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
    }
    public void Clear()
    {
        lock (sync)
        {
            try { File.Delete(FilePath); File.Delete(FilePath + ".bak"); }
            catch (IOException) { throw new AppException("storageWrite"); }
            catch (UnauthorizedAccessException) { throw new AppException("storageWrite"); }
        }
    }
    public void Dispose() => lease.Dispose();
}
