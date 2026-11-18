using System.Net;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SoundReborn.Core;
using SoundReborn.Core.Models;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.ViewModels;

/// <summary>
/// La carte « Copier les touches » de l'onglet Présélections : copie toutes les
/// touches de l'enceinte sélectionnée vers une autre enceinte STR, ou vers toutes.
///
/// C'est l'agent de l'enceinte source qui fait le travail (POST
/// /api/box/preset-copy, agent 1.6.5 et suivants), comme le « Transfert » de
/// l'application STR pour PC : il écrit les touches sur chaque cible, les deux
/// rangées quand la cible en a deux, et transmet le compte Spotify de la source
/// quand une touche Spotify fait partie de la copie. La question de confirmation
/// le dit, puisque c'est un compte qui change d'enceinte.
///
/// Sa propre vue-modèle, comme les réveils dans Réglages : rien ne change dans
/// celle des présélections.
/// </summary>
public sealed partial class PresetCopyViewModel : BaseViewModel
{
    public PresetCopyViewModel(SpeakerManager speakers) : base(speakers)
    {
        Speakers.DeviceChanged += (_, _) => OnMainThread(Refresh);
        Speakers.KnownSpeakersChanged += (_, _) => OnMainThread(Refresh);
        Localization.LanguageChanged += (_, _) => OnMainThread(Refresh);
        Refresh();
    }

    /// <summary>Visible dès que l'enceinte sélectionnée a l'agent STR.</summary>
    [ObservableProperty]
    private bool _showCard;

    [ObservableProperty]
    private bool _canCopy;

    /// <summary>Ce que fait la copie, ou pourquoi elle n'est pas possible.</summary>
    [ObservableProperty]
    private string _hint = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowButton))]
    private bool _isCopying;

    public bool ShowButton => !IsCopying;

    /// <summary>Recalcule visibilité et indication. Sans réseau.</summary>
    public void Refresh()
    {
        var device = Device;
        ShowCard = device?.Str is not null;

        if (device?.Str is null)
        {
            CanCopy = false;
            Hint = "";
            return;
        }

        var name = device.Endpoint.DisplayName;

        if (device.AgentTooOldForPresetCopy)
        {
            CanCopy = false;
            Hint = Localization.Get("S_CopyTooOld", name);
        }
        else if (Targets(device).Count == 0)
        {
            CanCopy = false;
            Hint = Localization.Get("S_CopyNoOther");
        }
        else
        {
            CanCopy = !IsCopying;
            Hint = Localization.Get("L_CopyHint", name);
        }
    }

    [RelayCommand]
    private async Task CopyAsync()
    {
        Refresh();

        var device = Device;

        if (device?.Str is not { } str || IsCopying)
        {
            return;
        }

        if (!CanCopy)
        {
            ShowWarning(Hint);
            return;
        }

        var source = device.Endpoint.DisplayName;
        var targets = Targets(device);
        var labels = Labels(targets);
        var all = Localization.Get("L_CopyAll");
        var buttons = targets.Count > 1 ? labels.Append(all).ToArray() : labels.ToArray();

        var choice = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_CopyChoose", source),
            Localization.Get("L_Cancel"),
            null,
            buttons).ConfigureAwait(true);

        List<string>? chosen;
        string targetLabel;

        if (choice == all && targets.Count > 1)
        {
            chosen = null;
            targetLabel = Localization.Get("L_CopyAllTargets");
        }
        else
        {
            var index = Array.IndexOf(labels, choice);

            if (choice is null || index < 0)
            {
                return;
            }

            chosen = new List<string> { targets[index].Host };
            targetLabel = labels[index];
        }

        var go = Localization.Get("L_CopyGo");

        var confirm = await Shell.Current.DisplayActionSheetAsync(
            Localization.Get("L_CopyConfirm", targetLabel, source),
            Localization.Get("L_Cancel"),
            null,
            go).ConfigureAwait(true);

        if (confirm != go)
        {
            return;
        }

        IsCopying = true;
        CanCopy = false;
        IsError = false;
        StatusMessage = Localization.Get("S_CopyRunning");

        try
        {
            var result = await str.CopyPresetsAsync(chosen).ConfigureAwait(true);
            Report(result, source);
        }
        catch (SpeakerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            ShowWarning(Localization.Get("S_CopyTooOld", source));
        }
        catch (SpeakerException ex) when (StrRefusal.From(ex)?.Code is { } code && RefusalKey(code) is { } key)
        {
            ShowWarning(Localization.Get(key));
        }
        catch (OperationCanceledException)
        {
            // Délai dépassé : l'agent continue la copie de son côté.
            ShowWarning(Localization.Get("S_CopyTimeout"));
        }
        catch (Exception ex)
        {
            ShowError(ex);
        }
        finally
        {
            IsCopying = false;
            Refresh();
        }
    }

    // ------------------------------------------------------------------ interne

    /// <summary>Les autres enceintes connues qui ont l'agent STR, dans l'ordre de la liste.</summary>
    private List<SpeakerEndpoint> Targets(SpeakerDevice device)
        => Speakers.KnownSpeakers
            .Where(e => e.HasStr && !string.Equals(e.Host, device.Host, StringComparison.OrdinalIgnoreCase))
            .ToList();

    /// <summary>Le nom de chaque cible ; deux enceintes du même nom sont départagées par leur adresse.</summary>
    private static string[] Labels(List<SpeakerEndpoint> targets)
    {
        var names = targets.Select(t => t.DisplayName).ToList();

        return targets
            .Select((t, i) => names.Count(n => n == names[i]) > 1 ? $"{t.DisplayName} ({t.Host})" : t.DisplayName)
            .ToArray();
    }

    private void Report(StrPresetCopyResult result, string source)
    {
        if (result.Results.Count == 0)
        {
            ShowWarning(Localization.Get("S_CopyNoTargets"));
            return;
        }

        var lines = result.Results.Select(t => Line(t, source)).ToList();
        IsError = result.Results.Any(t => !t.Ok);
        StatusMessage = string.Join("\n", lines);
    }

    private string Line(StrPresetCopyTarget target, string source)
    {
        var name = NameOf(target);

        if (target.Ok)
        {
            var line = Localization.Get("S_CopyDone", name, target.Copied);

            if (target.Row1Only || target.NotCopied > 0)
            {
                line += " " + Localization.Get("S_CopyRow1Only");
            }

            if (target.Spotify)
            {
                line += " " + Localization.Get("S_CopySpotify");
            }

            return line;
        }

        return target.Code switch
        {
            StrPresetCopyTarget.AlreadyOnSlot when !string.IsNullOrWhiteSpace(target.ConflictName) && target.ConflictSlot > 0
                => Localization.Get("S_CopyConflict", name, target.ConflictName!, target.ConflictSlot),
            StrPresetCopyTarget.Unreachable => Localization.Get("S_CopyUnreachable", name),
            StrPresetCopyTarget.Refused => Localization.Get("S_CopyRefused", name),
            StrPresetCopyTarget.UnknownSpeaker => Localization.Get("S_CopyUnknown", name, source),
            _ => Localization.Get("S_CopyFailed", name, string.IsNullOrWhiteSpace(target.Error) ? target.Code ?? "?" : target.Error!),
        };
    }

    /// <summary>Le nom donné par l'agent, sinon celui que l'application connaît pour cette adresse, sinon l'adresse.</summary>
    private string NameOf(StrPresetCopyTarget target)
    {
        if (!string.IsNullOrWhiteSpace(target.Name))
        {
            return target.Name;
        }

        var known = Speakers.KnownSpeakers.FirstOrDefault(e =>
            string.Equals(e.Host, target.Ip, StringComparison.OrdinalIgnoreCase));

        return known?.DisplayName ?? target.Ip;
    }

    private static string? RefusalKey(string code) => code switch
    {
        "busy" => "S_CopyBusy",
        "no-presets" => "S_CopyNoPresets",
        "no-targets" => "S_CopyNoTargets",
        _ => null,
    };

    private void ShowWarning(string message)
    {
        IsError = true;
        StatusMessage = message;
    }
}
