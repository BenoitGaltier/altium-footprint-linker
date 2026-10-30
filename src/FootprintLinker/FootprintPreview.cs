using System.Security.Cryptography;
using System.Text.Json;
using OriginalCircuit.Altium;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering.Gltf;

namespace FootprintLinker;

public sealed record PreviewRequest(string PlanId, string RowId);
public sealed record PreviewExtrusion(double[][] Outline, double Base, double Height, string Color, double Opacity);
public sealed record PreviewData(string Name, string File, string Sha256, string Svg, string? Glb,
    int Pads, int Bodies, int RenderedModels, double WidthMm, double HeightMm,
    double CenterX, double CenterY, PreviewExtrusion[] Extrusions, PreviewLayer[] Layers, string[] Warnings);

public sealed class FootprintPreview
{
    private readonly SemaphoreSlim gate = new(1, 1);
    private readonly Dictionary<string, PreviewData> cache = [];
    public async Task<PreviewData> Render(PlanRow row, CancellationToken ct)
    {
        if (row.File is null || row.InternalName is null) throw new AppException("previewUnavailable");
        await gate.WaitAsync(ct);
        try
        {
            var file = row.File;
            if (!File.Exists(file.Path) || new FileInfo(file.Path).Length > 256_000_000) throw new AppException("previewUnavailable");
            var bytes = await File.ReadAllBytesAsync(file.Path, ct);
            if (Convert.ToHexString(SHA256.HashData(bytes)) != file.Sha256) throw new AppException("fileChanged");
            var key = file.Sha256 + ":" + row.InternalName;
            if (cache.TryGetValue(key, out var saved)) return saved with { File = file.RelativePath };
            return await Task.Run(async () =>
            {
                var library = (OriginalCircuit.Altium.Models.Pcb.PcbLibrary)await AltiumLibrary.OpenPcbLibAsync(new MemoryStream(bytes), ct);
                if (library[row.InternalName] is not PcbComponent component) throw new AppException("previewUnavailable");
                if (component.Children.Count() > 100_000)
                    throw new AppException("previewTooLarge");
                var warnings = library.Diagnostics.Select(d => d.ToString()).Distinct().Take(20).ToList();
                var bounds = component.Bounds;
                if (!component.Children.Any())
                    throw new AppException("previewEmpty");
                var layered = LayerPreview.Render(component, ct);
                if (layered.RuleBasedMasks) warnings.Add("previewMaskRules");

                // A library footprint is placed into an in-memory document at its original coordinates.
                // No PCB substrate, guessed package outline or external file resolver is created.
                var board = new PcbDocument();
                board.AddComponent(component);
                foreach (var p in component.Pads.OfType<PcbPad>()) board.AddPad(p);
                foreach (var p in component.Tracks.OfType<PcbTrack>()) board.AddTrack(p);
                foreach (var p in component.Arcs.OfType<PcbArc>()) board.AddArc(p);
                foreach (var p in component.Vias.OfType<PcbVia>()) board.AddVia(p);
                foreach (var p in component.Fills.OfType<PcbFill>()) board.AddFill(p);
                foreach (var p in component.Regions.OfType<PcbRegion>()) board.AddRegion(p);
                foreach (var p in component.Texts.OfType<PcbText>()) board.AddText(p);
                foreach (var p in component.ComponentBodies.OfType<PcbComponentBody>()) board.AddComponentBody(p);
                board.Models.AddRange(library.Models);
                // Match the glTF builder's origin: centres of pads/vias and endpoints of tracks.
                var points = board.Pads.Select(p => p.Location).Concat(board.Vias.Select(v => v.Location))
                    .Concat(board.Tracks.SelectMany(t => new[] { t.Start, t.End })).ToArray();
                var centerX = points.Length == 0 ? 0 : (points.Min(p => p.X.ToMm()) + points.Max(p => p.X.ToMm())) / 2;
                var centerY = points.Length == 0 ? 0 : (points.Min(p => p.Y.ToMm()) + points.Max(p => p.Y.ToMm())) / 2;
                var settings = new GltfRenderSettings { IncludeSubstrate = false, IncludeSolderMask = false,
                    EmbeddedBoardResolver = _ => null, Format = GltfOutputFormat.Glb };
                var stack = board.GetStackup() ?? PcbStackup.CreateDefault(settings.FallbackBoardThicknessMm, 2);
                var top = stack.ForLayer(37)?.Z1Mm ?? stack.TotalThicknessMm;
                var extrusions = component.ComponentBodies.OfType<PcbComponentBody>()
                    .Where(b => b.Kind == 0 && string.IsNullOrEmpty(b.ModelId) && b.Outline.Count >= 3 && b.OverallHeight > b.StandoffHeight)
                    .Select(b => new PreviewExtrusion(b.Outline.Select(p => new[] { p.X.ToMm(), p.Y.ToMm() }).ToArray(),
                        top + b.StandoffHeight.ToMm(), (b.OverallHeight - b.StandoffHeight).ToMm(), $"#{(b.BodyColor3D & 0xffffff):X6}", Math.Clamp(b.BodyOpacity3D, 0, 1))).ToArray();
                string? glb = null; int rendered = 0;
                try
                {
                    ct.ThrowIfCancellationRequested();
                    using var model = new MemoryStream();
                    await new GltfRenderer().RenderAsync(board, model, settings, ct);
                    var output = model.ToArray();
                    if (output.Length > 40_000_000) warnings.Add("previewTooLarge3d");
                    else
                    {
                        glb = Convert.ToBase64String(output);
                        using var json = JsonDocument.Parse(output.AsMemory(20, BitConverter.ToInt32(output, 12)));
                        if (json.RootElement.TryGetProperty("nodes", out var nodes))
                            rendered = nodes.EnumerateArray().Count(n => n.TryGetProperty("extras", out var extras) &&
                                extras.TryGetProperty("role", out var role) && role.GetString() == "component");
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception) { warnings.Add("preview3dFailed"); }
                var expectedModels = component.ComponentBodies.OfType<PcbComponentBody>().Count(b => !string.IsNullOrEmpty(b.ModelId));
                if (rendered < expectedModels) warnings.Add("previewMissingModels");
                if (rendered + extrusions.Length < component.ComponentBodies.Count) warnings.Add("previewMissingModels");
                if (rendered == 0 && extrusions.Length == 0) warnings.Add("previewNoBody");
                var result = new PreviewData(component.Name, file.RelativePath, file.Sha256, layered.Svg, glb,
                    component.Pads.Count, component.ComponentBodies.Count, rendered,
                    bounds.Max.X.ToMm() - bounds.Min.X.ToMm(), bounds.Max.Y.ToMm() - bounds.Min.Y.ToMm(),
                    centerX, centerY, extrusions, layered.Layers, warnings.Distinct().ToArray());
                if (cache.Count >= 3) cache.Clear();
                cache[key] = result;
                return result;
            }, ct);
        }
        catch (AppException) { throw; }
        catch (OperationCanceledException) { throw; }
        catch (Exception) { throw new AppException("previewReadFailed"); }
        finally { gate.Release(); }
    }
}
