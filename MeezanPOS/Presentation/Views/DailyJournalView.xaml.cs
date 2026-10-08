using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MeezanPOS.Application.ViewModels;

namespace MeezanPOS.Presentation.Views;

public partial class DailyJournalView : UserControl
{
    public DailyJournalView()
    {
        InitializeComponent();
        this.PreviewKeyDown += DailyJournalView_PreviewKeyDown;
        this.KeyDown += DailyJournalView_KeyDown;
    }

    /// <summary>
    /// اختصارات الحفظ والطباعة (F9 / Ctrl+P) تُنفَّذ والمؤشر ما زال داخل الحقل،
    /// وحقول المبالغ مربوطة بـ LostFocus، لذا ندفع القيمة المكتوبة للـ ViewModel أولاً.
    /// </summary>
    private void DailyJournalView_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool isShortcut = e.Key == Key.F9 || (e.Key == Key.P && Keyboard.Modifiers == ModifierKeys.Control);
        if (isShortcut && Keyboard.FocusedElement is TextBox focused)
        {
            focused.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        }
    }

    private void DailyJournalView_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (DataContext is DailyJournalViewModel vm && vm.ClearFormCommand.CanExecute(null))
            {
                e.Handled = true;
                var answer = MessageBox.Show(
                    "سيتم مسح جميع البيانات المدخلة في اليومية الحالية دون حفظ. هل تريد المتابعة؟",
                    "تأكيد المسح",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No,
                    MessageBoxOptions.RtlReading | MessageBoxOptions.RightAlign);
                if (answer == MessageBoxResult.Yes)
                {
                    vm.ClearFormCommand.Execute(null);
                }
            }
        }
    }
}
