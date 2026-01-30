using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace SalesforceDebugAnalyzer.Helpers;

/// <summary>
/// Converts boolean to inverse boolean
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return true;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return !boolValue;
        }
        return false;
    }
}

/// <summary>
/// Converts boolean to Visibility (with optional inversion)
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            // Check if we should invert
            bool invert = parameter is string str && str.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            if (invert) boolValue = !boolValue;
            
            return boolValue ? Visibility.Visible : Visibility.Collapsed;
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is Visibility visibility)
        {
            bool result = visibility == Visibility.Visible;
            
            // Check if we should invert
            bool invert = parameter is string str && str.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            if (invert) result = !result;
            
            return result;
        }
        return false;
    }
}
