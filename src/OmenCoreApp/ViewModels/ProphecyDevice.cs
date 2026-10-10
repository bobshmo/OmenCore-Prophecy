using NvpwrControlBlackwell;
using VictusPowerUnlockGui;
using OmenCore.Hardware;
using System.Reflection;
using NvAPIWrapper.GPU;
using OmenCore.Services;

namespace OmenCore.ViewModels;

internal sealed record ProphecySnapshot(CompatibilityState Compatibility, PowerState Power, TelemetryState Telemetry, TunerState Tuning, bool Victus, bool CpuSupported, bool? FanMax, int? SavedMax, int? Startup);
internal sealed record ProphecyHpLimits(int Tpp,int Gpu,int? PlGpu,bool Hpcm);
internal interface IProphecyDevice : IDisposable
{
    ProphecySnapshot Read();
    TelemetryState ReadTelemetry();
    OperationResult Execute(string action,int value=0,string? path=null,TuneRequest? tuning=null,CpuSettings? cpu=null,ProphecyHpLimits? hp=null);
}
internal sealed class ProphecyDevice : IProphecyDevice
{
    private readonly PowerBackend _power=new();
    private SmuPowerController? _cpu;
    public TelemetryState ReadTelemetry()=>SystemProbe.GetTelemetry();
    public ProphecySnapshot Read() {
        var c=_power.CheckCompatibility();
        var telemetry=SystemProbe.GetTelemetry();
        var tuning=c.Profile!=null ? NvApiTuner.Probe(c.Profile.AllowMsvdd):new TunerState();
        RyzenControl.Init(); bool? fan=null;
        if(HpPlatform.IsVictus) { try { fan=HpPlatform.ReadFanMax(); } catch(Exception ex){ AppLog.Write("Fan read: "+ex.Message); } }
        return new(c,_power.GetPowerState(),telemetry,tuning,HpPlatform.IsVictus,
            SmuPowerController.SupportsCpu(RyzenControl.CpuName,RyzenControl.CpuModel,RyzenControl.Family),fan,_power.GetInstalledMaxOverrideTarget(),_power.GetAutostartTarget());
    }
    public OperationResult Execute(string action,int value=0,string? path=null,TuneRequest? tuning=null,CpuSettings? cpu=null,ProphecyHpLimits? hp=null) {
        var profile=action is "tune" or "restore-tune" or "restore-current" ? _power.GetProfile():null;
        if(action is "tune" or "restore-tune" && profile==null) return OperationResult.Fail("Unsupported NVIDIA laptop GPU.");
        int stock=profile?.StockPowerW??0;
        if(action=="restore-current") {var c=_power.CheckCompatibility(); stock=c.VbiosResolver?.StockMaxW>0?c.VbiosResolver.StockMaxW:stock;}
        return action switch {
            "max"=>_power.SetMaxOverride(value), "current"=>_power.SetCurrent(value),
            "restore-current"=>_power.SetCurrent(stock), "restore-max"=>_power.RemoveMaxOverride(),
            "validate"=>_power.ValidateDriverResolver(), "auto-rom"=>_power.TryAutoResolveVbios(out _),
            "rom"=>_power.ResolveVbiosFromRom(path!), "voltage"=>_power.SetVoltage(value), "restore-voltage"=>_power.RestoreVoltage(),
            "import-state"=>ResolverStateStore.Import(path!),
            "startup"=>_power.InstallAutostart(value,Environment.ProcessPath!), "remove-startup"=>_power.RemoveAutostart(),
            "tune"=>NvApiTuner.Apply(tuning!,profile?.AllowMsvdd??false,out _),
            "restore-tune"=>NvApiTuner.ResetFactory(profile?.AllowMsvdd??false,out _),
            "fan"=>OperationResult.Ok(HpPlatform.SetFanMax(value!=0)?"Fan Max verified on.":"Fan Auto verified."),
            "cpu"=>ApplyCpu(cpu!), "hp"=>ApplyHp(hp!), "read-hp"=>ReadHp(),
            "report"=>_power.ExportCompatibilityReport(path!),
            "restart"=>SystemProbe.RestartNvidiaDevice(SystemProbe.GetGpuName()), "reboot"=>SystemProbe.RebootWindowsNow(),
            _=>OperationResult.Fail("Unknown control action.")
        };
    }
    private OperationResult ApplyCpu(CpuSettings settings) { settings.Validate(); _cpu??=new SmuPowerController(); return OperationResult.Ok(_cpu.ApplyCpuPower(settings)); }
    private static OperationResult ApplyHp(ProphecyHpLimits settings) {
        if(!HpPlatform.IsVictus) return OperationResult.Fail("HP platform controls require Victus.");
        if(FanService.IsAnyDiagnosticModeActive) return OperationResult.Fail("Finish OmenCore fan diagnostics before changing HP platform controls.");
        if(settings.Tpp is <100 or >180 || settings.Gpu is <100 or >115 || settings.PlGpu is <35 or >71) return OperationResult.Fail("HP power fields are outside their supported ranges.");
        lock(HpPlatform.Sync) {
            using var guard=settings.Hpcm?HpPlatform.BeforeCurrentPower():null;
            using var hp=new HpWmiBios();
            if(!hp.IsAvailable) return OperationResult.Fail("HP WMI is unavailable: "+hp.Status);
            if(!settings.Hpcm) SendHp(hp,0x1A,[0xFF,0]);
            SendHp(hp,0x22,[1,1,1,0]);
            if(settings.PlGpu.HasValue) SendHp(hp,0x29,[0xFF,0xFF,0xFF,(byte)settings.PlGpu.Value]);
            using var pcf=new PcfPowerController();
            pcf.SetPowerField(PcfPowerFields.ACMaxGPULimit,(uint)settings.Gpu*1000);
            pcf.SetPowerField(PcfPowerFields.ACTargetTPPLimit,(uint)settings.Tpp*1000);
            Thread.Sleep(300); var actual=pcf.GetPowerValues();
            if(actual.ACMaxGPULimitInMilliwatts!=(uint)settings.Gpu*1000 || actual.ACTargetTPPLimitInMilliwatts!=(uint)settings.Tpp*1000)
                return OperationResult.Fail("HP/PCF power readback mismatch.");
            return OperationResult.Ok($"HP/PCF verified: TPP {settings.Tpp} W, GPU {settings.Gpu} W.");
        }
    }
    private static void SendHp(HpWmiBios hp,uint command,byte[] bytes) {
        var method=typeof(HpWmiBios).GetMethod("SendBiosCommand",BindingFlags.Instance|BindingFlags.NonPublic,null,[typeof(HpWmiBios.BiosCmd),typeof(uint),typeof(byte[]),typeof(byte)],null)??throw new MissingMethodException("HP transport unavailable.");
        if(method.Invoke(hp,[HpWmiBios.BiosCmd.Default,command,bytes,(byte)0])==null) throw new InvalidOperationException($"HP command {command:X2} failed.");
    }
    private static OperationResult ReadHp() { using var pcf=new PcfPowerController(); var v=pcf.GetPowerValues(); return OperationResult.Ok($"HP TPP {v.ACTargetTPPLimitInMilliwatts/1000.0:0.0} W; GPU max {v.ACMaxGPULimitInMilliwatts/1000.0:0.0} W"); }
    public void Dispose() { _cpu?.Dispose(); _cpu=null; }
}
