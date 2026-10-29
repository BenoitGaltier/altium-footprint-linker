using System.Net;
using System.Text;
using System.Text.Json;

namespace FootprintLinker.Tests;

internal sealed class FakeAltium : HttpMessageHandler
{
    public int Creates, Adds, Uploads, Calls;
    public bool FailCreation, FailAssociation, PartialInventory;
    public bool MissingParameter { get; set; }
    public bool UseNamingScheme { get; set; }
    public string ParameterField { get; set; } = "AU_ID";
    public LibraryFolder[] FootprintFolders { get; set; } = [new("fp-folder", "Models/Footprints")];
    public List<string> CreatedFolders { get; } = [];
    public readonly Dictionary<string,List<object>> Links = new() { ["c1"] = [new {id="original",name="DEFAULT",folder=new{folderId="fp-folder",path="Models/Footprints"}}], ["c2"] = [new {id="original",name="DEFAULT",folder=new{folderId="fp-folder",path="Models/Footprints"}}] };
    private readonly Dictionary<string,string> createdFolders = [];
    private static object Page(object[] nodes, int? count=null) => new { nodes, totalCount=count??nodes.Length, pageInfo=new{hasNextPage=false,endCursor=(string?)null} };
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,CancellationToken ct)
    {
        Calls++;
        if(request.Headers.Authorization?.ToString()!="Bearer fixture-token")throw new Exception("Incorrect authentication");
        object value;
        if(request.RequestUri!.AbsolutePath=="/api/files/File/Upload")
        { Uploads++; if(request.Content is not MultipartFormDataContent)throw new Exception("No multipart upload");value="uploaded-file";return Json(value); }
        using var body=JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
        var query=body.RootElement.GetProperty("query").GetString()!;var variables=body.RootElement.GetProperty("variables");
        if(query.Contains("LensCategories"))value=new{desLibrary=new{name="Fixture workspace",components=new{totalCount=2},componentTypes=Page([])}};
        else if(query.Contains("LinkerFolders"))value=new{desLibrary=new{folders=FootprintFolders.Select(f=>(object)new{folderId=f.Id,path=f.Path,folderType="PCB_COMPONENT_LIBRARY",itemNamingSchemeTemplate=(string?)null,parent=new{folderId="parent"}}).Append(new{folderId="parent",path="Models",folderType="GENERIC_FOLDER",itemNamingSchemeTemplate=UseNamingScheme?"FP-{0000}":null,parent=(object?)null}).ToArray()}};
        else if(query.Contains("LensComponents"))value=new{desLibrary=new{components=Page(new[]{"c1","c2"}.Select(id=>(object)new{id,name=id,revisionId="1",description="Test",folder=new{path="Components"},componentType=(object?)null,manufacturerParts=Array.Empty<object>()}).ToArray(),PartialInventory?3:2)}};
        else if(query.Contains("LensComponentDetails"))
        {var id=variables.GetProperty("id").GetString();value=new{desComponentById=new{id,details=new{parameters=MissingParameter?Array.Empty<object>():new object[]{new{name=ParameterField,value="00042"}},itemParameters=Array.Empty<object>()}}};}
        else if(query.Contains("LinkerDetails"))
        {var id=variables.GetProperty("id").GetString()!;value=new{desComponentById=new{id,name=id,revisionId="1",folder=new{path="Components/Passives/Resistors"},details=new{parameters=new[]{new{name=ParameterField,value="00042"}},itemParameters=Array.Empty<object>(),footprints=Links[id].ToArray()}}};}
        else if(query.Contains("LinkerCreate"))
        {
            Creates++;
            var input=variables.GetProperty("input");
            var targetFolder=input.GetProperty("folderId").GetString()!;
            if(!FootprintFolders.Any(f=>f.Id==targetFolder)||input.GetProperty("files")[0].GetProperty("fileId").GetString()!="uploaded-file")throw new Exception("Incorrect creation input");
            if(UseNamingScheme && input.GetProperty("name").ValueKind!=JsonValueKind.Null)throw new Exception("Folder naming scheme was ignored");
            if(FailCreation)return new(HttpStatusCode.BadGateway){Content=new StringContent("uncertain")};
            var footprintId=Creates==1?"new-footprint":"new-footprint-"+Creates;CreatedFolders.Add(targetFolder);createdFolders[footprintId]=targetFolder;
            value=new{desCreateFootprint=new{footprintId,errors=Array.Empty<object>()}};
        }
        else if(query.Contains("LinkerAdd"))
        {
            Adds++;var input=variables.GetProperty("input");if(input.TryGetProperty("addAtIndex",out _))throw new Exception("Default footprint must not be changed");
            var id=input.GetProperty("componentId").GetString()!;var fp=input.GetProperty("footprintId").GetString()!;
            var folderId=createdFolders.GetValueOrDefault(fp,"fp-folder");
            Links[id].Add(new{id=fp,name="SOT-23",folder=new{folderId,path=FootprintFolders.Single(f=>f.Id==folderId).Path}});
            if(FailAssociation)return new(HttpStatusCode.BadGateway){Content=new StringContent("uncertain")};
            value=new{desAddFootprintToComponent=new{errors=Array.Empty<object>()}};
        }
        else if(query.Contains("LinkerVerify"))value=new{desFootprintById=new{id="new-footprint",name="SOT-23",comment="SOT-23",folder=new{folderId="fp-folder"}}};
        else throw new Exception("Unexpected GraphQL operation");
        return Json(new{data=value});
    }
    private static HttpResponseMessage Json(object data)=>new(HttpStatusCode.OK){Content=new StringContent(JsonSerializer.Serialize(data),Encoding.UTF8,"application/json")};
}
