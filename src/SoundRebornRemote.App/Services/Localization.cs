using System.Globalization;

namespace SoundRebornRemote.App.Services;

public enum AppLanguage
{
    French,
    English,
    German,
    Dutch,
    Spanish,
    Italian,
}

/// <summary>
/// Traductions de l'application.
///
/// Volontairement une table en C# plutôt que des fichiers .resx : pas de fichier
/// généré par le concepteur à garder synchronisé, tout compile, et ajouter une
/// langue tient en une colonne de plus dans Add().
///
/// Les libellés de l'interface sont poussés dans le dictionnaire de ressources de
/// l'application ; les vues les consomment via {DynamicResource L_...}, ce qui les
/// met à jour immédiatement quand la langue change, sans redémarrage.
/// Le code (messages d'état) appelle Get() directement.
/// </summary>
public static class Localization
{
    private const string LanguageKey = "app_language";

    private static readonly Dictionary<string, string[]> Table = new(StringComparer.Ordinal);

    static Localization()
    {
        Fill();
        Current = LoadSavedLanguage();

        // Dès le départ, la culture du processus suit la langue choisie : sinon les
        // noms de pays et de langues des filtres sortiraient dans la langue du
        // téléphone, pas dans celle de l'application.
        ApplyCulture(Current);
    }

    /// <summary>Déclenché après chaque changement de langue, une fois les ressources à jour.</summary>
    public static event EventHandler? LanguageChanged;

    public static AppLanguage Current { get; private set; }

    public static IReadOnlyList<AppLanguage> Available { get; } = new[]
    {
        AppLanguage.French,
        AppLanguage.English,
        AppLanguage.German,
        AppLanguage.Dutch,
        AppLanguage.Spanish,
        AppLanguage.Italian,
    };

    /// <summary>Nom de la langue dans la langue elle-même — on ne le traduit pas.</summary>
    public static string NameOf(AppLanguage language) => language switch
    {
        AppLanguage.French => "Français",
        AppLanguage.English => "English",
        AppLanguage.German => "Deutsch",
        AppLanguage.Dutch => "Nederlands",
        AppLanguage.Spanish => "Español",
        AppLanguage.Italian => "Italiano",
        _ => language.ToString(),
    };

    public static string Get(string key)
    {
        if (Table.TryGetValue(key, out var values))
        {
            var index = (int)Current;
            var value = index < values.Length ? values[index] : null;

            if (!string.IsNullOrEmpty(value))
            {
                return value;
            }

            // Libellé manquant dans cette langue : l'anglais, compris du plus grand
            // nombre, sinon le français.
            return index != 0 && values.Length > 1 && !string.IsNullOrEmpty(values[1]) ? values[1] : values[0];
        }

        // Clé absente : on renvoie la clé, ce qui saute aux yeux pendant les tests.
        return key;
    }

    public static string Get(string key, params object[] args)
    {
        try
        {
            return string.Format(CultureInfo.CurrentCulture, Get(key), args);
        }
        catch (FormatException)
        {
            return Get(key);
        }
    }

    /// <summary>Aligne la culture du processus sur la langue de l'interface.</summary>
    private static void ApplyCulture(AppLanguage language)
    {
        try
        {
            var culture = CultureOf(language);
            CultureInfo.DefaultThreadCurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }
        catch (Exception)
        {
            // Culture indisponible sur l'appareil : les noms resteront en anglais.
        }
    }

    /// <summary>Culture .NET correspondante, utilisée pour les noms de pays et de langues.</summary>
    public static CultureInfo CultureOf(AppLanguage language) => CultureInfo.GetCultureInfo(language switch
    {
        AppLanguage.English => "en",
        AppLanguage.German => "de",
        AppLanguage.Dutch => "nl",
        AppLanguage.Spanish => "es",
        AppLanguage.Italian => "it",
        _ => "fr",
    });

    public static void SetLanguage(AppLanguage language, bool persist = true)
    {
        Current = language;

        ApplyCulture(language);

        if (persist)
        {
            try
            {
                Preferences.Default.Set(LanguageKey, language.ToString());
            }
            catch (Exception)
            {
                // La persistance du réglage n'est pas critique.
            }
        }

        ApplyToResources();
        LanguageChanged?.Invoke(null, EventArgs.Empty);
    }

    /// <summary>
    /// Écrit toutes les chaînes dans le dictionnaire de ressources de l'application.
    /// À appeler une fois au démarrage, puis à chaque changement de langue.
    /// </summary>
    public static void ApplyToResources(ResourceDictionary? target = null)
    {
        // Au tout premier appel, App n'est pas encore affectée à Application.Current :
        // le constructeur d'App passe donc son propre dictionnaire.
        var resources = target ?? Application.Current?.Resources;

        if (resources is null)
        {
            return;
        }

        foreach (var key in Table.Keys)
        {
            if (key.StartsWith("L_", StringComparison.Ordinal))
            {
                resources[key] = Get(key);
            }
        }
    }

    private static AppLanguage LoadSavedLanguage()
    {
        try
        {
            var saved = Preferences.Default.Get(LanguageKey, string.Empty);

            if (!string.IsNullOrWhiteSpace(saved) && Enum.TryParse<AppLanguage>(saved, out var parsed))
            {
                return parsed;
            }
        }
        catch (Exception)
        {
            // Premier lancement, ou préférences illisibles.
        }

        // Pas de choix enregistré : on suit la langue du téléphone si elle est gérée,
        // et l'anglais sert de repli pour toutes les autres — c'est la convention,
        // et un utilisateur polonais ou portugais comprendra mieux l'anglais que le
        // français. Dès qu'une langue est choisie dans les réglages, elle prime.
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "fr" => AppLanguage.French,
            "de" => AppLanguage.German,
            "nl" => AppLanguage.Dutch,
            "es" => AppLanguage.Spanish,
            "it" => AppLanguage.Italian,
            _ => AppLanguage.English,
        };
    }

    private static void Add(string key, string fr, string en, string de, string nl, string es, string it)
        => Table[key] = new[] { fr, en, de, nl, es, it };

    private static void Fill()
    {
        // ---------------------------------------------------------- Onglets
        Add("L_TabPlayer", "Lecture", "Now playing", "Wiedergabe", "Afspelen", "Reproducción", "Riproduzione");
        // Libellés courts : la barre d'onglets d'Android tronque au-delà.
        Add("L_TabPresets", "Présélections", "Presets", "Presets", "Presets", "Presintonías", "Preselezioni");
        Add("L_TabSources", "Sources", "Sources", "Quellen", "Bronnen", "Fuentes", "Sorgenti");
        Add("L_TabMultiroom", "Groupes", "Groups", "Gruppen", "Groepen", "Grupos", "Gruppi");
        Add("L_TabSettings", "Réglages", "Settings", "Einstellungen", "Instellingen", "Ajustes", "Impostazioni");

        // ---------------------------------------------------------- Page Lecture
        Add("L_Speaker", "ENCEINTE", "SPEAKER", "LAUTSPRECHER", "SPEAKER", "ALTAVOZ", "DIFFUSORE");
        Add("L_Live", "direct", "live", "live", "live", "en vivo", "in diretta");
        Add("L_Polling", "sondage", "polling", "Abfrage", "polling", "sondeo", "interrogazione");
        Add("L_Volume", "VOLUME", "VOLUME", "LAUTSTÄRKE", "VOLUME", "VOLUMEN", "VOLUME");
        Add("L_Bass", "GRAVES", "BASS", "BÄSSE", "LAGE TONEN", "GRAVES", "BASSI");
        Add("L_BassNote",
            "L'API SoundTouch n'expose pas de réglage d'aigus — seuls les graves sont pilotables.",
            "The SoundTouch API exposes no treble control — only bass can be adjusted.",
            "Die SoundTouch-API bietet keine Höhenregelung — nur die Bässe lassen sich einstellen.",
            "De SoundTouch-API kent geen regeling voor hoge tonen — alleen de lage tonen zijn instelbaar.",
            "La API de SoundTouch no ofrece control de agudos — solo se pueden ajustar los graves.",
            "L'API SoundTouch non offre la regolazione degli acuti — si possono regolare solo i bassi.");
        Add("L_Presets", "PRÉSÉLECTIONS", "PRESETS", "PRESETS", "PRESETS", "PRESINTONÍAS", "PRESELEZIONI");
        Add("L_Refresh", "Rafraîchir", "Refresh", "Aktualisieren", "Vernieuwen", "Actualizar", "Aggiorna");
        Add("L_PowerButton", "Veille / Allumer", "Standby / Power on", "Standby / Einschalten", "Stand-by / Aanzetten", "Reposo / Encender", "Standby / Accensione");
        Add("L_NoSpeakerTitle", "Aucune enceinte", "No speaker", "Kein Lautsprecher", "Geen speaker", "Sin altavoz", "Nessun diffusore");
        Add("L_NoSpeakerDetail",
            "Choisis une enceinte dans Réglages",
            "Pick a speaker in Settings",
            "Wähle einen Lautsprecher unter Einstellungen",
            "Kies een speaker bij Instellingen",
            "Elige un altavoz en Ajustes",
            "Scegli un diffusore nelle Impostazioni");
        Add("L_Standby", "En veille", "Standby", "Standby", "Stand-by", "En reposo", "Standby");
        Add("L_NothingPlaying", "Rien en lecture", "Nothing playing", "Nichts in Wiedergabe", "Niets aan het afspelen", "Nada en reproducción", "Niente in riproduzione");

        // ---------------------------------------------------------- Sources (noms affichés)
        Add("L_SrcRadio", "Radio Internet", "Internet radio", "Internetradio", "Internetradio", "Radio por Internet", "Radio internet");
        Add("L_SrcLibrary", "Bibliothèque réseau", "Network library", "Netzwerkbibliothek", "Netwerkbibliotheek", "Biblioteca de red", "Libreria di rete");
        Add("L_SrcAux", "Entrée AUX", "AUX input", "AUX-Eingang", "AUX-ingang", "Entrada AUX", "Ingresso AUX");
        Add("L_SrcNotification", "Annonce", "Announcement", "Ansage", "Melding", "Aviso", "Annuncio");

        // ---------------------------------------------------------- Page Présélections
        Add("L_PresetsHint",
            "Touche une tuile pour lancer la présélection. Pour en enregistrer une, garde la touche appuyée sur l'enceinte ou passe par l'application STR du bureau.",
            "Tap a tile to start a preset. To store one, hold the key on the speaker itself or use the STR desktop app.",
            "Tippe auf eine Kachel, um ein Preset zu starten. Zum Speichern die Taste am Lautsprecher gedrückt halten oder die STR-Desktop-App verwenden.",
            "Tik op een tegel om een preset te starten. Opslaan doe je door de toets op de speaker ingedrukt te houden of via de STR-desktopapp.",
            "Toca una tarjeta para lanzar una presintonía. Para guardarla, mantén pulsada la tecla en el altavoz o usa la app STR de escritorio.",
            "Tocca un riquadro per avviare una preselezione. Per memorizzarne una, tieni premuto il tasto sul diffusore oppure usa l'app STR per computer.");
        Add("L_PresetEmpty", "vide", "empty", "leer", "leeg", "vacía", "vuota");
        Add("L_PresetNumber", "Présélection {0}", "Preset {0}", "Preset {0}", "Preset {0}", "Presintonía {0}", "Preselezione {0}");

        // ---------------------------------------------------------- Page Sources
        Add("L_Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth");
        Add("L_Aux", "AUX", "AUX", "AUX", "AUX", "AUX", "AUX");
        Add("L_StandbyButton", "Veille", "Standby", "Standby", "Stand-by", "Reposo", "Standby");
        Add("L_Spotify", "SPOTIFY", "SPOTIFY", "SPOTIFY", "SPOTIFY", "SPOTIFY", "SPOTIFY");
        Add("L_SpotifyHint",
            "Sélectionne l'enceinte dans l'application Spotify (Spotify Connect). Elle apparaît dès que l'agent STR tourne.",
            "Pick the speaker inside the Spotify app (Spotify Connect). It shows up as soon as the STR agent is running.",
            "Wähle den Lautsprecher in der Spotify-App (Spotify Connect). Er erscheint, sobald der STR-Agent läuft.",
            "Kies de speaker in de Spotify-app (Spotify Connect). Hij verschijnt zodra de STR-agent draait.",
            "Selecciona el altavoz en la aplicación Spotify (Spotify Connect). Aparece en cuanto el agente STR está activo.",
            "Scegli il diffusore nell'app Spotify (Spotify Connect). Compare non appena l'agente STR è in funzione.");
        Add("L_SpotifyNoAgent",
            "Aucun agent STR détecté sur cette enceinte : Spotify Connect ne fonctionnera pas, le cloud Bose qui l'assurait est arrêté.",
            "No STR agent on this speaker: Spotify Connect will not work, the Bose cloud that provided it is shut down.",
            "Kein STR-Agent auf diesem Lautsprecher: Spotify Connect funktioniert nicht, die Bose-Cloud dafür ist abgeschaltet.",
            "Geen STR-agent op deze speaker: Spotify Connect werkt niet, de Bose-cloud die dit verzorgde is gestopt.",
            "Sin agente STR en este altavoz: Spotify Connect no funcionará, la nube de Bose que lo proporcionaba está apagada.",
            "Nessun agente STR su questo diffusore: Spotify Connect non funzionerà, il cloud Bose che lo forniva è stato chiuso.");
        Add("L_SpeakerSources", "SOURCES DE L'ENCEINTE", "SPEAKER SOURCES", "QUELLEN DES LAUTSPRECHERS", "BRONNEN VAN DE SPEAKER", "FUENTES DEL ALTAVOZ", "SORGENTI DEL DIFFUSORE");
        Add("L_NoSources",
            "Aucune source listée. Rafraîchis, ou réveille l'enceinte.",
            "No sources listed. Refresh, or wake the speaker.",
            "Keine Quellen gelistet. Aktualisiere, oder wecke den Lautsprecher.",
            "Geen bronnen gevonden. Vernieuw, of wek de speaker.",
            "No hay fuentes. Actualiza, o despierta el altavoz.",
            "Nessuna sorgente elencata. Aggiorna o riattiva il diffusore.");
        Add("L_PlayStream", "JOUER UN FLUX", "PLAY A STREAM", "STREAM ABSPIELEN", "STREAM AFSPELEN", "REPRODUCIR UN FLUJO", "RIPRODURRE UN FLUSSO");
        Add("L_PlayStreamButton", "Lancer", "Play", "Abspielen", "Afspelen", "Reproducir", "Riproduci");
        Add("L_PlayStreamNote",
            "L'agent convertit une adresse HTTPS en HTTP : le moteur UPnP de l'enceinte refuse TLS.",
            "The agent rewrites an HTTPS address to HTTP: the speaker's UPnP renderer rejects TLS.",
            "Der Agent wandelt eine HTTPS-Adresse in HTTP um: der UPnP-Renderer des Lautsprechers lehnt TLS ab.",
            "De agent zet een HTTPS-adres om naar HTTP: de UPnP-renderer van de speaker weigert TLS.",
            "El agente convierte una dirección HTTPS en HTTP: el motor UPnP del altavoz rechaza TLS.",
            "L'agente riscrive un indirizzo HTTPS in HTTP: il renderer UPnP del diffusore rifiuta il TLS.");
        // ---------------------------------------------------------- Enregistrement (Sources)
        Add("L_RecTitle", "ENREGISTRER", "RECORD", "AUFNEHMEN", "OPNEMEN", "GRABAR", "REGISTRA");
        Add("L_RecButton", "Enregistrer", "Record", "Aufnehmen", "Opnemen", "Grabar", "Registra");
        Add("L_RecStop", "Arrêter", "Stop", "Stoppen", "Stoppen", "Detener", "Ferma");
        Add("L_RecMax", "max 5 min", "max 5 min", "max. 5 Min.", "max. 5 min", "máx. 5 min", "max 5 min");
        Add("L_RecReady",
            "{0} — flux disponible",
            "{0} — stream available",
            "{0} — Stream verfügbar",
            "{0} — stream beschikbaar",
            "{0} — flujo disponible",
            "{0} — flusso disponibile");
        Add("L_RecDelete", "Supprimer", "Delete", "Löschen", "Verwijderen", "Eliminar", "Elimina");
        Add("L_RecDeleteAsk",
            "Supprimer l'enregistrement {0} du {1} à {2} ?",
            "Delete the recording {0} of {1} at {2}?",
            "Die Aufnahme {0} vom {1} um {2} löschen?",
            "De opname {0} van {1} om {2} verwijderen?",
            "¿Eliminar la grabación {0} del {1} a las {2}?",
            "Eliminare la registrazione {0} del {1} alle {2}?");
        Add("L_RecNotifTitle",
            "Enregistrement en cours",
            "Recording",
            "Aufnahme läuft",
            "Opname bezig",
            "Grabando",
            "Registrazione");
        Add("L_RecMinSec", "{0} min {1:00}", "{0} min {1:00}", "{0} Min. {1:00}", "{0} min {1:00}", "{0} min {1:00}", "{0} min {1:00}");
        Add("L_RecNone",
            "Aucun enregistrement pour l'instant.",
            "No recordings yet.",
            "Noch keine Aufnahmen.",
            "Nog geen opnames.",
            "Todavía no hay grabaciones.",
            "Ancora nessuna registrazione.");
        Add("S_RecNeedAgent",
            "Il faut l'agent STR sur cette enceinte pour enregistrer.",
            "Recording needs the STR agent on this speaker.",
            "Zum Aufnehmen braucht dieser Lautsprecher den STR-Agenten.",
            "Opnemen vereist de STR-agent op deze speaker.",
            "Para grabar, este altavoz necesita el agente STR.",
            "La registrazione richiede l'agente STR su questo diffusore.");
        Add("L_CopyTitle",
            "COPIER LES TOUCHES",
            "COPY THE KEYS",
            "TASTEN KOPIEREN",
            "TOETSEN KOPIËREN",
            "COPIAR LAS TECLAS",
            "COPIA I TASTI");
        Add("L_CopyHint",
            "Copie les touches de {0} vers une autre enceinte STR.",
            "Copies the keys of {0} to another STR speaker.",
            "Kopiert die Tasten von {0} auf einen anderen STR-Lautsprecher.",
            "Kopieert de toetsen van {0} naar een andere STR-speaker.",
            "Copia las teclas de {0} a otro altavoz STR.",
            "Copia i tasti di {0} su un altro diffusore STR.");
        Add("L_CopyButton",
            "Copier vers…",
            "Copy to…",
            "Kopieren nach…",
            "Kopiëren naar…",
            "Copiar a…",
            "Copia su…");
        Add("L_CopyChoose",
            "Copier les touches de {0} vers :",
            "Copy the keys of {0} to:",
            "Tasten von {0} kopieren nach:",
            "De toetsen van {0} kopiëren naar:",
            "Copiar las teclas de {0} a:",
            "Copia i tasti di {0} su:");
        Add("L_CopyAll",
            "Toutes les enceintes",
            "All speakers",
            "Alle Lautsprecher",
            "Alle speakers",
            "Todos los altavoces",
            "Tutti i diffusori");
        Add("L_CopyAllTargets",
            "toutes les enceintes STR",
            "all STR speakers",
            "alle STR-Lautsprecher",
            "alle STR-speakers",
            "todos los altavoces STR",
            "tutti i diffusori STR");
        Add("L_CopyConfirm",
            "Copier vers {0} ? Chaque touche réglée sur {1} remplace celle de la cible ; les autres touches de la cible restent. Une touche Spotify transmet aussi le compte Spotify de {1}.",
            "Copy to {0}? Each key set on {1} replaces the target's; the target's other keys stay. A Spotify key also hands on the Spotify account of {1}.",
            "Nach {0} kopieren? Jede auf {1} belegte Taste ersetzt die des Ziels; die übrigen Tasten des Ziels bleiben. Eine Spotify-Taste überträgt auch das Spotify-Konto von {1}.",
            "Kopiëren naar {0}? Elke toets die op {1} is ingesteld, vervangt die van het doel; de andere toetsen van het doel blijven. Een Spotify-toets geeft ook het Spotify-account van {1} door.",
            "¿Copiar a {0}? Cada tecla configurada en {1} sustituye a la del destino; las demás teclas del destino se mantienen. Una tecla de Spotify también transmite la cuenta de Spotify de {1}.",
            "Copiare su {0}? Ogni tasto impostato su {1} sostituisce quello di destinazione; gli altri tasti di destinazione restano. Un tasto Spotify trasmette anche l'account Spotify di {1}.");
        Add("L_CopyGo",
            "Copier",
            "Copy",
            "Kopieren",
            "Kopiëren",
            "Copiar",
            "Copia");
        Add("S_CopyRunning",
            "Copie en cours…",
            "Copying…",
            "Wird kopiert…",
            "Bezig met kopiëren…",
            "Copiando…",
            "Copia in corso…");
        Add("S_CopyDone",
            "{0} : touches copiées ({1}).",
            "{0}: keys copied ({1}).",
            "{0}: Tasten kopiert ({1}).",
            "{0}: toetsen gekopieerd ({1}).",
            "{0}: teclas copiadas ({1}).",
            "{0}: tasti copiati ({1}).");
        Add("S_CopyRow1Only",
            "Seconde rangée non copiée : cette enceinte n'en a pas.",
            "Second row not copied: this speaker has none.",
            "Zweite Reihe nicht kopiert: Dieser Lautsprecher hat keine.",
            "Tweede rij niet gekopieerd: deze speaker heeft er geen.",
            "Segunda fila no copiada: este altavoz no la tiene.",
            "Seconda fila non copiata: questo diffusore non ne ha.");
        Add("S_CopySpotify",
            "Compte Spotify transmis.",
            "Spotify account handed on.",
            "Spotify-Konto übertragen.",
            "Spotify-account doorgegeven.",
            "Cuenta de Spotify transmitida.",
            "Account Spotify trasmesso.");
        Add("S_CopyConflict",
            "{0} : « {1} » y est déjà sur la touche {2}. Rien n'a été écrit sur cette enceinte.",
            "{0}: “{1}” is already on key {2} there. Nothing was written to this speaker.",
            "{0}: „{1}“ liegt dort schon auf Taste {2}. Auf diesen Lautsprecher wurde nichts geschrieben.",
            "{0}: “{1}” staat daar al op toets {2}. Er is niets naar deze speaker geschreven.",
            "{0}: «{1}» ya está allí en la tecla {2}. No se ha escrito nada en este altavoz.",
            "{0}: “{1}” è già sul tasto {2} di quel diffusore. Su questo diffusore non è stato scritto nulla.");
        Add("S_CopyUnreachable",
            "{0} : injoignable, rien n'a été copié.",
            "{0}: unreachable, nothing was copied.",
            "{0}: nicht erreichbar, nichts wurde kopiert.",
            "{0}: onbereikbaar, er is niets gekopieerd.",
            "{0}: inaccesible, no se ha copiado nada.",
            "{0}: irraggiungibile, non è stato copiato nulla.");
        Add("S_CopyRefused",
            "{0} : copie refusée par l'enceinte.",
            "{0}: the speaker refused the copy.",
            "{0}: Der Lautsprecher hat das Kopieren abgelehnt.",
            "{0}: de speaker weigerde de kopie.",
            "{0}: el altavoz rechazó la copia.",
            "{0}: il diffusore ha rifiutato la copia.");
        Add("S_CopyUnknown",
            "{0} : {1} ne la connaît pas encore. Réessaie dans quelques minutes.",
            "{0}: {1} does not know it yet. Try again in a few minutes.",
            "{0}: {1} kennt ihn noch nicht. Versuche es in ein paar Minuten erneut.",
            "{0}: {1} kent hem nog niet. Probeer het over een paar minuten opnieuw.",
            "{0}: {1} todavía no lo conoce. Vuelve a intentarlo dentro de unos minutos.",
            "{0}: {1} non lo conosce ancora. Riprova tra qualche minuto.");
        Add("S_CopyFailed",
            "{0} : échec ({1}).",
            "{0}: failed ({1}).",
            "{0}: fehlgeschlagen ({1}).",
            "{0}: mislukt ({1}).",
            "{0}: error ({1}).",
            "{0}: non riuscita ({1}).");
        Add("S_CopyBusy",
            "Une copie est déjà en cours sur cette enceinte.",
            "A copy is already running on this speaker.",
            "Auf diesem Lautsprecher läuft bereits ein Kopiervorgang.",
            "Er loopt al een kopie op deze speaker.",
            "Ya hay una copia en curso en este altavoz.",
            "Su questo diffusore è già in corso una copia.");
        Add("S_CopyNoPresets",
            "Cette enceinte n'a aucune touche réglée à copier.",
            "This speaker has no key set to copy.",
            "Dieser Lautsprecher hat keine belegte Taste zum Kopieren.",
            "Deze speaker heeft geen ingestelde toets om te kopiëren.",
            "Este altavoz no tiene ninguna tecla configurada que copiar.",
            "Questo diffusore non ha tasti impostati da copiare.");
        Add("S_CopyNoTargets",
            "Aucune enceinte cible n'a répondu.",
            "No target speaker answered.",
            "Kein Ziellautsprecher hat geantwortet.",
            "Geen enkele doelspeaker antwoordde.",
            "Ningún altavoz de destino respondió.",
            "Nessun diffusore di destinazione ha risposto.");
        Add("S_CopyTooOld",
            "La copie des touches demande l'agent STR 1.6.5 ou plus récent sur {0}.",
            "Copying the keys needs STR agent 1.6.5 or later on {0}.",
            "Das Kopieren der Tasten braucht den STR-Agenten 1.6.5 oder neuer auf {0}.",
            "Toetsen kopiëren vereist STR-agent 1.6.5 of nieuwer op {0}.",
            "Copiar las teclas requiere el agente STR 1.6.5 o posterior en {0}.",
            "La copia dei tasti richiede l'agente STR 1.6.5 o successivo su {0}.");
        Add("S_CopyNoOther",
            "Aucune autre enceinte STR connue. Lance un balayage dans Réglages.",
            "No other STR speaker known. Run a scan in Settings.",
            "Kein anderer STR-Lautsprecher bekannt. Starte eine Suche in den Einstellungen.",
            "Geen andere STR-speaker bekend. Start een scan in Instellingen.",
            "No se conoce ningún otro altavoz STR. Lanza una búsqueda en Ajustes.",
            "Nessun altro diffusore STR conosciuto. Avvia una ricerca nelle Impostazioni.");
        Add("S_CopyTimeout",
            "La copie continue sur l'enceinte ; vérifie les touches dans un moment.",
            "The copy goes on on the speaker; check the keys in a moment.",
            "Das Kopieren läuft auf dem Lautsprecher weiter; prüfe die Tasten gleich.",
            "De kopie loopt verder op de speaker; controleer de toetsen zo meteen.",
            "La copia continúa en el altavoz; comprueba las teclas dentro de un momento.",
            "La copia prosegue sul diffusore; controlla i tasti tra un momento.");
        Add("L_Like",
            "J'aime",
            "Like",
            "Gefällt mir",
            "Vind ik leuk",
            "Me gusta",
            "Mi piace");
        Add("L_Unlike",
            "Je n'aime pas",
            "Dislike",
            "Gefällt mir nicht",
            "Vind ik niet leuk",
            "No me gusta",
            "Non mi piace");
        Add("L_LikedAccount",
            "Le titre va dans les Titres likés du compte Spotify de l'enceinte.",
            "The song goes to the Liked Songs of the speaker's Spotify account.",
            "Der Titel landet in den Lieblingssongs des Spotify-Kontos des Lautsprechers.",
            "Het nummer gaat naar de Gelikete nummers van het Spotify-account van de speaker.",
            "La canción va a las canciones que te gustan de la cuenta de Spotify del altavoz.",
            "Il brano va nei Brani che ti piacciono dell'account Spotify del diffusore.");
        Add("S_LikedSaved",
            "Ajouté aux Titres likés : {0}.",
            "Added to Liked Songs: {0}.",
            "Zu den Lieblingssongs hinzugefügt: {0}.",
            "Toegevoegd aan Gelikete nummers: {0}.",
            "Añadida a Tus me gusta: {0}.",
            "Aggiunto ai Brani che ti piacciono: {0}.");
        Add("S_LikedRemoved",
            "Retiré des Titres likés : {0}.",
            "Removed from Liked Songs: {0}.",
            "Aus den Lieblingssongs entfernt: {0}.",
            "Verwijderd uit Gelikete nummers: {0}.",
            "Quitada de Tus me gusta: {0}.",
            "Rimosso dai Brani che ti piacciono: {0}.");
        Add("S_LikedNotPlaying",
            "Spotify ne joue plus sur cette enceinte.",
            "Spotify is no longer playing on this speaker.",
            "Spotify spielt auf diesem Lautsprecher nicht mehr.",
            "Spotify speelt niet meer op deze speaker.",
            "Spotify ya no suena en este altavoz.",
            "Spotify non è più in riproduzione su questo diffusore.");
        Add("S_LikedNoTrack",
            "Aucun titre Spotify en cours.",
            "No Spotify song is playing.",
            "Es läuft kein Spotify-Titel.",
            "Er speelt geen Spotify-nummer.",
            "No suena ninguna canción de Spotify.",
            "Nessun brano Spotify in riproduzione.");
        Add("S_LikedNotSong",
            "Ce qui joue n'est pas un morceau (podcast, publicité…).",
            "What is playing is not a song (podcast, ad…).",
            "Was läuft, ist kein Song (Podcast, Werbung…).",
            "Wat er speelt is geen nummer (podcast, advertentie…).",
            "Lo que suena no es una canción (pódcast, anuncio…).",
            "Ciò che è in riproduzione non è un brano (podcast, pubblicità…).");
        Add("S_LikedNoEngine",
            "Cette enceinte n'a pas le moteur Spotify de STR.",
            "This speaker does not have STR's Spotify engine.",
            "Dieser Lautsprecher hat die Spotify-Engine von STR nicht.",
            "Deze speaker heeft de Spotify-engine van STR niet.",
            "Este altavoz no tiene el motor de Spotify de STR.",
            "Questo diffusore non ha il motore Spotify di STR.");
        Add("S_LikedEngineOld",
            "Le moteur Spotify de l'enceinte est trop ancien : mets STR à jour.",
            "The speaker's Spotify engine is too old: update STR.",
            "Die Spotify-Engine des Lautsprechers ist zu alt: Aktualisiere STR.",
            "De Spotify-engine van de speaker is te oud: werk STR bij.",
            "El motor de Spotify del altavoz es demasiado antiguo: actualiza STR.",
            "Il motore Spotify del diffusore è troppo vecchio: aggiorna STR.");
        Add("S_LikedNoSession",
            "Le moteur Spotify de l'enceinte n'est connecté à aucun compte.",
            "The speaker's Spotify engine is not logged in to any account.",
            "Die Spotify-Engine des Lautsprechers ist bei keinem Konto angemeldet.",
            "De Spotify-engine van de speaker is bij geen enkel account aangemeld.",
            "El motor de Spotify del altavoz no ha iniciado sesión en ninguna cuenta.",
            "Il motore Spotify del diffusore non è collegato a nessun account.");
        Add("S_LikedRefusedBySpotify",
            "Spotify a refusé la demande.",
            "Spotify refused the request.",
            "Spotify hat die Anfrage abgelehnt.",
            "Spotify weigerde het verzoek.",
            "Spotify rechazó la solicitud.",
            "Spotify ha rifiutato la richiesta.");
        Add("S_LikedRateLimited",
            "Trop de demandes à Spotify : réessaie dans un instant.",
            "Too many requests to Spotify: try again in a moment.",
            "Zu viele Anfragen an Spotify: Versuche es gleich noch einmal.",
            "Te veel verzoeken aan Spotify: probeer het zo opnieuw.",
            "Demasiadas solicitudes a Spotify: vuelve a intentarlo en un momento.",
            "Troppe richieste a Spotify: riprova tra un momento.");
        Add("S_LikedTimeout",
            "Spotify n'a pas répondu à temps.",
            "Spotify did not answer in time.",
            "Spotify hat nicht rechtzeitig geantwortet.",
            "Spotify antwoordde niet op tijd.",
            "Spotify no respondió a tiempo.",
            "Spotify non ha risposto in tempo.");
        Add("S_LikedFailed",
            "Échec ({0}).",
            "Failed ({0}).",
            "Fehlgeschlagen ({0}).",
            "Mislukt ({0}).",
            "Error ({0}).",
            "Non riuscito ({0}).");
        Add("S_LikedUnreachable",
            "L'enceinte ne répond pas.",
            "The speaker does not answer.",
            "Der Lautsprecher antwortet nicht.",
            "De speaker antwoordt niet.",
            "El altavoz no responde.",
            "Il diffusore non risponde.");
        Add("L_JoinPlaying",
            "{0} joue « {1} ».",
            "{0} is playing “{1}”.",
            "{0} spielt „{1}“.",
            "{0} speelt “{1}”.",
            "{0} está reproduciendo «{1}».",
            "{0} sta riproducendo “{1}”.");
        Add("L_JoinPaused",
            "{0} est en pause sur « {1} ».",
            "{0} is paused on “{1}”.",
            "{0} ist bei „{1}“ pausiert.",
            "{0} staat gepauzeerd op “{1}”.",
            "{0} está en pausa en «{1}».",
            "{0} è in pausa su “{1}”.");
        Add("L_JoinThenPlays",
            "L'ajouter au groupe arrête cette écoute pour jouer « {0} ».",
            "Adding it to the group stops that to play “{0}”.",
            "Beim Hinzufügen zur Gruppe endet das, und „{0}“ läuft.",
            "Toevoegen aan de groep stopt dat om “{0}” te spelen.",
            "Añadirlo al grupo detiene eso para reproducir «{0}».",
            "Aggiungerlo al gruppo interrompe questo ascolto per riprodurre “{0}”.");
        Add("L_JoinThenStops",
            "L'ajouter au groupe arrête cette écoute.",
            "Adding it to the group stops that.",
            "Beim Hinzufügen zur Gruppe endet das.",
            "Toevoegen aan de groep stopt dat.",
            "Añadirlo al grupo detiene eso.",
            "Aggiungerlo al gruppo interrompe questo ascolto.");
        Add("L_JoinGo",
            "Ajouter",
            "Add",
            "Hinzufügen",
            "Toevoegen",
            "Añadir",
            "Aggiungi");
        Add("S_RecSizeLimit",
            "Enregistrement arrêté : le flux arrive anormalement vite et remplirait le téléphone. {0} gardées.",
            "Recording stopped: the stream arrives abnormally fast and would fill the phone. {0} kept.",
            "Aufnahme gestoppt: Der Stream kommt ungewöhnlich schnell und würde das Telefon füllen. {0} behalten.",
            "Opname gestopt: de stream komt abnormaal snel binnen en zou de telefoon vullen. {0} bewaard.",
            "Grabación detenida: el flujo llega anormalmente rápido y llenaría el teléfono. Se conservan {0}.",
            "Registrazione fermata: il flusso arriva in modo anomalo e riempirebbe il telefono. {0} conservati.");
        Add("S_RecOffline",
            "Enceinte injoignable : l'enregistrement n'est pas possible pour l'instant. Tes enregistrements restent disponibles.",
            "Speaker unreachable: recording is not possible for now. Your recordings stay available.",
            "Lautsprecher nicht erreichbar: Aufnehmen ist im Moment nicht möglich. Deine Aufnahmen bleiben verfügbar.",
            "Speaker onbereikbaar: opnemen is nu niet mogelijk. Je opnames blijven beschikbaar.",
            "Altavoz inaccesible: por ahora no se puede grabar. Tus grabaciones siguen disponibles.",
            "Diffusore irraggiungibile: per ora non è possibile registrare. Le tue registrazioni restano disponibili.");
        Add("S_RecNoSpeaker",
            "Aucune enceinte connectée : l'enregistrement n'est pas possible. Tes enregistrements restent disponibles.",
            "No speaker connected: recording is not possible. Your recordings stay available.",
            "Kein Lautsprecher verbunden: Aufnehmen ist nicht möglich. Deine Aufnahmen bleiben verfügbar.",
            "Geen speaker verbonden: opnemen is niet mogelijk. Je opnames blijven beschikbaar.",
            "Ningún altavoz conectado: no se puede grabar. Tus grabaciones siguen disponibles.",
            "Nessun diffusore collegato: non è possibile registrare. Le tue registrazioni restano disponibili.");
        Add("S_RecNotPlaying",
            "Aucune radio internet en cours.",
            "No internet radio playing.",
            "Es läuft kein Internetradio.",
            "Er speelt geen internetradio.",
            "No se está reproduciendo ninguna radio por internet.",
            "Nessuna radio internet in riproduzione.");
        Add("S_RecNotRadio",
            "{0} : enregistrement impossible.",
            "{0}: recording not possible.",
            "{0}: Aufnahme nicht möglich.",
            "{0}: opnemen niet mogelijk.",
            "{0}: no se puede grabar.",
            "{0}: registrazione non possibile.");
        Add("S_RecNoUrl",
            "Le flux de cette station n'est pas accessible.",
            "This station's stream is not reachable.",
            "Der Stream dieses Senders ist nicht erreichbar.",
            "De stream van deze zender is niet bereikbaar.",
            "El flujo de esta emisora no está accesible.",
            "Il flusso di questa stazione non è raggiungibile.");
        Add("S_RecHls",
            "Flux HLS non pris en charge.",
            "HLS streams are not supported.",
            "HLS-Streams werden nicht unterstützt.",
            "HLS-streams worden niet ondersteund.",
            "Los flujos HLS no son compatibles.",
            "I flussi HLS non sono supportati.");
        Add("S_RecNotAudio",
            "Ce flux n'est pas un fichier audio.",
            "This stream is not audio.",
            "Dieser Stream ist kein Audio.",
            "Deze stream is geen audio.",
            "Este flujo no es audio.",
            "Questo flusso non è audio.");
        Add("S_RecFull",
            "10 enregistrements : supprime-en un pour en faire un nouveau.",
            "10 recordings: delete one to make a new one.",
            "10 Aufnahmen: Lösche eine, um eine neue zu machen.",
            "10 opnames: verwijder er een om een nieuwe te maken.",
            "10 grabaciones: elimina una para hacer otra.",
            "10 registrazioni: eliminane una per farne una nuova.");
        Add("S_RecNetworkDetail",
            "Impossible d'ouvrir le flux ({0}).",
            "Could not open the stream ({0}).",
            "Der Stream ließ sich nicht öffnen ({0}).",
            "De stream kon niet worden geopend ({0}).",
            "No se pudo abrir el flujo ({0}).",
            "Impossibile aprire il flusso ({0}).");
        Add("S_RecNetwork",
            "Impossible d'ouvrir le flux.",
            "Could not open the stream.",
            "Der Stream konnte nicht geöffnet werden.",
            "De stream kon niet worden geopend.",
            "No se pudo abrir el flujo.",
            "Impossibile aprire il flusso.");
        Add("S_RecBusy",
            "Un enregistrement est déjà en cours.",
            "A recording is already in progress.",
            "Es läuft bereits eine Aufnahme.",
            "Er loopt al een opname.",
            "Ya hay una grabación en curso.",
            "È già in corso una registrazione.");
        Add("S_RecSaved",
            "Enregistré : {0}.",
            "Recorded: {0}.",
            "Aufgenommen: {0}.",
            "Opgenomen: {0}.",
            "Grabado: {0}.",
            "Registrato: {0}.");
        Add("S_RecSavedMax",
            "Enregistré : 5 min (maximum atteint).",
            "Recorded: 5 min (maximum reached).",
            "Aufgenommen: 5 Min. (Maximum erreicht).",
            "Opgenomen: 5 min (maximum bereikt).",
            "Grabado: 5 min (máximo alcanzado).",
            "Registrato: 5 min (massimo raggiunto).");
        Add("S_RecInterrupted",
            "Enregistrement interrompu (réseau) – {0} gardées.",
            "Recording interrupted (network) – {0} kept.",
            "Aufnahme unterbrochen (Netzwerk) – {0} behalten.",
            "Opname onderbroken (netwerk) – {0} bewaard.",
            "Grabación interrumpida (red): se conservan {0}.",
            "Registrazione interrotta (rete) – {0} conservati.");
        Add("S_RecTooShort",
            "Enregistrement trop court, il n'a pas été gardé.",
            "Recording too short, it was not kept.",
            "Aufnahme zu kurz, sie wurde nicht behalten.",
            "Opname te kort, ze is niet bewaard.",
            "Grabación demasiado corta, no se ha conservado.",
            "Registrazione troppo breve, non è stata conservata.");
        Add("S_RecDeleted",
            "Enregistrement supprimé.",
            "Recording deleted.",
            "Aufnahme gelöscht.",
            "Opname verwijderd.",
            "Grabación eliminada.",
            "Registrazione eliminata.");

        Add("L_Recent", "ÉCOUTÉ RÉCEMMENT", "RECENTLY PLAYED", "ZULETZT GEHÖRT", "ONLANGS AFGESPEELD", "ESCUCHADO RECIENTEMENTE", "ASCOLTATI DI RECENTE");
        Add("L_NoRecent",
            "Rien dans l'historique de l'agent.",
            "Nothing in the agent's history.",
            "Nichts im Verlauf des Agenten.",
            "Niets in de geschiedenis van de agent.",
            "Nada en el historial del agente.",
            "Nulla nella cronologia dell'agente.");

        // ---------------------------------------------------------- Page Multi-pièces
        Add("L_Master", "ENCEINTE MAÎTRE", "MASTER SPEAKER", "HAUPTLAUTSPRECHER", "HOOFDSPEAKER", "ALTAVOZ PRINCIPAL", "DIFFUSORE PRINCIPALE");
        Add("L_MasterNote",
            "C'est elle qui diffuse le flux ; les autres le suivent. Change d'enceinte active pour changer de maître.",
            "It broadcasts the stream; the others follow it. Switch the active speaker to change the master.",
            "Er sendet den Stream; die anderen folgen ihm. Wechsle den aktiven Lautsprecher, um den Hauptlautsprecher zu ändern.",
            "Deze speaker zendt de stream uit; de andere volgen. Wissel van actieve speaker om de hoofdspeaker te wijzigen.",
            "Es el que difunde el flujo; los demás lo siguen. Cambia de altavoz activo para cambiar de principal.",
            "Trasmette il flusso; gli altri lo seguono. Cambia il diffusore attivo per cambiare il principale.");
        Add("L_NotMasterNote",
            "L'enceinte pilotée suit ce groupe, elle ne le diffuse pas. Les commandes de groupe s'adressent au maître ci-dessus.",
            "The controlled speaker follows this group, it does not broadcast it. Group commands are addressed to the master above.",
            "Der gesteuerte Lautsprecher folgt dieser Gruppe, er sendet sie nicht. Gruppenbefehle gehen an den Master oben.",
            "De bediende speaker volgt deze groep, hij zendt ze niet uit. Groepsopdrachten gaan naar de master hierboven.",
            "El altavoz controlado sigue este grupo, no lo emite. Los mandos de grupo se dirigen al maestro de arriba.",
            "Il diffusore controllato segue questo gruppo, non lo trasmette. I comandi del gruppo vanno al principale qui sopra.");
        Add("L_SpeakersToGroup", "ENCEINTES À GROUPER", "SPEAKERS TO GROUP", "ZU GRUPPIERENDE LAUTSPRECHER", "SPEAKERS OM TE GROEPEREN", "ALTAVOCES A AGRUPAR", "DIFFUSORI DA RAGGRUPPARE");
        Add("L_NoOtherSpeakers",
            "Aucune autre enceinte connue. Lance un balayage dans Réglages.",
            "No other speaker known. Run a scan in Settings.",
            "Kein weiterer Lautsprecher bekannt. Starte einen Suchlauf unter Einstellungen.",
            "Geen andere speaker bekend. Start een scan bij Instellingen.",
            "No se conoce ningún otro altavoz. Lanza un escaneo en Ajustes.",
            "Nessun altro diffusore conosciuto. Avvia una ricerca nelle Impostazioni.");
        Add("L_PermanentGroup",
            "Groupe permanent (reformé à la prochaine lecture)",
            "Permanent group (re-formed on the next play)",
            "Dauerhafte Gruppe (wird bei der nächsten Wiedergabe neu gebildet)",
            "Permanente groep (wordt bij de volgende weergave hersteld)",
            "Grupo permanente (se recrea en la próxima reproducción)",
            "Gruppo permanente (ricomposto alla prossima riproduzione)");
        Add("L_FormGroup", "Grouper", "Group", "Gruppieren", "Groeperen", "Agrupar", "Raggruppa");
        Add("L_Dissolve", "Dissoudre", "Dissolve", "Auflösen", "Ontbinden", "Disolver", "Sciogli");
        Add("L_GroupVolume", "VOLUME DU GROUPE", "GROUP VOLUME", "GRUPPENLAUTSTÄRKE", "GROEPSVOLUME", "VOLUMEN DEL GRUPO", "VOLUME DEL GRUPPO");
        Add("L_ApplyToGroup", "Appliquer à tout le groupe", "Apply to the whole group", "Auf die ganze Gruppe anwenden", "Op de hele groep toepassen", "Aplicar a todo el grupo", "Applica a tutto il gruppo");
        Add("L_NoActiveGroup", "Pas de groupe actif.", "No active group.", "Keine aktive Gruppe.", "Geen actieve groep.", "Sin grupo activo.", "Nessun gruppo attivo.");

        // ---------------------------------------------------------- Page Réglages
        // « Sélectionnée » et non « active » : dans un groupe, l'enceinte désignée
        // n'est pas celle qui reçoit les commandes.
        Add("L_ActiveSpeaker", "ENCEINTE SÉLECTIONNÉE", "SELECTED SPEAKER", "AUSGEWÄHLTER LAUTSPRECHER", "GEKOZEN SPEAKER", "ALTAVOZ SELECCIONADO", "DIFFUSORE SELEZIONATO");
        Add("L_CommandsGoTo",
            "Elle suit un groupe : les commandes vont à {0}, qui diffuse.",
            "It follows a group: commands go to {0}, which does the playing.",
            "Er folgt einer Gruppe: Befehle gehen an {0}, der wiedergibt.",
            "Hij volgt een groep: opdrachten gaan naar {0}, die afspeelt.",
            "Sigue a un grupo: los comandos van a {0}, que reproduce.",
            "Segue un gruppo: i comandi vanno a {0}, che si occupa della riproduzione.");
        Add("L_NoSpeakerSelected", "Aucune enceinte sélectionnée", "No speaker selected", "Kein Lautsprecher ausgewählt", "Geen speaker geselecteerd", "Ningún altavoz seleccionado", "Nessun diffusore selezionato");
        Add("L_Search", "RECHERCHE", "SEARCH", "SUCHE", "ZOEKEN", "BÚSQUEDA", "RICERCA");
        Add("L_ScanButton", "Balayer le réseau local", "Scan the local network", "Lokales Netzwerk durchsuchen", "Lokaal netwerk scannen", "Escanear la red local", "Cerca nella rete locale");
        Add("L_ScanNote",
            "Le balayage interroge chaque adresse du sous-réseau sur le port 8090. Le téléphone doit être sur le même Wi-Fi que les enceintes.",
            "The scan probes every address of the subnet on port 8090. The phone must be on the same Wi-Fi as the speakers.",
            "Der Suchlauf prüft jede Adresse des Subnetzes auf Port 8090. Das Telefon muss im selben WLAN sein wie die Lautsprecher.",
            "De scan bevraagt elk adres van het subnet op poort 8090. De telefoon moet op dezelfde wifi zitten als de speakers.",
            "El escaneo consulta cada dirección de la subred en el puerto 8090. El teléfono debe estar en la misma wifi que los altavoces.",
            "La ricerca interroga ogni indirizzo della sottorete sulla porta 8090. Il telefono deve essere sulla stessa rete Wi-Fi dei diffusori.");
        Add("L_Add", "Ajouter", "Add", "Hinzufügen", "Toevoegen", "Añadir", "Aggiungi");
        Add("L_KnownSpeakers", "ENCEINTES CONNUES", "KNOWN SPEAKERS", "BEKANNTE LAUTSPRECHER", "BEKENDE SPEAKERS", "ALTAVOCES CONOCIDOS", "DIFFUSORI CONOSCIUTI");
        Add("L_NoKnownSpeakers",
            "Aucune enceinte connue. Lance un balayage.",
            "No speaker known yet. Run a scan.",
            "Noch kein Lautsprecher bekannt. Starte einen Suchlauf.",
            "Nog geen speaker bekend. Start een scan.",
            "Aún no hay altavoces. Lanza un escaneo.",
            "Ancora nessun diffusore conosciuto. Avvia una ricerca.");
        Add("L_Use", "Utiliser", "Use", "Verwenden", "Gebruiken", "Usar", "Usa");

        // ------------------------------------------- Enceintes sans agent STR
        Add("L_ReadyForStr",
            "Sans STR — prête pour l'installation",
            "No STR — ready for installation",
            "Ohne STR — bereit zur Installation",
            "Zonder STR — klaar voor installatie",
            "Sin STR — lista para instalar",
            "Senza STR — pronto per l'installazione");
        Add("L_StrMissingTitle", "AGENT STR MANQUANT", "STR AGENT MISSING", "STR-AGENT FEHLT", "STR-AGENT ONTBREEKT", "FALTA EL AGENTE STR", "AGENTE STR MANCANTE");
        Add("L_StrMissingOne",
            "Une enceinte connue n'a pas l'agent STR.",
            "One known speaker has no STR agent.",
            "Ein bekannter Lautsprecher hat keinen STR-Agenten.",
            "Eén bekende speaker heeft geen STR-agent.",
            "Un altavoz conocido no tiene el agente STR.",
            "Un diffusore conosciuto non ha l'agente STR.");
        Add("L_StrMissingMany",
            "{0} enceintes connues n'ont pas l'agent STR.",
            "{0} known speakers have no STR agent.",
            "{0} bekannte Lautsprecher haben keinen STR-Agenten.",
            "{0} bekende speakers hebben geen STR-agent.",
            "{0} altavoces conocidos no tienen el agente STR.",
            "{0} diffusori conosciuti non hanno l'agente STR.");
        Add("L_StrMissingBody",
            "Sans l'agent, seules les fonctions du firmware restent : volume, graves, touches déjà enregistrées. La recherche de stations, l'enregistrement des touches et le groupage fiable passent par STR.\n\nL'installation ne peut pas se faire depuis le téléphone : elle ouvre un accès système sur l'enceinte, y copie l'agent et la redémarre. Elle se fait depuis l'outil STR sur PC ou Mac, qui gère aussi les cas de récupération.",
            "Without the agent, only firmware features remain: volume, bass, presets already stored. Station search, saving presets and reliable grouping all go through STR.\n\nInstalling cannot be done from the phone: it opens a system access on the speaker, copies the agent onto it and reboots it. It is done with the STR tool on PC or Mac, which also handles recovery cases.",
            "Ohne den Agenten bleiben nur Firmware-Funktionen: Lautstärke, Bass, bereits gespeicherte Tasten. Sendersuche, Tastenbelegung und zuverlässige Gruppen laufen über STR.\n\nDie Installation ist vom Telefon aus nicht möglich: sie öffnet einen Systemzugang am Lautsprecher, kopiert den Agenten darauf und startet ihn neu. Das macht das STR-Werkzeug für PC oder Mac, das auch Wiederherstellungsfälle abdeckt.",
            "Zonder de agent blijven alleen firmwarefuncties over: volume, bass, al opgeslagen toetsen. Zenders zoeken, toetsen opslaan en betrouwbaar groeperen gaan via STR.\n\nInstalleren kan niet vanaf de telefoon: het opent een systeemtoegang op de speaker, kopieert de agent erop en herstart hem. Dat doet het STR-programma op pc of Mac, dat ook herstelgevallen aankan.",
            "Sin el agente solo quedan las funciones del firmware: volumen, graves, teclas ya guardadas. Buscar emisoras, guardar teclas y agrupar de forma fiable pasan por STR.\n\nLa instalación no puede hacerse desde el teléfono: abre un acceso de sistema en el altavoz, copia el agente y lo reinicia. Se hace con la herramienta STR para PC o Mac, que también cubre los casos de recuperación.",
            "Senza l'agente restano solo le funzioni del firmware: volume, bassi, preselezioni già memorizzate. La ricerca delle stazioni, il salvataggio delle preselezioni e i gruppi affidabili passano tutti da STR.\n\nL'installazione non si può fare dal telefono: apre un accesso di sistema sul diffusore, vi copia l'agente e lo riavvia. Si fa con lo strumento STR su PC o Mac, che gestisce anche i casi di ripristino.");
        Add("L_StrMissingOpen",
            "Voir comment installer STR",
            "See how to install STR",
            "So installiert man STR",
            "Zie hoe je STR installeert",
            "Ver cómo instalar STR",
            "Vedi come installare STR");
        Add("L_Diagnostics", "DIAGNOSTIC", "DIAGNOSTICS", "DIAGNOSE", "DIAGNOSE", "DIAGNÓSTICO", "DIAGNOSTICA");
        Add("L_DiagnoseButton", "Interroger l'enceinte active", "Query the active speaker", "Aktiven Lautsprecher abfragen", "Actieve speaker bevragen", "Consultar el altavoz activo", "Interroga il diffusore attivo");
        Add("L_Language", "LANGUE", "LANGUAGE", "SPRACHE", "TAAL", "IDIOMA", "LINGUA");
        Add("L_LanguageNote",
            "Le changement est immédiat et retenu pour les prochains lancements.",
            "The change takes effect at once and is kept for future launches.",
            "Die Änderung wirkt sofort und bleibt für künftige Starts erhalten.",
            "De wijziging werkt meteen en blijft bewaard voor volgende keren.",
            "El cambio se aplica de inmediato y se conserva para próximos inicios.",
            "La modifica vale subito e viene mantenuta ai prossimi avvii.");

        // ---------------------------------------------------------- Messages d'état
        Add("S_NoSpeaker",
            "Aucune enceinte sélectionnée. Va dans Réglages pour en chercher une.",
            "No speaker selected. Go to Settings to find one.",
            "Kein Lautsprecher ausgewählt. Suche einen unter Einstellungen.",
            "Geen speaker geselecteerd. Zoek er een bij Instellingen.",
            "Ningún altavoz seleccionado. Busca uno en Ajustes.",
            "Nessun diffusore selezionato. Vai nelle Impostazioni per trovarne uno.");
        Add("S_Timeout",
            "L'enceinte n'a pas répondu à temps.",
            "The speaker did not answer in time.",
            "Der Lautsprecher hat nicht rechtzeitig geantwortet.",
            "De speaker antwoordde niet op tijd.",
            "El altavoz no respondió a tiempo.",
            "Il diffusore non ha risposto in tempo.");
        Add("S_PowerOnRefused",
            "L'enceinte n'a pas répondu à l'allumage. Si elle fait partie d'un groupe, elle suit l'enceinte maître : allume plutôt celle-ci, ou dissous le groupe.",
            "The speaker did not come on. If it belongs to a group it follows the master speaker: turn that one on instead, or dissolve the group.",
            "Der Lautsprecher ist nicht angegangen. Gehört er zu einer Gruppe, folgt er dem Master: schalte stattdessen diesen ein oder löse die Gruppe auf.",
            "De speaker ging niet aan. Hoort hij bij een groep, dan volgt hij de masterspeaker: zet die aan of ontbind de groep.",
            "El altavoz no se ha encendido. Si pertenece a un grupo, sigue al altavoz maestro: enciende ese, o disuelve el grupo.",
            "Il diffusore non si è acceso. Se fa parte di un gruppo segue il diffusore principale: accendi quello, oppure sciogli il gruppo.");
        Add("S_Unreachable",
            "Enceinte injoignable. Même réseau Wi-Fi ?",
            "Speaker unreachable. Same Wi-Fi network?",
            "Lautsprecher nicht erreichbar. Gleiches WLAN?",
            "Speaker onbereikbaar. Zelfde wifi-netwerk?",
            "Altavoz inaccesible. ¿Misma red wifi?",
            "Diffusore irraggiungibile. Stessa rete Wi-Fi?");
        Add("S_Scanning", "Balayage du réseau local…", "Scanning the local network…", "Lokales Netzwerk wird durchsucht…", "Lokaal netwerk scannen…", "Escaneando la red local…", "Ricerca nella rete locale…");
        Add("S_NoneFound",
            "Aucune enceinte trouvée. Vérifie que le téléphone est sur le même Wi-Fi (pas en 4G, pas sur un réseau invité).",
            "No speaker found. Check the phone is on the same Wi-Fi (not on mobile data, not on a guest network).",
            "Kein Lautsprecher gefunden. Prüfe, ob das Telefon im selben WLAN ist (nicht mobil, nicht im Gastnetz).",
            "Geen speaker gevonden. Controleer of de telefoon op dezelfde wifi zit (niet op mobiel, niet op een gastnetwerk).",
            "No se encontró ningún altavoz. Comprueba que el teléfono esté en la misma wifi (no en datos móviles ni en red de invitados).",
            "Nessun diffusore trovato. Verifica che il telefono sia sulla stessa rete Wi-Fi (non sui dati mobili, non su una rete ospiti).");
        Add("S_OneFound", "1 enceinte trouvée.", "1 speaker found.", "1 Lautsprecher gefunden.", "1 speaker gevonden.", "1 altavoz encontrado.", "1 diffusore trovato.");
        Add("S_ManyFound", "{0} enceintes trouvées.", "{0} speakers found.", "{0} Lautsprecher gefunden.", "{0} speakers gevonden.", "{0} altavoces encontrados.", "{0} diffusori trovati.");
        Add("S_EnterIp",
            "Saisis l'adresse IP de l'enceinte, par exemple 192.168.1.42.",
            "Enter the speaker's IP address, for example 192.168.1.42.",
            "Gib die IP-Adresse des Lautsprechers ein, zum Beispiel 192.168.1.42.",
            "Voer het IP-adres van de speaker in, bijvoorbeeld 192.168.1.42.",
            "Escribe la dirección IP del altavoz, por ejemplo 192.168.1.42.",
            "Inserisci l'indirizzo IP del diffusore, ad esempio 192.168.1.42.");
        Add("S_NothingAt",
            "Rien ne répond à l'API SoundTouch sur {0}:8090.",
            "Nothing answers the SoundTouch API at {0}:8090.",
            "Auf {0}:8090 antwortet keine SoundTouch-API.",
            "Niets antwoordt op de SoundTouch-API op {0}:8090.",
            "Nada responde a la API SoundTouch en {0}:8090.",
            "Nessuna risposta dall'API SoundTouch su {0}:8090.");
        Add("S_Added", "{0} ajoutée.", "{0} added.", "{0} hinzugefügt.", "{0} toegevoegd.", "{0} añadido.", "{0} aggiunto.");
        Add("S_Connected", "Connecté à {0}.", "Connected to {0}.", "Verbunden mit {0}.", "Verbonden met {0}.", "Conectado a {0}.", "Connesso a {0}.");
        Add("S_Removed", "{0} retirée de la liste.", "{0} removed from the list.", "{0} aus der Liste entfernt.", "{0} uit de lijst verwijderd.", "{0} eliminado de la lista.", "{0} rimosso dall'elenco.");
        Add("S_SelectFirst",
            "Sélectionne d'abord une enceinte.",
            "Select a speaker first.",
            "Wähle zuerst einen Lautsprecher.",
            "Selecteer eerst een speaker.",
            "Selecciona primero un altavoz.",
            "Seleziona prima un diffusore.");
        Add("S_PresetStarted", "{0} lancée", "{0} started", "{0} gestartet", "{0} gestart", "{0} iniciada", "{0} avviata");
        Add("S_NoPresets",
            "Aucune présélection enregistrée.",
            "No preset stored.",
            "Kein Preset gespeichert.",
            "Geen preset opgeslagen.",
            "Ninguna presintonía guardada.",
            "Nessuna preselezione memorizzata.");
        Add("S_SourceSet", "Source : {0}", "Source: {0}", "Quelle: {0}", "Bron: {0}", "Fuente: {0}", "Sorgente: {0}");
        Add("S_StandbySet", "Mise en veille", "Going to standby", "Wird in Standby versetzt", "Naar stand-by", "Poniendo en reposo", "Passaggio in standby");
        Add("S_StreamStarted", "Flux lancé", "Stream started", "Stream gestartet", "Stream gestart", "Flujo iniciado", "Flusso avviato");
        Add("S_EnterStream",
            "Saisis l'adresse d'un flux.",
            "Enter a stream address.",
            "Gib eine Stream-Adresse ein.",
            "Voer een streamadres in.",
            "Escribe la dirección de un flujo.",
            "Inserisci l'indirizzo di un flusso.");
        Add("S_NeedAgentUrl",
            "Jouer une URL demande l'agent STR (l'ancien point /speaker du firmware est mort).",
            "Playing a URL needs the STR agent (the firmware's old /speaker endpoint is dead).",
            "Eine URL abzuspielen erfordert den STR-Agenten (der alte /speaker-Endpunkt der Firmware ist tot).",
            "Een URL afspelen vereist de STR-agent (het oude /speaker-eindpunt van de firmware is dood).",
            "Reproducir una URL requiere el agente STR (el antiguo endpoint /speaker del firmware está muerto).",
            "Per riprodurre un URL serve l'agente STR (il vecchio endpoint /speaker del firmware non funziona più).");
        Add("S_NeedAgentReplay",
            "Rejouer un flux demande l'agent STR.",
            "Replaying a stream needs the STR agent.",
            "Einen Stream erneut abzuspielen erfordert den STR-Agenten.",
            "Een stream opnieuw afspelen vereist de STR-agent.",
            "Volver a reproducir un flujo requiere el agente STR.",
            "Per riascoltare un flusso serve l'agente STR.");
        Add("S_NotReplayable",
            "Cette entrée ne porte pas d'adresse rejouable.",
            "This entry carries no replayable address.",
            "Dieser Eintrag enthält keine erneut abspielbare Adresse.",
            "Dit item bevat geen herspeelbaar adres.",
            "Esta entrada no tiene una dirección reproducible.",
            "Questa voce non ha un indirizzo riascoltabile.");
        Add("S_Replaying", "Relance : {0}", "Replaying: {0}", "Erneut: {0}", "Opnieuw: {0}", "Repitiendo: {0}", "Riascolto: {0}");
        Add("S_CheckOne",
            "Coche au moins une enceinte à ajouter au groupe.",
            "Tick at least one speaker to add to the group.",
            "Wähle mindestens einen Lautsprecher für die Gruppe aus.",
            "Vink minstens één speaker aan om toe te voegen.",
            "Marca al menos un altavoz para añadir al grupo.",
            "Spunta almeno un diffusore da aggiungere al gruppo.");
        Add("S_OnlyOneSpeaker",
            "Une seule enceinte connue. Lance un balayage dans Réglages pour en trouver d'autres.",
            "Only one speaker known. Run a scan in Settings to find others.",
            "Nur ein Lautsprecher bekannt. Starte einen Suchlauf unter Einstellungen.",
            "Slechts één speaker bekend. Start een scan bij Instellingen.",
            "Solo se conoce un altavoz. Lanza un escaneo en Ajustes.",
            "Un solo diffusore conosciuto. Avvia una ricerca nelle Impostazioni per trovarne altri.");
        Add("S_GroupFormed", "Groupe formé", "Group formed", "Gruppe gebildet", "Groep gevormd", "Grupo formado", "Gruppo formato");
        Add("S_GroupDissolved", "Groupe dissous", "Group dissolved", "Gruppe aufgelöst", "Groep ontbonden", "Grupo disuelto", "Gruppo sciolto");
        Add("S_GroupStillUp",
            "L'enceinte a gardé le groupe : {0} membre(s) encore liés.",
            "The speaker kept the group: {0} member(s) still linked.",
            "Der Lautsprecher hat die Gruppe behalten: {0} Mitglied(er) noch verbunden.",
            "De speaker hield de groep aan: {0} lid/leden nog verbonden.",
            "El altavoz mantuvo el grupo: {0} miembro(s) aún enlazados.",
            "Il diffusore ha mantenuto il gruppo: {0} membro/i ancora collegati.");
        Add("S_StereoPairKept",
            "Le groupe est défait, la paire stéréo reste active.",
            "The group is undone; the stereo pair is still active.",
            "Die Gruppe ist aufgelöst, das Stereopaar bleibt bestehen.",
            "De groep is opgeheven, het stereopaar blijft actief.",
            "El grupo está deshecho; la pareja estéreo sigue activa.",
            "Il gruppo è sciolto; la coppia stereo è ancora attiva.");
        Add("S_ZoneUnreadable",
            "Zone illisible : rien n'a été modifié, pour ne pas perdre le groupe enregistré.",
            "Zone unreadable: nothing was changed, so the saved group is not lost.",
            "Zone nicht lesbar: nichts geändert, damit die gespeicherte Gruppe erhalten bleibt.",
            "Zone onleesbaar: er is niets gewijzigd, zodat de opgeslagen groep blijft.",
            "Zona ilegible: no se cambió nada, para no perder el grupo guardado.",
            "Zona illeggibile: non è stato modificato nulla, quindi il gruppo salvato non va perso.");
        Add("S_GroupVolumeApplied", "Volume du groupe appliqué", "Group volume applied", "Gruppenlautstärke angewendet", "Groepsvolume toegepast", "Volumen de grupo aplicado", "Volume del gruppo applicato");
        Add("S_NeedAgentGroupVolume",
            "Le volume de groupe demande l'agent STR.",
            "Group volume needs the STR agent.",
            "Die Gruppenlautstärke erfordert den STR-Agenten.",
            "Groepsvolume vereist de STR-agent.",
            "El volumen de grupo requiere el agente STR.",
            "Il volume del gruppo richiede l'agente STR.");
        Add("S_MissingDeviceId",
            "Identifiant manquant pour {0}. Relance un balayage dans Réglages.",
            "Missing device id for {0}. Run a scan again in Settings.",
            "Fehlende Geräte-ID für {0}. Starte erneut einen Suchlauf unter Einstellungen.",
            "Ontbrekende apparaat-id voor {0}. Start opnieuw een scan bij Instellingen.",
            "Falta el identificador de {0}. Vuelve a lanzar un escaneo en Ajustes.",
            "Identificativo mancante per {0}. Avvia di nuovo una ricerca nelle Impostazioni.");

        // ---------------------------------------------------------- Transport et thème
        Add("L_NowPlaying", "EN LECTURE", "NOW PLAYING", "WIEDERGABE", "SPEELT NU", "EN REPRODUCCIÓN", "IN RIPRODUZIONE");
        Add("L_Stop", "Arrêt", "Stop", "Stopp", "Stop", "Parar", "Stop");
        Add("L_PowerOnAction", "Allumer", "Power on", "Einschalten", "Aanzetten", "Encender", "Accendi");
        Add("L_PowerOffAction", "Veille", "Standby", "Standby", "Stand-by", "Reposo", "Standby");
        Add("L_GroupWith", "GROUPER AVEC", "GROUP WITH", "GRUPPIEREN MIT", "GROEPEREN MET", "AGRUPAR CON", "RAGGRUPPA CON");
        Add("L_Theme", "THÈME", "THEME", "DESIGN", "THEMA", "TEMA", "TEMA");
        Add("L_ThemeSystem", "Système", "System", "System", "Systeem", "Sistema", "Sistema");
        Add("L_ThemeLight", "Clair", "Light", "Hell", "Licht", "Claro", "Chiaro");
        Add("L_ThemeDark", "Foncé", "Dark", "Dunkel", "Donker", "Oscuro", "Scuro");
        Add("L_ThemeNote",
            "« Système » suit le réglage d'affichage du téléphone.",
            "\"System\" follows the phone's display setting.",
            "„System“ folgt der Anzeigeeinstellung des Telefons.",
            "\"Systeem\" volgt de weergave-instelling van de telefoon.",
            "«Sistema» sigue el ajuste de pantalla del teléfono.",
            "\"Sistema\" segue l'impostazione di visualizzazione del telefono.");
        Add("L_ClearArtCache", "Vider le cache des logos", "Clear the logo cache", "Logo-Cache leeren", "Logocache wissen", "Vaciar la caché de logos", "Svuota la cache dei loghi");
        Add("S_ArtCacheCleared",
            "Cache vidé. Les logos seront retéléchargés.",
            "Cache cleared. Logos will be downloaded again.",
            "Cache geleert. Logos werden erneut geladen.",
            "Cache gewist. Logo's worden opnieuw gedownload.",
            "Caché vaciada. Los logos se descargarán de nuevo.",
            "Cache svuotata. I loghi verranno scaricati di nuovo.");

        // ---------------------------------------------------------- Recherche de radios
        Add("L_RadioSearch", "TROUVER UNE STATION", "FIND A STATION", "SENDER SUCHEN", "EEN ZENDER ZOEKEN", "BUSCAR UNA EMISORA", "TROVA UNA STAZIONE");
        Add("L_RadioPlaceholder", "ex. Bel RTL, Nostalgie, jazz…", "e.g. BBC, NDR, jazz…", "z. B. NDR, WDR, Jazz…", "bijv. Radio 1, NPO, jazz…", "p. ej. Cadena SER, jazz…", "es. BBC, NDR, jazz…");
        Add("L_SearchButton", "Rechercher", "Search", "Suchen", "Zoeken", "Buscar", "Cerca");
        Add("L_TopStations", "Top liste", "Top list", "Top-Liste", "Toplijst", "Top lista", "Le più ascoltate");
        Add("L_Country", "Pays", "Country", "Land", "Land", "País", "Paese");
        Add("L_LanguageFilter", "Langue", "Language", "Sprache", "Taal", "Idioma", "Lingua");
        Add("L_AllCountries", "Tous les pays", "All countries", "Alle Länder", "Alle landen", "Todos los países", "Tutti i paesi");
        Add("L_AllLanguages", "Toutes les langues", "All languages", "Alle Sprachen", "Alle talen", "Todos los idiomas", "Tutte le lingue");
        Add("L_BoseOnly",
            "Compatibles Bose uniquement",
            "Bose-compatible only",
            "Nur Bose-kompatible",
            "Alleen Bose-compatibel",
            "Solo compatibles con Bose",
            "Solo compatibili Bose");
        Add("L_RadioSource",
            "Annuaire radio-browser.info",
            "radio-browser.info directory",
            "Verzeichnis radio-browser.info",
            "Gids radio-browser.info",
            "Directorio radio-browser.info",
            "elenco radio-browser.info");
        Add("L_NoResults",
            "Aucune station. Élargis la recherche ou décoche le filtre.",
            "No station. Widen the search or clear the filter.",
            "Kein Sender. Suche erweitern oder Filter abwählen.",
            "Geen zender. Verbreed de zoekopdracht of zet het filter uit.",
            "Ninguna emisora. Amplía la búsqueda o quita el filtro.",
            "Nessuna stazione. Amplia la ricerca o togli il filtro.");
        Add("L_Listen", "Écouter maintenant", "Play now", "Jetzt abspielen", "Nu afspelen", "Escuchar ahora", "Ascolta ora");
        Add("L_AssignToKey", "Affecter à la touche {0}", "Assign to key {0}", "Auf Taste {0} legen", "Toewijzen aan toets {0}", "Asignar a la tecla {0}", "Assegna al tasto {0}");
        Add("L_Cancel", "Annuler", "Cancel", "Abbrechen", "Annuleren", "Cancelar", "Annulla");
        Add("L_ChooseAction", "Que faire de cette station ?", "What to do with this station?", "Was mit diesem Sender tun?", "Wat doen met deze zender?", "¿Qué hacer con esta emisora?", "Cosa fare con questa stazione?");
        Add("S_Searching", "Recherche en cours…", "Searching…", "Suche läuft…", "Bezig met zoeken…", "Buscando…", "Ricerca…");
        Add("S_StationPlaying", "Lecture : {0}", "Playing: {0}", "Wiedergabe: {0}", "Speelt af: {0}", "Reproduciendo: {0}", "In riproduzione: {0}");
        Add("S_StationSaved", "{0} enregistrée sur la touche {1}", "{0} saved to key {1}", "{0} auf Taste {1} gespeichert", "{0} opgeslagen op toets {1}", "{0} guardada en la tecla {1}", "{0} salvata sul tasto {1}");
        Add("S_NeedAgentStation",
            "Chercher et enregistrer des stations demande l'agent STR.",
            "Finding and saving stations needs the STR agent.",
            "Sender suchen und speichern erfordert den STR-Agenten.",
            "Zenders zoeken en opslaan vereist de STR-agent.",
            "Buscar y guardar emisoras requiere el agente STR.",
            "Per trovare e salvare le stazioni serve l'agente STR.");

        // ---------------------------------------------------------- Recherche : filtres de l'application PC de STR
        Add("L_SortTitle", "Trier", "Sort", "Sortieren", "Sorteren", "Ordenar", "Ordina");
        Add("L_SortVotes", "Popularité", "Popularity", "Beliebtheit", "Populariteit", "Popularidad", "Popolarità");
        Add("L_SortClicks", "Les plus écoutées", "Most played", "Meistgehört", "Meest beluisterd", "Más escuchadas", "Più ascoltate");
        Add("L_SortTrend", "Tendance", "Trending", "Im Trend", "Trending", "Tendencia", "Di tendenza");
        Add("L_SortName", "Nom (A → Z)", "Name (A → Z)", "Name (A → Z)", "Naam (A → Z)", "Nombre (A → Z)", "Nome (A → Z)");
        Add("L_SortBitrate", "Débit", "Bitrate", "Bitrate", "Bitrate", "Tasa de bits", "Bitrate");
        Add("L_BitrateTitle", "Débit", "Bitrate", "Bitrate", "Bitrate", "Tasa de bits", "Bitrate");
        Add("L_BitrateAll", "Débit : tous", "Bitrate: any", "Bitrate: alle", "Bitrate: alle", "Tasa: todas", "Bitrate: tutti");
        Add("L_BitrateMin", "{0} kbit/s ou plus", "{0} kbit/s or more", "ab {0} kbit/s", "{0} kbit/s of meer", "{0} kbit/s o más", "{0} kbit/s o più");
        Add("L_OnlyWorking", "En service uniquement", "Working stations only", "Nur erreichbare", "Alleen werkende zenders", "Solo emisoras activas", "Solo stazioni attive");
        Add("L_AllGenres", "Tous", "All", "Alle", "Alle", "Todos", "Tutti");
        Add("L_MoreGenres", "Plus de genres ({0})", "More genres ({0})", "Mehr Genres ({0})", "Meer genres ({0})", "Más géneros ({0})", "Altri generi ({0})");
        Add("L_FewerGenres", "Moins de genres", "Fewer genres", "Weniger Genres", "Minder genres", "Menos géneros", "Meno generi");
        Add("L_GenrePop", "Pop", "Pop", "Pop", "Pop", "Pop", "Pop");
        Add("L_GenreRock", "Rock", "Rock", "Rock", "Rock", "Rock", "Rock");
        Add("L_GenreHits", "Tubes", "Hits", "Hits", "Hits", "Éxitos", "Successi");
        Add("L_GenreOldies", "Anciens succès", "Oldies", "Oldies", "Oldies", "Clásicos", "Oldies");
        Add("L_GenreJazz", "Jazz", "Jazz", "Jazz", "Jazz", "Jazz", "Jazz");
        Add("L_GenreClassical", "Classique", "Classical", "Klassik", "Klassiek", "Clásica", "Classica");
        Add("L_GenreChill", "Chill", "Chill", "Chill", "Chill", "Chill", "Chill");
        Add("L_GenreDance", "Dance", "Dance", "Dance", "Dance", "Dance", "Dance");
        Add("L_GenreElectronic", "Électronique", "Electronic", "Elektronisch", "Elektronisch", "Electrónica", "Elettronica");
        Add("L_GenreHouse", "House", "House", "House", "House", "House", "House");
        Add("L_GenreHipHop", "Hip Hop", "Hip Hop", "Hip-Hop", "Hiphop", "Hip hop", "Hip hop");
        Add("L_GenreLatin", "Latino", "Latin", "Latin", "Latin", "Latina", "Latina");
        Add("L_Genre80s", "Années 80", "80s", "80er", "Jaren 80", "Años 80", "Anni '80");
        Add("L_Genre90s", "Années 90", "90s", "90er", "Jaren 90", "Años 90", "Anni '90");
        Add("L_GenreNews", "Actualités", "News", "Nachrichten", "Nieuws", "Noticias", "Notizie");
        Add("L_ResultsFiltered",
            "{0} masquée(s) par « Compatibles Bose » ou le débit.",
            "{0} hidden by \"Bose-compatible\" or the bitrate.",
            "{0} durch „Bose-kompatibel“ oder die Bitrate ausgeblendet.",
            "{0} verborgen door „Bose-compatibel” of de bitrate.",
            "{0} ocultas por «Compatibles con Bose» o la tasa de bits.",
            "{0} nascoste da «Compatibili Bose» o dal bitrate.");
        Add("S_AllFiltered",
            "Les stations trouvées sont toutes masquées : décoche « Compatibles Bose » ou baisse le débit.",
            "Every station found is hidden: clear \"Bose-compatible\" or lower the bitrate.",
            "Alle gefundenen Sender sind ausgeblendet: „Bose-kompatibel“ abwählen oder die Bitrate senken.",
            "Alle gevonden zenders zijn verborgen: zet „Bose-compatibel” uit of verlaag de bitrate.",
            "Todas las emisoras encontradas están ocultas: quita «Compatibles con Bose» o baja la tasa de bits.",
            "Tutte le stazioni trovate sono nascoste: togli «Compatibili Bose» o abbassa il bitrate.");
        Add("L_RelaxedHint",
            "L'annuaire signale ces stations hors service. Elles jouent parfois très bien : son contrôle se fait depuis l'étranger.",
            "The directory reports these stations as down. They often play fine: its check runs from abroad.",
            "Das Verzeichnis meldet diese Sender als nicht erreichbar. Oft spielen sie trotzdem: geprüft wird vom Ausland aus.",
            "De gids meldt deze zenders als onbereikbaar. Vaak spelen ze toch: de controle gebeurt vanuit het buitenland.",
            "El directorio marca estas emisoras como caídas. A menudo suenan bien: su control se hace desde el extranjero.",
            "L'elenco segnala queste stazioni come non attive. Spesso funzionano: il controllo avviene dall'estero.");
        Add("L_StationUnchecked",
            "Hors service au dernier contrôle de l'annuaire",
            "Down at the directory's last check",
            "Bei der letzten Prüfung nicht erreichbar",
            "Onbereikbaar bij de laatste controle",
            "Caída en el último control",
            "Non attiva all'ultimo controllo");
        Add("L_LoadMore", "Voir plus", "Show more", "Mehr anzeigen", "Meer tonen", "Ver más", "Mostra altre");
        Add("L_AddMissingStation",
            "Une station manque ? Ajoute-la sur radio-browser.info : elle sera trouvable ici, et pour tout le monde.",
            "A station is missing? Add it on radio-browser.info: it will turn up here, and for everyone.",
            "Fehlt ein Sender? Trag ihn auf radio-browser.info ein: Dann findest du ihn hier, und alle anderen auch.",
            "Mist er een zender? Voeg hem toe op radio-browser.info: dan vind je hem hier, en iedereen met jou.",
            "¿Falta una emisora? Añádela en radio-browser.info: aparecerá aquí, y para todos.",
            "Manca una stazione? Aggiungila su radio-browser.info: comparirà qui, e per tutti.");
        Add("L_PastedAddress", "Adresse collée", "Pasted address", "Eingefügte Adresse", "Geplakt adres", "Dirección pegada", "Indirizzo incollato");

        // ---------------------------------------------------------- Écoute sur l'appareil / diffusion sur l'enceinte
        // Deux verbes distincts dans chaque langue : écouter (appareil), diffuser (enceinte).
        Add("L_ThisPhone", "ce téléphone", "this phone", "diesem Telefon", "deze telefoon", "este teléfono", "questo telefono");
        Add("L_ThisTablet", "cette tablette", "this tablet", "diesem Tablet", "deze tablet", "esta tableta", "questo tablet");
        Add("L_ThisDevice", "cet appareil", "this device", "diesem Gerät", "dit apparaat", "este dispositivo", "questo dispositivo");
        Add("L_ListenOnDevice", "📱 ▶  Écouter sur {0}", "📱 ▶  Listen on {0}", "📱 ▶  Auf {0} anhören", "📱 ▶  Beluisteren op {0}", "📱 ▶  Escuchar en {0}", "📱 ▶  Ascolta su {0}");
        Add("L_StopOnDevice", "📱 ■  Arrêter sur {0}", "📱 ■  Stop on {0}", "📱 ■  Auf {0} stoppen", "📱 ■  Stoppen op {0}", "📱 ■  Detener en {0}", "📱 ■  Ferma su {0}");
        Add("L_BroadcastOn", "🔊  Diffuser sur {0}", "🔊  Play on {0}", "🔊  Auf {0} abspielen", "🔊  Afspelen op {0}", "🔊  Reproducir en {0}", "🔊  Riproduci su {0}");
        Add("L_BroadcastNoSpeaker",
            "🔊  Diffuser sur l'enceinte (aucune choisie)",
            "🔊  Play on the speaker (none selected)",
            "🔊  Auf dem Lautsprecher abspielen (keiner gewählt)",
            "🔊  Afspelen op de speaker (geen gekozen)",
            "🔊  Reproducir en el altavoz (ninguno elegido)",
            "🔊  Riproduci sul diffusore (nessuno scelto)");
        Add("L_MoreActions", "⋯  Touches, Local…", "⋯  Keys, Local…", "⋯  Tasten, Local…", "⋯  Toetsen, Local…", "⋯  Teclas, Local…", "⋯  Tasti, Local…");
        Add("L_PreviewConnecting", "📱  Connexion à {0}…", "📱  Connecting to {0}…", "📱  Verbindung zu {0}…", "📱  Verbinden met {0}…", "📱  Conectando con {0}…", "📱  Connessione a {0}…");
        Add("L_PreviewPlaying", "📱  Sur {0} : {1}", "📱  On {0}: {1}", "📱  Auf {0}: {1}", "📱  Op {0}: {1}", "📱  En {0}: {1}", "📱  Su {0}: {1}");
        Add("L_PreviewStop", "■  Arrêter", "■  Stop", "■  Stoppen", "■  Stoppen", "■  Detener", "■  Ferma");
        Add("S_PreviewUnsupported",
            "L'écoute sur l'appareil n'est pas disponible ici.",
            "Listening on the device is not available here.",
            "Das Anhören auf dem Gerät ist hier nicht verfügbar.",
            "Beluisteren op het apparaat is hier niet beschikbaar.",
            "Escuchar en el dispositivo no está disponible aquí.",
            "L'ascolto sul dispositivo non è disponibile qui.");
        Add("S_PreviewFailed",
            "Cette station ne joue pas sur {0}. Elle peut quand même jouer sur l'enceinte : l'agent STR convertit ce que l'appareil ne lit pas.",
            "This station does not play on {0}. It may still play on the speaker: the STR agent converts what the device cannot read.",
            "Dieser Sender spielt nicht auf {0}. Auf dem Lautsprecher kann er trotzdem laufen: Der STR-Agent wandelt um, was das Gerät nicht liest.",
            "Deze zender speelt niet op {0}. Op de speaker kan hij toch spelen: de STR-agent zet om wat het apparaat niet leest.",
            "Esta emisora no suena en {0}. Puede sonar igualmente en el altavoz: el agente STR convierte lo que el dispositivo no lee.",
            "Questa stazione non si sente su {0}. Può comunque suonare sul diffusore: l'agente STR converte ciò che il dispositivo non legge.");
        Add("S_PreviewMobileData",
            "Hors Wi-Fi, l'écoute utilise les données mobiles (environ 1 Mo par minute à 128 kbit/s).",
            "Away from Wi-Fi, listening uses mobile data (about 1 MB per minute at 128 kbit/s).",
            "Ohne WLAN nutzt das Anhören mobile Daten (etwa 1 MB pro Minute bei 128 kbit/s).",
            "Zonder wifi gebruikt beluisteren mobiele data (ongeveer 1 MB per minuut bij 128 kbit/s).",
            "Sin Wi-Fi, escuchar usa datos móviles (alrededor de 1 MB por minuto a 128 kbit/s).",
            "Senza Wi-Fi, l'ascolto usa i dati mobili (circa 1 MB al minuto a 128 kbit/s).");
        Add("S_PreviewEnded",
            "L'écoute sur {0} s'est arrêtée : le flux s'est coupé.",
            "Listening on {0} stopped: the stream dropped.",
            "Das Anhören auf {0} wurde beendet: Der Stream ist abgebrochen.",
            "Het beluisteren op {0} is gestopt: de stream viel weg.",
            "La escucha en {0} se detuvo: el flujo se cortó.",
            "L'ascolto su {0} si è fermato: il flusso si è interrotto.");

        // ---------------------------------------------------------- Pays
        // Traduits à la main : sur Android, RegionInfo.DisplayName renvoie le nom
        // NATIF du pays (« België », « Lëtzebuerg »), pas sa traduction.
        Add("L_CtryBE", "Belgique", "Belgium", "Belgien", "België", "Bélgica", "Belgio");
        Add("L_CtryFR", "France", "France", "Frankreich", "Frankrijk", "Francia", "Francia");
        Add("L_CtryNL", "Pays-Bas", "Netherlands", "Niederlande", "Nederland", "Países Bajos", "Paesi Bassi");
        Add("L_CtryDE", "Allemagne", "Germany", "Deutschland", "Duitsland", "Alemania", "Germania");
        Add("L_CtryLU", "Luxembourg", "Luxembourg", "Luxemburg", "Luxemburg", "Luxemburgo", "Lussemburgo");
        Add("L_CtryCH", "Suisse", "Switzerland", "Schweiz", "Zwitserland", "Suiza", "Svizzera");
        Add("L_CtryGB", "Royaume-Uni", "United Kingdom", "Vereinigtes Königreich", "Verenigd Koninkrijk", "Reino Unido", "Regno Unito");
        Add("L_CtryES", "Espagne", "Spain", "Spanien", "Spanje", "España", "Spagna");
        Add("L_CtryIT", "Italie", "Italy", "Italien", "Italië", "Italia", "Italia");
        Add("L_CtryPT", "Portugal", "Portugal", "Portugal", "Portugal", "Portugal", "Portogallo");
        Add("L_CtryUS", "États-Unis", "United States", "Vereinigte Staaten", "Verenigde Staten", "Estados Unidos", "Stati Uniti");
        Add("L_CtryCA", "Canada", "Canada", "Kanada", "Canada", "Canadá", "Canada");
        Add("L_CtryAT", "Autriche", "Austria", "Österreich", "Oostenrijk", "Austria", "Austria");
        Add("L_CtryIE", "Irlande", "Ireland", "Irland", "Ierland", "Irlanda", "Irlanda");
        Add("L_CtryDK", "Danemark", "Denmark", "Dänemark", "Denemarken", "Dinamarca", "Danimarca");
        Add("L_CtrySE", "Suède", "Sweden", "Schweden", "Zweden", "Suecia", "Svezia");
        Add("L_CtryNO", "Norvège", "Norway", "Norwegen", "Noorwegen", "Noruega", "Norvegia");
        Add("L_CtryFI", "Finlande", "Finland", "Finnland", "Finland", "Finlandia", "Finlandia");
        Add("L_CtryPL", "Pologne", "Poland", "Polen", "Polen", "Polonia", "Polonia");
        Add("L_CtryCZ", "Tchéquie", "Czechia", "Tschechien", "Tsjechië", "Chequia", "Cechia");
        Add("L_CtryGR", "Grèce", "Greece", "Griechenland", "Griekenland", "Grecia", "Grecia");
        Add("L_CtryTR", "Turquie", "Turkey", "Türkei", "Turkije", "Turquía", "Turchia");
        Add("L_CtryMA", "Maroc", "Morocco", "Marokko", "Marokko", "Marruecos", "Marocco");
        Add("L_CtryBR", "Brésil", "Brazil", "Brasilien", "Brazilië", "Brasil", "Brasile");
        Add("L_CtryMX", "Mexique", "Mexico", "Mexiko", "Mexico", "México", "Messico");
        Add("L_CtryAR", "Argentine", "Argentina", "Argentinien", "Argentinië", "Argentina", "Argentina");
        Add("L_CtryAU", "Australie", "Australia", "Australien", "Australië", "Australia", "Australia");
        Add("L_CtryJP", "Japon", "Japan", "Japan", "Japan", "Japón", "Giappone");
        Add("L_CtryMQ", "Martinique", "Martinique", "Martinique", "Martinique", "Martinica", "Martinica");
        Add("L_CtryRE", "La Réunion", "Réunion", "Réunion", "Réunion", "Reunión", "Riunione");
        Add("L_CtryGP", "Guadeloupe", "Guadeloupe", "Guadeloupe", "Guadeloupe", "Guadalupe", "Guadalupa");
        Add("L_CtryGF", "Guyane", "French Guiana", "Französisch-Guayana", "Frans-Guyana", "Guayana Francesa", "Guyana francese");
        Add("L_CtryYT", "Mayotte", "Mayotte", "Mayotte", "Mayotte", "Mayotte", "Mayotte");
        Add("L_CtryNC", "Nouvelle-Calédonie", "New Caledonia", "Neukaledonien", "Nieuw-Caledonië", "Nueva Caledonia", "Nuova Caledonia");
        Add("L_CtryPF", "Polynésie française", "French Polynesia", "Französisch-Polynesien", "Frans-Polynesië", "Polinesia Francesa", "Polinesia francese");
        Add("L_CtryPM", "Saint-Pierre-et-Miquelon", "Saint Pierre and Miquelon", "Saint-Pierre und Miquelon", "Saint-Pierre en Miquelon", "San Pedro y Miquelón", "Saint-Pierre e Miquelon");
        Add("L_CtryDZ", "Algérie", "Algeria", "Algerien", "Algerije", "Argelia", "Algeria");
        Add("L_CtryCI", "Côte d'Ivoire", "Côte d'Ivoire", "Elfenbeinküste", "Ivoorkust", "Costa de Marfil", "Costa d'Avorio");
        Add("L_CtryTN", "Tunisie", "Tunisia", "Tunesien", "Tunesië", "Túnez", "Tunisia");
        Add("L_CtryCM", "Cameroun", "Cameroon", "Kamerun", "Kameroen", "Camerún", "Camerun");
        Add("L_CtrySN", "Sénégal", "Senegal", "Senegal", "Senegal", "Senegal", "Senegal");

        // ---------------------------------------------------------- Affectation d'une station
        Add("L_ReplacePreset", "Remplacer {0}", "Replace {0}", "{0} ersetzen", "{0} vervangen", "Sustituir {0}", "Sostituisci {0}");
        Add("L_AssignEmpty", "Affecter à cette touche", "Assign to this key", "Auf diese Taste legen", "Aan deze toets toewijzen", "Asignar a esta tecla", "Assegna a questo tasto");
        Add("L_Controls", "COMMANDES", "CONTROLS", "STEUERUNG", "BEDIENING", "CONTROLES", "COMANDI");
        Add("L_RightNow", "EN CE MOMENT", "RIGHT NOW", "GERADE EBEN", "OP DIT MOMENT", "AHORA MISMO", "ORA");
        Add("S_Refreshed", "Actualisé", "Refreshed", "Aktualisiert", "Bijgewerkt", "Actualizado", "Aggiornato");
        Add("L_ResultsHeader", "RÉSULTATS ({0})", "RESULTS ({0})", "ERGEBNISSE ({0})", "RESULTATEN ({0})", "RESULTADOS ({0})", "RISULTATI ({0})");
        Add("S_GroupedWith", "Groupé avec {0}", "Grouped with {0}", "Mit {0} gruppiert", "Gegroepeerd met {0}", "Agrupado con {0}", "Raggruppato con {0}");
        Add("S_Grouping", "Groupement avec {0} en cours…", "Grouping with {0}…", "Gruppierung mit {0} läuft…", "Groeperen met {0}…", "Agrupando con {0}…", "Raggruppamento con {0}…");
        Add("S_Ungrouping", "Retrait de {0} du groupe en cours…", "Removing {0} from the group…", "{0} wird aus der Gruppe entfernt…", "{0} wordt uit de groep gehaald…", "Retirando {0} del grupo…", "Rimozione di {0} dal gruppo…");
        Add("S_Ungrouped", "{0} retirée du groupe", "{0} removed from the group", "{0} aus der Gruppe entfernt", "{0} uit de groep gehaald", "{0} retirado del grupo", "{0} rimosso dal gruppo");
        Add("L_VolumeOfGroup",
            "Volume de l'ensemble du groupe",
            "Volume of the whole group",
            "Lautstärke der ganzen Gruppe",
            "Volume van de hele groep",
            "Volumen de todo el grupo",
            "Volume di tutto il gruppo");
        Add("L_LevelGroup",
            "Égaliser",
            "Level all",
            "Angleichen",
            "Gelijkzetten",
            "Igualar",
            "Livella tutti");
        Add("L_LevelGroupHint",
            "La touche « = » met toutes les enceintes à cette valeur.",
            "The “=” key sets every speaker to this value.",
            "Die Taste „=“ setzt alle Lautsprecher auf diesen Wert.",
            "De toets “=” zet alle speakers op deze waarde.",
            "La tecla «=» pone todos los altavoces en este valor.",
            "Il tasto “=” porta ogni diffusore a questo valore.");
        Add("S_SlaveCannotForm",
            "Cette enceinte suit {0}. Sélectionne {0} pour modifier le groupe.",
            "This speaker follows {0}. Select {0} to change the group.",
            "Dieser Lautsprecher folgt {0}. Wähle {0}, um die Gruppe zu ändern.",
            "Deze speaker volgt {0}. Kies {0} om de groep te wijzigen.",
            "Este altavoz sigue a {0}. Selecciona {0} para cambiar el grupo.",
            "Questo diffusore segue {0}. Seleziona {0} per modificare il gruppo.");
        Add("L_SlaveOfGroup",
            "Cette enceinte suit le groupe. Touche l'enceinte cochée pour l'en retirer.",
            "This speaker follows the group. Tap the ticked speaker to leave it.",
            "Dieser Lautsprecher folgt der Gruppe. Tippe auf den angehakten Lautsprecher, um ihn zu verlassen.",
            "Deze speaker volgt de groep. Tik op de aangevinkte speaker om hem te verlaten.",
            "Este altavoz sigue al grupo. Toca el altavoz marcado para salir.",
            "Questo diffusore segue il gruppo. Tocca il diffusore spuntato per uscirne.");
        Add("S_GroupLevelled",
            "Toutes les enceintes à {0} %",
            "All speakers at {0}%",
            "Alle Lautsprecher auf {0} %",
            "Alle speakers op {0} %",
            "Todos los altavoces al {0} %",
            "Tutti i diffusori al {0}%");
        Add("L_PerSpeaker", "PAR ENCEINTE", "PER SPEAKER", "PRO LAUTSPRECHER", "PER SPEAKER", "POR ALTAVOZ", "PER DIFFUSORE");
        Add("L_GroupedBadge", "groupé", "grouped", "gruppiert", "gegroepeerd", "agrupado", "raggruppato");
        Add("L_TapToUngroup",
            "Touche une enceinte cochée pour la retirer du groupe.",
            "Tap a ticked speaker to remove it from the group.",
            "Tippe auf einen angehakten Lautsprecher, um ihn aus der Gruppe zu nehmen.",
            "Tik op een aangevinkte speaker om hem uit de groep te halen.",
            "Toca un altavoz marcado para sacarlo del grupo.",
            "Tocca un diffusore spuntato per toglierlo dal gruppo.");

        // ---------------------------------------------------------- À propos
        Add("L_About", "À PROPOS", "ABOUT", "ÜBER", "OVER", "ACERCA DE", "INFORMAZIONI");
        Add("L_AppTagline",
            "Télécommande locale pour enceintes Bose SoundTouch.",
            "Local remote for Bose SoundTouch speakers.",
            "Lokale Fernbedienung für Bose-SoundTouch-Lautsprecher.",
            "Lokale afstandsbediening voor Bose SoundTouch-speakers.",
            "Mando local para altavoces Bose SoundTouch.",
            "Telecomando locale per diffusori Bose SoundTouch.");
        Add("L_ThanksTitle", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn");
        Add("L_ThanksBody",
            "Quand Bose a coupé son cloud le 6 mai 2026, les enceintes SoundTouch perdaient l'essentiel de leur usage. Jens Roggenfelder (JRpersonal) a écrit STR, l'agent qui tourne sur l'enceinte elle-même et lui rend tout ce que le cloud assurait. Cette application ne fait que lui parler : sans son travail, elle n'aurait rien à piloter.",
            "When Bose shut its cloud down on 6 May 2026, SoundTouch speakers lost most of what made them useful. Jens Roggenfelder (JRpersonal) wrote STR, the agent that runs on the speaker itself and gives back everything the cloud used to provide. This app only talks to it: without his work, it would have nothing to control.",
            "Als Bose am 6. Mai 2026 seine Cloud abschaltete, verloren die SoundTouch-Lautsprecher fast ihren gesamten Nutzen. Jens Roggenfelder (JRpersonal) schrieb STR, den Agenten, der auf dem Lautsprecher selbst läuft und zurückgibt, was die Cloud leistete. Diese App spricht nur mit ihm: ohne seine Arbeit hätte sie nichts zu steuern.",
            "Toen Bose op 6 mei 2026 zijn cloud uitzette, verloren de SoundTouch-speakers vrijwel hun hele nut. Jens Roggenfelder (JRpersonal) schreef STR, de agent die op de speaker zelf draait en teruggeeft wat de cloud deed. Deze app praat er alleen mee: zonder zijn werk zou ze niets te besturen hebben.",
            "Cuando Bose apagó su nube el 6 de mayo de 2026, los altavoces SoundTouch perdieron casi toda su utilidad. Jens Roggenfelder (JRpersonal) escribió STR, el agente que corre en el propio altavoz y devuelve lo que la nube aportaba. Esta aplicación solo habla con él: sin su trabajo, no tendría nada que controlar.",
            "Quando Bose ha chiuso il suo cloud il 6 maggio 2026, i diffusori SoundTouch hanno perso gran parte di ciò che li rendeva utili. Jens Roggenfelder (JRpersonal) ha scritto STR, l'agente che gira sul diffusore stesso e restituisce tutto ciò che il cloud forniva. Questa app parla solo con lui: senza il suo lavoro non avrebbe nulla da comandare.");
        Add("L_ThanksLicense",
            "STR est publié sous licence MIT.",
            "STR is released under the MIT licence.",
            "STR steht unter der MIT-Lizenz.",
            "STR valt onder de MIT-licentie.",
            "STR se publica bajo licencia MIT.",
            "STR è distribuito con licenza MIT.");
        Add("L_OpenWebsite", "Site du projet STR", "STR project website", "Website des STR-Projekts", "Website van het STR-project", "Sitio del proyecto STR", "Sito del progetto STR");
        Add("L_OpenSource", "Code source", "Source code", "Quellcode", "Broncode", "Código fuente", "Codice sorgente");
        Add("L_RadioCredit",
            "Annuaire des stations : radio-browser.info, base ouverte tenue par des bénévoles.",
            "Station directory: radio-browser.info, an open database kept by volunteers.",
            "Senderverzeichnis: radio-browser.info, eine offene, von Freiwilligen gepflegte Datenbank.",
            "Zendergids: radio-browser.info, een open database bijgehouden door vrijwilligers.",
            "Directorio de emisoras: radio-browser.info, una base abierta mantenida por voluntarios.",
            "Elenco delle stazioni: radio-browser.info, una banca dati aperta curata da volontari.");
        Add("L_IndependenceNotice",
            "Application indépendante du projet STR : ni développée, ni maintenue, ni prise en charge par lui. Les problèmes rencontrés avec elle sont à signaler à son auteur.",
            "This app is independent of the STR project: not built, maintained or supported by it. Report problems with the app to its author.",
            "Diese App ist unabhängig vom STR-Projekt: sie wird von ihm weder entwickelt noch gepflegt noch unterstützt. Probleme mit der App bitte an ihren Autor melden.",
            "Deze app staat los van het STR-project: niet erdoor gebouwd, onderhouden of ondersteund. Meld problemen met de app bij de auteur ervan.",
            "Aplicación independiente del proyecto STR: ni desarrollada, ni mantenida, ni respaldada por él. Informa de los problemas con la aplicación a su autor.",
            "Questa app è indipendente dal progetto STR: non è realizzata, mantenuta né supportata da esso. Segnala i problemi dell'app al suo autore.");
        Add("L_TrademarkNotice",
            "Projet personnel, sans lien avec Bose Corporation. « Bose » et « SoundTouch » sont des marques de Bose Corporation, citées pour indiquer la compatibilité.",
            "Personal project, not affiliated with Bose Corporation. “Bose” and “SoundTouch” are trademarks of Bose Corporation, named here to indicate compatibility.",
            "Privates Projekt, nicht mit der Bose Corporation verbunden. „Bose“ und „SoundTouch“ sind Marken der Bose Corporation und werden hier nur genannt, um die Kompatibilität anzugeben.",
            "Persoonlijk project, niet verbonden aan Bose Corporation. “Bose” en “SoundTouch” zijn merken van Bose Corporation, hier genoemd om de compatibiliteit aan te geven.",
            "Proyecto personal, sin vínculo con Bose Corporation. «Bose» y «SoundTouch» son marcas de Bose Corporation, citadas aquí para indicar la compatibilidad.",
            "Progetto personale, non affiliato a Bose Corporation. “Bose” e “SoundTouch” sono marchi di Bose Corporation, citati qui per indicare la compatibilità.");
        Add("L_ClaudeCredit",
            "Application écrite avec Claude (Anthropic).",
            "App written with Claude (Anthropic).",
            "App geschrieben mit Claude (Anthropic).",
            "App geschreven met Claude (Anthropic).",
            "Aplicación escrita con Claude (Anthropic).",
            "App scritta con Claude (Anthropic).");

        // ---------------------------------------------------------- Diagnostic
        Add("D_Name", "Nom", "Name", "Name", "Naam", "Nombre", "Nome");
        Add("D_Model", "Modèle", "Model", "Modell", "Model", "Modelo", "Modello");
        Add("D_Firmware", "Firmware", "Firmware", "Firmware", "Firmware", "Firmware", "Firmware");
        Add("D_Address", "Adresse", "Address", "Adresse", "Adres", "Dirección", "Indirizzo");
        Add("D_Agent", "Agent STR", "STR agent", "STR-Agent", "STR-agent", "Agente STR", "Agente STR");
        Add("D_AgentYes", "oui, port {0}", "yes, port {0}", "ja, Port {0}", "ja, poort {0}", "sí, puerto {0}", "sì, porta {0}");
        Add("D_AgentNo", "non détecté", "not detected", "nicht erkannt", "niet gevonden", "no detectado", "non rilevato");
        Add("D_AgentSilent",
            "ne répond pas (ports 17008 et 8888 essayés)",
            "no answer (ports 17008 and 8888 tried)",
            "keine Antwort (Ports 17008 und 8888 versucht)",
            "geen antwoord (poorten 17008 en 8888 geprobeerd)",
            "sin respuesta (puertos 17008 y 8888 probados)",
            "nessuna risposta (porte 17008 e 8888 provate)");
        Add("D_AgentStored",
            "dernière version connue : {0}",
            "last known version: {0}",
            "zuletzt bekannte Version: {0}",
            "laatst bekende versie: {0}",
            "última versión conocida: {0}",
            "ultima versione conosciuta: {0}");
        Add("D_Notifications", "Notifications", "Notifications", "Benachrichtigungen", "Meldingen", "Notificaciones", "Notifiche");
        Add("D_WsUp", "WebSocket 8080 connecté", "WebSocket 8080 connected", "WebSocket 8080 verbunden", "WebSocket 8080 verbonden", "WebSocket 8080 conectado", "WebSocket 8080 connesso");
        Add("D_WsDown", "hors ligne", "offline", "offline", "offline", "sin conexión", "non connesso");
        Add("D_BassRange", "{0} à {1} (défaut {2})", "{0} to {1} (default {2})", "{0} bis {1} (Standard {2})", "{0} tot {1} (standaard {2})", "{0} a {1} (por defecto {2})", "da {0} a {1} (predefinito {2})");
        Add("D_BassNone", "non réglables sur ce modèle", "not adjustable on this model", "bei diesem Modell nicht einstellbar", "niet instelbaar op dit model", "no ajustables en este modelo", "non regolabile su questo modello");
        Add("D_Endpoints", "Points exposés", "Exposed endpoints", "Verfügbare Endpunkte", "Beschikbare eindpunten", "Endpoints expuestos", "Endpoint esposti");
        Add("S_PresetsUnread",
            "Touches non relues — l'enceinte n'a pas répondu.",
            "Presets not re-read — the speaker did not answer.",
            "Tasten nicht neu gelesen — der Lautsprecher hat nicht geantwortet.",
            "Toetsen niet opnieuw gelezen — de speaker antwoordde niet.",
            "Teclas no releídas — el altavoz no respondió.",
            "Preselezioni non rilette — il diffusore non ha risposto.");
        Add("D_Zone", "Zone vue par chaque enceinte", "Zone as each speaker sees it", "Zone aus Sicht jedes Lautsprechers", "Zone zoals elke speaker die ziet", "Zona según cada altavoz", "Zona vista da ogni diffusore");
        Add("D_EndpointsNone",
            "/supportedURLs non disponible.",
            "/supportedURLs not available.",
            "/supportedURLs nicht verfügbar.",
            "/supportedURLs niet beschikbaar.",
            "/supportedURLs no disponible.",
            "/supportedURLs non disponibile.");

        // ---------------------------------------------------------- Seconde rangée de touches
        Add("L_RowOnce", "Une fois", "Once", "Einmal", "Eén keer", "Una vez", "Una volta");
        Add("L_RowTwice", "Deux fois", "Twice", "Zweimal", "Twee keer", "Dos veces", "Due volte");

        // ---------------------------------------------------------- Renommer, effacer une touche (1.8.2)
        Add("L_KeyNumber", "Touche {0}", "Key {0}", "Taste {0}", "Toets {0}", "Tecla {0}", "Tasto {0}");
        Add("L_KeyRenameTitle",
            "Renommer la touche {0}",
            "Rename key {0}",
            "Taste {0} umbenennen",
            "Toets {0} hernoemen",
            "Renombrar la tecla {0}",
            "Rinomina il tasto {0}");
        Add("L_KeyRenameMessage",
            "Seul le nom change : la station et son image restent.",
            "Only the name changes: the station and its picture stay.",
            "Nur der Name ändert sich: Sender und Bild bleiben.",
            "Alleen de naam verandert: de zender en zijn afbeelding blijven.",
            "Solo cambia el nombre: la emisora y su imagen se mantienen.",
            "Cambia solo il nome: la stazione e la sua immagine restano.");
        Add("L_KeyRenameGo", "Renommer", "Rename", "Umbenennen", "Hernoemen", "Renombrar", "Rinomina");
        Add("L_KeyClearConfirm",
            "Effacer la touche {0} ({1}) ?",
            "Clear key {0} ({1})?",
            "Taste {0} ({1}) löschen?",
            "Toets {0} ({1}) wissen?",
            "¿Borrar la tecla {0} ({1})?",
            "Cancellare il tasto {0} ({1})?");
        Add("L_KeyClearGo", "Effacer", "Clear", "Löschen", "Wissen", "Borrar", "Cancella");
        Add("S_KeyRenamed", "Touche {0} renommée.", "Key {0} renamed.", "Taste {0} umbenannt.", "Toets {0} hernoemd.", "Tecla {0} renombrada.", "Tasto {0} rinominato.");
        Add("S_KeyCleared", "Touche {0} effacée.", "Key {0} cleared.", "Taste {0} gelöscht.", "Toets {0} gewist.", "Tecla {0} borrada.", "Tasto {0} cancellato.");
        Add("S_KeyGone",
            "La touche {0} est déjà vide : elle a été modifiée ailleurs.",
            "Key {0} is already empty: it was changed elsewhere.",
            "Taste {0} ist bereits leer: Sie wurde anderswo geändert.",
            "Toets {0} is al leeg: hij werd elders gewijzigd.",
            "La tecla {0} ya está vacía: se cambió en otro lugar.",
            "Il tasto {0} è già vuoto: è stato modificato altrove.");
        Add("S_AlreadyOnKey",
            "Déjà enregistré sur la touche {0}.",
            "Already stored on key {0}.",
            "Bereits auf Taste {0} gespeichert.",
            "Staat al op toets {0}.",
            "Ya está guardado en la tecla {0}.",
            "Già memorizzata sul tasto {0}.");
        Add("S_AlreadyOnKeyNamed",
            "{0} est déjà sur la touche {1}.",
            "{0} is already on key {1}.",
            "{0} liegt bereits auf Taste {1}.",
            "{0} staat al op toets {1}.",
            "{0} ya está en la tecla {1}.",
            "{0} è già sul tasto {1}.");

        // ---------------------------------------------------------- Paires stéréo et groupe enregistré
        Add("S_StereoCannotExtend",
            "Cette enceinte fait partie d'une paire stéréo : une paire ne peut pas diriger un groupe. Lancez le groupe depuis une autre enceinte, puis ajoutez-y la paire (agent STR 1.6.3 ou plus récent).",
            "This speaker is half of a stereo pair: a pair cannot lead a group. Start the group on another speaker, then add the pair to it (STR agent 1.6.3 or later).",
            "Dieser Lautsprecher gehört zu einem Stereopaar: Ein Paar kann keine Gruppe leiten. Starten Sie die Gruppe auf einem anderen Lautsprecher und fügen Sie dann das Paar hinzu (STR-Agent 1.6.3 oder neuer).",
            "Deze speaker maakt deel uit van een stereopaar: een paar kan geen groep leiden. Start de groep op een andere speaker en voeg het paar daarna toe (STR-agent 1.6.3 of nieuwer).",
            "Este altavoz forma parte de un par estéreo: un par no puede dirigir un grupo. Inicie el grupo en otro altavoz y añada después el par (agente STR 1.6.3 o posterior).",
            "Questo diffusore è metà di una coppia stereo: una coppia non può guidare un gruppo. Avvia il gruppo su un altro diffusore, poi aggiungi la coppia (agente STR 1.6.3 o successivo).");
        Add("L_SavedGroupAsk",
            "Un groupe permanent est enregistré sur cette enceinte. Former un autre groupe peut le remplacer.",
            "A permanent group is saved on this speaker. Forming a different group may replace it.",
            "Auf diesem Lautsprecher ist eine permanente Gruppe gespeichert. Eine andere Gruppe zu bilden kann sie ersetzen.",
            "Op deze speaker is een permanente groep opgeslagen. Een andere groep vormen kan die vervangen.",
            "Hay un grupo permanente guardado en este altavoz. Formar otro grupo puede sustituirlo.",
            "Su questo diffusore è salvato un gruppo permanente. Formare un gruppo diverso potrebbe sostituirlo.");
        Add("L_SavedGroupAskNamed",
            "Un groupe permanent est enregistré sur cette enceinte ({0}). Former un autre groupe peut le remplacer.",
            "A permanent group is saved on this speaker ({0}). Forming a different group may replace it.",
            "Auf diesem Lautsprecher ist eine permanente Gruppe gespeichert ({0}). Eine andere Gruppe zu bilden kann sie ersetzen.",
            "Op deze speaker is een permanente groep opgeslagen ({0}). Een andere groep vormen kan die vervangen.",
            "Hay un grupo permanente guardado en este altavoz ({0}). Formar otro grupo puede sustituirlo.",
            "Su questo diffusore è salvato un gruppo permanente ({0}). Formare un gruppo diverso potrebbe sostituirlo.");
        Add("L_FormAnyway",
            "Former ce groupe quand même",
            "Form this group anyway",
            "Diese Gruppe trotzdem bilden",
            "Deze groep toch vormen",
            "Formar este grupo de todos modos",
            "Forma comunque questo gruppo");
        Add("L_SetAsideAsk",
            "Un groupe ordinaire est enregistré sur cette enceinte. S'il a été formé par-dessus un groupe permanent, l'agent garde ce dernier de côté, et le nouveau groupe permanent le remplacera.",
            "An ordinary group is saved on this speaker. If it was formed on top of a permanent group, the agent keeps that one aside, and the new permanent group will replace it.",
            "Auf diesem Lautsprecher ist eine normale Gruppe gespeichert. Wurde sie über einer permanenten Gruppe gebildet, hält der Agent diese zurück, und die neue permanente Gruppe ersetzt sie.",
            "Op deze speaker is een gewone groep opgeslagen. Werd die gevormd boven op een permanente groep, dan houdt de agent die apart, en de nieuwe permanente groep vervangt haar.",
            "Hay un grupo normal guardado en este altavoz. Si se formó encima de un grupo permanente, el agente lo guarda aparte y el nuevo grupo permanente lo sustituirá.",
            "Su questo diffusore è salvato un gruppo normale. Se è stato formato sopra un gruppo permanente, l'agente tiene quest'ultimo da parte, e il nuovo gruppo permanente lo sostituirà.");
        Add("S_AgentTooOldGroups",
            "L'agent STR de cette enceinte ({0}) est trop ancien pour modifier les groupes : la version {1} ou plus récente est nécessaire. Mettez-le à jour avec l'application STR sur ordinateur.",
            "This speaker's STR agent ({0}) is too old to change groups: version {1} or later is needed. Update it with the STR app on a computer.",
            "Der STR-Agent dieses Lautsprechers ({0}) ist zu alt, um Gruppen zu ändern: Version {1} oder neuer ist nötig. Aktualisieren Sie ihn mit der STR-App am Computer.",
            "De STR-agent van deze speaker ({0}) is te oud om groepen te wijzigen: versie {1} of nieuwer is nodig. Werk hem bij met de STR-app op een computer.",
            "El agente STR de este altavoz ({0}) es demasiado antiguo para cambiar grupos: se necesita la versión {1} o posterior. Actualícelo con la aplicación STR en un ordenador.",
            "L'agente STR di questo diffusore ({0}) è troppo vecchio per modificare i gruppi: serve la versione {1} o successiva. Aggiornalo con l'app STR su un computer.");
        Add("L_AgentTooOldShort", "trop ancien", "too old", "zu alt", "te oud", "demasiado antiguo", "troppo vecchio");
        Add("D_AgentTooOld",
            "trop ancien, {0} minimum",
            "too old, {0} minimum",
            "zu alt, mindestens {0}",
            "te oud, minimaal {0}",
            "demasiado antiguo, mínimo {0}",
            "troppo vecchio, minimo {0}");
        Add("L_AgentOldTitle", "AGENT STR TROP ANCIEN", "STR AGENT TOO OLD", "STR-AGENT ZU ALT", "STR-AGENT TE OUD", "AGENTE STR DEMASIADO ANTIGUO", "AGENTE STR TROPPO VECCHIO");
        Add("L_AgentOldOne",
            "Une enceinte connue a un agent STR antérieur à la version {0}.",
            "One known speaker has an STR agent older than version {0}.",
            "Ein bekannter Lautsprecher hat einen STR-Agenten älter als Version {0}.",
            "Eén bekende speaker heeft een STR-agent ouder dan versie {0}.",
            "Un altavoz conocido tiene un agente STR anterior a la versión {0}.",
            "Un diffusore conosciuto ha un agente STR precedente alla versione {0}.");
        Add("L_AgentOldMany",
            "{0} enceintes connues ont un agent STR antérieur à la version {1}.",
            "{0} known speakers have an STR agent older than version {1}.",
            "{0} bekannte Lautsprecher haben einen STR-Agenten älter als Version {1}.",
            "{0} bekende speakers hebben een STR-agent ouder dan versie {1}.",
            "{0} altavoces conocidos tienen un agente STR anterior a la versión {1}.",
            "{0} diffusori conosciuti hanno un agente STR precedente alla versione {1}.");
        Add("L_AgentOldBody",
            "L'application ne modifie pas les groupes de ces enceintes : un agent plus ancien remplace le groupe permanent enregistré au lieu de le garder. Le reste fonctionne. La mise à jour se fait avec l'application STR sur PC ou Mac.",
            "The app does not change the groups of these speakers: an older agent replaces the saved permanent group instead of keeping it. Everything else works. Updating is done with the STR app on PC or Mac.",
            "Die App ändert die Gruppen dieser Lautsprecher nicht: Ein älterer Agent ersetzt die gespeicherte permanente Gruppe, statt sie zu behalten. Alles andere funktioniert. Das Update erfolgt mit der STR-App auf PC oder Mac.",
            "De app wijzigt de groepen van deze speakers niet: een oudere agent vervangt de opgeslagen permanente groep in plaats van haar te bewaren. De rest werkt. Bijwerken gebeurt met de STR-app op pc of Mac.",
            "La aplicación no cambia los grupos de estos altavoces: un agente más antiguo sustituye el grupo permanente guardado en lugar de conservarlo. Todo lo demás funciona. La actualización se hace con la aplicación STR en PC o Mac.",
            "L'app non modifica i gruppi di questi diffusori: un agente più vecchio sostituisce il gruppo permanente salvato invece di conservarlo. Tutto il resto funziona. L'aggiornamento si fa con l'app STR su PC o Mac.");
        Add("L_AgentOldOpen",
            "Voir comment mettre STR à jour",
            "See how to update STR",
            "So aktualisiert man STR",
            "Zie hoe je STR bijwerkt",
            "Ver cómo actualizar STR",
            "Vedi come aggiornare STR");
        Add("L_AlarmsTitle",
            "RÉVEILS",
            "ALARMS",
            "WECKER",
            "WEKKERS",
            "ALARMAS",
            "SVEGLIE");
        Add("L_AlarmsNoSpeaker",
            "Aucune enceinte sélectionnée.",
            "No speaker selected.",
            "Kein Lautsprecher ausgewählt.",
            "Geen speaker geselecteerd.",
            "Ningún altavoz seleccionado.",
            "Nessun diffusore selezionato.");
        Add("L_AlarmsNoAgent",
            "Les réveils demandent l'agent STR sur cette enceinte.",
            "Alarms need the STR agent on this speaker.",
            "Wecker brauchen den STR-Agenten auf diesem Lautsprecher.",
            "Wekkers hebben de STR-agent op deze speaker nodig.",
            "Las alarmas necesitan el agente STR en este altavoz.",
            "Le sveglie richiedono l'agente STR su questo diffusore.");
        Add("L_AlarmsUnavailable",
            "L'agent STR de cette enceinte ne répond pas pour les réveils. Réessayez avec « Rafraîchir ».",
            "This speaker's STR agent does not answer for alarms. Try again with \"Refresh\".",
            "Der STR-Agent dieses Lautsprechers antwortet nicht für die Wecker. Versuchen Sie es erneut mit „Aktualisieren“.",
            "De STR-agent van deze speaker antwoordt niet voor de wekkers. Probeer opnieuw met \"Vernieuwen\".",
            "El agente STR de este altavoz no responde para las alarmas. Vuelva a intentarlo con «Actualizar».",
            "L'agente STR di questo diffusore non risponde per le sveglie. Riprova con \"Aggiorna\".");
        Add("L_AlarmsNone",
            "Aucun réveil sur cette enceinte.",
            "No alarm on this speaker.",
            "Kein Wecker auf diesem Lautsprecher.",
            "Geen wekker op deze speaker.",
            "Ninguna alarma en este altavoz.",
            "Nessuna sveglia su questo diffusore.");
        Add("L_AlarmOn",
            "Activé",
            "On",
            "Ein",
            "Aan",
            "Activada",
            "Attiva");
        Add("L_AlarmOff",
            "Désactivé",
            "Off",
            "Aus",
            "Uit",
            "Desactivada",
            "Disattivata");
        Add("L_AlarmNext",
            "Prochain réveil : {0}",
            "Next alarm: {0}",
            "Nächster Wecker: {0}",
            "Volgende wekker: {0}",
            "Próxima alarma: {0}",
            "Prossima sveglia: {0}");
        Add("S_AlarmClockUntrusted",
            "L'horloge de l'enceinte n'est pas encore à l'heure : aucun réveil ne sonnera tant qu'elle ne l'est pas.",
            "The speaker's clock is not set yet: no alarm will ring until it is.",
            "Die Uhr des Lautsprechers ist noch nicht gestellt: Kein Wecker klingelt, bevor sie es ist.",
            "De klok van de speaker staat nog niet goed: geen wekker gaat af zolang dat zo is.",
            "El reloj del altavoz aún no está en hora: ninguna alarma sonará hasta que lo esté.",
            "L'orologio del diffusore non è ancora impostato: nessuna sveglia suonerà finché non lo sarà.");
        Add("S_AlarmZoneUnknown",
            "Le fuseau horaire enregistré est inconnu de l'enceinte : aucun réveil n'est programmé.",
            "The saved time zone is unknown to the speaker: no alarm is scheduled.",
            "Die gespeicherte Zeitzone ist dem Lautsprecher unbekannt: Kein Wecker ist geplant.",
            "De opgeslagen tijdzone is onbekend voor de speaker: er is geen wekker gepland.",
            "El altavoz no reconoce la zona horaria guardada: no hay ninguna alarma programada.",
            "Il fuso orario salvato è sconosciuto al diffusore: nessuna sveglia è programmata.");
        Add("L_AlarmZoneUtc",
            "Fuseau horaire de l'enceinte : UTC.",
            "Speaker time zone: UTC.",
            "Zeitzone des Lautsprechers: UTC.",
            "Tijdzone van de speaker: UTC.",
            "Zona horaria del altavoz: UTC.",
            "Fuso orario del diffusore: UTC.");
        Add("L_AlarmZoneUtcFix",
            "Fuseau horaire de l'enceinte : UTC. Celui du téléphone ({0}) sera enregistré avec le prochain réveil.",
            "Speaker time zone: UTC. The phone's ({0}) will be saved with the next alarm.",
            "Zeitzone des Lautsprechers: UTC. Die des Telefons ({0}) wird mit dem nächsten Wecker gespeichert.",
            "Tijdzone van de speaker: UTC. Die van de telefoon ({0}) wordt met de volgende wekker opgeslagen.",
            "Zona horaria del altavoz: UTC. La del teléfono ({0}) se guardará con la próxima alarma.",
            "Fuso orario del diffusore: UTC. Quello del telefono ({0}) verrà salvato con la prossima sveglia.");
        Add("L_AlarmZone",
            "Fuseau horaire de l'enceinte : {0}",
            "Speaker time zone: {0}",
            "Zeitzone des Lautsprechers: {0}",
            "Tijdzone van de speaker: {0}",
            "Zona horaria del altavoz: {0}",
            "Fuso orario del diffusore: {0}");
        Add("L_AlarmNew",
            "NOUVEAU RÉVEIL",
            "NEW ALARM",
            "NEUER WECKER",
            "NIEUWE WEKKER",
            "NUEVA ALARMA",
            "NUOVA SVEGLIA");
        Add("L_AlarmEdit",
            "RÉVEIL DE {0}",
            "ALARM AT {0}",
            "WECKER UM {0}",
            "WEKKER OM {0}",
            "ALARMA DE LAS {0}",
            "SVEGLIA ALLE {0}");
        Add("S_AlarmNoDay",
            "Choisissez au moins un jour.",
            "Pick at least one day.",
            "Wählen Sie mindestens einen Tag.",
            "Kies minstens één dag.",
            "Elija al menos un día.",
            "Scegli almeno un giorno.");
        Add("S_AlarmIncomplete",
            "Choisissez l'heure et la touche.",
            "Pick the time and the key.",
            "Wählen Sie Uhrzeit und Taste.",
            "Kies het uur en de toets.",
            "Elija la hora y la tecla.",
            "Scegli l'ora e il tasto.");
        Add("S_AlarmAdded",
            "Réveil de {0} ajouté.",
            "Alarm at {0} added.",
            "Wecker um {0} hinzugefügt.",
            "Wekker om {0} toegevoegd.",
            "Alarma de las {0} añadida.",
            "Sveglia alle {0} aggiunta.");
        Add("S_AlarmSaved",
            "Réveil de {0} enregistré.",
            "Alarm at {0} saved.",
            "Wecker um {0} gespeichert.",
            "Wekker om {0} opgeslagen.",
            "Alarma de las {0} guardada.",
            "Sveglia alle {0} salvata.");
        Add("S_AlarmFull",
            "Cette enceinte a déjà huit réveils, le maximum.",
            "This speaker already has eight alarms, the maximum.",
            "Dieser Lautsprecher hat bereits acht Wecker, das Maximum.",
            "Deze speaker heeft al acht wekkers, het maximum.",
            "Este altavoz ya tiene ocho alarmas, el máximo.",
            "Questo diffusore ha già otto sveglie, il massimo.");
        Add("S_AlarmGone",
            "Ce réveil a été supprimé ailleurs entre-temps. La liste est à jour.",
            "This alarm was deleted elsewhere in the meantime. The list is up to date.",
            "Dieser Wecker wurde inzwischen anderswo gelöscht. Die Liste ist aktuell.",
            "Deze wekker werd intussen elders verwijderd. De lijst is bijgewerkt.",
            "Esta alarma se eliminó en otro lugar mientras tanto. La lista está actualizada.",
            "Nel frattempo questa sveglia è stata eliminata altrove. L'elenco è aggiornato.");
        Add("L_AlarmDelete",
            "Supprimer ce réveil",
            "Delete this alarm",
            "Diesen Wecker löschen",
            "Deze wekker verwijderen",
            "Eliminar esta alarma",
            "Elimina questa sveglia");
        Add("L_AlarmDeleteAsk",
            "Supprimer le réveil de {0} ?",
            "Delete the alarm at {0}?",
            "Den Wecker um {0} löschen?",
            "De wekker om {0} verwijderen?",
            "¿Eliminar la alarma de las {0}?",
            "Eliminare la sveglia delle {0}?");
        Add("S_AlarmDeleted",
            "Réveil supprimé.",
            "Alarm deleted.",
            "Wecker gelöscht.",
            "Wekker verwijderd.",
            "Alarma eliminada.",
            "Sveglia eliminata.");
        Add("S_AlarmConflict",
            "Les réveils changent sans cesse ailleurs : rien n'a été enregistré. Réessayez.",
            "The alarms keep changing elsewhere: nothing was saved. Try again.",
            "Die Wecker ändern sich ständig anderswo: Nichts wurde gespeichert. Versuchen Sie es erneut.",
            "De wekkers veranderen voortdurend elders: er is niets opgeslagen. Probeer opnieuw.",
            "Las alarmas cambian sin cesar en otro lugar: no se guardó nada. Vuelva a intentarlo.",
            "Le sveglie continuano a cambiare altrove: non è stato salvato nulla. Riprova.");
        Add("S_AlarmRefused",
            "L'enceinte a refusé : {0}",
            "The speaker refused: {0}",
            "Der Lautsprecher hat abgelehnt: {0}",
            "De speaker weigerde: {0}",
            "El altavoz lo rechazó: {0}",
            "Il diffusore ha rifiutato: {0}");
        Add("S_StartVolumeOff",
            "Volume de départ désactivé.",
            "Start volume switched off.",
            "Startlautstärke ausgeschaltet.",
            "Startvolume uitgeschakeld.",
            "Volumen de inicio desactivado.",
            "Volume di avvio disattivato.");
        Add("S_StartVolumeSet",
            "Volume de départ réglé à {0}.",
            "Start volume set to {0}.",
            "Startlautstärke auf {0} gesetzt.",
            "Startvolume ingesteld op {0}.",
            "Volumen de inicio ajustado a {0}.",
            "Volume di avvio impostato a {0}.");
        Add("L_AlarmAutoOffNever",
            "Jamais",
            "Never",
            "Nie",
            "Nooit",
            "Nunca",
            "Mai");
        Add("L_AlarmAutoOffMinutes",
            "Après {0} min",
            "After {0} min",
            "Nach {0} Min.",
            "Na {0} min",
            "Tras {0} min",
            "Dopo {0} min");
        Add("L_AlarmSnoozeMinutes",
            "{0} min",
            "{0} min",
            "{0} Min.",
            "{0} min",
            "{0} min",
            "{0} min");
        Add("L_AlarmKey",
            "Touche {0}",
            "Key {0}",
            "Taste {0}",
            "Toets {0}",
            "Tecla {0}",
            "Tasto {0}");
        Add("L_AlarmKeyEmpty",
            "vide",
            "empty",
            "leer",
            "leeg",
            "vacía",
            "vuoto");
        Add("L_AlarmEveryDay",
            "Tous les jours",
            "Every day",
            "Täglich",
            "Elke dag",
            "Todos los días",
            "Tutti i giorni");
        Add("L_AlarmWeekdays",
            "Du lundi au vendredi",
            "Monday to Friday",
            "Montag bis Freitag",
            "Maandag tot vrijdag",
            "De lunes a viernes",
            "Dal lunedì al venerdì");
        Add("L_AlarmDaysWork", "Lun → Ven", "Mon → Fri", "Mo → Fr", "Ma → Vr", "Lun → Vie", "Lun → Ven");
        Add("L_AlarmDaysWeekend", "Week-end", "Weekend", "Wochenende", "Weekend", "Fin de semana", "Weekend");
        Add("L_AlarmDaysAll", "Tous les jours", "Every day", "Täglich", "Elke dag", "Todos los días", "Tutti i giorni");
        Add("L_AlarmWeekend",
            "Le week-end",
            "Weekends",
            "Am Wochenende",
            "In het weekend",
            "Fines de semana",
            "Fine settimana");
        Add("L_AlarmOffAt",
            "Arrêt à {0}, réglé depuis la page téléphone de STR : il est conservé.",
            "Switches off at {0}, set from the STR phone page: it is kept.",
            "Schaltet um {0} ab, auf der STR-Telefonseite eingestellt: bleibt erhalten.",
            "Schakelt uit om {0}, ingesteld via de STR-telefoonpagina: blijft behouden.",
            "Se apaga a las {0}, ajustado desde la página de teléfono de STR: se conserva.",
            "Si spegne alle {0}, impostato dalla pagina STR del telefono: viene mantenuto.");
        Add("L_AlarmAdd",
            "Ajouter un réveil",
            "Add an alarm",
            "Wecker hinzufügen",
            "Wekker toevoegen",
            "Añadir una alarma",
            "Aggiungi una sveglia");
        Add("L_AlarmHour",
            "Heure",
            "Hour",
            "Stunde",
            "Uur",
            "Hora",
            "Ora");
        Add("L_AlarmDays",
            "Jours",
            "Days",
            "Tage",
            "Dagen",
            "Días",
            "Giorni");
        Add("L_DayMonday",
            "Lun",
            "Mon",
            "Mo",
            "Ma",
            "Lun",
            "Lun");
        Add("L_DayTuesday",
            "Mar",
            "Tue",
            "Di",
            "Di",
            "Mar",
            "Mar");
        Add("L_DayWednesday",
            "Mer",
            "Wed",
            "Mi",
            "Wo",
            "Mié",
            "Mer");
        Add("L_DayThursday",
            "Jeu",
            "Thu",
            "Do",
            "Do",
            "Jue",
            "Gio");
        Add("L_DayFriday",
            "Ven",
            "Fri",
            "Fr",
            "Vr",
            "Vie",
            "Ven");
        Add("L_DaySaturday",
            "Sam",
            "Sat",
            "Sa",
            "Za",
            "Sáb",
            "Sab");
        Add("L_DaySunday",
            "Dim",
            "Sun",
            "So",
            "Zo",
            "Dom",
            "Dom");
        Add("L_AlarmKeyTitle",
            "Touche jouée",
            "Key to play",
            "Gespielte Taste",
            "Gespeelde toets",
            "Tecla que suena",
            "Tasto da riprodurre");
        Add("L_AlarmUseVolume",
            "Régler le volume du réveil",
            "Set the alarm volume",
            "Weckerlautstärke einstellen",
            "Volume van de wekker instellen",
            "Ajustar el volumen de la alarma",
            "Imposta il volume della sveglia");
        Add("L_AlarmAutoOffTitle",
            "Arrêt automatique",
            "Automatic switch-off",
            "Automatisch ausschalten",
            "Automatisch uitschakelen",
            "Apagado automático",
            "Spegnimento automatico");
        Add("L_AlarmSnoozeTitle",
            "Répétition",
            "Snooze",
            "Schlummern",
            "Sluimeren",
            "Repetición",
            "Posticipa");
        Add("L_AlarmSnoozeNote",
            "La répétition ne sert que si une touche de la télécommande (pouce ou AUX) répète le réveil : voir « Touche de répétition » plus bas.",
            "Snooze only works if a remote key (thumbs or AUX) snoozes the alarm: see \"Snooze key\" below.",
            "Schlummern wirkt nur, wenn eine Taste der Fernbedienung (Daumen oder AUX) den Wecker schlummern lässt: siehe „Schlummertaste“ weiter unten.",
            "Sluimeren werkt alleen als een toets van de afstandsbediening (duim of AUX) de wekker laat sluimeren: zie \"Sluimertoets\" hieronder.",
            "La repetición solo funciona si una tecla del mando (pulgar o AUX) repite la alarma: ver «Tecla de repetición» más abajo.",
            "Il posticipo funziona solo se un tasto del telecomando (pollici o AUX) posticipa la sveglia: vedi \"Tasto posticipa\" più sotto.");
        Add("L_AlarmEnabled",
            "Réveil activé",
            "Alarm on",
            "Wecker aktiv",
            "Wekker aan",
            "Alarma activada",
            "Sveglia attiva");
        Add("L_AlarmSave",
            "Enregistrer",
            "Save",
            "Speichern",
            "Opslaan",
            "Guardar",
            "Salva");
        Add("L_StartVolumeTitle",
            "VOLUME DE DÉPART",
            "START VOLUME",
            "STARTLAUTSTÄRKE",
            "STARTVOLUME",
            "VOLUMEN DE INICIO",
            "VOLUME DI AVVIO");
        Add("L_StartVolumeUse",
            "Démarrer toujours à ce volume",
            "Always start at this volume",
            "Immer mit dieser Lautstärke starten",
            "Altijd op dit volume starten",
            "Empezar siempre con este volumen",
            "Parti sempre con questo volume");
        Add("L_StartVolumeNote",
            "Le niveau que prend l'enceinte quand on l'allume et qu'elle se met à jouer. Un volume réglé pendant l'écoute n'est jamais modifié.",
            "The level the speaker takes when it is switched on and starts playing. A volume set while listening is never changed.",
            "Die Lautstärke, die der Lautsprecher beim Einschalten annimmt, sobald er spielt. Eine während des Hörens eingestellte Lautstärke wird nie geändert.",
            "Het niveau dat de speaker krijgt wanneer hij wordt aangezet en begint te spelen. Een volume dat tijdens het luisteren is ingesteld, wordt nooit gewijzigd.",
            "El nivel que toma el altavoz al encenderlo y empezar a sonar. Un volumen ajustado durante la escucha nunca se modifica.",
            "Il livello che il diffusore assume quando viene acceso e inizia a suonare. Un volume regolato durante l'ascolto non viene mai modificato.");
        Add("L_StartVolumeSave",
            "Enregistrer le volume de départ",
            "Save the start volume",
            "Startlautstärke speichern",
            "Startvolume opslaan",
            "Guardar el volumen de inicio",
            "Salva il volume di avvio");
        Add("L_SnoozeKeysTitle",
            "TOUCHE DE RÉPÉTITION",
            "SNOOZE KEY",
            "SCHLUMMERTASTE",
            "SLUIMERTOETS",
            "TECLA DE REPETICIÓN",
            "TASTO POSTICIPA");
        Add("L_SnoozeKeysNote",
            "La touche cochée répète un réveil qui sonne, après le délai choisi pour ce réveil. Hors réveil, un pouce ne fait rien ; AUX passe comme d'habitude sur l'entrée AUX.",
            "The ticked key snoozes a ringing alarm, for the delay chosen for that alarm. Outside an alarm, a thumbs key does nothing; AUX switches to the AUX input as usual.",
            "Die angehakte Taste lässt einen klingelnden Wecker schlummern, für die bei diesem Wecker gewählte Dauer. Außerhalb eines Weckers tut eine Daumentaste nichts; AUX schaltet wie gewohnt auf den AUX-Eingang.",
            "De aangevinkte toets laat een afgaande wekker sluimeren, voor de tijd die voor die wekker is gekozen. Buiten een wekker doet een duimtoets niets; AUX schakelt zoals gewoonlijk naar de AUX-ingang.",
            "La tecla marcada repite una alarma que suena, tras el tiempo elegido para esa alarma. Fuera de una alarma, un pulgar no hace nada; AUX cambia como siempre a la entrada AUX.",
            "Il tasto spuntato posticipa una sveglia che suona, del tempo scelto per quella sveglia. Fuori da una sveglia, un tasto pollice non fa nulla; AUX passa come sempre all'ingresso AUX.");
        Add("L_SnoozeKeyAux",
            "AUX",
            "AUX",
            "AUX",
            "AUX",
            "AUX",
            "AUX");
        Add("L_SnoozeKeyThumbsUp",
            "Pouce levé 👍",
            "Thumbs up 👍",
            "Daumen hoch 👍",
            "Duim omhoog 👍",
            "Pulgar arriba 👍",
            "Pollice su 👍");
        Add("L_SnoozeKeyThumbsDown",
            "Pouce baissé 👎",
            "Thumbs down 👎",
            "Daumen runter 👎",
            "Duim omlaag 👎",
            "Pulgar abajo 👎",
            "Pollice giù 👎");
        Add("L_SnoozeKeyCurrent",
            "{0} (aujourd'hui : {1})",
            "{0} (now: {1})",
            "{0} (derzeit: {1})",
            "{0} (nu: {1})",
            "{0} (ahora: {1})",
            "{0} (ora: {1})");
        Add("L_SnoozeKeysSave",
            "Enregistrer la touche de répétition",
            "Save the snooze key",
            "Schlummertaste speichern",
            "Sluimertoets opslaan",
            "Guardar la tecla de repetición",
            "Salva il tasto posticipa");
        Add("S_SnoozeKeyReplaces",
            "{0} : la répétition remplacera « {1} ».",
            "{0}: snooze will replace \"{1}\".",
            "{0}: Schlummern ersetzt „{1}“.",
            "{0}: sluimeren vervangt \"{1}\".",
            "{0}: la repetición sustituirá «{1}».",
            "{0}: il posticipo sostituirà \"{1}\".");
        Add("S_SnoozeKeysSaved",
            "Répétition du réveil enregistrée sur : {0}.",
            "Snooze saved on: {0}.",
            "Schlummern gespeichert auf: {0}.",
            "Sluimeren opgeslagen op: {0}.",
            "Repetición guardada en: {0}.",
            "Posticipo salvato su: {0}.");
        Add("S_SnoozeKeysNone",
            "Plus aucune touche ne répète le réveil.",
            "No key snoozes the alarm any more.",
            "Keine Taste lässt den Wecker mehr schlummern.",
            "Geen enkele toets laat de wekker nog sluimeren.",
            "Ya ninguna tecla repite la alarma.",
            "Nessun tasto posticipa più la sveglia.");
        Add("L_KeyActionSnooze",
            "répéter le réveil",
            "snooze",
            "Schlummern",
            "sluimeren",
            "repetir la alarma",
            "posticipa");
        Add("L_KeyActionSleep",
            "minuterie de sommeil",
            "sleep timer",
            "Einschlaftimer",
            "slaaptimer",
            "temporizador de apagado",
            "timer di spegnimento");
        Add("L_KeyActionSleepFixed",
            "minuterie {0} min",
            "sleep timer {0} min",
            "Einschlaftimer {0} Min.",
            "slaaptimer {0} min",
            "temporizador {0} min",
            "timer {0} min");
        Add("L_KeyActionBassUp",
            "graves +",
            "bass +",
            "Bass +",
            "bas +",
            "graves +",
            "bassi +");
        Add("L_KeyActionBassDown",
            "graves −",
            "bass −",
            "Bass −",
            "bas −",
            "graves −",
            "bassi −");
        Add("L_KeyActionShuffle",
            "lecture aléatoire Spotify",
            "Spotify shuffle",
            "Spotify-Zufallswiedergabe",
            "Spotify shuffle",
            "aleatorio de Spotify",
            "riproduzione casuale Spotify");
        Add("L_KeyActionRepeat",
            "répétition Spotify",
            "Spotify repeat",
            "Spotify-Wiederholung",
            "Spotify herhalen",
            "repetición de Spotify",
            "ripetizione Spotify");

        // ---------------------------------------------------------- Garder ce qui joue
        Add("L_KeepPlaying",
            "Garder ce qui joue sur une touche",
            "Keep what's playing on a key",
            "Laufendes auf eine Taste legen",
            "Wat nu speelt op een toets zetten",
            "Guardar lo que suena en una tecla",
            "Salva su un tasto ciò che suona");
        Add("L_KeepPlayingTitle", "Sur quelle touche ?", "On which key?", "Auf welche Taste?", "Op welke toets?", "¿En qué tecla?", "Su quale tasto?");
        Add("S_NeedAgentKeep",
            "Garder ce qui joue demande l'agent STR.",
            "Keeping what's playing needs the STR agent.",
            "Laufendes zu speichern erfordert den STR-Agenten.",
            "Bewaren wat nu speelt vereist de STR-agent.",
            "Guardar lo que suena requiere el agente STR.",
            "Per salvare ciò che suona serve l'agente STR.");
        Add("S_Kept",
            "{0} : enregistré sur la touche {1}",
            "{0} saved to key {1}",
            "{0} auf Taste {1} gespeichert",
            "{0} opgeslagen op toets {1}",
            "{0}: guardado en la tecla {1}",
            "{0} salvata sul tasto {1}");
        Add("S_KeptAlready",
            "{0} est déjà sur la touche {1}",
            "{0} is already on key {1}",
            "{0} liegt bereits auf Taste {1}",
            "{0} staat al op toets {1}",
            "{0} ya está en la tecla {1}",
            "{0} è già sul tasto {1}");
        Add("S_NothingToKeep",
            "Ce qui joue ne peut pas être gardé sur une touche (veille, AUX, Bluetooth ou Spotify).",
            "What's playing cannot be kept on a key (standby, AUX, Bluetooth or Spotify).",
            "Das Laufende lässt sich nicht auf eine Taste legen (Standby, AUX, Bluetooth oder Spotify).",
            "Wat nu speelt kan niet op een toets worden gezet (stand-by, AUX, Bluetooth of Spotify).",
            "Lo que suena no se puede guardar en una tecla (reposo, AUX, Bluetooth o Spotify).",
            "Ciò che suona non può essere salvato su un tasto (standby, AUX, Bluetooth o Spotify).");

        // ---------------------------------------------------------- Historique : bibliothèque
        Add("S_LibraryOffline",
            "Le serveur de médias est éteint ou introuvable : rien n'a été lancé.",
            "The media server is off or cannot be found: nothing was started.",
            "Der Medienserver ist aus oder nicht auffindbar: Es wurde nichts gestartet.",
            "De mediaserver staat uit of is niet te vinden: er is niets gestart.",
            "El servidor multimedia está apagado o no se encuentra: no se ha iniciado nada.",
            "Il server multimediale è spento o introvabile: non è stato avviato nulla.");
        Add("S_LibraryOfflineNamed",
            "Le serveur de médias {0} est éteint ou introuvable : rien n'a été lancé.",
            "Media server {0} is off or cannot be found: nothing was started.",
            "Der Medienserver {0} ist aus oder nicht auffindbar: Es wurde nichts gestartet.",
            "Mediaserver {0} staat uit of is niet te vinden: er is niets gestart.",
            "El servidor multimedia {0} está apagado o no se encuentra: no se ha iniciado nada.",
            "Il server multimediale {0} è spento o introvabile: non è stato avviato nulla.");

        // ---------------------------------------------------------- Minuterie de sommeil
        Add("L_Sleep", "Minuterie de sommeil", "Sleep timer", "Sleeptimer", "Slaaptimer", "Temporizador de apagado", "Timer di spegnimento");
        Add("L_SleepIn", "Veille dans {0} min", "Standby in {0} min", "Standby in {0} Min.", "Stand-by over {0} min", "Reposo en {0} min", "Standby tra {0} min");
        Add("L_SleepInGroup",
            "Veille du groupe dans {0} min",
            "Group standby in {0} min",
            "Gruppe in {0} Min. im Standby",
            "Groep in stand-by over {0} min",
            "Grupo en reposo en {0} min",
            "Standby del gruppo tra {0} min");
        Add("L_SleepTitle", "Mettre en veille dans…", "Go to standby in…", "In den Standby in…", "Naar stand-by over…", "Pasar a reposo en…", "Passare in standby tra…");
        Add("L_SleepCancel", "Annuler la minuterie", "Cancel the timer", "Timer abbrechen", "Timer annuleren", "Cancelar el temporizador", "Annulla il timer");
        Add("L_Minutes", "{0} min", "{0} min", "{0} Min.", "{0} min", "{0} min", "{0} min");
        Add("L_SleepScope",
            "Que faut-il mettre en veille ?",
            "What should go to standby?",
            "Was soll in den Standby?",
            "Wat moet naar stand-by?",
            "¿Qué debe pasar a reposo?",
            "Cosa deve andare in standby?");
        Add("L_SleepWholeGroup", "Tout le groupe", "The whole group", "Die ganze Gruppe", "De hele groep", "Todo el grupo", "Tutto il gruppo");
        Add("L_SleepOnlyThis", "Seulement {0}", "Only {0}", "Nur {0}", "Alleen {0}", "Solo {0}", "Solo {0}");
        Add("S_NeedAgentSleep",
            "La minuterie de sommeil demande l'agent STR.",
            "The sleep timer needs the STR agent.",
            "Der Sleeptimer erfordert den STR-Agenten.",
            "De slaaptimer vereist de STR-agent.",
            "El temporizador requiere el agente STR.",
            "Il timer di spegnimento richiede l'agente STR.");
        Add("S_SleepCancelled", "Minuterie annulée", "Timer cancelled", "Timer abgebrochen", "Timer geannuleerd", "Temporizador cancelado", "Timer annullato");
        Add("S_SleepSet",
            "{0} passera en veille dans {1} min",
            "{0} will go to standby in {1} min",
            "{0} geht in {1} Min. in den Standby",
            "{0} gaat over {1} min naar stand-by",
            "{0} pasará a reposo en {1} min",
            "{0} andrà in standby tra {1} min");
        Add("S_SleepSetGroup",
            "Le groupe passera en veille dans {0} min",
            "The group will go to standby in {0} min",
            "Die Gruppe geht in {0} Min. in den Standby",
            "De groep gaat over {0} min naar stand-by",
            "El grupo pasará a reposo en {0} min",
            "Il gruppo andrà in standby tra {0} min");

        // ---------------------------------------------------------- Présélections « Local » et message vocal (1.8.3)
        Add("L_RowLocal",
            "Local",
            "Local",
            "Lokal",
            "Lokaal",
            "Local",
            "Locale");
        Add("L_RowFavorites",
            "★ STR",
            "★ STR",
            "★ STR",
            "★ STR",
            "★ STR",
            "★ STR");
        Add("L_AddLocal",
            "Ajouter à Local",
            "Add to Local",
            "Zu Lokal hinzufügen",
            "Toevoegen aan Lokaal",
            "Añadir a Local",
            "Aggiungi a Locale");
        Add("L_FavNote",
            "Les stations étoilées de l'application STR : les mêmes sur toutes les enceintes STR et sur l'ordinateur. Un favori ajouté ou retiré ici l'est aussi là-bas. Toucher un favori le lance sur l'enceinte.",
            "The starred stations of the STR app: the same on every STR speaker and on the computer. A favourite added or removed here is added or removed there too. Tap a favourite to play it on the speaker.",
            "Die mit Stern markierten Sender der STR-App: dieselben auf allen STR-Lautsprechern und am Computer. Ein hier hinzugefügter oder entfernter Favorit wird auch dort hinzugefügt oder entfernt. Antippen spielt ihn auf dem Lautsprecher.",
            "De zenders met een ster in de STR-app: dezelfde op elke STR-speaker en op de computer. Een favoriet die je hier toevoegt of verwijdert, verandert daar ook. Tik op een favoriet om hem op de speaker te spelen.",
            "Las emisoras con estrella de la aplicación STR: las mismas en todos los altavoces STR y en el ordenador. Un favorito añadido o quitado aquí también lo es allí. Toca un favorito para ponerlo en el altavoz.",
            "Le stazioni con la stella dell'app STR: le stesse su tutti i diffusori STR e sul computer. Un preferito aggiunto o tolto qui lo è anche là. Tocca un preferito per avviarlo sul diffusore.");
        Add("L_FavEmpty",
            "Aucun favori pour l'instant. Ajoute une station depuis la recherche (⋯ puis « ★ Ajouter aux favoris STR ») ou ce qui joue.",
            "No favourites yet. Add a station from the search (⋯ then \"★ Add to STR favourites\") or what is playing.",
            "Noch keine Favoriten. Füge einen Sender aus der Suche hinzu (⋯ und dann „★ Zu STR-Favoriten“) oder das, was gerade läuft.",
            "Nog geen favorieten. Voeg een zender toe vanuit het zoeken (⋯ en dan \"★ Aan STR-favorieten toevoegen\") of wat nu speelt.",
            "Aún no hay favoritos. Añade una emisora desde la búsqueda (⋯ y luego «★ Añadir a favoritos STR») o lo que suena.",
            "Nessun preferito per ora. Aggiungi una stazione dalla ricerca (⋯ poi \"★ Aggiungi ai preferiti STR\") o ciò che suona.");
        Add("L_FavAddPlaying",
            "★ Ajouter ce qui joue",
            "★ Add what's playing",
            "★ Laufendes hinzufügen",
            "★ Wat nu speelt toevoegen",
            "★ Añadir lo que suena",
            "★ Aggiungi ciò che suona");
        Add("L_AddFavorite",
            "★ Ajouter aux favoris STR",
            "★ Add to STR favourites",
            "★ Zu STR-Favoriten",
            "★ Aan STR-favorieten toevoegen",
            "★ Añadir a favoritos STR",
            "★ Aggiungi ai preferiti STR");
        Add("L_FavOnKey",
            "Mettre sur une touche…",
            "Put on a key…",
            "Auf eine Taste legen…",
            "Op een toets zetten…",
            "Poner en una tecla…",
            "Metti su un tasto…");
        Add("L_FavRemove",
            "Retirer des favoris STR",
            "Remove from STR favourites",
            "Aus STR-Favoriten entfernen",
            "Uit STR-favorieten verwijderen",
            "Quitar de favoritos STR",
            "Togli dai preferiti STR");
        Add("L_FavRemoveGo",
            "Retirer",
            "Remove",
            "Entfernen",
            "Verwijderen",
            "Quitar",
            "Togli");
        Add("L_FavRemoveConfirm",
            "Retirer « {0} » des favoris STR ? Il disparaît aussi des autres enceintes et de l'ordinateur.",
            "Remove \"{0}\" from the STR favourites? It also goes from the other speakers and the computer.",
            "„{0}“ aus den STR-Favoriten entfernen? Er verschwindet auch von den anderen Lautsprechern und vom Computer.",
            "\"{0}\" uit de STR-favorieten verwijderen? Hij verdwijnt ook van de andere speakers en van de computer.",
            "¿Quitar «{0}» de los favoritos STR? También desaparece de los demás altavoces y del ordenador.",
            "Togliere \"{0}\" dai preferiti STR? Sparisce anche dagli altri diffusori e dal computer.");
        Add("S_FavNotPlayable",
            "« {0} » se lance depuis l'application STR (station sans adresse de flux).",
            "\"{0}\" plays from the STR app (station without a stream address).",
            "„{0}“ startet über die STR-App (Sender ohne Streamadresse).",
            "\"{0}\" start vanuit de STR-app (zender zonder streamadres).",
            "«{0}» se pone desde la aplicación STR (emisora sin dirección de flujo).",
            "\"{0}\" si avvia dall'app STR (stazione senza indirizzo di flusso).");
        Add("S_FavRemoved",
            "« {0} » retiré des favoris STR.",
            "\"{0}\" removed from the STR favourites.",
            "„{0}“ aus den STR-Favoriten entfernt.",
            "\"{0}\" uit de STR-favorieten verwijderd.",
            "«{0}» quitado de los favoritos STR.",
            "\"{0}\" tolto dai preferiti STR.");
        Add("S_FavNotRemoved",
            "« {0} » est toujours dans les favoris STR : l'enceinte ne l'a pas retiré.",
            "\"{0}\" is still in the STR favourites: the speaker did not remove it.",
            "„{0}“ ist noch in den STR-Favoriten: der Lautsprecher hat ihn nicht entfernt.",
            "\"{0}\" staat nog in de STR-favorieten: de speaker heeft hem niet verwijderd.",
            "«{0}» sigue en los favoritos STR: el altavoz no lo ha quitado.",
            "\"{0}\" è ancora nei preferiti STR: il diffusore non l'ha tolto.");
        Add("S_FavAdded",
            "« {0} » ajouté aux favoris STR.",
            "\"{0}\" added to the STR favourites.",
            "„{0}“ zu den STR-Favoriten hinzugefügt.",
            "\"{0}\" aan de STR-favorieten toegevoegd.",
            "«{0}» añadido a los favoritos STR.",
            "\"{0}\" aggiunto ai preferiti STR.");
        Add("S_FavAlready",
            "« {0} » est déjà dans les favoris STR.",
            "\"{0}\" is already in the STR favourites.",
            "„{0}“ ist schon in den STR-Favoriten.",
            "\"{0}\" staat al in de STR-favorieten.",
            "«{0}» ya está en los favoritos STR.",
            "\"{0}\" è già nei preferiti STR.");
        Add("S_FavNothing",
            "Rien à ajouter : l'enceinte ne joue pas de radio internet.",
            "Nothing to add: the speaker is not playing an internet radio.",
            "Nichts hinzuzufügen: der Lautsprecher spielt kein Internetradio.",
            "Niets toe te voegen: de speaker speelt geen internetradio.",
            "Nada que añadir: el altavoz no está poniendo una radio por internet.",
            "Niente da aggiungere: il diffusore non sta suonando una radio internet.");
        Add("S_FavAgentTooOld",
            "L'agent STR de cette enceinte ne sait pas encore ajouter un favori depuis le téléphone : mets-le à jour avec l'application STR sur ordinateur.",
            "The STR agent on this speaker cannot add a favourite from the phone yet: update it with the STR app on a computer.",
            "Der STR-Agent dieses Lautsprechers kann noch keinen Favoriten vom Telefon hinzufügen: aktualisiere ihn mit der STR-App am Computer.",
            "De STR-agent van deze speaker kan nog geen favoriet vanaf de telefoon toevoegen: werk hem bij met de STR-app op de computer.",
            "El agente STR de este altavoz aún no puede añadir un favorito desde el teléfono: actualízalo con la aplicación STR del ordenador.",
            "L'agente STR di questo diffusore non sa ancora aggiungere un preferito dal telefono: aggiornalo con l'app STR sul computer.");
        Add("L_LocalEmpty",
            "Aucune présélection locale. Ajoute la radio qui joue, une adresse, ou une station trouvée par la recherche (« Ajouter à Local »). Elles restent dans ce téléphone.",
            "No local presets yet. Add the radio that is playing, an address, or a station found by the search (“Add to Local”). They stay on this phone.",
            "Noch keine lokalen Presets. Füge den laufenden Sender, eine Adresse oder einen in der Suche gefundenen Sender hinzu („Zu Lokal hinzufügen“). Sie bleiben auf diesem Telefon.",
            "Nog geen lokale presets. Voeg de zender toe die speelt, een adres, of een zender uit de zoekfunctie (“Toevoegen aan Lokaal”). Ze blijven op deze telefoon.",
            "Aún no hay presintonías locales. Añade la radio que suena, una dirección o una emisora encontrada en la búsqueda («Añadir a Local»). Se quedan en este teléfono.",
            "Nessuna preselezione locale. Aggiungi la radio in riproduzione, un indirizzo o una stazione trovata con la ricerca («Aggiungi a Locale»). Restano su questo telefono.");
        Add("L_LocalAddPlaying",
            "Ajouter ce qui joue",
            "Add what is playing",
            "Laufenden Sender hinzufügen",
            "Wat speelt toevoegen",
            "Añadir lo que suena",
            "Aggiungi ciò che suona");
        Add("L_LocalAddUrl",
            "Ajouter une adresse",
            "Add an address",
            "Adresse hinzufügen",
            "Adres toevoegen",
            "Añadir una dirección",
            "Aggiungi un indirizzo");
        Add("L_LocalUrlTitle",
            "Adresse du flux",
            "Stream address",
            "Stream-Adresse",
            "Streamadres",
            "Dirección del flujo",
            "Indirizzo del flusso");
        Add("L_LocalUrlMessage",
            "Colle l'adresse http ou https de la radio.",
            "Paste the radio's http or https address.",
            "Füge die http- oder https-Adresse des Senders ein.",
            "Plak het http- of https-adres van de zender.",
            "Pega la dirección http o https de la radio.",
            "Incolla l'indirizzo http o https della radio.");
        Add("L_Next",
            "Suivant",
            "Next",
            "Weiter",
            "Volgende",
            "Siguiente",
            "Avanti");
        Add("L_LocalNameTitle",
            "Nom de la radio",
            "Radio name",
            "Name des Senders",
            "Naam van de zender",
            "Nombre de la radio",
            "Nome della radio");
        Add("L_LocalNameMessage",
            "Le nom affiché dans la liste.",
            "The name shown in the list.",
            "Der Name in der Liste.",
            "De naam in de lijst.",
            "El nombre que se muestra en la lista.",
            "Il nome mostrato nell'elenco.");
        Add("L_LocalAddGo",
            "Ajouter",
            "Add",
            "Hinzufügen",
            "Toevoegen",
            "Añadir",
            "Aggiungi");
        Add("L_LocalRenameTitle",
            "Renommer la présélection",
            "Rename the preset",
            "Preset umbenennen",
            "Preset hernoemen",
            "Renombrar la presintonía",
            "Rinomina la preselezione");
        Add("L_LocalRemoveGo",
            "Retirer",
            "Remove",
            "Entfernen",
            "Verwijderen",
            "Quitar",
            "Rimuovi");
        Add("L_LocalRemoveConfirm",
            "Retirer {0} des présélections locales ?",
            "Remove {0} from the local presets?",
            "{0} aus den lokalen Presets entfernen?",
            "{0} uit de lokale presets verwijderen?",
            "¿Quitar {0} de las presintonías locales?",
            "Rimuovere {0} dalle preselezioni locali?");
        Add("S_LocalNothing",
            "Rien à ajouter : seule une radio internet dont l'adresse est connue peut être gardée.",
            "Nothing to add: only an internet radio whose address is known can be kept.",
            "Nichts hinzuzufügen: Nur ein Internetradio mit bekannter Adresse kann gespeichert werden.",
            "Niets toe te voegen: alleen een internetradio met een bekend adres kan worden bewaard.",
            "Nada que añadir: solo se puede guardar una radio por internet cuya dirección se conoce.",
            "Niente da aggiungere: si può salvare solo una radio internet di cui si conosce l'indirizzo.");
        Add("S_LocalBadUrl",
            "Adresse non valable : elle doit commencer par http:// ou https://.",
            "Invalid address: it must start with http:// or https://.",
            "Ungültige Adresse: Sie muss mit http:// oder https:// beginnen.",
            "Ongeldig adres: het moet beginnen met http:// of https://.",
            "Dirección no válida: debe empezar por http:// o https://.",
            "Indirizzo non valido: deve iniziare con http:// o https://.");
        Add("S_LocalAlready",
            "{0} est déjà dans la liste locale.",
            "{0} is already in the local list.",
            "{0} ist bereits in der lokalen Liste.",
            "{0} staat al in de lokale lijst.",
            "{0} ya está en la lista local.",
            "{0} è già nell'elenco locale.");
        Add("S_LocalFull",
            "La liste locale est pleine ({0} présélections).",
            "The local list is full ({0} presets).",
            "Die lokale Liste ist voll ({0} Presets).",
            "De lokale lijst is vol ({0} presets).",
            "La lista local está llena ({0} presintonías).",
            "L'elenco locale è pieno ({0} preselezioni).");
        Add("S_LocalAdded",
            "{0} ajoutée aux présélections locales.",
            "{0} added to the local presets.",
            "{0} zu den lokalen Presets hinzugefügt.",
            "{0} toegevoegd aan de lokale presets.",
            "{0} añadida a las presintonías locales.",
            "{0} aggiunta alle preselezioni locali.");
        Add("S_LocalRenamed",
            "Renommée : {0}.",
            "Renamed: {0}.",
            "Umbenannt: {0}.",
            "Hernoemd: {0}.",
            "Renombrada: {0}.",
            "Rinominata: {0}.");
        Add("S_LocalRemoved",
            "{0} retirée.",
            "{0} removed.",
            "{0} entfernt.",
            "{0} verwijderd.",
            "{0} quitada.",
            "{0} rimossa.");
        Add("S_LocalNotSaved",
            "La liste n'a pas pu être enregistrée sur le téléphone.",
            "The list could not be saved on the phone.",
            "Die Liste konnte nicht auf dem Telefon gespeichert werden.",
            "De lijst kon niet op de telefoon worden opgeslagen.",
            "No se pudo guardar la lista en el teléfono.",
            "Impossibile salvare l'elenco sul telefono.");
        Add("L_VoiceTitle",
            "Message vocal",
            "Voice message",
            "Sprachnachricht",
            "Spraakbericht",
            "Mensaje de voz",
            "Messaggio vocale");
        Add("L_VoiceTextPlaceholder",
            "Texte à lire sur l'enceinte",
            "Text for the speaker to read",
            "Text, den der Lautsprecher vorliest",
            "Tekst die de luidspreker voorleest",
            "Texto que leerá el altavoz",
            "Testo da leggere sul diffusore");
        Add("L_VoiceSpeak",
            "Lire",
            "Read",
            "Vorlesen",
            "Voorlezen",
            "Leer",
            "Leggi");
        Add("L_VoiceHint",
            "Le micro n'enregistre que pendant l'appui sur le bouton rond ; le son part vers ton enceinte, sur le réseau local, puis est effacé du téléphone. L'enceinte joue le message puis reprend ce qu'elle jouait. Le texte est lu par la voix de Google : il passe par Google, et l'enceinte doit avoir accès à internet. En groupe, le message passe par l'enceinte choisie.",
            "The microphone records only while you hold the round button; the sound goes to your speaker over the local network, then is deleted from the phone. The speaker plays the message, then goes back to what it was playing. The text is read by Google's voice: it goes through Google, and the speaker needs internet access. In a group, the message goes to the selected speaker.",
            "Das Mikrofon nimmt nur auf, solange du den runden Knopf hältst; der Ton geht über das lokale Netz an deinen Lautsprecher und wird danach vom Telefon gelöscht. Der Lautsprecher spielt die Nachricht und setzt dann fort, was er gespielt hat. Der Text wird von der Google-Stimme gelesen: Er läuft über Google, und der Lautsprecher braucht Internetzugang. In einer Gruppe geht die Nachricht an den gewählten Lautsprecher.",
            "De microfoon neemt alleen op terwijl je de ronde knop vasthoudt; het geluid gaat via het lokale netwerk naar je speaker en wordt daarna van de telefoon gewist. De luidspreker speelt het bericht en gaat daarna verder met wat hij speelde. De tekst wordt voorgelezen door de stem van Google: hij gaat via Google, en de luidspreker heeft internettoegang nodig. In een groep gaat het bericht naar de gekozen luidspreker.",
            "El micrófono solo graba mientras mantienes el botón redondo; el sonido va a tu altavoz por la red local y luego se borra del teléfono. El altavoz reproduce el mensaje y luego vuelve a lo que sonaba. El texto lo lee la voz de Google: pasa por Google, y el altavoz necesita acceso a internet. En un grupo, el mensaje va al altavoz elegido.",
            "Il microfono registra solo mentre tieni premuto il pulsante rotondo; il suono va al tuo diffusore sulla rete locale, poi viene cancellato dal telefono. Il diffusore riproduce il messaggio, poi riprende ciò che suonava. Il testo è letto dalla voce di Google: passa da Google, e il diffusore deve avere accesso a internet. In un gruppo, il messaggio va al diffusore scelto.");
        Add("S_VoiceNoMic",
            "Le micro n'est pas autorisé. Autorise-le dans les réglages d'Android.",
            "The microphone is not allowed. Allow it in the Android settings.",
            "Das Mikrofon ist nicht erlaubt. Erlaube es in den Android-Einstellungen.",
            "De microfoon is niet toegestaan. Sta hem toe in de Android-instellingen.",
            "El micrófono no está permitido. Permítelo en los ajustes de Android.",
            "Il microfono non è autorizzato. Autorizzalo nelle impostazioni di Android.");
        Add("S_VoiceMicFailed",
            "Le micro n'a pas pu démarrer.",
            "The microphone could not start.",
            "Das Mikrofon konnte nicht starten.",
            "De microfoon kon niet starten.",
            "No se pudo iniciar el micrófono.",
            "Impossibile avviare il microfono.");
        Add("S_VoiceCancelled",
            "Message abandonné.",
            "Message discarded.",
            "Nachricht verworfen.",
            "Bericht verworpen.",
            "Mensaje descartado.",
            "Messaggio annullato.");
        Add("S_VoiceNothing",
            "Rien n'a été enregistré.",
            "Nothing was recorded.",
            "Es wurde nichts aufgenommen.",
            "Er is niets opgenomen.",
            "No se ha grabado nada.",
            "Non è stato registrato nulla.");
        Add("S_VoiceTooBig",
            "Message trop lourd pour l'enceinte (8 Mo au plus).",
            "Message too large for the speaker (8 MB at most).",
            "Nachricht zu groß für den Lautsprecher (höchstens 8 MB).",
            "Bericht te groot voor de luidspreker (hoogstens 8 MB).",
            "Mensaje demasiado grande para el altavoz (8 MB como máximo).",
            "Messaggio troppo grande per il diffusore (8 MB al massimo).");
        Add("S_VoiceSending",
            "Envoi à l'enceinte…",
            "Sending to the speaker…",
            "Wird an den Lautsprecher gesendet…",
            "Versturen naar de luidspreker…",
            "Enviando al altavoz…",
            "Invio al diffusore…");
        Add("S_VoiceSent",
            "Message envoyé à {0}.",
            "Message sent to {0}.",
            "Nachricht an {0} gesendet.",
            "Bericht verstuurd naar {0}.",
            "Mensaje enviado a {0}.",
            "Messaggio inviato a {0}.");
        Add("S_VoiceSpoken",
            "Texte envoyé à {0}.",
            "Text sent to {0}.",
            "Text an {0} gesendet.",
            "Tekst verstuurd naar {0}.",
            "Texto enviado a {0}.",
            "Testo inviato a {0}.");
        Add("S_VoiceNoNetwork",
            "Le téléphone n'a pas d'adresse sur le réseau de l'enceinte.",
            "The phone has no address on the speaker's network.",
            "Das Telefon hat keine Adresse im Netzwerk des Lautsprechers.",
            "De telefoon heeft geen adres op het netwerk van de luidspreker.",
            "El teléfono no tiene dirección en la red del altavoz.",
            "Il telefono non ha un indirizzo sulla rete del diffusore.");
        Add("S_VoiceNotFetched",
            "L'enceinte n'est pas venue chercher le message. Le Wi-Fi isole peut-être les appareils (réseau invité).",
            "The speaker did not fetch the message. The Wi-Fi may be isolating devices (guest network).",
            "Der Lautsprecher hat die Nachricht nicht abgeholt. Das WLAN isoliert vielleicht die Geräte (Gastnetz).",
            "De luidspreker heeft het bericht niet opgehaald. De wifi isoleert misschien de apparaten (gastnetwerk).",
            "El altavoz no recogió el mensaje. Quizá la wifi aísla los dispositivos (red de invitados).",
            "Il diffusore non ha prelevato il messaggio. Forse il Wi-Fi isola i dispositivi (rete ospiti).");
        Add("S_VoiceNeedAgent",
            "Le message vocal demande l'agent STR.",
            "The voice message needs the STR agent.",
            "Die Sprachnachricht erfordert den STR-Agenten.",
            "Het spraakbericht vereist de STR-agent.",
            "El mensaje de voz requiere el agente STR.",
            "Il messaggio vocale richiede l'agente STR.");
        Add("S_VoiceUnsupported",
            "L'agent STR de cette enceinte ne connaît pas encore les annonces. Mets-le à jour.",
            "This speaker's STR agent does not know announcements yet. Update it.",
            "Der STR-Agent dieses Lautsprechers kennt noch keine Durchsagen. Aktualisiere ihn.",
            "De STR-agent van deze luidspreker kent nog geen aankondigingen. Werk hem bij.",
            "El agente STR de este altavoz aún no admite anuncios. Actualízalo.",
            "L'agente STR di questo diffusore non conosce ancora gli annunci. Aggiornalo.");
        Add("L_VoiceHoldHint",
            "Maintiens pour parler, relâche pour envoyer",
            "Hold to talk, release to send",
            "Halten zum Sprechen, loslassen zum Senden",
            "Ingedrukt houden om te spreken, loslaten om te versturen",
            "Mantén pulsado para hablar, suelta para enviar",
            "Tieni premuto per parlare, rilascia per inviare");
        Add("L_VoiceReleaseHint",
            "Relâche pour envoyer",
            "Release to send",
            "Loslassen zum Senden",
            "Loslaten om te versturen",
            "Suelta para enviar",
            "Rilascia per inviare");
        Add("S_VoiceRecording",
            "Je t'écoute… relâche pour envoyer (1 minute au plus).",
            "Listening… release to send (1 minute at most).",
            "Ich höre zu… loslassen zum Senden (höchstens 1 Minute).",
            "Ik luister… loslaten om te versturen (hoogstens 1 minuut).",
            "Te escucho… suelta para enviar (1 minuto como máximo).",
            "Ti ascolto… rilascia per inviare (1 minuto al massimo).");
        Add("S_VoiceHold",
            "Maintiens le bouton pendant que tu parles, puis relâche-le pour envoyer.",
            "Hold the button while you speak, then release it to send.",
            "Halte die Taste gedrückt, während du sprichst, und lass sie dann zum Senden los.",
            "Houd de knop ingedrukt terwijl je spreekt en laat hem dan los om te versturen.",
            "Mantén pulsado el botón mientras hablas y suéltalo para enviar.",
            "Tieni premuto il pulsante mentre parli, poi rilascialo per inviare.");
        Add("L_VoiceKeep",
            "Garder",
            "Keep",
            "Behalten",
            "Bewaren",
            "Guardar",
            "Salva");
        Add("L_VoiceTexts",
            "Textes gardés",
            "Kept texts",
            "Gespeicherte Texte",
            "Bewaarde teksten",
            "Textos guardados",
            "Testi salvati");
        Add("L_VoiceTextEditTitle",
            "Modifier le texte",
            "Edit the text",
            "Text bearbeiten",
            "Tekst bewerken",
            "Editar el texto",
            "Modifica il testo");
        Add("L_VoiceTextEditMessage",
            "Le texte que l'enceinte lira.",
            "The text the speaker will read.",
            "Der Text, den der Lautsprecher vorliest.",
            "De tekst die de luidspreker zal voorlezen.",
            "El texto que leerá el altavoz.",
            "Il testo che il diffusore leggerà.");
        Add("L_VoiceTextEditGo",
            "Modifier",
            "Edit",
            "Bearbeiten",
            "Bewerken",
            "Editar",
            "Modifica");
        Add("L_VoiceTextDeleteGo",
            "Supprimer",
            "Delete",
            "Löschen",
            "Verwijderen",
            "Eliminar",
            "Elimina");
        Add("L_VoiceTextDeleteConfirm",
            "Supprimer « {0} » ?",
            "Delete “{0}”?",
            "„{0}“ löschen?",
            "“{0}” verwijderen?",
            "¿Eliminar «{0}»?",
            "Eliminare «{0}»?");
        Add("S_VoiceTextAlready",
            "Ce texte est déjà gardé.",
            "This text is already kept.",
            "Dieser Text ist bereits gespeichert.",
            "Deze tekst is al bewaard.",
            "Este texto ya está guardado.",
            "Questo testo è già salvato.");
        Add("S_VoiceTextsFull",
            "{0} textes au plus : supprimes-en un pour en garder un autre.",
            "{0} texts at most: delete one to keep another.",
            "Höchstens {0} Texte: Lösche einen, um einen anderen zu behalten.",
            "Hoogstens {0} teksten: verwijder er een om een andere te bewaren.",
            "{0} textos como máximo: elimina uno para guardar otro.",
            "{0} testi al massimo: eliminane uno per salvarne un altro.");
        Add("S_VoiceTextKept",
            "Texte gardé.",
            "Text kept.",
            "Text gespeichert.",
            "Tekst bewaard.",
            "Texto guardado.",
            "Testo salvato.");
        Add("S_VoiceTextEdited",
            "Texte modifié.",
            "Text edited.",
            "Text bearbeitet.",
            "Tekst bewerkt.",
            "Texto editado.",
            "Testo modificato.");
        Add("S_VoiceTextDeleted",
            "Texte supprimé.",
            "Text deleted.",
            "Text gelöscht.",
            "Tekst verwijderd.",
            "Texto eliminado.",
            "Testo eliminato.");

        // ---------------------------------------------------------- Volume des annonces, sauvegarde des favoris (1.9.0)
        Add("L_VoiceVolumeSpeaker",
            "Volume des annonces : celui de l'enceinte",
            "Announcement volume: the speaker's own",
            "Lautstärke der Durchsagen: die des Lautsprechers",
            "Volume van aankondigingen: dat van de speaker",
            "Volumen de los anuncios: el del altavoz",
            "Volume degli annunci: quello del diffusore");
        Add("L_VoiceVolumeValue",
            "Volume des annonces : {0} %",
            "Announcement volume: {0} %",
            "Lautstärke der Durchsagen: {0} %",
            "Volume van aankondigingen: {0} %",
            "Volumen de los anuncios: {0} %",
            "Volume degli annunci: {0} %");
        // ---------------------------------------------------------- Mise à jour (version GitHub)
        Add("L_UpdateTitle", "MISE À JOUR", "UPDATE", "AKTUALISIERUNG", "UPDATE", "ACTUALIZACIÓN", "AGGIORNAMENTO");
        Add("L_UpdateInstalled", "Version installée : {0}", "Installed version: {0}", "Installierte Version: {0}", "Geïnstalleerde versie: {0}", "Versión instalada: {0}", "Versione installata: {0}");
        Add("L_UpdateAuto",
            "Vérifier automatiquement (une fois par jour au plus, auprès de GitHub)",
            "Check automatically (at most once a day, with GitHub)",
            "Automatisch prüfen (höchstens einmal am Tag, bei GitHub)",
            "Automatisch controleren (hoogstens één keer per dag, bij GitHub)",
            "Comprobar automáticamente (como mucho una vez al día, en GitHub)",
            "Controlla automaticamente (al massimo una volta al giorno, su GitHub)");
        Add("L_UpdateCheck", "Vérifier maintenant", "Check now", "Jetzt prüfen", "Nu controleren", "Comprobar ahora", "Controlla ora");
        Add("L_UpdateAvailable", "Version {0} disponible", "Version {0} available", "Version {0} verfügbar", "Versie {0} beschikbaar", "Versión {0} disponible", "Versione {0} disponibile");
        Add("L_UpdateNotes", "Nouveautés", "What's new", "Neuigkeiten", "Nieuw", "Novedades", "Novità");
        Add("L_UpdateInstall", "Installer", "Install", "Installieren", "Installeren", "Instalar", "Installa");
        Add("L_UpdateLater", "Plus tard", "Later", "Später", "Later", "Más tarde", "Più tardi");
        Add("L_AgentNewTitle",
            "AGENT STR : NOUVELLE VERSION",
            "STR AGENT: NEW VERSION",
            "STR-AGENT: NEUE VERSION",
            "STR-AGENT: NIEUWE VERSIE",
            "AGENTE STR: NUEVA VERSIÓN",
            "AGENTE STR: NUOVA VERSIONE");
        Add("L_AgentNewSummary",
            "La version {0} de l'agent STR est disponible.",
            "Version {0} of the STR agent is available.",
            "Version {0} des STR-Agenten ist verfügbar.",
            "Versie {0} van de STR-agent is beschikbaar.",
            "La versión {0} del agente STR está disponible.",
            "La versione {0} dell'agente STR è disponibile.");
        Add("L_AgentNewSpeakers",
            "À mettre à jour : {0}",
            "To update: {0}",
            "Zu aktualisieren: {0}",
            "Bij te werken: {0}",
            "Por actualizar: {0}",
            "Da aggiornare: {0}");
        Add("L_AgentNewBody",
            "L'agent se met à jour avec l'application STR sur ordinateur, pas depuis le téléphone. La version est lue sur GitHub au plus une fois par jour.",
            "The agent is updated with the STR app on a computer, not from the phone. The version is read from GitHub at most once a day.",
            "Der Agent wird mit der STR-App am Computer aktualisiert, nicht vom Telefon aus. Die Version wird höchstens einmal am Tag auf GitHub gelesen.",
            "De agent wordt bijgewerkt met de STR-app op de computer, niet vanaf de telefoon. De versie wordt hoogstens één keer per dag op GitHub gelezen.",
            "El agente se actualiza con la aplicación STR del ordenador, no desde el teléfono. La versión se lee en GitHub como mucho una vez al día.",
            "L'agente si aggiorna con l'app STR sul computer, non dal telefono. La versione viene letta su GitHub al massimo una volta al giorno.");
        Add("L_AgentNewShort",
            "{0} dispo",
            "{0} available",
            "{0} verfügbar",
            "{0} beschikbaar",
            "{0} disponible",
            "{0} disponibile");
        Add("L_UpdateOpenPage", "Ouvrir la page de la version", "Open the release page", "Versionsseite öffnen", "Versiepagina openen", "Abrir la página de la versión", "Apri la pagina della versione");
        Add("L_UpdateDownloading", "Téléchargement… {0} %", "Downloading… {0}%", "Download… {0} %", "Downloaden… {0}%", "Descargando… {0} %", "Download… {0}%");
        Add("L_UpdateHint",
            "Le fichier est vérifié avant l'installation (taille et empreinte publiées sur GitHub). Android te demande ensuite de confirmer.",
            "The file is checked before installing (size and fingerprint published on GitHub). Android then asks you to confirm.",
            "Die Datei wird vor der Installation geprüft (auf GitHub veröffentlichte Größe und Prüfsumme). Danach fragt Android nach deiner Bestätigung.",
            "Het bestand wordt vóór de installatie gecontroleerd (grootte en vingerafdruk zoals op GitHub). Daarna vraagt Android om te bevestigen.",
            "El archivo se comprueba antes de instalar (tamaño y huella publicados en GitHub). Después Android te pide confirmar.",
            "Il file viene controllato prima dell'installazione (dimensione e impronta pubblicate su GitHub). Poi Android ti chiede di confermare.");
        Add("S_UpdateChecking", "Vérification…", "Checking…", "Prüfung…", "Controleren…", "Comprobando…", "Controllo…");
        Add("S_UpdateNone", "Tu as la dernière version ({0}).", "You have the latest version ({0}).", "Du hast die neueste Version ({0}).", "Je hebt de nieuwste versie ({0}).", "Tienes la última versión ({0}).", "Hai l'ultima versione ({0}).");
        Add("S_UpdateFound", "La version {0} est disponible.", "Version {0} is available.", "Version {0} ist verfügbar.", "Versie {0} is beschikbaar.", "La versión {0} está disponible.", "La versione {0} è disponibile.");
        Add("S_UpdateCheckFailed",
            "Vérification impossible : GitHub n'a pas répondu. Le téléphone a-t-il accès à internet ?",
            "Could not check: GitHub did not answer. Does the phone have internet access?",
            "Prüfung nicht möglich: GitHub hat nicht geantwortet. Hat das Telefon Internetzugang?",
            "Controle niet mogelijk: GitHub antwoordde niet. Heeft de telefoon internet?",
            "No se pudo comprobar: GitHub no respondió. ¿Tiene el teléfono acceso a internet?",
            "Controllo impossibile: GitHub non ha risposto. Il telefono ha accesso a internet?");
        Add("S_UpdateAllowInstall",
            "Autorise SoundReborn Remote à installer des applications dans l'écran qui vient de s'ouvrir, puis reviens et touche « Installer ».",
            "Allow SoundReborn Remote to install apps in the screen that just opened, then come back and tap \"Install\".",
            "Erlaube SoundReborn Remote im eben geöffneten Bildschirm, Apps zu installieren, komm zurück und tippe auf „Installieren“.",
            "Sta SoundReborn Remote toe om apps te installeren in het scherm dat net openging, kom terug en tik op „Installeren”.",
            "Permite que SoundReborn Remote instale aplicaciones en la pantalla que se acaba de abrir, vuelve y toca «Instalar».",
            "Consenti a SoundReborn Remote di installare app nella schermata appena aperta, poi torna e tocca «Installa».");
        Add("S_UpdateInstallerOpened",
            "Fichier vérifié. Confirme l'installation dans l'écran d'Android.",
            "File checked. Confirm the installation in Android's screen.",
            "Datei geprüft. Bestätige die Installation im Android-Bildschirm.",
            "Bestand gecontroleerd. Bevestig de installatie in het scherm van Android.",
            "Archivo comprobado. Confirma la instalación en la pantalla de Android.",
            "File controllato. Conferma l'installazione nella schermata di Android.");
        Add("S_UpdateCancelled", "Téléchargement annulé.", "Download cancelled.", "Download abgebrochen.", "Download geannuleerd.", "Descarga cancelada.", "Download annullato.");
        Add("S_UpdateBadFile",
            "Le fichier téléchargé ne correspond pas à celui publié sur GitHub : il n'est pas installé.",
            "The downloaded file does not match the one published on GitHub: it is not installed.",
            "Die heruntergeladene Datei entspricht nicht der auf GitHub veröffentlichten: Sie wird nicht installiert.",
            "Het gedownloade bestand komt niet overeen met dat op GitHub: het wordt niet geïnstalleerd.",
            "El archivo descargado no coincide con el publicado en GitHub: no se instala.",
            "Il file scaricato non corrisponde a quello pubblicato su GitHub: non viene installato.");
        Add("S_UpdateDownloadFailed",
            "Le téléchargement a échoué. Tu peux réessayer, ou installer depuis la page de la version.",
            "The download failed. You can try again, or install from the release page.",
            "Der Download ist fehlgeschlagen. Versuch es noch einmal oder installiere über die Versionsseite.",
            "De download is mislukt. Probeer opnieuw of installeer via de versiepagina.",
            "La descarga falló. Puedes reintentarlo o instalar desde la página de la versión.",
            "Il download non è riuscito. Puoi riprovare o installare dalla pagina della versione.");

        Add("L_BackupTitle",
            "SAUVEGARDE DES FAVORIS",
            "FAVOURITES BACKUP",
            "SICHERUNG DER FAVORITEN",
            "BACK-UP VAN FAVORIETEN",
            "COPIA DE LOS FAVORITOS",
            "BACKUP DEI PREFERITI");
        Add("L_BackupHint",
            "Les présélections « Local » et les textes gardés ne vivent que dans ce téléphone : une mise à jour les garde, une désinstallation les efface. Exporte-les dans un fichier (Drive, e-mail…) pour les retrouver ; l'import ajoute ce qui manque, sans rien supprimer.",
            "Local presets and kept texts only live on this phone: an update keeps them, an uninstall erases them. Export them to a file (Drive, e-mail…) to get them back; importing adds what is missing and deletes nothing.",
            "Lokale Presets und gespeicherte Texte leben nur auf diesem Telefon: Ein Update behält sie, eine Deinstallation löscht sie. Exportiere sie in eine Datei (Drive, E-Mail…), um sie wiederzufinden; der Import fügt Fehlendes hinzu und löscht nichts.",
            "Lokale presets en bewaarde teksten leven alleen op deze telefoon: een update houdt ze, een verwijdering wist ze. Exporteer ze naar een bestand (Drive, e-mail…) om ze terug te vinden; importeren voegt toe wat ontbreekt en verwijdert niets.",
            "Las presintonías «Local» y los textos guardados solo viven en este teléfono: una actualización los conserva, una desinstalación los borra. Expórtalos a un archivo (Drive, correo…) para recuperarlos; la importación añade lo que falta sin borrar nada.",
            "Le preselezioni «Locale» e i testi salvati vivono solo in questo telefono: un aggiornamento li conserva, una disinstallazione li cancella. Esportali in un file (Drive, e-mail…) per ritrovarli; l'importazione aggiunge ciò che manca e non elimina nulla.");
        Add("L_BackupExport",
            "Exporter",
            "Export",
            "Exportieren",
            "Exporteren",
            "Exportar",
            "Esporta");
        Add("L_BackupImport",
            "Importer",
            "Import",
            "Importieren",
            "Importeren",
            "Importar",
            "Importa");
        Add("L_SpeakerBackupTitle",
            "SAUVEGARDE DE L'ENCEINTE",
            "SPEAKER BACKUP",
            "LAUTSPRECHER-SICHERUNG",
            "BACK-UP VAN DE SPEAKER",
            "COPIA DEL ALTAVOZ",
            "BACKUP DEL DIFFUSORE");
        Add("L_SpeakerBackupTarget",
            "Enceinte : {0}",
            "Speaker: {0}",
            "Lautsprecher: {0}",
            "Speaker: {0}",
            "Altavoz: {0}",
            "Diffusore: {0}");
        Add("L_SpeakerBackupNoAgent",
            "{0} : l'agent STR est nécessaire.",
            "{0}: the STR agent is needed.",
            "{0}: der STR-Agent wird benötigt.",
            "{0}: de STR-agent is nodig.",
            "{0}: se necesita el agente STR.",
            "{0}: serve l'agente STR.");
        Add("L_SpeakerBackupHint",
            "Les touches 1 à 12, les réveils, le volume de départ et la touche de répétition de l'enceinte sélectionnée, dans un fichier. La restauration peut aussi recopier ces réglages sur une autre enceinte. Le groupe permanent n'y est pas. Une touche Spotify ne joue que si l'enceinte est connectée au même compte Spotify.",
            "Keys 1 to 12, the alarms, the start volume and the snooze key of the selected speaker, in a file. Restoring can also copy these settings to another speaker. The permanent group is not included. A Spotify key only plays if the speaker is signed in to the same Spotify account.",
            "Die Tasten 1 bis 12, die Wecker, die Startlautstärke und die Schlummertaste des ausgewählten Lautsprechers, in einer Datei. Die Wiederherstellung kann diese Einstellungen auch auf einen anderen Lautsprecher übertragen. Die feste Gruppe ist nicht enthalten. Eine Spotify-Taste spielt nur, wenn der Lautsprecher mit demselben Spotify-Konto verbunden ist.",
            "De toetsen 1 tot 12, de wekkers, het startvolume en de sluimertoets van de geselecteerde speaker, in een bestand. Terugzetten kan deze instellingen ook naar een andere speaker kopiëren. De vaste groep zit er niet in. Een Spotify-toets speelt alleen als de speaker met hetzelfde Spotify-account verbonden is.",
            "Las teclas 1 a 12, las alarmas, el volumen de inicio y la tecla de repetición del altavoz seleccionado, en un archivo. La restauración también puede copiar estos ajustes a otro altavoz. El grupo permanente no está incluido. Una tecla de Spotify solo suena si el altavoz está conectado a la misma cuenta de Spotify.",
            "I tasti da 1 a 12, le sveglie, il volume di avvio e il tasto posticipa del diffusore selezionato, in un file. Il ripristino può anche copiare queste impostazioni su un altro diffusore. Il gruppo permanente non è incluso. Un tasto Spotify suona solo se il diffusore è collegato allo stesso account Spotify.");
        Add("L_SpeakerBackupSave",
            "Sauvegarder dans un fichier",
            "Save to a file",
            "In eine Datei sichern",
            "In een bestand opslaan",
            "Guardar en un archivo",
            "Salva in un file");
        Add("L_SpeakerBackupRestore",
            "Restaurer depuis un fichier…",
            "Restore from a file…",
            "Aus einer Datei wiederherstellen…",
            "Terugzetten uit een bestand…",
            "Restaurar desde un archivo…",
            "Ripristina da un file…");
        Add("L_SpeakerRestoreAsk",
            "Restaurer sur {0} la sauvegarde de {1} du {2} ({3} touches, {4} réveils) ? Les touches du fichier remplacent celles de {0} (les autres ne changent pas). Les réveils de {0} sont remplacés.",
            "Restore onto {0} the backup of {1} from {2} ({3} keys, {4} alarms)? The keys in the file replace those of {0} (the others stay). The alarms of {0} are replaced.",
            "Auf {0} die Sicherung von {1} vom {2} wiederherstellen ({3} Tasten, {4} Wecker)? Die Tasten der Datei ersetzen die von {0} (die anderen bleiben). Die Wecker von {0} werden ersetzt.",
            "Op {0} de back-up van {1} van {2} terugzetten ({3} toetsen, {4} wekkers)? De toetsen uit het bestand vervangen die van {0} (de andere blijven). De wekkers van {0} worden vervangen.",
            "¿Restaurar en {0} la copia de {1} del {2} ({3} teclas, {4} alarmas)? Las teclas del archivo sustituyen a las de {0} (las demás no cambian). Las alarmas de {0} se sustituyen.",
            "Ripristinare su {0} il backup di {1} del {2} ({3} tasti, {4} sveglie)? I tasti del file sostituiscono quelli di {0} (gli altri restano). Le sveglie di {0} vengono sostituite.");
        Add("L_SpeakerRestoreGo",
            "Restaurer",
            "Restore",
            "Wiederherstellen",
            "Terugzetten",
            "Restaurar",
            "Ripristina");
        Add("L_ShortcutStandbyShort",
            "Tout en veille",
            "All standby",
            "Alle Standby",
            "Alles stand-by",
            "Todo en espera",
            "Tutto standby");
        Add("L_ShortcutStandbyLong",
            "Tout mettre en veille",
            "All speakers to standby",
            "Alle in Standby",
            "Alles op stand-by zetten",
            "Poner todo en espera",
            "Metti tutto in standby");
        Add("L_ShortcutKeyShort",
            "Touche {0}",
            "Key {0}",
            "Taste {0}",
            "Toets {0}",
            "Tecla {0}",
            "Tasto {0}");
        Add("L_ShortcutKeyLong",
            "Lancer la touche {0}",
            "Play key {0}",
            "Taste {0} abspielen",
            "Toets {0} afspelen",
            "Poner la tecla {0}",
            "Avvia il tasto {0}");
        Add("S_ShortcutNoSpeaker",
            "Aucune enceinte joignable : choisis-en une dans Réglages.",
            "No speaker reachable: pick one in Settings.",
            "Kein Lautsprecher erreichbar: wähle einen in den Einstellungen.",
            "Geen speaker bereikbaar: kies er een in Instellingen.",
            "Ningún altavoz accesible: elige uno en Ajustes.",
            "Nessun diffusore raggiungibile: scegline uno in Impostazioni.");
        Add("S_ShortcutNoAgent",
            "Aucune enceinte avec l'agent STR : rien n'a été mis en veille.",
            "No speaker with the STR agent: nothing was put on standby.",
            "Kein Lautsprecher mit dem STR-Agenten: nichts wurde in Standby gesetzt.",
            "Geen speaker met de STR-agent: er is niets op stand-by gezet.",
            "Ningún altavoz con el agente STR: no se ha puesto nada en espera.",
            "Nessun diffusore con l'agente STR: nulla è stato messo in standby.");
        Add("S_ShortcutStandbyDone",
            "{0} enceinte(s) mise(s) en veille.",
            "{0} speaker(s) put on standby.",
            "{0} Lautsprecher in Standby gesetzt.",
            "{0} speaker(s) op stand-by gezet.",
            "{0} altavoz/altavoces en espera.",
            "{0} diffusore/i in standby.");
        Add("S_ShortcutStandbyPartly",
            "{0} sur {1} mise(s) en veille ; sans réponse : {2}.",
            "{0} of {1} put on standby; no answer: {2}.",
            "{0} von {1} in Standby; keine Antwort: {2}.",
            "{0} van {1} op stand-by; geen antwoord: {2}.",
            "{0} de {1} en espera; sin respuesta: {2}.",
            "{0} su {1} in standby; nessuna risposta: {2}.");
        Add("S_ShortcutStandbySkipped",
            "{0} enceinte(s) sans agent STR non touchée(s) : leur seule commande de mise en veille les allumerait si elles étaient éteintes.",
            "{0} speaker(s) without the STR agent left alone: their only standby command would switch them on if they were off.",
            "{0} Lautsprecher ohne STR-Agent nicht angetastet: ihr einziger Standby-Befehl würde sie einschalten, wenn sie aus wären.",
            "{0} speaker(s) zonder STR-agent niet aangeraakt: hun enige stand-byopdracht zou ze aanzetten als ze uit stonden.",
            "{0} altavoz/altavoces sin agente STR sin tocar: su única orden de espera los encendería si estuvieran apagados.",
            "{0} diffusore/i senza agente STR non toccato/i: il loro unico comando di standby li accenderebbe se fossero spenti.");
        Add("S_ShortcutKeyPlayed",
            "Touche {0} lancée sur {1}.",
            "Key {0} playing on {1}.",
            "Taste {0} spielt auf {1}.",
            "Toets {0} speelt op {1}.",
            "Tecla {0} sonando en {1}.",
            "Tasto {0} avviato su {1}.");
        Add("S_SpeakerBackupReading",
            "Lecture de l'enceinte…",
            "Reading the speaker…",
            "Lautsprecher wird gelesen…",
            "Speaker wordt gelezen…",
            "Leyendo el altavoz…",
            "Lettura del diffusore…");
        Add("S_SpeakerBackupSaved",
            "Sauvegarde de {0} prête : {1} touches, {2} réveils.",
            "Backup of {0} ready: {1} keys, {2} alarms.",
            "Sicherung von {0} bereit: {1} Tasten, {2} Wecker.",
            "Back-up van {0} klaar: {1} toetsen, {2} wekkers.",
            "Copia de {0} lista: {1} teclas, {2} alarmas.",
            "Backup di {0} pronto: {1} tasti, {2} sveglie.");
        Add("S_SpeakerBackupNotRecognized",
            "Ce fichier n'est pas une sauvegarde d'enceinte de SoundReborn Remote.",
            "This file is not a SoundReborn Remote speaker backup.",
            "Diese Datei ist keine Lautsprecher-Sicherung von SoundReborn Remote.",
            "Dit bestand is geen speaker-back-up van SoundReborn Remote.",
            "Este archivo no es una copia de altavoz de SoundReborn Remote.",
            "Questo file non è un backup di diffusore di SoundReborn Remote.");
        Add("S_SpeakerRestoreChanged",
            "L'enceinte sélectionnée a changé : rien n'a été restauré.",
            "The selected speaker changed: nothing was restored.",
            "Der ausgewählte Lautsprecher hat gewechselt: nichts wurde wiederhergestellt.",
            "De geselecteerde speaker is veranderd: er is niets teruggezet.",
            "El altavoz seleccionado ha cambiado: no se ha restaurado nada.",
            "Il diffusore selezionato è cambiato: non è stato ripristinato nulla.");
        Add("S_SpeakerRestoring",
            "Restauration sur {0}…",
            "Restoring onto {0}…",
            "Wiederherstellung auf {0}…",
            "Terugzetten op {0}…",
            "Restaurando en {0}…",
            "Ripristino su {0}…");
        Add("S_SpeakerRestored",
            "Restauré sur {0} :",
            "Restored onto {0}:",
            "Auf {0} wiederhergestellt:",
            "Teruggezet op {0}:",
            "Restaurado en {0}:",
            "Ripristinato su {0}:");
        Add("S_SpeakerRestoredPartly",
            "Restauré en partie sur {0} :",
            "Partly restored onto {0}:",
            "Teilweise auf {0} wiederhergestellt:",
            "Gedeeltelijk teruggezet op {0}:",
            "Restaurado en parte en {0}:",
            "Ripristinato in parte su {0}:");
        Add("S_SpeakerRestorePresets",
            "{0} touches",
            "{0} keys",
            "{0} Tasten",
            "{0} toetsen",
            "{0} teclas",
            "{0} tasti");
        Add("S_SpeakerRestorePresetsFailed",
            "Touches non restaurées : {0}",
            "Keys not restored: {0}",
            "Tasten nicht wiederhergestellt: {0}",
            "Toetsen niet teruggezet: {0}",
            "Teclas no restauradas: {0}",
            "Tasti non ripristinati: {0}");
        Add("S_SpeakerRestoreNoSecondRow",
            "{0} touches de la seconde rangée passées : cette enceinte ne l'a pas.",
            "{0} second-row keys skipped: this speaker does not have it.",
            "{0} Tasten der zweiten Reihe übersprungen: dieser Lautsprecher hat sie nicht.",
            "{0} toetsen van de tweede rij overgeslagen: deze speaker heeft die niet.",
            "{0} teclas de la segunda fila omitidas: este altavoz no la tiene.",
            "{0} tasti della seconda fila saltati: questo diffusore non ce l'ha.");
        Add("S_SpeakerRestoreAlarms",
            "{0} réveils",
            "{0} alarms",
            "{0} Wecker",
            "{0} wekkers",
            "{0} alarmas",
            "{0} sveglie");
        Add("S_SpeakerRestoreAlarmsFailed",
            "Réveils non restaurés : {0}",
            "Alarms not restored: {0}",
            "Wecker nicht wiederhergestellt: {0}",
            "Wekkers niet teruggezet: {0}",
            "Alarmas no restauradas: {0}",
            "Sveglie non ripristinate: {0}");
        Add("S_SpeakerRestoreStartVolume",
            "volume de départ {0}",
            "start volume {0}",
            "Startlautstärke {0}",
            "startvolume {0}",
            "volumen de inicio {0}",
            "volume di avvio {0}");
        Add("S_SpeakerRestoreStartVolumeOff",
            "volume de départ désactivé",
            "start volume off",
            "Startlautstärke aus",
            "startvolume uit",
            "volumen de inicio desactivado",
            "volume di avvio disattivato");
        Add("S_SpeakerRestoreStartVolumeFailed",
            "Volume de départ non restauré : {0}",
            "Start volume not restored: {0}",
            "Startlautstärke nicht wiederhergestellt: {0}",
            "Startvolume niet teruggezet: {0}",
            "Volumen de inicio no restaurado: {0}",
            "Volume di avvio non ripristinato: {0}");
        Add("S_SpeakerRestoreKeyActions",
            "touches de la télécommande",
            "remote keys",
            "Tasten der Fernbedienung",
            "toetsen van de afstandsbediening",
            "teclas del mando",
            "tasti del telecomando");
        Add("S_SpeakerRestoreKeyActionsFailed",
            "Touches de la télécommande non restaurées : {0}",
            "Remote keys not restored: {0}",
            "Tasten der Fernbedienung nicht wiederhergestellt: {0}",
            "Toetsen van de afstandsbediening niet teruggezet: {0}",
            "Teclas del mando no restauradas: {0}",
            "Tasti del telecomando non ripristinati: {0}");
        Add("S_SpeakerRestoreUnsupported",
            "l'agent de cette enceinte ne le gère pas",
            "the agent on this speaker does not support it",
            "der Agent dieses Lautsprechers unterstützt das nicht",
            "de agent van deze speaker ondersteunt dat niet",
            "el agente de este altavoz no lo admite",
            "l'agente di questo diffusore non lo supporta");
        Add("L_BackupShareTitle",
            "Sauvegarde des favoris SoundReborn Remote",
            "SoundReborn Remote favourites backup",
            "Sicherung der Favoriten von SoundReborn Remote",
            "Back-up van favorieten van SoundReborn Remote",
            "Copia de los favoritos de SoundReborn Remote",
            "Backup dei preferiti di SoundReborn Remote");
        Add("L_BackupPickTitle",
            "Choisir une sauvegarde des favoris",
            "Choose a favourites backup",
            "Eine Sicherung der Favoriten wählen",
            "Een back-up van favorieten kiezen",
            "Elegir una copia de los favoritos",
            "Scegli un backup dei preferiti");
        Add("S_BackupEmpty",
            "Rien à exporter : aucune présélection « Local » ni texte gardé.",
            "Nothing to export: no local preset and no kept text.",
            "Nichts zu exportieren: keine lokalen Presets und keine gespeicherten Texte.",
            "Niets te exporteren: geen lokale presets en geen bewaarde teksten.",
            "Nada que exportar: ninguna presintonía «Local» ni texto guardado.",
            "Niente da esportare: nessuna preselezione «Locale» né testo salvato.");
        Add("S_BackupExported",
            "Fichier prêt : {0} présélection(s) « Local », {1} texte(s).",
            "File ready: {0} local preset(s), {1} text(s).",
            "Datei bereit: {0} lokale(s) Preset(s), {1} Text(e).",
            "Bestand klaar: {0} lokale preset(s), {1} tekst(en).",
            "Archivo listo: {0} presintonía(s) «Local», {1} texto(s).",
            "File pronto: {0} preselezione/i «Locale», {1} testo/i.");
        Add("S_BackupImported",
            "Ajouté : {0} présélection(s) « Local », {1} texte(s).",
            "Added: {0} local preset(s), {1} text(s).",
            "Hinzugefügt: {0} lokale(s) Preset(s), {1} Text(e).",
            "Toegevoegd: {0} lokale preset(s), {1} tekst(en).",
            "Añadido: {0} presintonía(s) «Local», {1} texto(s).",
            "Aggiunto: {0} preselezione/i «Locale», {1} testo/i.");
        Add("S_BackupNothingNew",
            "Rien de nouveau : tout ce que contient le fichier est déjà là (ou les listes sont pleines).",
            "Nothing new: everything in the file is already here (or the lists are full).",
            "Nichts Neues: Alles aus der Datei ist schon da (oder die Listen sind voll).",
            "Niets nieuws: alles uit het bestand staat er al (of de lijsten zijn vol).",
            "Nada nuevo: todo lo del archivo ya está (o las listas están llenas).",
            "Niente di nuovo: tutto ciò che contiene il file c'è già (o gli elenchi sono pieni).");
        Add("S_BackupNotRecognized",
            "Ce fichier n'est pas une sauvegarde des favoris de SoundReborn Remote.",
            "This file is not a SoundReborn Remote favourites backup.",
            "Diese Datei ist keine Sicherung der Favoriten von SoundReborn Remote.",
            "Dit bestand is geen back-up van favorieten van SoundReborn Remote.",
            "Este archivo no es una copia de los favoritos de SoundReborn Remote.",
            "Questo file non è un backup dei preferiti di SoundReborn Remote.");

        // ---------------------------------------------------------- Message vocal : réessayer (1.9.0)
        Add("L_VoiceRetry",
            "Réessayer",
            "Try again",
            "Erneut versuchen",
            "Opnieuw proberen",
            "Reintentar",
            "Riprova");
        Add("S_VoiceTextFetchFailed",
            "L'enceinte n'a pas obtenu la voix de Google ({0}). Vérifie qu'elle a accès à internet, puis réessaie.",
            "The speaker could not get Google's voice ({0}). Check that it has internet access, then try again.",
            "Der Lautsprecher hat die Google-Stimme nicht erhalten ({0}). Prüfe, ob er Internetzugang hat, und versuche es erneut.",
            "De luidspreker kreeg de stem van Google niet ({0}). Controleer of hij internettoegang heeft en probeer opnieuw.",
            "El altavoz no obtuvo la voz de Google ({0}). Comprueba que tiene acceso a internet y vuelve a intentarlo.",
            "Il diffusore non ha ottenuto la voce di Google ({0}). Verifica che abbia accesso a internet, poi riprova.");
        Add("S_VoiceTextFetchFailedPlain",
            "L'enceinte n'a pas obtenu la voix de Google. Vérifie qu'elle a accès à internet, puis réessaie.",
            "The speaker could not get Google's voice. Check that it has internet access, then try again.",
            "Der Lautsprecher hat die Google-Stimme nicht erhalten. Prüfe, ob er Internetzugang hat, und versuche es erneut.",
            "De luidspreker kreeg de stem van Google niet. Controleer of hij internettoegang heeft en probeer opnieuw.",
            "El altavoz no obtuvo la voz de Google. Comprueba que tiene acceso a internet y vuelve a intentarlo.",
            "Il diffusore non ha ottenuto la voce di Google. Verifica che abbia accesso a internet, poi riprova.");
        Add("S_VoiceClipFetchFailed",
            "L'enceinte n'a pas pu chercher le message sur le téléphone ({0}). Réessaie.",
            "The speaker could not fetch the message from the phone ({0}). Try again.",
            "Der Lautsprecher konnte die Nachricht nicht vom Telefon abholen ({0}). Versuche es erneut.",
            "De luidspreker kon het bericht niet van de telefoon ophalen ({0}). Probeer opnieuw.",
            "El altavoz no pudo recoger el mensaje del teléfono ({0}). Vuelve a intentarlo.",
            "Il diffusore non ha potuto prelevare il messaggio dal telefono ({0}). Riprova.");
        Add("S_VoiceClipFetchFailedPlain",
            "L'enceinte n'a pas pu chercher le message sur le téléphone. Réessaie.",
            "The speaker could not fetch the message from the phone. Try again.",
            "Der Lautsprecher konnte die Nachricht nicht vom Telefon abholen. Versuche es erneut.",
            "De luidspreker kon het bericht niet van de telefoon ophalen. Probeer opnieuw.",
            "El altavoz no pudo recoger el mensaje del teléfono. Vuelve a intentarlo.",
            "Il diffusore non ha potuto prelevare il messaggio dal telefono. Riprova.");
    }
}
