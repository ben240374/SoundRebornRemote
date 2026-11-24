using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App.Views;

/// <summary>
/// Barre de titre des onglets (Shell.TitleView) : titre de la page et nom de
/// l'application. Le titre est lu dans les textes traduits plutôt que par une
/// ressource dynamique : la vue de titre n'est pas toujours rattachée à la page
/// au moment où la ressource serait cherchée.
/// </summary>
public partial class AppTitleBar : ContentView
{
    public static readonly BindableProperty TitleKeyProperty = BindableProperty.Create(
        nameof(TitleKey), typeof(string), typeof(AppTitleBar), "",
        propertyChanged: (bindable, _, _) => ((AppTitleBar)bindable).ApplyTitle());

    public AppTitleBar()
    {
        InitializeComponent();

        // Les barres vivent aussi longtemps que leurs pages (singletons) : un
        // abonnement par barre, jamais repris, ne fuit pas.
        Localization.LanguageChanged += (_, _) => MainThread.BeginInvokeOnMainThread(ApplyTitle);
    }

    /// <summary>Clé du texte traduit du titre (L_TabPlayer…).</summary>
    public string TitleKey
    {
        get => (string)GetValue(TitleKeyProperty);
        set => SetValue(TitleKeyProperty, value);
    }

    private void ApplyTitle()
        => TitleLabel.Text = string.IsNullOrWhiteSpace(TitleKey) ? "" : Localization.Get(TitleKey);
}
