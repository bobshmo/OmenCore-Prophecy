using FluentAssertions;
using OmenCore.Hardware;
using Xunit;

namespace OmenCoreApp.Tests.Hardware
{
    /// <summary>
    /// A firmware fan count that was actually read must not be cut down by an unverified database entry
    /// (88F8 #207, 8C2D #205, 8C30 #208/#220). Verified entries still win; a failed read never counts.
    /// </summary>
    public class FirmwareFanCountPolicyTests
    {
        [Fact]
        public void FirmwareSaysTwo_UnverifiedEntrySaysOne_TrustsFirmware()
        {
            CapabilityDetectionService.ShouldTrustFirmwareFanCount(false, true, databaseCount: 1, firmwareCount: 2).Should().BeTrue();
        }

        [Fact]
        public void UserVerifiedEntry_KeepsItsDatabaseCount()
        {
            CapabilityDetectionService.ShouldTrustFirmwareFanCount(true, true, 1, 2).Should().BeFalse();
        }

        [Fact]
        public void DefaultTwoFromAFailedRead_IsNotEvidence()
        {
            // HpWmiBios.FanCount defaults to 2 when the query is unanswered.
            CapabilityDetectionService.ShouldTrustFirmwareFanCount(false, firmwareCountWasRead: false, 1, 2).Should().BeFalse();
        }

        [Theory]
        [InlineData(0)]
        [InlineData(3)]
        [InlineData(255)]
        public void ImplausibleFirmwareCounts_AreIgnored(int firmwareCount)
        {
            CapabilityDetectionService.ShouldTrustFirmwareFanCount(false, true, 1, firmwareCount).Should().BeFalse();
        }

        [Fact]
        public void FirmwareSayingFewerFansThanTheDatabase_IsNotPromoted()
        {
            CapabilityDetectionService.ShouldTrustFirmwareFanCount(false, true, databaseCount: 2, firmwareCount: 1).Should().BeFalse();
        }

        [Fact]
        public void Board8C30_NowDeclaresItsTwoFirmwareFans()
        {
            ModelCapabilityDatabase.GetCapabilities("8C30").FanZoneCount.Should().Be(2);
        }
    }
}
