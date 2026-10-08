#nullable enable
using VictusPowerUnlockGui;
using NvpwrControlBlackwell;
namespace Prophecy.Integration;
public static class ProphecyControls
{
    public static void RunSelfTests() {
        CurrentGpuPower.TestRouting(); HpPlatform.TestCurrentGuard(); GpuDetectionTests.Run();
        new CpuSettings(45,71,54,54,54,95).Validate();
        foreach (var field in Enumerable.Range(0,6)) foreach (int bad in field == 5 ? new[]{74,101} : new[]{44,72}) {
            int[] v = {54,71,54,54,54,95}; v[field] = bad;
            try { new CpuSettings(v[0],v[1],v[2],v[3],v[4],v[5]).Validate(); throw new Exception("Invalid CPU value accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }
    }
    public static bool BackgroundWatcherRequested => SettingsStore.Load().MsiAutoApply;
    public static void RunBackground(string[] args) {
        int i=Array.IndexOf(args,"--apply-current");
        if (i<0 || i+1>=args.Length || !int.TryParse(args[i+1],out int watts) || watts<=0) return;
        Thread.Sleep(15000);
        var backend=new PowerBackend();
        var result=backend.SetCurrent(watts);
        AppLog.Write("Scheduled CURRENT: "+result.Message);
        if (!result.Success) return;
        var settings=SettingsStore.Load();
        settings.CurrentSelection=watts; SettingsStore.Save(settings);
        if (settings.CoreOffsetEnabled || settings.MemoryOffsetEnabled) {
            var c=backend.CheckCompatibility();
            var tune=NvApiTuner.Apply(new TuneRequest { SetCore=settings.CoreOffsetEnabled,CoreMHz=settings.CoreOffsetMHz,
                SetMemory=settings.MemoryOffsetEnabled,MemoryMHz=settings.MemoryOffsetMHz },c.Profile?.AllowMsvdd??false,out _);
            AppLog.Write("Scheduled tuning: "+tune.Message);
        }
    }
}
