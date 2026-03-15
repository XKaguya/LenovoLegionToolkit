using System;
using System.Globalization;
using System.Windows.Data;
using LenovoLegionToolkit.Lib.Extensions;

namespace LenovoLegionToolkit.WPF.Converters;

public class EnumDisplayNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Enum enumValue)
        {
            return enumValue.GetDisplayName();
        }
        return value?.ToString() ?? string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}