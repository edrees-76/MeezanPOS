using System;
using System.Windows;
using MeezanPOS.Application.Interfaces;
using MeezanPOS.Application.Services;
using MeezanPOS.Domain.Entities;

namespace MeezanPOS.Presentation.Views
{
    public partial class WorkerEditDialog : Window
    {
        private readonly IWagesService _wagesService;
        private readonly Worker? _worker;

        public WorkerEditDialog(Worker? worker = null)
        {
            InitializeComponent();
            _wagesService = AppServiceProvider.Resolve<IWagesService>();
            _worker = worker;

            if (_worker != null)
            {
                TxtTitle.Text = "تعديل بيانات العامل";
                TxtName.Text = _worker.WorkerName;
                TxtDailyWage.Text = _worker.DailyWage.ToString("F2");
                ChkActive.IsChecked = _worker.IsActive;
                TxtNotes.Text = _worker.Notes;
            }
            else
            {
                TxtTitle.Text = "إضافة عامل جديد";
                ChkActive.IsChecked = true;
            }
        }

        private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                DragMove();
            }
        }

        private async void Save_Click(object sender, RoutedEventArgs e)
        {
            string name = TxtName.Text.Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("يرجى إدخال اسم العامل بالكامل.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!decimal.TryParse(TxtDailyWage.Text, out decimal dailyWage) || dailyWage <= 0)
            {
                MessageBox.Show("يرجى تحديد أجر يومي صحيح أكبر من الصفر.", "تنبيه", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // منع الضغط المزدوج على زر الحفظ (كان يُنشئ العامل مرتين)
            if (sender is UIElement saveButton) saveButton.IsEnabled = false;
            try
            {
                Worker worker = _worker ?? new Worker();
                worker.WorkerName = name;
                worker.DailyWage = dailyWage;
                worker.IsActive = ChkActive.IsChecked == true;
                worker.Notes = TxtNotes.Text.Trim();

                await _wagesService.SaveWorkerAsync(worker);

                DialogResult = true;
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"خطأ أثناء حفظ ملف العامل: {ex.Message}", "خطأ", MessageBoxButton.OK, MessageBoxImage.Error);
                if (sender is UIElement saveButton2) saveButton2.IsEnabled = true;
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
