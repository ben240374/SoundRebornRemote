# SoundReborn Remote — Handbuch

Version 0.9.79 · Android 7 und neuer

> Sprachen: [Français](fr.md) · [English](en.md) · **Deutsch** · [Nederlands](nl.md) · [Español](es.md)

---

## Inhalt

1. [Was vorher da sein muss](#1-was-vorher-da-sein-muss)
2. [Installation](#2-installation)
3. [Erster Start](#3-erster-start)
4. [Der Reiter Wiedergabe](#4-der-reiter-wiedergabe)
5. [Der Reiter Presets](#5-der-reiter-presets)
6. [Der Reiter Quellen](#6-der-reiter-quellen)
7. [Der Reiter Gruppen](#7-der-reiter-gruppen)
8. [Der Reiter Einstellungen](#8-der-reiter-einstellungen)
9. [Gesten](#9-gesten)
10. [Was tun, wenn…](#10-was-tun-wenn)
11. [Bekannte Grenzen](#11-bekannte-grenzen)
12. [Hinweise](#12-hinweise)

---

## 1. Was vorher da sein muss

SoundReborn Remote steuert **Bose-SoundTouch-Lautsprecher** von einem Android-Telefon
oder -Tablet aus. Alles läuft im lokalen Netz: kein Konto, kein Onlinedienst, keine Daten,
die das Haus verlassen.

Drei Voraussetzungen:

- **Telefon und Lautsprecher im selben WLAN.** Vorsicht bei Gastnetzen und Routern, die
  Geräte voneinander abschotten — dann wird nichts gefunden.
- **Der STR-Agent auf den Lautsprechern.** Er ersetzt das, was die Bose-Cloud bis zu
  ihrer Abschaltung am 6. Mai 2026 geleistet hat. Ohne ihn funktioniert die App zwar
  weiter, aber beschränkt auf das, was allein die Firmware bietet: Lautstärke, Bass,
  bereits gespeicherte Tasten. Sendersuche, Tastenbelegung und zuverlässige Gruppen
  laufen alle über den Agenten.
  Installiert wird er mit dem STR-Werkzeug für PC oder Mac — <https://st-reborn.de>.
  **Diese App installiert ihn nicht**; sie weist lediglich auf Lautsprecher hin, denen er
  fehlt.
- **Android 7 oder neuer.**

---

## 2. Installation

Die App wird nicht über den Play Store verteilt. Sie steht auf der *Releases*-Seite des
Projekts: <https://github.com/ben240374/SoundRebornRemote/releases>

1. Lade die `.apk` auf das Telefon.
2. Öffne sie. Android fragt nach der Erlaubnis, aus einer unbekannten Quelle zu
   installieren — erteile sie der App, die heruntergeladen hat, meist dem Browser.
3. Installieren, dann öffnen.

Beim ersten Start wird keine besondere Berechtigung verlangt: Die App greift weder auf
Kontakte noch auf Standort noch auf Speicher zu.

---

## 3. Erster Start

Die App startet in der Sprache des Telefons, sofern sie sie kennt — Französisch,
Englisch, Deutsch, Niederländisch, Spanisch — sonst auf Englisch. Ändern lässt sich das
jederzeit.

Beim ersten Start ist kein Lautsprecher bekannt. Gehe zu **Einstellungen** und dann auf
**Lokales Netzwerk durchsuchen**.

![Einstellungen](../images/06-reglages.jpg)

Die Suche läuft in zwei Stufen. Zuerst bittet sie die Geräte, sich zu melden (mDNS) — der
schnelle Weg, zwei bis drei Sekunden. Antwortet niemand, fragt sie alle 254 Adressen des
Subnetzes einzeln auf Port 8090 ab: einige Sekunden länger, funktioniert aber auch dort,
wo Multicast gefiltert wird.

Gefundene Lautsprecher erscheinen unter **BEKANNTE LAUTSPRECHER**. Tippe bei dem
gewünschten auf **Verwenden**. Er wird gemerkt: Beim nächsten Start verbindet sich die
App von selbst wieder.

Findet der Suchlauf nichts, du kennst aber die IP-Adresse des Lautsprechers, trage sie in
das vorgesehene Feld ein und tippe auf **Hinzufügen**.

---

## 4. Der Reiter Wiedergabe

Der Hauptbildschirm, den man öffnet, um schnell etwas zu tun.

![Wiedergabe](../images/01-lecture.jpg)

### Die Wahl des Lautsprechers

Oben je eine Karte pro bekanntem Lautsprecher, mit Modell und Version des STR-Agenten.
Die blau umrandete Karte ist der gesteuerte Lautsprecher; tippe auf eine andere Karte, um
zu wechseln, ohne den Umweg über die Einstellungen.

Der kleine Punkt rechts zeigt die Verbindung: **direkt** heißt, der Lautsprecher meldet
Änderungen in Echtzeit, **Abfrage**, dass die App sie in Abständen neu liest. Im
Abfragemodus erscheint eine Änderung von einer anderen Fernbedienung erst nach ein, zwei
Sekunden.

### Gerade läuft

Cover, Titel und Quelle des Laufenden. Bei einem Radiosender dessen Logo und Name, bei
Spotify das Albumcover.

### Steuerung

Vier Tasten: Stopp, zurück, Wiedergabe/Pause, vor. Je nach Quelle bleiben manche ohne
Wirkung — ein Livestream lässt sich nicht zurückspulen.

Der grüne Balken **Standby** schaltet den Lautsprecher aus; ist er aus, heißt er
**Einschalten**.

### Lautstärke

Stumm, minus, Regler, plus. Der Prozentwert steht rechts.

Gehört der Lautsprecher zu einer Gruppe, wird dieser Regler zur **Lautstärke der ganzen
Gruppe**: Er verschiebt alle Lautsprecher um denselben Betrag und erhält damit das
Verhältnis, das du zwischen ihnen eingestellt hast. Die einzelnen Lautstärken bleiben im
Reiter Gruppen zugänglich.

### Bass und Presets

![Bass und Presets](../images/02-lecture-graves.jpg)

Der Bass reicht je nach Modell von -10 bis +10. **Eine Höhenregelung gibt es nicht**: Die
API des Lautsprechers bietet keine — das ist kein Versäumnis der App.

Die sechs Presets stehen hier noch einmal, um einen Reiterwechsel zu sparen. Die grün
umrandete Kachel ist die laufende.

### Gruppieren mit

Eine Schaltfläche je weiterem Lautsprecher. Tippen fügt ihn der Gruppe hinzu, ein Haken
erscheint. Erneut tippen nimmt ihn wieder heraus. Der gesteuerte Lautsprecher ist der
**Master**: Er sendet, die anderen folgen.

Der Link **Aktualisieren** unten liest alles neu: laufenden Titel, Cover, Lautstärke,
Zustand der Gruppe.

---

## 5. Der Reiter Presets

![Presets](../images/03-preselections.jpg)

### Die sechs Tasten

Dieselben wie die physischen Tasten am Lautsprecher. Jede Kachel zeigt Senderlogo und
Bitrate. Tippen startet die Wiedergabe; der grüne Rahmen markiert die laufende.

Logos werden heruntergeladen und danach lokal behalten. Fehlt eines, veröffentlicht der
Sender kein brauchbares Bild, oder sein Server antwortet nicht.

### Einen Sender finden

Das Suchfeld fragt **radio-browser.info** ab, ein offenes, von Freiwilligen gepflegtes
Verzeichnis. Zwei Auswahllisten grenzen auf Land und Sprache ein, und **Top-Liste** zeigt
die meistgehörten Sender, ohne etwas einzutippen.

Das Kästchen **Nur Bose-kompatible** ist standardmäßig gesetzt und sollte es bleiben: Es
filtert die Formate heraus, die SoundTouch-Lautsprecher nicht decodieren (Ogg, Opus,
FLAC) und die zu Stille ohne Fehlermeldung führen würden.

Tippe auf ein Ergebnis, dann öffnet sich ein Auswahlblatt mit

- **Anhören** — startet den Sender sofort, ohne etwas zu speichern;
- **① Dieser Taste zuweisen**, wenn die Taste frei ist, oder **① Bel RTL ersetzen**, wenn
  sie belegt ist — der Name des bereits gespeicherten Senders wird angezeigt, damit du
  keinen überschreibst, an dem dir lag.

**Das Zuweisen setzt den STR-Agenten voraus.** Ohne ihn ist nur sofortiges Anhören
möglich; gespeichert wird dann durch Gedrückthalten der Taste am Lautsprecher selbst oder
über die STR-Desktop-App.

---

## 6. Der Reiter Quellen

![Quellen](../images/04-sources.jpg)

**Bluetooth**, **AUX** und **Standby** oben: drei direkte Verknüpfungen.

**Spotify** wird nicht aus dieser App gesteuert. Öffne Spotify auf dem Telefon und wähle
den Lautsprecher in der Geräteauswahl (Spotify Connect): Er erscheint dort, sobald der
STR-Agent läuft.

**Quellen des Lautsprechers** listet auf, was der Lautsprecher meldet, mit Zustand:
`READY` heißt verfügbar, `UNAVAILABLE`, dass die Quelle zwar existiert, aber nicht nutzbar
ist — typischerweise ein Streamingdienst, dessen Konto seit der Cloud-Abschaltung nicht
mehr erreichbar ist.

**Einen Stream abspielen** nimmt die Adresse eines direkten Audiostreams entgegen — eine
`.mp3` oder eine `.m3u8` — und startet ihn auf dem Lautsprecher. Nützlich für ein
Webradio, das im Verzeichnis fehlt. Dafür ist der STR-Agent nötig.

**Zuletzt gehört** zeigt den vom Agenten geführten Verlauf: Tippe auf eine Zeile, um
erneut zu starten.

---

## 7. Der Reiter Gruppen

![Gruppen](../images/05-groupes.jpg)

**Master-Lautsprecher** nennt den sendenden. Um den Master zu wechseln, wechsle im Reiter
Wiedergabe den aktiven Lautsprecher.

**Zu gruppierende Lautsprecher**: Hake die an, die folgen sollen, dann **Gruppieren**.
**Auflösen** löst die ganze Gruppe auf.

Die Option **Dauerhafte Gruppe** bittet den Agenten, die Gruppe bei der nächsten
Wiedergabe wiederherzustellen, statt sie zerfallen zu lassen, wenn die Musik endet.

**Gruppenlautstärke** wirkt auf alle Lautsprecher zugleich. Unter diesem Regler hat jeder
Lautsprecher der Gruppe seinen eigenen, sodass sich die Küche leiser stellen lässt, ohne
das Wohnzimmer anzurühren. **Auf ganze Gruppe anwenden** setzt alle auf denselben Wert.

Ein Verhalten sollte man kennen: Der Gruppenregler **verschiebt** die Lautstärken und
erhält dabei die Abstände. Steht die Küche auf 60 und das Wohnzimmer auf 30, ergibt ein
Anheben um 10 die Werte 70 und 40 — nicht 70 und 35.

---

## 8. Der Reiter Einstellungen

![Bekannte Lautsprecher](../images/07-reglages-enceintes.jpg)

**Aktiver Lautsprecher**: Name, Adresse, Modell und Version des Agenten.

**Sprache**: Französisch, Englisch, Deutsch, Niederländisch, Spanisch. Die Änderung wirkt
sofort, ohne Neustart, und wird behalten.

**Design**: System, Hell oder Dunkel. „System" folgt der Anzeigeeinstellung des Telefons,
einschließlich der automatischen Umschaltung am Abend.

**Suche**: der Suchlauf und die manuelle Eingabe per IP-Adresse.

**Bekannte Lautsprecher**: die gemerkte Liste. **Verwenden** wechselt dorthin, **✕**
vergisst den Eintrag. Ein Lautsprecher mit dem Hinweis **„Ohne STR — bereit zur
Installation"** antwortet einwandfrei, hat aber keinen Agenten; weiter unten erscheint
dann ein Erklärungsblock mit einem Link zur Seite des STR-Projekts.

**Diagnose**: *Aktiven Lautsprecher abfragen* zeigt, was der Lautsprecher tatsächlich
meldet — Name, Modell, Geräte-ID, Firmwareversion, Vorhandensein des Agenten, Zustand der
Benachrichtigungen, Bassbereich, Liste der erkannten API-Endpunkte. Das ist die Angabe,
die man einer Fehlermeldung beilegt. *Logo-Cache leeren* erzwingt das erneute
Herunterladen der Senderbilder.

**Über**: die Version, der Dank an das STR-Projekt mit einem Link zu dessen Seite in der
gewählten Sprache, und die rechtlichen Hinweise.

---

## 9. Gesten

**Waagerechtes Wischen** wechselt zum nächsten oder vorherigen Reiter: Wiedergabe →
Presets → Quellen → Gruppen → Einstellungen. Die Geste muss zügig sein; ein langsames
Ziehen wird nicht als Reiterwechsel gelesen.

Ein Wischen, das **auf einem Regler** beginnt, gehört dem Regler: Das ist gewollt, sonst
ließe sich die Lautstärke nicht mehr einstellen. Beginne die Geste auf einer freien
Fläche.

---

## 10. Was tun, wenn…

**Der Suchlauf findet keinen Lautsprecher.**
Prüfe, ob das Telefon im selben WLAN ist wie die Lautsprecher und nicht im Gastnetz oder
im Mobilfunk. Manche Router schotten Geräte voneinander ab — die Einstellung heißt
meist *AP-Isolation* oder *Client-Modus*. Kennst du die IP-Adresse, trage sie in den
Einstellungen von Hand ein.

**Der Lautsprecher erscheint, reagiert aber nicht.**
Wahrscheinlich ist er im Tiefschlaf. Wecke ihn mit der Ein-/Aus-Schaltfläche im Reiter
Wiedergabe oder drücke eine Taste am Gerät.

**Ein Preset bewirkt nichts.**
Eine leere Taste hat nichts zu starten. Prüfe außerdem, ob der Lautsprecher aus ist.

**Es werden keine Senderlogos angezeigt.**
Leere den Logo-Cache in der Diagnose. Bleibt es bei einem bestimmten Sender, liegt es an
dessen eigenem Bildserver.

**Bei einem Lautsprecher fehlt die Agentenversion.**
Entweder ist der Agent dort nicht installiert, oder er ist zu alt, um seine Versionsnummer
zu veröffentlichen. Starte den Suchlauf erneut.

**Die Gruppe bildet sich nicht.**
Beide Lautsprecher müssen eingeschaltet und erreichbar sein. Fehlt einem der STR-Agent,
läuft das Gruppieren über die Firmware — weniger zuverlässig und mitunter ohne Meldung
abgelehnt.

**Die App ist in der falschen Sprache.**
Einstellungen → Sprache. Die Wahl hat Vorrang vor der Sprache des Telefons.

---

## 11. Bekannte Grenzen

- **Keine Höhenregelung.** Die API des Lautsprechers bietet keine.
- **Die App installiert den STR-Agenten nicht.** Das verlangt Systemzugriff auf den
  Lautsprecher und einen Neustart; dafür ist das STR-Werkzeug für PC oder Mac da.
- **Spotify wird aus Spotify gesteuert**, über Spotify Connect.
- **Kein Durchsuchen einer DLNA-Bibliothek**, bislang.
- **Nur Android.** Eine iOS-Fassung gibt es nicht.

---

## 12. Hinweise

SoundReborn Remote ist ein privates Projekt unter MIT-Lizenz.

Es wird **weder von der Bose Corporation hergestellt noch von ihr unterstützt**. „Bose"
und „SoundTouch" sind Marken der Bose Corporation und werden hier nur genannt, um die
Kompatibilität anzugeben.

Es ist **unabhängig vom STR-Projekt**: von ihm weder entwickelt noch gepflegt noch
unterstützt. Probleme mit dieser App bitte
[im eigenen Repository](https://github.com/ben240374/SoundRebornRemote/issues) melden,
nicht beim STR-Projekt.

Der Agent **STR (SoundTouch Reborn)** stammt von Jens Roggenfelder
([JRpersonal](https://github.com/JRpersonal/streborn)) — <https://st-reborn.de>. Ohne ihn
hätte diese App nichts zu steuern.

Das Senderverzeichnis stellt [radio-browser.info](https://www.radio-browser.info) bereit.

Der Code wurde mit Claude (Anthropic) geschrieben.
