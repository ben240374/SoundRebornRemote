using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

/// <summary>
/// La page « Spotify &amp; Deezer », modale. Tout le travail est dans
/// <see cref="SpotifyDeezerViewModel"/> ; ici, seulement l'ouverture et la fermeture.
/// </summary>
public partial class SpotifyDeezerPage : ContentPage
{
    private readonly SpotifyDeezerViewModel _viewModel;

    public SpotifyDeezerPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<SpotifyDeezerViewModel>();
        BindingContext = _viewModel;
    }

    /// <summary>
    /// Ouvre la page, ou la laisse telle quelle si elle est déjà ouverte (double
    /// appui, lien partagé pendant qu'elle est à l'écran). Un texte partagé
    /// remplit le champ du lien Deezer.
    /// </summary>
    public static async Task ShowAsync(INavigation navigation, string? sharedText = null)
    {
        if (sharedText is not null)
        {
            ServiceHelper.Get<SpotifyDeezerViewModel>().OfferSharedText(sharedText);
        }

        if (navigation.ModalStack.Any(page => page is SpotifyDeezerPage))
        {
            return;
        }

        await navigation.PushModalAsync(new SpotifyDeezerPage());
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _ = _viewModel.OpenAsync();
    }

    protected override void OnDisappearing()
    {
        _viewModel.Close();
        base.OnDisappearing();
    }

    private async void OnBack(object? sender, TappedEventArgs e)
    {
        // « async void » : une exception ici fermerait l'application.
        try
        {
            if (Navigation.ModalStack.Contains(this))
            {
                await Navigation.PopModalAsync();
            }
        }
        catch (Exception)
        {
            // Déjà refermée (double appui) : rien à faire.
        }
    }
}
