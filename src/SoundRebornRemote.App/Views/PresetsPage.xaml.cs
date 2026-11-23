using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class PresetsPage : ContentPage
{
    private readonly PresetsViewModel _viewModel;

    public PresetsPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<PresetsViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.RefreshCommand.CanExecute(null))
        {
            _viewModel.RefreshCommand.Execute(null);
        }

        // Les genres de l'annuaire, pour « Plus de genres » : lus une fois.
        _ = _viewModel.LoadGenresAsync();

        // Les favoris STR : relus à chaque visite, ils changent aussi depuis l'ordinateur.
        _ = _viewModel.Favorites.LoadAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        // L'écoute sur l'appareil est un aperçu : elle ne suit pas dans les autres onglets.
        _viewModel.StopPreview();
    }
}
