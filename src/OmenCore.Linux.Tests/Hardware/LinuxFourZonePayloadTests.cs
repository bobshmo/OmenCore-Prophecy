using FluentAssertions;
using OmenCore.Linux.Hardware;

namespace OmenCore.Linux.Tests.Hardware;

/// <summary>
/// Wire format of the DKMS hp-wmi four-zone nodes (from the OMEN Slim 16 / 8D40 fork by saikiranworks).
/// </summary>
public class LinuxFourZonePayloadTests
{
    [Fact]
    public void Uniform_RepeatsTheColourAcrossAllFourZones()
    {
        LinuxFourZonePayload.Uniform(0x00, 0xBF, 0xFF).Should().Be("00bfff00bfff00bfff00bfff");
    }

    [Fact]
    public void WithZone_ReplacesOnlyThatZone()
    {
        var result = LinuxFourZonePayload.WithZone("ff0000" + "00ff00" + "0000ff" + "ffffff", 2, 0x12, 0x34, 0x56);
        result.Should().Be("ff0000" + "00ff00" + "123456" + "ffffff");
    }

    [Theory]
    [InlineData("", "000000000000000000000000")]
    [InlineData("ff", "ff0000000000000000000000")]
    [InlineData("  FF0000FF0000FF0000FF0000\n", "ff0000ff0000ff0000ff0000")]
    [InlineData("ff0000ff0000ff0000ff0000EXTRA", "ff0000ff0000ff0000ff0000")]
    [InlineData("zzzzzzzzzzzzzzzzzzzzzzzz", "000000000000000000000000")]
    public void Normalize_AlwaysYieldsExactly24LowercaseHexChars(string input, string expected)
    {
        LinuxFourZonePayload.Normalize(input).Should().Be(expected);
    }

    [Fact]
    public void Normalize_Null_IsAllBlack()
    {
        LinuxFourZonePayload.Normalize(null).Should().Be(new string('0', 24));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    public void WithZone_RejectsZonesOutsideZeroToThree(int zone)
    {
        var act = () => LinuxFourZonePayload.WithZone("", zone, 1, 2, 3);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 255)]
    [InlineData(50, 128)]
    [InlineData(-10, 0)]
    [InlineData(250, 255)]
    public void PercentToRaw_ScalesTo0To255AndClamps(int percent, int expected)
    {
        LinuxFourZonePayload.PercentToRaw(percent).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(255, 100)]
    [InlineData(128, 50)]
    [InlineData(999, 100)]
    public void RawToPercent_ScalesBackAndClamps(int raw, int expected)
    {
        LinuxFourZonePayload.RawToPercent(raw).Should().Be(expected);
    }

    [Theory]
    [InlineData(0, false, false, true)]    // dark gate + visible colour: raise it
    [InlineData(0, true, false, false)]    // black colour: leave it
    [InlineData(0, false, true, false)]    // user opted out
    [InlineData(128, false, false, false)] // gate already open
    public void BrightnessGate_IsRaisedOnlyForAVisibleColourOnADarkKeyboard(int raw, bool black, bool optedOut, bool expected)
    {
        LinuxFourZonePayload.ShouldRaiseBrightnessGate(raw, black, optedOut).Should().Be(expected);
    }
}
