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

        // La carte « Sauvegarde des favoris » a sa propre vue-modèle. Posée AVANT celle
        // de la page : un BindingContext déjà fixé n'hérite pas de celui de la page.
        BackupCard.BindingContext = ServiceHelper.Get<BackupViewModel>();
        UpdateCard.BindingContext = ServiceHelper.Get<UpdateViewModel>();
        _agentUpdate = ServiceHelper.Get<AgentUpdateViewModel>();
        AgentUpdateCard.BindingContext = _agentUpdate;
        var speakerBackup = ServiceHelper.Get<SpeakerBackupViewModel>();
        SpeakerBackupCard.BindingContext = speakerBackup;

        _viewModel = ServiceHelper.Get<SettingsViewModel>();
        BindingContext = _viewModel;

        // Après une restauration, la carte des réveils relit l'enceinte.
        speakerBackup.Restored += async (_, _) =>
        {
            try
            {
                await _viewModel.Alarms.LoadAsync();
            }
            catch (Exception)
            {
                // La carte des réveils garde ce qu'elle affichait.
            }
        };
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

            return;
        }

        _initialized = true;

        // Reconnecte l'enceinte mémorisée au premier affichage.
        await _viewModel.InitializeAsync();
    }
}
