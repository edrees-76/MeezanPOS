using System;
using System.Globalization;
using System.Windows.Data;
using MeezanPOS.Domain.Enums;

namespace MeezanPOS.Presentation.Converters;

public class TransactionTypeToNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is BankTransactionType type)
        {
            return type switch
            {
                BankTransactionType.CardSalesDeposit => "خدمات مصرفية",
                BankTransactionType.Deposit => "إيداع نقدي",
                BankTransactionType.Withdrawal => "سحب نقدي",
                BankTransactionType.InternalTransfer => "تحويل داخلي",
                BankTransactionType.SupplierPayment => "سداد مورد",
                BankTransactionType.ExpensePayment => "مصروف عام",
                BankTransactionType.OwnerDebtSettlement => "تسوية مالك",
                BankTransactionType.ExchangeDifference => "فروقات",
                _ => "أخرى"
            };
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
