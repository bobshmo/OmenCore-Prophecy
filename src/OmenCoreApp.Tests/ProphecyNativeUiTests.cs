using OmenCore.ViewModels;
using NvpwrControlBlackwell;
using VictusPowerUnlockGui;
using Prophecy.Integration;
using System.Text.Json.Nodes;
using System.IO;

namespace OmenCoreApp.Tests;

public static class ProphecyPreviewFixture
{
    public static ProphecyViewModel Create()=>new(new FakeProphecyDevice());
}
internal sealed class FakeProphecyDevice : IProphecyDevice
{
    internal List<string> Actions {get;}=new();
    internal Func<string,OperationResult>? Operation;
    internal bool FailRead,Disposed;
    internal int TelemetryReads;
    internal ProphecySnapshot Snapshot {get;}=new(
        new CompatibilityState {GpuName="NVIDIA RTX 5060 Laptop GPU",DriverVersion="Demo",Vbios="Preview",Profile=GpuProfiles.Detect("RTX 5060 Laptop"),CurrentWritesReady=true,MaxWritesReady=true,Reason="Preview data · hardware is not initialized."},
        new PowerState{CurrentW=115,MaxW=140},
        new TelemetryState{PowerW=12,GpuTempC=42,CoreClockMHz=1200,MemoryClockMHz=6000,UtilizationPct=2,Power=new PowerState{CurrentW=115,MaxW=140}},
        new TunerState {CoreMHz=new TuneRange{Supported=true,Min=-500,Max=1000},MemoryMHz=new TuneRange{Supported=true,Min=-500,Max=3000},NvvddMv=new TuneRange{Supported=true,Min=-100,Max=100},XbarWritable=true,RatioWritable=true,GpcXbarRatio=0.9},
        true,true,false,null,null);
    public ProphecySnapshot Read(){if(FailRead)throw new InvalidOperationException("read failed");return Snapshot;}
    public TelemetryState ReadTelemetry(){TelemetryReads++;return Snapshot.Telemetry;}
    public OperationResult Execute(string action,int value=0,string? path=null,TuneRequest? tuning=null,CpuSettings? cpu=null,ProphecyHpLimits? hp=null){Actions.Add(action);return Operation?.Invoke(action)??OperationResult.Ok("Applied and verified.");}
    public void Dispose()=>Disposed=true;
}
public sealed class ProphecyNativeUiTests
{
    [Theory]
    [InlineData("not a number")][InlineData("NaN")][InlineData("Infinity")][InlineData("1001")][InlineData("0.5")]
    public void InvalidInput_CannotBecomeATuningWrite(string text){using var vm=new ProphecyViewModel(new FakeProphecyDevice());vm.ApplySnapshot(new FakeProphecyDevice().Snapshot);var f=vm.TuneFields[0];f.Enabled=true;f.Text=text;Assert.False(vm.CanExecute("tune"));Assert.Throws<InvalidOperationException>(()=>vm.TuneValues());}
    [Fact]
    public async Task TelemetryAndRefresh_PreserveEditedTuning(){var device=new FakeProphecyDevice();using var vm=new ProphecyViewModel(device);vm.ApplySnapshot(device.Snapshot);vm.TuneFields[0].Text="250";vm.TuneFields[0].Enabled=true;await vm.PollAsync();vm.ApplySnapshot(device.Snapshot);Assert.Equal("250",vm.TuneFields[0].Text);Assert.True(vm.TuneFields[0].Enabled);Assert.Empty(device.Actions);}
    [Fact]
    public void SelectorResetDuringRefresh_PreservesPendingPowerTargets(){var device=new FakeProphecyDevice();using var vm=new ProphecyViewModel(device);vm.ApplySnapshot(device.Snapshot);vm.MaxTarget=130;vm.CurrentTarget=125;vm.MaxTargets.CollectionChanged+=(_,e)=>{if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset)vm.MaxTarget=0;};vm.CurrentTargets.CollectionChanged+=(_,e)=>{if(e.Action==System.Collections.Specialized.NotifyCollectionChangedAction.Reset)vm.CurrentTarget=0;};vm.ApplySnapshot(device.Snapshot);Assert.Equal(130,vm.MaxTarget);Assert.Equal(125,vm.CurrentTarget);}
    [Fact]
    public async Task FailedTuningRestore_PreservesSelectedFields(){var device=new FakeProphecyDevice{Operation=_=>OperationResult.Fail("Driver refused")};using var vm=new ProphecyViewModel(device);vm.ApplySnapshot(device.Snapshot);vm.TuneFields[0].Enabled=true;vm.TuneFields[0].Text="200";await vm.ExecuteAsync("restore-tune");Assert.True(vm.TuneFields[0].Enabled);Assert.Equal("200",vm.TuneFields[0].Text);}
    [Fact]
    public async Task CpuReapply_PreservesInputFocusWhileBlockingOtherWrites(){using var vm=new ProphecyViewModel(new FakeProphecyDevice());var done=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var running=vm.RunAsync(async()=>{await done.Task;return "CPU accepted.";},repeat:true);Assert.True(vm.Busy);Assert.True(vm.EditingEnabled);Assert.False(vm.Idle);done.SetResult();await running;Assert.True(vm.Idle);}
    [Fact]
    public async Task ConcurrentApplyIsBlocked_AndDisposeWaitsForTheWriter(){var started=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var finish=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);var device=new FakeProphecyDevice{Operation=_=>{started.SetResult();finish.Task.GetAwaiter().GetResult();return OperationResult.Ok("Verified.");}};var vm=new ProphecyViewModel(device);vm.ApplySnapshot(device.Snapshot);var running=vm.ExecuteAsync("current");await started.Task;Assert.True(vm.Busy);await vm.ExecuteAsync("max");await vm.PollAsync();vm.Dispose();Assert.False(device.Disposed);finish.SetResult();await running;Assert.Equal(new[]{"current"},device.Actions);Assert.Equal(0,device.TelemetryReads);Assert.True(device.Disposed);}
    [Fact]
    public async Task BackendFailure_IsReportedWithoutSuccess(){var device=new FakeProphecyDevice{Operation=_=>OperationResult.Fail("HPCM refused")};using var vm=new ProphecyViewModel(device);vm.ApplySnapshot(device.Snapshot);await vm.ExecuteAsync("current");Assert.Contains("HPCM refused",vm.Status);Assert.DoesNotContain("Applied and verified",vm.Activity);Assert.False(vm.Busy);}
    [Fact]
    public async Task SuccessfulWriteWithFailedRefresh_KeepsTheVerifiedResult(){var device=new FakeProphecyDevice{FailRead=true};using var vm=new ProphecyViewModel(device);vm.ApplySnapshot(device.Snapshot);await vm.ExecuteAsync("current");Assert.Contains("Applied and verified",vm.Status);Assert.Contains("Readback refresh failed",vm.Status);Assert.False(vm.CanExecute("current"));}
    [Fact]
    public void UnsupportedCpuAndUnknownGpu_BlockThoseControls(){using var vm=new ProphecyViewModel(new FakeProphecyDevice());vm.ApplySnapshot(new FakeProphecyDevice().Snapshot with {CpuSupported=false,Compatibility=new CompatibilityState()});Assert.False(vm.CanExecute("cpu"));Assert.False(vm.CanExecute("current"));Assert.False(vm.CanExecute("max"));}
    [Fact]
    public void OffsetVoltageAndCurveTarget_UseDistinctTransports(){var offset=NvApiTuner.VoltageRouting(new TuneRequest{SetNvvdd=true,NvvddMv=-25});Assert.True(offset.Pstates);Assert.False(offset.Curve);var target=NvApiTuner.VoltageRouting(new TuneRequest{SetNvvdd=true,NvvddIsTarget=true,NvvddMv=1000});Assert.False(target.Pstates);Assert.True(target.Curve);}
    [Fact]
    public void NativeVoltageField_PreservesTheReportedControlKind(){using var vm=new ProphecyViewModel(new FakeProphecyDevice());vm.ApplySnapshot(new FakeProphecyDevice().Snapshot);var field=vm.TuneFields[2];field.Enabled=true;field.Text="-25";Assert.False(vm.TuneValues().NvvddIsTarget);field.Update(true,900,1150,1000);field.Text="1000";Assert.True(vm.TuneValues().NvvddIsTarget);}
    private static string ProfileFile()=>Path.Combine(Path.GetTempPath(),Guid.NewGuid()+".json");
    private static JsonObject Store()=>JsonNode.Parse("""{"schema":"mvolt.profile.v3","adapter_id":"fixture","vbios_id":"test","profiles":[{"id":"one","name":"Test profile","controls":{}}]}""")!.AsObject();
    [Fact]
    public void ChangedProfileStore_IsRejectedBeforeAnyCliCall(){string path=ProfileFile();try{var store=Store();File.WriteAllText(path,store.ToJsonString());var client=new MvoltClient(_=>throw new Exception("CLI must not run"));client.Import(path);client.Select("one");store["profiles"]![0]!["name"]="changed";File.WriteAllText(path,store.ToJsonString());Assert.Throws<InvalidOperationException>(client.ValidateSaved);}finally{File.Delete(path);}}
    [Fact]
    public async Task MismatchedVbios_DoesNotApplyImportedProfile(){string path=ProfileFile();try{File.WriteAllText(path,Store().ToJsonString());var calls=new List<string>();var client=new MvoltClient(args=>{calls.Add(string.Join(' ',args));return Task.FromResult("{\"adapter_id\":\"fixture\",\"vbios\":\"different\"}");});client.Import(path);client.Select("one");await Assert.ThrowsAsync<InvalidOperationException>(client.ApplyAsync);Assert.DoesNotContain(calls,s=>s.Contains("--profile"));}finally{File.Delete(path);}}
    [Fact]
    public async Task FullSnapshot_RequiresSuccessfulPowerPreparation(){string path=ProfileFile();try{var store=Store();store["profiles"]![0]!["full_snapshot"]=true;File.WriteAllText(path,store.ToJsonString());var calls=new List<string>();var client=new MvoltClient(args=>{calls.Add(string.Join(' ',args));return Task.FromResult("{\"adapter_id\":\"fixture\",\"vbios\":\"test\"}");},()=>throw new InvalidOperationException("HPCM refused"));client.Import(path);client.Select("one");await Assert.ThrowsAsync<InvalidOperationException>(client.ApplyAsync);Assert.DoesNotContain(calls,s=>s.Contains("--profile"));}finally{File.Delete(path);}}
}
