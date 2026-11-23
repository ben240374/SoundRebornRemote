using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class PlayerPage : ContentPage
{
    private readonly PlayerViewModel _viewModel;
    private readonly VoiceMessageViewModel _voice;

    public PlayerPage()
    {
        InitializeComponent();

        // La carte « Message vocal » a sa propre vue-modèle. Posée AVANT celle de la
        // page : un BindingContext déjà fixé n'hérite pas de celui de la page.
        _voice = ServiceHelper.Get<VoiceMessageViewModel>();
        VoiceCard.BindingContext = _voice;
        _voice.RecordingStarted += OnRecordingStarted;

        // Bannière « nouvelle version » : même règle, posée avant la page.
        _update = ServiceHelper.Get<UpdateViewModel>();
        UpdateBanner.BindingContext = _update;

        _viewModel = ServiceHelper.Get<PlayerViewModel>();
        BindingContext = _viewModel;
    }

    private readonly UpdateViewModel _update;

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();

        // Au plus une fois par jour, et seulement si l'utilisateur ne l'a pas coupé.
        _ = _update.AutoCheckAsync();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();

        // Quitter l'onglet le doigt encore sur le bouton n'envoie rien.
        _voice.CancelRecordingCommand.Execute(null);
    }

    /// <summary>Le doigt se pose sur le bouton rond : l'enregistrement commence.</summary>
    private void OnVoicePressed(object? sender, EventArgs e)
    {
#if ANDROID
        // La page défile : sans cela, un doigt qui bouge un peu pendant qu'on parle
        // ferait prendre le geste par le défilement, qui interromprait l'appui et
        // enverrait le message trop tôt. Valable pour ce seul geste.
        if (VoiceButton.Handler?.PlatformView is Android.Views.View view)
        {
            view.Parent?.RequestDisallowInterceptTouchEvent(true);
        }
#endif
        _ = _voice.PressAsync();
    }

    /// <summary>
    /// Le micro tourne : une courte vibration le dit, on peut parler. Le micro met un
    /// court instant à démarrer, et un mot dit avant était perdu.
    /// </summary>
    private void OnRecordingStarted(object? sender, EventArgs e)
    {
        try
        {
            HapticFeedback.Default.Perform(HapticFeedbackType.LongPress);
        }
        catch (Exception)
        {
            // Pas de vibreur, ou vibrations coupées : le bouton rouge suffit.
        }
    }

    /// <summary>
    /// Le doigt se lève (ou le geste est interrompu par le système) : le message
    /// part vers l'enceinte.
    /// </summary>
    private void OnVoiceReleased(object? sender, EventArgs e) => _ = _voice.ReleaseAsync();
}
