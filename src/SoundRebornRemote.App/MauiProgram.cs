using Microsoft.Extensions.Logging;
using SoundReborn.Core;
using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;
using SoundRebornRemote.App.Views;

namespace SoundRebornRemote.App;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();

        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                // Aucune police n'est embarquée : l'application utilise celle du système.
            })
            .ConfigureEssentials(essentials =>
            {
                // Raccourcis de l'icône (publiés par App) : leur réception.
                essentials.OnAppAction(App.HandleAppAction);
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Un seul HttpClient pour toute l'application, sans délai d'attente propre :
        // chaque appel pose le sien. Un plafond global unique ne peut pas convenir à
        // la fois à une commande ordinaire (8 s) et à celles qui réveillent
        // l'enceinte (25 s), et le plus court des deux l'emporterait toujours.
        // Voir StrApiClient.NormalTimeout / WakeTimeout et BoseApiClient.CallTimeout.
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient
            {
                Timeout = Timeout.InfiniteTimeSpan,
            };

            http.DefaultRequestHeaders.ExpectContinue = false;
            return http;
        });

        builder.Services.AddSingleton<SpeakerDiscovery>();
        builder.Services.AddSingleton<SpeakerManager>();
        builder.Services.AddSingleton<ArtworkCache>();
        builder.Services.AddSingleton<RadioBrowserClient>();
        builder.Services.AddSingleton<ZoneLocator>();

        // Enregistrement d'une radio : le service lui-même ne dépend pas d'Android ;
        // la notification, la lecture et le partage passent par IRecordingPlatform.
#if ANDROID
        builder.Services.AddSingleton<IRecordingPlatform, AndroidRecordingPlatform>();
#else
        builder.Services.AddSingleton<IRecordingPlatform, NoRecordingPlatform>();
#endif
        builder.Services.AddSingleton<RecordingService>(services => new RecordingService(
            services.GetRequiredService<HttpClient>(),
            services.GetRequiredService<IRecordingPlatform>(),
            Path.Combine(FileSystem.AppDataDirectory, "recordings"),
            TimeProvider.System));

        // Message vocal : le micro passe par IVoiceRecorder.
#if ANDROID
        builder.Services.AddSingleton<IVoiceRecorder, AndroidVoiceRecorder>();
#else
        builder.Services.AddSingleton<IVoiceRecorder, NoVoiceRecorder>();
#endif
        builder.Services.AddSingleton<VoiceMessageViewModel>(services => new VoiceMessageViewModel(
            services.GetRequiredService<SpeakerManager>(),
            services.GetRequiredService<IVoiceRecorder>()));

        // Sauvegarde des favoris : partage et choix de fichiers par IFileExchange.
#if ANDROID
        builder.Services.AddSingleton<IFileExchange, AndroidFileExchange>();
#else
        builder.Services.AddSingleton<IFileExchange, NoFileExchange>();
#endif
        builder.Services.AddSingleton<BackupViewModel>(services => new BackupViewModel(
            services.GetRequiredService<SpeakerManager>(),
            services.GetRequiredService<PresetsViewModel>(),
            services.GetRequiredService<VoiceMessageViewModel>(),
            services.GetRequiredService<IFileExchange>()));

        // Mise à jour depuis GitHub. La version Google Play (PLAY_STORE) n'installe
        // rien elle-même : c'est le Play Store qui met l'application à jour.
#if ANDROID && !PLAY_STORE
        builder.Services.AddSingleton<IAppInstaller, AndroidAppInstaller>();
#else
        builder.Services.AddSingleton<IAppInstaller, NoAppInstaller>();
#endif
        builder.Services.AddSingleton<UpdateViewModel>(services => new UpdateViewModel(
            services.GetRequiredService<SpeakerManager>(),
            services.GetRequiredService<HttpClient>(),
            services.GetRequiredService<IAppInstaller>()));

        // Raccourcis de l'icône : « Tout mettre en veille », touches 1 à 3.
        builder.Services.AddSingleton<ShortcutRunner>();

        // Dernière version de l'agent STR sur GitHub : la carte des Réglages et la
        // pastille de la liste des enceintes. Rien n'est installé d'ici.
        builder.Services.AddSingleton<AgentUpdateViewModel>(services => new AgentUpdateViewModel(
            services.GetRequiredService<SpeakerManager>(),
            services.GetRequiredService<HttpClient>()));

        // Écoute d'une station sur l'appareil, avant de la diffuser sur l'enceinte.
#if ANDROID
        builder.Services.AddSingleton<IStreamPreview, AndroidStreamPreview>();
#else
        builder.Services.AddSingleton<IStreamPreview, NoStreamPreview>();
#endif

        builder.Services.AddSingleton<PlayerViewModel>();
        builder.Services.AddSingleton<PresetsViewModel>();
        builder.Services.AddSingleton<SourcesViewModel>();
        builder.Services.AddSingleton<MultiroomViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();
        builder.Services.AddSingleton<SpotifyDeezerViewModel>();

        builder.Services.AddSingleton<PlayerPage>();
        builder.Services.AddSingleton<PresetsPage>();
        builder.Services.AddSingleton<SourcesPage>();
        builder.Services.AddSingleton<MultiroomPage>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder.Build();
    }
}
