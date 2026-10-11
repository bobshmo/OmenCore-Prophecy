using NvpwrControlBlackwell;
using OmenCore.ViewModels;
using VictusPowerUnlockGui;
using OmenCore.Hardware;
namespace OmenCoreApp.Tests;
public class Prophecy445Tests
{
    [Fact]
    public async Task VerifiedPcfWriteWithFailedRefresh_KeepsVerifiedToggleState(){var d=new FakeProphecyDevice{FailRead=true};using var vm=new ProphecyViewModel(d);vm.ApplySnapshot(d.Snapshot with{Pcf=new PcfDetection(true,115,80000,80000,true,"test","PCI")});await vm.ExecuteAsync("pcf-on");Assert.True(vm.PcfMaxEnabled);Assert.Contains("Readback refresh failed",vm.Status);}
    [Fact]
    public async Task RefusedPcfApply_ReassertsUncheckedState(){var d=new FakeProphecyDevice{Operation=_=>OperationResult.Fail("PCF refused")};using var vm=new ProphecyViewModel(d);vm.ApplySnapshot(d.Snapshot with{Pcf=new PcfDetection(true,115,80000,80000,true,"test","PCI")});int updates=0;vm.PropertyChanged+=(_,e)=>{if(e.PropertyName==nameof(vm.PcfMaxEnabled))updates++;};await vm.ExecuteAsync("pcf-on");Assert.False(vm.PcfMaxEnabled);Assert.True(updates>0);Assert.Contains("PCF refused",vm.Status);}
    [Theory]
    [InlineData("1",true)][InlineData("73",true)][InlineData("140",true)]
    [InlineData("141",false)][InlineData("0",false)][InlineData("NaN",false)][InlineData("72.5",false)]
    public void CurrentAcceptsWholeWattsOnlyUpToLiveMax(string text,bool enabled){var d=new FakeProphecyDevice();using var vm=new ProphecyViewModel(d);vm.ApplySnapshot(d.Snapshot);vm.CurrentPowerText=text;Assert.Equal(enabled,vm.CanExecute("current"));vm.ApplySnapshot(d.Snapshot);Assert.Equal(text,vm.CurrentPowerText);Assert.Equal(enabled,vm.CanExecute("current"));}
    [Fact]
    public void PcfHasOneStockToggleAndNoEditableGpuCap(){using var vm=new ProphecyViewModel(new FakeProphecyDevice());Assert.DoesNotContain(vm.HpFields,f=>f.Id=="gpu");Assert.False(vm.CanExecute("pcf-on"));}
    [Theory]
    [InlineData("RTX 5050 Laptop",115)][InlineData("RTX 5060 Laptop",115)][InlineData("RTX 5070 Laptop",115)]
    [InlineData("RTX 5070 Ti Laptop",140)][InlineData("RTX 5080 Laptop",175)][InlineData("RTX 5090 Laptop",175)]
    [InlineData("RTX 4050 Laptop",140)][InlineData("RTX 4060 Laptop",140)][InlineData("RTX 4070 Laptop",140)]
    [InlineData("RTX 4080 Laptop",175)][InlineData("RTX 4090 Laptop",175)]
    public void StockPcfCeilingIncludesDynamicBoost(string model,int watts)=>Assert.Equal(watts,GpuProfiles.Detect(model).StockTgpMaxW);
    [Theory]
    [InlineData(true,80000,115,true)][InlineData(false,80000,115,false)][InlineData(true,115000,115,false)][InlineData(true,0,115,false)]
    public void OnlyDetectedHpRestrictionMayEnableWithoutPrompt(bool hp,uint cap,int stock,bool auto)=>Assert.Equal(auto,PcfUnlock.ShouldAutoEnable(hp,cap,stock));
    [Fact]
    public void LegacyFirmwareUsesLegacyPerformance(){Assert.Equal(HpWmiBios.FanMode.LegacyPerformance,HpWmiMode.ModeForPolicy(0,true));Assert.Equal(HpWmiBios.FanMode.Performance,HpWmiMode.ModeForPolicy(1,true));}
    [Fact]
    public void FirmwareImportChecksExactPciImages(){byte[] rom=new byte[0x10000];rom[0]=0x55;rom[1]=0xAA;rom[0x18]=0x40;"PCIR"u8.CopyTo(rom.AsSpan(0x40));BitConverter.GetBytes((ushort)0x10DE).CopyTo(rom,0x44);BitConverter.GetBytes((ushort)0x2D19).CopyTo(rom,0x46);Assert.True(VbiosResolver.RomMatchesDevice(rom,0x2D19));Assert.False(VbiosResolver.RomMatchesDevice(rom,0x2D98));Assert.False(VbiosResolver.RomMatchesDevice(new byte[10],0x2D19));}
}
