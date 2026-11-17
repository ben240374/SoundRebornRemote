namespace SoundReborn.Core;

/// <summary>
/// La version minimale de l'agent STR que l'application accepte pour modifier
/// les groupes.
///
/// Avant la v1.4.0, un groupe formé sans « permanent » par-dessus un groupe
/// permanent écrasait ce dernier au lieu de le mettre de côté ; plusieurs règles
/// de l'application reposent sur ce comportement. La v1.6.0 est la version pour
/// laquelle l'application est écrite et vérifiée : c'est le seuil retenu.
/// </summary>
public static class AgentRequirement
{
    /// <summary>1.6.0.</summary>
    public static Version Minimum { get; } = new(1, 6, 0);

    /// <summary>« 1.6.0 », pour les messages.</summary>
    public static string MinimumLabel => $"{Minimum.Major}.{Minimum.Minor}.{Minimum.Build}";

    /// <summary>
    /// Lit « v1.6.3 », « 1.6.3 », « 1.6.3-beta » ou « 1.6 ». Null quand le texte
    /// ne commence pas par au moins deux nombres séparés par un point : une version
    /// illisible n'est pas une version trop ancienne.
    /// </summary>
    public static Version? Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        var value = text.Trim();

        if (value.StartsWith('v') || value.StartsWith('V'))
        {
            value = value[1..];
        }

        // On ne garde que la tête « chiffres et points » : un suffixe de
        // préversion ou de compilation ne change pas la version de l'agent.
        var length = 0;

        while (length < value.Length && (char.IsAsciiDigit(value[length]) || value[length] == '.'))
        {
            length++;
        }

        var parts = value[..length].Split('.', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length < 2)
        {
            return null;
        }

        var numbers = new int[3];

        for (var i = 0; i < 3 && i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out numbers[i]))
            {
                return null;
            }
        }

        return new Version(numbers[0], numbers[1], numbers[2]);
    }

    /// <summary>
    /// Vrai seulement pour une version LUE et inférieure au minimum. Une version
    /// inconnue ou illisible ne bloque rien.
    /// </summary>
    public static bool IsTooOld(string? version) => Parse(version) is { } read && read < Minimum;
}
