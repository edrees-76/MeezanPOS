using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace MeezanPOS.Presentation.Behaviors;

/// <summary>
/// تحويل الأرقام العربية-الهندية (٠١٢٣ و ۰۱۲۳) والفاصلة العشرية العربية (٫) إلى أرقام لاتينية أثناء الكتابة
/// في كل حقول النص بالمنظومة. بدونه تفشل حقول المبالغ في قراءة الرقم بصمت ويبقى المبلغ القديم في الحساب.
/// </summary>
public static class ArabicDigitsInput
{
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;
        _registered = true;
        EventManager.RegisterClassHandler(typeof(TextBox), UIElement.PreviewTextInputEvent,
            new TextCompositionEventHandler(OnPreviewTextInput));
    }

    public static string Normalize(string text)
    {
        StringBuilder? sb = null;
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            char mapped = c switch
            {
                >= '٠' and <= '٩' => (char)('0' + (c - '٠')), // ٠-٩
                >= '۰' and <= '۹' => (char)('0' + (c - '۰')), // ۰-۹ (فارسي)
                '٫' => '.',                                            // ٫ فاصلة عشرية
                _ => c
            };
            if (mapped != c)
            {
                sb ??= new StringBuilder(text, 0, i, text.Length);
            }
            sb?.Append(mapped);
        }
        return sb?.ToString() ?? text;
    }

    private static void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (sender is not TextBox textBox || textBox.IsReadOnly || string.IsNullOrEmpty(e.Text)) return;

        var normalized = Normalize(e.Text);
        if (normalized == e.Text) return;

        e.Handled = true;
        int start = textBox.SelectionStart;
        textBox.SelectedText = normalized;
        textBox.SelectionLength = 0;
        textBox.CaretIndex = start + normalized.Length;
    }
}
