using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Extensions.DependencyInjection;

namespace MeezanPOS.Presentation.Views
{
    /// <summary>
    /// Interaction logic for PeriodUnlockDialog.xaml
    /// </summary>
    public partial class PeriodUnlockDialog : Window
    {
        public string SelectedReason { get; private set; } = string.Empty;
        public string SelectedDetailReason { get; private set; } = string.Empty;
        public string ApproverUsername { get; private set; } = string.Empty;

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

        private async void Confirm_Click(object sender, RoutedEventArgs e)
        {
            var reasonItem = CbReason.SelectedItem as ComboBoxItem;
            SelectedReason = reasonItem?.Content?.ToString() ?? "سبب آخر";
            SelectedDetailReason = TxtDetailReason.Text.Trim();

            if (SelectedDetailReason.Length < 20)
            {
                Dialogs.Show("الشرح التفصيلي يجب أن لا يقل عن 20 حرفاً لتأكيد العملية.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnConfirm.IsEnabled = false;
            try
            {
                using var scope = MeezanPOS.Application.Services.AppServiceProvider.Provider.CreateScope();
                var approval = scope.ServiceProvider.GetRequiredService<MeezanPOS.Application.Services.PeriodUnlockApproval>();
                var session = scope.ServiceProvider.GetRequiredService<MeezanPOS.Application.Interfaces.ISessionService>();
                var result = await approval.VerifyAsync(TxtApproverUser.Text, TxtApproverPassword.Password, session.CurrentUser?.Id ?? 0);
                if (!result.IsApproved)
                {
                    Dialogs.Show(result.Error!, "اعتماد فك القفل", MessageBoxButton.OK, MessageBoxImage.Warning);
                    TxtApproverPassword.Clear();
                    return;
                }

                ApproverUsername = result.Approver!.Username;
                // يُحفظ المعتمِد ضمن سبب فك القفل في سجل التدقيق
                SelectedDetailReason = $"{SelectedDetailReason} | اعتمده: {ApproverUsername}";
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "خطأ أثناء التحقق من اعتماد فك القفل");
                Dialogs.Show("تعذر التحقق من بيانات المعتمِد.", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            finally
            {
                BtnConfirm.IsEnabled = SelectedDetailReason.Length >= 20;
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
