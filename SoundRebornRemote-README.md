# SoundReborn Remote

Télécommande Android pour enceintes Bose SoundTouch, écrite en C# avec .NET MAUI.
Elle parle directement à l'enceinte sur le réseau local : aucun compte, aucun cloud,
aucun service intermédiaire.

> Projet personnel, sans lien avec Bose Corporation. « Bose » et « SoundTouch » sont des
> marques de Bose Corporation, citées ici uniquement pour indiquer la compatibilité.

## Grâce à STR

Quand Bose a coupé son cloud en février 2026, les enceintes SoundTouch ont perdu
l'essentiel de leur usage. **[STR — SoundTouch Reborn](https://st-reborn.de/fr/)**, de
**Jens Roggenfelder ([JRpersonal](https://github.com/JRpersonal/streborn))**, est l'agent
qui tourne sur l'enceinte elle-même et lui rend ce que le cloud assurait. Cette
application ne fait que lui parler : sans son travail, elle n'aurait rien à piloter.

L'agent n'est pas redistribué ici, et cette application ne l'installe pas — l'installation
se fait avec l'outil STR pour PC ou Mac. Elle signale simplement les enceintes qui ne
l'ont pas encore.

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
| Groupes multi-pièces | onglet Multi-pièces | `POST :8888/api/box/zone`, repli sur `POST :8090/setZone` |
| Volume par enceinte du groupe | onglet Multi-pièces | `GET/POST :8888/api/box/zone/volume` |
| Découverte des enceintes | onglet Réglages | balayage du `/24` sur `GET :8090/info` |
| Bascule d'une enceinte à l'autre | onglet Lecture | cartes en haut de l'écran, avec modèle et version de l'agent |
| Marche / veille | onglet Lecture | `POST :8888/api/box/power`, repli sur la touche `POWER` |
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
  le cloud Bose faisait avant son arrêt de février 2026.

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

Depuis Visual Studio : sélectionne la configuration **Release**, la cible
*Android Emulator* ou ton téléphone en débogage USB, puis `Générer > Publier`.

En ligne de commande :

```powershell
dotnet publish src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android -c Release
```

L'APK signé en debug sort dans
`src\SoundRebornRemote.App\bin\Release\net10.0-android\publish\`.
Pour une installation hors Play Store, ce fichier suffit : transfère-le sur le téléphone
et autorise l'installation depuis une source inconnue.

Pour signer avec ta propre clé, ajoute au `.csproj` :

```xml
<PropertyGroup Condition="'$(Configuration)'=='Release'">
  <AndroidKeyStore>true</AndroidKeyStore>
  <AndroidSigningKeyStore>macle.keystore</AndroidSigningKeyStore>
  <AndroidSigningKeyAlias>soundreborn</AndroidSigningKeyAlias>
  <AndroidSigningKeyPass>…</AndroidSigningKeyPass>
  <AndroidSigningStorePass>…</AndroidSigningStorePass>
</PropertyGroup>
```

## Première utilisation

1. Lance l'application, va dans **Réglages**, touche **Balayer le réseau local**.
   Le balayage interroge les 254 adresses du sous-réseau sur le port 8090, en parallèle.
   Compte quelques secondes.
2. Touche **Utiliser** sur l'enceinte voulue. Elle est mémorisée : au prochain lancement
   l'application s'y reconnecte seule.
3. Si rien n'est trouvé, saisis l'adresse IP à la main (champ juste en dessous). Les causes
   habituelles : téléphone en 4G, réseau invité isolé, ou isolation des clients activée
   sur la box.
4. **Diagnostic** affiche le modèle, la version du firmware, la présence de l'agent STR,
   l'état du WebSocket et la plage de graves réelle du modèle.

## Organisation du code

```
SoundRebornRemote.sln
├── src/SoundReborn.Core/                 bibliothèque sans interface, réutilisable
│   ├── BoseApiClient.cs                 API firmware :8090 (XML)
│   ├── StrApiClient.cs                  API agent STR :8888 / :17008 (JSON)
│   ├── GabboSocket.cs                   WebSocket :8080, notifications temps réel
│   ├── SpeakerDiscovery.cs              balayage du sous-réseau
│   ├── RadioBrowserClient.cs            annuaire radio-browser.info, miroirs et filtres
│   ├── SpeakerDevice.cs              façade : choisit STR ou firmware, gère les replis
│   ├── RemoteKey.cs                     énumération des touches et leur nom protocolaire
│   ├── Internal/XmlParsing.cs           lecture et construction des corps XML
│   └── Models/                          modèles de données
└── src/SoundRebornRemote.App/            application MAUI Android
    ├── MauiProgram.cs                   injection de dépendances
    ├── AppShell.xaml                    les cinq onglets
    ├── Services/                        SpeakerManager, ArtworkCache, ThemeService,
    │                                    Localization, Debouncer, ServiceHelper
    ├── ViewModels/                      un par onglet, MVVM avec CommunityToolkit
    ├── Views/                           les pages XAML
    └── Platforms/Android/               manifeste, autorisations, trafic en clair
```

`SoundReborn.Core` ne dépend de rien d'autre que du framework. Tu peux la référencer telle
quelle depuis un projet WinForms, WPF ou console si tu veux piloter les enceintes depuis
le PC — c'est le même code.

## Points d'implémentation qui méritent un mot

**Le WebSocket exige le sous-protocole `gabbo`.** Sans lui, le firmware refuse la poignée
de main sans message d'erreur exploitable. Il ferme aussi une connexion inactive au bout
d'environ cinq minutes, d'où le `KeepAliveInterval` et la reconnexion avec attente
croissante dans `GabboSocket`.

**Une touche demande deux requêtes.** `POST /key` attend un `state="press"` puis un
`state="release"`. N'envoyer que le premier laisse la touche enfoncée côté enceinte.

**Les curseurs sont temporisés.** Un glissement produit des dizaines de valeurs par
seconde ; le serveur HTTP du firmware se bloque si on les lui envoie toutes. `Debouncer`
ne garde que la dernière après 220 ms de silence, et `PlayerViewModel` ignore les
notifications de volume pendant 1,5 s après une action locale — sinon le curseur saute
sous le doigt quand l'écho revient.

**`/api/status` renvoie du XML, pas du JSON.** L'agent relaie le corps `now_playing` du
firmware tel quel, derrière un micro-cache. C'est volontaire côté agent, et c'est pour ça
que `StrApiClient.GetStatusAsync` parse du XML là où le reste de la classe fait du JSON.

**Le repli est systématique.** Chaque commande de `SpeakerDevice` essaie l'agent puis
retombe sur le firmware. Une enceinte sans agent, ou avec un agent d'une version plus
ancienne, reste donc pilotable.

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

**Les pochettes de présélections sont mémorisées.** Un appel qui expire, ou une enceinte
momentanément occupée, renvoyait une liste sans image et la grille se vidait alors
qu'elle s'était affichée correctement juste avant. `PresetsViewModel` ne remplace donc
une image que par une autre image — jamais par du vide — et garde la dernière URL connue
par touche dans les préférences. Une tuile sans logo affiche un symbole selon le type
(📻, 🎵, 🗂) plutôt qu'un rectangle vide.

**La recherche de stations interroge radio-browser directement.** L'agent STR ne sert
plus `/api/radio/*` : depuis la v0.8 il ne compile même plus le client radio-browser,
c'est l'application qui interroge le service. `RadioBrowserClient` fait pareil, avec la
même liste de miroirs codée en dur et la même bascule à la première erreur — la
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

## Pistes d'extension

- Widget Android et contrôles sur l'écran de verrouillage (`MediaSession`).
- Découverte mDNS (`_soundtouch._tcp` et `_streborn._tcp`) via `NsdManager`, en complément
  du balayage — plus rapide, mais il faut prendre un `MulticastLock`.
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

Projet personnel, ni produit ni approuvé par Bose Corporation. « Bose » et
« SoundTouch » sont des marques de Bose Corporation. STR est publié sous licence MIT par
son auteur.

Le code a été écrit avec Claude (Anthropic).
