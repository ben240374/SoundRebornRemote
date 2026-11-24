using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        // Les libellés doivent être dans le dictionnaire avant que la première page
        // ne se construise, sinon les {DynamicResource L_...} restent vides.
        Localization.ApplyToResources(Resources);

        // Le thème enregistré s'applique avant le premier rendu : pas de flash clair
        // au lancement quand l'utilisateur a choisi le mode foncé.
        ThemeService.Initialize(this);

        // Les raccourcis de l'icône suivent la langue de l'application.
        Localization.LanguageChanged += (_, _) => _ = PublishShortcutsAsync();
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell()) { Title = "SoundReborn Remote" };

        // Application à l'arrière-plan : l'écoute d'une station sur l'appareil s'arrête.
        window.Stopped += (_, _) =>
        {
            try
            {
                ServiceHelper.Get<ViewModels.PresetsViewModel>().StopPreview();
            }
            catch (Exception)
            {
                // Conteneur pas encore prêt : aucune écoute n'a pu commencer.
            }
        };

        // Retour dans l'application (après avoir choisi l'enceinte dans Spotify, par
        // exemple) : la page « Spotify & Deezer », si elle est ouverte, se relit.
        window.Resumed += (_, _) =>
        {
            try
            {
                var streaming = ServiceHelper.Get<ViewModels.SpotifyDeezerViewModel>();

                if (streaming.IsOpen)
                {
                    _ = streaming.RefreshCommand.ExecuteAsync(null);
                }
            }
            catch (Exception)
            {
                // Conteneur pas encore prêt : la page n'a pas pu être ouverte.
            }
        };

        // Raccourcis de l'icône : publiés dans la langue de l'application, et refaits
        // quand elle change.
        _ = PublishShortcutsAsync();

        return window;
    }

    // ---------------------------------------------------------- Raccourcis de l'icône

    /// <summary>
    /// Publie « Tout mettre en veille » et « Touche 1 » à « Touche 3 » dans le menu de
    /// l'icône (appui long). Sans prise en charge par le système : rien.
    /// </summary>
    private static async Task PublishShortcutsAsync()
    {
        try
        {
            if (!Microsoft.Maui.ApplicationModel.AppActions.Current.IsSupported)
            {
                return;
            }

            var actions = Services.ShortcutRunner.Definitions()
                .Select(d => new Microsoft.Maui.ApplicationModel.AppAction(d.Id, d.Title, d.Subtitle, d.Icon))
                .ToList();

            await Microsoft.Maui.ApplicationModel.AppActions.Current.SetAsync(actions).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Lanceur sans raccourcis, ou refus du système : l'application marche sans.
        }
    }

    // ---------------------------------------------------------- Texte partagé

    /// <summary>
    /// Un texte partagé vers l'application (« Partager » dans Deezer : il contient
    /// le lien). L'onglet Réglages s'affiche et la page « Spotify &amp; Deezer »
    /// s'ouvre, le lien déjà dans son champ. Au lancement à froid, l'interface
    /// n'existe pas encore : on l'attend quelques secondes.
    /// </summary>
    public static void HandleSharedText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return;
        }

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                for (var attempt = 0; attempt < 50 && Shell.Current is null; attempt++)
                {
                    await Task.Delay(100).ConfigureAwait(true);
                }

                if (Shell.Current is not { } shell)
                {
                    return;
                }

                if (shell.Navigation.ModalStack.Count == 0)
                {
                    await shell.GoToAsync("//settings").ConfigureAwait(true);
                }

                await Views.SpotifyDeezerPage.ShowAsync(shell.Navigation, text).ConfigureAwait(true);
            }
            catch (Exception)
            {
                // Un partage ne doit jamais fermer l'application.
            }
        });
    }

    /// <summary>
    /// Un raccourci de l'icône a été touché (MauiProgram : OnAppAction). L'action se
    /// fait en tâche de fond ; l'onglet Lecture s'affiche et en donne le compte rendu.
    /// </summary>
    public static void HandleAppAction(Microsoft.Maui.ApplicationModel.AppAction action)
    {
        var id = action?.Id;

        MainThread.BeginInvokeOnMainThread(async () =>
        {
            try
            {
                var (message, isError) = await ServiceHelper.Get<Services.ShortcutRunner>().RunAsync(id).ConfigureAwait(true);

                if (string.IsNullOrWhiteSpace(message))
                {
                    return;
                }

                if (Shell.Current is { } shell)
                {
                    await shell.GoToAsync("//player").ConfigureAwait(true);
                }

                ServiceHelper.Get<ViewModels.PlayerViewModel>().ShowShortcutResult(message, isError);
            }
            catch (Exception)
            {
                // Un raccourci ne doit jamais fermer l'application.
            }
        });
    }
}
