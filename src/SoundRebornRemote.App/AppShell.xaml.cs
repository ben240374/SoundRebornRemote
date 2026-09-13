using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Reconnexion à la dernière enceinte utilisée, sans bloquer l'affichage :
        // les pages se mettent à jour d'elles-mêmes via l'événement DeviceChanged.
        _ = Task.Run(async () =>
        {
            try
            {
                await ServiceHelper.Get<SpeakerManager>().TryRestoreAsync().ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Enceinte éteinte au lancement : l'utilisateur passera par Réglages.
            }
        });
    }
}
