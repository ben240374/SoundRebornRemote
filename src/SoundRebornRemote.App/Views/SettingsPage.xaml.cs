using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private readonly AgentUpdateViewModel _agentUpdate;
    private bool _initialized;

    public SettingsPage()
    {
        InitializeComponent();

        // La carte « Sauvegarde » a sa propre vue-modèle. Posée AVANT celle de la
        // page : un BindingContext déjà fixé n'hérite pas de celui de la page.
        var backup = ServiceHelper.Get<BackupViewModel>();
        BackupCard.BindingContext = backup;
        var update = ServiceHelper.Get<UpdateViewModel>();
        UpdateCard.BindingContext = update;
        _agentUpdate = ServiceHelper.Get<AgentUpdateViewModel>();
        AgentUpdateCard.BindingContext = _agentUpdate;

        _viewModel = ServiceHelper.Get<SettingsViewModel>();
        BindingContext = _viewModel;

        // Après une restauration : langue, thème et vérification affichés, puis les
        // cartes des réveils et de l'écran relisent l'enceinte.
        backup.Restored += async (_, _) =>
        {
            _viewModel.SyncAppSettings();
            update.ReloadAutoCheck();

            try
            {
                await _viewModel.Alarms.LoadAsync();
                await _viewModel.Display.LoadAsync();
            }
            catch (Exception)
            {
                // Les cartes gardent ce qu'elles affichaient.
            }
        };
    }

    /// <summary>Ouvre la page « Spotify &amp; Deezer » (une seule fois, même après un double appui).</summary>
    private async void OnOpenStreaming(object? sender, EventArgs e)
    {
        // « async void » : une exception ici fermerait l'application.
        try
        {
            await SpotifyDeezerPage.ShowAsync(Navigation);
        }
        catch (Exception)
        {
            // La page n'a pas pu s'ouvrir : Réglages reste affiché.
        }
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

        // Dernière version de l'agent STR : au plus une lecture par jour, en tâche de fond.
        _ = _agentUpdate.AutoCheckAsync();

        if (_initialized)
        {
            // Les réveils ont pu changer ailleurs (page téléphone STR, autre appareil).
            // « async void » : une exception ici fermerait l'application.
            try
            {
                await _viewModel.Alarms.LoadAsync();
            }
            catch (Exception)
            {
                // La carte des réveils garde ce qu'elle affichait.
            }

            // Les réglages d'écran aussi (application de bureau, page téléphone).
            try
            {
                await _viewModel.Display.LoadAsync();
            }
            catch (Exception)
            {
                // La carte de l'écran garde ce qu'elle affichait.
            }

            return;
        }

        _initialized = true;

        // Reconnecte l'enceinte mémorisée au premier affichage.
        await _viewModel.InitializeAsync();
    }
}
