# SoundReborn Remote

*English below · [Version française](#version-française)*

Android remote for Bose SoundTouch speakers, written in C# with .NET MAUI.
It talks to the speaker directly over the local network: no account, no cloud,
no service in between.

> A personal project, unaffiliated with Bose Corporation. "Bose" and "SoundTouch" are
> trademarks of Bose Corporation, named here only to state what hardware this works with.
>
> **This application is independent of the STR project**: it is not developed, maintained
> or supported by it. Report problems with this app
> [here](https://github.com/ben240374/SoundRebornRemote/issues), not to the STR project.

## Thanks to STR

When Bose shut down its cloud on 6 May 2026, SoundTouch speakers lost most of what made
them useful. **[STR — SoundTouch Reborn](https://st-reborn.de/)**, by
**Jens Roggenfelder ([JRpersonal](https://github.com/JRpersonal/streborn))**, is the agent
that runs on the speaker itself and gives back what the cloud provided. This application
does nothing more than talk to it: without his work it would have nothing to control.

The agent is not redistributed here, and this application does not install it — that is
done with the STR tool for PC or Mac. It simply flags speakers that do not have it yet.

## Screens

<p align="center">
  <img src="docs/images/01-lecture.jpg" width="30%" alt="Play tab" />
  <img src="docs/images/03-preselections.jpg" width="30%" alt="Presets tab" />
  <img src="docs/images/05-groupes.jpg" width="30%" alt="Groups tab" />
</p>
<p align="center">
  <img src="docs/images/02-lecture-graves.jpg" width="30%" alt="Bass and presets" />
  <img src="docs/images/04-sources.jpg" width="30%" alt="Sources tab" />
  <img src="docs/images/06-reglages.jpg" width="30%" alt="Settings tab" />
</p>
<p align="center"><em>IP addresses and account names replaced with example values.</em></p>

## User manual

**[Project site →](https://ben240374.github.io/SoundRebornRemote/)** — the manual reads
better there.

In the repository: [English](docs/manual/en.md) · [Français](docs/manual/fr.md) ·
[Deutsch](docs/manual/de.md) · [Nederlands](docs/manual/nl.md) · [Español](docs/manual/es.md)

Installation, screen by screen, gestures, troubleshooting and known limits. PDF versions
are attached to each [release](https://github.com/ben240374/SoundRebornRemote/releases).

## What the application controls

| Feature | Where | How it is done |
|---|---|---|
| Volume, mute, standby/power | Play tab | `POST :8090/volume`, `MUTE` / `POWER` keys, or `PUT :8888/api/box/volume` |
| Play / pause / next / previous | Play tab | `POST :8888/api/pause` `…/resume` `…/next` `…/prev`, falling back to `POST :8090/key` |
| Bass | Play tab | `POST :8090/bass`, range read from `/bassCapabilities` |
| Presets 1 to 6 | Presets tab | `POST :8888/api/play/<slot>`, falling back to the `PRESET_n` key |
| Now playing, artwork, source | everywhere | WebSocket `:8080` (subprotocol `gabbo`), falling back to `GET /now_playing` |
| Sources, Bluetooth, AUX | Sources tab | `GET :8090/sources`, `POST :8090/select`, `PUT :8888/api/box/source` |
| Playing an arbitrary URL | Sources tab | `POST :8888/api/play` |
| Listening history | Sources tab | `GET :8888/api/recent` |
| Multi-room groups | Groups tab | `POST :8888/api/box/zone`, falling back to `POST :8090/setZone` |
| Per-speaker volume in a group | Play and Groups tabs | `GET/POST :8888/api/box/zone/volume` |
| Speaker discovery | Settings tab | mDNS (`_streborn._tcp`, `_soundtouch._tcp`), falling back to a `/24` sweep on `GET :8090/info` |
| Switching speakers | Play tab | cards at the top of the screen, with model and agent version |
| STR agent version | Play tab | `GET :8888/api/agent/version` |
| Station search | Presets tab | radio-browser.info, queried by the application itself |
| Assigning a station to a key | Presets tab | `PUT :8888/api/presets/{slot}` |
| Interface language | Settings tab | EN, FR, DE, NL, ES — applied immediately, no restart |
| Light or dark theme | Settings tab | System, Light or Dark — switches immediately |
| Swipe navigation | everywhere | horizontal gesture recognised in `MainActivity`, without touching scrolling |
| Speakers without the STR agent | Settings tab | flagged, pointing to the official installer |

Two APIs are used, and the application picks on its own:

- **The native firmware API, port 8090, XML.** Always present. It remains the reference
  for bass, sources and keys.
- **The STR agent API (SoundTouch Reborn), port 8888 or 17008, JSON.** Present only if
  you installed the agent. It is preferred whenever it answers: it wakes the speaker, it
  can play an arbitrary URL, and it replaces what the Bose cloud did before it shut down
  on 6 May 2026.

Port 17008 is tried first: on BCO chassis (SoundTouch Portable, some ST20s), the network
chipset accepts incoming connections only on that port, redirected internally to 8888.

### What the API does not allow

- **No treble control.** The SoundTouch firmware exposes only `/bass`. There is no
  `/treble` equivalent on any model. The Play tab says so.
- **`POST :8090/speaker` is dead.** That endpoint (announcements, text to speech)
  validated an application key against the Bose cloud and now returns `403 unsupported
  device`. The application uses the agent's `POST /api/play` instead.
- **Spotify cannot be browsed from here.** With the STR agent the speaker is a Spotify
  Connect endpoint again: you pick the speaker in the Spotify app, and transport (pause,
  next) then answers from the Play tab. Browsing your Spotify catalogue would need the
  Spotify API and a developer account, which is another project.

## Requirements

- **Visual Studio 2022** (17.12 or newer) with the *.NET Multi-platform App UI
  development* workload. It installs the Android SDK and OpenJDK.
- **.NET 10 SDK**. If you only have 9 or 8, change these lines:
  - `src/SoundReborn.Core/SoundReborn.Core.csproj` → `<TargetFramework>net9.0</TargetFramework>`
  - `src/SoundRebornRemote.App/SoundRebornRemote.App.csproj` → `<TargetFrameworks>net9.0-android</TargetFrameworks>`
  - and in the same file, `Microsoft.Extensions.Logging.Debug` at version `9.0.0`.
- An Android 7.0 or newer phone, **on the same Wi-Fi as the speakers**.

From the command line, if you prefer:

```powershell
dotnet workload install maui-android
dotnet restore
dotnet build src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android
```

## Building the APK

From Visual Studio: pick the **Release** configuration, your phone over USB debugging or
an emulator, then `Build > Publish`.

From the command line:

```powershell
dotnet publish src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android -c Release
```

The debug-signed APK lands in
`src\SoundRebornRemote.App\bin\Release\net10.0-android\publish\`.
For sideloading, that file is enough: copy it to the phone and allow installation from
an unknown source.

To sign with your own key, pass the keystore on the command line rather than writing it
into the `.csproj` — passwords do not belong in a versioned file:

```powershell
dotnet publish src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android -c Release `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=mykey.keystore `
  -p:AndroidSigningKeyAlias=soundreborn `
  -p:AndroidSigningKeyPass='<password>' `
  -p:AndroidSigningStorePass='<password>'
```

Single quotes matter in PowerShell: `$` is expanded inside a bare or double-quoted string.

## First run

1. Start the app, go to **Settings**, tap **Scan the local network**.
   Discovery asks mDNS first and only sweeps the subnet if that finds nothing. A few
   seconds either way.
2. Tap **Use** on the speaker you want. It is remembered: next time the app reconnects
   on its own.
3. If nothing is found, type the IP address by hand (field just below). Usual causes:
   phone on mobile data, isolated guest network, or client isolation enabled on the router.
4. **Diagnostics** shows the model, the firmware version, the STR agent and its version,
   the WebSocket state, the real bass range of the model, and what each speaker says
   about the current zone.

## Code layout

```
SoundRebornRemote.sln
├── src/SoundReborn.Core/                 headless library, reusable
│   ├── BoseApiClient.cs                  firmware API :8090 (XML)
│   ├── StrApiClient.cs                   STR agent API :8888 / :17008 (JSON)
│   ├── GabboSocket.cs                    WebSocket :8080, real-time notifications
│   ├── SpeakerDiscovery.cs               mDNS first, subnet sweep as fallback
│   ├── MdnsProbe.cs                      raw DNS-SD probe, QU bit set
│   ├── RadioBrowserClient.cs             radio-browser.info directory, mirrors and filters
│   ├── SpeakerDevice.cs                  façade: picks STR or firmware, handles fallbacks
│   ├── RemoteKey.cs                      key enumeration and protocol names
│   ├── Internal/XmlParsing.cs            reading and building XML bodies
│   └── Models/                           data models
└── src/SoundRebornRemote.App/            MAUI Android application
    ├── MauiProgram.cs                    dependency injection
    ├── AppShell.xaml                     the five tabs
    ├── Services/                         SpeakerManager, ZoneLocator, ArtworkCache,
    │                                     ThemeService, Localization, Debouncer
    ├── ViewModels/                       one per tab, MVVM with CommunityToolkit
    ├── Views/                            the XAML pages
    └── Platforms/Android/                manifest, permissions, cleartext traffic
```

`SoundReborn.Core` depends on nothing but the framework. You can reference it as-is from
a WinForms, WPF or console project to drive the speakers from a PC — it is the same code.

## Implementation notes worth a word

**The WebSocket requires the `gabbo` subprotocol.** Without it the firmware refuses the
handshake with no usable error. It also closes an idle connection after about five
minutes, hence `KeepAliveInterval`, `KeepAliveTimeout` and the backing-off reconnect in
`GabboSocket`.

**A key press takes two requests.** `POST /key` expects `state="press"` then
`state="release"`. Sending only the first leaves the key held down on the speaker.

**Sliders are debounced.** A drag produces dozens of values per second and the firmware's
HTTP server locks up if you send them all. `Debouncer` keeps only the last after 220 ms of
silence, and `PlayerViewModel` ignores volume notifications for 1.5 s after a local action
— otherwise the slider jumps under your finger when the echo comes back.

**A timeout is never treated as a refusal.** Every fallback from the agent to a firmware
key press is guarded by `catch (Exception ex) when (IsDefiniteRefusal(ex))`, which admits
only `SpeakerException` and `HttpRequestException`. `POWER` and `PLAY_PAUSE` are toggles:
a request that arrived but whose answer was lost would otherwise switch the speaker off
instead of on.

**Every call carries its own budget.** The shared `HttpClient` runs on
`Timeout.InfiniteTimeSpan`, and each call sets its own deadline through a linked
`CancellationTokenSource` — 8 seconds for ordinary calls, 25 for those that wake a
speaker. One client-wide timeout cannot serve both, and the shorter always wins.

**The master of a zone is found, not assumed.** The app used to take the speaker it was
connected to for the master of its group. That holds for a group you just formed and
fails as soon as you switch to a slave. `ZoneLocator` asks the speaker itself, then the
other known speakers if it says nothing, because whoever leads a group always describes
it. Commands then follow the master, since the master is what is playing.

**`/api/box/zone/volume` describes the zone only from the master's side**, and the
firmware's `/getZone` member list varies by chassis — on a slave the `master` attribute
can be present with an empty member list. Membership is therefore keyed on the `master`
attribute, with the member list as a supplement.

**`/api/status` returns XML, not JSON.** The agent relays the firmware's `now_playing`
body as-is behind a micro-cache. That is deliberate on the agent side, which is why
`StrApiClient.GetStatusAsync` parses XML where the rest of the class parses JSON.

**Logos are downloaded by the application, not by the Image control.** That is
`Services/ArtworkCache.cs`. These images are hosted by the stations themselves: expired or
self-signed certificates, cascading redirects, servers that refuse a request without a
browser User-Agent, dead hosts. Android's image loader then fails silently — no error, no
image, an empty tile. Downloading them ourselves gives control over the header, the
timeout, the maximum size and certificate tolerance; we check the file's magic number,
because a server that announces `image/png` and returns an HTML error page is common; and
the image stays available offline on the next start. The second, more tolerant HTTP client
is used only after a TLS failure of the first: what goes through it is a public logo, no
user data.

**Preset artwork is remembered, and never wiped by a failed read.** A call that times out
returned an empty list and the grid went blank although it had just displayed correctly.
`PresetsViewModel` replaces an image only with another image — never with nothing — keeps
the last known URL per key in the preferences, and distinguishes "nothing came back" from
"both reads failed", leaving the grid alone in the second case.

**Station search queries radio-browser directly.** The STR agent no longer serves
`/api/radio/*`: since v0.8 it does not even compile the radio-browser client, the desktop
application queries the service. `RadioBrowserClient` does the same, with the same
hard-coded mirror list and the same switch on first error — the official server discovery
itself goes through a mirror, so it falls over at the same time. The "Bose-compatible"
filter follows the desktop client's rule: HLS accepted (the agent converts it), MP3 / AAC
/ AAC+ / MPEG accepted, unknown codec allowed through, Ogg / Opus / FLAC excluded.

**Both themes live in the styles.** No view references a raw colour: everything goes
through a style or an `AppThemeBinding`, and `ThemeService` only sets `UserAppTheme`. That
is what makes the light/dark switch immediate, without reloading a single page.

**Translations are a C# table, not .resx files.** No designer-generated file to keep in
sync, and adding a language is one more column in the `Add()` calls of
`Services/Localization.cs`. Labels are pushed into the application's resource dictionary
and consumed with `{DynamicResource L_...}`, so a language change is immediate. Code uses
`Localization.Get("key")`.

**Source comments are in French.** The author's working language. The public API names,
the models and this document are in English.

## Possible extensions

- Android widget and lock-screen controls (`MediaSession`).
- Browsing the DLNA library through the agent's `GET /api/library/browse`.
- Storing a preset from the phone (`POST :8090/storePreset`).
- Announcements with playback resume (the agent's `POST :17008/api/announce`).

## Sources

- Native firmware API: the Bose SoundTouch Web API documentation and the
  community-maintained endpoint inventory.
- Agent API, ports, firmware behaviour: `docs/CONTROL-API.md`, `docs/AUTOMATION.md` and
  `docs/ARCHITECTURE.md` in the `JRpersonal/streborn` repository.

## Licence

MIT — see [LICENSE](LICENSE).

Trademark notices, the credit to STR and the support scope are in [NOTICE.md](NOTICE.md).

---
---

# Version française

Télécommande Android pour enceintes Bose SoundTouch, écrite en C# avec .NET MAUI.
Elle parle directement à l'enceinte sur le réseau local : aucun compte, aucun cloud,
aucun service intermédiaire.

> Projet personnel, sans lien avec Bose Corporation. « Bose » et « SoundTouch » sont des
> marques de Bose Corporation, citées ici uniquement pour indiquer la compatibilité.
>
> **Cette application est indépendante du projet STR** : elle n'est ni développée, ni
> maintenue, ni prise en charge par lui. Signalez les problèmes rencontrés avec elle
> [ici](https://github.com/ben240374/SoundRebornRemote/issues), pas au projet STR.

## Grâce à STR

Quand Bose a coupé son cloud le 6 mai 2026, les enceintes SoundTouch ont perdu
l'essentiel de leur usage. **[STR — SoundTouch Reborn](https://st-reborn.de/fr/)**, de
**Jens Roggenfelder ([JRpersonal](https://github.com/JRpersonal/streborn))**, est l'agent
qui tourne sur l'enceinte elle-même et lui rend ce que le cloud assurait. Cette
application ne fait que lui parler : sans son travail, elle n'aurait rien à piloter.

L'agent n'est pas redistribué ici, et cette application ne l'installe pas — l'installation
se fait avec l'outil STR pour PC ou Mac. Elle signale simplement les enceintes qui ne
l'ont pas encore.

## Aperçu

<p align="center">
  <img src="docs/images/01-lecture.jpg" width="30%" alt="Page Lecture" />
  <img src="docs/images/03-preselections.jpg" width="30%" alt="Page Présélections" />
  <img src="docs/images/05-groupes.jpg" width="30%" alt="Page Groupes" />
</p>
<p align="center">
  <img src="docs/images/02-lecture-graves.jpg" width="30%" alt="Graves et présélections" />
  <img src="docs/images/04-sources.jpg" width="30%" alt="Page Sources" />
  <img src="docs/images/06-reglages.jpg" width="30%" alt="Page Réglages" />
</p>
<p align="center"><em>Adresses IP et noms de comptes remplacés par des valeurs d'exemple.</em></p>

## Manuel d'utilisation

**[Site du projet →](https://ben240374.github.io/SoundRebornRemote/)** — le manuel s'y lit
plus confortablement.

Dans le dépôt : [Français](docs/manual/fr.md) · [English](docs/manual/en.md) ·
[Deutsch](docs/manual/de.md) · [Nederlands](docs/manual/nl.md) · [Español](docs/manual/es.md)

Installation, écran par écran, gestes, dépannage et limites connues. Les versions PDF
sont jointes à chaque [release](https://github.com/ben240374/SoundRebornRemote/releases).

## Ce que l'application pilote

| Fonction | Où | Comment c'est obtenu |
|---|---|---|
| Volume, sourdine, marche/veille | onglet Lecture | `POST :8090/volume`, touches `MUTE` / `POWER`, ou `PUT :8888/api/box/volume` |
| Lecture / pause / suivant / précédent | onglet Lecture | `POST :8888/api/pause` `…/resume` `…/next` `…/prev`, repli sur `POST :8090/key` |
| Graves | onglet Lecture | `POST :8090/bass`, plage lue dans `/bassCapabilities` |
| Présélections 1 à 6 | onglet Présélections | `POST :8888/api/play/<slot>`, repli sur la touche `PRESET_n` |
| Titre en cours, pochette, source | partout | WebSocket `:8080` (sous-protocole `gabbo`), repli sur `GET /now_playing` |
| Sources, Bluetooth, AUX | onglet Sources | `GET :8090/sources`, `POST :8090/select`, `PUT :8888/api/box/source` |
| Lecture d'une URL quelconque | onglet Sources | `POST :8888/api/play` |
| Historique d'écoute | onglet Sources | `GET :8888/api/recent` |
| Groupes multi-pièces | onglet Groupes | `POST :8888/api/box/zone`, repli sur `POST :8090/setZone` |
| Volume par enceinte du groupe | onglets Lecture et Groupes | `GET/POST :8888/api/box/zone/volume` |
| Découverte des enceintes | onglet Réglages | mDNS (`_streborn._tcp`, `_soundtouch._tcp`), repli sur le balayage du `/24` sur `GET :8090/info` |
| Bascule d'une enceinte à l'autre | onglet Lecture | cartes en haut de l'écran, avec modèle et version de l'agent |
| Version de l'agent STR | onglet Lecture | `GET :8888/api/agent/version` |
| Recherche de stations | onglet Présélections | radio-browser.info, interrogé directement par l'application |
| Affectation à une touche | onglet Présélections | `PUT :8888/api/presets/{slot}` |
| Langue de l'interface | onglet Réglages | FR, EN, DE, NL, ES — changement immédiat, sans redémarrage |
| Thème clair ou foncé | onglet Réglages | Système, Clair ou Foncé — bascule immédiate |
| Navigation par balayage | partout | geste horizontal reconnu dans `MainActivity`, sans toucher au défilement |
| Enceintes sans agent STR | onglet Réglages | signalées, avec renvoi vers l'outil d'installation officiel |

Deux API sont utilisées, et l'application choisit toute seule :

- **L'API native du firmware, port 8090, en XML.** Toujours présente. C'est elle qui
  reste la référence pour les graves, les sources et les touches.
- **L'API de l'agent STR (SoundTouch Reborn), port 8888 ou 17008, en JSON.** Présente
  seulement si tu as installé l'agent. Elle est préférée quand elle répond : elle gère
  le réveil de l'enceinte, elle sait jouer une URL arbitraire, et elle remplace ce que
  le cloud Bose faisait avant son arrêt le 6 mai 2026.

Le port 17008 est testé en premier : sur les châssis BCO (SoundTouch Portable, certaines
ST20), le chipset réseau n'accepte de connexion entrante que sur ce port, redirigé vers
le 8888 en interne.

### Ce que l'API ne permet pas

- **Pas de réglage d'aigus.** Le firmware SoundTouch n'expose que `/bass`. Il n'y a pas
  d'équivalent `/treble`, sur aucun modèle. L'onglet Lecture le signale.
- **`POST :8090/speaker` est mort.** Ce point de terminaison (annonces, synthèse vocale)
  validait une clé applicative auprès du cloud Bose et renvoie désormais `403 unsupported
  device`. L'application passe par `POST /api/play` de l'agent à la place.
- **Spotify ne se pilote pas depuis ici.** Avec l'agent STR, l'enceinte redevient un point
  Spotify Connect : tu choisis l'enceinte dans l'application Spotify, et le transport
  (pause, suivant) répond ensuite depuis l'onglet Lecture. Parcourir ton catalogue Spotify
  demanderait l'API Spotify et un compte développeur, ce qui est un autre projet.

## Prérequis

- **Visual Studio 2022** (17.12 ou plus récent) avec la charge de travail
  *Développement .NET Multiplateforme pour applications mobiles (.NET MAUI)*.
  Elle installe le SDK Android et OpenJDK.
- **SDK .NET 10**. Si tu n'as que le 9 ou le 8, change les deux lignes suivantes :
  - `src/SoundReborn.Core/SoundReborn.Core.csproj` → `<TargetFramework>net9.0</TargetFramework>`
  - `src/SoundRebornRemote.App/SoundRebornRemote.App.csproj` → `<TargetFrameworks>net9.0-android</TargetFrameworks>`
  - et dans le même fichier, `Microsoft.Extensions.Logging.Debug` en version `9.0.0`.
- Un téléphone Android 7.0 ou plus récent, **sur le même Wi-Fi que les enceintes**.

En ligne de commande, si tu préfères :

```powershell
dotnet workload install maui-android
dotnet restore
dotnet build src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android
```

## Construire l'APK

Depuis Visual Studio : sélectionne la configuration **Release**, ton téléphone en débogage
USB ou un émulateur, puis `Générer > Publier`.

En ligne de commande :

```powershell
dotnet publish src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android -c Release
```

L'APK signé en debug sort dans
`src\SoundRebornRemote.App\bin\Release\net10.0-android\publish\`.
Pour une installation hors Play Store, ce fichier suffit : transfère-le sur le téléphone
et autorise l'installation depuis une source inconnue.

Pour signer avec ta propre clé, passe le keystore en ligne de commande plutôt que de
l'écrire dans le `.csproj` — un mot de passe n'a rien à faire dans un fichier versionné :

```powershell
dotnet publish src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android -c Release `
  -p:AndroidKeyStore=true `
  -p:AndroidSigningKeyStore=macle.keystore `
  -p:AndroidSigningKeyAlias=soundreborn `
  -p:AndroidSigningKeyPass='<mot de passe>' `
  -p:AndroidSigningStorePass='<mot de passe>'
```

Les guillemets simples comptent : en PowerShell, `$` est développé dans une chaîne nue ou
entre guillemets doubles.

## Première utilisation

1. Lance l'application, va dans **Réglages**, touche **Balayer le réseau local**.
   La découverte interroge d'abord le mDNS et ne balaie le sous-réseau que si elle ne
   trouve rien. Quelques secondes dans les deux cas.
2. Touche **Utiliser** sur l'enceinte voulue. Elle est mémorisée : au prochain lancement
   l'application s'y reconnecte seule.
3. Si rien n'est trouvé, saisis l'adresse IP à la main (champ juste en dessous). Les causes
   habituelles : téléphone en 4G, réseau invité isolé, ou isolation des clients activée
   sur la box.
4. **Diagnostic** affiche le modèle, la version du firmware, l'agent STR et sa version,
   l'état du WebSocket, la plage de graves réelle du modèle, et ce que chaque enceinte
   déclare de la zone en cours.

## Organisation du code

```
SoundRebornRemote.sln
├── src/SoundReborn.Core/                 bibliothèque sans interface, réutilisable
│   ├── BoseApiClient.cs                  API firmware :8090 (XML)
│   ├── StrApiClient.cs                   API agent STR :8888 / :17008 (JSON)
│   ├── GabboSocket.cs                    WebSocket :8080, notifications temps réel
│   ├── SpeakerDiscovery.cs               mDNS d'abord, balayage du sous-réseau en repli
│   ├── MdnsProbe.cs                      sonde DNS-SD brute, bit QU posé
│   ├── RadioBrowserClient.cs             annuaire radio-browser.info, miroirs et filtres
│   ├── SpeakerDevice.cs                  façade : choisit STR ou firmware, gère les replis
│   ├── RemoteKey.cs                      énumération des touches et leur nom protocolaire
│   ├── Internal/XmlParsing.cs            lecture et construction des corps XML
│   └── Models/                           modèles de données
└── src/SoundRebornRemote.App/            application MAUI Android
    ├── MauiProgram.cs                    injection de dépendances
    ├── AppShell.xaml                     les cinq onglets
    ├── Services/                         SpeakerManager, ZoneLocator, ArtworkCache,
    │                                     ThemeService, Localization, Debouncer
    ├── ViewModels/                       un par onglet, MVVM avec CommunityToolkit
    ├── Views/                            les pages XAML
    └── Platforms/Android/                manifeste, autorisations, trafic en clair
```

`SoundReborn.Core` ne dépend de rien d'autre que du framework. Tu peux la référencer telle
quelle depuis un projet WinForms, WPF ou console si tu veux piloter les enceintes depuis
le PC — c'est le même code.

## Points d'implémentation qui méritent un mot

**Le WebSocket exige le sous-protocole `gabbo`.** Sans lui, le firmware refuse la poignée
de main sans message d'erreur exploitable. Il ferme aussi une connexion inactive au bout
d'environ cinq minutes, d'où le `KeepAliveInterval`, le `KeepAliveTimeout` et la
reconnexion avec attente croissante dans `GabboSocket`.

**Une touche demande deux requêtes.** `POST /key` attend un `state="press"` puis un
`state="release"`. N'envoyer que le premier laisse la touche enfoncée côté enceinte.

**Les curseurs sont temporisés.** Un glissement produit des dizaines de valeurs par
seconde ; le serveur HTTP du firmware se bloque si on les lui envoie toutes. `Debouncer`
ne garde que la dernière après 220 ms de silence, et `PlayerViewModel` ignore les
notifications de volume pendant 1,5 s après une action locale — sinon le curseur saute
sous le doigt quand l'écho revient.

**Un délai dépassé n'est jamais pris pour un refus.** Chaque repli de l'agent vers une
touche du firmware est gardé par `catch (Exception ex) when (IsDefiniteRefusal(ex))`, qui
n'admet que `SpeakerException` et `HttpRequestException`. `POWER` et `PLAY_PAUSE` sont des
bascules : une requête arrivée mais dont la réponse s'est perdue éteindrait sinon
l'enceinte au lieu de l'allumer.

**Chaque appel porte son propre budget.** Le `HttpClient` partagé tourne en
`Timeout.InfiniteTimeSpan`, et chaque appel pose son échéance par un
`CancellationTokenSource` lié — 8 secondes pour un appel ordinaire, 25 pour ceux qui
réveillent l'enceinte. Un délai unique ne peut pas servir les deux, et le plus court
l'emporterait toujours.

**Le maître d'une zone se cherche, il ne se suppose pas.** L'application prenait
l'enceinte à laquelle elle était connectée pour le maître de son groupe. C'est vrai d'un
groupe qu'on vient de former, faux dès qu'on bascule sur une suivante. `ZoneLocator`
interroge l'enceinte, puis les autres si elle ne déclare rien, car celle qui mène un
groupe le décrit toujours. Les commandes suivent ensuite le maître, puisque c'est lui qui
diffuse.

**`/api/box/zone/volume` ne décrit la zone que vu du maître**, et la liste des membres de
`/getZone` varie selon le châssis — sur une suivante, l'attribut `master` peut être
présent avec une liste vide. L'appartenance se teste donc sur l'attribut `master`, la
liste des membres ne servant que de complément.

**`/api/status` renvoie du XML, pas du JSON.** L'agent relaie le corps `now_playing` du
firmware tel quel, derrière un micro-cache. C'est volontaire côté agent, et c'est pour ça
que `StrApiClient.GetStatusAsync` parse du XML là où le reste de la classe fait du JSON.

**Les logos sont téléchargés par l'application, pas par le contrôle Image.** C'est
`Services/ArtworkCache.cs`. Ces images sont hébergées par les stations elles-mêmes :
certificats périmés ou auto-signés, redirections en cascade, serveurs qui refusent une
requête sans User-Agent de navigateur, hôtes morts. Le chargeur d'images d'Android
échoue alors en silence — pas d'erreur, pas d'image, une tuile vide. En téléchargeant
nous-mêmes on maîtrise l'en-tête, le délai d'attente, la taille maximale et la tolérance
aux certificats ; on vérifie le nombre magique du fichier, parce qu'un serveur qui
annonce `image/png` et renvoie une page HTML d'erreur est monnaie courante ; et l'image
reste disponible hors ligne au lancement suivant. Le second client HTTP, plus tolérant,
ne sert qu'après un échec TLS du premier : ce qui y transite est un logo public, aucune
donnée de l'utilisateur.

**Les pochettes de présélections sont mémorisées, et une lecture ratée ne les efface
pas.** Un appel qui expire renvoyait une liste sans image et la grille se vidait alors
qu'elle s'était affichée correctement juste avant. `PresetsViewModel` ne remplace donc une
image que par une autre image — jamais par du vide — garde la dernière URL connue par
touche dans les préférences, et distingue « rien n'est revenu » de « les deux lectures ont
échoué », laissant la grille en place dans le second cas.

**La recherche de stations interroge radio-browser directement.** L'agent STR ne sert
plus `/api/radio/*` : depuis la v0.8 il ne compile même plus le client radio-browser,
c'est l'application de bureau qui interroge le service. `RadioBrowserClient` fait pareil,
avec la même liste de miroirs codée en dur et la même bascule à la première erreur — la
découverte officielle des serveurs passe elle-même par un miroir, donc elle tombe en
même temps que lui. Le filtre « compatibles Bose » reprend la règle du client de bureau :
HLS accepté (l'agent le convertit), MP3/AAC/AAC+/MPEG acceptés, codec inconnu laissé
passer, Ogg/Opus/FLAC écartés.

**Les deux thèmes vivent dans les styles.** Aucune vue ne référence une couleur brute :
tout passe par un style ou un `AppThemeBinding`, et `ThemeService` se contente de poser
`UserAppTheme`. C'est ce qui rend la bascule clair/foncé immédiate, sans recharger une
seule page.

**Les traductions sont une table C#, pas des fichiers .resx.** Pas de fichier généré par
le concepteur à garder synchronisé, et ajouter une langue tient en une colonne de plus
dans les appels `Add()` de `Services/Localization.cs`. Les libellés sont poussés dans le
dictionnaire de ressources de l'application et consommés par `{DynamicResource L_...}` :
le changement de langue est donc immédiat. Le code passe par `Localization.Get("clé")`.

**Les commentaires du code sont en français.** C'est la langue de travail de l'auteur. Les
noms publics, les modèles et ce document sont en anglais.

## Pistes d'extension

- Widget Android et contrôles sur l'écran de verrouillage (`MediaSession`).
- Parcours de la bibliothèque DLNA via `GET /api/library/browse` de l'agent.
- Enregistrement d'une présélection depuis le téléphone (`POST :8090/storePreset`).
- Annonces avec reprise de la lecture (`POST :17008/api/announce` de l'agent).

## Sources

- API native du firmware : documentation Bose SoundTouch Web API et l'inventaire des
  points de terminaison maintenu par la communauté.
- API de l'agent, ports, comportements du firmware : `docs/CONTROL-API.md`,
  `docs/AUTOMATION.md` et `docs/ARCHITECTURE.md` du dépôt `JRpersonal/streborn`.

## Licence

MIT — voir [LICENSE](LICENSE).

Les mentions de marques, le crédit à STR et le périmètre de support sont dans
[NOTICE.md](NOTICE.md).
