using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SalesforceDebugAnalyzer.Helpers;

/// <summary>
/// Converts boolean to inverse boolean
/// </summary>
public class InverseBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return false;
    }
}

/// <summary>
/// Converts boolean to bool for IsVisible (with optional inversion via parameter "Inverse")
/// </summary>
public class BooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
        {
            bool invert = parameter is string str && str.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            if (invert) boolValue = !boolValue;
            return boolValue;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool b)
        {
            bool invert = parameter is string str && str.Equals("Inverse", StringComparison.OrdinalIgnoreCase);
            return invert ? !b : b;
        }
        return false;
    }
}

/// <summary>
/// Converts an integer to bool based on whether it equals the parameter
/// </summary>
public class IntEqualsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue && parameter != null)
        {
            if (int.TryParse(parameter.ToString(), out int targetValue))
                return intValue == targetValue;
        }
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a string to bool (true if not null/empty)
/// </summary>
public class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrEmpty(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a percentage (0-100) to a width for progress bars
/// </summary>
public class PercentToWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            percent = Math.Max(0, Math.Min(100, percent));
            return percent * 3.0;
        }
        return 0.0;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a percentage to a severity level string for styling
/// </summary>
public class PercentToSeverityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is double percent)
        {
            if (percent >= 80) return "Critical";
            if (percent >= 50) return "Warning";
            return "Normal";
        }
        return "Normal";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a number > 0 to True
/// </summary>
public class GreaterThanZeroConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue) return intValue > 0;
        if (value is double doubleValue) return doubleValue > 0;
        if (value is long longValue) return longValue > 0;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a count > 0 to true (visible), 0 to false (hidden)
/// </summary>
public class GreaterThanZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue) return intValue > 0;
        if (value is double doubleValue) return doubleValue > 0;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts boolean to inverse visibility (True=hidden, False=visible)
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool boolValue)
            return !boolValue;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a percentage (0-100) to an arc PathGeometry for circular progress.
/// </summary>
public class ArcPathConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double percent;
        if (value is double d)
            percent = d;
        else if (value is int i)
            percent = i;
        else
            return null;
        percent = Math.Max(0, Math.Min(100, percent));
        if (percent <= 0) return null;

        const double radius = 56;
        const double center = 60;
        double startAngle = -90;
        double sweepAngle = Math.Min(percent / 100.0 * 360.0, 359.99);
        double endAngle = startAngle + sweepAngle;

        var start = PointOnCircle(center, center, radius, startAngle);
        var end = PointOnCircle(center, center, radius, endAngle);
        bool isLargeArc = sweepAngle > 180.0;

        var figure = new PathFigure
        {
            StartPoint = start,
            IsClosed = false,
            Segments = new PathSegments
            {
                new ArcSegment
                {
                    Point = end,
                    Size = new Avalonia.Size(radius, radius),
                    IsLargeArc = isLargeArc,
                    SweepDirection = SweepDirection.Clockwise
                }
            }
        };

        var geometry = new PathGeometry();
        geometry.Figures ??= new PathFigures();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();

    private static Avalonia.Point PointOnCircle(double cx, double cy, double radius, double angleDegrees)
    {
        double radians = angleDegrees * Math.PI / 180.0;
        return new Avalonia.Point(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));
    }
}

/// <summary>
/// Converts null to false (hidden), non-null to true (visible)
/// </summary>
public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Inverse of NullToVisibilityConverter
/// </summary>
public class InverseNullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value == null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts 0 to true (visible), non-zero to false. For empty state overlays.
/// </summary>
public class ZeroToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue) return intValue == 0;
        if (value is double doubleValue) return doubleValue == 0;
        return true;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts a health score (0-100) to a color brush.
/// </summary>
public class HealthScoreToColorConverter : IValueConverter
{
    private static readonly ISolidColorBrush GreenBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#76BA70"));
    private static readonly ISolidColorBrush AmberBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#C09040"));
    private static readonly ISolidColorBrush OrangeBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#B87040"));
    private static readonly ISolidColorBrush CoralBrush = new SolidColorBrush(Avalonia.Media.Color.Parse("#CC6055"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        int score = value switch
        {
            int i => i,
            double d => (int)d,
            _ => 100
        };

        return score switch
        {
            >= 80 => GreenBrush,
            >= 60 => AmberBrush,
            >= 40 => OrangeBrush,
            _ => CoralBrush
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Generates a consistent color for a username
/// </summary>
public class UserToColorConverter : IValueConverter
{
    private static readonly string[] UserColors =
    [
        "#5865F2", "#57F287", "#FEE75C", "#FAA61A", "#EB459E", "#22D3EE",
        "#8B5CF6", "#F97316", "#10B981", "#EC4899", "#06B6D4", "#8B5A2B"
    ];

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string username && !string.IsNullOrEmpty(username))
        {
            int hash = 0;
            foreach (char c in username)
                hash = ((hash << 5) - hash) + c;
            hash &= 0x7FFFFFFF;
            int index = Math.Abs(hash) % UserColors.Length;
            return new SolidColorBrush(Avalonia.Media.Color.Parse(UserColors[index]));
        }
        return new SolidColorBrush(Avalonia.Media.Color.Parse("#72767D"));
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Returns true when a double value exceeds the threshold supplied as ConverterParameter.
/// </summary>
public class DoubleGreaterThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        double threshold = 0;
        if (parameter is string paramStr)
            double.TryParse(paramStr, NumberStyles.Any, CultureInfo.InvariantCulture, out threshold);

        if (value is double d) return d > threshold;
        if (value is float f) return f > threshold;
        if (value is int i) return i > threshold;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Converts null to false, non-null to true
/// </summary>
public class NullToBooleanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value != null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}

/// <summary>
/// Combines multiple boolean values with AND logic, returns bool for IsVisible
/// </summary>
public class MultiBooleanToVisibilityConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values == null || values.Count == 0)
            return false;

        foreach (var value in values)
        {
            if (value is bool boolValue)
            {
                if (!boolValue) return false;
            }
            else
            {
                return false;
            }
        }
        return true;
    }
}

/// <summary>
/// Converts an issue severity string to a SolidColorBrush
/// </summary>
public class SeverityToColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return (value?.ToString() ?? "") switch
        {
            "Critical" => new SolidColorBrush(Avalonia.Media.Color.Parse("#F85149")),
            "High" => new SolidColorBrush(Avalonia.Media.Color.Parse("#D29922")),
            "Medium" => new SolidColorBrush(Avalonia.Media.Color.Parse("#58A6FF")),
            "Low" => new SolidColorBrush(Avalonia.Media.Color.Parse("#3FB950")),
            _ => new SolidColorBrush(Avalonia.Media.Color.Parse("#30363D"))
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}