namespace SoundReborn.Core;

/// <summary>
/// Touches acceptées par POST /key sur l'API Bose (port 8090).
/// Le nom envoyé dans le XML est celui de la constante, en majuscules.
/// </summary>
public enum RemoteKey
{
    Play,
    Pause,
    PlayPause,
    Stop,
    PrevTrack,
    NextTrack,
    ThumbsUp,
    ThumbsDown,
    Bookmark,
    AddFavorite,
    RemoveFavorite,
    Power,
    Mute,
    VolumeUp,
    VolumeDown,
    Preset1,
    Preset2,
    Preset3,
    Preset4,
    Preset5,
    Preset6,
    AuxInput,
    ShuffleOn,
    ShuffleOff,
    RepeatOff,
    RepeatOne,
    RepeatAll,
}

public static class RemoteKeyExtensions
{
    /// <summary>Convertit l'énumération vers le littéral attendu par le firmware (PREV_TRACK, PRESET_1, ...).</summary>
    public static string ToWireName(this RemoteKey key) => key switch
    {
        RemoteKey.Play => "PLAY",
        RemoteKey.Pause => "PAUSE",
        RemoteKey.PlayPause => "PLAY_PAUSE",
        RemoteKey.Stop => "STOP",
        RemoteKey.PrevTrack => "PREV_TRACK",
        RemoteKey.NextTrack => "NEXT_TRACK",
        RemoteKey.ThumbsUp => "THUMBS_UP",
        RemoteKey.ThumbsDown => "THUMBS_DOWN",
        RemoteKey.Bookmark => "BOOKMARK",
        RemoteKey.AddFavorite => "ADD_FAVORITE",
        RemoteKey.RemoveFavorite => "REMOVE_FAVORITE",
        RemoteKey.Power => "POWER",
        RemoteKey.Mute => "MUTE",
        RemoteKey.VolumeUp => "VOLUME_UP",
        RemoteKey.VolumeDown => "VOLUME_DOWN",
        RemoteKey.Preset1 => "PRESET_1",
        RemoteKey.Preset2 => "PRESET_2",
        RemoteKey.Preset3 => "PRESET_3",
        RemoteKey.Preset4 => "PRESET_4",
        RemoteKey.Preset5 => "PRESET_5",
        RemoteKey.Preset6 => "PRESET_6",
        RemoteKey.AuxInput => "AUX_INPUT",
        RemoteKey.ShuffleOn => "SHUFFLE_ON",
        RemoteKey.ShuffleOff => "SHUFFLE_OFF",
        RemoteKey.RepeatOff => "REPEAT_OFF",
        RemoteKey.RepeatOne => "REPEAT_ONE",
        RemoteKey.RepeatAll => "REPEAT_ALL",
        _ => throw new ArgumentOutOfRangeException(nameof(key), key, "Touche inconnue"),
    };

    /// <summary>PRESET_1..PRESET_6 à partir d'un numéro de 1 à 6.</summary>
    public static RemoteKey PresetKey(int slot) => slot switch
    {
        1 => RemoteKey.Preset1,
        2 => RemoteKey.Preset2,
        3 => RemoteKey.Preset3,
        4 => RemoteKey.Preset4,
        5 => RemoteKey.Preset5,
        6 => RemoteKey.Preset6,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, "Présélection 1 à 6 uniquement"),
    };
}
