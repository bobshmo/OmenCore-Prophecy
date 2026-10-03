using FluentAssertions;
using OmenCore.Models;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>
    /// A startup recovery that only rewrites config leaves the untested values live in the driver / CPU.
    /// These pin which hardware areas get zeroed for which recovery outcome.
    /// </summary>
    public class TuningHardwareRecoveryTests
    {
        [Fact]
        public void NoRecovery_TouchesNothing()
        {
            SystemControlViewModel.HardwareRecoveryTargets(null).Should().Be((false, false));
            SystemControlViewModel.HardwareRecoveryTargets(new TuningStartupRecoveryOutcome()).Should().Be((false, false));
        }

        [Fact]
        public void CpuUndervoltRecovery_ZeroesOnlyTheCpu()
        {
            SystemControlViewModel.HardwareRecoveryTargets(new TuningStartupRecoveryOutcome { CpuUndervoltReset = true })
                .Should().Be((true, false));
        }

        [Fact]
        public void GpuOcRecovery_ZeroesOnlyTheGpu()
        {
            SystemControlViewModel.HardwareRecoveryTargets(new TuningStartupRecoveryOutcome { GpuOcReset = true })
                .Should().Be((false, true));
        }

        [Fact]
        public void BothRecovered_ZeroesBoth()
        {
            SystemControlViewModel.HardwareRecoveryTargets(
                new TuningStartupRecoveryOutcome { CpuUndervoltReset = true, GpuOcReset = true })
                .Should().Be((true, true));
        }
    }
}
