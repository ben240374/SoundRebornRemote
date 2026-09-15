using System.Globalization;

namespace SoundRebornRemote.App.Services;

public enum AppLanguage
{
    French,
    English,
    German,
    Dutch,
    Spanish,
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
    };

    /// <summary>Nom de la langue dans la langue elle-même — on ne le traduit pas.</summary>
    public static string NameOf(AppLanguage language) => language switch
    {
        AppLanguage.French => "Français",
        AppLanguage.English => "English",
        AppLanguage.German => "Deutsch",
        AppLanguage.Dutch => "Nederlands",
        AppLanguage.Spanish => "Español",
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

            return values[0];
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
        // et un utilisateur italien ou polonais comprendra mieux l'anglais que le
        // français. Dès qu'une langue est choisie dans les réglages, elle prime.
        return CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.ToLowerInvariant() switch
        {
            "fr" => AppLanguage.French,
            "de" => AppLanguage.German,
            "nl" => AppLanguage.Dutch,
            "es" => AppLanguage.Spanish,
            _ => AppLanguage.English,
        };
    }

    private static void Add(string key, string fr, string en, string de, string nl, string es)
        => Table[key] = new[] { fr, en, de, nl, es };

    private static void Fill()
    {
        // ---------------------------------------------------------- Onglets
        Add("L_TabPlayer", "Lecture", "Now playing", "Wiedergabe", "Afspelen", "Reproducción");
        // Libellés courts : la barre d'onglets d'Android tronque au-delà.
        Add("L_TabPresets", "Présélections", "Presets", "Presets", "Presets", "Presintonías");
        Add("L_TabSources", "Sources", "Sources", "Quellen", "Bronnen", "Fuentes");
        Add("L_TabMultiroom", "Groupes", "Groups", "Gruppen", "Groepen", "Grupos");
        Add("L_TabSettings", "Réglages", "Settings", "Einstellungen", "Instellingen", "Ajustes");

        // ---------------------------------------------------------- Page Lecture
        Add("L_Speaker", "ENCEINTE", "SPEAKER", "LAUTSPRECHER", "SPEAKER", "ALTAVOZ");
        Add("L_Live", "direct", "live", "live", "live", "en vivo");
        Add("L_Polling", "sondage", "polling", "Abfrage", "polling", "sondeo");
        Add("L_Volume", "VOLUME", "VOLUME", "LAUTSTÄRKE", "VOLUME", "VOLUMEN");
        Add("L_Bass", "GRAVES", "BASS", "BÄSSE", "LAGE TONEN", "GRAVES");
        Add("L_BassNote",
            "L'API SoundTouch n'expose pas de réglage d'aigus — seuls les graves sont pilotables.",
            "The SoundTouch API exposes no treble control — only bass can be adjusted.",
            "Die SoundTouch-API bietet keine Höhenregelung — nur die Bässe lassen sich einstellen.",
            "De SoundTouch-API kent geen regeling voor hoge tonen — alleen de lage tonen zijn instelbaar.",
            "La API de SoundTouch no ofrece control de agudos — solo se pueden ajustar los graves.");
        Add("L_Presets", "PRÉSÉLECTIONS", "PRESETS", "PRESETS", "PRESETS", "PRESINTONÍAS");
        Add("L_Refresh", "Rafraîchir", "Refresh", "Aktualisieren", "Vernieuwen", "Actualizar");
        Add("L_PowerButton", "Veille / Allumer", "Standby / Power on", "Standby / Einschalten", "Stand-by / Aanzetten", "Reposo / Encender");
        Add("L_NoSpeakerTitle", "Aucune enceinte", "No speaker", "Kein Lautsprecher", "Geen speaker", "Sin altavoz");
        Add("L_NoSpeakerDetail",
            "Choisis une enceinte dans Réglages",
            "Pick a speaker in Settings",
            "Wähle einen Lautsprecher unter Einstellungen",
            "Kies een speaker bij Instellingen",
            "Elige un altavoz en Ajustes");
        Add("L_Standby", "En veille", "Standby", "Standby", "Stand-by", "En reposo");
        Add("L_NothingPlaying", "Rien en lecture", "Nothing playing", "Nichts in Wiedergabe", "Niets aan het afspelen", "Nada en reproducción");

        // ---------------------------------------------------------- Sources (noms affichés)
        Add("L_SrcRadio", "Radio Internet", "Internet radio", "Internetradio", "Internetradio", "Radio por Internet");
        Add("L_SrcLibrary", "Bibliothèque réseau", "Network library", "Netzwerkbibliothek", "Netwerkbibliotheek", "Biblioteca de red");
        Add("L_SrcAux", "Entrée AUX", "AUX input", "AUX-Eingang", "AUX-ingang", "Entrada AUX");
        Add("L_SrcNotification", "Annonce", "Announcement", "Ansage", "Melding", "Aviso");

        // ---------------------------------------------------------- Page Présélections
        Add("L_PresetsHint",
            "Touche une tuile pour lancer la présélection. Pour en enregistrer une, garde la touche appuyée sur l'enceinte ou passe par l'application STR du bureau.",
            "Tap a tile to start a preset. To store one, hold the key on the speaker itself or use the STR desktop app.",
            "Tippe auf eine Kachel, um ein Preset zu starten. Zum Speichern die Taste am Lautsprecher gedrückt halten oder die STR-Desktop-App verwenden.",
            "Tik op een tegel om een preset te starten. Opslaan doe je door de toets op de speaker ingedrukt te houden of via de STR-desktopapp.",
            "Toca una tarjeta para lanzar una presintonía. Para guardarla, mantén pulsada la tecla en el altavoz o usa la app STR de escritorio.");
        Add("L_PresetEmpty", "vide", "empty", "leer", "leeg", "vacía");
        Add("L_PresetNumber", "Présélection {0}", "Preset {0}", "Preset {0}", "Preset {0}", "Presintonía {0}");

        // ---------------------------------------------------------- Page Sources
        Add("L_Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth", "Bluetooth");
        Add("L_Aux", "AUX", "AUX", "AUX", "AUX", "AUX");
        Add("L_StandbyButton", "Veille", "Standby", "Standby", "Stand-by", "Reposo");
        Add("L_Spotify", "SPOTIFY", "SPOTIFY", "SPOTIFY", "SPOTIFY", "SPOTIFY");
        Add("L_SpotifyHint",
            "Sélectionne l'enceinte dans l'application Spotify (Spotify Connect). Elle apparaît dès que l'agent STR tourne.",
            "Pick the speaker inside the Spotify app (Spotify Connect). It shows up as soon as the STR agent is running.",
            "Wähle den Lautsprecher in der Spotify-App (Spotify Connect). Er erscheint, sobald der STR-Agent läuft.",
            "Kies de speaker in de Spotify-app (Spotify Connect). Hij verschijnt zodra de STR-agent draait.",
            "Selecciona el altavoz en la aplicación Spotify (Spotify Connect). Aparece en cuanto el agente STR está activo.");
        Add("L_SpotifyNoAgent",
            "Aucun agent STR détecté sur cette enceinte : Spotify Connect ne fonctionnera pas, le cloud Bose qui l'assurait est arrêté.",
            "No STR agent on this speaker: Spotify Connect will not work, the Bose cloud that provided it is shut down.",
            "Kein STR-Agent auf diesem Lautsprecher: Spotify Connect funktioniert nicht, die Bose-Cloud dafür ist abgeschaltet.",
            "Geen STR-agent op deze speaker: Spotify Connect werkt niet, de Bose-cloud die dit verzorgde is gestopt.",
            "Sin agente STR en este altavoz: Spotify Connect no funcionará, la nube de Bose que lo proporcionaba está apagada.");
        Add("L_SpeakerSources", "SOURCES DE L'ENCEINTE", "SPEAKER SOURCES", "QUELLEN DES LAUTSPRECHERS", "BRONNEN VAN DE SPEAKER", "FUENTES DEL ALTAVOZ");
        Add("L_NoSources",
            "Aucune source listée. Rafraîchis, ou réveille l'enceinte.",
            "No sources listed. Refresh, or wake the speaker.",
            "Keine Quellen gelistet. Aktualisiere, oder wecke den Lautsprecher.",
            "Geen bronnen gevonden. Vernieuw, of wek de speaker.",
            "No hay fuentes. Actualiza, o despierta el altavoz.");
        Add("L_PlayStream", "JOUER UN FLUX", "PLAY A STREAM", "STREAM ABSPIELEN", "STREAM AFSPELEN", "REPRODUCIR UN FLUJO");
        Add("L_PlayStreamButton", "Lancer", "Play", "Abspielen", "Afspelen", "Reproducir");
        Add("L_PlayStreamNote",
            "L'agent convertit une adresse HTTPS en HTTP : le moteur UPnP de l'enceinte refuse TLS.",
            "The agent rewrites an HTTPS address to HTTP: the speaker's UPnP renderer rejects TLS.",
            "Der Agent wandelt eine HTTPS-Adresse in HTTP um: der UPnP-Renderer des Lautsprechers lehnt TLS ab.",
            "De agent zet een HTTPS-adres om naar HTTP: de UPnP-renderer van de speaker weigert TLS.",
            "El agente convierte una dirección HTTPS en HTTP: el motor UPnP del altavoz rechaza TLS.");
        Add("L_Recent", "ÉCOUTÉ RÉCEMMENT", "RECENTLY PLAYED", "ZULETZT GEHÖRT", "ONLANGS AFGESPEELD", "ESCUCHADO RECIENTEMENTE");
        Add("L_NoRecent",
            "Rien dans l'historique de l'agent.",
            "Nothing in the agent's history.",
            "Nichts im Verlauf des Agenten.",
            "Niets in de geschiedenis van de agent.",
            "Nada en el historial del agente.");

        // ---------------------------------------------------------- Page Multi-pièces
        Add("L_Master", "ENCEINTE MAÎTRE", "MASTER SPEAKER", "HAUPTLAUTSPRECHER", "HOOFDSPEAKER", "ALTAVOZ PRINCIPAL");
        Add("L_MasterNote",
            "C'est elle qui diffuse le flux ; les autres le suivent. Change d'enceinte active pour changer de maître.",
            "It broadcasts the stream; the others follow it. Switch the active speaker to change the master.",
            "Er sendet den Stream; die anderen folgen ihm. Wechsle den aktiven Lautsprecher, um den Hauptlautsprecher zu ändern.",
            "Deze speaker zendt de stream uit; de andere volgen. Wissel van actieve speaker om de hoofdspeaker te wijzigen.",
            "Es el que difunde el flujo; los demás lo siguen. Cambia de altavoz activo para cambiar de principal.");
        Add("L_NotMasterNote",
            "L'enceinte pilotée suit ce groupe, elle ne le diffuse pas. Les commandes de groupe s'adressent au maître ci-dessus.",
            "The controlled speaker follows this group, it does not broadcast it. Group commands are addressed to the master above.",
            "Der gesteuerte Lautsprecher folgt dieser Gruppe, er sendet sie nicht. Gruppenbefehle gehen an den Master oben.",
            "De bediende speaker volgt deze groep, hij zendt ze niet uit. Groepsopdrachten gaan naar de master hierboven.",
            "El altavoz controlado sigue este grupo, no lo emite. Los mandos de grupo se dirigen al maestro de arriba.");
        Add("L_SpeakersToGroup", "ENCEINTES À GROUPER", "SPEAKERS TO GROUP", "ZU GRUPPIERENDE LAUTSPRECHER", "SPEAKERS OM TE GROEPEREN", "ALTAVOCES A AGRUPAR");
        Add("L_NoOtherSpeakers",
            "Aucune autre enceinte connue. Lance un balayage dans Réglages.",
            "No other speaker known. Run a scan in Settings.",
            "Kein weiterer Lautsprecher bekannt. Starte einen Suchlauf unter Einstellungen.",
            "Geen andere speaker bekend. Start een scan bij Instellingen.",
            "No se conoce ningún otro altavoz. Lanza un escaneo en Ajustes.");
        Add("L_PermanentGroup",
            "Groupe permanent (reformé à la prochaine lecture)",
            "Permanent group (re-formed on the next play)",
            "Dauerhafte Gruppe (wird bei der nächsten Wiedergabe neu gebildet)",
            "Permanente groep (wordt bij de volgende weergave hersteld)",
            "Grupo permanente (se recrea en la próxima reproducción)");
        Add("L_FormGroup", "Grouper", "Group", "Gruppieren", "Groeperen", "Agrupar");
        Add("L_Dissolve", "Dissoudre", "Dissolve", "Auflösen", "Ontbinden", "Disolver");
        Add("L_GroupVolume", "VOLUME DU GROUPE", "GROUP VOLUME", "GRUPPENLAUTSTÄRKE", "GROEPSVOLUME", "VOLUMEN DEL GRUPO");
        Add("L_ApplyToGroup", "Appliquer à tout le groupe", "Apply to the whole group", "Auf die ganze Gruppe anwenden", "Op de hele groep toepassen", "Aplicar a todo el grupo");
        Add("L_NoActiveGroup", "Pas de groupe actif.", "No active group.", "Keine aktive Gruppe.", "Geen actieve groep.", "Sin grupo activo.");

        // ---------------------------------------------------------- Page Réglages
        // « Sélectionnée » et non « active » : dans un groupe, l'enceinte désignée
        // n'est pas celle qui reçoit les commandes.
        Add("L_ActiveSpeaker", "ENCEINTE SÉLECTIONNÉE", "SELECTED SPEAKER", "AUSGEWÄHLTER LAUTSPRECHER", "GEKOZEN SPEAKER", "ALTAVOZ SELECCIONADO");
        Add("L_CommandsGoTo",
            "Elle suit un groupe : les commandes vont à {0}, qui diffuse.",
            "It follows a group: commands go to {0}, which does the playing.",
            "Er folgt einer Gruppe: Befehle gehen an {0}, der wiedergibt.",
            "Hij volgt een groep: opdrachten gaan naar {0}, die afspeelt.",
            "Sigue a un grupo: los comandos van a {0}, que reproduce.");
        Add("L_NoSpeakerSelected", "Aucune enceinte sélectionnée", "No speaker selected", "Kein Lautsprecher ausgewählt", "Geen speaker geselecteerd", "Ningún altavoz seleccionado");
        Add("L_Search", "RECHERCHE", "SEARCH", "SUCHE", "ZOEKEN", "BÚSQUEDA");
        Add("L_ScanButton", "Balayer le réseau local", "Scan the local network", "Lokales Netzwerk durchsuchen", "Lokaal netwerk scannen", "Escanear la red local");
        Add("L_ScanNote",
            "Le balayage interroge chaque adresse du sous-réseau sur le port 8090. Le téléphone doit être sur le même Wi-Fi que les enceintes.",
            "The scan probes every address of the subnet on port 8090. The phone must be on the same Wi-Fi as the speakers.",
            "Der Suchlauf prüft jede Adresse des Subnetzes auf Port 8090. Das Telefon muss im selben WLAN sein wie die Lautsprecher.",
            "De scan bevraagt elk adres van het subnet op poort 8090. De telefoon moet op dezelfde wifi zitten als de speakers.",
            "El escaneo consulta cada dirección de la subred en el puerto 8090. El teléfono debe estar en la misma wifi que los altavoces.");
        Add("L_Add", "Ajouter", "Add", "Hinzufügen", "Toevoegen", "Añadir");
        Add("L_KnownSpeakers", "ENCEINTES CONNUES", "KNOWN SPEAKERS", "BEKANNTE LAUTSPRECHER", "BEKENDE SPEAKERS", "ALTAVOCES CONOCIDOS");
        Add("L_NoKnownSpeakers",
            "Aucune enceinte connue. Lance un balayage.",
            "No speaker known yet. Run a scan.",
            "Noch kein Lautsprecher bekannt. Starte einen Suchlauf.",
            "Nog geen speaker bekend. Start een scan.",
            "Aún no hay altavoces. Lanza un escaneo.");
        Add("L_Use", "Utiliser", "Use", "Verwenden", "Gebruiken", "Usar");

        // ------------------------------------------- Enceintes sans agent STR
        Add("L_ReadyForStr",
            "Sans STR — prête pour l'installation",
            "No STR — ready for installation",
            "Ohne STR — bereit zur Installation",
            "Zonder STR — klaar voor installatie",
            "Sin STR — lista para instalar");
        Add("L_StrMissingTitle", "AGENT STR MANQUANT", "STR AGENT MISSING", "STR-AGENT FEHLT", "STR-AGENT ONTBREEKT", "FALTA EL AGENTE STR");
        Add("L_StrMissingOne",
            "Une enceinte connue n'a pas l'agent STR.",
            "One known speaker has no STR agent.",
            "Ein bekannter Lautsprecher hat keinen STR-Agenten.",
            "Eén bekende speaker heeft geen STR-agent.",
            "Un altavoz conocido no tiene el agente STR.");
        Add("L_StrMissingMany",
            "{0} enceintes connues n'ont pas l'agent STR.",
            "{0} known speakers have no STR agent.",
            "{0} bekannte Lautsprecher haben keinen STR-Agenten.",
            "{0} bekende speakers hebben geen STR-agent.",
            "{0} altavoces conocidos no tienen el agente STR.");
        Add("L_StrMissingBody",
            "Sans l'agent, seules les fonctions du firmware restent : volume, graves, touches déjà enregistrées. La recherche de stations, l'enregistrement des touches et le groupage fiable passent par STR.\n\nL'installation ne peut pas se faire depuis le téléphone : elle ouvre un accès système sur l'enceinte, y copie l'agent et la redémarre. Elle se fait depuis l'outil STR sur PC ou Mac, qui gère aussi les cas de récupération.",
            "Without the agent, only firmware features remain: volume, bass, presets already stored. Station search, saving presets and reliable grouping all go through STR.\n\nInstalling cannot be done from the phone: it opens a system access on the speaker, copies the agent onto it and reboots it. It is done with the STR tool on PC or Mac, which also handles recovery cases.",
            "Ohne den Agenten bleiben nur Firmware-Funktionen: Lautstärke, Bass, bereits gespeicherte Tasten. Sendersuche, Tastenbelegung und zuverlässige Gruppen laufen über STR.\n\nDie Installation ist vom Telefon aus nicht möglich: sie öffnet einen Systemzugang am Lautsprecher, kopiert den Agenten darauf und startet ihn neu. Das macht das STR-Werkzeug für PC oder Mac, das auch Wiederherstellungsfälle abdeckt.",
            "Zonder de agent blijven alleen firmwarefuncties over: volume, bass, al opgeslagen toetsen. Zenders zoeken, toetsen opslaan en betrouwbaar groeperen gaan via STR.\n\nInstalleren kan niet vanaf de telefoon: het opent een systeemtoegang op de speaker, kopieert de agent erop en herstart hem. Dat doet het STR-programma op pc of Mac, dat ook herstelgevallen aankan.",
            "Sin el agente solo quedan las funciones del firmware: volumen, graves, teclas ya guardadas. Buscar emisoras, guardar teclas y agrupar de forma fiable pasan por STR.\n\nLa instalación no puede hacerse desde el teléfono: abre un acceso de sistema en el altavoz, copia el agente y lo reinicia. Se hace con la herramienta STR para PC o Mac, que también cubre los casos de recuperación.");
        Add("L_StrMissingOpen",
            "Voir comment installer STR",
            "See how to install STR",
            "So installiert man STR",
            "Zie hoe je STR installeert",
            "Ver cómo instalar STR");
        Add("L_Diagnostics", "DIAGNOSTIC", "DIAGNOSTICS", "DIAGNOSE", "DIAGNOSE", "DIAGNÓSTICO");
        Add("L_DiagnoseButton", "Interroger l'enceinte active", "Query the active speaker", "Aktiven Lautsprecher abfragen", "Actieve speaker bevragen", "Consultar el altavoz activo");
        Add("L_Language", "LANGUE", "LANGUAGE", "SPRACHE", "TAAL", "IDIOMA");
        Add("L_LanguageNote",
            "Le changement est immédiat et retenu pour les prochains lancements.",
            "The change takes effect at once and is kept for future launches.",
            "Die Änderung wirkt sofort und bleibt für künftige Starts erhalten.",
            "De wijziging werkt meteen en blijft bewaard voor volgende keren.",
            "El cambio se aplica de inmediato y se conserva para próximos inicios.");

        // ---------------------------------------------------------- Messages d'état
        Add("S_NoSpeaker",
            "Aucune enceinte sélectionnée. Va dans Réglages pour en chercher une.",
            "No speaker selected. Go to Settings to find one.",
            "Kein Lautsprecher ausgewählt. Suche einen unter Einstellungen.",
            "Geen speaker geselecteerd. Zoek er een bij Instellingen.",
            "Ningún altavoz seleccionado. Busca uno en Ajustes.");
        Add("S_Timeout",
            "L'enceinte n'a pas répondu à temps.",
            "The speaker did not answer in time.",
            "Der Lautsprecher hat nicht rechtzeitig geantwortet.",
            "De speaker antwoordde niet op tijd.",
            "El altavoz no respondió a tiempo.");
        Add("S_PowerOnRefused",
            "L'enceinte n'a pas répondu à l'allumage. Si elle fait partie d'un groupe, elle suit l'enceinte maître : allume plutôt celle-ci, ou dissous le groupe.",
            "The speaker did not come on. If it belongs to a group it follows the master speaker: turn that one on instead, or dissolve the group.",
            "Der Lautsprecher ist nicht angegangen. Gehört er zu einer Gruppe, folgt er dem Master: schalte stattdessen diesen ein oder löse die Gruppe auf.",
            "De speaker ging niet aan. Hoort hij bij een groep, dan volgt hij de masterspeaker: zet die aan of ontbind de groep.",
            "El altavoz no se ha encendido. Si pertenece a un grupo, sigue al altavoz maestro: enciende ese, o disuelve el grupo.");
        Add("S_Unreachable",
            "Enceinte injoignable. Même réseau Wi-Fi ?",
            "Speaker unreachable. Same Wi-Fi network?",
            "Lautsprecher nicht erreichbar. Gleiches WLAN?",
            "Speaker onbereikbaar. Zelfde wifi-netwerk?",
            "Altavoz inaccesible. ¿Misma red wifi?");
        Add("S_Scanning", "Balayage du réseau local…", "Scanning the local network…", "Lokales Netzwerk wird durchsucht…", "Lokaal netwerk scannen…", "Escaneando la red local…");
        Add("S_NoneFound",
            "Aucune enceinte trouvée. Vérifie que le téléphone est sur le même Wi-Fi (pas en 4G, pas sur un réseau invité).",
            "No speaker found. Check the phone is on the same Wi-Fi (not on mobile data, not on a guest network).",
            "Kein Lautsprecher gefunden. Prüfe, ob das Telefon im selben WLAN ist (nicht mobil, nicht im Gastnetz).",
            "Geen speaker gevonden. Controleer of de telefoon op dezelfde wifi zit (niet op mobiel, niet op een gastnetwerk).",
            "No se encontró ningún altavoz. Comprueba que el teléfono esté en la misma wifi (no en datos móviles ni en red de invitados).");
        Add("S_OneFound", "1 enceinte trouvée.", "1 speaker found.", "1 Lautsprecher gefunden.", "1 speaker gevonden.", "1 altavoz encontrado.");
        Add("S_ManyFound", "{0} enceintes trouvées.", "{0} speakers found.", "{0} Lautsprecher gefunden.", "{0} speakers gevonden.", "{0} altavoces encontrados.");
        Add("S_EnterIp",
            "Saisis l'adresse IP de l'enceinte, par exemple 192.168.1.42.",
            "Enter the speaker's IP address, for example 192.168.1.42.",
            "Gib die IP-Adresse des Lautsprechers ein, zum Beispiel 192.168.1.42.",
            "Voer het IP-adres van de speaker in, bijvoorbeeld 192.168.1.42.",
            "Escribe la dirección IP del altavoz, por ejemplo 192.168.1.42.");
        Add("S_NothingAt",
            "Rien ne répond à l'API SoundTouch sur {0}:8090.",
            "Nothing answers the SoundTouch API at {0}:8090.",
            "Auf {0}:8090 antwortet keine SoundTouch-API.",
            "Niets antwoordt op de SoundTouch-API op {0}:8090.",
            "Nada responde a la API SoundTouch en {0}:8090.");
        Add("S_Added", "{0} ajoutée.", "{0} added.", "{0} hinzugefügt.", "{0} toegevoegd.", "{0} añadido.");
        Add("S_Connected", "Connecté à {0}.", "Connected to {0}.", "Verbunden mit {0}.", "Verbonden met {0}.", "Conectado a {0}.");
        Add("S_Removed", "{0} retirée de la liste.", "{0} removed from the list.", "{0} aus der Liste entfernt.", "{0} uit de lijst verwijderd.", "{0} eliminado de la lista.");
        Add("S_SelectFirst",
            "Sélectionne d'abord une enceinte.",
            "Select a speaker first.",
            "Wähle zuerst einen Lautsprecher.",
            "Selecteer eerst een speaker.",
            "Selecciona primero un altavoz.");
        Add("S_PresetStarted", "{0} lancée", "{0} started", "{0} gestartet", "{0} gestart", "{0} iniciada");
        Add("S_NoPresets",
            "Aucune présélection enregistrée.",
            "No preset stored.",
            "Kein Preset gespeichert.",
            "Geen preset opgeslagen.",
            "Ninguna presintonía guardada.");
        Add("S_SourceSet", "Source : {0}", "Source: {0}", "Quelle: {0}", "Bron: {0}", "Fuente: {0}");
        Add("S_StandbySet", "Mise en veille", "Going to standby", "Wird in Standby versetzt", "Naar stand-by", "Poniendo en reposo");
        Add("S_StreamStarted", "Flux lancé", "Stream started", "Stream gestartet", "Stream gestart", "Flujo iniciado");
        Add("S_EnterStream",
            "Saisis l'adresse d'un flux.",
            "Enter a stream address.",
            "Gib eine Stream-Adresse ein.",
            "Voer een streamadres in.",
            "Escribe la dirección de un flujo.");
        Add("S_NeedAgentUrl",
            "Jouer une URL demande l'agent STR (l'ancien point /speaker du firmware est mort).",
            "Playing a URL needs the STR agent (the firmware's old /speaker endpoint is dead).",
            "Eine URL abzuspielen erfordert den STR-Agenten (der alte /speaker-Endpunkt der Firmware ist tot).",
            "Een URL afspelen vereist de STR-agent (het oude /speaker-eindpunt van de firmware is dood).",
            "Reproducir una URL requiere el agente STR (el antiguo endpoint /speaker del firmware está muerto).");
        Add("S_NeedAgentReplay",
            "Rejouer un flux demande l'agent STR.",
            "Replaying a stream needs the STR agent.",
            "Einen Stream erneut abzuspielen erfordert den STR-Agenten.",
            "Een stream opnieuw afspelen vereist de STR-agent.",
            "Volver a reproducir un flujo requiere el agente STR.");
        Add("S_NotReplayable",
            "Cette entrée ne porte pas d'adresse rejouable.",
            "This entry carries no replayable address.",
            "Dieser Eintrag enthält keine erneut abspielbare Adresse.",
            "Dit item bevat geen herspeelbaar adres.",
            "Esta entrada no tiene una dirección reproducible.");
        Add("S_Replaying", "Relance : {0}", "Replaying: {0}", "Erneut: {0}", "Opnieuw: {0}", "Repitiendo: {0}");
        Add("S_CheckOne",
            "Coche au moins une enceinte à ajouter au groupe.",
            "Tick at least one speaker to add to the group.",
            "Wähle mindestens einen Lautsprecher für die Gruppe aus.",
            "Vink minstens één speaker aan om toe te voegen.",
            "Marca al menos un altavoz para añadir al grupo.");
        Add("S_OnlyOneSpeaker",
            "Une seule enceinte connue. Lance un balayage dans Réglages pour en trouver d'autres.",
            "Only one speaker known. Run a scan in Settings to find others.",
            "Nur ein Lautsprecher bekannt. Starte einen Suchlauf unter Einstellungen.",
            "Slechts één speaker bekend. Start een scan bij Instellingen.",
            "Solo se conoce un altavoz. Lanza un escaneo en Ajustes.");
        Add("S_GroupFormed", "Groupe formé", "Group formed", "Gruppe gebildet", "Groep gevormd", "Grupo formado");
        Add("S_GroupDissolved", "Groupe dissous", "Group dissolved", "Gruppe aufgelöst", "Groep ontbonden", "Grupo disuelto");
        Add("S_GroupVolumeApplied", "Volume du groupe appliqué", "Group volume applied", "Gruppenlautstärke angewendet", "Groepsvolume toegepast", "Volumen de grupo aplicado");
        Add("S_NeedAgentGroupVolume",
            "Le volume de groupe demande l'agent STR.",
            "Group volume needs the STR agent.",
            "Die Gruppenlautstärke erfordert den STR-Agenten.",
            "Groepsvolume vereist de STR-agent.",
            "El volumen de grupo requiere el agente STR.");
        Add("S_MissingDeviceId",
            "Identifiant manquant pour {0}. Relance un balayage dans Réglages.",
            "Missing device id for {0}. Run a scan again in Settings.",
            "Fehlende Geräte-ID für {0}. Starte erneut einen Suchlauf unter Einstellungen.",
            "Ontbrekende apparaat-id voor {0}. Start opnieuw een scan bij Instellingen.",
            "Falta el identificador de {0}. Vuelve a lanzar un escaneo en Ajustes.");

        // ---------------------------------------------------------- Transport et thème
        Add("L_NowPlaying", "EN LECTURE", "NOW PLAYING", "WIEDERGABE", "SPEELT NU", "EN REPRODUCCIÓN");
        Add("L_Stop", "Arrêt", "Stop", "Stopp", "Stop", "Parar");
        Add("L_PowerOnAction", "Allumer", "Power on", "Einschalten", "Aanzetten", "Encender");
        Add("L_PowerOffAction", "Veille", "Standby", "Standby", "Stand-by", "Reposo");
        Add("L_GroupWith", "GROUPER AVEC", "GROUP WITH", "GRUPPIEREN MIT", "GROEPEREN MET", "AGRUPAR CON");
        Add("L_Theme", "THÈME", "THEME", "DESIGN", "THEMA", "TEMA");
        Add("L_ThemeSystem", "Système", "System", "System", "Systeem", "Sistema");
        Add("L_ThemeLight", "Clair", "Light", "Hell", "Licht", "Claro");
        Add("L_ThemeDark", "Foncé", "Dark", "Dunkel", "Donker", "Oscuro");
        Add("L_ThemeNote",
            "« Système » suit le réglage d'affichage du téléphone.",
            "\"System\" follows the phone's display setting.",
            "„System“ folgt der Anzeigeeinstellung des Telefons.",
            "\"Systeem\" volgt de weergave-instelling van de telefoon.",
            "«Sistema» sigue el ajuste de pantalla del teléfono.");
        Add("L_ClearArtCache", "Vider le cache des logos", "Clear the logo cache", "Logo-Cache leeren", "Logocache wissen", "Vaciar la caché de logos");
        Add("S_ArtCacheCleared",
            "Cache vidé. Les logos seront retéléchargés.",
            "Cache cleared. Logos will be downloaded again.",
            "Cache geleert. Logos werden erneut geladen.",
            "Cache gewist. Logo's worden opnieuw gedownload.",
            "Caché vaciada. Los logos se descargarán de nuevo.");

        // ---------------------------------------------------------- Recherche de radios
        Add("L_RadioSearch", "TROUVER UNE STATION", "FIND A STATION", "SENDER SUCHEN", "EEN ZENDER ZOEKEN", "BUSCAR UNA EMISORA");
        Add("L_RadioPlaceholder", "ex. Bel RTL, Nostalgie, jazz…", "e.g. BBC, NDR, jazz…", "z. B. NDR, WDR, Jazz…", "bijv. Radio 1, NPO, jazz…", "p. ej. Cadena SER, jazz…");
        Add("L_SearchButton", "Rechercher", "Search", "Suchen", "Zoeken", "Buscar");
        Add("L_TopStations", "Top liste", "Top list", "Top-Liste", "Toplijst", "Top lista");
        Add("L_Country", "Pays", "Country", "Land", "Land", "País");
        Add("L_LanguageFilter", "Langue", "Language", "Sprache", "Taal", "Idioma");
        Add("L_AllCountries", "Tous les pays", "All countries", "Alle Länder", "Alle landen", "Todos los países");
        Add("L_AllLanguages", "Toutes les langues", "All languages", "Alle Sprachen", "Alle talen", "Todos los idiomas");
        Add("L_BoseOnly",
            "Compatibles Bose uniquement",
            "Bose-compatible only",
            "Nur Bose-kompatible",
            "Alleen Bose-compatibel",
            "Solo compatibles con Bose");
        Add("L_RadioSource",
            "Annuaire radio-browser.info",
            "radio-browser.info directory",
            "Verzeichnis radio-browser.info",
            "Gids radio-browser.info",
            "Directorio radio-browser.info");
        Add("L_NoResults",
            "Aucune station. Élargis la recherche ou décoche le filtre.",
            "No station. Widen the search or clear the filter.",
            "Kein Sender. Suche erweitern oder Filter abwählen.",
            "Geen zender. Verbreed de zoekopdracht of zet het filter uit.",
            "Ninguna emisora. Amplía la búsqueda o quita el filtro.");
        Add("L_Listen", "Écouter maintenant", "Play now", "Jetzt abspielen", "Nu afspelen", "Escuchar ahora");
        Add("L_AssignToKey", "Affecter à la touche {0}", "Assign to key {0}", "Auf Taste {0} legen", "Toewijzen aan toets {0}", "Asignar a la tecla {0}");
        Add("L_Cancel", "Annuler", "Cancel", "Abbrechen", "Annuleren", "Cancelar");
        Add("L_ChooseAction", "Que faire de cette station ?", "What to do with this station?", "Was mit diesem Sender tun?", "Wat doen met deze zender?", "¿Qué hacer con esta emisora?");
        Add("S_Searching", "Recherche en cours…", "Searching…", "Suche läuft…", "Bezig met zoeken…", "Buscando…");
        Add("S_StationPlaying", "Lecture : {0}", "Playing: {0}", "Wiedergabe: {0}", "Speelt af: {0}", "Reproduciendo: {0}");
        Add("S_StationSaved", "{0} enregistrée sur la touche {1}", "{0} saved to key {1}", "{0} auf Taste {1} gespeichert", "{0} opgeslagen op toets {1}", "{0} guardada en la tecla {1}");
        Add("S_NeedAgentStation",
            "Chercher et enregistrer des stations demande l'agent STR.",
            "Finding and saving stations needs the STR agent.",
            "Sender suchen und speichern erfordert den STR-Agenten.",
            "Zenders zoeken en opslaan vereist de STR-agent.",
            "Buscar y guardar emisoras requiere el agente STR.");

        // ---------------------------------------------------------- Pays
        // Traduits à la main : sur Android, RegionInfo.DisplayName renvoie le nom
        // NATIF du pays (« België », « Lëtzebuerg »), pas sa traduction.
        Add("L_CtryBE", "Belgique", "Belgium", "Belgien", "België", "Bélgica");
        Add("L_CtryFR", "France", "France", "Frankreich", "Frankrijk", "Francia");
        Add("L_CtryNL", "Pays-Bas", "Netherlands", "Niederlande", "Nederland", "Países Bajos");
        Add("L_CtryDE", "Allemagne", "Germany", "Deutschland", "Duitsland", "Alemania");
        Add("L_CtryLU", "Luxembourg", "Luxembourg", "Luxemburg", "Luxemburg", "Luxemburgo");
        Add("L_CtryCH", "Suisse", "Switzerland", "Schweiz", "Zwitserland", "Suiza");
        Add("L_CtryGB", "Royaume-Uni", "United Kingdom", "Vereinigtes Königreich", "Verenigd Koninkrijk", "Reino Unido");
        Add("L_CtryES", "Espagne", "Spain", "Spanien", "Spanje", "España");
        Add("L_CtryIT", "Italie", "Italy", "Italien", "Italië", "Italia");
        Add("L_CtryPT", "Portugal", "Portugal", "Portugal", "Portugal", "Portugal");
        Add("L_CtryUS", "États-Unis", "United States", "Vereinigte Staaten", "Verenigde Staten", "Estados Unidos");
        Add("L_CtryCA", "Canada", "Canada", "Kanada", "Canada", "Canadá");

        // ---------------------------------------------------------- Affectation d'une station
        Add("L_ReplacePreset", "Remplacer {0}", "Replace {0}", "{0} ersetzen", "{0} vervangen", "Sustituir {0}");
        Add("L_AssignEmpty", "Affecter à cette touche", "Assign to this key", "Auf diese Taste legen", "Aan deze toets toewijzen", "Asignar a esta tecla");
        Add("L_Controls", "COMMANDES", "CONTROLS", "STEUERUNG", "BEDIENING", "CONTROLES");
        Add("L_RightNow", "EN CE MOMENT", "RIGHT NOW", "GERADE EBEN", "OP DIT MOMENT", "AHORA MISMO");
        Add("S_Refreshed", "Actualisé", "Refreshed", "Aktualisiert", "Bijgewerkt", "Actualizado");
        Add("L_ResultsHeader", "RÉSULTATS ({0})", "RESULTS ({0})", "ERGEBNISSE ({0})", "RESULTATEN ({0})", "RESULTADOS ({0})");
        Add("S_GroupedWith", "Groupé avec {0}", "Grouped with {0}", "Mit {0} gruppiert", "Gegroepeerd met {0}", "Agrupado con {0}");
        Add("S_Ungrouped", "{0} retirée du groupe", "{0} removed from the group", "{0} aus der Gruppe entfernt", "{0} uit de groep gehaald", "{0} retirado del grupo");
        Add("L_VolumeOfGroup",
            "Volume de l'ensemble du groupe",
            "Volume of the whole group",
            "Lautstärke der ganzen Gruppe",
            "Volume van de hele groep",
            "Volumen de todo el grupo");
        Add("L_LevelGroup",
            "Égaliser",
            "Level all",
            "Angleichen",
            "Gelijkzetten",
            "Igualar");
        Add("L_LevelGroupHint",
            "La touche « = » met toutes les enceintes à cette valeur.",
            "The “=” key sets every speaker to this value.",
            "Die Taste „=“ setzt alle Lautsprecher auf diesen Wert.",
            "De toets “=” zet alle speakers op deze waarde.",
            "La tecla «=» pone todos los altavoces en este valor.");
        Add("S_SlaveCannotForm",
            "Cette enceinte suit {0}. Sélectionne {0} pour modifier le groupe.",
            "This speaker follows {0}. Select {0} to change the group.",
            "Dieser Lautsprecher folgt {0}. Wähle {0}, um die Gruppe zu ändern.",
            "Deze speaker volgt {0}. Kies {0} om de groep te wijzigen.",
            "Este altavoz sigue a {0}. Selecciona {0} para cambiar el grupo.");
        Add("L_SlaveOfGroup",
            "Cette enceinte suit le groupe. Touche l'enceinte cochée pour l'en retirer.",
            "This speaker follows the group. Tap the ticked speaker to leave it.",
            "Dieser Lautsprecher folgt der Gruppe. Tippe auf den angehakten Lautsprecher, um ihn zu verlassen.",
            "Deze speaker volgt de groep. Tik op de aangevinkte speaker om hem te verlaten.",
            "Este altavoz sigue al grupo. Toca el altavoz marcado para salir.");
        Add("S_GroupLevelled",
            "Toutes les enceintes à {0} %",
            "All speakers at {0}%",
            "Alle Lautsprecher auf {0} %",
            "Alle speakers op {0} %",
            "Todos los altavoces al {0} %");
        Add("L_PerSpeaker", "PAR ENCEINTE", "PER SPEAKER", "PRO LAUTSPRECHER", "PER SPEAKER", "POR ALTAVOZ");
        Add("L_GroupedBadge", "groupé", "grouped", "gruppiert", "gegroepeerd", "agrupado");
        Add("L_TapToUngroup",
            "Touche une enceinte cochée pour la retirer du groupe.",
            "Tap a ticked speaker to remove it from the group.",
            "Tippe auf einen angehakten Lautsprecher, um ihn aus der Gruppe zu nehmen.",
            "Tik op een aangevinkte speaker om hem uit de groep te halen.",
            "Toca un altavoz marcado para sacarlo del grupo.");

        // ---------------------------------------------------------- À propos
        Add("L_About", "À PROPOS", "ABOUT", "ÜBER", "OVER", "ACERCA DE");
        Add("L_AppTagline",
            "Télécommande locale pour enceintes Bose SoundTouch.",
            "Local remote for Bose SoundTouch speakers.",
            "Lokale Fernbedienung für Bose-SoundTouch-Lautsprecher.",
            "Lokale afstandsbediening voor Bose SoundTouch-speakers.",
            "Mando local para altavoces Bose SoundTouch.");
        Add("L_ThanksTitle", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn", "STR — SoundTouch Reborn");
        Add("L_ThanksBody",
            "Quand Bose a coupé son cloud le 6 mai 2026, les enceintes SoundTouch perdaient l'essentiel de leur usage. Jens Roggenfelder (JRpersonal) a écrit STR, l'agent qui tourne sur l'enceinte elle-même et lui rend tout ce que le cloud assurait. Cette application ne fait que lui parler : sans son travail, elle n'aurait rien à piloter.",
            "When Bose shut its cloud down on 6 May 2026, SoundTouch speakers lost most of what made them useful. Jens Roggenfelder (JRpersonal) wrote STR, the agent that runs on the speaker itself and gives back everything the cloud used to provide. This app only talks to it: without his work, it would have nothing to control.",
            "Als Bose am 6. Mai 2026 seine Cloud abschaltete, verloren die SoundTouch-Lautsprecher fast ihren gesamten Nutzen. Jens Roggenfelder (JRpersonal) schrieb STR, den Agenten, der auf dem Lautsprecher selbst läuft und zurückgibt, was die Cloud leistete. Diese App spricht nur mit ihm: ohne seine Arbeit hätte sie nichts zu steuern.",
            "Toen Bose op 6 mei 2026 zijn cloud uitzette, verloren de SoundTouch-speakers vrijwel hun hele nut. Jens Roggenfelder (JRpersonal) schreef STR, de agent die op de speaker zelf draait en teruggeeft wat de cloud deed. Deze app praat er alleen mee: zonder zijn werk zou ze niets te besturen hebben.",
            "Cuando Bose apagó su nube el 6 de mayo de 2026, los altavoces SoundTouch perdieron casi toda su utilidad. Jens Roggenfelder (JRpersonal) escribió STR, el agente que corre en el propio altavoz y devuelve lo que la nube aportaba. Esta aplicación solo habla con él: sin su trabajo, no tendría nada que controlar.");
        Add("L_ThanksLicense",
            "STR est publié sous licence MIT.",
            "STR is released under the MIT licence.",
            "STR steht unter der MIT-Lizenz.",
            "STR valt onder de MIT-licentie.",
            "STR se publica bajo licencia MIT.");
        Add("L_OpenWebsite", "Site du projet STR", "STR project website", "Website des STR-Projekts", "Website van het STR-project", "Sitio del proyecto STR");
        Add("L_OpenSource", "Code source", "Source code", "Quellcode", "Broncode", "Código fuente");
        Add("L_RadioCredit",
            "Annuaire des stations : radio-browser.info, base ouverte tenue par des bénévoles.",
            "Station directory: radio-browser.info, an open database kept by volunteers.",
            "Senderverzeichnis: radio-browser.info, eine offene, von Freiwilligen gepflegte Datenbank.",
            "Zendergids: radio-browser.info, een open database bijgehouden door vrijwilligers.",
            "Directorio de emisoras: radio-browser.info, una base abierta mantenida por voluntarios.");
        Add("L_IndependenceNotice",
            "Application indépendante du projet STR : ni développée, ni maintenue, ni prise en charge par lui. Les problèmes rencontrés avec elle sont à signaler à son auteur.",
            "This app is independent of the STR project: not built, maintained or supported by it. Report problems with the app to its author.",
            "Diese App ist unabhängig vom STR-Projekt: sie wird von ihm weder entwickelt noch gepflegt noch unterstützt. Probleme mit der App bitte an ihren Autor melden.",
            "Deze app staat los van het STR-project: niet erdoor gebouwd, onderhouden of ondersteund. Meld problemen met de app bij de auteur ervan.",
            "Aplicación independiente del proyecto STR: ni desarrollada, ni mantenida, ni respaldada por él. Informa de los problemas con la aplicación a su autor.");
        Add("L_TrademarkNotice",
            "Projet personnel, sans lien avec Bose Corporation. « Bose » et « SoundTouch » sont des marques de Bose Corporation, citées pour indiquer la compatibilité.",
            "Personal project, not affiliated with Bose Corporation. “Bose” and “SoundTouch” are trademarks of Bose Corporation, named here to indicate compatibility.",
            "Privates Projekt, nicht mit der Bose Corporation verbunden. „Bose“ und „SoundTouch“ sind Marken der Bose Corporation und werden hier nur genannt, um die Kompatibilität anzugeben.",
            "Persoonlijk project, niet verbonden aan Bose Corporation. “Bose” en “SoundTouch” zijn merken van Bose Corporation, hier genoemd om de compatibiliteit aan te geven.",
            "Proyecto personal, sin vínculo con Bose Corporation. «Bose» y «SoundTouch» son marcas de Bose Corporation, citadas aquí para indicar la compatibilidad.");
        Add("L_ClaudeCredit",
            "Application écrite avec Claude (Anthropic).",
            "App written with Claude (Anthropic).",
            "App geschrieben mit Claude (Anthropic).",
            "App geschreven met Claude (Anthropic).",
            "Aplicación escrita con Claude (Anthropic).");

        // ---------------------------------------------------------- Diagnostic
        Add("D_Name", "Nom", "Name", "Name", "Naam", "Nombre");
        Add("D_Model", "Modèle", "Model", "Modell", "Model", "Modelo");
        Add("D_Firmware", "Firmware", "Firmware", "Firmware", "Firmware", "Firmware");
        Add("D_Address", "Adresse", "Address", "Adresse", "Adres", "Dirección");
        Add("D_Agent", "Agent STR", "STR agent", "STR-Agent", "STR-agent", "Agente STR");
        Add("D_AgentYes", "oui, port {0}", "yes, port {0}", "ja, Port {0}", "ja, poort {0}", "sí, puerto {0}");
        Add("D_AgentNo", "non détecté", "not detected", "nicht erkannt", "niet gevonden", "no detectado");
        Add("D_AgentSilent",
            "ne répond pas (ports 17008 et 8888 essayés)",
            "no answer (ports 17008 and 8888 tried)",
            "keine Antwort (Ports 17008 und 8888 versucht)",
            "geen antwoord (poorten 17008 en 8888 geprobeerd)",
            "sin respuesta (puertos 17008 y 8888 probados)");
        Add("D_AgentStored",
            "dernière version connue : {0}",
            "last known version: {0}",
            "zuletzt bekannte Version: {0}",
            "laatst bekende versie: {0}",
            "última versión conocida: {0}");
        Add("D_Notifications", "Notifications", "Notifications", "Benachrichtigungen", "Meldingen", "Notificaciones");
        Add("D_WsUp", "WebSocket 8080 connecté", "WebSocket 8080 connected", "WebSocket 8080 verbunden", "WebSocket 8080 verbonden", "WebSocket 8080 conectado");
        Add("D_WsDown", "hors ligne", "offline", "offline", "offline", "sin conexión");
        Add("D_BassRange", "{0} à {1} (défaut {2})", "{0} to {1} (default {2})", "{0} bis {1} (Standard {2})", "{0} tot {1} (standaard {2})", "{0} a {1} (por defecto {2})");
        Add("D_BassNone", "non réglables sur ce modèle", "not adjustable on this model", "bei diesem Modell nicht einstellbar", "niet instelbaar op dit model", "no ajustables en este modelo");
        Add("D_Endpoints", "Points exposés", "Exposed endpoints", "Verfügbare Endpunkte", "Beschikbare eindpunten", "Endpoints expuestos");
        Add("S_PresetsUnread",
            "Touches non relues — l'enceinte n'a pas répondu.",
            "Presets not re-read — the speaker did not answer.",
            "Tasten nicht neu gelesen — der Lautsprecher hat nicht geantwortet.",
            "Toetsen niet opnieuw gelezen — de speaker antwoordde niet.",
            "Teclas no releídas — el altavoz no respondió.");
        Add("D_Zone", "Zone vue par chaque enceinte", "Zone as each speaker sees it", "Zone aus Sicht jedes Lautsprechers", "Zone zoals elke speaker die ziet", "Zona según cada altavoz");
        Add("D_EndpointsNone",
            "/supportedURLs non disponible.",
            "/supportedURLs not available.",
            "/supportedURLs nicht verfügbar.",
            "/supportedURLs niet beschikbaar.",
            "/supportedURLs no disponible.");
    }
}
