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
