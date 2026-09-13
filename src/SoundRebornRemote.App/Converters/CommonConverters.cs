using System.Globalization;

namespace SoundRebornRemote.App.Converters;

/// <summary>Inverse un booléen — pour afficher un élément quand une condition est fausse.</summary>
public sealed class InvertedBoolConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : true;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is bool b ? !b : false;
}

/// <summary>Vrai quand la chaîne n'est ni nulle ni vide — pour masquer une ligne sans contenu.</summary>
public sealed class StringNotEmptyConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => !string.IsNullOrWhiteSpace(value as string);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Colore un message d'état : rouge en cas d'erreur, gris sinon.
/// La valeur liée est le booléen IsError.
/// </summary>
public sealed class ErrorColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Color.FromArgb("#E05B5B") : Color.FromArgb("#9AA7B4");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>Grise une ligne dont la source n'est pas prête (compte déconnecté, Bluetooth non appairé).</summary>
public sealed class ReadyOpacityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? 1.0 : 0.45;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
