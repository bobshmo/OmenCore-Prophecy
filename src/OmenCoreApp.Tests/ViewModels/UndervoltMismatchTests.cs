using FluentAssertions;
using OmenCore.Models;
using OmenCore.Services;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.ViewModels
{
    /// <summary>GitHub #220: a never-applied draft slider must not read as a failed undervolt.</summary>
    public class UndervoltMismatchTests
    {
        private static UndervoltStatus Live(double core, double cache) =>
            new() { CurrentCoreOffsetMv = core, CurrentCacheOffsetMv = cache };

        [Fact]
        public void NothingApplied_DraftDiffersFromHardware_IsNotAMismatch()
        {
            SystemControlViewModel.ComputeUndervoltMismatch(Live(0, 0), -90, -60, applyAttempted: false).Should().BeFalse();
        }

        [Fact]
        public void AfterAnApply_HardwareStillAtZero_IsAMismatch()
        {
            SystemControlViewModel.ComputeUndervoltMismatch(Live(0, 0), -90, -60, applyAttempted: true).Should().BeTrue();
        }

        [Fact]
        public void AfterAnApply_HardwareMatchesRequest_IsNotAMismatch()
        {
            SystemControlViewModel.ComputeUndervoltMismatch(Live(-90, -60), -90, -60, applyAttempted: true).Should().BeFalse();
        }

        [Fact]
        public void NoStatus_IsNeverAMismatch()
        {
            SystemControlViewModel.ComputeUndervoltMismatch(null, -90, -60, true).Should().BeFalse();
        }

        [Fact]
        public void FreshInstallDefault_RequestsNoUndervolt()
        {
            var cfg = DefaultConfiguration.Create();
            cfg.Undervolt.DefaultOffset!.CoreMv.Should().Be(0);
            cfg.Undervolt.DefaultOffset.CacheMv.Should().Be(0);
            cfg.Undervolt.ApplyOnStartup.Should().BeFalse();
        }
    }
}
