using OriginalCircuit.Altium.Models.Pcb;
using OriginalCircuit.Eda.Primitives;
using OriginalCircuit.Mech.STEP.Build;
using OriginalCircuit.Mech.STEP.Core;

namespace FootprintLinker.Tests;

internal static class VisualFixture
{
    public static async Task Write(string path, string name = "SOT-23", string modelKind = "step", bool layers = false)
    {
        var library = new OriginalCircuit.Altium.Models.Pcb.PcbLibrary();
        var other = new PcbComponent { Name = "WRONG_FIRST", Height = Coord.FromMm(5) };
        other.AddPad(Pad(0,0,"WRONG")); library.Add(other);
        var part = new PcbComponent { Name = name, Height = Coord.FromMm(1.2) };
        part.AddPad(Pad(-1,-1,"1"));part.AddPad(Pad(-1,1,"2"));part.AddPad(Pad(1,0,"3"));
        foreach(var pair in new[]{(-1.5,-1.5,1.5,-1.5),(1.5,-1.5,1.5,1.5),(1.5,1.5,-1.5,1.5),(-1.5,1.5,-1.5,-1.5)})
            part.AddTrack(new PcbTrack{Start=Point(pair.Item1,pair.Item2),End=Point(pair.Item3,pair.Item4),Width=Coord.FromMm(.1),Layer=33});
        if(layers)
        {
            var bottom=Pad(0,-.7,"4");bottom.Layer=32;part.AddPad(bottom);
            foreach(var layer in new[]{34,56,57}) part.AddTrack(new PcbTrack{Start=Point(-1.3,(layer-55)*.12),End=Point(1.3,(layer-55)*.12),Width=Coord.FromMm(.1),Layer=layer});
        }
        if(modelKind!="none")
        {
            var id="12345678-1234-1234-1234-123456789012";
            var body=new PcbComponentBody { Name="Known box",ModelName="synthetic-box.step",ModelId=modelKind=="extruded"?null:id,
                ModelEmbed=modelKind=="step",Model2DLocation=Point(-1,-.7),OverallHeight=Coord.FromMm(1.2),StandoffHeight=Coord.FromMm(modelKind=="extruded"?.2:0),BodyColor3D=0x32463b,Layer=57 };
            body.SetOutline(new[]{Point(-1,-.7),Point(1,-.7),Point(1,.7),Point(-1,.7)});part.AddComponentBody(body);
            if(modelKind=="step")
            {
                var builder=StepBuilder.NewModel("Synthetic 2 x 1.4 x 1.2 mm box");var brep=builder.AddBox(2,1.4,1.2);
                var context=builder.File.AddEntity("REPRESENTATION_CONTEXT",StepValue.Text(""),StepValue.Text(""));
                builder.File.AddEntity("ADVANCED_BREP_SHAPE_REPRESENTATION",StepValue.Text("Synthetic box"),StepValue.List(StepValue.Ref(brep.Id)),StepValue.Ref(context.Id));
                var model=new PcbModel{Id=id,Name="synthetic-box.step",StepData=builder.File.ToText()};model.Checksum=unchecked((int)PcbModel.ComputeChecksum(System.Text.Encoding.UTF8.GetBytes(model.StepData)));library.Models.Add(model);
            }
        }
        library.Add(part); await library.SaveAsync(path);
    }
    private static CoordPoint Point(double x,double y)=>new(Coord.FromMm(x),Coord.FromMm(y));
    private static PcbPad Pad(double x,double y,string name)=>new(){Designator=name,Location=Point(x,y),SizeTop=Point(.8,1),SizeMiddle=Point(.8,1),SizeBottom=Point(.8,1),
        ShapeTop=PadShape.Rectangular,ShapeMiddle=PadShape.Rectangular,ShapeBottom=PadShape.Rectangular,Layer=1};
}
