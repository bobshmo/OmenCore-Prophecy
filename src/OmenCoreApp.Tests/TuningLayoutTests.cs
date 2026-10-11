using System.IO;
using System.Xml.Linq;
using OmenCore.Views;

namespace OmenCoreApp.Tests;

public class TuningLayoutTests
{
    private static string Root()
    {
        var dir=new DirectoryInfo(AppContext.BaseDirectory);
        while(dir!=null){if(File.Exists(Path.Combine(dir.FullName,"OmenCore.sln")))return dir.FullName;dir=dir.Parent;}
        throw new InvalidOperationException("Source checkout not found.");
    }
    [Fact]
    public void TuningPage_HasOneContentHostAndNoAppendedDuplicateSections()
    {
        XNamespace w="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        XNamespace views="clr-namespace:OmenCore.Views";
        var root=XDocument.Load(Path.Combine(Root(),"src/OmenCoreApp/Views/TuningView.xaml")).Root!;
        var body=root.Element(w+"ScrollViewer")!.Element(w+"StackPanel")!;
        Assert.Equal(new[]{w+"Grid",views+"ProphecyView"},body.Elements().Select(e=>e.Name));
        var host=body.Element(views+"ProphecyView")!;
        Assert.NotNull(host.Element(views+"ProphecyView.CpuContent"));
        Assert.NotNull(host.Element(views+"ProphecyView.GpuRecoveryContent"));
        Assert.NotNull(host.Element(views+"ProphecyView.DiagnosticsContent"));
        Assert.Single(root.Descendants(views+"ProphecyView"));
    }
    [Fact]
    public void OneTabSet_KeepsAllSectionsAndRecoveryControls()
    {
        XNamespace w="http://schemas.microsoft.com/winfx/2006/xaml/presentation";
        var root=XDocument.Load(Path.Combine(Root(),"src/OmenCoreApp/Views/ProphecyView.xaml")).Root!;
        var tabs=Assert.Single(root.Descendants(w+"TabControl"));
        Assert.Equal(new[]{"GPU power","GPU tuning","CPU tuning","Victus","Profiles","Device"},tabs.Elements(w+"TabItem").Select(e=>e.Attribute("Header")!.Value));
        var recovery=Assert.Single(root.Descendants(w+"Expander"));
        Assert.Equal("False",recovery.Attribute("IsExpanded")!.Value);
        Assert.Contains("GpuRecoveryContent",recovery.Element(w+"ContentPresenter")!.Attribute("Content")!.Value);
    }
    [Fact]
    public void NestedSelectorEvents_DoNotTriggerShellNavigation()
    {
        var shell=new object();var inner=new object();
        Assert.True(MainWindow.IsShellSelectionEvent(shell,shell,shell));
        Assert.False(MainWindow.IsShellSelectionEvent(shell,inner,shell));
        Assert.False(MainWindow.IsShellSelectionEvent(inner,inner,shell));
    }
}
