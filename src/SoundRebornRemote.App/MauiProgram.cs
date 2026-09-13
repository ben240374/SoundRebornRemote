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
            });

#if DEBUG
        builder.Logging.AddDebug();
#endif

        // Un seul HttpClient pour toute l'application. Le délai d'attente est court :
        // sur le LAN, une enceinte qui ne répond pas en 8 s ne répondra pas.
        builder.Services.AddSingleton(_ =>
        {
            var http = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(8),
            };

            http.DefaultRequestHeaders.ExpectContinue = false;
            return http;
        });

        builder.Services.AddSingleton<SpeakerDiscovery>();
        builder.Services.AddSingleton<SpeakerManager>();
        builder.Services.AddSingleton<ArtworkCache>();
        builder.Services.AddSingleton<RadioBrowserClient>();

        builder.Services.AddSingleton<PlayerViewModel>();
        builder.Services.AddSingleton<PresetsViewModel>();
        builder.Services.AddSingleton<SourcesViewModel>();
        builder.Services.AddSingleton<MultiroomViewModel>();
        builder.Services.AddSingleton<SettingsViewModel>();

        builder.Services.AddSingleton<PlayerPage>();
        builder.Services.AddSingleton<PresetsPage>();
        builder.Services.AddSingleton<SourcesPage>();
        builder.Services.AddSingleton<MultiroomPage>();
        builder.Services.AddSingleton<SettingsPage>();

        return builder.Build();
    }
}
