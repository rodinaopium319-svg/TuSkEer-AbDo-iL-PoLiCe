using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using TuSkEer.AbDoILPoLiCe.Models;

namespace TuSkEer.AbDoILPoLiCe.Converters;

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool flag = value is bool b && b;
        if (Invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is Visibility v && v == Visibility.Visible;
}

public sealed class NullToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        bool hasValue = value is not null;
        if (Invert) hasValue = !hasValue;
        return hasValue ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class EngineStateToBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush ReadyBrush = new((Color)ColorConverter.ConvertFromString("#22C55E"));
    private static readonly SolidColorBrush TypingBrush = new((Color)ColorConverter.ConvertFromString("#00D4FF"));
    private static readonly SolidColorBrush PausedBrush = new((Color)ColorConverter.ConvertFromString("#A855F7"));
    private static readonly SolidColorBrush SwitchingBrush = new((Color)ColorConverter.ConvertFromString("#F59E0B"));
    private static readonly SolidColorBrush StoppedBrush = new((Color)ColorConverter.ConvertFromString("#EF4444"));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return value is EngineRunState state ? state switch
        {
            EngineRunState.Typing => TypingBrush,
            EngineRunState.Pausing or EngineRunState.Paused => PausedBrush,
            EngineRunState.Switching => SwitchingBrush,
            EngineRunState.Stopped => StoppedBrush,
            _ => ReadyBrush
        } : ReadyBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

public sealed class TabEqualityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is not null && parameter is not null &&
           string.Equals(value.ToString(), parameter.ToString(), StringComparison.OrdinalIgnoreCase);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
