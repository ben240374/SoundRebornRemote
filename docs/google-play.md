# Publier SoundReborn Remote sur Google Play

Relecture des règles du Programme pour les développeurs Google Play
(<https://play.google/developer-content-policy/>), faite le 22 novembre 2026 pour la
version 1.9.1. Les règles changent : relire les pages citées avant de soumettre.

## En bref

| Point | État | Action |
|---|---|---|
| Mise à jour par l'application elle-même | Interdit sur Play | Fait : version Play sans mise à jour GitHub (`-p:Store=play`) |
| Enregistrement de radios | Risque de refus | Décision à prendre (voir plus bas) |
| Marques Bose / SoundTouch | Acceptable si bien présenté | Fiche sans logo Bose, mention « non affilié » |
| Micro (message vocal) | Conforme | Formulaire « Sécurité des données » à remplir |
| Règles de confidentialité | Obligatoire | Fait : `docs/privacy.html` |
| API cible | Android 16 (API 36) exigé | À vérifier dans la Play Console |
| Format | AAB exigé | `-p:AndroidPackageFormat=aab` |
| Clé de signature | Choix important | Garder ta propre clé (voir plus bas) |
| Nouveau compte personnel | Test fermé obligatoire | 12 testeurs pendant 14 jours |
| Accès pour l'examen | L'appli demande une enceinte | Vidéo et explications pour l'équipe d'examen |

## 1. Mise à jour depuis GitHub : interdite sur Play

Règle « Device and Network Abuse » : une application distribuée par Google Play ne peut
pas se modifier, se remplacer ni se mettre à jour autrement que par le mécanisme de
Google Play. Et la permission `REQUEST_INSTALL_PACKAGES` « ne peut pas servir à des mises
à jour de l'application par elle-même ».

**Fait dans la 1.9.1.** La version Play se construit avec :

```powershell
dotnet publish src\SoundRebornRemote.App\SoundRebornRemote.App.csproj -f net10.0-android -c Release `
  -p:Store=play -p:AndroidPackageFormat=aab `
  -p:AndroidKeyStore=true -p:AndroidSigningKeyStore=<chemin>\soundreborn.keystore `
  -p:AndroidSigningKeyAlias=soundreborn `
  -p:AndroidSigningKeyPass='<mot de passe>' -p:AndroidSigningStorePass='<mot de passe>'
```

Avec `Store=play`, la constante `PLAY_STORE` retire la carte « Mise à jour », la bannière,
l'installateur, son FileProvider et la permission `REQUEST_INSTALL_PACKAGES`. Après la
première compilation, contrôle le manifeste du bundle : la permission ne doit pas y figurer.

## 2. Enregistrement de radios : le vrai risque

Règle « Propriété intellectuelle » : Play refuse les applications de streaming « qui
permettent de télécharger une copie locale de contenu protégé sans autorisation ».
L'enregistrement de 5 minutes d'une radio internet tombe dans cette zone, même pour un
usage privé. Il demande aussi de déclarer le service au premier plan `dataSync`, avec une
vidéo, alors que les cas d'usage listés par Google pour `dataSync` (sauvegarde,
import/export, traitement local) ne décrivent pas l'enregistrement d'un flux.

Deux choix :

- **Retirer l'enregistrement de la version Play** avec le même interrupteur `PLAY_STORE`.
  Il disparaît alors avec les permissions `FOREGROUND_SERVICE`,
  `FOREGROUND_SERVICE_DATA_SYNC` et `WAKE_LOCK`, et la déclaration vidéo. La version
  GitHub le garde. C'est ce que je recommande.
- Le garder et accepter le risque d'un refus, voire d'une suspension de l'application.

## 3. Marques Bose et SoundTouch

Le nom « SoundReborn Remote » et l'icône ne reprennent rien de Bose : c'est bien. Dans la
fiche Play :

- citer Bose SoundTouch seulement pour dire la compatibilité (« pour enceintes Bose
  SoundTouch équipées de l'agent STR ») ;
- aucun logo Bose, aucune photo officielle de Bose ;
- la phrase du site : « Projet personnel, sans lien avec Bose Corporation. « Bose » et
  « SoundTouch » sont des marques de Bose Corporation, citées pour indiquer la
  compatibilité. »

## 4. Micro, données et formulaire « Sécurité des données »

Le micro est demandé au premier appui sur le bouton du message vocal, et la carte dit
maintenant en clair ce qu'il fait : il n'enregistre que pendant l'appui, le son part vers
l'enceinte sur le réseau local, puis il est effacé. L'usage est donc dans l'attente
raisonnable de l'utilisateur ; la « divulgation bien visible » renforcée n'est exigée que
pour une collecte inattendue.

Dans le formulaire « Sécurité des données », déclarer par prudence :

- **Audio / enregistrements vocaux** : collectés, non partagés, traités de façon éphémère,
  à la demande de l'utilisateur, pour une fonction de l'application ;
- **Messages / texte saisi** (texte lu par l'enceinte) : il passe par Google via l'agent
  STR ; le déclarer comme partagé pour une fonction de l'application ;
- rien d'autre : pas de compte, pas d'identifiant publicitaire, pas d'analyse d'usage ;
- chiffrement en transit : non pour le réseau local (HTTP vers l'enceinte), à expliquer.

**Règles de confidentialité** : URL à donner dans la Play Console,
<https://ben240374.github.io/SoundRebornRemote/privacy.html> (fichier `docs/privacy.html`,
français et anglais).

## 5. Exigences techniques

- **API cible** : depuis le 31 août 2026, une nouvelle application doit cibler Android 16
  (API 36). `net10.0-android` cible normalement l'API 36 ; la Play Console le dira au
  premier envoi.
- **Pages mémoire de 16 Ko** : exigées pour les applications qui ciblent Android 15 et
  plus. Le rapport du bundle dans la Play Console signale un problème éventuel.
- **Format** : une nouvelle application s'envoie en bundle AAB, pas en APK.

## 6. Clé de signature : garder la même que GitHub

Par défaut, Play App Signing génère sa propre clé et re-signe l'application. Les
installations Play et GitHub auraient alors deux signatures différentes : passer de l'une
à l'autre obligerait à désinstaller, donc à perdre les favoris « Local ». À la création
de l'application, choisis d'**utiliser ta propre clé** (`soundreborn.keystore`, exportée
avec l'outil PEPK que la console propose). La clé d'importation peut être une autre clé.

## 7. Compte et lancement

- **Compte personnel récent** : avant la production, test fermé avec au moins
  **12 testeurs** inscrits sans interruption pendant **14 jours**.
- **Vérification de l'identité** : demandée à l'ouverture du compte Play. La même identité
  sert à la vérification des développeurs Android : les applications installées hors Play
  devront venir d'un développeur vérifié, à partir de 2027 dans le monde entier. Avec un
  compte Play, ton paquet `be.barbaix.soundrebornremote` est enregistré, ce qui couvre aussi
  l'APK de GitHub.
- **Accès pour l'examen** : l'équipe d'examen n'a pas d'enceinte SoundTouch. Dans « Accès à
  l'application », expliquer qu'il faut une enceinte Bose SoundTouch avec l'agent STR sur
  le même réseau, et joindre une courte vidéo de l'application en marche. Sans cela,
  l'application risque d'être refusée pour « fonctionnalités inutilisables ».
- **Classification du contenu**, **public cible** (pas les enfants), **annonces** (aucune) :
  questionnaires de la Play Console.

## 8. Mises à jour dans la version Play

Le Play Store met déjà à jour l'application tout seul. Pour la proposer depuis l'application
elle-même, la voie autorisée est l'API « In-App Updates » de Google Play (bibliothèque Play
Core, paquet NuGet `Xamarin.Google.Android.Play.App.Update`) : l'application demande au Play
Store si une version plus récente existe, puis la télécharge et l'installe par lui. Elle ne
répond que pour une application installée depuis Play, et ne s'essaie qu'avec une version
publiée sur un canal de test. À ajouter à la version Play quand le compte sera ouvert.

## Sources

- Device and Network Abuse : <https://support.google.com/googleplay/android-developer/answer/9888379>
- REQUEST_INSTALL_PACKAGES : <https://support.google.com/googleplay/android-developer/answer/12085295>
- Propriété intellectuelle : <https://support.google.com/googleplay/android-developer/answer/9888072>
- Données utilisateur : <https://support.google.com/googleplay/android-developer/answer/10144311>
- Services au premier plan : <https://support.google.com/googleplay/android-developer/answer/13392821>
- API cible : <https://support.google.com/googleplay/android-developer/answer/11926878>
- Test pour les nouveaux comptes personnels : <https://support.google.com/googleplay/android-developer/answer/14151465>
- Vérification des développeurs Android : <https://developer.android.com/developer-verification/guides>
