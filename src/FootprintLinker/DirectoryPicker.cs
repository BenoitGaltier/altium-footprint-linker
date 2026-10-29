using System.Runtime.InteropServices;

namespace FootprintLinker;

public interface IDirectoryPicker
{
    Task<DirectoryPickResult> Pick(string? initialDirectory, CancellationToken ct);
}

public sealed class WindowsDirectoryPicker : IDirectoryPicker
{
    private readonly SemaphoreSlim gate = new(1, 1);
    [ComImport, Guid("42F85136-DB7E-439C-85F1-E4075D135FC8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IFileDialog
    {
        [PreserveSig] int Show(IntPtr owner);
        void SetFileTypes(uint count, IntPtr filters);
        void SetFileTypeIndex(uint index); void GetFileTypeIndex(out uint index);
        void Advise(IntPtr events, out uint cookie); void Unadvise(uint cookie);
        void SetOptions(uint options); void GetOptions(out uint options);
        void SetDefaultFolder(IShellItem folder); void SetFolder(IShellItem folder);
        void GetFolder(out IShellItem folder); void GetCurrentSelection(out IShellItem item);
        void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string name); void GetFileName(out IntPtr name);
        void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string title);
        void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string text);
        void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string label);
        void GetResult(out IShellItem result);
        void AddPlace(IShellItem item, uint alignment);
        void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string extension);
        void Close(int result); void SetClientGuid(ref Guid guid); void ClearClientData(); void SetFilter(IntPtr filter);
    }
    [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellItem
    {
        void BindToHandler(IntPtr context, ref Guid handler, ref Guid iid, out IntPtr result);
        void GetParent(out IShellItem parent);
        void GetDisplayName(uint kind, out IntPtr name);
        void GetAttributes(uint mask, out uint attributes);
        void Compare(IShellItem other, uint hint, out int order);
    }
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    private static extern void SHCreateItemFromParsingName(string path, IntPtr context, ref Guid iid, out IShellItem item);
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();

    public async Task<DirectoryPickResult> Pick(string? initialDirectory, CancellationToken ct)
    {
        if (!OperatingSystem.IsWindows()) throw new AppException("pickerPlatform");
        if (!await gate.WaitAsync(0, ct)) throw new AppException("pickerBusy", status: 409);
        try
        {
            var result = new TaskCompletionSource<DirectoryPickResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            // The common Windows dialog needs its own STA thread; the HTTP server stays responsive.
            var thread = new Thread(() =>
            {
                try { result.SetResult(Show(initialDirectory)); }
                catch (Exception ex) when (ex is COMException or UnauthorizedAccessException or ArgumentException)
                { result.SetException(new AppException("pickerFailed")); }
                catch (Exception ex) { result.SetException(ex); }
            }) { IsBackground = true, Name = "Footprint Linker folder picker" };
            thread.SetApartmentState(ApartmentState.STA); thread.Start();
            return await result.Task;
        }
        finally { gate.Release(); }
    }
    private static DirectoryPickResult Show(string? directory)
    {
        if (!OperatingSystem.IsWindows()) throw new AppException("pickerPlatform");
        var type = Type.GetTypeFromCLSID(new Guid("DC1C5A9C-E88A-4DDE-A5A1-60F82A20AEF7"), throwOnError: true)!;
        var dialog = (IFileDialog)Activator.CreateInstance(type)!;
        try
        {
            dialog.SetTitle("Choisir le répertoire racine des empreintes — Footprint Linker");
            dialog.SetOkButtonLabel("Choisir ce répertoire");
            dialog.GetOptions(out var options);
            dialog.SetOptions(options | 0x20 | 0x40 | 0x800 | 0x8 | 0x02000000);
            if (Directory.Exists(directory))
            {
                var iid = typeof(IShellItem).GUID;
                SHCreateItemFromParsingName(Path.GetFullPath(directory!), IntPtr.Zero, ref iid, out var initial);
                try { dialog.SetFolder(initial); } finally { Marshal.ReleaseComObject(initial); }
            }
            var hr = dialog.Show(GetForegroundWindow());
            if (hr == unchecked((int)0x800704c7)) return new(null, true);
            Marshal.ThrowExceptionForHR(hr);
            dialog.GetResult(out var item);
            try
            {
                item.GetDisplayName(0x80058000, out var path);
                try
                {
                    var selected = Marshal.PtrToStringUni(path);
                    if (!Directory.Exists(selected)) throw new AppException("directoryMissing");
                    return new(Path.GetFullPath(selected!), false);
                }
                finally { Marshal.FreeCoTaskMem(path); }
            }
            finally { Marshal.ReleaseComObject(item); }
        }
        finally { Marshal.ReleaseComObject(dialog); }
    }
}
