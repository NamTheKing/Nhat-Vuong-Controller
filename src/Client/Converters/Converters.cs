using System.Globalization;
using NhatVuong.Client.Localization;
using NhatVuong.Client.Services;
using NhatVuong.Contracts;
using Connectivity = NhatVuong.Contracts.Connectivity;

namespace NhatVuong.Client.Converters;

/// <summary>Enum value → localised text (NFR-07).</summary>
public sealed class EnumTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => L.EnumObject(value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>FR-C3: Online green, Fault red, Offline grey.</summary>
public sealed class ConnectivityColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        Connectivity.Online => Color.FromArgb("#1E8E3E"),
        Connectivity.Fault => Color.FromArgb("#C5221F"),
        _ => Color.FromArgb("#80868B"),
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class PowerColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PowerState.On ? Color.FromArgb("#0B6E99") : Color.FromArgb("#80868B");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>UTC instant → campus local time text (AD-8). ConverterParameter is the format.</summary>
public sealed class LocalTimeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        CampusTime.Format(value as DateTimeOffset?, parameter as string ?? "HH:mm dd/MM");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => value is not true;
}

public sealed class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && (value is not string s || s.Length > 0);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Formats a value with a localised resource template: ConverterParameter is the resource key.</summary>
public sealed class ResourceFormatConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        L.Format(parameter as string ?? string.Empty, value is DateTimeOffset d ? CampusTime.Format(d) : value);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
