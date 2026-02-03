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

/// <summary>
/// Converts an integer to Visibility based on whether it equals the parameter
/// Usage: Visibility="{Binding SelectedTabIndex, Converter={StaticResource IntEqualsToVisibilityConverter}, ConverterParameter=0}"
/// </summary>
public class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int targetValue))
            {
                return intValue == targetValue ? Visibility.Visible : Visibility.Collapsed;
            }
        }
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a string to Visibility (Visible if not null/empty, Collapsed otherwise)
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a percentage (0-100) to a width for progress bars
/// Assumes max width of ~300px (scales based on parent)
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            // Clamp to 0-100 and convert to a proportion
            percent = Math.Max(0, Math.Min(100, percent));
            // Return a GridLength or actual width - we'll use a proportion of available space
            // For a container ~400px wide, this gives us reasonable bar widths
            return percent * 3.0; // Max ~300px at 100%
        }
        return 0.0;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a percentage to a severity level string for styling
/// </summary>
public class PercentToSeverityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            if (percent >= 80) return "Critical";
            if (percent >= 50) return "Warning";
            return "Normal";
        }
        return "Normal";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a number > 0 to True, otherwise False
/// </summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue) return intValue > 0;
        if (value is double doubleValue) return doubleValue > 0;
        if (value is long longValue) return longValue > 0;
        return false;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts a count > 0 to Visibility.Visible, otherwise Collapsed
/// </summary>
public class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue) return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (value is double doubleValue) return doubleValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

/// <summary>
/// Converts boolean to inverse visibility (True=Collapsed, False=Visible)
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            return boolValue ? Visibility.Collapsed : Visibility.Visible;
        }
        return Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
