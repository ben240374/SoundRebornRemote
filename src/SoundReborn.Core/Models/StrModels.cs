using System.Text.Json;
using System.Text.Json.Serialization;

namespace SoundReborn.Core.Models;

/// <summary>Réponse de GET /api/agent/version : version de l'agent STR installé sur l'enceinte.</summary>
public sealed class AgentInfo
{
    [JsonPropertyName("version")] public string? Version { get; set; }
    [JsonPropertyName("build")] public string? Build { get; set; }
    [JsonPropertyName("friendlyName")] public string? FriendlyName { get; set; }
    [JsonPropertyName("model")] public string? Model { get; set; }

    /// <summary>
    /// « 2 » sur un agent qui gère la seconde rangée de touches (7 à 12). Absent
    /// avant. Lu tel quel : l'agent l'envoie comme chaîne, et un type strict ferait
    /// échouer toute la lecture de version le jour où il deviendrait un nombre.
    /// </summary>
    [JsonPropertyName("presetRows")] public JsonElement PresetRows { get; set; }

    /// <summary>Vrai quand les touches 7 à 12 existent sur cet agent.</summary>
    [JsonIgnore]
    public bool HasSecondPresetRow
    {
        get
        {
            if (PresetRows.ValueKind == JsonValueKind.String)
            {
                return int.TryParse(PresetRows.GetString(), out var announced) && announced >= 2;
            }

            if (PresetRows.ValueKind == JsonValueKind.Number)
            {
                return PresetRows.TryGetInt32(out var counted) && counted >= 2;
            }

            return false;
        }
    }

    /// <summary>« v0.9.79 » — préfixé du v si l'agent ne le fait pas lui-même.</summary>
    [JsonIgnore]
    public string DisplayVersion
    {
        get
        {
            if (string.IsNullOrWhiteSpace(Version))
            {
                return "";
            }

            var v = Version!.Trim();
            return v.StartsWith('v') ? v : "v" + v;
        }
    }
}

/// <summary>Réponse de GET /api/box/volume sur l'agent STR.</summary>
public sealed class StrVolume
{
    [JsonPropertyName("value")] public int Value { get; set; }
    [JsonPropertyName("target")] public int Target { get; set; }
    [JsonPropertyName("muted")] public bool Muted { get; set; }
}

/// <summary>
/// Une présélection du magasin STR (GET /api/presets). Douze emplacements : 1 à 6
/// sont les touches de l'enceinte, 7 à 12 la seconde rangée — la touche n pressée
/// deux fois donne l'emplacement 6 + n.
/// </summary>
public sealed class StrPreset
{
    [JsonPropertyName("slot")] public int Slot { get; set; }
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("type")] public string? Type { get; set; }
    [JsonPropertyName("stream_url")] public string? StreamUrl { get; set; }
    [JsonPropertyName("uri")] public string? Uri { get; set; }
    [JsonPropertyName("art")] public string? Art { get; set; }
    [JsonPropertyName("account")] public string? Account { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }
    [JsonPropertyName("bitrate")] public int Bitrate { get; set; }
    [JsonPropertyName("codec")] public string? Codec { get; set; }

    [JsonIgnore] public bool IsSpotify => string.Equals(Type, "spotify", StringComparison.OrdinalIgnoreCase);
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? $"Présélection {Slot}" : Name!;
}

/// <summary>Enveloppe tolérée par STR : soit un tableau nu, soit {"presets":[...]}.</summary>
public sealed class StrPresetEnvelope
{
    [JsonPropertyName("presets")] public List<StrPreset>? Presets { get; set; }
}

/// <summary>Une entrée de GET /api/recent (30 derniers titres, toutes sources).</summary>
public sealed class RecentEntry
{
    [JsonPropertyName("ts")] public string? Timestamp { get; set; }
    [JsonPropertyName("source")] public string? Source { get; set; }
    [JsonPropertyName("cardKey")] public string? CardKey { get; set; }
    [JsonPropertyName("cardName")] public string? CardName { get; set; }
    [JsonPropertyName("cardArt")] public string? CardArt { get; set; }
    [JsonPropertyName("cardURL")] public string? CardUrl { get; set; }
    [JsonPropertyName("mime")] public string? Mime { get; set; }

    /// <summary>
    /// Durée d'une piste de bibliothèque. Gardée brute : elle n'est là que pour
    /// être renvoyée à l'agent, et une forme inattendue ne doit pas faire échouer
    /// la lecture de tout l'historique.
    /// </summary>
    [JsonPropertyName("durationSec")] public JsonElement DurationSec { get; set; }

    [JsonPropertyName("track")] public string? Track { get; set; }
    [JsonPropertyName("account")] public string? Account { get; set; }
    [JsonPropertyName("homepage")] public string? Homepage { get; set; }

    [JsonIgnore] public string DisplayTitle =>
        !string.IsNullOrWhiteSpace(Track) ? Track! :
        !string.IsNullOrWhiteSpace(CardName) ? CardName! : "(inconnu)";

    [JsonIgnore] public string DisplaySubtitle =>
        string.IsNullOrWhiteSpace(Track) ? (Source ?? "") : $"{CardName} · {Source}";

    /// <summary>Entrée de bibliothèque musicale (serveur UPnP du réseau).</summary>
    [JsonIgnore] public bool IsLibrary =>
        string.Equals(Source, "upnp", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Album ou dossier entier. Il se rejoue par sa clé, pas par son adresse :
    /// cardURL n'en désigne que la première piste.
    /// </summary>
    [JsonIgnore] public bool IsQueueCard =>
        IsLibrary &&
        !string.IsNullOrWhiteSpace(CardKey) &&
        CardKey!.StartsWith("queue:", StringComparison.OrdinalIgnoreCase);

    /// <summary>Durée en secondes quand l'agent en fournit une exploitable.</summary>
    [JsonIgnore]
    public double? DurationSeconds
    {
        get
        {
            if (DurationSec.ValueKind == JsonValueKind.Number &&
                DurationSec.TryGetDouble(out var number))
            {
                return number;
            }

            if (DurationSec.ValueKind == JsonValueKind.String &&
                double.TryParse(
                    DurationSec.GetString(),
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out var parsed))
            {
                return parsed;
            }

            return null;
        }
    }

    /// <summary>Vrai quand l'entrée peut être relancée : dossier, ou adresse http.</summary>
    [JsonIgnore] public bool CanReplay =>
        IsQueueCard ||
        (!string.IsNullOrWhiteSpace(CardUrl) &&
         CardUrl!.StartsWith("http", StringComparison.OrdinalIgnoreCase));
}

/// <summary>Un membre de zone tel que l'agent STR le décrit dans /api/box/zone/volume.</summary>
public sealed class StrZoneMemberVolume
{
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
    [JsonPropertyName("deviceID")] public string? DeviceId { get; set; }
    [JsonPropertyName("role")] public string? Role { get; set; }
    [JsonPropertyName("isSelf")] public bool IsSelf { get; set; }
    [JsonPropertyName("isMaster")] public bool IsMaster { get; set; }

    /// <summary>-1 quand l'enceinte n'a pas répondu.</summary>
    [JsonPropertyName("volume")] public int Volume { get; set; }
    [JsonPropertyName("muted")] public bool Muted { get; set; }

    [JsonIgnore] public bool IsReachable => Volume >= 0;
    [JsonIgnore] public string DisplayName => string.IsNullOrWhiteSpace(Name) ? (Ip ?? "?") : Name!;
}

/// <summary>Réponse de GET /api/box/zone/volume.</summary>
public sealed class StrZoneVolume
{
    [JsonPropertyName("grouped")] public bool Grouped { get; set; }
    [JsonPropertyName("stereo")] public bool Stereo { get; set; }
    [JsonPropertyName("average")] public int Average { get; set; }
    [JsonPropertyName("members")] public List<StrZoneMemberVolume> Members { get; set; } = new();
}

/// <summary>Un membre de zone dans le corps de POST /api/box/zone.</summary>
public sealed class StrZoneMemberRef
{
    [JsonPropertyName("deviceID")] public string? DeviceId { get; set; }
    [JsonPropertyName("ip")] public string? Ip { get; set; }
}

/// <summary>Corps de POST /api/box/zone (former un groupe).</summary>
public sealed class StrZoneFormRequest
{
    [JsonPropertyName("master")] public StrZoneMemberRef Master { get; set; } = new();
    [JsonPropertyName("slaves")] public List<StrZoneMemberRef> Slaves { get; set; } = new();

    /// <summary>"native" (diffusion firmware) ou "mirror". Laisser "native".</summary>
    [JsonPropertyName("mode")] public string Mode { get; set; } = "native";

    /// <summary>Groupe permanent : re-formé automatiquement à la prochaine lecture du maître.</summary>
    [JsonPropertyName("permanent")] public bool Permanent { get; set; }

    // Pas de champ « stereo », et c'est voulu.
    //
    // Pour l'agent, « stereo : true » n'est pas une propriété du groupe que l'on
    // rendrait telle qu'on l'a lue : c'est un ORDRE, « fais une paire stéréo de
    // ces enceintes » (/addGroup du firmware). Le renvoyer en ajoutant une
    // troisième enceinte à la moitié d'une paire demandait donc d'apparier A et
    // C. Une paire est un groupe du firmware, distinct de la zone multi-pièces ;
    // cette application ne les crée pas, et ce type ne peut plus en demander une.
    // « name » disparaît avec lui : il ne servait qu'à nommer la paire.
}

/// <summary>Une suiveuse du groupe que l'agent garde en mémoire quand aucun groupe n'est vivant.</summary>
public sealed class StrRememberedMember
{
    public string Ip { get; init; } = "";

    public string? DeviceId { get; init; }

    public string? Name { get; init; }
}

/// <summary>
/// Ce qu'il faut relire d'une zone avant de la republier. L'agent enregistre le
/// document tel quel : tout drapeau omis est un drapeau perdu.
/// </summary>
public sealed class StrZoneFacts
{
    /// <summary>
    /// Vrai quand le groupe ENREGISTRÉ sur cette enceinte est durable — qu'il soit
    /// vivant ou qu'il attende la prochaine lecture de son maître.
    /// </summary>
    public bool Permanent { get; init; }

    /// <summary>
    /// Vrai quand l'enceinte est la moitié d'une paire stéréo. À lire seulement :
    /// ce drapeau ne se renvoie jamais dans une formation de groupe.
    /// </summary>
    public bool Stereo { get; init; }

    public string? StereoName { get; init; }

    /// <summary>
    /// Les suiveuses du groupe enregistré, quand AUCUN groupe n'est vivant. Vide
    /// dès qu'un groupe joue : l'agent ne les donne que pour le groupe en attente.
    /// </summary>
    public IReadOnlyList<StrRememberedMember> Remembered { get; init; } = Array.Empty<StrRememberedMember>();

    /// <summary>
    /// Vrai quand ces suiveuses sont exactement celles du groupe enregistré : on
    /// s'apprête alors à FORMER ce groupe, pas à le remplacer par un autre.
    /// </summary>
    public bool RemembersExactly(IEnumerable<string?> followerIps)
    {
        var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var ip in followerIps)
        {
            if (!string.IsNullOrWhiteSpace(ip))
            {
                wanted.Add(ip!.Trim());
            }
        }

        if (wanted.Count == 0 || Remembered.Count == 0)
        {
            return false;
        }

        var saved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var member in Remembered)
        {
            saved.Add(member.Ip.Trim());
        }

        return saved.SetEquals(wanted);
    }
}

/// <summary>
/// Réponse de DELETE /api/box/zone. L'agent répond 200 même lorsqu'il n'a pas
/// défait le groupe ; c'est le corps qui le dit.
/// </summary>
public sealed class StrDissolveResult
{
    /// <summary>Faux quand l'enceinte a gardé le groupe malgré la demande.</summary>
    public bool Ok { get; init; }

    /// <summary>Vrai quand il n'y avait aucun groupe à défaire — ce n'est pas un échec.</summary>
    public bool Nothing { get; init; }

    /// <summary>Nombre de membres encore groupés quand <see cref="Ok"/> est faux.</summary>
    public int Remaining { get; init; }

    /// <summary>Vrai quand le groupe est défait mais que la paire stéréo subsiste.</summary>
    public bool StereoPairKept { get; init; }

    public string? Error { get; init; }
}

/// <summary>Corps de POST /api/play (jouer une URL arbitraire).</summary>
public sealed class StrPlayUrlRequest
{
    [JsonPropertyName("url")] public string Url { get; set; } = "";
    [JsonPropertyName("title")] public string? Title { get; set; }
    [JsonPropertyName("icon")] public string? Icon { get; set; }

    /// <summary>
    /// Format du flux : « AAC », « MP3 »… À ne pas confondre avec « mime », que
    /// l'agent interprète comme « fichier de bibliothèque locale, passe l'URL telle
    /// quelle à l'enceinte » — ce qui coupe le relais de flux et le chemin des
    /// stations natives.
    /// </summary>
    [JsonPropertyName("codec")] public string? Codec { get; set; }

    /// <summary>
    /// Réservé à une piste de bibliothèque que l'on relance depuis l'historique :
    /// c'est précisément le cas où l'agent doit passer l'adresse telle quelle.
    /// Jamais pour une station.
    /// </summary>
    [JsonPropertyName("mime")] public string? Mime { get; set; }

    /// <summary>Durée de la piste de bibliothèque, renvoyée avec « mime ».</summary>
    [JsonPropertyName("duration_sec")] public double? DurationSec { get; set; }
}

/// <summary>Corps de POST /api/queue/replay-card (rejouer un album ou un dossier).</summary>
public sealed class StrReplayCardRequest
{
    [JsonPropertyName("key")] public string Key { get; set; } = "";
    [JsonPropertyName("name")] public string? Name { get; set; }
    [JsonPropertyName("art")] public string? Art { get; set; }
}

/// <summary>
/// Ce que l'agent dit d'une relance. Un 200 ne suffit pas : un dossier dont le
/// serveur de médias est introuvable répond 200 avec « offline ».
/// </summary>
public sealed class StrReplayOutcome
{
    /// <summary>Vrai quand le serveur de médias n'a pas été trouvé : rien ne joue.</summary>
    public bool Offline { get; init; }

    /// <summary>Nom du serveur de médias concerné, quand l'agent le donne.</summary>
    public string? Server { get; init; }
}

/// <summary>Réponse de POST /api/box/save-playing.</summary>
public sealed class StrSavedPlaying
{
    [JsonPropertyName("slot")] public int Slot { get; set; }

    /// <summary>Ce qui a été gardé : station, dossier, piste, copie d'une touche.</summary>
    [JsonPropertyName("kind")] public string? Kind { get; set; }

    [JsonPropertyName("name")] public string? Name { get; set; }

    /// <summary>Vrai quand la touche portait déjà exactement cela.</summary>
    [JsonPropertyName("unchanged")] public bool Unchanged { get; set; }
}

/// <summary>Réponse de GET /api/box/sleep.</summary>
public sealed class StrSleepState
{
    [JsonPropertyName("active")] public bool Active { get; set; }

    /// <summary>Secondes restantes ; n'a de sens que si <see cref="Active"/>.</summary>
    [JsonPropertyName("remainingSec")] public double RemainingSec { get; set; }

    /// <summary>Vrai quand l'échéance éteindra tout le groupe, pas cette seule enceinte.</summary>
    [JsonPropertyName("group")] public bool Group { get; set; }
}
