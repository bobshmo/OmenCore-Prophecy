using System.Windows.Controls;

namespace OmenCore.Views
{
    /// <summary>
    /// Tuning Utilities view for CPU undervolting, power limits, and GPU overclocking.
    /// </summary>
    public partial class TuningView : UserControl
    {
        public TuningView()
        {
            InitializeComponent();
            DataContextChanged += (_, _) => SyncLegacyContexts();
            Loaded += (_, _) => SyncLegacyContexts();
        }
        private void SyncLegacyContexts()
        {
            foreach (var content in new[] { UnifiedTuning.CpuContent, UnifiedTuning.GpuRecoveryContent, UnifiedTuning.DiagnosticsContent })
                if (content is System.Windows.FrameworkElement element) element.DataContext = DataContext;
        }
    }
}
