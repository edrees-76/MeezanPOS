using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeezanPOS.Presentation.Views
{
    /// <summary>
    /// Interaction logic for PeriodUnlockDialog.xaml
    /// </summary>
    public partial class PeriodUnlockDialog : Window
    {
        public string SelectedReason { get; private set; } = string.Empty;
        public string SelectedDetailReason { get; private set; } = string.Empty;

        public PeriodUnlockDialog()
        {
            InitializeComponent();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    this.DragMove();
                }
                catch { }
            }
        }

        private void TxtDetailReason_TextChanged(object sender, TextChangedEventArgs e)
        {
            var text = TxtDetailReason.Text ?? string.Empty;
            int length = text.Trim().Length;
            TxtCharCount.Text = $"{length} / 20";

            if (length >= 20)
            {
                BtnConfirm.IsEnabled = true;
                TxtCharCount.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                BtnConfirm.IsEnabled = false;
                TxtCharCount.Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesignBodyLight"); // or just some gray/red brush
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var reasonItem = CbReason.SelectedItem as ComboBoxItem;
            SelectedReason = reasonItem?.Content?.ToString() ?? "سبب آخر";
            SelectedDetailReason = TxtDetailReason.Text.Trim();

            if (SelectedDetailReason.Length < 20)
            {
                Dialogs.Show("الشرح التفصيلي يجب أن لا يقل عن 20 حرفاً لتأكيد العملية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
