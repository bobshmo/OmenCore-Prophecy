using FluentAssertions;
using OmenCore.Hardware;
using OmenCore.Services;
using OmenCore.Services.KeyboardLighting;
using OmenCore.ViewModels;
using Xunit;

namespace OmenCoreApp.Tests.Services
{
    /// <summary>
    /// GitHub #217 (88EE): the database and the firmware both say backlight-only, yet the V1 fallback
    /// offered four-zone colour writes. Suppress only on agreement, and never on pre-2021 boards.
    /// </summary>
    public class KeyboardColorSuppressionTests
    {
        private const HpWmiBios.KeyboardLightingType Normal = HpWmiBios.KeyboardLightingType.Normal;

        [Fact]
        public void DatabaseAndFirmwareAgree_On2021PlusBoard_Suppresses()
        {
            KeyboardLightingService.ShouldSuppressColorControl(KeyboardMethod.BacklightOnly, Normal, 2022, v2FoundBackend: false)
                .Should().BeTrue();
        }

        [Fact]
        public void FirmwareReportsFourZone_DoesNotSuppress()
        {
            KeyboardLightingService.ShouldSuppressColorControl(
                KeyboardMethod.BacklightOnly, HpWmiBios.KeyboardLightingType.FourZoneWithoutNumpad, 2022, false)
                .Should().BeFalse("a board the database guessed wrong must keep its working colour path");
        }

        [Fact]
        public void FirmwareGivesNoAnswer_DoesNotSuppress()
        {
            KeyboardLightingService.ShouldSuppressColorControl(KeyboardMethod.BacklightOnly, null, 2022, false).Should().BeFalse();
        }

        [Fact]
        public void Pre2021Board_NeverSuppressed_BecauseTheProbeMayJustBeUnimplemented()
        {
            // 8600 (OMEN 15-dh0, 2019): "backlight-only" is a conservative placeholder there.
            KeyboardLightingService.ShouldSuppressColorControl(KeyboardMethod.BacklightOnly, Normal, 2019, false).Should().BeFalse();
            KeyboardLightingService.ShouldSuppressColorControl(KeyboardMethod.BacklightOnly, Normal, 0, false).Should().BeFalse();
        }

        [Fact]
        public void DatabaseSaysColour_OrBackendFound_DoesNotSuppress()
        {
            KeyboardLightingService.ShouldSuppressColorControl(KeyboardMethod.ColorTable2020, Normal, 2022, false).Should().BeFalse();
            KeyboardLightingService.ShouldSuppressColorControl(KeyboardMethod.BacklightOnly, Normal, 2022, v2FoundBackend: true).Should().BeFalse();
        }

        [Fact]
        public void Every2021PlusBacklightOnlyEntry_IsAVictus()
        {
            // Documents why the year gate is safe: nothing OMEN-branded that is 2021+ is BacklightOnly.
            foreach (var id in new[] { "8A23", "8A3E", "8C30", "8BB4", "88EC", "88EE" })
            {
                var cfg = KeyboardModelDatabase.GetConfig(id);
                cfg.Should().NotBeNull(id);
                cfg!.ModelName.Should().Contain("Victus", id);
            }
        }

        [Fact]
        public void UnavailableMessage_ForBacklightOnly_DoesNotReadLikeAFault()
        {
            LightingViewModel.BuildKeyboardUnavailableMessage(true).Should().Contain("single-colour").And.Contain("Fn");
            LightingViewModel.BuildKeyboardUnavailableMessage(false).Should().Contain("not available");
        }
    }
}
