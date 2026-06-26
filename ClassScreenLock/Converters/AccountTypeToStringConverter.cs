using System;
using System.Globalization;
using Avalonia;
using Avalonia.Data.Converters;
using ClassScreenLock.Models;
using ClassScreenLock.Services;

namespace ClassScreenLock.Converters;

public class AccountTypeToStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AccountType type)
        {
            return type switch
            {
                AccountType.SuperAdmin => LocalizationService.Instance.GetString("Account_Type_SuperAdmin"),
                AccountType.Admin => LocalizationService.Instance.GetString("Account_Type_Admin"),
                AccountType.User => LocalizationService.Instance.GetString("Account_Type_User"),
                _ => type.ToString()
            };
        }
        return string.Empty;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return AvaloniaProperty.UnsetValue;
    }
}
