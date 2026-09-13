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
            return;
        }

        _initialized = true;

        // Reconnecte l'enceinte mémorisée au premier affichage.
        await _viewModel.InitializeAsync();
    }
}
