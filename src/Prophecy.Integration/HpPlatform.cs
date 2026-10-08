#nullable enable
using OmenCore.Hardware;
using OmenCore.Models;
using OmenCore.Services;

namespace VictusPowerUnlockGui;

internal static class HpPlatform
{
    internal static readonly object Sync = new();
    internal static FanService? HostFans { get; set; }
    private static readonly Lazy<bool> VictusHost = new(() => {
        try {
            using var q = new System.Management.ManagementObjectSearcher("SELECT Manufacturer,Model FROM Win32_ComputerSystem");
            foreach (System.Management.ManagementObject o in q.Get())
                if ((Convert.ToString(o["Manufacturer"]) ?? "").Contains("HP", StringComparison.OrdinalIgnoreCase) &&
                    (Convert.ToString(o["Model"]) ?? "").Contains("Victus", StringComparison.OrdinalIgnoreCase)) return true;
        } catch (Exception ex) { NvpwrControlBlackwell.AppLog.Write("Host identification failed: " + ex.Message); }
        return false;
    });
    public static bool IsVictus => VictusHost.Value;
    public static bool? ReadFanMax() { lock (Sync) { using var hp = Open(); return hp.GetFanMax(); } }
    public static bool SetFanMax(bool enabled)
    {
        lock (Sync) {
            var fans = HostFans ?? throw new InvalidOperationException("OmenCore fan controller is unavailable.");
            if (FanService.IsAnyDiagnosticModeActive) throw new InvalidOperationException("Finish OmenCore fan diagnostics first.");
            var preset = new FanPreset { Name = enabled ? "Max" : "Auto", Mode = enabled ? FanMode.Max : FanMode.Auto, IsBuiltIn = true };
            if (!fans.ApplyPreset(preset, immediate: true)) throw new InvalidOperationException("OmenCore refused the fan preset.");
            using var hp = Open();
            if (hp.GetFanMax() != enabled) throw new InvalidOperationException("Fan Max readback did not match. Read fans before retrying.");
            return enabled;
        }
    }
    public static int SendCurrent(Func<int> write) => GuardCurrent(BeforeCurrentPower, write);
    private static int GuardCurrent(Func<IDisposable> prepare, Func<int> write) { using var lease = prepare(); return write(); }
    public static IDisposable BeforeCurrentPower()
    {
        if (!IsVictus) return new NoLease();
        Monitor.Enter(Sync);
        bool diagnosticEntered = false;
        var fans = HostFans;
        try {
            if (FanService.IsAnyDiagnosticModeActive) throw new InvalidOperationException("Finish OmenCore fan diagnostics before changing GPU CURRENT.");
            bool preserveMax = fans?.ActivePreset?.Mode == FanMode.Max;
            if (fans != null) {
                if (!preserveMax && !fans.ApplyPreset(new FanPreset { Name = "Performance", Mode = FanMode.Performance, IsBuiltIn = true }, immediate: true))
                    throw new InvalidOperationException("OmenCore could not prepare performance cooling. CURRENT was not sent.");
                fans.EnterDiagnosticMode(); diagnosticEntered = true;
            }
            using var hp = Open();
            if (!hp.SetFanMode(HpWmiBios.FanMode.LegacyPerformance))
                throw new InvalidOperationException("HPCM performance mode was refused. CURRENT was not sent.");
            if (preserveMax && (!hp.SetFanMax(true) || hp.GetFanMax() != true))
                throw new InvalidOperationException("Fan Max could not be preserved. CURRENT was not sent.");
            NvpwrControlBlackwell.AppLog.Write("OmenCore HPCM accepted before CURRENT; fan engine paused for the transaction.");
            return new HeldLease(fans, diagnosticEntered);
        } catch {
            if (diagnosticEntered) fans?.ExitDiagnosticMode();
            Monitor.Exit(Sync); throw;
        }
    }
    private sealed class NoLease : IDisposable { public void Dispose() {} }
    private sealed class HeldLease(FanService? fans, bool diagnosticEntered) : IDisposable
    {
        public void Dispose() {
            try { if (diagnosticEntered) fans?.ExitDiagnosticMode(); }
            finally { Monitor.Exit(Sync); }
        }
    }
    private static HpWmiBios Open() {
        var hp = new HpWmiBios();
        if (!hp.IsAvailable) { string status = hp.Status; hp.Dispose(); throw new InvalidOperationException("HP WMI unavailable: " + status); }
        return hp;
    }
    public static void TestCurrentGuard() {
        bool wrote = false;
        try { GuardCurrent(() => throw new InvalidOperationException("HP refused"), () => { wrote = true; return 0; }); }
        catch (InvalidOperationException) { }
        if (wrote) throw new Exception("CURRENT ran after failed HPCM preparation.");
        var calls = new List<string>();
        GuardCurrent(() => { calls.Add("HPCM"); return new TestLease(calls); }, () => { calls.Add("CURRENT"); return 0; });
        if (string.Join(",", calls) != "HPCM,CURRENT,release") throw new Exception("HPCM ordering failed.");
    }
    private sealed class TestLease(List<string> calls) : IDisposable { public void Dispose() => calls.Add("release"); }
}
