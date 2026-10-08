#nullable enable
using OmenCore.Services;
using VictusPowerUnlockGui;

namespace Prophecy.Integration;

public sealed class ProphecyControls : UserControl
{
    private readonly SuiteForm _suite;
    public bool CanClose => _suite.CanClose;
    public ProphecyControls(FanService? fans, string? previewDirectory = null)
    {
        HpPlatform.HostFans = fans;
        _suite = new SuiteForm(previewDirectory) { TopLevel = false, FormBorderStyle = FormBorderStyle.None, Dock = DockStyle.Fill };
        Dock = DockStyle.Fill; Controls.Add(_suite); _suite.Show();
    }
    public void StopHoldingCpu() => _suite.StopHoldingCpu();
    protected override void Dispose(bool disposing) {
        if (disposing) { _suite.StopHoldingCpu(); _suite.Close(); HpPlatform.HostFans = null; }
        base.Dispose(disposing);
    }
    public static void RunSelfTests() {
        CurrentGpuPower.TestRouting(); HpPlatform.TestCurrentGuard(); GpuDetectionTests.Run();
        new CpuSettings(45,71,54,54,54,95).Validate();
        foreach (var field in Enumerable.Range(0,6)) foreach (int bad in field == 5 ? new[]{74,101} : new[]{44,72}) {
            int[] v = {54,71,54,54,54,95}; v[field] = bad;
            try { new CpuSettings(v[0],v[1],v[2],v[3],v[4],v[5]).Validate(); throw new Exception("Invalid CPU value accepted."); }
            catch (ArgumentOutOfRangeException) { }
        }
    }
    public static void RunBackground(string[] args) => NvpwrControlBlackwell.Program.Main(args);
    public static void CapturePreview(string directory) {
        System.Windows.Forms.Application.EnableVisualStyles();
        System.Windows.Forms.Application.Run(new SuiteForm(directory));
    }
}
