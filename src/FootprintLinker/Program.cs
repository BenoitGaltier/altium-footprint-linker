using System.Diagnostics;
using FootprintLinker;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using System.Security.Cryptography;
using System.Text.Json;

if (args.FirstOrDefault() == "--render-preview")
{
    try
    {
        if (args.Length != 4) throw new AppException("Usage: --render-preview FILE.PcbLib FOOTPRINT OUTPUT_DIRECTORY");
        var path = Path.GetFullPath(args[1]);
        if (!Path.GetExtension(path).Equals(".PcbLib", StringComparison.OrdinalIgnoreCase) || new FileInfo(path).Length > 256_000_000) throw new AppException("previewUnavailable");
        var hash = Convert.ToHexString(SHA256.HashData(await File.ReadAllBytesAsync(path)));
        var file = new LibraryFile(path, Path.GetFileName(path), hash, PcbLibrary.Names(path));
        var row = new PlanRow("local", new(0,"","","","","",args[2]),null,null,null,"blocked",null,file,args[2],[],0);
        var preview = await new FootprintPreview().Render(row, CancellationToken.None);
        var output = Path.GetFullPath(args[3]); Directory.CreateDirectory(output);
        await File.WriteAllTextAsync(Path.Combine(output,"preview.svg"),preview.Svg);
        if (preview.Glb is not null) await File.WriteAllBytesAsync(Path.Combine(output,"preview.glb"),Convert.FromBase64String(preview.Glb));
        await File.WriteAllTextAsync(Path.Combine(output,"preview.json"),JsonSerializer.Serialize(preview with { Svg="", Glb=null },new JsonSerializerOptions(JsonSerializerDefaults.Web){WriteIndented=true}));
        Console.WriteLine("Preview rendered locally. No workspace or Altium Designer required.");
    }
    catch (AppException ex) { Console.Error.WriteLine(ex.Code); Environment.ExitCode=1; }
    catch (Exception) { Console.Error.WriteLine("previewReadFailed"); Environment.ExitCode=1; }
    return;
}

if (args.Contains("--help"))
{
    Console.WriteLine($"Footprint Linker {AppHost.Version}\nUsage: FootprintLinker.exe [--headless] [--port 0-65535] [--data-dir PATH] [--no-persistence]\nOffline preview: FootprintLinker.exe --render-preview FILE.PcbLib FOOTPRINT OUTPUT_DIRECTORY\nDefault: local encrypted state under %LOCALAPPDATA%/FootprintLinker, random loopback port, default browser. Close this window or choose Quit to stop.");
    return;
}
try
{
    await using var app = AppHost.Build(args);
    await app.StartAsync();
    var address = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
    var launchUrl = address + "/#session=" + AppHost.SessionKey(app);
    Console.WriteLine($"\n  FOOTPRINT LINKER  {AppHost.Version}\n  Portable Altium 365 complementary footprint importer\n");
    Console.WriteLine("  Open: " + launchUrl);
    Console.WriteLine("\n  Keep this window open. Close it, press Ctrl+C, or choose Quit in the app to stop.\n  Saved connections and import journal are protected with Windows user encryption.\n");
    if (!args.Contains("--headless"))
    {
        try { Process.Start(new ProcessStartInfo(launchUrl) { UseShellExecute = true }); }
        catch (Exception) { Console.WriteLine("  Browser could not be opened automatically. Copy the URL above into your browser."); }
    }
    await app.WaitForShutdownAsync();
}
catch (AppException ex) { Console.Error.WriteLine("Footprint Linker: " + ex.Code); Environment.ExitCode = 1; }
