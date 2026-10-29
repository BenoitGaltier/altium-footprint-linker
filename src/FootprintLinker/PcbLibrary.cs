using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace FootprintLinker;

// Windows structured storage preserves every primitive, embedded 3D model and unknown stream.
// Only the library index and SectionKeys are reduced when extracting one footprint.
public static class PcbLibrary
{
    [ComImport, Guid("0000000B-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IStorage
    {
        void CreateStream([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out IStream stream);
        void OpenStream([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr reserved, uint mode, uint r2, out IStream stream);
        void CreateStorage([MarshalAs(UnmanagedType.LPWStr)] string name, uint mode, uint r1, uint r2, out IStorage storage);
        void OpenStorage([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage? priority, uint mode, IntPtr exclude, uint reserved, out IStorage storage);
        void CopyTo(uint count, IntPtr excludeIds, IntPtr excludeNames, IStorage destination);
        void MoveElementTo([MarshalAs(UnmanagedType.LPWStr)] string name, IStorage destination, [MarshalAs(UnmanagedType.LPWStr)] string newName, uint flags);
        void Commit(uint flags); void Revert();
        void EnumElements(uint r1, IntPtr r2, uint r3, out IEnumStorage entries);
        void DestroyElement([MarshalAs(UnmanagedType.LPWStr)] string name);
        void RenameElement([MarshalAs(UnmanagedType.LPWStr)] string oldName, [MarshalAs(UnmanagedType.LPWStr)] string newName);
        void SetElementTimes([MarshalAs(UnmanagedType.LPWStr)] string name, IntPtr creation, IntPtr access, IntPtr modification);
        void SetClass(ref Guid clsid); void SetStateBits(uint bits, uint mask); void Stat(out STATSTG stat, uint flags);
    }
    [ComImport, Guid("0000000D-0000-0000-C000-000000000046"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumStorage
    {
        [PreserveSig] int Next(uint count, out STATSTG stat, out uint fetched);
        void Skip(uint count); void Reset(); void Clone(out IEnumStorage clone);
    }
    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void StgOpenStorage(string name, IStorage? priority, uint mode, IntPtr exclude, uint reserved, out IStorage storage);
    [DllImport("ole32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void StgCreateDocfile(string name, uint mode, uint reserved, out IStorage storage);
    private static Encoding Ansi { get { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); return Encoding.GetEncoding(1252); } }
    private static void Release(object value) { if (OperatingSystem.IsWindows()) Marshal.ReleaseComObject(value); }
    private static IStorage Open(string path)
    {
        if (!OperatingSystem.IsWindows()) throw new AppException("storagePlatform");
        StgOpenStorage(path, null, 0x20, IntPtr.Zero, 0, out var result); return result;
    }
    private static byte[] Read(IStorage storage, string name)
    {
        storage.OpenStream(name, IntPtr.Zero, 0x10, 0, out var stream);
        try
        {
            stream.Stat(out var stat, 1);
            if (stat.cbSize is < 0 or > 256_000_000) throw new InvalidDataException();
            var data = new byte[(int)stat.cbSize]; var count = Marshal.AllocHGlobal(4);
            try { stream.Read(data, data.Length, count); if (Marshal.ReadInt32(count) != data.Length) throw new EndOfStreamException(); }
            finally { Marshal.FreeHGlobal(count); }
            return data;
        }
        finally { Release(stream); }
    }
    private static void Write(IStorage storage, string name, byte[] bytes)
    {
        storage.CreateStream(name, 0x1012, 0, 0, out var stream);
        try { stream.Write(bytes, bytes.Length, IntPtr.Zero); stream.Commit(0); } finally { Release(stream); }
    }
    private static byte[] LibraryData(IStorage root)
    {
        root.OpenStorage("Library", null, 0x10, IntPtr.Zero, 0, out var library);
        try { return Read(library, "Data"); } finally { Release(library); }
    }
    private sealed record NameBlock(string Name, byte[] Bytes);
    private static NameBlock Block(BinaryReader reader)
    {
        var start = reader.BaseStream.Position; var length = reader.ReadInt32() & 0xffffff;
        if (length < 1 || length > 1_000_000 || length > reader.BaseStream.Length - reader.BaseStream.Position) throw new InvalidDataException();
        var bytes = reader.ReadBytes(length); var chars = bytes[0];
        if (chars > length - 1) throw new InvalidDataException();
        return new(Ansi.GetString(bytes, 1, chars).TrimEnd('\0'), BitConverter.GetBytes(length).Concat(bytes).ToArray());
    }
    private static (byte[] Header, NameBlock[] Names) Index(byte[] data)
    {
        using var reader = new BinaryReader(new MemoryStream(data));
        var size = reader.ReadInt32() & 0xffffff;
        if (size < 1 || size > data.Length - 8) throw new InvalidDataException();
        var header = reader.ReadBytes(size); var count = reader.ReadUInt32();
        if (count is 0 or > 100_000) throw new InvalidDataException();
        var names = Enumerable.Range(0, (int)count).Select(_ => Block(reader)).ToArray();
        if (names.Any(n => string.IsNullOrWhiteSpace(n.Name)) || names.Select(n => n.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length) throw new InvalidDataException();
        return (header, names);
    }
    private static Dictionary<string, NameBlock> Sections(IStorage root)
    {
        var result = new Dictionary<string, NameBlock>(StringComparer.OrdinalIgnoreCase);
        var entries = Entries(root);
        if (!entries.Contains("SectionKeys", StringComparer.OrdinalIgnoreCase)) return result;
        using var reader = new BinaryReader(new MemoryStream(Read(root, "SectionKeys")));
        var count = reader.ReadInt32(); if (count is < 0 or > 100_000) throw new InvalidDataException();
        for (int i = 0; i < count; i++) { var name = Block(reader); result.Add(name.Name, Block(reader)); }
        return result;
    }
    private static string[] Entries(IStorage storage)
    {
        storage.EnumElements(0, IntPtr.Zero, 0, out var entries);
        try { var list = new List<string>(); while (entries.Next(1, out var stat, out var fetched) == 0 && fetched == 1) list.Add(stat.pwcsName); return list.ToArray(); }
        finally { Release(entries); }
    }
    private static string Section(string name, Dictionary<string, NameBlock> sections) => sections.TryGetValue(name, out var block)
        ? block.Name : Regex.Replace(name, @"[\\/:*?""<>|]", "_")[..Math.Min(31, name.Length)];
    public static byte[] RemoveManagedBindings(byte[] bytes)
    {
        var text = Encoding.Latin1.GetString(bytes);
        var replacements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase) {
            ["ITEMGUID"] = "ORIGGUID", ["REVISIONGUID"] = "SOURCE_REVID", ["VAULTGUID"] = "ORIGVAULT", ["FOLDERGUID"] = "ORIGFOLDID"
        };
        text = Regex.Replace(text, @"(?<=\|)(ITEMGUID|REVISIONGUID|VAULTGUID|FOLDERGUID)=\{?([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})\}?(?=[|\x00])", match =>
            replacements[match.Groups[1].Value] + "=" + new string(' ', match.Length - match.Groups[1].Length - 1), RegexOptions.IgnoreCase);
        var result = Encoding.Latin1.GetBytes(text);
        if (result.Length != bytes.Length) throw new InvalidDataException();
        return result;
    }
    private static void CleanBindings(IStorage storage)
    {
        storage.EnumElements(0, IntPtr.Zero, 0, out var iterator);
        var entries = new List<(string Name, int Type)>();
        try { while (iterator.Next(1, out var stat, out var fetched) == 0 && fetched == 1) entries.Add((stat.pwcsName, stat.type)); }
        finally { Release(iterator); }
        foreach (var entry in entries)
        {
            if (entry.Type == 1)
            {
                storage.OpenStorage(entry.Name, null, 0x12, IntPtr.Zero, 0, out var child);
                try { CleanBindings(child); child.Commit(0); } finally { Release(child); }
            }
            else if (entry.Type == 2)
            {
                var original = Read(storage, entry.Name); var clean = RemoveManagedBindings(original);
                if (!original.AsSpan().SequenceEqual(clean)) Write(storage, entry.Name, clean);
            }
        }
    }
    public static string[] Names(string path)
    {
        var root = Open(path);
        try { return Index(LibraryData(root)).Names.Select(n => n.Name).ToArray(); }
        finally { Release(root); }
    }
    public static byte[] Extract(string path, string name)
    {
        var source = Open(path); var temporary = Path.Combine(Path.GetTempPath(), "FootprintLinker-" + Guid.NewGuid().ToString("N") + ".PcbLib");
        try
        {
            var index = Index(LibraryData(source));
            var selected = index.Names.SingleOrDefault(n => n.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? throw new AppException("footprintMissing");
            var sections = Sections(source);
            StgCreateDocfile(temporary, 0x1012, 0, out var target);
            try
            {
                source.CopyTo(0, IntPtr.Zero, IntPtr.Zero, target);
                foreach (var other in index.Names.Where(n => n != selected)) target.DestroyElement(Section(other.Name, sections));
                target.OpenStorage("Library", null, 0x12, IntPtr.Zero, 0, out var library);
                try
                {
                    var header = Ansi.GetBytes(Regex.Replace(Ansi.GetString(index.Header), @"(?<=\|WEIGHT=)\d+", "1", RegexOptions.IgnoreCase));
                    using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
                    writer.Write(header.Length); writer.Write(header); writer.Write(1); writer.Write(selected.Bytes);
                    Write(library, "Data", data.ToArray());
                    // This optional index is regenerated by Altium; retaining rows for removed footprints is misleading.
                    if (Entries(library).Contains("ComponentParamsTOC", StringComparer.OrdinalIgnoreCase)) library.DestroyElement("ComponentParamsTOC");
                    library.Commit(0);
                }
                finally { Release(library); }
                if (Entries(target).Contains("SectionKeys", StringComparer.OrdinalIgnoreCase))
                {
                    using var data = new MemoryStream(); using var writer = new BinaryWriter(data);
                    var mapped = sections.TryGetValue(selected.Name, out var mappedBlock); writer.Write(mapped ? 1 : 0);
                    if (mapped) { writer.Write(selected.Bytes); writer.Write(mappedBlock!.Bytes); }
                    Write(target, "SectionKeys", data.ToArray());
                }
                CleanBindings(target);
                target.Commit(0);
            }
            finally { Release(target); }
            if (!Names(temporary).SequenceEqual(new[] { selected.Name })) throw new InvalidDataException();
            return File.ReadAllBytes(temporary);
        }
        finally { Release(source); File.Delete(temporary); }
    }
    public static LibraryCatalog Catalog(string directory, CancellationToken ct)
    {
        if (!Directory.Exists(directory)) throw new AppException("directoryMissing");
        var files = new List<LibraryFile>(); var warnings = new List<string>();
        try
        {
            var options = new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false };
            foreach (var path in Directory.EnumerateFiles(directory, "*", options).Where(p => Path.GetExtension(p).Equals(".PcbLib", StringComparison.OrdinalIgnoreCase)))
            {
                ct.ThrowIfCancellationRequested();
                if (files.Count >= 20_000) throw new AppException("directoryTooLarge");
                try
                {
                    if (new FileInfo(path).Length > 256_000_000) throw new InvalidDataException();
                    var bytes = File.ReadAllBytes(path); var names = Names(path);
                    files.Add(new(path, Path.GetRelativePath(directory, path), Convert.ToHexString(SHA256.HashData(bytes)), names));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or COMException or InvalidDataException or ArgumentException)
                { warnings.Add(Path.GetRelativePath(directory, path) + ": invalidPcbLib"); }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { throw new AppException("directoryUnreadable"); }
        return new(files.ToArray(), warnings.ToArray());
    }
}
