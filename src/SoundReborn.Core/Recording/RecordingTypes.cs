namespace SoundReborn.Core.Recording;

/// <summary>Les limites de l'enregistrement d'une radio, fixées avec Benoît.</summary>
public static class RecordingRules
{
    /// <summary>Durée maximale d'un enregistrement.</summary>
    public static TimeSpan MaxDuration { get; } = TimeSpan.FromMinutes(5);

    /// <summary>En dessous, un enregistrement interrompu n'est pas gardé.</summary>
    public static TimeSpan MinimumKept { get; } = TimeSpan.FromSeconds(5);

    /// <summary>Sans un octet reçu pendant ce temps, le flux est considéré comme coupé.</summary>
    public static TimeSpan Stall { get; } = TimeSpan.FromSeconds(15);

    /// <summary>Les verrous Android sont relâchés au plus tard après ce délai, quoi qu'il arrive.</summary>
    public static TimeSpan LockLimit { get; } = TimeSpan.FromMinutes(6);

    /// <summary>
    /// Taille maximale d'un fichier. Cinq minutes à 320 kbit/s font 12 Mo ; au-delà
    /// de 32 Mo, le serveur envoie anormalement vite et remplirait le téléphone.
    /// </summary>
    public const long MaxBytes = 32L * 1024 * 1024;

    /// <summary>Nombre maximal d'enregistrements gardés. Au-delà, on refuse : rien n'est supprimé d'office.</summary>
    public const int MaxCount = 10;
}

/// <summary>Raisons pour lesquelles un enregistrement ne peut pas se faire.</summary>
public static class RecordingRefusal
{
    public const string NotPlaying = "not-playing";
    public const string NotRadio = "not-radio";
    public const string NoUrl = "no-url";
    public const string Hls = "hls";
    public const string NotAudio = "not-audio";
    public const string Full = "full";
    public const string Busy = "busy";
    public const string Network = "network";
}

/// <summary>
/// Ce qu'on peut enregistrer de ce que joue l'enceinte : une adresse de flux, ou
/// la raison pour laquelle il n'y en a pas. <see cref="Source"/> porte la source
/// de l'enceinte (« SPOTIFY »…) pour le message.
/// </summary>
public sealed record StreamResolution(string? Url, string Station, string? Refusal, string? Source)
{
    public bool CanRecord => Url is not null && Refusal is null;
}

/// <summary>Un enregistrement impossible, avec l'un des codes de <see cref="RecordingRefusal"/>.</summary>
public sealed class RecordingException : Exception
{
    public RecordingException(string code, Exception? inner = null)
        : base(code, inner)
    {
        Code = code;
    }

    public string Code { get; }
}
