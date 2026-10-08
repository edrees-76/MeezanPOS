using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeezanPOS.Presentation.Views
{
    /// <summary>
    /// Interaction logic for DeleteReasonDialog.xaml
    /// </summary>
    public partial class DeleteReasonDialog : Window
    {
        public string SelectedReason { get; private set; } = string.Empty;
        public string SelectedDetailReason { get; private set; } = string.Empty;

        public DeleteReasonDialog()
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
            TxtCharCount.Text = $"{length} / 5";

            if (length >= 5)
            {
                BtnConfirm.IsEnabled = true;
                TxtCharCount.Foreground = System.Windows.Media.Brushes.Green;
            }
            else
            {
                BtnConfirm.IsEnabled = false;
                TxtCharCount.Foreground = (System.Windows.Media.Brush)FindResource("MaterialDesignBodyLight");
            }
        }

        private void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var reasonItem = CbReason.SelectedItem as ComboBoxItem;
            SelectedReason = reasonItem?.Content?.ToString() ?? "سبب آخر";
            SelectedDetailReason = TxtDetailReason.Text.Trim();

            if (SelectedDetailReason.Length < 5)
            {
                Dialogs.Show("الشرح التفصيلي يجب أن لا يقل عن 5 حروف لتأكيد عملية الحذف.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
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
