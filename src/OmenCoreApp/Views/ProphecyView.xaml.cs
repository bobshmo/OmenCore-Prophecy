using System.Windows;
using System.Windows.Controls;
using OmenCore.ViewModels;

namespace OmenCore.Views;

public partial class ProphecyView : UserControl
{
    public ProphecyView(){InitializeComponent();Loaded+=OnLoaded;Unloaded+=OnUnloaded;}
    private async void OnLoaded(object sender,RoutedEventArgs e){if(DataContext is ProphecyViewModel viewModel)await viewModel.ActivateAsync();}
    private void OnUnloaded(object sender,RoutedEventArgs e){if(DataContext is ProphecyViewModel viewModel)viewModel.Deactivate();}
}
