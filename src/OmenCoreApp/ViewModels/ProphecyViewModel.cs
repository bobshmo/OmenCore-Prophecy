using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows.Threading;
using System.Windows.Input;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using NvpwrControlBlackwell;
using OmenCore.Services;
using OmenCore.Utils;
using Prophecy.Integration;
using VictusPowerUnlockGui;

namespace OmenCore.ViewModels;

public sealed class ProphecyField : ViewModelBase
{
    public string Id { get; }
    public string Label { get; private set; }
    public string Unit { get; }
    private string _text;
    private bool _enabled,_supported,_edited;
    public double Minimum { get; private set; }
    public double Maximum { get; private set; }
    public bool Integer { get; }
    public bool Supported { get=>_supported; private set=>SetProperty(ref _supported,value); }
    public bool Enabled { get=>_enabled; set=>SetProperty(ref _enabled,value); }
    public string Text { get=>_text; set { _edited=true; if(SetProperty(ref _text,value)) { OnPropertyChanged(nameof(Valid)); OnPropertyChanged(nameof(Hint)); } } }
    public double Value=>double.TryParse(Text,NumberStyles.Float,CultureInfo.InvariantCulture,out var value)?value:double.NaN;
    public bool Valid=>double.IsFinite(Value) && Value>=Minimum && Value<=Maximum && (!Integer || Value==Math.Truncate(Value));
    public string Hint=>!Supported?"Unavailable on this device":$"{Minimum:0.###}–{Maximum:0.###} {Unit}"+(!Valid?" · Enter a value in this range":"");
    public ProphecyField(string id,string label,string unit,double min,double max,double value,bool integer=true) {
        Id=id; Label=label; Unit=unit; Minimum=min; Maximum=max; Integer=integer; _text=value.ToString("0.###",CultureInfo.InvariantCulture);
    }
    internal void Update(bool supported,double min,double max,double current,string? label=null) {
        Supported=supported; Minimum=min; Maximum=max;
        if(label!=null) {Label=label; OnPropertyChanged(nameof(Label));}
        if(!_edited && supported && current>=min && current<=max) { _text=current.ToString("0.###",CultureInfo.InvariantCulture); OnPropertyChanged(nameof(Text)); }
        if(!supported) Enabled=false;
        OnPropertyChanged(nameof(Valid)); OnPropertyChanged(nameof(Hint));
    }
    internal void ResetEdit()=>_edited=false;
    internal double Require() {if(!Valid)throw new InvalidOperationException($"{Label}: enter {Minimum}–{Maximum} {Unit}.");return Value;}
}
public sealed record ProphecyProfileChoice(string Id,string Name);

public sealed class ProphecyViewModel : ViewModelBase, IDisposable
{
    private readonly IProphecyDevice _device;
    private readonly MvoltClient _mvolt=new();
    private readonly AppSettings _settings;
    private readonly bool _persistSettings;
    private readonly SemaphoreSlim _gate=new(1,1);
    private readonly DispatcherTimer _telemetry=new(),_cpuTimer=new(){Interval=TimeSpan.FromSeconds(1)};
    private readonly MsiScenarioWatcher? _watcher;
    private bool _busy,_disposed,_disposeComplete,_reading,_active,_victus,_cpuSupported;
    private bool _currentReady,_maxReady,_holdCpu=true,_telemetryEnabled,_watchMsi,_repeating;
    private string _gpuName="Read device to identify your laptop GPU",_identity="",_compatibility="Driver and VBIOS checks appear here.",_status="No settings have been applied.",_log="",_live="Telemetry not read.",_profileInfo="Select official mVolt and import your own profile store.";
    private bool? _fanMax;
    private int _maxTarget,_currentTarget,_voltageTarget=1000,_pollMs=1500;
    private int? _stock;
    private CpuSettings? _heldCpu;
    private ProphecyProfileChoice? _selectedProfile;
    public ObservableCollection<int> MaxTargets {get;}=new();
    public ObservableCollection<int> CurrentTargets {get;}=new();
    public ObservableCollection<ProphecyField> TuneFields {get;}=new();
    public ObservableCollection<ProphecyField> CpuFields {get;}=new();
    public ObservableCollection<ProphecyField> HpFields {get;}=new();
    public ObservableCollection<ProphecyProfileChoice> Profiles {get;}=new();
    public ObservableCollection<MvoltRow> ProfileRows {get;}=new();
    public sealed record MvoltRow(string Name,string Enabled,string Values);
    public ICommand ActionCommand {get;}
    public bool Busy {get=>_busy;private set {if(SetProperty(ref _busy,value)){OnPropertyChanged(nameof(Idle));OnPropertyChanged(nameof(EditingEnabled));RaiseCommand();}}}
    public bool Idle=>!Busy&&!_disposed;
    public bool EditingEnabled=>(!Busy||_repeating)&&!_disposed;
    public bool IsVictus {get=>_victus;private set=>SetProperty(ref _victus,value);}
    public bool CpuSupported {get=>_cpuSupported;private set=>SetProperty(ref _cpuSupported,value);}
    public string GpuName {get=>_gpuName;private set=>SetProperty(ref _gpuName,value);}
    public string Identity {get=>_identity;private set=>SetProperty(ref _identity,value);}
    public string Compatibility {get=>_compatibility;private set=>SetProperty(ref _compatibility,value);}
    public string Status {get=>_status;private set=>SetProperty(ref _status,value);}
    public string Activity {get=>_log;private set=>SetProperty(ref _log,value);}
    public string Live {get=>_live;private set=>SetProperty(ref _live,value);}
    public string ProfileInfo {get=>_profileInfo;private set=>SetProperty(ref _profileInfo,value);}
    public string SavedMax {get;private set;}="None";
    public string Startup {get;private set;}="Off";
    public string MaxBlockReason {get;private set;}="Read device first to check MAX compatibility.";
    public string CurrentBlockReason {get;private set;}="Read device first to check CURRENT compatibility.";
    public bool? FanMax {get=>_fanMax;private set=>SetProperty(ref _fanMax,value);}
    public int MaxTarget {get=>_maxTarget;set {if(SetProperty(ref _maxTarget,value)&&MaxTargets.Contains(value)){_settings.MaxSelection=value;PersistSettings();}RaiseCommand();}}
    public int CurrentTarget {get=>_currentTarget;set {if(SetProperty(ref _currentTarget,value)&&CurrentTargets.Contains(value)){_settings.CurrentSelection=value;PersistSettings();}RaiseCommand();}}
    public int VoltageTarget {get=>_voltageTarget;set=>SetProperty(ref _voltageTarget,value);}
    public bool ApplyPlGpu {get;set;}=true;
    public bool Hpcm {get;set;}=true;
    public bool HoldCpu {get=>_holdCpu;set {if(SetProperty(ref _holdCpu,value) && !value){_cpuTimer.Stop();_heldCpu=null;}}}
    public bool TelemetryEnabled {get=>_telemetryEnabled;set {if(SetProperty(ref _telemetryEnabled,value)){_settings.TelemetryEnabled=value;PersistSettings();UpdateTimer();}}}
    public int PollMs {get=>_pollMs;set {if(SetProperty(ref _pollMs,Math.Clamp(value,500,10000))){_settings.TelemetryIntervalMs=_pollMs;PersistSettings();UpdateTimer();}}}
    public bool WatchMsi {get=>_watchMsi;set {if(SetProperty(ref _watchMsi,value)){_settings.MsiAutoApply=value;PersistSettings();if(value)_watcher?.Start();else _watcher?.Stop();}}}
    public int MsiDelay {get=>_settings.MsiTimeoutSec;set{_settings.MsiTimeoutSec=Math.Clamp(value,1,10);if(_watcher!=null)_watcher.TimeoutSec=_settings.MsiTimeoutSec;PersistSettings();OnPropertyChanged();}}
    public ProphecyProfileChoice? SelectedProfile {get=>_selectedProfile;set {
        if(SetProperty(ref _selectedProfile,value) && value!=null){ProfileRows.Clear();foreach(var r in _mvolt.Select(value.Id))ProfileRows.Add(new(r.Name,r.Enabled,r.Values));ProfileInfo="Imported: "+value.Name+" · reviewed values apply only on request.";} RaiseCommand();
    }}
    public ProphecyViewModel(FanService fans):this(new ProphecyDevice(),true) {HpPlatform.HostFans=fans;}
    internal ProphecyViewModel(IProphecyDevice device,bool loadSettings=false) {
        _device=device;_persistSettings=loadSettings;_settings=loadSettings?SettingsStore.Load():new AppSettings();
        _maxTarget=_settings.MaxSelection;_currentTarget=_settings.CurrentSelection;
        _telemetryEnabled=_settings.TelemetryEnabled;_pollMs=_settings.TelemetryIntervalMs;_watchMsi=_settings.MsiAutoApply;
        TuneFields.Add(new("core","Core clock offset","MHz",-1000,1000,0));
        TuneFields.Add(new("memory","Memory clock offset","MHz",-1000,3000,0));
        TuneFields.Add(new("nvvdd","Core voltage offset","mV",-100,100,0));
        TuneFields.Add(new("xbar","Crossbar clock offset","MHz",-1000,1000,0));
        TuneFields.Add(new("msvdd","MSVDD voltage offset","mV",-100,100,0));
        TuneFields.Add(new("ratio","Graphics / crossbar ratio","",0,2,0.9,false));
        if(_settings.CoreOffsetEnabled){TuneFields[0].Text=_settings.CoreOffsetMHz.ToString(CultureInfo.InvariantCulture);TuneFields[0].Enabled=true;}
        if(_settings.MemoryOffsetEnabled){TuneFields[1].Text=_settings.MemoryOffsetMHz.ToString(CultureInfo.InvariantCulture);TuneFields[1].Enabled=true;}
        string[] names={"STAPM sustained","Fast PPT boost","Slow PPT","APU Slow","Skin power","Temperature limit"}; int[] defaults={54,71,54,54,54,95};
        for(int i=0;i<6;i++)CpuFields.Add(new(i.ToString(),names[i],i==5?"°C":"W",i==5?75:45,i==5?100:71,defaults[i]));
        HpFields.Add(new("tpp","HP target TPP","W",100,180,180)); HpFields.Add(new("gpu","PCF GPU maximum","W",100,115,115)); HpFields.Add(new("plgpu","HP PLGPU","W",35,71,71));
        ActionCommand=new AsyncRelayCommand(p=>ExecuteAsync(p?.ToString()??"refresh"),p=>CanExecute(p?.ToString()??"refresh"));
        foreach(var field in TuneFields.Concat(CpuFields).Concat(HpFields))field.PropertyChanged+=(_,_)=>RaiseCommand();
        _telemetry.Tick+=async(_,_)=>await PollAsync();
        _cpuTimer.Tick+=async(_,_)=>{if(_heldCpu!=null&&!Busy&&!_reading&&HoldCpu)await RunAsync(()=>Hardware("cpu",cpu:_heldCpu),false,true);};
        if(loadSettings){
            _watcher=new MsiScenarioWatcher(()=>System.Windows.Application.Current?.Dispatcher?.BeginInvoke(async()=>{
                if(WatchMsi&&!Busy&&!_disposed)await ExecuteAsync("reapply");
            }));
            _watcher.TimeoutSec=_settings.MsiTimeoutSec;
            if(_watchMsi)_watcher.Start();
        }
    }
    private void RaiseCommand(){if(ActionCommand is AsyncRelayCommand c)c.RaiseCanExecuteChanged();}
    internal bool CanExecute(string action) {
        if(!Idle)return false;
        return action switch {
            "max"=>_maxReady&&MaxTargets.Contains(MaxTarget),
            "current" or "startup"=>_currentReady&&CurrentTargets.Contains(CurrentTarget),
            "restore-current"=>_currentReady,
            "restore-max"=>_maxReady,
            "restore-tune" or "restore-all" or "restore-voltage"=>MaxTargets.Count>0,
            "cpu"=>CpuSupported&&CpuFields.All(f=>f.Valid),
            "hp"=>IsVictus&&HpFields.All(f=>f.Valid),
            "both"=>CpuSupported&&IsVictus&&CpuFields.Concat(HpFields).All(f=>f.Valid),
            "fan-on" or "fan-off" or "read-hp" or "restore-victus"=>IsVictus,
            "tune"=>TuneFields.Any(f=>f.Enabled&&f.Supported)&&TuneFields.Where(f=>f.Enabled).All(f=>f.Supported&&f.Valid),
            "apply-profile"=>SelectedProfile!=null,
            "voltage"=>MaxTargets.Count>0&&VoltageTarget is >=900 and <=1150,
            _=>true
        };
    }
    public async Task ActivateAsync(){_active=true;UpdateTimer();await ExecuteAsync("refresh");}
    public void Deactivate(){_active=false;_telemetry.Stop();}
    private void UpdateTimer(){_telemetry.Interval=TimeSpan.FromMilliseconds(PollMs);if(_active&&TelemetryEnabled&&!_disposed)_telemetry.Start();else _telemetry.Stop();}
    internal async Task PollAsync() {
        if(_disposed||Busy||_reading||!await _gate.WaitAsync(0))return;
        _reading=true;
        try {var value=await Task.Run(_device.ReadTelemetry);if(!_disposed)UpdateLive(value);}
        catch(Exception ex){if(!_disposed)Live="Telemetry unavailable: "+ex.Message;}
        finally {_reading=false;_gate.Release();FinishDispose();}
    }
    internal async Task ExecuteAsync(string action) {
        if(!CanExecute(action))return;
        if(action=="import-state"){
            var dialog=new OpenFolderDialog{Title="Select the previous working app folder or its state folder"};
            if(dialog.ShowDialog()==true)await RunAsync(()=>Hardware("import-state",path:dialog.FolderName),true);
            return;
        }
        if(action=="refresh"){await RunAsync(async()=>{try{ApplySnapshot(await Task.Run(_device.Read));}catch(Exception){_currentReady=false;_maxReady=false;RaiseCommand();throw;}return "Device checks updated. No settings applied.";});return;}
        if(action is "rom" or "report" or "select-mvolt" or "import-profile") {
            string? path=null;
            if(action=="report"){var dialog=new SaveFileDialog{Filter="Text report|*.txt",FileName="OmenCore-Prophecy-report.txt"};if(dialog.ShowDialog()==true)path=dialog.FileName;}
            else {var dialog=new OpenFileDialog{Filter=action=="rom"?"GPU ROM|*.rom;*.bin":action=="select-mvolt"?"mVolt executable|*.exe":"mVolt profile store|*.json"};if(dialog.ShowDialog()==true)path=dialog.FileName;}
            if(path==null)return;
            if(action=="select-mvolt"){_mvolt.Engine=path;ProfileInfo="Official mVolt selected. Import a saved profile to review it.";return;}
            if(action=="import-profile"){await RunAsync(()=>{var profiles=_mvolt.Import(path);Profiles.Clear();foreach(var p in profiles)Profiles.Add(new(p.Id,p.Name));SelectedProfile=Profiles[0];return Task.FromResult("Profile imported in memory. No settings applied.");});return;}
            await RunAsync(()=>Hardware(action,path:path),action=="rom");return;
        }
        if(action is "reboot" or "restart" or "restore-all") {
            string text=action=="reboot"?"Restart Windows now?":action=="restart"?"Restart the NVIDIA device now?":"Restore NVIDIA tuning, CURRENT, saved MAX and scheduled startup?";
            if(System.Windows.MessageBox.Show(text,"OmenCore + Prophecy",System.Windows.MessageBoxButton.YesNo)!=System.Windows.MessageBoxResult.Yes)return;
        }
        await RunAsync(async()=> {
            switch(action) {
                case "cpu": _heldCpu=null;_cpuTimer.Stop(); var cpu=CpuValues();string message=await Hardware("cpu",cpu:cpu);_heldCpu=cpu;if(HoldCpu)_cpuTimer.Start();return message;
                case "hp":return await Hardware("hp",hp:HpValues());
                case "both":string first=await Hardware("cpu",cpu:CpuValues());string second=await Hardware("hp",hp:HpValues());_heldCpu=CpuValues();if(HoldCpu)_cpuTimer.Start();return first+"\n"+second;
                case "fan-on":return await Hardware("fan",1);
                case "fan-off":return await Hardware("fan",0);
                case "max":return await Hardware("max",MaxTarget);
                case "current":return await Hardware("current",CurrentTarget);
                case "startup":return await Hardware("startup",CurrentTarget);
                case "voltage":return await Hardware("voltage",VoltageTarget);
                case "tune":var request=TuneValues();var result=await Hardware("tune",tuning:request);SaveTuning(request);return result;
                case "restore-tune":string restored=await Hardware("restore-tune");foreach(var field in TuneFields){field.Enabled=false;field.ResetEdit();}_settings.CoreOffsetEnabled=false;_settings.MemoryOffsetEnabled=false;PersistSettings();return restored;
                case "reapply":string current=await Hardware("current",CurrentTarget);var saved=new TuneRequest{SetCore=_settings.CoreOffsetEnabled,CoreMHz=_settings.CoreOffsetMHz,SetMemory=_settings.MemoryOffsetEnabled,MemoryMHz=_settings.MemoryOffsetMHz};if(saved.SetCore||saved.SetMemory)current+="\n"+await Hardware("tune",tuning:saved);return current;
                case "restore-victus":_heldCpu=null;HoldCpu=false;var parts=new List<string>();if(CpuSupported)parts.Add(await Hardware("cpu",cpu:new(45,45,45,45,45,100)));parts.Add(await Hardware("hp",hp:new(125,100,35,false)));return string.Join("\n",parts);
                case "restore-all":var errors=new List<string>();var messages=new List<string>();foreach(string item in new[]{"restore-tune","restore-current","remove-startup","restore-max"}){try{messages.Add(await Hardware(item));}catch(Exception ex){errors.Add(ex.Message);}}if(errors.Count>0)throw new InvalidOperationException("Restore incomplete: "+string.Join("; ",errors));foreach(var f in TuneFields){f.Enabled=false;f.ResetEdit();}_settings.CoreOffsetEnabled=false;_settings.MemoryOffsetEnabled=false;PersistSettings();return string.Join("\n",messages);
                case "apply-profile":return await _mvolt.ApplyAsync();
                case "read-profile":return await _mvolt.ReadAsync();
                case "open-mvolt":_mvolt.Open();return "Opened official mVolt.";
                case "open-log":Process.Start(new ProcessStartInfo(AppLog.DirectoryPath){UseShellExecute=true});return "Opened local diagnostics folder.";
                default:return await Hardware(action);
            }
        },action is not ("read-profile" or "open-mvolt" or "open-log"));
    }
    internal async Task RunAsync(Func<Task<string>> action,bool refresh=false,bool repeat=false) {
        if(Busy||_disposed)return;
        _repeating=repeat;Busy=true;if(!repeat)Status="Working…";await _gate.WaitAsync();
        try {
            if(_disposed)return;
            var message=await action(); if(_disposed)return;
            Status=message;
            if(!repeat)Append(message);
            if(refresh) {
                try {ApplySnapshot(await Task.Run(_device.Read));}
                catch(Exception ex){_currentReady=false;_maxReady=false;Compatibility="Readback unavailable: "+ex.Message;Status=message+"\nReadback refresh failed: "+ex.Message;RaiseCommand();}
            }
        } catch(Exception ex) {
            _heldCpu=null;_cpuTimer.Stop();Status="Operation failed: "+ex.Message;Append(Status);
        } finally {_gate.Release();_repeating=false;Busy=false;FinishDispose();}
    }
    private async Task<string> Hardware(string action,int value=0,string? path=null,TuneRequest? tuning=null,CpuSettings? cpu=null,ProphecyHpLimits? hp=null) {
        var result=await Task.Run(()=>_device.Execute(action,value,path,tuning,cpu,hp));
        if(!result.Success)throw new InvalidOperationException(result.Message);
        return result.Message;
    }
    private CpuSettings CpuValues(){int[] v=CpuFields.Select(f=>(int)f.Require()).ToArray();return new(v[0],v[1],v[2],v[3],v[4],v[5]);}
    private ProphecyHpLimits HpValues()=>new((int)HpFields[0].Require(),(int)HpFields[1].Require(),ApplyPlGpu?(int)HpFields[2].Require():null,Hpcm);
    internal TuneRequest TuneValues(){var r=new TuneRequest();foreach(var f in TuneFields.Where(f=>f.Enabled)){double v=f.Require();if(!f.Supported)throw new InvalidOperationException(f.Label+" is unavailable.");switch(f.Id){case "core":r.SetCore=true;r.CoreMHz=(int)v;break;case "memory":r.SetMemory=true;r.MemoryMHz=(int)v;break;case "nvvdd":r.SetNvvdd=true;r.NvvddMv=(int)v;r.NvvddIsTarget=f.Minimum>=800;break;case "xbar":r.SetXbar=true;r.XbarMHz=(int)v;break;case "msvdd":r.SetMsvdd=true;r.MsvddMv=(int)v;break;case "ratio":r.SetRatio=true;r.GpcXbarRatio=v;break;}}return r;}
    private void SaveTuning(TuneRequest request){_settings.CoreOffsetEnabled=request.SetCore;_settings.CoreOffsetMHz=request.CoreMHz;_settings.MemoryOffsetEnabled=request.SetMemory;_settings.MemoryOffsetMHz=request.MemoryMHz;PersistSettings();}
    internal void ApplySnapshot(ProphecySnapshot snapshot) {
        int previousMax=MaxTarget,previousCurrent=CurrentTarget;
        var c=snapshot.Compatibility;GpuName=string.IsNullOrWhiteSpace(c.GpuName)?"No supported NVIDIA laptop GPU":c.GpuName;
        Identity=$"Driver {c.DriverVersion} · VBIOS {c.Vbios}";Compatibility=c.Reason;_currentReady=c.CurrentWritesReady;_maxReady=c.MaxWritesReady;
        MaxBlockReason=_maxReady?"":c.Profile==null?"This GPU could not be mapped. Check Device compatibility.":c.VbiosResolver?.Resolved!=true?"MAX needs this GPU's resolved VBIOS. Use Import prior validation or Select GPU ROM in Device.":"MAX blocked by policy: "+c.Policy.Reason;
        CurrentBlockReason=_currentReady?"":c.Profile==null?"This GPU could not be mapped. Check Device compatibility.":c.Driver?.Trusted!=true?"CURRENT needs driver validation. Use Import prior validation or Validate driver in Device.":"CURRENT blocked by policy: "+c.Policy.Reason;
        OnPropertyChanged(nameof(MaxBlockReason));OnPropertyChanged(nameof(CurrentBlockReason));
        var maxChoices=c.Profile?.Targets().ToArray()??Array.Empty<int>();
        var currentChoices=maxChoices.ToList();
        _stock=c.Profile==null?null:c.VbiosResolver?.StockMaxW>0?c.VbiosResolver.StockMaxW:c.Profile.StockPowerW;
        if(_stock is int stock&&!currentChoices.Contains(stock))currentChoices.Add(stock);
        ReplaceChoices(MaxTargets,maxChoices);ReplaceChoices(CurrentTargets,currentChoices);
        MaxTarget=MaxTargets.Contains(previousMax)?previousMax:MaxTargets.FirstOrDefault();
        CurrentTarget=CurrentTargets.Contains(previousCurrent)?previousCurrent:CurrentTargets.Contains((int)(snapshot.Power.CurrentW??0))?(int)snapshot.Power.CurrentW!:CurrentTargets.FirstOrDefault();
        FanMax=snapshot.FanMax;IsVictus=snapshot.Victus;CpuSupported=snapshot.CpuSupported;
        foreach(var f in CpuFields)f.Update(CpuSupported,f.Minimum,f.Maximum,f.Value);
        foreach(var f in HpFields)f.Update(IsVictus,f.Minimum,f.Maximum,f.Value);
        var t=snapshot.Tuning;
        TuneFields[0].Update(t.CoreMHz.Supported,t.CoreMHz.Min,t.CoreMHz.Max,t.CoreMHz.Current);
        TuneFields[1].Update(t.MemoryMHz.Supported,t.MemoryMHz.Min,t.MemoryMHz.Max,t.MemoryMHz.Current);
        TuneFields[2].Update(t.NvvddMv.Supported,t.NvvddMv.Min,t.NvvddMv.Max,t.NvvddMv.Current,t.NvvddMv.Min>=800?"Core voltage target":"Core voltage offset");
        TuneFields[3].Update(t.XbarWritable,t.XbarMinMHz,t.XbarMaxMHz,t.XbarMHz);
        TuneFields[4].Update(t.MsvddWritable&&c.Profile?.AllowMsvdd==true,t.MsvddMinMv,t.MsvddMaxMv,t.MsvddMv);
        TuneFields[5].Update(t.RatioWritable,0,2,t.GpcXbarRatio);
        SavedMax=snapshot.SavedMax is int saved?$"{saved} W":"None";Startup=snapshot.Startup is int startup?$"{startup} W at sign-in":"Off";
        OnPropertyChanged(nameof(SavedMax));OnPropertyChanged(nameof(Startup));UpdateLive(snapshot.Telemetry);RaiseCommand();
    }
    private void UpdateLive(TelemetryState state) {
        static string N(double? value,string suffix)=>value.HasValue?$"{value:0.#} {suffix}":"Unavailable";
        Live=$"Power {N(state.PowerW,"W")}   ·   Temperature {N(state.GpuTempC,"°C")}   ·   Hotspot {N(state.HotspotTempC,"°C")}\nCore {N(state.CoreClockMHz,"MHz")}   ·   Memory {N(state.MemoryClockMHz,"MHz")}   ·   Load {N(state.UtilizationPct,"%")}\nCURRENT {N(state.Power.CurrentW,"W")}   ·   MAX {N(state.Power.MaxW,"W")}   ·   Voltage {N(state.VoltageV,"V")}   ·   VRAM {N(state.MemoryTempC,"°C")}";
        if(!string.IsNullOrWhiteSpace(state.Error))Live+="\n"+state.Error;
    }
    private void Append(string text){Activity=$"[{DateTime.Now:HH:mm:ss}] {text}\n"+Activity;if(Activity.Length>16000)Activity=Activity[..16000];}
    private void PersistSettings(){if(_persistSettings)SettingsStore.Save(_settings);}
    private static void ReplaceChoices(ObservableCollection<int> choices,IEnumerable<int> values){
        var next=values.ToArray();if(choices.SequenceEqual(next))return;
        choices.Clear();foreach(int value in next)choices.Add(value);
    }
    public void Dispose(){if(_disposed)return;_disposed=true;_telemetry.Stop();_cpuTimer.Stop();_watcher?.Dispose();_heldCpu=null;FinishDispose();RaiseCommand();}
    private void FinishDispose(){if(_disposed&&!Busy&&!_reading&&!_disposeComplete){_disposeComplete=true;_device.Dispose();HpPlatform.HostFans=null;}}
}
