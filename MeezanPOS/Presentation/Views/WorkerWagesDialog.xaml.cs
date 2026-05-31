using System.Windows;
using System.Windows.Input;

namespace MeezanPOS.Presentation.Views
{
    public partial class WorkerWagesDialog : Window
    {
        public WorkerWagesDialog()
        {
            InitializeComponent();
        }

        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
    }
}
