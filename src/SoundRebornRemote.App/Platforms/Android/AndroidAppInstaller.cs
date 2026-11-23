#if !PLAY_STORE
using Android.App;
using Android.Content;
using SoundRebornRemote.App.Services;

// Demander l'installation d'un APK. Déclarée ici plutôt que dans le manifeste :
// la version Google Play (PLAY_STORE) ne doit pas la contenir, Play l'interdit pour
// une mise à jour de l'application par elle-même.
[assembly: UsesPermission("android.permission.REQUEST_INSTALL_PACKAGES")]

namespace SoundRebornRemote.App;

/// <summary>
/// Ouvre l'installateur d'Android sur l'APK téléchargé. Le fichier est passé par
/// un FileProvider propre à l'application, limité au dossier « updates » du cache
/// (Resources/xml/update_paths.xml) : l'installateur ne voit que ce fichier-là.
/// </summary>
public sealed class AndroidAppInstaller : IAppInstaller
{
    /// <summary>Autorité du FileProvider ; doit rester celle de <see cref="UpdateFileProvider"/>.</summary>
    public const string Authority = "be.barbaix.soundrebornremote.updates";

    public bool IsSupported => true;

    public Task<bool> EnsurePermissionAsync()
    {
        var context = Android.App.Application.Context;

        // Avant Android 8, l'autorisation est un réglage global que l'installateur
        // propose lui-même au besoin.
        if (!OperatingSystem.IsAndroidVersionAtLeast(26) || context.PackageManager?.CanRequestPackageInstalls() == true)
        {
            return Task.FromResult(true);
        }

        var intent = new Intent(Android.Provider.Settings.ActionManageUnknownAppSources,
            Android.Net.Uri.Parse("package:" + context.PackageName));
        intent.AddFlags(ActivityFlags.NewTask);
        context.StartActivity(intent);

        return Task.FromResult(false);
    }

    public void Install(string apkPath)
    {
        var context = Android.App.Application.Context;
        var uri = AndroidX.Core.Content.FileProvider.GetUriForFile(context, Authority, new Java.IO.File(apkPath));

        var intent = new Intent(Intent.ActionView);
        intent.SetDataAndType(uri, "application/vnd.android.package-archive");
        intent.AddFlags(ActivityFlags.GrantReadUriPermission | ActivityFlags.NewTask);
        context.StartActivity(intent);
    }
}

/// <summary>
/// FileProvider de la mise à jour, distinct de celui de MAUI : son propre fichier de
/// chemins n'ouvre que le dossier « updates » du cache.
/// </summary>
[ContentProvider(new[] { AndroidAppInstaller.Authority }, Exported = false, GrantUriPermissions = true)]
[MetaData("android.support.FILE_PROVIDER_PATHS", Resource = "@xml/update_paths")]
public sealed class UpdateFileProvider : AndroidX.Core.Content.FileProvider
{
}
#endif
