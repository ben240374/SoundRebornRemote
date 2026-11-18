using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace SoundReborn.Core.Recording;

/// <summary>Un enregistrement gardé sur le téléphone.</summary>
public sealed record RecordingEntry(string File, string Station, DateTimeOffset Started, TimeSpan Duration, string ContentType, long Bytes)
{
    /// <summary>Chemin complet du fichier, résolu par la bibliothèque.</summary>
    public string Path { get; init; } = "";
}

/// <summary>
/// Les enregistrements du téléphone : un dossier privé de l'application et un
/// index « recordings.json ».
///
/// Le dossier fait foi. À chaque lecture, l'index lui est rapproché : une entrée
/// dont le fichier a disparu est retirée, un fichier audio sans entrée est
/// ajouté. Un enregistrement s'écrit d'abord sous un nom « .part », avec un petit
/// fichier « .part.json » qui garde la station et l'heure de début : si
/// l'application est arrêtée brutalement, il est rattaché à la relance s'il dure
/// au moins <see cref="RecordingRules.MinimumKept"/>, et supprimé sinon.
///
/// Rien n'est jamais supprimé d'office pour faire de la place : au-delà de
/// <see cref="RecordingRules.MaxCount"/>, c'est à l'utilisateur de choisir.
/// </summary>
public sealed class RecordingLibrary
{
    private const string IndexName = "recordings.json";
    private const string PartSuffix = ".part";
    private const string SidecarSuffix = ".part.json";
    private const int MaxStationLength = 60;

    private static readonly string[] AudioExtensions = { ".mp3", ".aac", ".ogg" };

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
    };

    private readonly string _folder;
    private readonly object _gate = new();

    /// <summary>Le « .part » en cours d'écriture : la reprise ne doit pas y toucher.</summary>
    private string? _activePart;

    public RecordingLibrary(string folder)
    {
        _folder = folder;
    }

    public string Folder => _folder;

    /// <summary>Plus de place : il faut supprimer un enregistrement avant d'en faire un autre.</summary>
    public bool IsFull => Load().Count >= RecordingRules.MaxCount;

    /// <summary>Les enregistrements, du plus récent au plus ancien.</summary>
    public IReadOnlyList<RecordingEntry> Load()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_folder);
            var entries = ReadIndex();
            var changed = RecoverParts(entries);

            // Une entrée dont le fichier a disparu est retirée.
            changed |= entries.RemoveAll(e => !System.IO.File.Exists(System.IO.Path.Combine(_folder, e.File))) > 0;

            // Un fichier audio sans entrée est ajouté, avec ce qu'on peut en savoir.
            foreach (var path in Directory.GetFiles(_folder))
            {
                var name = System.IO.Path.GetFileName(path);

                if (!AudioExtensions.Contains(System.IO.Path.GetExtension(name).ToLowerInvariant()) ||
                    entries.Any(e => string.Equals(e.File, name, StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                var info = new FileInfo(path);
                entries.Add(new RecordingEntry(
                    name,
                    StationFromFileName(name),
                    new DateTimeOffset(info.LastWriteTime),
                    TimeSpan.Zero,
                    ContentTypeFor(info.Extension),
                    info.Length));
                changed = true;
            }

            if (changed)
            {
                WriteIndex(entries);
            }

            return entries
                .OrderByDescending(e => e.Started)
                .Select(e => e with { Path = System.IO.Path.Combine(_folder, e.File) })
                .ToList();
        }
    }

    /// <summary>
    /// Prépare le fichier d'un nouvel enregistrement et renvoie son chemin
    /// « .part ». <paramref name="started"/> est l'heure locale de début.
    /// </summary>
    public string BeginPart(string station, DateTimeOffset started, string extension, string contentType)
    {
        lock (_gate)
        {
            Directory.CreateDirectory(_folder);
            var name = UniqueName(FileNameFor(station, started, extension));
            var part = System.IO.Path.Combine(_folder, name + PartSuffix);

            System.IO.File.WriteAllBytes(part, Array.Empty<byte>());
            System.IO.File.WriteAllText(
                part + ".json",
                JsonSerializer.Serialize(new Sidecar { Station = station, Started = started, ContentType = contentType }, Json));

            _activePart = part;
            return part;
        }
    }

    /// <summary>
    /// Termine un enregistrement : gardé s'il dure au moins
    /// <see cref="RecordingRules.MinimumKept"/>, supprimé sinon (null).
    /// </summary>
    public RecordingEntry? Commit(string partPath, TimeSpan duration, long bytes)
    {
        lock (_gate)
        {
            if (string.Equals(_activePart, partPath, StringComparison.Ordinal))
            {
                _activePart = null;
            }

            var sidecar = ReadSidecar(partPath);
            DeleteQuietly(partPath + ".json");

            if (duration < RecordingRules.MinimumKept || sidecar is null || !System.IO.File.Exists(partPath))
            {
                DeleteQuietly(partPath);
                return null;
            }

            var entry = Adopt(partPath, sidecar, duration, bytes);
            var entries = ReadIndex();
            entries.Add(entry);
            WriteIndex(entries);
            return entry with { Path = System.IO.Path.Combine(_folder, entry.File) };
        }
    }

    public void Delete(RecordingEntry entry)
    {
        if (!IsPlainName(entry.File))
        {
            return;
        }

        lock (_gate)
        {
            DeleteQuietly(System.IO.Path.Combine(_folder, entry.File));
            var entries = ReadIndex();
            entries.RemoveAll(e => string.Equals(e.File, entry.File, StringComparison.OrdinalIgnoreCase));
            WriteIndex(entries);
        }
    }

    /// <summary>« Bel RTL 2026-11-17 14h32.mp3 » : lisible pour celui qui reçoit le fichier.</summary>
    public static string FileNameFor(string station, DateTimeOffset started, string extension)
    {
        var invalid = System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '/', '\\', ':', '*', '?', '"', '<', '>', '|' }).ToHashSet();
        var builder = new StringBuilder();

        foreach (var c in (station ?? "").Trim())
        {
            builder.Append(invalid.Contains(c) || char.IsControl(c) ? '-' : c);
        }

        var name = builder.ToString().Trim().TrimEnd('.');

        if (name.Length > MaxStationLength)
        {
            name = name[..MaxStationLength].TrimEnd();
        }

        if (name.Length == 0)
        {
            name = "Radio";
        }

        return $"{name} {started.ToString("yyyy-MM-dd HH'h'mm", CultureInfo.InvariantCulture)}{extension}";
    }

    // ------------------------------------------------------------------ interne

    private RecordingEntry Adopt(string partPath, Sidecar sidecar, TimeSpan duration, long bytes)
    {
        var wanted = System.IO.Path.GetFileName(partPath)[..^PartSuffix.Length];
        var name = System.IO.File.Exists(System.IO.Path.Combine(_folder, wanted)) ? UniqueName(wanted) : wanted;
        System.IO.File.Move(partPath, System.IO.Path.Combine(_folder, name));

        return new RecordingEntry(
            name,
            sidecar.Station ?? StationFromFileName(name),
            sidecar.Started,
            duration > RecordingRules.MaxDuration ? RecordingRules.MaxDuration : duration,
            sidecar.ContentType ?? ContentTypeFor(System.IO.Path.GetExtension(name)),
            bytes);
    }

    /// <summary>Rattache ou supprime les « .part » laissés par un arrêt brutal.</summary>
    private bool RecoverParts(List<RecordingEntry> entries)
    {
        var changed = false;

        foreach (var part in Directory.GetFiles(_folder, "*" + PartSuffix))
        {
            if (string.Equals(part, _activePart, StringComparison.Ordinal))
            {
                continue;
            }

            var sidecar = ReadSidecar(part);
            DeleteQuietly(part + ".json");

            var duration = sidecar is null
                ? TimeSpan.Zero
                : new DateTimeOffset(System.IO.File.GetLastWriteTimeUtc(part), TimeSpan.Zero) - sidecar.Started;

            if (sidecar is null || duration < RecordingRules.MinimumKept)
            {
                DeleteQuietly(part);
                continue;
            }

            entries.Add(Adopt(part, sidecar, duration, new FileInfo(part).Length));
            changed = true;
        }

        // Un « .part.json » orphelin ne sert plus à rien.
        foreach (var orphan in Directory.GetFiles(_folder, "*" + SidecarSuffix))
        {
            if (!System.IO.File.Exists(orphan[..^".json".Length]))
            {
                DeleteQuietly(orphan);
            }
        }

        return changed;
    }

    private string UniqueName(string wanted)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(wanted);
        var extension = System.IO.Path.GetExtension(wanted);
        var candidate = wanted;

        for (var n = 2; Taken(candidate); n++)
        {
            candidate = $"{stem} ({n}){extension}";
        }

        return candidate;
    }

    private bool Taken(string name)
    {
        var path = System.IO.Path.Combine(_folder, name);
        return System.IO.File.Exists(path) || System.IO.File.Exists(path + PartSuffix);
    }

    private static readonly Regex StampPattern = new(@"\s+\d{4}-\d{2}-\d{2} \d{2}h\d{2}(\s+\(\d+\))?$", RegexOptions.Compiled);

    private static string StationFromFileName(string name)
    {
        var stem = System.IO.Path.GetFileNameWithoutExtension(name);
        var station = StampPattern.Replace(stem, "").Trim();
        return station.Length == 0 ? stem : station;
    }

    private static string ContentTypeFor(string extension)
        => extension.ToLowerInvariant() switch
        {
            ".aac" => "audio/aac",
            ".ogg" => "audio/ogg",
            _ => "audio/mpeg",
        };

    /// <summary>Un nom de fichier seul, sans dossier ni « .. », sans caractère interdit.</summary>
    internal static bool IsPlainName(string? name)
        => !string.IsNullOrWhiteSpace(name) &&
           name is not "." and not ".." &&
           name.IndexOfAny(new[] { '/', '\\' }) < 0 &&
           name.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0 &&
           string.Equals(System.IO.Path.GetFileName(name), name, StringComparison.Ordinal);

    private List<RecordingEntry> ReadIndex()
    {
        try
        {
            var path = System.IO.Path.Combine(_folder, IndexName);

            if (!System.IO.File.Exists(path))
            {
                return new List<RecordingEntry>();
            }

            var rows = JsonSerializer.Deserialize<List<IndexRow>>(System.IO.File.ReadAllText(path), Json) ?? new();

            // Un nom qui n'est pas un simple nom de fichier (« ../x », un chemin
            // absolu) est ignoré : la corbeille ne doit jamais toucher hors du dossier.
            return rows
                .Where(r => IsPlainName(r.File))
                .Select(r => new RecordingEntry(r.File!, r.Station ?? "", r.Started, TimeSpan.FromSeconds(r.DurationSeconds), r.ContentType ?? "audio/mpeg", r.Bytes))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            // Un index illisible se reconstruit depuis le dossier.
            return new List<RecordingEntry>();
        }
    }

    private void WriteIndex(List<RecordingEntry> entries)
    {
        var rows = entries.Select(e => new IndexRow
        {
            File = e.File,
            Station = e.Station,
            Started = e.Started,
            DurationSeconds = e.Duration.TotalSeconds,
            ContentType = e.ContentType,
            Bytes = e.Bytes,
        }).ToList();

        var path = System.IO.Path.Combine(_folder, IndexName);
        var temp = path + ".tmp";
        System.IO.File.WriteAllText(temp, JsonSerializer.Serialize(rows, Json));
        System.IO.File.Move(temp, path, overwrite: true);
    }

    private static Sidecar? ReadSidecar(string partPath)
    {
        try
        {
            var path = partPath + ".json";
            return System.IO.File.Exists(path) ? JsonSerializer.Deserialize<Sidecar>(System.IO.File.ReadAllText(path), Json) : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static void DeleteQuietly(string path)
    {
        try
        {
            if (System.IO.File.Exists(path))
            {
                System.IO.File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Un fichier qu'on ne peut pas supprimer sera repris au prochain rapprochement.
        }
    }

    private sealed class IndexRow
    {
        public string? File { get; set; }
        public string? Station { get; set; }
        public DateTimeOffset Started { get; set; }
        public double DurationSeconds { get; set; }
        public string? ContentType { get; set; }
        public long Bytes { get; set; }
    }

    private sealed class Sidecar
    {
        public string? Station { get; set; }
        public DateTimeOffset Started { get; set; }
        public string? ContentType { get; set; }
    }
}
