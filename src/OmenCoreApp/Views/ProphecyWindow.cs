using System.Windows;
using System.Windows.Forms.Integration;
using OmenCore.Services;
using Prophecy.Integration;

namespace OmenCore.Views;

public sealed class ProphecyWindow : Window
{
    private readonly ProphecyControls _controls;
    private readonly WindowsFormsHost _host;
    public ProphecyWindow(FanService fans)
    {
        Title = "OmenCore — Prophecy Power Unlocker";
        Width = 1400; Height = 1000; MinWidth = 1080; MinHeight = 840;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        _controls = new ProphecyControls(fans);
        _host = new WindowsFormsHost { Child = _controls }; Content = _host;
        Closing += (_, e) => {
            if (!_controls.CanClose) { e.Cancel = true; return; }
            _controls.StopHoldingCpu();
        };
        Closed += (_, _) => { _host.Child = null; _controls.Dispose(); _host.Dispose(); };
    }
}
