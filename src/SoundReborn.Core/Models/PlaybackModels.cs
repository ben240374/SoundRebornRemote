namespace SoundReborn.Core.Models;

/// <summary>État de lecture tel que le firmware le rapporte dans &lt;playStatus&gt;.</summary>
public enum PlayStatus
{
    Unknown,
    Play,
    Pause,
    Stop,
    Buffering,
    Invalid,
}

/// <summary>
/// Un « ContentItem » : la description d'une source ou d'un contenu jouable.
/// C'est l'objet que POST /select attend, et celui que /presets et /now_playing renvoient.
/// </summary>
public sealed class ContentItem
{
    public string Source { get; init; } = "";
    public string? SourceAccount { get; init; }
    public string? Type { get; init; }
    public string? Location { get; init; }
    public bool IsPresetable { get; init; }
    public string? ItemName { get; init; }
    public string? ContainerArt { get; init; }

    public override string ToString() => string.IsNullOrEmpty(ItemName) ? Source : ItemName!;
}

/// <summary>Contenu de GET /now_playing.</summary>
public sealed class NowPlaying
{
    public string? DeviceId { get; init; }
    public string Source { get; init; } = "STANDBY";
    public string? SourceAccount { get; init; }
    public ContentItem? Content { get; init; }

    public string? Track { get; init; }
    public string? Artist { get; init; }
    public string? Album { get; init; }
    public string? StationName { get; init; }
    public string? Description { get; init; }
    public string? ArtUrl { get; init; }
    public string? ArtStatus { get; init; }

    public PlayStatus PlayStatus { get; init; } = PlayStatus.Unknown;
    public string? ShuffleSetting { get; init; }
    public string? RepeatSetting { get; init; }

    /// <summary>Position et durée en secondes, quand la source les fournit (0 sinon).</summary>
    public int PositionSeconds { get; init; }
    public int DurationSeconds { get; init; }

    public bool IsPlaying => PlayStatus == PlayStatus.Play;
    public bool IsStandby => string.Equals(Source, "STANDBY", StringComparison.OrdinalIgnoreCase);
    public bool IsIdle => IsStandby || string.Equals(Source, "INVALID_SOURCE", StringComparison.OrdinalIgnoreCase);

    /// <summary>Titre principal à afficher : piste, sinon nom de station, sinon nom du contenu.</summary>
    public string DisplayTitle =>
        FirstNonEmpty(Track, StationName, Content?.ItemName, IsStandby ? "En veille" : "Rien en lecture");

    /// <summary>Sous-titre : artiste, sinon description, sinon nom de la source.</summary>
    public string DisplaySubtitle =>
        FirstNonEmpty(Artist, Description, StationName, IsIdle ? "" : Source);

    public static NowPlaying Standby { get; } = new() { Source = "STANDBY" };

    private static string FirstNonEmpty(params string?[] values)
    {
        foreach (var v in values)
        {
            if (!string.IsNullOrWhiteSpace(v))
            {
                return v!;
            }
        }

        return "";
    }
}

/// <summary>Contenu de GET /volume.</summary>
public sealed record VolumeState(int Target, int Actual, bool IsMuted)
{
    public static VolumeState Empty { get; } = new(0, 0, false);
}

/// <summary>Contenu de GET /bass.</summary>
public sealed record BassState(int Target, int Actual)
{
    public static BassState Empty { get; } = new(0, 0);
}

/// <summary>
/// Contenu de GET /bassCapabilities. Attention : l'API Bose n'expose PAS de réglage
/// d'aigus — seuls les graves sont réglables, sur une plage typiquement -9..0.
/// </summary>
public sealed record BassCapabilities(bool Available, int Min, int Max, int Default)
{
    /// <summary>Repli prudent quand l'enceinte ne répond pas à /bassCapabilities.</summary>
    public static BassCapabilities Unknown { get; } = new(false, -9, 0, 0);
}

/// <summary>Une présélection matérielle (touches 1 à 6 de l'enceinte).</summary>
public sealed class PresetInfo
{
    public int Id { get; init; }
    public ContentItem? Content { get; init; }

    public string Name => Content?.ItemName ?? $"Présélection {Id}";
    public string? ArtUrl => Content?.ContainerArt;
    public string Source => Content?.Source ?? "";
    public bool IsEmpty => Content is null;
}

/// <summary>Une entrée de GET /sources.</summary>
public sealed class SourceItem
{
    public string Source { get; init; } = "";
    public string? SourceAccount { get; init; }
    public string? Status { get; init; }
    public bool IsLocal { get; init; }
    public bool MultiroomAllowed { get; init; }
    public string? ItemName { get; init; }

    public bool IsReady => string.Equals(Status, "READY", StringComparison.OrdinalIgnoreCase);
    public string DisplayName => string.IsNullOrWhiteSpace(ItemName) ? Source : ItemName!;

    public ContentItem ToContentItem() => new()
    {
        Source = Source,
        SourceAccount = SourceAccount,
        ItemName = ItemName,
    };
}
