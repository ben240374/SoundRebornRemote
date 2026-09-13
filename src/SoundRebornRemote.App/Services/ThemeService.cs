namespace SoundRebornRemote.App.Services;

public enum AppThemeChoice
{
    System,
    Light,
    Dark,
}

/// <summary>
/// Thème clair ou sombre. Les couleurs sont déclarées deux fois dans les styles,
/// via AppThemeBinding ; il suffit donc de poser UserAppTheme pour que toute
/// l'interface bascule, sans recharger la moindre page.
/// </summary>
public static class ThemeService
{
    private const string ThemeKey = "app_theme";

    public static AppThemeChoice Current { get; private set; } = AppThemeChoice.System;

    public static IReadOnlyList<AppThemeChoice> Available { get; } = new[]
    {
        AppThemeChoice.System,
        AppThemeChoice.Light,
        AppThemeChoice.Dark,
    };

    /// <summary>Clé de traduction du libellé d'un choix.</summary>
    public static string LabelKey(AppThemeChoice choice) => choice switch
    {
        AppThemeChoice.Light => "L_ThemeLight",
        AppThemeChoice.Dark => "L_ThemeDark",
        _ => "L_ThemeSystem",
    };

    /// <summary>
    /// Applique le thème enregistré. Appelé depuis le constructeur d'App, qui passe
    /// sa propre instance : à ce moment Application.Current n'est pas encore affectée.
    /// </summary>
    public static void Initialize(Application application)
    {
        _application = application;
        Current = Load();
        Apply(Current);
    }

    private static Application? _application;

    public static void Set(AppThemeChoice choice)
    {
        Current = choice;

        try
        {
            Preferences.Default.Set(ThemeKey, choice.ToString());
        }
        catch (Exception)
        {
            // La persistance du réglage n'est pas critique.
        }

        Apply(choice);
    }

    private static void Apply(AppThemeChoice choice)
    {
        var application = _application ?? Application.Current;

        if (application is null)
        {
            return;
        }

        application.UserAppTheme = choice switch
        {
            AppThemeChoice.Light => AppTheme.Light,
            AppThemeChoice.Dark => AppTheme.Dark,
            _ => AppTheme.Unspecified,
        };
    }

    private static AppThemeChoice Load()
    {
        try
        {
            var saved = Preferences.Default.Get(ThemeKey, string.Empty);

            if (!string.IsNullOrWhiteSpace(saved) && Enum.TryParse<AppThemeChoice>(saved, out var parsed))
            {
                return parsed;
            }
        }
        catch (Exception)
        {
            // Premier lancement.
        }

        return AppThemeChoice.System;
    }
}
