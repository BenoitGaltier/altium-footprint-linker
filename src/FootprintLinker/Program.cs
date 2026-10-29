using System.Diagnostics;
using FootprintLinker;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

if (args.Contains("--help"))
{
    Console.WriteLine($"Footprint Linker {AppHost.Version}\nUsage: FootprintLinker.exe [--headless] [--port 0-65535] [--data-dir PATH] [--no-persistence]\nDefault: local encrypted state under %LOCALAPPDATA%/FootprintLinker, random loopback port, default browser. Close this window or choose Quit to stop.");
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
