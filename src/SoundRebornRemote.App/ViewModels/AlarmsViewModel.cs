using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Un réveil tel qu'affiché dans la liste.</summary>
public sealed class AlarmRow
{
    public required string Id { get; init; }

    /// <summary>« 07:30 ».</summary>
    public required string Time { get; init; }

    /// <summary>« lun. mar. mer. · Touche 3 — Radio X ».</summary>
    public required string Detail { get; init; }

    /// <summary>« Activé » ou « Désactivé ».</summary>
    public required string State { get; init; }

    public required bool IsOn { get; init; }
}

/// <summary>
/// Réveils et volume de départ de l'enceinte DÉSIGNÉE (onglet Réglages).
///
/// Un réveil ne fait sonner que l'enceinte sur laquelle il est enregistré, même
/// dans un groupe : c'est donc l'enceinte que l'utilisateur a choisie qui compte,
/// pas le maître qui reçoit les commandes.
///
/// L'agent remplace le document des réveils en entier à chaque enregistrement.
/// Chaque modification part donc du dernier document lu, avec sa révision : si une
/// autre application a enregistré entre-temps, l'agent répond 409 avec le document
/// actuel, et la modification est refaite UNE fois sur celui-ci — sur le réveil
/// désigné par son identifiant, jamais par sa position.
/// </summary>
public sealed partial class AlarmsViewModel : BaseViewModel
{
    /// <summary>Durées d'arrêt automatique proposées, en minutes ; 0 = jamais.</summary>
    private static readonly int[] StandardAutoOff = { 0, 15, 30, 45, 60, 90, 120, 180 };

    /// <summary>Les seules durées de répétition que l'agent accepte.</summary>
    private static readonly int[] SnoozeChoices = { 5, 9, 10, 15 };

    /// <summary>Ordre d'affichage des jours : lundi d'abord. Valeurs de l'agent : 0 = dimanche.</summary>
    private static readonly int[] WeekOrder = { 1, 2, 3, 4, 5, 6, 0 };

    private StrAlarmDocument? _document;
    private StrApiClient? _agent;
    private string? _host;
    private int _loadVersion;
    private bool _hasSecondRow;
    private Dictionary<int, string> _keyNames = new();
    private List<int> _keySlots = new();
    private List<int> _autoOffChoices = new();

    /// <summary>Le réveil en cours de modification ; null pour un nouveau.</summary>
    private string? _editingId;

    /// <summary>
    /// Les valeurs du réveil quand l'édition a commencé. À l'enregistrement, seuls
    /// les champs que l'utilisateur a CHANGÉS sont écrits : refaite sur un document
    /// plus récent, la modification ne défait pas ce qu'une autre application a
    /// changé sur le même réveil entre-temps.
    /// </summary>
    private AlarmValues? _original;

    private sealed record AlarmValues(
        bool Enabled, int Hour, int Minute, string Days, int Slot, int Volume, int? AutoOff, int Snooze);

    public AlarmsViewModel(SpeakerManager speakers) : base(speakers)
    {
        BuildSnoozeLabels();
    }

    // ---------------------------------------------------------------- État affiché

    [ObservableProperty]
    private bool _isAvailable;

    /// <summary>Pourquoi les réveils ne sont pas proposés (pas d'enceinte, pas d'agent…).</summary>
    [ObservableProperty]
    private string _unavailableNote = "";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private string _speakerLabel = "";

    [ObservableProperty]
    private string _nextAlarmText = "";

    [ObservableProperty]
    private bool _hasNextAlarm;

    [ObservableProperty]
    private string _warningText = "";

    [ObservableProperty]
    private bool _hasWarning;

    [ObservableProperty]
    private string _zoneText = "";

    public ObservableCollection<AlarmRow> Alarms { get; } = new();

    [ObservableProperty]
    private bool _hasAlarms;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddButton))]
    private bool _canAdd;

    /// <summary>« Ajouter un réveil » : quand il reste de la place, et pas pendant une modification.</summary>
    public bool ShowAddButton => CanAdd && !IsEditing;

    // ---------------------------------------------------------------- Éditeur

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowAddButton))]
    private bool _isEditing;

    /// <summary>Vrai pour un réveil déjà enregistré : le bouton Supprimer apparaît.</summary>
    [ObservableProperty]
    private bool _isExisting;

    [ObservableProperty]
    private string _editorTitle = "";

    public ObservableCollection<string> KeyLabels { get; } = new();

    public ObservableCollection<string> AutoOffLabels { get; } = new();

    public ObservableCollection<string> SnoozeLabels { get; } = new();

    /// <summary>
    /// Heure du réveil, liée au sélecteur d'heure d'Android (le cadran). Nullable :
    /// c'est le type du TimePicker de .NET MAUI 10 ; null veut dire « pas d'heure ».
    /// </summary>
    [ObservableProperty]
    private TimeSpan? _alarmTime = new TimeSpan(7, 0, 0);

    [ObservableProperty]
    private bool _monday;

    [ObservableProperty]
    private bool _tuesday;

    [ObservableProperty]
    private bool _wednesday;

    [ObservableProperty]
    private bool _thursday;

    [ObservableProperty]
    private bool _friday;

    [ObservableProperty]
    private bool _saturday;

    [ObservableProperty]
    private bool _sunday;

    [ObservableProperty]
    private int _keyIndex;

    [ObservableProperty]
    private bool _alarmEnabled = true;

    /// <summary>Faux : le réveil laisse l'enceinte à son volume (0 chez l'agent).</summary>
    [ObservableProperty]
    private bool _useAlarmVolume;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AlarmVolumeLabel))]
    private double _alarmVolume = 20;

    public string AlarmVolumeLabel => Level(AlarmVolume).ToString(CultureInfo.InvariantCulture);

    [ObservableProperty]
    private int _autoOffIndex;

    /// <summary>Une heure d'arrêt fixée ailleurs (téléphone STR) : l'agent ignore alors la durée.</summary>
    [ObservableProperty]
    private bool _hasOffAt;

    [ObservableProperty]
    private string _offAtNote = "";

    [ObservableProperty]
    private int _snoozeIndex = 1;

    // ---------------------------------------------------------------- Volume de départ

    [ObservableProperty]
    private bool _startVolumeAvailable;

    [ObservableProperty]
    private bool _startVolumeOn;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StartVolumeLabel))]
    private double _startVolumeLevel = 20;

    public string StartVolumeLabel => Level(StartVolumeLevel).ToString(CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- Lecture

    /// <summary>
    /// Relit les réveils et le volume de départ de l'enceinte désignée. Une lecture
    /// dépassée par une autre — l'utilisateur a changé d'enceinte entre-temps — est
    /// jetée sans rien afficher.
    /// </summary>
    public async Task LoadAsync()
    {
        var version = ++_loadVersion;
        var target = Speakers.SelectedEndpoint ?? Device?.Endpoint;
        var agent = AgentFor(target);

        if (!string.Equals(_host, target?.Host, StringComparison.OrdinalIgnoreCase))
        {
            // Autre enceinte : ce qui était en cours d'édition ne la concerne pas.
            CloseEditor();
            _document = null;
            _agent = null;
        }

        _host = target?.Host;
        SpeakerLabel = target?.DisplayName ?? "";

        if (agent is null)
        {
            SetUnavailable(Localization.Get(target is null ? "L_AlarmsNoSpeaker" : "L_AlarmsNoAgent"));
            return;
        }

        IsLoading = true;

        try
        {
            StrAlarmDocument document;

            try
            {
                document = await agent.GetAlarmsAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                if (version != _loadVersion)
                {
                    return;
                }

                if (_document is not null)
                {
                    // Même enceinte, déjà lue : un hoquet réseau ne doit ni vider la
                    // liste ni fermer un réveil en cours de modification.
                    IsError = true;
                    StatusMessage = Localization.Get("L_AlarmsUnavailable");
                }
                else
                {
                    SetUnavailable(Localization.Get("L_AlarmsUnavailable"));
                }

                return;
            }

            var names = new Dictionary<int, string>();

            try
            {
                foreach (var preset in await agent.GetPresetsAsync().ConfigureAwait(true))
                {
                    names[preset.Slot] = preset.DisplayName;
                }
            }
            catch (Exception)
            {
                // Sans les noms, les touches s'affichent par leur numéro.
            }

            var secondRow = false;

            try
            {
                secondRow = (await agent.GetAgentInfoAsync().ConfigureAwait(true))?.HasSecondPresetRow ?? false;
            }
            catch (Exception)
            {
                // Agent muet sur sa version : on s'en tient aux six touches.
            }

            int? startVolume = null;

            try
            {
                startVolume = await agent.GetStartVolumeAsync().ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Agent qui ne le gère pas : le bloc reste caché.
            }

            if (version != _loadVersion)
            {
                return;
            }

            _agent = agent;
            _keyNames = names;
            _hasSecondRow = secondRow;
            _document = document;

            IsAvailable = true;
            UnavailableNote = "";
            ApplyDocument(document);
            ApplyStartVolume(startVolume);
        }
        finally
        {
            if (version == _loadVersion)
            {
                IsLoading = false;
            }
        }
    }

    [RelayCommand]
    private Task RefreshAsync() => LoadAsync();

    /// <summary>Textes traduits : à refaire quand la langue change.</summary>
    public void RebuildLabels()
    {
        var snooze = SnoozeIndex;
        BuildSnoozeLabels();
        SnoozeIndex = snooze;

        if (IsEditing)
        {
            var key = KeyIndex;
            var autoOff = AutoOffIndex;
            BuildKeyLabels();
            KeyIndex = key;
            BuildAutoOffLabels();
            AutoOffIndex = autoOff;
        }

        if (_document is not null)
        {
            ApplyDocument(_document);
        }
        else if (!IsAvailable)
        {
            var target = Speakers.SelectedEndpoint ?? Device?.Endpoint;
            UnavailableNote = Localization.Get(target is null
                ? "L_AlarmsNoSpeaker"
                : AgentFor(target) is null ? "L_AlarmsNoAgent" : "L_AlarmsUnavailable");
        }

        StatusMessage = null;
        IsError = false;
    }

    private StrApiClient? AgentFor(SpeakerEndpoint? target)
    {
        if (target is null)
        {
            return null;
        }

        var device = Device;

        if (device is not null && string.Equals(target.Host, device.Host, StringComparison.OrdinalIgnoreCase))
        {
            return device.Str;
        }

        return target.StrPort is int port ? new StrApiClient(target.Host, port, Speakers.Http) : null;
    }

    private void SetUnavailable(string note)
    {
        CloseEditor();
        _document = null;
        _agent = null;
        IsAvailable = false;
        UnavailableNote = note;
        Alarms.Clear();
        HasAlarms = false;
        CanAdd = false;
        HasNextAlarm = false;
        NextAlarmText = "";
        HasWarning = false;
        WarningText = "";
        ZoneText = "";
        StartVolumeAvailable = false;
    }

    private void ApplyDocument(StrAlarmDocument document)
    {
        Alarms.Clear();

        foreach (var alarm in document.Alarms.OrderBy(a => a.Hour).ThenBy(a => a.Minute))
        {
            Alarms.Add(new AlarmRow
            {
                Id = alarm.Id,
                Time = TimeText(alarm.Hour, alarm.Minute),
                Detail = $"{DaysText(alarm.Days)} · {KeyText(alarm.Slot)}",
                State = Localization.Get(alarm.Enabled ? "L_AlarmOn" : "L_AlarmOff"),
                IsOn = alarm.Enabled,
            });
        }

        HasAlarms = Alarms.Count > 0;
        CanAdd = document.CanAdd;

        var culture = Localization.CultureOf(Localization.Current);

        if (document.NextFire is { } next)
        {
            NextAlarmText = Localization.Get("L_AlarmNext",
                next.ToLocalTime().ToString("dddd d MMMM, HH:mm", culture));
            HasNextAlarm = true;
        }
        else
        {
            NextAlarmText = "";
            HasNextAlarm = false;
        }

        var warnings = new List<string>();

        if (!document.ClockTrusted)
        {
            warnings.Add(Localization.Get("S_AlarmClockUntrusted"));
        }

        if (!document.ZoneResolved)
        {
            warnings.Add(Localization.Get("S_AlarmZoneUnknown"));
        }

        WarningText = string.Join("\n", warnings);
        HasWarning = warnings.Count > 0;

        var phone = PhoneZone();

        ZoneText = string.IsNullOrWhiteSpace(document.Zone)
            ? phone is null
                ? Localization.Get("L_AlarmZoneUtc")
                : Localization.Get("L_AlarmZoneUtcFix", phone)
            : Localization.Get("L_AlarmZone", document.Zone);
    }

    private void ApplyStartVolume(int? level)
    {
        StartVolumeAvailable = level is not null;

        if (level is int value)
        {
            StartVolumeOn = value > 0;
            StartVolumeLevel = value > 0 ? value : 20;
        }
    }

    // ---------------------------------------------------------------- Édition

    [RelayCommand]
    private void AddAlarm()
    {
        if (_document is null || !_document.CanAdd)
        {
            return;
        }

        _editingId = null;
        _original = null;
        IsExisting = false;
        EditorTitle = Localization.Get("L_AlarmNew");

        AlarmTime = new TimeSpan(7, 0, 0);
        SetDays(new[] { 1, 2, 3, 4, 5 });
        AlarmEnabled = true;
        UseAlarmVolume = false;
        AlarmVolume = 20;

        // Un nouveau réveil s'arrête de lui-même au bout d'une heure, comme sur la
        // page téléphone de STR : personne n'est là pour un réveil qui joue toute la journée.
        var firstStored = _keyNames.Keys.Where(s => s is >= 1 and <= PresetKeys.Total).OrderBy(s => s).FirstOrDefault();
        BuildKeyLabels(firstStored > 0 ? firstStored : 1);
        KeyIndex = Math.Max(0, _keySlots.IndexOf(firstStored > 0 ? firstStored : 1));

        HasOffAt = false;
        OffAtNote = "";
        BuildAutoOffLabels(60);
        AutoOffIndex = _autoOffChoices.IndexOf(60);
        SnoozeIndex = Array.IndexOf(SnoozeChoices, StrAlarm.DefaultSnooze);

        StatusMessage = null;
        IsError = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void EditAlarm(AlarmRow? row)
    {
        var alarm = row is null ? null : _document?.Find(row.Id);

        if (alarm is null)
        {
            return;
        }

        _editingId = alarm.Id;
        IsExisting = true;
        EditorTitle = Localization.Get("L_AlarmEdit", TimeText(alarm.Hour, alarm.Minute));

        AlarmTime = new TimeSpan(Math.Clamp(alarm.Hour, 0, 23), Math.Clamp(alarm.Minute, 0, 59), 0);
        SetDays(alarm.Days);
        AlarmEnabled = alarm.Enabled;
        UseAlarmVolume = alarm.Volume > 0;
        AlarmVolume = alarm.Volume > 0 ? alarm.Volume : 20;

        BuildKeyLabels(alarm.Slot);
        KeyIndex = Math.Max(0, _keySlots.IndexOf(alarm.Slot));

        HasOffAt = alarm.OffAt is not null;
        OffAtNote = alarm.OffAt is null ? "" : Localization.Get("L_AlarmOffAt", alarm.OffAt);
        BuildAutoOffLabels(alarm.AutoOff);
        AutoOffIndex = Math.Max(0, _autoOffChoices.IndexOf(alarm.AutoOff));

        var snooze = Array.IndexOf(SnoozeChoices, alarm.SnoozeMinutes);
        SnoozeIndex = snooze >= 0 ? snooze : Array.IndexOf(SnoozeChoices, StrAlarm.DefaultSnooze);

        // Relevé APRÈS le remplissage : ce sont les valeurs telles que l'écran les
        // montre, donc celles que l'utilisateur n'a pas encore touchées.
        _original = ReadEditor();

        StatusMessage = null;
        IsError = false;
        IsEditing = true;
    }

    [RelayCommand]
    private void CancelEdit()
    {
        CloseEditor();
        StatusMessage = null;
        IsError = false;
    }

    [RelayCommand]
    private async Task SaveAlarmAsync()
    {
        if (!IsEditing)
        {
            return;
        }

        var days = SelectedDays();

        if (days.Count == 0)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_AlarmNoDay");
            return;
        }

        if (KeyIndex < 0 || KeyIndex >= _keySlots.Count ||
            AlarmTime is not { } time || time < TimeSpan.Zero || time.TotalHours >= 24)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_AlarmIncomplete");
            return;
        }

        // Tout est relevé MAINTENANT : la modification peut devoir être refaite sur
        // un document plus récent, et l'écran ne doit plus compter à ce moment-là.
        var id = _editingId;
        var original = _original;
        var edited = ReadEditor();

        var saved = await SaveChangeAsync(document =>
        {
            var alarm = id is null
                ? document.CanAdd ? document.Add() : null
                : document.Find(id);

            if (alarm is null)
            {
                return false;
            }

            // Un nouveau réveil reçoit tout ; un réveil existant, seulement ce qui a changé.
            var all = id is null || original is null;

            if (all || edited.Enabled != original!.Enabled)
            {
                alarm.Enabled = edited.Enabled;
            }

            if (all || edited.Hour != original!.Hour)
            {
                alarm.Hour = edited.Hour;
            }

            if (all || edited.Minute != original!.Minute)
            {
                alarm.Minute = edited.Minute;
            }

            if (all || edited.Days != original!.Days)
            {
                alarm.Days = ParseDays(edited.Days);
            }

            if (all || edited.Slot != original!.Slot)
            {
                alarm.Slot = edited.Slot;
            }

            if (all || edited.Volume != original!.Volume)
            {
                alarm.Volume = edited.Volume;
            }

            // Une heure d'arrêt fixée ailleurs l'emporte chez l'agent : on n'écrit
            // pas de durée par-dessus, elle ne servirait à rien.
            if (edited.AutoOff is int minutes && (all || edited.AutoOff != original!.AutoOff))
            {
                alarm.AutoOff = minutes;
            }

            if (all || edited.Snooze != original!.Snooze)
            {
                alarm.Snooze = edited.Snooze;
            }

            return true;
        },
        Localization.Get(id is null ? "S_AlarmAdded" : "S_AlarmSaved", TimeText(edited.Hour, edited.Minute)),
        Localization.Get(id is null ? "S_AlarmFull" : "S_AlarmGone")).ConfigureAwait(true);

        if (saved)
        {
            CloseEditor();
        }
    }

    /// <summary>Le bouton « Supprimer ce réveil » de l'éditeur.</summary>
    [RelayCommand]
    private async Task DeleteAlarmAsync()
    {
        if (IsEditing && _editingId is { } id)
        {
            await ConfirmAndDeleteAsync(id).ConfigureAwait(true);
        }
    }

    /// <summary>La corbeille d'une ligne de la liste.</summary>
    [RelayCommand]
    private async Task DeleteAlarmRowAsync(AlarmRow? row)
    {
        if (row is not null && !string.IsNullOrWhiteSpace(row.Id))
        {
            await ConfirmAndDeleteAsync(row.Id).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Demande confirmation, puis supprime le réveil désigné par son identifiant.
    /// La question nomme l'heure ENREGISTRÉE, pas celle qu'un éditeur ouvert
    /// afficherait après modification.
    /// </summary>
    private async Task ConfirmAndDeleteAsync(string id)
    {
        var alarm = _document?.Find(id);

        if (alarm is null)
        {
            return;
        }

        var delete = Localization.Get("L_AlarmDelete");

        var choice = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_AlarmDeleteAsk", TimeText(alarm.Hour, alarm.Minute)),
            Localization.Get("L_Cancel"),
            null,
            delete).ConfigureAwait(true);

        if (choice != delete)
        {
            return;
        }

        var deleted = await SaveChangeAsync(
            document => document.Remove(id),
            Localization.Get("S_AlarmDeleted"),
            Localization.Get("S_AlarmGone")).ConfigureAwait(true);

        // Si c'est le réveil ouvert dans l'éditeur, l'éditeur n'a plus d'objet.
        if (deleted && _editingId == id)
        {
            CloseEditor();
        }
    }

    /// <summary>
    /// Applique une modification au dernier document lu et l'enregistre. Sur un 409,
    /// la refait une fois sur le document que l'agent renvoie. Vrai si l'agent a
    /// enregistré.
    /// </summary>
    private async Task<bool> SaveChangeAsync(Func<StrAlarmDocument, bool> change, string success, string missing)
    {
        var agent = _agent;
        var document = _document;
        var host = _host;

        if (agent is null || document is null)
        {
            return false;
        }

        IsBusy = true;

        try
        {
            for (var attempt = 0; attempt < 2; attempt++)
            {
                var working = document.Clone();

                if (!change(working))
                {
                    IsError = true;
                    StatusMessage = missing;
                    return false;
                }

                // Un fuseau vide veut dire UTC pour l'agent : en Belgique, le réveil
                // sonnerait une ou deux heures trop tôt. On enregistre celui du
                // téléphone — l'écran l'annonce avant.
                if (string.IsNullOrWhiteSpace(working.Zone) && PhoneZone() is { } zone)
                {
                    working.Zone = zone;
                }

                var result = await agent.PutAlarmsAsync(working).ConfigureAwait(true);

                if (!string.Equals(host, _host, StringComparison.OrdinalIgnoreCase))
                {
                    // L'utilisateur a changé d'enceinte pendant l'envoi : l'écran
                    // montre désormais une autre enceinte, on n'y touche pas.
                    return false;
                }

                if (result.IsConflict)
                {
                    document = result.Current!;
                    _document = document;
                    ApplyDocument(document);
                    continue;
                }

                _document = result.Saved!;
                ApplyDocument(_document);
                ShowInfo(success);
                return true;
            }

            IsError = true;
            StatusMessage = Localization.Get("S_AlarmConflict");
            return false;
        }
        catch (SpeakerException ex) when (ex.StatusCode == HttpStatusCode.BadRequest &&
                                          !string.IsNullOrWhiteSpace(ex.ResponseBody))
        {
            // L'agent explique son refus en une phrase destinée à l'utilisateur.
            IsError = true;
            StatusMessage = Localization.Get("S_AlarmRefused", ex.ResponseBody!.Trim());
            return false;
        }
        catch (Exception ex)
        {
            ShowError(ex);
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void CloseEditor()
    {
        IsEditing = false;
        IsExisting = false;
        _editingId = null;
        _original = null;
    }

    /// <summary>Les valeurs de l'éditeur, dans la forme qu'attend l'agent.</summary>
    private AlarmValues ReadEditor()
    {
        int? autoOff = HasOffAt || AutoOffIndex < 0 || AutoOffIndex >= _autoOffChoices.Count
            ? null
            : _autoOffChoices[AutoOffIndex];

        var snooze = SnoozeIndex >= 0 && SnoozeIndex < SnoozeChoices.Length
            ? SnoozeChoices[SnoozeIndex]
            : StrAlarm.DefaultSnooze;

        return new AlarmValues(
            AlarmEnabled,
            AlarmTime?.Hours ?? 0,
            AlarmTime?.Minutes ?? 0,
            string.Join(",", SelectedDays()),
            KeyIndex >= 0 && KeyIndex < _keySlots.Count ? _keySlots[KeyIndex] : 0,
            UseAlarmVolume ? Math.Clamp(Level(AlarmVolume), 1, 100) : 0,
            autoOff,
            snooze);
    }

    private static List<int> ParseDays(string days)
        => days.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(d => int.Parse(d, CultureInfo.InvariantCulture))
            .ToList();

    // ---------------------------------------------------------------- Volume de départ

    [RelayCommand]
    private async Task SaveStartVolumeAsync()
    {
        var agent = _agent;

        if (agent is null || !StartVolumeAvailable)
        {
            return;
        }

        var level = StartVolumeOn ? Math.Clamp(Level(StartVolumeLevel), 1, 100) : 0;

        IsBusy = true;

        try
        {
            await agent.SetStartVolumeAsync(level).ConfigureAwait(true);
            ShowInfo(level == 0
                ? Localization.Get("S_StartVolumeOff")
                : Localization.Get("S_StartVolumeSet", level));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ---------------------------------------------------------------- Outils

    private void SetDays(IEnumerable<int> days)
    {
        var set = days.ToHashSet();
        Sunday = set.Contains(0);
        Monday = set.Contains(1);
        Tuesday = set.Contains(2);
        Wednesday = set.Contains(3);
        Thursday = set.Contains(4);
        Friday = set.Contains(5);
        Saturday = set.Contains(6);
    }

    private List<int> SelectedDays()
    {
        var days = new List<int>();

        if (Sunday) days.Add(0);
        if (Monday) days.Add(1);
        if (Tuesday) days.Add(2);
        if (Wednesday) days.Add(3);
        if (Thursday) days.Add(4);
        if (Friday) days.Add(5);
        if (Saturday) days.Add(6);

        return days;
    }

    /// <summary>
    /// Les touches proposées : 1 à 6, plus 7 à 12 quand l'agent a la seconde rangée.
    /// La touche du réveil modifié est toujours dans la liste, même hors de ces bornes.
    /// </summary>
    private void BuildKeyLabels(int keep = 0)
    {
        var limit = _hasSecondRow ? PresetKeys.Total : PresetKeys.PerRow;
        var slots = Enumerable.Range(1, limit).ToList();

        if (keep is >= 1 and <= PresetKeys.Total && !slots.Contains(keep))
        {
            slots.Add(keep);
        }

        // Garde l'emplacement d'une liste déjà construite (changement de langue).
        if (keep == 0)
        {
            foreach (var slot in _keySlots.Where(s => !slots.Contains(s)))
            {
                slots.Add(slot);
            }
        }

        _keySlots = slots.OrderBy(s => s).ToList();
        KeyLabels.Clear();

        foreach (var slot in _keySlots)
        {
            KeyLabels.Add(KeyText(slot));
        }
    }

    private void BuildAutoOffLabels(int? keep = null)
    {
        var choices = StandardAutoOff.ToList();

        if (keep is int minutes && minutes > 0 && !choices.Contains(minutes))
        {
            choices.Add(minutes);
        }

        if (keep is null)
        {
            foreach (var minutes2 in _autoOffChoices.Where(m => !choices.Contains(m)))
            {
                choices.Add(minutes2);
            }
        }

        _autoOffChoices = choices.OrderBy(m => m).ToList();
        AutoOffLabels.Clear();

        foreach (var value in _autoOffChoices)
        {
            AutoOffLabels.Add(value == 0
                ? Localization.Get("L_AlarmAutoOffNever")
                : Localization.Get("L_AlarmAutoOffMinutes", value));
        }
    }

    private void BuildSnoozeLabels()
    {
        SnoozeLabels.Clear();

        foreach (var minutes in SnoozeChoices)
        {
            SnoozeLabels.Add(Localization.Get("L_AlarmSnoozeMinutes", minutes));
        }
    }

    private string KeyText(int slot)
    {
        var key = Localization.Get("L_AlarmKey", PresetKeys.Label(slot));

        return _keyNames.TryGetValue(slot, out var name)
            ? $"{key} — {name}"
            : $"{key} — {Localization.Get("L_AlarmKeyEmpty")}";
    }

    private static string DaysText(IReadOnlyList<int> days)
    {
        var set = days.ToHashSet();

        if (set.Count == 7)
        {
            return Localization.Get("L_AlarmEveryDay");
        }

        if (set.SetEquals(new[] { 1, 2, 3, 4, 5 }))
        {
            return Localization.Get("L_AlarmWeekdays");
        }

        if (set.SetEquals(new[] { 0, 6 }))
        {
            return Localization.Get("L_AlarmWeekend");
        }

        var names = Localization.CultureOf(Localization.Current).DateTimeFormat.AbbreviatedDayNames;
        return string.Join(" ", WeekOrder.Where(set.Contains).Select(d => names[d]));
    }

    private static string TimeText(int hour, int minute)
        => $"{hour:00}:{minute:00}";

    private static int Level(double value) => (int)Math.Round(value, MidpointRounding.AwayFromZero);

    /// <summary>Le fuseau IANA du téléphone (« Europe/Brussels »), ou null s'il n'est pas connu.</summary>
    private static string? PhoneZone()
    {
        try
        {
            var id = TimeZoneInfo.Local.Id;

            if (id.Contains('/'))
            {
                return id;
            }

            return TimeZoneInfo.TryConvertWindowsIdToIanaId(id, out var iana) && iana.Contains('/') ? iana : null;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
