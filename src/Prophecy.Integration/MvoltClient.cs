#nullable enable
using System.Diagnostics;
using System.Text.Json.Nodes;
using VictusPowerUnlockGui;

namespace Prophecy.Integration;

internal sealed record MvoltProfile(string Id, string Name);
internal sealed record MvoltControl(string Name, string Enabled, string Values);
internal sealed class MvoltClient
{
    private JsonObject? _store;
    private JsonNode? _profile;
    private string? _source;
    private readonly Func<string[],Task<string>>? _execute;
    private readonly Func<IDisposable> _prepare;
    public string? Engine { get; set; }
    public MvoltClient(Func<string[],Task<string>>? execute = null, Func<IDisposable>? prepare = null) {
        _execute=execute; _prepare=prepare??HpPlatform.BeforeCurrentPower;
        string bundled=Path.Combine(AppContext.BaseDirectory,"mvolt","mVolt+.exe");
        if(File.Exists(bundled)) Engine=bundled;
    }
    public MvoltProfile[] Import(string path) {
        var store=JsonNode.Parse(File.ReadAllText(path))?.AsObject()??throw new InvalidOperationException("Invalid profile file.");
        if(store["schema"]?.GetValue<string>()!="mvolt.profile.v3" || store["profiles"] is not JsonArray profiles || profiles.Count==0)
            throw new InvalidOperationException("Select a mVolt v3 profile store containing a saved profile.");
        var choices=profiles.Select(p=>new MvoltProfile(p!["id"]!.GetValue<string>(),p["name"]!.GetValue<string>())).ToArray();
        _store=store; _source=path; _profile=null; return choices;
    }
    public MvoltControl[] Select(string id) {
        _profile=(_store?["profiles"]?.AsArray().Single(p=>p!["id"]!.GetValue<string>()==id)
            ??throw new InvalidOperationException("Import a profile file first.")).DeepClone();
        return _profile["controls"]!.AsObject().Select(pair=> {
            var values=pair.Value!.DeepClone().AsObject(); bool enabled=values["enabled"]?.GetValue<bool>()??false; values.Remove("enabled");
            string text=string.Join("; ",values.Select(v=> {
                if(v.Value is JsonValue n && n.TryGetValue<decimal>(out var value)) {
                    if(v.Key.EndsWith("_uv")) return v.Key[..^3].Replace('_',' ')+": "+(value/1000m).ToString("0.###")+" mV";
                    if(v.Key.EndsWith("_mv")) return v.Key[..^3].Replace('_',' ')+": "+value+" mV";
                    if(v.Key.EndsWith("_mhz")) return v.Key[..^4].Replace('_',' ')+": "+value+" MHz";
                    if(v.Key=="percent") return value+"%";
                }
                return v.Key.Replace('_',' ')+": "+v.Value?.ToJsonString();
            }));
            return new MvoltControl(pair.Key.Replace('_',' '),enabled?"Apply":"Preserve",text);
        }).ToArray();
    }
    public void ValidateSaved() {
        if(_profile==null || _store==null || _source==null) throw new InvalidOperationException("Select an imported profile first.");
        var live=JsonNode.Parse(File.ReadAllText(_source))!;
        var saved=live["profiles"]!.AsArray().Single(p=>p!["id"]!.GetValue<string>()==_profile["id"]!.GetValue<string>());
        if(!JsonNode.DeepEquals(saved,_profile) || !JsonNode.DeepEquals(live["adapter_id"],_store["adapter_id"]) || !JsonNode.DeepEquals(live["vbios_id"],_store["vbios_id"]))
            throw new InvalidOperationException("The saved profile or adapter identity changed. Import it again and review it.");
    }
    public async Task<string> ApplyAsync() {
        ValidateSaved(); string adapter=_store!["adapter_id"]!.GetValue<string>();
        var status=JsonNode.Parse(await Execute("--status","--gpu-id",adapter))!;
        if(status["adapter_id"]?.GetValue<string>()!=adapter || !string.Equals(status["vbios"]?.GetValue<string>(),_store["vbios_id"]!.GetValue<string>(),StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Live GPU/VBIOS does not match the imported profile.");
        var controls=_profile!["controls"]!;
        bool power=_profile["full_snapshot"]?.GetValue<bool>()==true || controls["power"]?["enabled"]?.GetValue<bool>()==true || controls["power_cap"]?["enabled"]?.GetValue<bool>()==true;
        string result=await Task.Run(()=> {
            using var guard=power?_prepare():null;
            return Execute("--gpu-id",adapter,"--profile",_profile["id"]!.GetValue<string>()).GetAwaiter().GetResult();
        });
        return result+Environment.NewLine+await ReadAsync();
    }
    public Task<string> ReadAsync() => _store==null ? Execute("--status") : Execute("--status","--gpu-id",_store["adapter_id"]!.GetValue<string>());
    public void Open() { RequireEngine(); Process.Start(new ProcessStartInfo(Engine!){UseShellExecute=true}); }
    private void RequireEngine() {
        if(string.IsNullOrWhiteSpace(Engine) || !File.Exists(Engine)) throw new InvalidOperationException("Select official mVolt+.exe first, or run the optional setup-mvolt.ps1.");
    }
    private async Task<string> Execute(params string[] args) {
        if(_execute!=null) return await _execute(args);
        RequireEngine(); var start=new ProcessStartInfo(Engine!){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true};
        foreach(var arg in args) start.ArgumentList.Add(arg);
        using var process=Process.Start(start)??throw new InvalidOperationException("Could not start mVolt.");
        var output=process.StandardOutput.ReadToEndAsync(); var error=process.StandardError.ReadToEndAsync();
        using var timeout=new CancellationTokenSource(TimeSpan.FromSeconds(60));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch(OperationCanceledException){ process.Kill(true); throw new InvalidOperationException("mVolt timed out. Read tuning before retrying; settings may be partially applied."); }
        string text=await output+await error;
        if(process.ExitCode!=0) throw new InvalidOperationException("mVolt failed; settings may be partially applied. "+text);
        return text;
    }
}
