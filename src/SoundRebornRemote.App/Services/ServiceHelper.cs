namespace SoundRebornRemote.App.Services;

/// <summary>
/// Accès au conteneur d'injection depuis un constructeur de page.
/// Shell instancie les pages de ses onglets lui-même : elles ne peuvent donc pas
/// recevoir leurs dépendances par constructeur, d'où ce point d'accès unique.
/// </summary>
public static class ServiceHelper
{
    public static IServiceProvider Services =>
        IPlatformApplication.Current?.Services
        ?? throw new InvalidOperationException("Le conteneur de services n'est pas encore prêt.");

    public static T Get<T>() where T : notnull => Services.GetRequiredService<T>();
}
