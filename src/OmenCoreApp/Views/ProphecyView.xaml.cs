using System.Windows;
using System.Windows.Controls;
using OmenCore.ViewModels;

namespace OmenCore.Views;

public partial class ProphecyView : UserControl
{
    public static readonly DependencyProperty CpuContentProperty = DependencyProperty.Register(nameof(CpuContent),typeof(object),typeof(ProphecyView));
    public static readonly DependencyProperty GpuRecoveryContentProperty = DependencyProperty.Register(nameof(GpuRecoveryContent),typeof(object),typeof(ProphecyView));
    public static readonly DependencyProperty DiagnosticsContentProperty = DependencyProperty.Register(nameof(DiagnosticsContent),typeof(object),typeof(ProphecyView));
    public object? CpuContent {get=>GetValue(CpuContentProperty);set=>SetValue(CpuContentProperty,value);}
    public object? GpuRecoveryContent {get=>GetValue(GpuRecoveryContentProperty);set=>SetValue(GpuRecoveryContentProperty,value);}
    public object? DiagnosticsContent {get=>GetValue(DiagnosticsContentProperty);set=>SetValue(DiagnosticsContentProperty,value);}
    public ProphecyView(){InitializeComponent();Loaded+=OnLoaded;Unloaded+=OnUnloaded;}
    private async void OnLoaded(object sender,RoutedEventArgs e){if(DataContext is ProphecyViewModel viewModel)await viewModel.ActivateAsync();}
    private void OnUnloaded(object sender,RoutedEventArgs e){if(DataContext is ProphecyViewModel viewModel)viewModel.Deactivate();}
}
