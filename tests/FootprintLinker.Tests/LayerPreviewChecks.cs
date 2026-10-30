using System.Globalization;
using System.Xml.Linq;
using FootprintLinker;
using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;

namespace FootprintLinker.Tests;

internal static class LayerPreviewChecks
{
    private static CoordPoint Point(double x, double y) => new(Coord.FromMm(x), Coord.FromMm(y));
    private static PcbPad Pad(int layer) => new() { Layer = layer, Location = Point(100, 100), SizeTop = Point(1, 1), SizeBottom = Point(2, 2), ShapeTop = PadShape.Rectangular,
        ShapeBottom = PadShape.Rectangular, PasteMaskExpansionMode = 2, PasteMaskExpansion = Coord.FromMm(-.1), SolderMaskExpansionMode = 2, SolderMaskExpansion = Coord.FromMm(.2) };
    private static void Check(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static XElement Group(string svg, int id) => XElement.Parse(svg).Descendants().Single(e => (string?)e.Attribute("data-layer") == id.ToString(CultureInfo.InvariantCulture));

    public static void AllLayers()
    {
        var part = new PcbComponent();
        foreach (var id in new[] { 1, 32, 33, 34, 56, 57, 144 })
            part.AddTrack(new PcbTrack { Layer = id, Start = Point(100, 100), End = Point(101, 101), Width = Coord.FromMm(.1) });
        part.AddPad(Pad(1)); part.AddPad(Pad(32)); var through = Pad(74); through.HoleSize = Coord.FromMm(.5); part.AddPad(through);
        part.AddVia(new PcbVia { Location = Point(100, 101), Diameter = Coord.FromMm(1), HoleSize = Coord.FromMm(.4), SolderMaskExpansionMode = 2 });
        var body = new PcbComponentBody { Layer = 57 }; body.SetOutline([Point(105, 105), Point(106, 105), Point(106, 106)]); part.AddComponentBody(body);
        var result = LayerPreview.Render(part, CancellationToken.None);
        foreach (var id in new[] { 1, 32, 33, 34, 35, 36, 37, 38, 56, 57, 74, 81, 82, 144, LayerPreview.ModelPositionLayer })
            Check(result.Layers.Any(l => l.Id == id), $"Layer {id} missing");
        Check(result.Layers.Select(l => l.Color).Distinct().Count() == result.Layers.Length, "Layer colours must differ");
        Check(!result.RuleBasedMasks, "All mask expansions are manual");
        foreach (var layer in result.Layers)
            Check(Group(result.Svg, layer.Id).Descendants().Attributes().Any(a => a.Value == layer.Color), $"Colour missing from layer {layer.Id}");
        Check(Group(result.Svg, 32).Descendants().Any(e => e.Name.LocalName == "line"), "Bottom primitives were culled");
        Check(Group(result.Svg, 57).Descendants().All(e => e.Name.LocalName != "polygon"), "3D body leaked into mechanical artwork");
        Check(Group(result.Svg, 200).Descendants().Any(e => e.Name.LocalName == "polygon"), "Body outline missing");
        var rect = Group(result.Svg, 36).Descendants().First(e => e.Name.LocalName == "rect");
        Check(double.Parse(rect.Attribute("width")!.Value, CultureInfo.InvariantCulture) > 50, "Offset footprint should be framed around its geometry");
        Check(part.Pads[1].Layer == 32 && ((PcbPad)part.Pads[1]).SizeBottom.X == Coord.FromMm(2) && body.Layer == 57, "Source primitives were modified");
    }

    public static void MaskGeometry()
    {
        var part = new PcbComponent(); var pad = Pad(32); part.AddPad(pad);
        var result = LayerPreview.Render(part, CancellationToken.None);
        Check(result.Layers.Select(l => l.Id).SequenceEqual(new[] { 32, 36, 38 }), "Bottom pad must not create top-side masks");
        double Width(int id) => double.Parse(Group(result.Svg, id).Descendants().First(e => e.Name.LocalName == "rect").Attribute("width")!.Value, CultureInfo.InvariantCulture);
        Check(Math.Abs(Width(36) / Width(32) - .9) < .0001, "Paste shrink must use bottom size");
        Check(Math.Abs(Width(38) / Width(32) - 1.2) < .0001, "Solder expansion must use bottom size");
        pad.IsTentingBottom = true; pad.PasteMaskExpansion = Coord.FromMm(-2);
        result = LayerPreview.Render(part, CancellationToken.None);
        Check(result.Layers.Select(l => l.Id).SequenceEqual(new[] { 32 }), "Tented or closed apertures must disappear");
        pad.IsTentingBottom = false; pad.PasteMaskExpansionMode = 0; pad.SolderMaskExpansionMode = 0;
        Check(LayerPreview.Render(part, CancellationToken.None).Layers.Length == 1, "Disabled masks must disappear");
        pad.SolderMaskExpansionMode = 1;
        Check(LayerPreview.Render(part, CancellationToken.None).RuleBasedMasks, "Rule-based nominal fallback must be explicit");
    }
}
