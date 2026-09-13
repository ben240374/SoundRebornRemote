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
    }

    protected override Window CreateWindow(IActivationState? activationState)
        => new(new AppShell()) { Title = "SoundReborn Remote" };
}
