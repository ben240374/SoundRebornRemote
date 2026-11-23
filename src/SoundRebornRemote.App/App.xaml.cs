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
