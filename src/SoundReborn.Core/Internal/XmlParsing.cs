using System.Globalization;
using System.Xml.Linq;
using SoundReborn.Core.Models;

namespace SoundReborn.Core.Internal;

/// <summary>
/// Petits utilitaires de lecture XML. Le firmware SoundTouch est capricieux :
/// des éléments manquent selon la source et le modèle, donc tout est optionnel
/// et rien ne lève d'exception sur une absence.
/// </summary>
internal static class Xml
{
    public static string? Str(XElement? parent, string name)
    {
        var value = parent?.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static string? Attr(XElement? element, string name)
    {
        var value = element?.Attribute(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public static int Int(XElement? parent, string name, int fallback = 0)
        => ParseInt(Str(parent, name), fallback);

    public static int IntAttr(XElement? element, string name, int fallback = 0)
        => ParseInt(Attr(element, name), fallback);

    public static bool Bool(XElement? parent, string name, bool fallback = false)
        => ParseBool(Str(parent, name), fallback);

    public static bool BoolAttr(XElement? element, string name, bool fallback = false)
        => ParseBool(Attr(element, name), fallback);

    private static int ParseInt(string? raw, int fallback) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static bool ParseBool(string? raw, bool fallback) => raw?.ToLowerInvariant() switch
    {
        "true" or "1" or "yes" => true,
        "false" or "0" or "no" => false,
        _ => fallback,
    };

    /// <summary>Échappe une valeur destinée à un corps XML construit à la main.</summary>
    public static string Escape(string? value) => string.IsNullOrEmpty(value)
        ? ""
        : value.Replace("&", "&amp;")
               .Replace("<", "&lt;")
               .Replace(">", "&gt;")
               .Replace("\"", "&quot;")
               .Replace("'", "&apos;");
}

/// <summary>Conversion des documents XML du firmware vers les modèles de la bibliothèque.</summary>
internal static class FirmwareXml
{
    public static DeviceInfo ParseInfo(XElement root) => new()
    {
        DeviceId = Xml.Attr(root, "deviceID") ?? "",
        Name = Xml.Str(root, "name") ?? "",
        Type = Xml.Str(root, "type") ?? Xml.Attr(root, "type") ?? "",
        SerialNumber = Xml.Str(root, "serialNumber"),
        SoftwareVersion = root.Elements("components")
                              .Elements("component")
                              .Select(c => Xml.Str(c, "softwareVersion"))
                              .FirstOrDefault(v => v is not null),
        MacAddress = root.Elements("networkInfo").Select(n => Xml.Str(n, "macAddress")).FirstOrDefault(v => v is not null),
        IpAddress = root.Elements("networkInfo").Select(n => Xml.Str(n, "ipAddress")).FirstOrDefault(v => v is not null),
    };

    public static ContentItem? ParseContentItem(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        return new ContentItem
        {
            Source = Xml.Attr(element, "source") ?? "",
            SourceAccount = Xml.Attr(element, "sourceAccount"),
            Type = Xml.Attr(element, "type"),
            Location = Xml.Attr(element, "location"),
            IsPresetable = Xml.BoolAttr(element, "isPresetable"),
            ItemName = Xml.Str(element, "itemName"),
            ContainerArt = Xml.Str(element, "containerArt"),
        };
    }

    public static NowPlaying ParseNowPlaying(XElement root)
    {
        var art = root.Element("art");
        var time = root.Element("time");
        var content = ParseContentItem(root.Element("ContentItem"));

        return new NowPlaying
        {
            DeviceId = Xml.Attr(root, "deviceID"),
            Source = Xml.Attr(root, "source") ?? "STANDBY",
            SourceAccount = Xml.Attr(root, "sourceAccount"),
            Content = content,
            Track = Xml.Str(root, "track"),
            Artist = Xml.Str(root, "artist"),
            Album = Xml.Str(root, "album"),
            StationName = Xml.Str(root, "stationName") ?? content?.ItemName,
            Description = Xml.Str(root, "description"),
            ArtUrl = NormalizeArt(art?.Value) ?? NormalizeArt(content?.ContainerArt),
            ArtStatus = Xml.Attr(art, "artImageStatus"),
            PlayStatus = ParsePlayStatus(Xml.Str(root, "playStatus")),
            ShuffleSetting = Xml.Str(root, "shuffleSetting"),
            RepeatSetting = Xml.Str(root, "repeatSetting"),
            PositionSeconds = time is null ? 0 : ParseIntValue(time.Value),
            DurationSeconds = Xml.IntAttr(time, "total"),
        };
    }

    private static string? NormalizeArt(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return null;
        }

        raw = raw.Trim();

        // Le firmware renvoie parfois un chemin relatif ou une URL vide ; on ne garde
        // que ce qui est réellement chargeable par un contrôle Image.
        return raw.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? raw : null;
    }

    private static int ParseIntValue(string? raw) =>
        int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;

    public static PlayStatus ParsePlayStatus(string? raw) => raw?.ToUpperInvariant() switch
    {
        "PLAY_STATE" => PlayStatus.Play,
        "PAUSE_STATE" => PlayStatus.Pause,
        "STOP_STATE" => PlayStatus.Stop,
        "BUFFERING_STATE" => PlayStatus.Buffering,
        "INVALID_PLAY_STATUS" => PlayStatus.Invalid,
        _ => PlayStatus.Unknown,
    };

    public static VolumeState ParseVolume(XElement root) => new(
        Xml.Int(root, "targetvolume"),
        Xml.Int(root, "actualvolume"),
        Xml.Bool(root, "muteenabled"));

    public static BassState ParseBass(XElement root) => new(
        Xml.Int(root, "targetbass"),
        Xml.Int(root, "actualbass"));

    public static BassCapabilities ParseBassCapabilities(XElement root) => new(
        Xml.Bool(root, "bassAvailable"),
        Xml.Int(root, "bassMin", -9),
        Xml.Int(root, "bassMax", 0),
        Xml.Int(root, "bassDefault"));

    public static IReadOnlyList<PresetInfo> ParsePresets(XElement root) => root
        .Elements("preset")
        .Select(p => new PresetInfo
        {
            Id = Xml.IntAttr(p, "id"),
            Content = ParseContentItem(p.Element("ContentItem")),
        })
        .Where(p => p.Id is >= 1 and <= 6)
        .OrderBy(p => p.Id)
        .ToList();

    public static IReadOnlyList<SourceItem> ParseSources(XElement root) => root
        .Elements("sourceItem")
        .Select(s => new SourceItem
        {
            Source = Xml.Attr(s, "source") ?? "",
            SourceAccount = Xml.Attr(s, "sourceAccount"),
            Status = Xml.Attr(s, "status"),
            IsLocal = Xml.BoolAttr(s, "isLocal"),
            MultiroomAllowed = Xml.BoolAttr(s, "multiroomallowed"),
            ItemName = string.IsNullOrWhiteSpace(s.Value) ? Xml.Str(s, "itemName") : s.Value.Trim(),
        })
        .Where(s => !string.IsNullOrEmpty(s.Source))
        .ToList();

    public static ZoneState ParseZone(XElement root)
    {
        // Format du firmware : <member ipaddress="192.168.1.20">DEVICEID</member>
        // — l'IP est l'attribut, le deviceID est le texte de l'élément.
        var members = root.Elements("member")
            .Select(m => new ZoneMember
            {
                DeviceId = m.Value.Trim(),
                IpAddress = Xml.Attr(m, "ipaddress") ?? Xml.Attr(m, "ipAddress") ?? "",
                Role = Xml.Attr(m, "role"),
            })
            .Where(m => !string.IsNullOrEmpty(m.IpAddress))
            .ToList();

        return new ZoneState
        {
            MasterDeviceId = Xml.Attr(root, "master"),
            SenderIp = Xml.Attr(root, "senderIPAddress"),
            Members = members,
        };
    }

    /// <summary>Corps XML de POST /select.</summary>
    public static string BuildSelectBody(ContentItem item)
    {
        var attributes = $"source=\"{Xml.Escape(item.Source)}\"";

        if (!string.IsNullOrWhiteSpace(item.SourceAccount))
        {
            attributes += $" sourceAccount=\"{Xml.Escape(item.SourceAccount)}\"";
        }

        if (!string.IsNullOrWhiteSpace(item.Type))
        {
            attributes += $" type=\"{Xml.Escape(item.Type)}\"";
        }

        if (!string.IsNullOrWhiteSpace(item.Location))
        {
            attributes += $" location=\"{Xml.Escape(item.Location)}\"";
        }

        var name = string.IsNullOrWhiteSpace(item.ItemName)
            ? ""
            : $"<itemName>{Xml.Escape(item.ItemName)}</itemName>";

        return $"<ContentItem {attributes}>{name}</ContentItem>";
    }

    /// <summary>Corps XML de POST /setZone et /addZoneSlave.</summary>
    public static string BuildZoneBody(string masterDeviceId, string masterIp, IEnumerable<ZoneMember> slaves)
    {
        var members = string.Concat(slaves.Select(s =>
            $"<member ipaddress=\"{Xml.Escape(s.IpAddress)}\">{Xml.Escape(s.DeviceId)}</member>"));

        return $"<zone master=\"{Xml.Escape(masterDeviceId)}\" senderIPAddress=\"{Xml.Escape(masterIp)}\">{members}</zone>";
    }
}
