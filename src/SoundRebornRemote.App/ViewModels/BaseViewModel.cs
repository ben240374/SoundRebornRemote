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
        var message = ex switch
        {
            SpeakerException ste => ste.Message,
            TaskCanceledException => Localization.Get("S_Timeout"),
            HttpRequestException => Localization.Get("S_Unreachable"),
            _ => ex.Message,
        };

        IsError = true;
        StatusMessage = message;
    }

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
