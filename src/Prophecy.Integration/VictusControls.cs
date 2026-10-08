#nullable enable
using System.Reflection;
using NvAPIWrapper.GPU;
using OmenCore.Hardware;
using OmenCore.Models;

namespace VictusPowerUnlockGui;

internal sealed class SmuPowerController : IDisposable
{
    private readonly RyzenSmu _smu;
    internal static bool SupportsCpu(string name, string model, RyzenFamily family) =>
        name.Contains("Ryzen AI 7 350", StringComparison.OrdinalIgnoreCase) &&
        model.Contains("Family 26", StringComparison.OrdinalIgnoreCase) &&
        model.Contains("Model 96", StringComparison.OrdinalIgnoreCase) && family == RyzenFamily.Unknown;

    public string CpuName => RyzenControl.CpuName.Trim();

    public SmuPowerController()
    {
        RyzenControl.Init();
        if (!SupportsCpu(RyzenControl.CpuName, RyzenControl.CpuModel, RyzenControl.Family))
        {
            throw new NotSupportedException(
                $"Safety gate refused this CPU: {RyzenControl.CpuName.Trim()} / {RyzenControl.CpuModel}. " +
                "This build is restricted to Ryzen AI 7 350, Family 26 Model 96.");
        }

        // Keep the carried-over mailbox path private to this exact CPU. Never mutate
        // RyzenControl.Family: OmenCore's native capability gates must stay intact.
        _smu = new RyzenSmu {
            Mp1AddrMsg = 0x3B10928, Mp1AddrRsp = 0x3B10978, Mp1AddrArg = 0x3B10998,
            PsmuAddrMsg = 0x3B10A20, PsmuAddrRsp = 0x3B10A80, PsmuAddrArg = 0x3B10A88
        };
        if (!_smu.Initialize()) { var reason = _smu.UnavailableReason; _smu.Dispose();
            throw new InvalidOperationException("PawnIO SMU unavailable: " + reason); }
    }

    public string ApplyCpuPower(CpuSettings settings)
    {
        int watts = settings.Stapm;
        int fastWatts = settings.Fast;
        settings.Validate();
        if (watts is < 45 or > 71)
            throw new ArgumentOutOfRangeException(nameof(watts), "CPU power must be 45–71 W.");
        if (fastWatts is < 45 or > 71)
            throw new ArgumentOutOfRangeException(nameof(fastWatts), "Fast PPT must be 45–71 W.");

        uint milliwatts = checked((uint)watts * 1000u);
        uint fastMilliwatts = checked((uint)fastWatts * 1000u);
        var results = new[] {
            Send(0x14, milliwatts), Send(0x15, fastMilliwatts),
            Send(0x16, checked((uint)settings.Slow * 1000u)),
            Send(0x23, checked((uint)settings.ApuSlow * 1000u)),
            Send(0x4A, checked((uint)settings.Skin * 1000u)),
            Send(0x19, checked((uint)settings.Temperature))
        };
        if (results.Any(result => result != RyzenSmu.SmuStatus.Ok))
            throw new InvalidOperationException("SMU refused a limit: " + string.Join(", ", results));

        return $"CPU commands accepted: {settings}. Mailbox acceptance is not power-limit readback.";
    }

    private RyzenSmu.SmuStatus Send(uint command, uint value) {
        uint[] args = new uint[6]; args[0] = value;
        return _smu.SendMp1(command, ref args);
    }
    public void Dispose() => _smu.Dispose();
}

internal sealed record CpuSettings(int Stapm, int Fast, int Slow, int ApuSlow, int Skin, int Temperature)
{
    public void Validate()
    {
        if (new[] { Stapm, Fast, Slow, ApuSlow, Skin }.Any(w => w is < 45 or > 71) || Temperature is < 75 or > 100)
            throw new ArgumentOutOfRangeException(nameof(CpuSettings), "CPU limits must be 45–71 W and temperature 75–100 C.");
    }
    public override string ToString() => $"STAPM {Stapm}, Fast {Fast}, Slow {Slow}, APU {ApuSlow}, Skin {Skin} W; {Temperature} C";
}

internal sealed class MainForm : Form { public bool OperationBusy => _busy;
    public void StopHoldingCpu() { _timer.Stop(); _held = null; }
    public event Action? OpenNvidiaRequested;
    private readonly CurrentGpuPower _currentGpu = new();
    private readonly NumericUpDown[] _cpu = Enumerable.Range(0, 6).Select(_ => new NumericUpDown()).ToArray();
    private readonly NumericUpDown _tpp = new(), _gpu = new(), _plgpu = new(), _nvpWatts = new();
    private readonly CheckBox _keep = new() { Text = "Keep all CPU settings active (every second)", Checked = true, AutoSize = true };
    private readonly CheckBox _hpPl = new() { Text = "Apply HP PLGPU", Checked = true, AutoSize = true };
    private readonly CheckBox _hpcm = new() { Text = "HPCM performance mode", Checked = true, AutoSize = true };
    private readonly CheckBox _useNvp = new() { Text = "Apply NVIDIA power target with GPU settings", AutoSize = true };
    private readonly TextBox _log = new() { Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical };
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 1000 };
    private SmuPowerController? _smu;
    private CpuSettings? _held;
    private bool _busy;
    private readonly List<Button> _buttons = new();

    private bool _preview;

    public MainForm(string? capturePath = null, bool quietPreview = false)
    {
        _preview = capturePath != null || quietPreview;
        Text = "Prophecy Power Unlocker — CPU + HP + NVIDIA";
        ClientSize = new Size(960, 690);
        MinimumSize = Size;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(20, 23, 30);
        ForeColor = Color.White;
        Font = new Font("Segoe UI", 10);
        LabelAt(this, "Prophecy Power Unlocker", 24, 16, 900, 40, 23);
        LabelAt(this, "Ryzen AI 7 350 · HP platform · NVIDIA power control 2.4.2", 26, 64, 900, 26);
        var cpuPanel = Card("CPU LIMITS", 24, 105, 438, 310);
        string[] labels = { "STAPM sustained (W)", "Fast PPT boost (W)", "Slow PPT (W)", "APU Slow (W)", "Skin power / 0x4A (W)", "Temperature limit (°C)" };
        int[] defaults = { 54, 71, 54, 54, 54, 95 };
        for (int i = 0; i < 6; i++) Field(cpuPanel, labels[i], _cpu[i], 50 + i * 36, i == 5 ? 75 : 45, i == 5 ? 100 : 71, defaults[i]);
        LabelAt(cpuPanel, "Each limit is independent; lower limits can bind first.", 16, 270, 410, 28, 9);
        var gpuPanel = Card("GPU / PLATFORM", 486, 105, 450, 310);
        Field(gpuPanel, "HP Target TPP (W)", _tpp, 50, 100, 180, 180);
        Field(gpuPanel, "NVIDIA PCF GPU maximum (W)", _gpu, 86, 100, 115, 115);
        Field(gpuPanel, "HP PLGPU (W)", _plgpu, 122, 35, 71, 71);
        Field(gpuPanel, "NVIDIA laptop target (W)", _nvpWatts, 158, 0, 0, 0);
        _nvpWatts.Enabled = false; _useNvp.Enabled = false;
        _nvpWatts.Increment = 5;
        _hpPl.Location = new Point(18, 200); _hpcm.Location = new Point(210, 200);
        _useNvp.Location = new Point(18, 231);
        gpuPanel.Controls.AddRange([_hpPl, _hpcm, _useNvp]);
        LabelAt(gpuPanel, "Higher MAX: save, reboot, then apply CURRENT.", 16, 270, 415, 30, 9);
        _keep.Location = new Point(26, 427); Controls.Add(_keep);
        ButtonAt("Apply CPU", 24, 469, 140, async () => await Run(ApplyCpu));
        ButtonAt("Apply GPU", 176, 469, 140, async () => await Run(ApplyGpu));
        ButtonAt("Apply Both", 328, 469, 140, async () => await Run(async () => { await ApplyCpu(); await ApplyGpu(); }));
        ButtonAt("Read GPU", 480, 469, 140, async () => await Run(ReadGpu));
        ButtonAt("Restore Defaults", 632, 469, 150, async () => await Run(Restore));
        ButtonAt("Advanced GPU…", 794, 469, 142, async () => await Run(OpenAdvanced));
        _log.SetBounds(24, 526, 912, 138);
        _log.BackColor = Color.FromArgb(12, 14, 19); _log.ForeColor = Color.Gainsboro;
        _log.Font = new Font("Consolas", 9); _log.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top | AnchorStyles.Bottom;
        Controls.Add(_log);
        _keep.CheckedChanged += (_, _) => { if (!_keep.Checked) { _timer.Stop(); _held = null; Log("CPU reapply stopped."); } };
        _timer.Tick += async (_, _) =>
        {
            if (_busy || _held is null || _smu is null || !_keep.Checked) return;
            var settings = _held;
            await Run(async () => { try { await Task.Run(() => _smu.ApplyCpuPower(settings)); } catch { _timer.Stop(); _held = null; throw; } });
        };
        Shown += async (_, _) =>
        {
            if (quietPreview) return;
            if (_preview)
            {
                Log("Preview — hardware is not initialized or modified.");
                using var bitmap = new Bitmap(ClientSize.Width, ClientSize.Height);
                DrawToBitmap(bitmap, ClientRectangle); bitmap.Save(capturePath!); Close(); return;
            }
            await Run(async () => {
                var profile = await Task.Run(() => new NvpwrControlBlackwell.PowerBackend().CheckCompatibility().Profile);
                if (profile == null) { Log("No supported NVIDIA laptop profile detected."); return; }
                _nvpWatts.Maximum = profile.MaxPowerW; _nvpWatts.Minimum = profile.MinW;
                _nvpWatts.Value = Math.Clamp(profile.StockPowerW, profile.MinW, profile.MaxPowerW);
                _nvpWatts.Enabled = true; _useNvp.Enabled = true;
                Log("GPU target range: " + profile);
            });
            await Run(async () => { _smu = await Task.Run(() => new SmuPowerController()); Log("CPU ready. Settings apply only when you press Apply."); });
        };
        FormClosing += (_, e) => { if (_busy) { e.Cancel = true; return; } _timer.Stop(); _smu?.Dispose(); };
    }

    private CpuSettings CpuValues() => new((int)_cpu[0].Value, (int)_cpu[1].Value, (int)_cpu[2].Value, (int)_cpu[3].Value, (int)_cpu[4].Value, (int)_cpu[5].Value);
    private async Task ApplyCpu()
    {
        var controller = _smu ?? throw new InvalidOperationException("CPU unavailable. PawnIO and the supported Ryzen AI 7 350 are required.");
        var settings = CpuValues(); settings.Validate();
        _timer.Stop(); _held = null;
        Log(await Task.Run(() => controller.ApplyCpuPower(settings)));
        _held = settings; if (_keep.Checked) _timer.Start();
    }
    private async Task ApplyGpu()
    {
        int tpp = (int)_tpp.Value, gpu = (int)_gpu.Value;
        int? pl = _hpPl.Checked ? (int)_plgpu.Value : null; bool hpcm = _hpcm.Checked;
        int nvp = (int)_nvpWatts.Value; bool useNvp = _useNvp.Checked;
        if (useNvp && nvp % 5 != 0) throw new InvalidOperationException("NVIDIA power targets require 5 W steps.");
        await Task.Run(() =>
        {
            ApplyHpState(pl, hpcm);
            using var pcf = new PcfPowerController();
            pcf.SetPowerField(PcfPowerFields.ACMaxGPULimit, (uint)gpu * 1000);
            pcf.SetPowerField(PcfPowerFields.ACTargetTPPLimit, (uint)tpp * 1000);
            Thread.Sleep(300);
            var values = pcf.GetPowerValues();
            if (values.ACMaxGPULimitInMilliwatts != (uint)gpu * 1000 || values.ACTargetTPPLimitInMilliwatts != (uint)tpp * 1000)
                throw new InvalidOperationException("HP/PCF power readback mismatch. NVIDIA target was not applied.");
        });
        Log($"HP/PCF verified: TPP {tpp} W; GPU max {gpu} W; PLGPU {(pl?.ToString() ?? "preserved")}; HPCM {hpcm}. CURRENT applies HPCM again immediately before writing.");
        if (useNvp) Log(await Task.Run(() => _currentGpu.Apply(nvp)));
    }
    private async Task ReadGpu()
    {
        try
        {
            var result = await Task.Run(() => { using var pcf = new PcfPowerController(); var v = pcf.GetPowerValues(); return $"PCF TPP {FormatWatts(v.ACTargetTPPLimitInMilliwatts)}; GPU max {FormatWatts(v.ACMaxGPULimitInMilliwatts)}"; });
            Log(result);
        }
        catch (Exception ex) { Log("PCF read unavailable: " + ex.Message); }
        Log(await Task.Run(() => _currentGpu.Read()));
    }
    private async Task Restore()
    {
        _timer.Stop(); _held = null;
        var failures = new List<string>();
        try { Log(await Task.Run(() => _currentGpu.Restore())); } catch (Exception ex) { failures.Add("NVIDIA power restore: " + ex.Message); }
        int[] defaults = { 45, 45, 45, 45, 45, 100 };
        for (int i = 0; i < 6; i++) _cpu[i].Value = defaults[i];
        try { await ApplyCpu(); } catch (Exception ex) { failures.Add("CPU restore: " + ex.Message); }
        _tpp.Value = 125; _gpu.Value = 100; _plgpu.Value = 35; _hpPl.Checked = true; _hpcm.Checked = false; _useNvp.Checked = false;
        try { await ApplyGpu(); } catch (Exception ex) { failures.Add("HP/PCF restore: " + ex.Message); }
        if (failures.Count > 0) throw new InvalidOperationException("Restore incomplete:\n" + string.Join("\n", failures));
        Log("Tested runtime defaults restored. Advanced NvAPI tuning must be restored in Advanced GPU.");
    }
    private Task OpenAdvanced()
    {
        OpenNvidiaRequested?.Invoke();
        return Task.CompletedTask;
    }
    private async Task Run(Func<Task> action)
    {
        if (_busy || _preview) return;
        _busy = true; UseWaitCursor = true;
        foreach (var button in _buttons) button.Enabled = false;
        try { await action(); }
        catch (Exception ex) { Log("ERROR: " + ex.Message); }
        finally { _busy = false; UseWaitCursor = false; foreach (var button in _buttons) button.Enabled = true; }
    }
    private void Log(string text) => _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    private Panel Card(string title, int x, int y, int width, int height)
    {
        var panel = new Panel { Location = new Point(x, y), Size = new Size(width, height), BackColor = Color.FromArgb(32, 37, 47) };
        Controls.Add(panel); LabelAt(panel, title, 16, 12, width - 32, 28, 12); return panel;
    }
    private void Field(Control parent, string label, NumericUpDown input, int y, int min, int max, int value)
    {
        LabelAt(parent, label, 16, y + 3, 290, 27);
        input.Minimum = min; input.Maximum = max; input.Value = value; input.SetBounds(parent.Width - 124, y, 105, 28);
        input.BackColor = Color.FromArgb(18, 21, 28); input.ForeColor = Color.White; parent.Controls.Add(input);
    }
    private static void LabelAt(Control parent, string text, int x, int y, int width, int height, float size = 10)
        => parent.Controls.Add(new Label { Text = text, Location = new Point(x, y), Size = new Size(width, height), Font = new Font("Segoe UI", size), ForeColor = Color.Gainsboro });
    private void ButtonAt(string text, int x, int y, int width, Func<Task> action)
    {
        var button = new Button { Text = text, Location = new Point(x, y), Size = new Size(width, 38), BackColor = Color.FromArgb(49, 99, 176), ForeColor = Color.White, FlatStyle = FlatStyle.Flat, Font = new Font("Segoe UI", 9) };
        button.Click += async (_, _) => await action(); Controls.Add(button); _buttons.Add(button);
    }
        private static string FormatWatts(uint milliwatts) =>
        milliwatts == uint.MaxValue ? "released" : $"{milliwatts / 1000.0:F1} W";

    private static void ApplyHpState(int? plGpuWatts, bool hpcmEnabled)
    {
        lock (HpPlatform.Sync) ApplyHpStateLocked(plGpuWatts, hpcmEnabled);
    }
    private static void ApplyHpStateLocked(int? plGpuWatts, bool hpcmEnabled)
    {
        using var hp = new HpWmiBios();
        if (!hp.IsAvailable)
            throw new InvalidOperationException("HP WMI BIOS interface is unavailable: " + hp.Status);
        RequireHpCommand(hp, 0x1A, [0xFF, hpcmEnabled ? (byte)0x01 : (byte)0x00]);
        RequireHpCommand(hp, 0x22, [0x01, 0x01, 0x01, 0x00]);
        if (plGpuWatts.HasValue)
            RequireHpCommand(hp, 0x29, [0xFF, 0xFF, 0xFF, checked((byte)plGpuWatts.Value)]);
    }

    private static void RequireHpCommand(HpWmiBios hp, uint command, byte[] payload)
    {
        var send = typeof(HpWmiBios).GetMethod(
            "SendBiosCommand", BindingFlags.Instance | BindingFlags.NonPublic,
            binder: null,
            types: [typeof(HpWmiBios.BiosCmd), typeof(uint), typeof(byte[]), typeof(byte)],
            modifiers: null)
            ?? throw new MissingMethodException("HP WMI command transport was not found.");
        var result = send.Invoke(hp, [HpWmiBios.BiosCmd.Default, command, payload, (byte)0]);
        if (result is null)
            throw new InvalidOperationException($"HP WMI command 0x{command:X2} failed.");
    }


}
