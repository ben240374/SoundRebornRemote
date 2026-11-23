# SoundReborn Remote — Handbuch

Version 1.9.2 · Android 7 und neuer

> Sprachen: [Français](fr.md) · [English](en.md) · **Deutsch** · [Nederlands](nl.md) · [Español](es.md) · [Italiano](it.md)

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

Vier Voraussetzungen:

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
- **Ein STR-Agent 1.8.0 oder neuer, um Gruppen zu ändern.** Bei einem älteren Agenten
  weist die App darauf hin und lässt die Gruppen unverändert; alles andere funktioniert.
- **Android 7 oder neuer.**

---

## 2. Installation

Die App wird nicht über den Play Store verteilt. Sie steht auf der *Releases*-Seite des
Projekts: <https://github.com/ben240374/SoundRebornRemote/releases>

1. Lade die `.apk` auf das Telefon.
2. Öffne sie. Android fragt nach der Erlaubnis, aus einer unbekannten Quelle zu
   installieren — erteile sie der App, die heruntergeladen hat, meist dem Browser.
3. Installieren, dann öffnen.

Danach meldet die App neue Versionen selbst und kann sie installieren (Tab Einstellungen,
**Aktualisierung**).

Beim ersten Start wird keine besondere Berechtigung verlangt: Die App greift weder auf
Kontakte noch auf Standort noch auf Speicher zu.

---

## 3. Erster Start

Die App startet in der Sprache des Telefons, sofern sie sie kennt — Französisch,
Englisch, Deutsch, Niederländisch, Spanisch, Italienisch — sonst auf Englisch. Ändern lässt sich das
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

Wenn Spotify läuft und der STR-Agent Version 1.8.1 oder neuer hat, erscheinen unter dem
Titel zwei Tasten: **Gefällt mir** fügt den laufenden Song zu den Lieblingssongs hinzu,
**Gefällt mir nicht** entfernt ihn, wie die Daumentasten der Fernbedienung. Der Song landet
im Spotify-Konto, mit dem der **Lautsprecher** angemeldet ist, nicht unbedingt in dem auf
deinem Telefon. Ein Lautsprecher mit Display zeigt „LIKED“ oder „REMOVED“.

Während einer Spotify-Taste (ein Preset, das eine Spotify-Playlist spielt) kennt der
Lautsprecher nur den Namen der Playlist. Mit dem STR-Agenten 1.8.2 oder neuer zeigt die App
stattdessen den Titel, den Interpreten und das Cover, mit „Spotify · Playlist“ als Quelle;
der Titel wird etwa alle fünf Sekunden neu gelesen.

Bei einem Internetradio zeigt die App außerdem den Titel, den der Sender ansagt, so wie
das Display des Lautsprechers: den Titel groß, den Interpreten darunter und „Sender ·
Internetradio“ als Quelle. Ein Sender, der nichts schickt oder nur seinen eigenen Namen,
behält die übliche Anzeige; ein Text ohne Interpret (eine Sendung, Werbung) wird so
gezeigt, wie er ist. Der Titel wird etwa alle fünf Sekunden neu gelesen.

### Steuerung

Vier Tasten: Stopp, zurück, Wiedergabe/Pause, vor. Je nach Quelle bleiben manche ohne
Wirkung — ein Livestream lässt sich nicht zurückspulen.

Der grüne Balken **Standby** schaltet den Lautsprecher aus; ist er aus, heißt er
**Einschalten**.

Unter dem Balken schickt **Sleeptimer** den Lautsprecher nach 15 bis 120 Minuten in den
Standby. Solange er läuft, zeigt der Link die Restzeit; tippe darauf, um die Dauer zu
ändern oder abzubrechen. In einer Gruppe fragt die App, ob die ganze Gruppe oder nur der
ausgewählte Lautsprecher ausgehen soll. Der Timer lebt im STR-Agenten des Lautsprechers:
Er läuft weiter, wenn du die App schließt, ein Neustart des Lautsprechers bricht ihn aber
ab. Der Link erscheint nur, wenn der Agent den Timer kennt.

### Lautstärke

Stumm, Angleichen, Minus, Regler, Plus. Der Prozentwert steht rechts.

Gehört der Lautsprecher zu einer Gruppe, wird dieser Regler zur **Lautstärke der ganzen
Gruppe**: Er verschiebt alle Lautsprecher um denselben Betrag und erhält damit das
Verhältnis, das du eingestellt hast. Küche auf 60, Wohnzimmer auf 30 — die Gruppe um 10
angehoben ergibt 70 und 40, nicht 70 und 35.

Die Taste **=** zwischen Stumm und Minus macht das Gegenteil: Sie setzt *alle*
Lautsprecher der Gruppe auf den angezeigten Wert. Gruppe auf 25, alle auf 25. Sie
erscheint nur, solange eine Gruppe aktiv ist — allein gibt es nichts anzugleichen.

Unter dem Regler führt **Pro Lautsprecher** jedes Mitglied der Gruppe mit eigenem Regler
und eigenen Tasten − und + auf, sodass du die Küche leiser stellen kannst, ohne das
Wohnzimmer anzurühren. Ein ausgeschalteter Lautsprecher wird blass dargestellt und
reagiert nicht: Ein Befehl an ihn ließe die App nur vergeblich warten.

### Bass und Presets

![Bass und Presets](../images/02-lecture-graves.jpg)

Der Bass reicht je nach Modell von -10 bis +10. **Eine Höhenregelung gibt es nicht**: Die
API des Lautsprechers bietet keine — das ist kein Versäumnis der App.

Die sechs Presets stehen hier noch einmal, um einen Reiterwechsel zu sparen. Die grün
umrandete Kachel ist die laufende.

Wenn der STR-Agent die zweite Reihe unterstützt, erscheinen über den Kacheln zwei
Schaltflächen, **Einmal** und **Zweimal**: Die zweite zeigt die sechs Sender, die der
Lautsprecher startet, wenn eine Taste zweimal gedrückt wird. Die Wahl gilt für beide
Reiter. Jede Kachel trägt oben den Namen ihrer Taste („Taste 1“ bis „Taste 6“, in beiden Reihen),
und die Schaltfläche **Zweimal** zeigt eine kleine Zahl: wie viele Tasten der zweiten Reihe
einen Sender tragen.

Rechts neben den Schaltflächen ersetzt **Lokal** die Kacheln durch deine lokalen
Presets, die auf dem Telefon bleiben (siehe Reiter Presets); **Einmal** bringt die Tasten
des Lautsprechers zurück. Auf einem schmalen Bildschirm rücken **Lokal** und **★ STR** in eine
zweite Zeile, unter **Einmal** und **Zweimal**.

Auch die Schaltfläche **Einmal** zeigt eine kleine Zahl: wie viele Tasten der ersten
Reihe einen Sender tragen.

**Laufendes auf eine Taste legen**, unter den Kacheln, speichert das, was der
Lautsprecher gerade spielt — Sender, Album oder Titel aus der Bibliothek — auf der Taste
deiner Wahl. Spotify, AUX und Bluetooth lassen sich so nicht speichern.

### Sprachnachricht

Die Karte **Sprachnachricht** lässt den Lautsprecher sprechen und danach fortsetzen, was
er gespielt hat.

- **Deine Stimme**: halte die runde Mikrofontaste gedrückt, während du sprichst, und lass
  sie zum Senden los (höchstens 1 Minute). Während der Aufnahme wird die Taste rot. Das
  Telefon behält die Nachricht, bis der Lautsprecher sie abholt, und löscht sie dann.
- **Ein Text**: tippe ihn unter der Taste ein, dann **Vorlesen**. Der Lautsprecher liest ihn
  mit der Google-Stimme in der Sprache der App; er braucht Internetzugang, und der Text
  läuft über Google.
- **Behalten** legt den getippten Text in die Liste der gespeicherten Texte (höchstens
  zehn). Tippe auf einen gespeicherten Text, um ihn vorlesen zu lassen; **✎** bearbeitet
  ihn, **✕** löscht ihn nach Bestätigung.

Eine kurze Vibration zeigt an, dass das Mikrofon läuft: Sprich ab dann. Die App fügt vor
und nach deiner Nachricht etwas Stille ein, damit der Lautsprecher, der seinen Verstärker
verzögert öffnet, den Anfang nicht verschluckt. Ein Text hat höchstens 200 Zeichen.

Der Regler **Lautstärke der Durchsagen** stellt die Lautstärke der Nachricht ein, Stimme
und Text: ganz links behält der Lautsprecher seine Lautstärke; sonst wechselt er für die
Nachricht auf diese Lautstärke und kehrt danach zur vorherigen zurück. Das Mikrofon des
Telefons lässt sich nicht einstellen: Für eine lautere Nachricht schiebe diesen Regler hoch.

Die Nachricht geht an den Lautsprecher, der die Befehle erhält. Auf allen Lautsprechern
einer Gruppe läuft sie noch nicht: Das ist im STR-Agenten geplant. Beim ersten Mal fragt
Android nach dem Zugriff auf das Mikrofon. Die Karte erscheint nur mit dem STR-Agenten.

### Gruppieren mit

Ein Plättchen je Lautsprecher, außer dem, der die Befehle erhält. Tippe darauf, um den
Lautsprecher der Gruppe hinzuzufügen; ein Haken und ein grüner Rand erscheinen. Noch
einmal tippen nimmt ihn wieder heraus.

Das Gruppieren dauert einige Sekunden: Währenddessen zeigt der Block *Gruppierung mit … läuft…*, danach das Ergebnis — oder den Fehler, in Rot.

**Der Master wird ermittelt, nicht angenommen.** In einer Gruppe gibt ein Lautsprecher
wieder, die anderen folgen ihm. Wählst du einen folgenden Lautsprecher, sagt die App es
— *Gruppiert mit …*, in Bernstein — und schickt Transport, Presets und
Lautstärke an den Master, denn er ist es, der wiedergibt. Einen Lautsprecher zu wählen
unterbricht also nie, was gerade läuft. Standby und Sleeptimer sind die Ausnahme: Sie
wirken auf den ausgewählten Lautsprecher.

Auf einem folgenden Lautsprecher ist das angehakte Plättchen dieser Lautsprecher selbst:
Tippen verlässt die Gruppe. Erweitert wird die Gruppe vom Master aus, denn er stellt sie
zusammen.

**Einen Lautsprecher auszuschalten nimmt ihn zuerst aus der Gruppe** und löst die Gruppe
auf, wenn es der letzte war. Beim Wiedereinschalten steht er allein da, gibt wieder, was
er wiederzugeben hat, und kann erneut gruppiert werden.

Der Link **Aktualisieren** unten liest alles neu: laufender Titel, Cover, Lautstärke,
Zustand der Gruppe.

Spielt der Lautsprecher, den du hinzufügst, etwas anderes oder ist er pausiert, sagt die
App das vor dem Gruppieren: „Salon spielt Radio Contact. Beim Hinzufügen zur Gruppe endet
das, und Bel RTL läuft.“ Nichts ändert sich, bis du **Hinzufügen** tippst. Ein Lautsprecher
im Standby, oder einer, der schon dasselbe spielt, wird ohne Rückfrage hinzugefügt.

---

## 5. Der Reiter Presets

![Presets](../images/03-preselections.jpg)

### Die sechs Tasten

Dieselben wie die physischen Tasten am Lautsprecher. Jede Kachel zeigt Senderlogo und
Bitrate. Tippen startet die Wiedergabe; der grüne Rahmen markiert die laufende.

Logos werden heruntergeladen und danach lokal behalten. Fehlt eines, veröffentlicht der
Sender kein brauchbares Bild, oder sein Server antwortet nicht.

### Einmal, zweimal

Mit einem aktuellen STR-Agenten (ab v1.6) trägt jede Taste des Lautsprechers zwei
Sender: Ein Druck startet den ersten, zwei schnelle Drücke den zweiten. Die
Schaltflächen **Einmal** und **Zweimal** wählen die angezeigte Reihe; die Kacheln
behalten ihre Tastennummer, ① bis ⑥.

Die zweite Reihe existiert nur im Agenten: Auf einem Lautsprecher ohne Agent oder mit
einem älteren erscheinen die Schaltflächen nicht.

Eine Taste, die nur in der zweiten Reihe einen Sender hat, erscheint unter **Einmal**
leer: Ein einfacher Druck am Lautsprecher startet dann den Sender der zweiten Reihe
(STR-Agent 1.6.3 und neuer).

### Lokale Presets

Die Schaltfläche **Lokal** rechts zeigt Presets, die auf dem Telefon bleiben, zusätzlich zu
den Tasten des Lautsprechers und in beliebiger Zahl (höchstens 100). Tippe auf eine Zeile,
um den Sender auf dem gewählten Lautsprecher zu starten; **✎** benennt ihn um, **✕**
entfernt ihn nach Bestätigung. Die Zeile des laufenden Senders ist grün.

Zum Hinzufügen:

- **Laufenden Sender hinzufügen** behält den Sender, den der Lautsprecher gerade spielt,
  mit der ursprünglichen Adresse des Streams und seinem Logo;
- **Adresse hinzufügen** fragt nach der http- oder https-Adresse des Streams, dann nach
  einem Namen;
- in der Suche einen Sender antippen, dann **Zu Lokal hinzufügen**.

Diese Presets bleiben auf diesem Telefon: Die Tasten des Lautsprechers kennen sie nicht,
und ein anderes Telefon sieht sie nicht. Eine Spotify-Playlist lässt sich dort nicht
ablegen: Der STR-Agent startet eine Playlist nur von einer seiner Tasten. Um sie bei einer Neuinstallation zu behalten, exportiere sie im Reiter Einstellungen (**Sicherung der Favoriten**).

### Eine Taste umbenennen und löschen

Jede Kachel trägt oben den Namen ihrer Taste. Bei einer Taste, die der STR-Agent gespeichert
hat, stehen daneben zwei kleine Schaltflächen: **✎** benennt die Taste um, **✕** löscht sie
nach Rückfrage. Umbenennen ändert nur den angezeigten Namen: Sender, Bild und die Playlist
einer Spotify-Taste bleiben. Ein Name hat höchstens 64 Zeichen. Die Schaltfläche **Zweimal**
zeigt, wie viele Tasten der zweiten Reihe einen Sender tragen.

Eine Taste, die nur die Firmware des Lautsprechers kennt, und eine leere Taste haben diese
Schaltflächen nicht: Der Agent hätte nichts umzubenennen oder zu löschen.

### Tasten auf einen anderen Lautsprecher kopieren

Die Karte **Tasten kopieren** kopiert alle Tasten des gewählten Lautsprechers auf einen
anderen STR-Lautsprecher oder auf alle. Tippe auf **Kopieren nach…**, wähle das Ziel und
bestätige. Jede hier belegte Taste ersetzt die des Ziels; die übrigen Tasten des Ziels
bleiben. Beide Reihen gehen an ein Ziel, das zwei hat. Gehört eine Spotify-Taste zur
Kopie, wird das Spotify-Konto dieses Lautsprechers an das Ziel übertragen: Die Rückfrage
weist darauf hin. Das Ergebnis erscheint pro Lautsprecher.

Das Kopieren braucht den STR-Agenten 1.6.5 oder neuer auf dem gewählten Lautsprecher.

### STR-Favoriten

Mit einem neuen STR-Agenten zeigt die Schaltfläche **★ STR**, rechts neben **Lokal**
(Reiter Wiedergabe und Presets), anstelle der Tasten die mit Stern markierten Sender der
STR-App am Computer. Sie sind auf allen STR-Lautsprechern dieselben: ein hier
hinzugefügter oder entfernter Favorit wird es einige Sekunden später auch auf den anderen
Lautsprechern und am Computer. **Einmal** oder **Zweimal** holt die Tasten zurück.

- Antippen spielt den Favoriten auf dem ausgewählten Lautsprecher.
- **⋯** bietet **Auf eine Taste legen…**, **Zu Lokal hinzufügen** und **Aus STR-Favoriten
  entfernen** (nach Bestätigung).
- **★ Laufendes hinzufügen** übernimmt das Radio, das der Lautsprecher spielt; in der Suche
  bietet **⋯ Tasten, Local…** **★ Zu STR-Favoriten**.
- Ein Pandora-Sender wird angezeigt, startet aber über die STR-App.

### Einen Sender finden

Das Suchfeld fragt **radio-browser.info** ab, ein offenes, von Freiwilligen gepflegtes
Verzeichnis. Die Suche folgt der der STR-Desktop-App:

- **Land** (alle in alphabetischer Reihenfolge, Überseegebiete eingeschlossen:
  Martinique, Réunion, Guadeloupe…) und **Sprache** grenzen die Suche ein; **Sortieren** ordnet nach Beliebtheit
  (Stimmen), Aufrufen, Trend, Name oder Bitrate. Eine Änderung startet die letzte Suche
  neu.
- Die **Genre-Chips** (Alle, Pop, Rock, Hits, Jazz, 80er…) filtern nach Genre; die ersten
  beiden hängen vom Land ab (Schlager und Deutschrap für Deutschland). **Mehr Genres**
  fügt die häufigsten Genres des Verzeichnisses hinzu. Ohne Eingabe durchstöbert ein Chip
  das Verzeichnis, wie die Top-Liste.
- **Top-Liste** zeigt die Sender mit den meisten Stimmen, mit diesen Filtern.
- Eine Suche aus mehreren Wörtern sucht diese Wörter auch in den Genres („rock belge“) und
  findet einen Namen mit anderer Zeichensetzung („Mi Soul“ findet „Mi-Soul“).
- Eine ins Feld eingefügte Stream-Adresse (http://… oder https://…) findet den Sender des
  Verzeichnisses, der sie sendet; eine unbekannte erscheint als **Eingefügte Adresse**,
  die du wie jeden anderen Sender abspielen oder ablegen kannst.
- **Mehr anzeigen** unter den Ergebnissen hängt die nächste Seite an.

**Nur erreichbare** (standardmäßig gesetzt) blendet die Sender aus, die das Verzeichnis bei
der letzten Prüfung nicht erreicht hat. Ein gerade eingetragener, noch nicht geprüfter
Sender bleibt sichtbar. Findet ein Name nur als nicht erreichbar gemeldete Sender, werden
sie trotzdem gezeigt, mit einem Hinweis: Das Verzeichnis prüft vom Ausland aus, und ein
Sender, der nur im eigenen Land sendet, wirkt dort tot, spielt bei dir aber einwandfrei.

**Nur Bose-kompatible** (standardmäßig gesetzt, so lassen) filtert die Formate heraus, die
SoundTouch-Lautsprecher nicht decodieren (Ogg, Opus, FLAC) und die zu Stille ohne
Fehlermeldung führen würden. **Bitrate** blendet Sender unter einer gewählten Bitrate aus;
Sender ohne bekannte Bitrate bleiben. Beide Filter wirken auf die erhaltene Liste, ohne
neue Suche, und die Zahl der ausgeblendeten Sender steht unter der Ergebnisüberschrift.

**Fehlt ein Sender?** Der Link unter den Ergebnissen öffnet radio-browser.info, wo jeder
ihn eintragen kann: Dann findest du ihn hier, und alle anderen auch.

Tippe auf ein Ergebnis: Die Zeile öffnet sich und bietet drei eindeutige Möglichkeiten.

- **📱 ▶ Auf diesem Telefon anhören** (oder **diesem Tablet**): Der Sender spielt auf dem
  Gerät, damit du ihn hörst, bevor er auf den Lautsprecher geht. Der Knopf wird zu
  **📱 ■ Stoppen**; eine Leiste über den Ergebnissen zeigt, was spielt, mit **■ Stoppen**
  und **🔊 Auf … abspielen**. Immer nur ein Sender; das Anhören endet beim Verlassen des
  Tabs oder der App und wenn der Sender auf den Lautsprecher geht. Ohne WLAN nutzt es
  mobile Daten (etwa 1 MB pro Minute bei 128 kbit/s). Ein Sender, der auf dem Gerät
  nicht spielt, kann auf dem Lautsprecher trotzdem laufen: Der STR-Agent wandelt Formate
  um, die der Android-Player nicht liest.
- **🔊 Auf Küche abspielen**: mit dem vollen Namen des gewählten Lautsprechers; der Sender
  startet dort, ohne etwas zu speichern.
- **⋯ Tasten, Local…** öffnet ein Auswahlblatt mit **Auf Küche abspielen**, **★ Zu STR-Favoriten** (neuer Agent), **Zu Local
  hinzufügen**, dann **① Dieser Taste zuweisen**, wenn die Taste frei ist, oder
  **① Bel RTL ersetzen**, wenn sie belegt ist — der Name des bereits gespeicherten Senders
  wird angezeigt, damit du keinen überschreibst, an dem dir lag.

**Das Zuweisen setzt den STR-Agenten voraus.** Ohne ihn ist nur sofortiges Anhören
möglich; gespeichert wird dann durch Gedrückthalten der Taste am Lautsprecher selbst oder
über die STR-Desktop-App.

Mit der zweiten Reihe bietet das Menü zwölf Tasten an: **①** bis **⑥**, dann **①×2**
bis **⑥×2**. Ein Sender liegt immer nur auf einer Taste: Ist er schon woanders
gespeichert, lehnt der Agent ab, und die App nennt die Taste, auf der er liegt.

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

**Aufnehmen** speichert einen Ausschnitt des Internetradios, das der Lautsprecher
spielt, auf dem Telefon – zum erneuten Anhören oder zum Versenden (WhatsApp, E-Mail…).
Tippe auf **Aufnehmen**: Die Aufnahme endet, wenn du **Stoppen** tippst, oder von selbst
nach 5 Minuten. Sie läuft bei ausgeschaltetem Bildschirm weiter: Eine Benachrichtigung
„Aufnahme läuft“ zeigt den Zähler und eine Taste **Stoppen**. Bei der ersten Aufnahme fragt
Android 13 und neuer, ob Benachrichtigungen erlaubt sind; lehnst du ab, wird trotzdem
aufgenommen, nur ohne Benachrichtigung.

Aufgenommen werden nur Internetradios, deren Stream erreichbar ist: STR-Speicherplätze,
im Verzeichnis gefundene Sender, von Hand gestartete Streams. Spotify, Deezer, Bluetooth
und AUX laufen nicht über das Telefon und lassen sich nicht aufnehmen, HLS-Streams
(`.m3u8`) ebenfalls nicht. Das Telefon öffnet den Stream des Senders parallel zum
Lautsprecher: Die Wiedergabe wird nicht gestört, aber die Aufnahme verbraucht Daten, etwa
1 MB pro Minute.

Die Liste behält die letzten 10 Aufnahmen mit Sender, Datum und Dauer. ▶ spielt sie auf dem
Telefon ab, ⤴ öffnet das Teilen-Menü von Android, der Papierkorb löscht nach Rückfrage. Bei
10 Aufnahmen verweigert die App eine neue: Lösche zuerst die, die du nicht mehr brauchst.
Die Aufnahmen bleiben in der App und verschwinden, wenn du sie deinstallierst. Die Liste bleibt verfügbar, auch wenn kein Lautsprecher antwortet: Nur das Aufnehmen ist dann nicht möglich.

Während eine Aufnahme läuft, erscheinen darunter ein Schieberegler und ein Zähler:
„01:23 / 04:50“, Position und Dauer. Zieh den Regler, um dich in der Aufnahme zu bewegen.

**Zuletzt gehört** zeigt den vom Agenten geführten Verlauf: Tippe auf eine Zeile, um
erneut zu starten.

Ein Album oder Ordner aus der Bibliothek wird vollständig erneut abgespielt, nicht nur
der erste Titel. Ist der Medienserver ausgeschaltet, sagt die App das, statt eine
Wiedergabe anzukündigen, die nie startet.

---

## 7. Der Reiter Gruppen

![Gruppen](../images/05-groupes.jpg)

**Master-Lautsprecher** nennt den, der tatsächlich wiedergibt — von den Lautsprechern
selbst gelesen, nicht als der angenommen, den du ausgewählt hast.

**Zu gruppierende Lautsprecher**: Hake die an, die folgen sollen, dann **Gruppieren**.
**Auflösen** löst die ganze Gruppe auf.

Ein Lautsprecher gehört immer nur zu einer Gruppe, und der Master stellt sie zusammen.
Folgt der ausgewählte Lautsprecher einem anderen, ist **Gruppieren** ausgegraut und eine
Meldung verweist auf den Master.

Die Option **Dauerhafte Gruppe** bittet den Agenten, die Gruppe bei der nächsten
Wiedergabe neu zu bilden, statt sie zerfallen zu lassen, wenn die Musik endet. Die App
liest dieses Kennzeichen jetzt zurück, bevor sie eine bestehende Gruppe erweitert, und
gibt es unverändert weiter: Einen Lautsprecher hinzuzufügen macht aus einer dauerhaften
Gruppe keine gewöhnliche mehr.

**Gruppenlautstärke** wirkt auf alle Lautsprecher zugleich und verschiebt die Werte,
ohne die Abstände anzutasten. Küche auf 60, Wohnzimmer auf 30 — die Gruppe um 10
angehoben ergibt 70 und 40, nicht 70 und 35. Unter dem Regler hat jeder Lautsprecher der
Gruppe seinen eigenen. **Auf die ganze Gruppe anwenden** setzt alle auf denselben Wert,
genau wie die Taste **=** im Reiter Wiedergabe. Dieser Block erscheint nur, wenn eine Gruppe besteht.

Eine permanente Gruppe bleibt so, wie du sie zusammengestellt hast. Einen ihrer
Lautsprecher in den Standby zu schicken nimmt ihn aus der laufenden Gruppe, ohne ihn aus
der gespeicherten Gruppe zu entfernen: Bei der nächsten Wiedergabe ist er wieder dabei.
Und wartet eine permanente Gruppe auf ihre nächste Wiedergabe, weist die App darauf hin
und fragt nach, bevor sie an ihrer Stelle eine andere bildet.

Spielt keine Gruppe, ist eine normale Gruppe gespeichert und du setzt das Häkchen bei
**Dauerhafte Gruppe**, fragt die App ebenfalls nach: Eine dauerhafte Gruppe, die der Agent
dahinter zurückhält, würde ersetzt.

---

## 8. Der Reiter Einstellungen

![Bekannte Lautsprecher](../images/07-reglages-enceintes.jpg)

**Ausgewählter Lautsprecher**: Name, Adresse und Modell. Das ist der Lautsprecher, den
du gewählt hast — nicht zwingend der, der die Befehle erhält. Folgt er einer Gruppe,
sagt eine bernsteinfarbene Zeile, wohin die Befehle tatsächlich gehen und warum.

**Wecker**: die des ausgewählten Lautsprechers — ein Wecker klingelt nur auf dem
Lautsprecher, auf dem er gespeichert ist, auch in einer Gruppe. Jeder Wecker spielt eine
Taste (1 bis 6, oder 1 bis 6 zweimal gedrückt) zur gewählten Zeit an den gewählten Tagen (die Kurzwahlen **Mo → Fr**, **Wochenende** und **Täglich** setzen die Tage auf einmal; jedes Kästchen bleibt einzeln änderbar).
Dazu stellst du seine Lautstärke ein (oder behältst die des Lautsprechers), das
automatische Ausschalten und die Schlummerdauer. Die Karte zeigt den nächsten Wecker so,
wie ihn der Lautsprecher plant, und warnt, wenn seine Uhr noch nicht gestellt ist. Ist die
Zeitzone des Lautsprechers leer (UTC), wird die des Telefons mit dem nächsten Wecker
gespeichert. Anderswo geänderte Wecker — STR-Telefonseite, Desktop-App — werden nicht
überschrieben: Die App speichert nur, was du geändert hast. Erfordert den STR-Agenten. Die Uhrzeit wird auf dem Ziffernblatt des Telefons eingestellt. Der Papierkorb einer Zeile löscht den Wecker nach Bestätigung.

**Startlautstärke**: die Lautstärke, die der Lautsprecher beim Einschalten annimmt, sobald
er spielt. Häkchen setzen, Lautstärke wählen, dann **Startlautstärke speichern**. Eine
während des Hörens eingestellte Lautstärke wird nie geändert.

**Schlummertaste** (in der Weckerkarte): hake die Taste der Fernbedienung an, die einen
klingelnden Wecker schlummern lässt — **AUX**, **Daumen hoch** oder **Daumen runter** —,
dann **Schlummertaste speichern**. Außerhalb eines Weckers tut eine Daumentaste nichts, und
AUX schaltet wie gewohnt auf den AUX-Eingang. Eine Daumentaste mit einer anderen Aktion
(Einschlaftimer, Bass…) zeigt diese an; das Anhaken ersetzt sie, und die App sagt es
vorher. Nur geänderte Kästchen werden geschrieben: eine anderswo gesetzte Aktion auf einer
anderen Taste bleibt. Eine Taste mit gespeicherter Gruppe lehnt der Lautsprecher ab und
nennt den Grund.

**Sprache**: Deutsch, Französisch, Englisch, Niederländisch, Spanisch, Italienisch. Die Umstellung
gilt sofort, ohne Neustart, und wird gemerkt.

**Thema**: System, Hell oder Dunkel. „System" folgt der Anzeigeeinstellung des Telefons,
einschließlich der automatischen Umschaltung am Abend.

**Suche**: der Netzwerk-Scan und die manuelle Eingabe einer IP-Adresse. Die Suche fragt
zuerst mDNS und durchsucht das Subnetz nur, wenn dabei nichts gefunden wird. Der
Durchlauf ist auf private Adressbereiche beschränkt und erreicht damit nie ein Mobilnetz
oder einen VPN-Tunnel.

**Bekannte Lautsprecher**: die gemerkte Liste. **Verwenden** wechselt zu einem, **✕**
vergisst ihn. Ein Lautsprecher mit dem Hinweis **„Kein STR — bereit zur Installation"**
antwortet einwandfrei, hat aber keinen Agenten; weiter unten erscheint dann ein
erklärender Block mit einem Link zur Seite des STR-Projekts.

Ein Lautsprecher, dessen Agent älter als Version 1.8.0 ist, zeigt einen ähnlichen Block,
**STR-Agent zu alt**, und seine Karte im Tab Wiedergabe trägt den Hinweis „zu alt".

**STR-Agent: neue Version**: höchstens einmal am Tag, beim Öffnen der Einstellungen, liest
die App auf GitHub die neueste Version des STR-Agenten. Haben bekannte Lautsprecher einen
älteren Agenten, nennt eine Karte sie, mit **Neuigkeiten** und **Später** (die Karte kommt
mit der nächsten Version wieder), und ihre Karte im Tab Wiedergabe zeigt „1.9.1
verfügbar“. Der Agent wird mit der STR-App am Computer aktualisiert. Das Kästchen
**Automatisch prüfen** der Karte **Aktualisierung** schaltet auch dies ab.

**Diagnose**: *Lautsprecher abfragen* betrifft den **ausgewählten** Lautsprecher — Name,
Modell, Geräte-ID, Firmware-Version, den Agenten mit Port und live auf 17008 und 8888
gelesener Version, den Zustand der Benachrichtigungen, den Bassbereich, die Liste der
erkannten API-Endpunkte und das, was jeder bekannte Lautsprecher über die aktuelle Zone
sagt. Das sind die Angaben, die in eine Fehlermeldung gehören. *Logo-Cache leeren*
erzwingt das erneute Laden der Senderbilder.

**Sicherung der Favoriten**: Die lokalen Presets und die gespeicherten Texte der
Sprachnachricht leben nur auf dem Telefon. Ein Update behält sie; eine Deinstallation löscht
sie, auch die, die Android vor der Installation eines mit einem anderen Schlüssel signierten
APK verlangt. **Exportieren** erstellt eine Datei und öffnet das Teilen (Drive, E-Mail,
Dateien…); **Importieren** liest eine solche Datei wieder ein und fügt Fehlendes hinzu, ohne
etwas zu ersetzen oder zu löschen.

**Lautsprecher-Sicherung**: **In eine Datei sichern** bewahrt, was der STR-Agent für den
ausgewählten Lautsprecher speichert — die Tasten 1 bis 12 (eine Spotify-Taste mit ihrer
Playlist), die Wecker, die Startlautstärke und die Schlummertaste. **Aus einer Datei
wiederherstellen…** spielt sie zurück, nach einer Frage, die den Ziellautsprecher und den
Ursprung der Datei nennt: die Datei kann diese Einstellungen auch auf einen anderen
Lautsprecher übertragen. Die Tasten der Datei ersetzen die des Lautsprechers, die anderen
bleiben; seine Wecker werden ersetzt. Ohne zweite Reihe am Ziel werden die Tasten 7 bis 12
übersprungen. Der Bericht nennt, was wiederhergestellt und was abgelehnt wurde. Die feste
Gruppe wird nicht gesichert. Eine Spotify-Taste spielt nur, wenn der Lautsprecher mit
demselben Spotify-Konto verbunden ist. STR-Agent erforderlich.

**Aktualisierung** (von GitHub installierte Version): Höchstens einmal am Tag fragt
die App bei GitHub, ob eine neuere Version veröffentlicht ist; das Kästchen **Automatisch
prüfen** schaltet das ab. **Jetzt prüfen** tut es auf Wunsch. Gibt es eine, erscheint auch
im Tab Wiedergabe ein Hinweis (**Neuigkeiten**, **Später**, **Installieren**).
**Installieren** lädt die APK, prüft, dass Größe und SHA-256-Prüfsumme genau denen auf
GitHub entsprechen, und öffnet dann den Android-Installer, in dem du bestätigst. Beim
ersten Mal fragt Android, ob SoundReborn Remote Apps installieren darf. Android lehnt es
ab, über eine mit einem anderen Schlüssel signierte APK zu installieren: Eine aus Visual
Studio gestartete Testversion wird so nicht aktualisiert.

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

**Langes Drücken auf das App-Symbol** (Android-Launcher): **Alle Standby** setzt jeden
bekannten Lautsprecher mit STR-Agent in Standby — ein Lautsprecher ohne Agent wird nicht
angetastet, denn sein einziger Standby-Befehl würde ihn einschalten, wenn er aus wäre —,
und **Taste 1** bis **Taste 3** spielen die Taste auf dem ausgewählten Lautsprecher. Die App
öffnet sich im Tab Wiedergabe und sagt, was getan wurde. Diese Verknüpfungen lassen sich
auf den Startbildschirm legen.

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

**Die Taste Aufnehmen ist ausgegraut.**
Die Meldung unter dem Titel nennt den Grund: Es läuft nichts, die Quelle ist kein
Internetradio, oder die Stream-Adresse dieses Senders ist unbekannt. Starte den Sender
erneut über einen Speicherplatz oder das Verzeichnis und öffne dann den Reiter Quellen neu.

**Der Lautsprecher spielt die Sprachnachricht nicht.**
Die Meldung unter der Karte nennt den Grund. Hat der Lautsprecher sie nicht abgeholt,
isoliert das WLAN wahrscheinlich die Geräte (Gastnetz). Ein zu kurzes Antippen sendet
nichts: Lass den Finger auf der Taste, während du sprichst. Der vorgelesene Text verlangt
Internetzugang am Lautsprecher. Ist eine Nachricht nicht angekommen, sendet **Erneut versuchen** neben dem Grund sie
unverändert noch einmal, ohne neue Aufnahme.

**Meine lokalen Presets sind nach einem Update verschwunden.**
Ein normales Update behält sie. Sie verschwinden, wenn die App deinstalliert wurde — Android
verlangt das beim Wechsel zu einer mit einem anderen Schlüssel signierten Version, etwa von
einer aus Visual Studio gestarteten Testversion zur veröffentlichten Version. Exportiere sie
vorher unter Einstellungen → **Sicherung der Favoriten** und importiere dann die Datei.

---

## 11. Bekannte Grenzen

- **Keine Höhenregelung.** Die API des Lautsprechers bietet keine.
- **Die App installiert den STR-Agenten nicht.** Das verlangt Systemzugriff auf den
  Lautsprecher und einen Neustart; dafür ist das STR-Werkzeug für PC oder Mac da.
- **Spotify wird aus Spotify gesteuert**, über Spotify Connect.
- **Kein Durchsuchen einer DLNA-Bibliothek**, bislang.
- **Nur Android.** Eine iOS-Fassung gibt es nicht.
- **Zweite Reihe, Sleeptimer und „Laufendes auf eine Taste legen“ erfordern einen
  aktuellen STR-Agenten.** Mit einem älteren Agenten erscheinen diese Befehle nicht oder
  werden abgelehnt.
- **Stereopaare.** Die App bildet und löst keine Stereopaare. Ein Paar kann keine Gruppe
  leiten; mit einem STR-Agenten 1.6.3 oder neuer kann es einer Gruppe beitreten, die auf
  einem anderen Lautsprecher gestartet wurde.
- **Aufnahme: nur Internetradios**, höchstens 5 Minuten pro Aufnahme, 10 Aufnahmen
  werden behalten.
- **Tasten kopieren: STR-Agent 1.6.5 oder neuer** auf dem Quell-Lautsprecher.
- **„Gefällt mir“ und „Gefällt mir nicht“: STR-Agent 1.8.1 oder neuer.** Der Titel landet
  im Spotify-Konto, mit dem der Lautsprecher angemeldet ist.
- **Tasten umbenennen und löschen: STR-Agent 1.8.0 oder neuer; der Titel einer
  Spotify-Taste: 1.8.2 oder neuer.**
- **Sprachnachricht: nur der gewählte Lautsprecher.** Eine Durchsage auf einer ganzen
  Gruppe wartet auf den STR-Agenten. Der vorgelesene Text läuft über die Google-Stimme: Er
  verlässt dein Netz, und der Lautsprecher braucht Internetzugang.
- **Lokale Presets: nur Sender**, auf diesem Telefon gespeichert.

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
