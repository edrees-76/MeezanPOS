using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Controls;

namespace MeezanPOS.Presentation.Behaviors
{
    public static class EnterKeyBehavior
    {
        // --- MoveFocusOnEnter ---
        public static readonly DependencyProperty MoveFocusOnEnterProperty =
            DependencyProperty.RegisterAttached(
                "MoveFocusOnEnter",
                typeof(bool),
                typeof(EnterKeyBehavior),
                new PropertyMetadata(false, OnMoveFocusOnEnterChanged));

        public static bool GetMoveFocusOnEnter(DependencyObject obj)
        {
            return (bool)obj.GetValue(MoveFocusOnEnterProperty);
        }

        public static void SetMoveFocusOnEnter(DependencyObject obj, bool value)
        {
            obj.SetValue(MoveFocusOnEnterProperty, value);
        }

        private static void OnMoveFocusOnEnterChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if ((bool)e.NewValue)
                {
                    element.KeyDown += Element_MoveFocus_KeyDown;
                }
                else
                {
                    element.KeyDown -= Element_MoveFocus_KeyDown;
                }
            }
        }

        private static void Element_MoveFocus_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.AcceptsReturn)
                {
                    return;
                }

                e.Handled = true;
                var element = sender as UIElement;
                element?.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }
        }

        // --- EnterCommand ---
        public static readonly DependencyProperty EnterCommandProperty =
            DependencyProperty.RegisterAttached(
                "EnterCommand",
                typeof(ICommand),
                typeof(EnterKeyBehavior),
                new PropertyMetadata(null, OnEnterCommandChanged));

        public static ICommand GetEnterCommand(DependencyObject obj)
        {
            return (ICommand)obj.GetValue(EnterCommandProperty);
        }

        public static void SetEnterCommand(DependencyObject obj, ICommand value)
        {
            obj.SetValue(EnterCommandProperty, value);
        }

        // --- EnterCommandParameter ---
        public static readonly DependencyProperty EnterCommandParameterProperty =
            DependencyProperty.RegisterAttached(
                "EnterCommandParameter",
                typeof(object),
                typeof(EnterKeyBehavior),
                new PropertyMetadata(null));

        public static object GetEnterCommandParameter(DependencyObject obj)
        {
            return obj.GetValue(EnterCommandParameterProperty);
        }

        public static void SetEnterCommandParameter(DependencyObject obj, object value)
        {
            obj.SetValue(EnterCommandParameterProperty, value);
        }

        private static void OnEnterCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is UIElement element)
            {
                if (e.NewValue != null)
                {
                    element.KeyDown += Element_Command_KeyDown;
                }
                else
                {
                    element.KeyDown -= Element_Command_KeyDown;
                }
            }
        }

        private static void Element_Command_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                if (sender is TextBox textBox && textBox.AcceptsReturn)
                {
                    return;
                }

                var element = sender as DependencyObject;
                if (element != null)
                {
                    var command = GetEnterCommand(element);
                    if (command != null)
                    {
                        var parameter = GetEnterCommandParameter(element);
                        if (command.CanExecute(parameter))
                        {
                            e.Handled = true;
                            // الربط قد يكون LostFocus: ادفع القيمة المكتوبة للـ ViewModel قبل التنفيذ
                            // وإلا يُحفظ الرقم السابق بدلاً من الرقم الذي كتبه المستخدم للتو.
                            (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
                            command.Execute(parameter);
                        }
                    }
                }
            }
        }
    }
}
