using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Voice;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>Un texte gardé pour le message vocal, tel qu'affiché dans la liste.</summary>
public sealed partial class SavedVoiceText : ObservableObject
{
    [ObservableProperty]
    private string _text = "";
}

/// <summary>
/// La carte « Message vocal » de l'onglet Lecture.
///
///   – Ma voix : on maintient le bouton rond pour parler, on le relâche pour
///     envoyer. Le téléphone sert le clip (AAC), l'agent STR de l'enceinte vient le
///     chercher (POST /api/announce avec « url »), le joue, puis reprend ce que
///     l'enceinte jouait.
///   – Texte lu : l'enceinte lit le texte avec la voix de Google Translate, dans la
///     langue de l'application. Elle doit avoir accès à internet, et le texte passe
///     par Google. Dix textes au plus se gardent dans le téléphone, pour être relus,
///     modifiés ou supprimés.
///
/// Le message part vers l'enceinte qui reçoit les ordres. L'annonce sur toutes les
/// enceintes d'un groupe attend l'agent STR : c'est sur sa liste de souhaits
/// (discussion #2007), et l'envoyer à chaque enceinte d'un groupe risquerait de le
/// défaire.
///
/// Sa propre vue-modèle, comme les boutons Spotify : rien ne change dans celle
/// de la lecture.
/// </summary>
public sealed partial class VoiceMessageViewModel : BaseViewModel
{
    /// <summary>Clé des préférences où vivent les textes gardés (tableau JSON de chaînes).</summary>
    public const string TextsKey = "voice_texts";

    /// <summary>Clé des préférences du volume des annonces (0 : celui de l'enceinte).</summary>
    public const string VolumeKey = "voice_volume";

    /// <summary>Plafond d'un message : bien en deçà des 8 Mo que l'agent accepte.</summary>
    public static TimeSpan MaxDuration { get; set; } = TimeSpan.FromSeconds(60);

    /// <summary>En deçà, ce n'est pas un message : un appui trop bref sur le bouton.</summary>
    public static TimeSpan MinDuration { get; set; } = TimeSpan.FromMilliseconds(700);

    private readonly IVoiceRecorder _recorder;
    private readonly string _directory;
    private readonly TimeProvider _clock;
    private readonly List<string> _texts;

    private DateTimeOffset _startedAt;
    private string? _clipPath;
    private CancellationTokenSource? _ticker;

    /// <summary>
    /// Vrai tant que le doigt est sur le bouton. La demande d'accès au micro peut
    /// s'afficher entre l'appui et le relâchement : si le doigt est déjà levé
    /// quand elle se referme, rien n'est enregistré.
    /// </summary>
    private volatile bool _holding;

    /// <summary>
    /// 1 pendant un enregistrement, remis à 0 par celui qui l'arrête : le
    /// relâchement et le décompte d'une minute peuvent arriver ensemble, un seul
    /// envoie.
    /// </summary>
    private int _armed;

    /// <summary>
    /// Le dernier message qui n'a pas pu partir, pour « Réessayer » : le clip (gardé
    /// en mémoire seulement, jamais sur le disque) ou le texte. L'un ou l'autre.
    /// </summary>
    private byte[]? _retryClip;

    private string? _retryText;

    public VoiceMessageViewModel(SpeakerManager speakers, IVoiceRecorder recorder)
        : this(speakers, recorder, Path.Combine(FileSystem.CacheDirectory, "voice"), TimeProvider.System)
    {
    }

    public VoiceMessageViewModel(SpeakerManager speakers, IVoiceRecorder recorder, string directory, TimeProvider clock)
        : base(speakers)
    {
        _recorder = recorder;
        _directory = directory;
        _clock = clock;
        _texts = LoadTexts();
        _announceVolume = LoadVolume();

        foreach (var text in _texts)
        {
            Texts.Add(new SavedVoiceText { Text = text });
        }

        Speakers.DeviceChanged += (_, _) => OnMainThread(Refresh);
        Localization.LanguageChanged += (_, _) => OnMainThread(Refresh);
        Refresh();
    }

    /// <summary>La carte n'apparaît qu'avec une enceinte qui a l'agent STR.</summary>
    [ObservableProperty]
    private bool _showCard;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSpeak), nameof(HoldHint))]
    private bool _isRecording;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecord), nameof(CanSpeak))]
    private bool _isSending;

    /// <summary>« 0:07 / 1:00 » pendant l'enregistrement, vide sinon.</summary>
    [ObservableProperty]
    private string _elapsedText = "";

    /// <summary>Le texte en cours de saisie.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSpeak), nameof(CanKeep))]
    private string _text = "";

    /// <summary>
    /// Volume des annonces, voix et texte : 0 garde celui de l'enceinte, 5 à 100 le
    /// fixe le temps du message. L'agent baisse d'abord la musique, met ce volume,
    /// joue le message, puis remet le volume et la musique d'avant.
    /// </summary>
    /// <remarks>Un double, comme la valeur du curseur auquel il est lié.</remarks>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(VolumeLabel))]
    private double _announceVolume;

    /// <summary>Le volume des annonces en pourcentage entier, 0 pour celui de l'enceinte.</summary>
    public int AnnounceVolumePercent => (int)Math.Round(AnnounceVolume);

    /// <summary>« Volume des annonces : 60 % », ou « … : celui de l'enceinte ».</summary>
    public string VolumeLabel => AnnounceVolumePercent <= 0
        ? Localization.Get("L_VoiceVolumeSpeaker")
        : Localization.Get("L_VoiceVolumeValue", AnnounceVolumePercent);

    /// <summary>Le volume envoyé à l'agent : null pour celui de l'enceinte.</summary>
    private int? Volume => AnnounceVolumePercent > 0 ? AnnounceVolumePercent : null;

    /// <summary>
    /// Levé quand le micro tourne vraiment : la page le signale d'une vibration,
    /// pour qu'on ne parle pas avant que l'enregistrement ait commencé.
    /// </summary>
    public event EventHandler? RecordingStarted;

    /// <summary>Vrai quand le dernier message n'est pas parti et peut être renvoyé tel quel.</summary>
    [ObservableProperty]
    private bool _canRetry;

    /// <summary>Les textes gardés, dans l'ordre où ils ont été ajoutés.</summary>
    public ObservableCollection<SavedVoiceText> Texts { get; } = new();

    /// <summary>Vrai quand l'appareil a un micro que l'application peut utiliser.</summary>
    public bool RecorderSupported => _recorder.IsSupported;

    public bool CanRecord => RecorderSupported && !IsSending;

    public bool CanSpeak => !IsRecording && !IsSending && !string.IsNullOrWhiteSpace(Text);

    public bool CanKeep => !string.IsNullOrWhiteSpace(Text);

    public bool HasTexts => Texts.Count > 0;

    /// <summary>Sous le bouton rond : comment s'en servir, ou que relâcher envoie.</summary>
    public string HoldHint => Localization.Get(IsRecording ? "L_VoiceReleaseHint" : "L_VoiceHoldHint");

    /// <summary>Recalcule la visibilité et les libellés. Sans réseau.</summary>
    public void Refresh()
    {
        ShowCard = Device?.Str is not null;
        OnPropertyChanged(nameof(HoldHint));
        OnPropertyChanged(nameof(VolumeLabel));
    }

    /// <summary>Le curseur avance par pas de 5 ; la valeur est gardée pour les prochaines fois.</summary>
    partial void OnAnnounceVolumeChanged(double value)
    {
        var stepped = Math.Clamp((int)Math.Round(value / 5.0) * 5, 0, 100);

        if (Math.Abs(stepped - value) > 0.001)
        {
            AnnounceVolume = stepped;
            return;
        }

        try
        {
            Preferences.Default.Set(VolumeKey, stepped);
        }
        catch (Exception)
        {
            // Le réglage vaut pour cette session.
        }
    }

    // ================================================================ Ma voix : maintenir pour parler

    /// <summary>
    /// Le doigt se pose sur le bouton : l'enregistrement commence, après la demande
    /// d'accès au micro la toute première fois.
    /// </summary>
    public async Task PressAsync()
    {
        if (IsRecording || IsSending || !RecorderSupported)
        {
            return;
        }

        _holding = true;
        ForgetRetry();

        if (Agent() is null)
        {
            return;
        }

        if (!await _recorder.EnsurePermissionAsync().ConfigureAwait(true))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceNoMic");
            return;
        }

        if (!_holding)
        {
            // Le doigt a été levé pendant la demande d'autorisation : on explique le
            // geste plutôt que d'enregistrer un message que personne ne dit.
            ShowInfo(Localization.Get("S_VoiceHold"));
            return;
        }

        var path = Path.Combine(_directory, "message." + VoiceFormat.Extension);

        try
        {
            _recorder.Start(path);
        }
        catch (Exception)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceMicFailed");
            return;
        }

        _clipPath = path;
        _startedAt = _clock.GetUtcNow();
        Interlocked.Exchange(ref _armed, 1);
        IsRecording = true;
        ShowInfo(Localization.Get("S_VoiceRecording"));
        UpdateElapsed();
        StartTicker();
        RecordingStarted?.Invoke(this, EventArgs.Empty);

        // Relâché pendant le démarrage du micro : le relâchement n'a rien trouvé à
        // arrêter, on le fait maintenant.
        if (!_holding)
        {
            await StopAndSendAsync().ConfigureAwait(true);
        }
    }

    /// <summary>Le doigt quitte le bouton : l'enregistrement s'arrête et part vers l'enceinte.</summary>
    public async Task ReleaseAsync()
    {
        _holding = false;

        if (IsRecording)
        {
            await StopAndSendAsync().ConfigureAwait(true);
        }
    }

    /// <summary>
    /// Abandonne l'enregistrement en cours sans rien envoyer : la page se ferme, ou
    /// l'application passe en arrière-plan le doigt encore posé.
    /// </summary>
    [RelayCommand]
    private void CancelRecording()
    {
        _holding = false;

        if (Interlocked.Exchange(ref _armed, 0) == 0)
        {
            return;
        }

        StopTicker();
        _recorder.Cancel();
        IsRecording = false;
        ElapsedText = "";
        DeleteClip(_clipPath);
        _clipPath = null;
        ShowInfo(Localization.Get("S_VoiceCancelled"));
    }

    /// <summary>Appelée par le décompte : la minute atteinte envoie le message tel quel.</summary>
    public async Task TickAsync()
    {
        if (!IsRecording)
        {
            return;
        }

        UpdateElapsed();

        if (_clock.GetUtcNow() - _startedAt >= MaxDuration)
        {
            await StopAndSendAsync().ConfigureAwait(true);
        }
    }

    private async Task StopAndSendAsync()
    {
        if (Interlocked.Exchange(ref _armed, 0) == 0)
        {
            return;
        }

        StopTicker();

        var duration = _clock.GetUtcNow() - _startedAt;
        var path = _clipPath;
        _clipPath = null;
        IsRecording = false;
        ElapsedText = "";

        if (duration < MinDuration)
        {
            // Un appui bref : on rappelle le geste, rien ne part.
            _recorder.Cancel();
            DeleteClip(path);
            ShowInfo(Localization.Get("S_VoiceHold"));
            return;
        }

        if (!_recorder.Stop() || path is null || !File.Exists(path))
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceNothing");
            return;
        }

        byte[] clip;

        try
        {
            clip = await File.ReadAllBytesAsync(path).ConfigureAwait(true);
        }
        catch (Exception)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceNothing");
            return;
        }
        finally
        {
            // Le message n'a rien à faire sur le téléphone une fois lu en mémoire.
            DeleteClip(path);
        }

        await SendClipAsync(clip).ConfigureAwait(true);
    }

    private async Task SendClipAsync(byte[] clip)
    {
        if (clip.Length > VoiceAnnouncer.MaxClipBytes)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceTooBig");
            return;
        }

        var agent = Agent();

        if (agent is null)
        {
            return;
        }

        IsSending = true;
        ShowInfo(Localization.Get("S_VoiceSending"));

        try
        {
            var outcome = await VoiceAnnouncer.SendClipAsync(agent, clip, Volume).ConfigureAwait(true);

            switch (outcome)
            {
                case VoiceOutcome.Delivered:
                    ForgetRetry();
                    ShowInfo(Localization.Get("S_VoiceSent", SpeakerName()));
                    break;
                case VoiceOutcome.NoNetwork:
                    KeepForRetry(clip, null);
                    IsError = true;
                    StatusMessage = Localization.Get("S_VoiceNoNetwork");
                    break;
                default:
                    KeepForRetry(clip, null);
                    IsError = true;
                    StatusMessage = Localization.Get("S_VoiceNotFetched");
                    break;
            }
        }
        catch (Exception ex)
        {
            ShowAnnounceError(ex, clip, null);
        }
        finally
        {
            IsSending = false;
        }
    }

    // ================================================================ Texte lu

    /// <summary>Fait lire par l'enceinte le texte en cours de saisie.</summary>
    [RelayCommand]
    private Task SpeakAsync() => SpeakTextAsync(Text);

    /// <summary>Fait lire par l'enceinte un texte gardé.</summary>
    [RelayCommand]
    private Task SpeakSavedAsync(SavedVoiceText? saved)
        => saved is null ? Task.CompletedTask : SpeakTextAsync(saved.Text);

    private async Task SpeakTextAsync(string? raw)
    {
        var text = VoiceTextList.Clean(raw);

        if (text.Length == 0 || IsRecording || IsSending)
        {
            return;
        }

        ForgetRetry();

        var agent = Agent();

        if (agent is null)
        {
            return;
        }

        IsSending = true;
        ShowInfo(Localization.Get("S_VoiceSending"));

        try
        {
            var lang = Localization.CultureOf(Localization.Current).TwoLetterISOLanguageName;
            await agent.AnnounceAsync(text, lang, null, Volume).ConfigureAwait(true);
            ForgetRetry();
            ShowInfo(Localization.Get("S_VoiceSpoken", SpeakerName()));
        }
        catch (Exception ex)
        {
            ShowAnnounceError(ex, null, text);
        }
        finally
        {
            IsSending = false;
        }
    }

    // ================================================================ Textes gardés

    /// <summary>Garde le texte en cours de saisie, pour le relire plus tard (dix au plus).</summary>
    [RelayCommand]
    private void KeepText()
    {
        var text = VoiceTextList.Clean(Text);

        if (text.Length == 0)
        {
            return;
        }

        if (VoiceTextList.Contains(_texts, text))
        {
            ShowInfo(Localization.Get("S_VoiceTextAlready"));
            return;
        }

        if (_texts.Count >= VoiceTextList.MaxCount)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceTextsFull", VoiceTextList.MaxCount);
            return;
        }

        _texts.Add(text);

        if (!SaveTexts())
        {
            return;
        }

        Texts.Add(new SavedVoiceText { Text = text });
        OnPropertyChanged(nameof(HasTexts));
        Text = "";
        ShowInfo(Localization.Get("S_VoiceTextKept"));
    }

    /// <summary>Modifie un texte gardé. Annuler, ne rien changer ou tout effacer n'enregistre rien.</summary>
    [RelayCommand]
    private async Task EditTextAsync(SavedVoiceText? saved)
    {
        if (saved is null)
        {
            return;
        }

        var current = saved.Text;

        var typed = await Shell.Current.DisplayPromptAsync(
            Localization.Get("L_VoiceTextEditTitle"),
            Localization.Get("L_VoiceTextEditMessage"),
            Localization.Get("L_VoiceTextEditGo"),
            Localization.Get("L_Cancel"),
            maxLength: VoiceTextList.MaxLength,
            initialValue: current).ConfigureAwait(true);

        var text = VoiceTextList.Clean(typed);

        if (text.Length == 0 || string.Equals(text, current, StringComparison.Ordinal))
        {
            return;
        }

        var index = _texts.FindIndex(t => string.Equals(t, current, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        // Un autre texte gardé dit déjà la même chose : pas de doublon.
        if (_texts.Where((t, i) => i != index).Any(t => string.Equals(t, text, StringComparison.OrdinalIgnoreCase)))
        {
            ShowInfo(Localization.Get("S_VoiceTextAlready"));
            return;
        }

        _texts[index] = text;

        if (!SaveTexts())
        {
            _texts[index] = current;
            return;
        }

        saved.Text = text;
        ShowInfo(Localization.Get("S_VoiceTextEdited"));
    }

    /// <summary>Supprime un texte gardé, après confirmation.</summary>
    [RelayCommand]
    private async Task DeleteTextAsync(SavedVoiceText? saved)
    {
        if (saved is null)
        {
            return;
        }

        var go = Localization.Get("L_VoiceTextDeleteGo");

        var answer = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_VoiceTextDeleteConfirm", saved.Text),
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        if (answer != go)
        {
            return;
        }

        var index = _texts.FindIndex(t => string.Equals(t, saved.Text, StringComparison.Ordinal));

        if (index < 0)
        {
            return;
        }

        var removed = _texts[index];
        _texts.RemoveAt(index);

        if (!SaveTexts())
        {
            _texts.Insert(index, removed);
            return;
        }

        Texts.Remove(saved);
        OnPropertyChanged(nameof(HasTexts));
        ShowInfo(Localization.Get("S_VoiceTextDeleted"));
    }

    /// <summary>
    /// Ajoute les textes d'une sauvegarde à ceux du téléphone, sans doublon et sans
    /// dépasser dix. Rend le nombre de textes ajoutés.
    /// </summary>
    public int ImportTexts(IEnumerable<string> texts)
    {
        var before = _texts.Count;

        foreach (var text in VoiceTextList.Sanitize(texts))
        {
            if (_texts.Count >= VoiceTextList.MaxCount)
            {
                break;
            }

            if (!VoiceTextList.Contains(_texts, text))
            {
                _texts.Add(text);
            }
        }

        var added = _texts.Count - before;

        if (added == 0)
        {
            return 0;
        }

        if (!SaveTexts())
        {
            _texts.RemoveRange(before, added);
            return 0;
        }

        foreach (var text in _texts.Skip(before))
        {
            Texts.Add(new SavedVoiceText { Text = text });
        }

        OnPropertyChanged(nameof(HasTexts));
        return added;
    }

    /// <summary>Les textes gardés, pour la sauvegarde.</summary>
    public IReadOnlyList<string> SavedTexts => _texts.ToList();

    private static int LoadVolume()
    {
        try
        {
            return Math.Clamp(Preferences.Default.Get(VolumeKey, 0), 0, 100);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    private static List<string> LoadTexts()
    {
        try
        {
            return VoiceTextList.Parse(Preferences.Default.Get(TextsKey, string.Empty));
        }
        catch (Exception)
        {
            // Liste illisible : on repart d'une liste vide plutôt que de bloquer la carte.
            return new List<string>();
        }
    }

    /// <summary>Enregistre la liste dans les préférences. Faux, avec le message, quand c'est impossible.</summary>
    private bool SaveTexts()
    {
        try
        {
            Preferences.Default.Set(TextsKey, VoiceTextList.Serialize(_texts));
            return true;
        }
        catch (Exception)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_LocalNotSaved");
            return false;
        }
    }

    // ================================================================ Outils

    /// <summary>L'agent de l'enceinte qui reçoit les ordres, ou null avec le message qui dit pourquoi.</summary>
    private StrApiClient? Agent()
    {
        var device = Device;

        if (device is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return null;
        }

        if (device.Str is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceNeedAgent");
            return null;
        }

        return device.Str;
    }

    private string SpeakerName() => Device?.Endpoint.DisplayName ?? "";

    // ================================================================ Réessayer

    /// <summary>Renvoie tel quel le dernier message qui n'est pas parti : le même clip, ou le même texte.</summary>
    [RelayCommand]
    private async Task RetryAsync()
    {
        if (!CanRetry || IsRecording || IsSending)
        {
            return;
        }

        var clip = _retryClip;
        var text = _retryText;

        if (clip is not null)
        {
            await SendClipAsync(clip).ConfigureAwait(true);
        }
        else if (text is not null)
        {
            await SpeakTextAsync(text).ConfigureAwait(true);
        }
    }

    private void KeepForRetry(byte[]? clip, string? text)
    {
        _retryClip = clip;
        _retryText = clip is null ? text : null;
        CanRetry = _retryClip is not null || _retryText is not null;
    }

    private void ForgetRetry()
    {
        _retryClip = null;
        _retryText = null;
        CanRetry = false;
    }

    /// <summary>
    /// Dit pourquoi l'annonce n'est pas partie et, quand c'est sans doute passager,
    /// garde le message pour « Réessayer ». Un agent qui ne connaît pas les
    /// annonces ne gagnerait rien à un nouvel essai.
    /// </summary>
    private void ShowAnnounceError(Exception ex, byte[]? clip, string? text)
    {
        if (AnnounceFailure.IsUnsupported(ex))
        {
            ForgetRetry();
            IsError = true;
            StatusMessage = Localization.Get("S_VoiceUnsupported");
            return;
        }

        KeepForRetry(clip, text);

        if (AnnounceFailure.IsFetchFailure(ex))
        {
            // 502 : l'agent n'a pas obtenu le son — la voix de Google pour un texte,
            // le message servi par le téléphone pour la voix.
            var detail = AnnounceFailure.Detail(ex);
            IsError = true;
            StatusMessage = (clip is not null, detail is not null) switch
            {
                (true, true) => Localization.Get("S_VoiceClipFetchFailed", detail!),
                (true, false) => Localization.Get("S_VoiceClipFetchFailedPlain"),
                (false, true) => Localization.Get("S_VoiceTextFetchFailed", detail!),
                _ => Localization.Get("S_VoiceTextFetchFailedPlain"),
            };
            return;
        }

        ShowError(ex);
    }

    private void UpdateElapsed()
    {
        var elapsed = _clock.GetUtcNow() - _startedAt;

        if (elapsed < TimeSpan.Zero)
        {
            elapsed = TimeSpan.Zero;
        }

        if (elapsed > MaxDuration)
        {
            elapsed = MaxDuration;
        }

        ElapsedText = $"{Clock(elapsed)} / {Clock(MaxDuration)}";
    }

    private static string Clock(TimeSpan value)
        => $"{(int)value.TotalMinutes}:{value.Seconds:00}";

    private static void DeleteClip(string? path)
    {
        if (path is null)
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Sans importance : le fichier sera écrasé au prochain message.
        }
    }

    /// <summary>Décompte de l'enregistrement : quatre fois par seconde, sur le fil de l'interface.</summary>
    private void StartTicker()
    {
        StopTicker();
        var cts = new CancellationTokenSource();
        _ticker = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                while (!cts.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(250), cts.Token).ConfigureAwait(false);
                    OnMainThread(() => _ = TickAsync());
                }
            }
            catch (OperationCanceledException)
            {
                // Arrêt demandé.
            }
        });
    }

    private void StopTicker()
    {
        var ticker = _ticker;
        _ticker = null;

        // Annulé sans être libéré : la boucle du décompte lit encore son jeton, et un
        // jeton sans minuterie n'a rien à libérer.
        ticker?.Cancel();
    }
}
