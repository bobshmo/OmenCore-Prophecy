using OmenCore.Hardware;
using OmenCore.Models;
using NvpwrControlBlackwell;
using VictusPowerUnlockGui;
using Prophecy.Integration;

namespace OmenCoreApp.Tests;

public class ProphecyIntegrationTests
{
    public static IEnumerable<object[]> LaptopIds() => GpuProfiles.All().SelectMany(p =>
        p.KnownDeviceIds.Select(id => new object[] { id, p.Id, p.Match }));
    [Theory]
    [MemberData(nameof(LaptopIds))]
    public void LaptopPciIdentity_SelectsTheExpectedModel(int id, string profile, string name)
    {
        var pci = $"PCI\\VEN_10DE&DEV_{id:X4}&SUBSYS_1234103C\\TEST";
        Assert.Equal(profile, GpuProfiles.Detect(name,pci)?.Id);
        Assert.Equal(profile, GpuProfiles.Detect("Generic NVIDIA adapter",pci)?.Id);
        Assert.Null(GpuProfiles.Detect(name,pci.Replace("VEN_10DE","VEN_1002")));
    }
    [Fact]
    public void UnsupportedAndContradictoryIdentities_BlockMapping()
    {
        Assert.Null(GpuProfiles.Detect("RTX 5060 Laptop", "PCI\\VEN_10DE&DEV_FFFF"));
        Assert.Null(GpuProfiles.Detect("RTX 5060 Laptop", "PCI\\VEN_10DE&DEV_2717"));
        Assert.Null(GpuProfiles.Detect("RTX 4060 Laptop", "PCI\\VEN_10DE&DEV_2882"));
    }
    [Fact]
    public void RoutingAndHpcmFailure_DoNotSendUnpreparedCurrent() => ProphecyControls.RunSelfTests();
    [Fact]
    public void CpuGate_StaysExactAndDoesNotChangeNativeFamily()
    {
        Assert.True(SmuPowerController.SupportsCpu("AMD Ryzen AI 7 350", "AMD64 Family 26 Model 96 Stepping 0", RyzenFamily.Unknown));
        Assert.False(SmuPowerController.SupportsCpu("AMD Ryzen AI 9 HX 370", "AMD64 Family 26 Model 96 Stepping 0", RyzenFamily.Unknown));
        Assert.False(SmuPowerController.SupportsCpu("AMD Ryzen AI 7 350", "AMD64 Family 26 Model 36 Stepping 0", RyzenFamily.Unknown));
        Assert.False(SmuPowerController.SupportsCpu("AMD Ryzen AI 7 350", "AMD64 Family 26 Model 96 Stepping 0", RyzenFamily.StrixPoint));
    }
}
