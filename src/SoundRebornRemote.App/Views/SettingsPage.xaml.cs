using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class SettingsPage : ContentPage
{
    private readonly SettingsViewModel _viewModel;
    private bool _initialized;

    public SettingsPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<SettingsViewModel>();
        BindingContext = _viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();

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
