using FootprintLinker;
using FootprintLinker.Tests;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var passed=0;var failed=0;
void Check(bool value,string why="Assertion failed"){if(!value)throw new Exception(why);}
async Task Test(string name,Func<Task> action){try{await action();Console.WriteLine("PASS "+name);passed++;}catch(Exception ex){Console.WriteLine("FAIL "+name+": "+ex);failed++;}}
Task Sync(Action action){action();return Task.CompletedTask;}
const string csv="Item;Description;Supplier;Supplier Name;Supplier Item No;footprint\r\n00042;Test;S;Supplier;R;SOT-23\r\n";
var root=Path.Combine(Path.GetTempPath(),"FootprintLinker-tests-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
try
{
await Test("CSV delimiters, leading zeros, reordered headers and BOM",()=>Sync(()=>{
 foreach(var separator in new[]{';',',','|','\t'})Check(CsvImport.Parse("\uFEFF"+csv.Replace(';',separator))[0].Item=="00042");
 Check(CsvImport.Parse("footprint,Item,Description,Supplier,Supplier Name,Supplier Item No\nSOT-23,00042,x,s,n,r")[0].Footprint=="SOT-23");
}));
await Test("Quoted multiline CSV and escaped quotes",()=>Sync(()=>Check(CsvImport.Parse(csv.Replace(";Test;",";\"Line 1\nLine \"\"2\"\"\";"))[0].Description=="Line 1\nLine \"2\"")));
await Test("Malformed CSV fails closed",()=>Sync(()=>{
 foreach(var value in new[]{"Item;footprint\nx;y",csv.Replace(";Test;",";\"unfinished;"),csv.Replace(";Test;",";Too;Many;")}){
 bool rejected=false;try{CsvImport.Parse(value);}catch(AppException){rejected=true;}Check(rejected);}
}));
await Test("AU_ID inheritance, exact values, conflicting values",()=>Sync(()=>{
 Check(Planner.AuId([new("AU_ID","00042")],[new("AU_ID","42")])=="00042");
 Check(Planner.AuId([],[new("au_id"," 00042 ")])=="00042");Check(Planner.AuId([new("AU_ID","" )],[new("AU_ID","00042")]) is null);
 Check(Planner.AuId([new("AU_ID","00042"),new("AU_ID","42")],[]) is null);
}));
await Test("URL restrictions block credential exfiltration",()=>Sync(()=>{
 Check(AltiumClient.Endpoint("example.365.altium.com").AbsoluteUri=="https://example.365.altium.com/api/graphql");
 foreach(var url in new[]{"https://evil.example","https://example.altium.com.evil.example","http://example.altium.com","https://x@foo.altium.com","https://foo.altium.com/?token=x","https://foo.altium.com:8443"}){
 bool rejected=false;try{AltiumClient.Endpoint(url);}catch(AppException){rejected=true;}Check(rejected,url);}
}));
var library=Path.Combine(root,"source.PcbLib");PcbFixture.Write(library,"SOT-23","SOIC-8");
await Test("Native PcbLib multi-footprint names and extraction",()=>Sync(()=>{
 Check(PcbLibrary.Names(library).SequenceEqual(new[]{"SOT-23","SOIC-8"}));
 var extracted=PcbLibrary.Extract(library,"SOT-23");var path=Path.Combine(root,"extracted.PcbLib");File.WriteAllBytes(path,extracted);
 Check(PcbLibrary.Names(path).SequenceEqual(new[]{"SOT-23"}));
 var text=Encoding.ASCII.GetString(extracted);Check(text.Contains("OPAQUE_GEOMETRY_SOT-23"));Check(!text.Contains("OPAQUE_GEOMETRY_SOIC-8"));
 Check(PcbLibrary.Names(library).Length==2,"Source file was modified");File.Delete(path);
}));
await Test("Duplicate CSV pairs and ambiguous files are blocked",()=>Sync(()=>{
 var component=new Component("c1","C1","1","",null,"",[new("AU_ID","00042")],[],[]);
 var file=new LibraryFile(library,"source.PcbLib","hash",["SOT-23"]);
 var rows=Planner.Build(CsvImport.Parse(csv+"00042;Test;S;Supplier;R;SOT-23\n"),[component],new([file],[]));Check(rows[0].Status=="ready"&&rows[1].Reason=="duplicateCsvRow");
 Check(Planner.Build(CsvImport.Parse(csv),[component],new([file,file with{Path="other"}],[]))[0].Reason=="ambiguousFile");
}));
await Test("Source managed GUID bindings are neutralized without moving binary offsets",()=>Sync(()=>{
 var bytes=Encoding.Latin1.GetBytes("opaque\0|ITEMGUID=12345678-1234-1234-1234-123456789012|REVISIONGUID={87654321-1234-1234-1234-123456789012}|END=unchanged\0");
 var clean=PcbLibrary.RemoveManagedBindings(bytes);Check(clean.Length==bytes.Length);var text=Encoding.Latin1.GetString(clean);
 Check(!text.Contains("12345678-1234")&&!text.Contains("87654321-1234"));Check(text.IndexOf("|END",StringComparison.Ordinal)==Encoding.Latin1.GetString(bytes).IndexOf("|END",StringComparison.Ordinal));
}));
await Test("DPAPI state is encrypted, atomic, locked and restorable",()=>Sync(()=>{
 var dir=Path.Combine(root,"dpapi");var state=new StoredState(1,new(new("workspace","Test","https://fixture.altium.com/api/graphql",0,[]),"fixture-token"),null,null,[]);
 using(var store=new LocalStore(dir)){store.Save(state);store.Save(state);Check(store.Load()!.Connection!.Token=="fixture-token");Check(!Encoding.UTF8.GetString(File.ReadAllBytes(store.FilePath)).Contains("fixture-token"));
 bool locked=false;try{using var second=new LocalStore(dir);}catch(AppException ex){locked=ex.Code=="storageInUse";}Check(locked);}
 using(var store=new LocalStore(dir)){Check(store.Load()!.Connection!.Info.Name=="Test");store.Clear();Check(!File.Exists(store.FilePath)&&!File.Exists(store.FilePath+".bak"));}
}));

async Task<(HttpClient Client,Microsoft.AspNetCore.Builder.WebApplication App,FakeAltium Fake)> Host(string name,FakeAltium? fake=null)
{
 fake??=new();var handler=fake;
 var app=AppHost.Build(["--port","0","--data-dir",Path.Combine(root,name)],s=>s.AddSingleton(new HttpClient(handler){Timeout=TimeSpan.FromSeconds(10)}));
 await app.StartAsync();var address=app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.Single();
 var client=new HttpClient{BaseAddress=new(address)};client.DefaultRequestHeaders.Add("X-Linker-Session",AppHost.SessionKey(app));return(client,app,fake);
}
async Task<JsonElement> Post(HttpClient client,string path,object body)
{using var response=await client.PostAsJsonAsync(path,body);var text=await response.Content.ReadAsStringAsync();Check(response.IsSuccessStatusCode,path+" "+text);return JsonDocument.Parse(text.Length==0?"{}":text).RootElement.Clone();}
async Task<JsonElement> Poll(HttpClient client,string id)
{for(int i=0;i<300;i++){var result=await client.GetFromJsonAsync<JsonElement>("/api/jobs/"+id);if(result.GetProperty("status").GetString()!="running")return result;await Task.Delay(30);}throw new Exception("Job timeout");}
async Task<JsonElement> Analyze(HttpClient client){var job=await Post(client,"/api/analyze",new{csv,directory=root,folderId="fp-folder"});var result=await Poll(client,job.GetProperty("id").GetString()!);Check(result.GetProperty("status").GetString()=="completed",result.ToString());return result.GetProperty("result").Clone();}
async Task<JsonElement> Apply(HttpClient client,JsonElement plan){var job=await Post(client,"/api/apply",new{planId=plan.GetProperty("id").GetString(),rowIds=plan.GetProperty("rows").EnumerateArray().Where(r=>r.GetProperty("status").GetString()=="ready").Select(r=>r.GetProperty("id").GetString()).ToArray()});return await Poll(client,job.GetProperty("id").GetString()!);}
await Test("HTTP session security, one upload shared by two AU_ID matches, append verification",async()=>{
 var (client,app,fake)=await Host("http");await using(app){using(client){
 using var anonymous=new HttpClient{BaseAddress=client.BaseAddress};Check((await anonymous.GetAsync("/api/session")).StatusCode==HttpStatusCode.Forbidden);
 using var origin=new HttpRequestMessage(HttpMethod.Get,"/api/session");origin.Headers.Add("Origin","https://evil.example");Check((await client.SendAsync(origin)).StatusCode==HttpStatusCode.Forbidden);
 await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="Bearer fixture-token"});
 var state=await client.GetStringAsync("/api/session");Check(!state.Contains("fixture-token"));
 var plan=await Analyze(client);Check(plan.GetProperty("rows").GetArrayLength()==2);
 var applied=await Apply(client,plan);Check(applied.GetProperty("result").GetProperty("results").EnumerateArray().All(r=>r.GetProperty("status").GetString()=="imported"),applied.ToString());
 Check(applied.GetProperty("result").GetProperty("plan").GetProperty("rows")[0].GetProperty("csv").GetProperty("item").GetString()=="00042","Import report lost CSV provenance");
 Check(fake.Creates==1&&fake.Uploads==1&&fake.Adds==2,$"creates={fake.Creates} uploads={fake.Uploads} adds={fake.Adds}");
 var repeated=await Analyze(client);Check(repeated.GetProperty("rows").EnumerateArray().All(r=>r.GetProperty("status").GetString()=="alreadyLinked"));
 await app.StopAsync();}}
});
await Test("Uncertain create is not retried, including after application restart",async()=>{
 var fake=new FakeAltium{FailCreation=true};var (client,app,_)=await Host("uncertain",fake);
 await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});var plan=await Analyze(client);var result=await Apply(client,plan);Check(fake.Creates==1&&fake.Adds==0,result.ToString());await app.StopAsync();}}
 var (next,restarted,_2)=await Host("uncertain",fake);
 await using(restarted){using(next){Check(fake.Calls>0);var before=fake.Calls;var state=await next.GetStringAsync("/api/session");Check(fake.Calls==before,"Restore contacted Altium");Check(!state.Contains("fixture-token"));
 await Post(next,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token=""});var plan=await Analyze(next);await Apply(next,plan);Check(fake.Creates==1,"Uncertain creation retried");await restarted.StopAsync();}}
});
await Test("Uncertain append is reconciled by reading the remote association",async()=>{
 var (client,app,fake)=await Host("append",new(){FailAssociation=true});await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});await Apply(client,await Analyze(client));Check(fake.Adds==2);var repeated=await Analyze(client);Check(repeated.GetProperty("rows").EnumerateArray().All(r=>r.GetProperty("status").GetString()=="alreadyLinked"));Check(fake.Creates==1);await app.StopAsync();}}
});
await Test("Partial inventory blocks all remote writes",async()=>{
 var(client,app,fake)=await Host("partial",new(){PartialInventory=true});await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});var plan=await Analyze(client);Check(plan.GetProperty("warnings").GetArrayLength()>0);var response=await client.PostAsJsonAsync("/api/apply",new{planId=plan.GetProperty("id").GetString(),rowIds=plan.GetProperty("rows").EnumerateArray().Select(r=>r.GetProperty("id").GetString()).ToArray()});Check(response.StatusCode==HttpStatusCode.Conflict);Check(fake.Creates==0&&fake.Adds==0);await app.StopAsync();}}
});
await Test("Inherited folder naming scheme is used for footprint creation",async()=>{
 var(client,app,fake)=await Host("naming",new(){UseNamingScheme=true});await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});var result=await Apply(client,await Analyze(client));Check(result.GetProperty("result").GetProperty("results").EnumerateArray().All(r=>r.GetProperty("status").GetString()=="imported"),result.ToString());Check(fake.Creates==1);await app.StopAsync();}}
});
await Test("Missing AU_ID produces explicit unmatched CSV rows",async()=>{
 var(client,app,fake)=await Host("missing",new(){MissingParameter=true});await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});var result=await Analyze(client);Check(result.GetProperty("rows")[0].GetProperty("reason").GetString()=="componentNotFound");Check(fake.Creates==0);await app.StopAsync();}}
});
await Test("File changes between analysis and import block uploads",async()=>{
 var(client,app,fake)=await Host("changed");await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});var result=await Analyze(client);var bytes=File.ReadAllBytes(library);try{File.AppendAllText(library,"changed");var applied=await Apply(client,result);Check(applied.GetProperty("result").GetProperty("results").EnumerateArray().All(r=>r.GetProperty("error").GetString()=="fileChanged"),applied.ToString());Check(fake.Uploads==0);}finally{File.WriteAllBytes(library,bytes);}await app.StopAsync();}}
});
await Test("Manual recovery reuses a verified footprint ID after an uncertain creation",async()=>{
 var(client,app,fake)=await Host("recovery",new(){FailCreation=true});await using(app){using(client){await Post(client,"/api/connect",new{name="Fixture",endpoint="fixture.altium.com",token="fixture-token"});await Apply(client,await Analyze(client));
 var state=await client.GetFromJsonAsync<JsonElement>("/api/session");var key=state.GetProperty("journal")[0].GetProperty("key").GetString();
 await Post(client,"/api/recover",new{key,footprintId="new-footprint"});fake.FailCreation=false;
 var result=await Apply(client,await Analyze(client));Check(result.GetProperty("result").GetProperty("results").EnumerateArray().All(r=>r.GetProperty("status").GetString()=="imported"),result.ToString());Check(fake.Creates==1&&fake.Adds==2);await app.StopAsync();}}
});
if(args.Contains("--preview"))
{
 var(client,app,_)=await Host("preview");await using(app){using(client){
 await Post(client,"/api/connect",new{name="SIMULATION — Fixture workspace",endpoint="fixture.altium.com",token="fixture-token"});await Analyze(client);
 Console.WriteLine("PREVIEW "+client.BaseAddress+"#session="+AppHost.SessionKey(app));
 await app.WaitForShutdownAsync();}}
}
}
finally{Directory.Delete(root,true);}
Console.WriteLine($"{passed} passed, {failed} failed");Environment.ExitCode=failed==0?0:1;
