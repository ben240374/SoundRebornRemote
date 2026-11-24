using CommunityToolkit.Mvvm.ComponentModel;
using SoundReborn.Core;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

public abstract partial class BaseViewModel : ObservableObject
{
    protected BaseViewModel(SpeakerManager speakers)
    {
        Speakers = speakers;
    }

    protected SpeakerManager Speakers { get; }

    protected SpeakerDevice? Device => Speakers.Current;

    [ObservableProperty]
    private bool _isBusy;

    /// <summary>Message affiché en bas de page : erreur, confirmation, ou vide.</summary>
    [ObservableProperty]
    private string? _statusMessage;

    [ObservableProperty]
    private bool _isError;

    public bool HasNoSpeaker => Device is null;

    protected void ShowError(Exception ex)
    {
        // Un refus de l'agent qui porte un code connu se dit en clair, dans la
        // langue de l'utilisateur. Tout le reste suit le chemin habituel.
        var message = DescribeRefusal(ex) ?? ex switch
        {
            SpeakerException ste => ste.Message,
            TaskCanceledException => Localization.Get("S_Timeout"),
            HttpRequestException => Localization.Get("S_Unreachable"),
            _ => ex.Message,
        };

        IsError = true;
        StatusMessage = message;
    }

    /// <summary>
    /// Traduit les refus de l'agent STR que l'on sait nommer. Null pour tout le
    /// reste, y compris une erreur qui ne vient pas de l'agent.
    /// </summary>
    private static string? DescribeRefusal(Exception ex)
    {
        var refusal = StrRefusal.From(ex);

        if (refusal is null)
        {
            return null;
        }

        if (refusal.Code == StrRefusal.AlreadyOnSlot && refusal.Slot > 0)
        {
            var key = PresetKeys.Label(refusal.Slot);

            return string.IsNullOrWhiteSpace(refusal.Name)
                ? Localization.Get("S_AlreadyOnKey", key)
                : Localization.Get("S_AlreadyOnKeyNamed", refusal.Name!, key);
        }

        if (refusal.Code == StrRefusal.LibraryServerOffline)
        {
            return LibraryOfflineMessage(refusal.Server);
        }

        if (refusal.Code == StrRefusal.PairCannotLead)
        {
            return Localization.Get("S_StereoCannotExtend");
        }

        return null;
    }

    /// <summary>L'agent de cette enceinte est antérieur à la version minimale : pas de modification de groupe.</summary>
    protected static string AgentTooOldForGroups(SpeakerDevice device)
        => Localization.Get("S_AgentTooOldGroups", device.AgentVersionText, AgentRequirement.MinimumLabel);

    /// <summary>Le serveur de médias de la bibliothèque est éteint ou introuvable.</summary>
    protected static string LibraryOfflineMessage(string? server)
        => string.IsNullOrWhiteSpace(server)
            ? Localization.Get("S_LibraryOffline")
            : Localization.Get("S_LibraryOfflineNamed", server!);

    protected void ShowInfo(string? message)
    {
        IsError = false;
        StatusMessage = message;
    }

    /// <summary>
    /// Exécute une commande vers l'enceinte en absorbant les erreurs réseau :
    /// une télécommande ne doit jamais se fermer parce que le Wi-Fi a hoqueté.
    /// </summary>
    protected async Task RunAsync(Func<SpeakerDevice, Task> action, string? successMessage = null)
    {
        var device = Device;

        if (device is null)
        {
            IsError = true;
            StatusMessage = Localization.Get("S_NoSpeaker");
            return;
        }

        try
        {
            await action(device).ConfigureAwait(true);
            ShowInfo(successMessage);
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
    }

    /// <summary>Un refus de l'agent que <see cref="ShowError"/> sait dire en clair (déjà sur une touche…).</summary>
    protected static bool IsKnownRefusal(Exception ex) => DescribeRefusal(ex) is not null;

    /// <summary>
    /// La phrase de l'agent quand il en donne une (« error » de son corps de
    /// réponse), sinon le message habituel. Jamais vide.
    /// </summary>
    protected static string AgentMessage(Exception ex)
    {
        if (ex is SpeakerException { ResponseBody: { } body } && !string.IsNullOrWhiteSpace(body))
        {
            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(body);

                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object &&
                    document.RootElement.TryGetProperty("error", out var error) &&
                    error.ValueKind == System.Text.Json.JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(error.GetString()))
                {
                    return error.GetString()!.Trim();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Corps en texte simple : on le prend tel quel s'il est court.
                var text = body.Trim();

                if (text.Length is > 0 and <= 200)
                {
                    return text;
                }
            }
        }

        return ex switch
        {
            TaskCanceledException => Localization.Get("S_Timeout"),
            HttpRequestException => Localization.Get("S_Unreachable"),
            _ => ex.Message,
        };
    }

    /// <summary>Force l'exécution sur le fil de l'interface (les événements WebSocket arrivent d'un fil de fond).</summary>
    protected static void OnMainThread(Action action)
    {
        if (MainThread.IsMainThread)
        {
            action();
        }
        else
        {
            MainThread.BeginInvokeOnMainThread(action);
        }
    }
}
