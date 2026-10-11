using System.IO;
using NvpwrControlBlackwell;

namespace OmenCoreApp.Tests;

[Collection("Config Isolation")]
public sealed class ProphecyResolverStateTests : IDisposable
{
    private readonly string _root=Path.Combine(Path.GetTempPath(),"ProphecyStateTests",Guid.NewGuid().ToString());
    private readonly string? _previous=Environment.GetEnvironmentVariable("OMENCORE_PROPHECY_STATE_DIR");
    public ProphecyResolverStateTests(){Environment.SetEnvironmentVariable("OMENCORE_PROPHECY_STATE_DIR",Path.Combine(_root,"durable"));}
    [Fact]
    public void ImportOldAppState_PersistsBothCachesOutsideAppFolder(){
        string old=Path.Combine(_root,"old","state");Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old,"driver-resolver-cache.txt"),"test-driver-row");
        File.WriteAllText(Path.Combine(old,"vbios-resolver-cache-v3.txt"),"test-vbios-row");
        Assert.True(ResolverStateStore.Import(Path.GetDirectoryName(old)!).Success);
        Assert.Equal("test-driver-row",File.ReadAllLines(ResolverStateStore.CacheFile("driver-resolver-cache.txt"))[0]);
        Assert.Equal("test-vbios-row",File.ReadAllLines(ResolverStateStore.CacheFile("vbios-resolver-cache-v3.txt"))[0]);
        Assert.StartsWith(Path.Combine(_root,"durable"),ResolverStateStore.CacheFile("driver-resolver-cache.txt"));
    }
    [Fact]
    public void ImportMergesWithoutDuplicatingOrRemovingExistingValidation(){
        string old=Path.Combine(_root,"old");Directory.CreateDirectory(old);
        File.WriteAllText(Path.Combine(old,"driver-resolver-cache.txt"),"first");
        Assert.True(ResolverStateStore.Import(old).Success);
        File.WriteAllText(Path.Combine(old,"driver-resolver-cache.txt"),"first\nsecond");
        Assert.True(ResolverStateStore.Import(old).Success);
        Assert.Equal(new[]{"first","second"},File.ReadAllLines(ResolverStateStore.CacheFile("driver-resolver-cache.txt")));
    }
    [Fact]
    public void MissingCachesAndUnknownCacheNamesAreRejected(){Assert.False(ResolverStateStore.Import(_root).Success);Assert.Throws<ArgumentException>(()=>ResolverStateStore.CacheFile("../settings.ini"));}
    public void Dispose(){Environment.SetEnvironmentVariable("OMENCORE_PROPHECY_STATE_DIR",_previous);if(Directory.Exists(_root))Directory.Delete(_root,true);}
}
