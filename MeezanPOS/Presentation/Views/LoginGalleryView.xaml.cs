using System;
using System.Windows;

namespace MeezanPOS.Presentation.Views
{
    /// <summary>
    /// Interaction logic for LoginGalleryView.xaml
    /// </summary>
    public partial class LoginGalleryView : Window
    {
        public LoginGalleryView()
        {
            InitializeComponent();
        }

        private void ApplyDesign_Click(object sender, RoutedEventArgs e)
        {
            string chosenDesign = "التصميم الأول (الملكي الداكن والزجاج المذهب)";
            
            if (BtnDesign2.IsChecked == true)
                chosenDesign = "التصميم الثاني (اللوحة المنقسمة التنفيذية)";
            else if (BtnDesign3.IsChecked == true)
                chosenDesign = "التصميم الثالث (الهندسي المعاصر البسيط)";
            else if (BtnDesign4.IsChecked == true)
                chosenDesign = "التصميم الرابع (الزجاج السينمائي الفاخر)";

            MessageBox.Show($"لقد قمت باختيار: {chosenDesign}!\nسيقوم المطور الآن بتثبيت وتطبيق هذا التصميم كواجهة دخول افتراضية دائمة للمنظومة.", 
                            "تم تحديد التصميم بنجاح ✓", 
                            MessageBoxButton.OK, 
                            MessageBoxImage.Information);
                            
            this.Close();
        }
    }
}
