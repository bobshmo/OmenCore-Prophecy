using System.Windows.Controls;

namespace OmenCore.Views
{
    /// <summary>
    /// Tuning Utilities view for CPU undervolting, power limits, and GPU overclocking.
    /// </summary>
    public partial class TuningView : UserControl
    {
        private ProphecyWindow? _prophecy;
        private void OpenProphecy_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (_prophecy != null) { _prophecy.Activate(); return; }
            if (DataContext is not OmenCore.ViewModels.MainViewModel main || main.FanService == null) return;
            _prophecy = new ProphecyWindow(main.FanService) { Owner = System.Windows.Window.GetWindow(this) };
            _prophecy.Closed += (_, _) => _prophecy = null;
            _prophecy.Show();
        }
        public TuningView()
        {
            InitializeComponent();
        }
    }
}
