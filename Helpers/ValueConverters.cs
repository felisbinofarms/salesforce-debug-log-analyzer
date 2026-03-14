using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace SalesforceDebugAnalyzer.Helpers;

/// <summary>
/// Returns true when the bound int equals the ConverterParameter int.
/// Usage: IsVisible="{Binding SelectedTabIndex, Converter={StaticResource EqualsInt}, ConverterParameter=0}"
/// </summary>
public class EqualsIntConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter is string strParam && int.TryParse(strParam, out var target))
        {
            return intValue == target;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Returns true when int > 0.
/// </summary>
public class PositiveIntToBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0;
        }

        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
