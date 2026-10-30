using System.Globalization;
using System.Xml.Linq;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Altium.Rendering;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Eda.Rendering;
using OriginalCircuit.Eda.Rendering.Svg;

namespace FootprintLinker;

public sealed record PreviewLayer(int Id, string Name, string Color);

// View-only projections: never change the library used by the importer or the 3D renderer.
public static class LayerPreview
{
    public const int ModelPositionLayer = 200;
    private static readonly XNamespace Svg = "http://www.w3.org/2000/svg";
    private sealed class Drawing
    {
        public PcbComponent Part { get; } = new();
        public List<Action<SvgRenderContext, CoordTransform>> Holes { get; } = [];
    }

    public static (string Svg, PreviewLayer[] Layers, bool RuleBasedMasks) Render(PcbComponent source, CancellationToken ct)
    {
        var drawings = new Dictionary<int, Drawing>();
        Drawing At(int id) { if (!drawings.TryGetValue(id, out var d)) drawings[id] = d = new(); return d; }
        foreach (var p in source.Tracks.OfType<PcbTrack>()) At(p.Layer).Part.AddTrack(p);
        foreach (var p in source.Arcs.OfType<PcbArc>()) At(p.Layer).Part.AddArc(p);
        foreach (var p in source.Fills.OfType<PcbFill>()) At(p.Layer).Part.AddFill(p);
        foreach (var p in source.Regions.OfType<PcbRegion>()) At(RegionLayer(p)).Part.AddRegion(p);
        foreach (var p in source.Texts.OfType<PcbText>()) At(p.Layer).Part.AddText(p);
        foreach (var p in source.ComponentBodies.OfType<PcbComponentBody>()) At(ModelPositionLayer).Part.AddComponentBody(p);
        var ruleBased = false;
        void Pad(PcbPad p, int layer, Coord expansion, bool bottom, bool label = false)
        {
            var size = bottom ? p.SizeBottom : p.SizeTop;
            size = new(size.X + expansion * 2, size.Y + expansion * 2);
            // A negative mask expansion may close the aperture completely.
            if (size.X <= Coord.Zero || size.Y <= Coord.Zero) return;
            var shape = bottom ? p.ShapeBottom : p.ShapeTop;
            // The shared pad renderer draws both pad slots and mask outlines. Identical slots,
            // zero expansion and a per-group colour keep this projection on just one layer.
            At(layer).Part.AddPad(new PcbPad { Layer = layer, Location = p.Location, Rotation = p.Rotation,
                SizeTop = size, SizeMiddle = size, SizeBottom = size, ShapeTop = shape, ShapeMiddle = shape, ShapeBottom = shape,
                CornerRadiusPercentage = p.CornerRadiusPercentage, Designator = label ? p.Designator : null });
        }
        foreach (var p in source.Pads.OfType<PcbPad>())
        {
            Pad(p, p.Layer, Coord.Zero, p.Layer == 32, true);
            if (p.HoleSize > Coord.Zero) At(81).Holes.Add((ctx, transform) => Hole(ctx, transform, p));
            foreach (var bottom in new[] { false, true })
            {
                var side = bottom ? 32 : 1;
                if (p.Layer != side && p.Layer != 74) continue;
                if (!p.IsTenting && !(bottom ? p.IsTentingBottom : p.IsTentingTop) && p.SolderMaskExpansionMode != 0)
                {
                    ruleBased |= p.SolderMaskExpansionMode == 1;
                    Pad(p, bottom ? 38 : 37, p.SolderMaskExpansionMode == 2 ? p.SolderMaskExpansion : Coord.Zero, bottom);
                }
                var paste = (p.Layer == side && p.HoleSize <= Coord.Zero) || (bottom ? p.IsBottomPasteEnabled : p.IsTopPasteEnabled);
                if (paste && p.PasteMaskExpansionMode != 0)
                {
                    ruleBased |= p.PasteMaskExpansionMode == 1;
                    Pad(p, bottom ? 36 : 35, p.PasteMaskExpansionMode == 2 ? p.PasteMaskExpansion : Coord.Zero, bottom);
                }
            }
        }
        foreach (var v in source.Vias.OfType<PcbVia>())
        {
            var p = new PcbPad { Location = v.Location, SizeTop = new(v.Diameter, v.Diameter), SizeBottom = new(v.Diameter, v.Diameter), HoleSize = v.HoleSize };
            Pad(p, v.Layer, Coord.Zero, false);
            if (v.HoleSize > Coord.Zero) At(82).Holes.Add((ctx, transform) => Hole(ctx, transform, p));
            foreach (var bottom in new[] { false, true })
                if ((bottom ? v.EndLayer == 32 : v.StartLayer == 1) && !v.IsTented && !(bottom ? v.IsTentingBottom : v.IsTentingTop) && v.SolderMaskExpansionMode != 0)
                {
                    ruleBased |= v.SolderMaskExpansionMode == 1;
                    Pad(p, bottom ? 38 : 37, v.SolderMaskExpansionMode == 2 ? v.SolderMaskExpansion : Coord.Zero, bottom);
                }
        }
        var bounds = CoordRect.Union(source.Children.Select(p => p.Bounds)
            .Concat(drawings.Values.SelectMany(d => d.Part.Children).Select(p => p.Bounds)));
        var transform = new CoordTransform { ScreenWidth = 1100, ScreenHeight = 760 };
        transform.AutoZoom(bounds, .9);
        var root = new XElement(Svg + "svg", new XAttribute("width", 1100), new XAttribute("height", 760), new XAttribute("viewBox", "0 0 1100 760"),
            new XElement(Svg + "rect", new XAttribute("width", "100%"), new XAttribute("height", "100%"), new XAttribute("fill", "#f9faf8")));
        var layers = drawings.Keys.OrderBy(id => id).Select(id => new PreviewLayer(id, Name(id), Color(id))).ToArray();
        foreach (var layer in layers.OrderBy(l => Priority(l.Id)).ThenBy(l => l.Id))
        {
            ct.ThrowIfCancellationRequested();
            var ctx = new SvgRenderContext(1100, 760);
            new PcbComponentRenderer(transform) { ViewSide = PcbViewSide.Both }.Render(drawings[layer.Id].Part, ctx);
            foreach (var hole in drawings[layer.Id].Holes) hole(ctx, transform);
            var svg = XElement.Parse(ctx.ToSvgString());
            // Each independent render context starts its clipping IDs at zero.
            foreach (var attr in svg.DescendantsAndSelf().Attributes().ToArray())
            {
                if (attr.Name.LocalName == "id") attr.Value = $"layer-{layer.Id}-{attr.Value}";
                else if (attr.Value.Contains("url(#", StringComparison.Ordinal)) attr.Value = attr.Value.Replace("url(#", $"url(#layer-{layer.Id}-", StringComparison.Ordinal);
                else if (attr.Name.LocalName is "fill" or "stroke" && attr.Value != "none")
                    attr.Value = attr.Parent?.Name.LocalName == "text" ? "#ffffff" : layer.Color;
            }
            var group = new XElement(Svg + "g", new XAttribute("data-layer", layer.Id), new XAttribute("data-layer-name", layer.Name),
                new XAttribute("opacity", layer.Id == ModelPositionLayer ? ".45" : ".88"), new XElement(Svg + "title", layer.Name), svg.Elements());
            root.Add(group);
        }
        var output = root.ToString(SaveOptions.DisableFormatting);
        if (output.Length > 10_000_000) throw new AppException("previewTooLarge");
        return (output, layers, ruleBased);
    }

    private static void Hole(SvgRenderContext ctx, CoordTransform transform, PcbPad pad)
    {
        var (x, y) = transform.WorldToScreen(pad.Location);
        var diameter = transform.ScaleValue(pad.HoleSize);
        ctx.SaveState(); ctx.Translate(x, y); ctx.Rotate(-pad.Rotation - pad.HoleRotation);
        if (pad.HoleType == PadHoleType.Square) ctx.FillRectangle(-diameter / 2, -diameter / 2, diameter, diameter, 0xff000000);
        else if (pad.HoleType == PadHoleType.Slot)
        {
            var length = Math.Max(diameter, transform.ScaleValue(Coord.FromRaw(pad.HoleSlotLength)));
            ctx.FillRoundedRectangle(-length / 2, -diameter / 2, length, diameter, diameter / 2, 0xff000000);
        }
        else ctx.FillEllipse(0, 0, diameter / 2, diameter / 2, 0xff000000);
        ctx.RestoreState();
    }

    private static int Priority(int id) => id switch { ModelPositionLayer => 0, 37 or 38 => 1, 35 or 36 => 2, 81 or 82 => 5, _ => 3 };
    private static string Name(int id) => id switch
    {
        1 => "Top Layer", 32 => "Bottom Layer", 33 => "Top Overlay", 34 => "Bottom Overlay", 35 => "Top Paste", 36 => "Bottom Paste",
        37 => "Top Solder Mask", 38 => "Bottom Solder Mask", 55 => "Drill Guide", 56 => "Keep-Out", 73 => "Drill Drawing", 74 => "Multi-Layer",
        81 => "Perçages des pastilles", 82 => "Perçages des vias", ModelPositionLayer => "Position des corps 3D",
        >= 2 and <= 31 => $"Mid-Layer {id - 1}", >= 39 and <= 54 => $"Internal Plane {id - 38}", >= 57 and <= 72 => $"Mechanical {id - 56}", _ => $"Layer {id}"
    };
    private static string Color(int id) => id switch
    {
        1 => "#e53935", 32 => "#3157d5", 33 => "#b88600", 34 => "#008577", 35 => "#e56b15", 36 => "#9257c5",
        37 => "#ba287c", 38 => "#1b9bc0", 74 => "#647380", 81 => "#203645", 82 => "#506b32", ModelPositionLayer => "#85715d",
        _ => $"#{(0x234561L + (uint)id * 0x9e3779L) % 0xffffff:X6}"
    };
    private static int RegionLayer(PcbRegion region)
    {
        var token = (region.V7LayerName ?? "").Replace(" ", "").Replace("-", "").ToUpperInvariant();
        if (token.StartsWith("MECHANICAL", StringComparison.Ordinal) && int.TryParse(token[10..], NumberStyles.None, CultureInfo.InvariantCulture, out var n) && n is >= 1 and <= 16) return n + 56;
        return token switch { "TOP" or "TOPLAYER" => 1, "BOTTOM" or "BOTTOMLAYER" => 32, "TOPOVERLAY" => 33, "BOTTOMOVERLAY" => 34,
            "TOPPASTE" => 35, "BOTTOMPASTE" => 36, "TOPSOLDER" => 37, "BOTTOMSOLDER" => 38, "KEEPOUT" or "KEEPOUTLAYER" => 56, _ => region.Layer };
    }
}
