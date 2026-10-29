# SoundReborn Remote — Handleiding

Versie 0.9.82 · Android 7 en hoger

> Talen: [Français](fr.md) · [English](en.md) · [Deutsch](de.md) · **Nederlands** · [Español](es.md)

---

## Inhoud

1. [Wat je vooraf nodig hebt](#1-wat-je-vooraf-nodig-hebt)
2. [Installeren](#2-installeren)
3. [Eerste start](#3-eerste-start)
4. [Het tabblad Afspelen](#4-het-tabblad-afspelen)
5. [Het tabblad Presets](#5-het-tabblad-presets)
6. [Het tabblad Bronnen](#6-het-tabblad-bronnen)
7. [Het tabblad Groepen](#7-het-tabblad-groepen)
8. [Het tabblad Instellingen](#8-het-tabblad-instellingen)
9. [Gebaren](#9-gebaren)
10. [Wat te doen als…](#10-wat-te-doen-als)
11. [Bekende beperkingen](#11-bekende-beperkingen)
12. [Vermeldingen](#12-vermeldingen)

---

## 1. Wat je vooraf nodig hebt

SoundReborn Remote bedient **Bose SoundTouch**-speakers vanaf een Android-telefoon of
-tablet. Alles gebeurt op je eigen netwerk: geen account, geen onlinedienst, geen
gegevens die het huis verlaten.

Drie voorwaarden:

- **Telefoon en speakers op dezelfde wifi.** Let op gastnetwerken en modems die apparaten
  van elkaar afschermen — dan wordt er niets gevonden.
- **De STR-agent op de speakers.** Die vervangt wat de Bose-cloud deed tot ze op
  6 mei 2026 werd uitgezet. Zonder de agent werkt de app nog wel, maar beperkt tot wat de
  firmware alleen biedt: volume, bass, al opgeslagen toetsen. Zenders zoeken, toetsen
  toewijzen en betrouwbaar groeperen lopen allemaal via de agent.
  Installeren gebeurt met het STR-programma voor pc of Mac — <https://st-reborn.de>.
  **Deze app installeert hem niet**; ze wijst alleen de speakers aan die hem missen.
- **Android 7 of hoger.**

---

## 2. Installeren

De app wordt niet via de Play Store verspreid. Je downloadt ze op de *Releases*-pagina van
het project: <https://github.com/ben240374/SoundRebornRemote/releases>

1. Download het `.apk`-bestand op de telefoon.
2. Open het. Android vraagt toestemming om uit een onbekende bron te installeren — geef
   die aan de app die gedownload heeft, meestal de browser.
3. Installeren, daarna openen.

Bij de eerste start wordt geen bijzondere toestemming gevraagd: de app raakt contacten,
locatie noch opslag aan.

---

## 3. Eerste start

De app start in de taal van de telefoon als ze die kent — Frans, Engels, Duits,
Nederlands, Spaans — en anders in het Engels. Je kunt dit altijd wijzigen.

Bij de eerste start is er nog geen speaker bekend. Ga naar **Instellingen** en dan
**Lokaal netwerk scannen**.

![Instellingen](../images/06-reglages.jpg)

De zoekactie verloopt in twee stappen. Eerst vraagt ze apparaten zich aan te melden
(mDNS) — de snelle weg, twee tot drie seconden. Antwoordt niemand, dan bevraagt ze alle
254 adressen van het subnet één voor één op poort 8090: enkele seconden trager, maar het
werkt ook waar multicast wordt gefilterd.

Gevonden speakers verschijnen onder **BEKENDE SPEAKERS**. Tik bij de gewenste op
**Gebruiken**. Hij wordt onthouden: bij de volgende start verbindt de app zich er vanzelf
weer mee.

Vindt de scan niets terwijl je het IP-adres van de speaker kent, typ het dan in het
daarvoor bestemde veld en tik op **Toevoegen**.

---

## 4. Het tabblad Afspelen

Het hoofdscherm, dat je opent om snel iets te doen.

![Afspelen](../images/01-lecture.jpg)

### De speaker kiezen

Bovenaan één kaart per bekende speaker, met model en versie van de STR-agent. De blauw
omlijnde kaart is de bediende speaker; tik op een andere kaart om te wisselen, zonder
langs de instellingen te gaan.

Het puntje rechts toont de verbinding: **live** betekent dat de speaker zijn wijzigingen
meteen doorgeeft, **peiling** dat de app ze met tussenpozen opnieuw uitleest. Bij peiling
duurt het een seconde of twee voor een wijziging van een andere afstandsbediening
zichtbaar wordt.

### Nu aan het spelen

De hoes, de titel en de bron van wat er speelt. Bij een radiozender diens logo en naam,
bij Spotify de albumhoes.

### Bediening

Vier toetsen: stoppen, vorige, afspelen/pauzeren, volgende. Afhankelijk van de bron doen
sommige niets — een live radiostream laat zich niet terugspoelen.

De groene balk **Stand-by** schakelt de speaker uit; is hij uit, dan staat er
**Inschakelen**.

### Volume

Demping, gelijkzetten, min, schuif, plus. Het percentage staat rechts.

Hoort de speaker bij een groep, dan wordt deze schuif **het volume van de hele groep**:
hij verschuift alle speakers met dezelfde stap en houdt zo de verhouding aan die je hebt
ingesteld. Keuken op 60 en woonkamer op 30 — de groep 10 omhoog geeft 70 en 40, niet 70
en 35.

De toets **=** tussen demping en min doet het omgekeerde: hij zet *alle* speakers van de
groep op de getoonde waarde. Groep op 25, iedereen op 25. Hij verschijnt alleen zolang
er een groep actief is — alleen valt er niets gelijk te zetten.

Onder de schuif geeft **Per speaker** een regel per lid van de groep, met een eigen
schuif en eigen toetsen − en +, zodat je de keuken zachter kunt zetten zonder de
woonkamer aan te raken. Een uitgeschakelde speaker staat vaag en reageert niet: een
opdracht ernaartoe zou de app alleen voor niets laten wachten.

### Bass en presets

![Bass en presets](../images/02-lecture-graves.jpg)

De bass loopt van -10 tot +10, afhankelijk van het model. **Een regeling voor hoge tonen
is er niet**: de API van de speaker biedt die niet — het is geen vergetelheid van de app.

De zes presets staan hier ook, om een tabbladwissel te besparen. De groen omlijnde tegel
is degene die speelt.

### Groeperen met

Eén chip per speaker, behalve die welke de opdrachten krijgt. Tik erop om die speaker
aan de groep toe te voegen; een vinkje en een groene rand verschijnen. Nog een tik haalt
hem er weer uit.

**De master wordt opgezocht, niet aangenomen.** In een groep speelt één speaker af en
volgen de andere. Kies je een speaker die volgt, dan zegt de app dat — *Gegroepeerd met
…*, in amber — en stuurt bediening, presets, stand-by en volume naar de master, want die
speelt af. Een speaker kiezen onderbreekt dus nooit wat je aan het luisteren bent.

Op een volgende speaker is de aangevinkte chip die speaker zelf: erop tikken verlaat de
groep. Uitbreiden gebeurt vanaf de master, want die stelt de groep samen.

**Een speaker uitzetten haalt hem eerst uit de groep** en heft de groep op als het de
laatste was. Zet je hem weer aan, dan staat hij op zichzelf, speelt af wat hij af te
spelen heeft, en kun je hem opnieuw groeperen.

De link **Vernieuwen** onderaan leest alles opnieuw: lopende titel, hoes, volume, staat
van de groep.

---

## 5. Het tabblad Presets

![Presets](../images/03-preselections.jpg)

### De zes toetsen

Dezelfde als de fysieke toetsen op de speaker. Elke tegel toont het zenderlogo en de
bitrate. Tikken start het afspelen; de groene omlijning markeert wat er speelt.

Logo's worden gedownload en daarna lokaal bewaard. Ontbreekt er een, dan publiceert de
zender geen bruikbare afbeelding, of antwoordt zijn server niet.

### Een zender vinden

Het zoekveld bevraagt **radio-browser.info**, een open gids die door vrijwilligers wordt
bijgehouden. Twee keuzelijsten beperken de zoekactie tot een land en een taal, en
**Toplijst** toont de meest beluisterde zenders zonder dat je iets typt.

Het vakje **Alleen Bose-compatibel** staat standaard aan en dat kun je beter zo laten: het
filtert de formaten weg die SoundTouch-speakers niet kunnen decoderen (Ogg, Opus, FLAC) en
die stilte zonder foutmelding zouden opleveren.

Tik op een resultaat en er opent een actieblad met

- **Beluisteren** — start de zender meteen, zonder iets op te slaan;
- **① Aan deze toets toewijzen** als de toets vrij is, of **① Bel RTL vervangen** als hij
  bezet is — de naam van de zender die er al staat wordt getoond, zodat je er geen
  overschrijft waar je aan hechtte.

**Toewijzen vereist de STR-agent.** Zonder hem kun je alleen meteen luisteren; opslaan
gebeurt dan door de toets op de speaker zelf ingedrukt te houden, of via de
STR-desktopapp.

---

## 6. Het tabblad Bronnen

![Bronnen](../images/04-sources.jpg)

**Bluetooth**, **AUX** en **Stand-by** bovenaan: drie rechtstreekse snelkoppelingen.

**Spotify** wordt niet vanuit deze app bediend. Open Spotify op je telefoon en kies de
speaker in de apparaatkiezer (Spotify Connect): hij verschijnt daar zodra de STR-agent
draait.

**Bronnen van de speaker** toont wat de speaker opgeeft, met de toestand: `READY` betekent
beschikbaar, `UNAVAILABLE` dat de bron wel bestaat maar niet bruikbaar is — doorgaans een
streamingdienst waarvan het account sinds de cloud-afsluiting onbereikbaar is.

**Een stream afspelen** aanvaardt het adres van een rechtstreekse audiostream — een
`.mp3` of een `.m3u8` — en start die op de speaker. Handig voor een webradio die in de
gids ontbreekt. Hiervoor is de STR-agent nodig.

**Onlangs afgespeeld** toont de geschiedenis die de agent bijhoudt: tik op een regel om
opnieuw te starten.

---

## 7. Het tabblad Groepen

![Groepen](../images/05-groupes.jpg)

**Masterspeaker** noemt degene die werkelijk afspeelt — afgelezen bij de speakers zelf,
niet aangenomen als degene die je hebt gekozen.

**Te groeperen speakers**: vink aan wie moet volgen, dan **Groeperen**. **Opheffen**
haalt de hele groep uit elkaar.

Een speaker hoort maar bij één groep tegelijk, en de master stelt die samen. Volgt de
gekozen speaker een andere, dan is **Groeperen** grijs en verwijst een bericht je naar
de master.

De optie **Blijvende groep** vraagt de agent de groep bij de volgende weergave opnieuw
te vormen, in plaats van hem uiteen te laten vallen als de muziek stopt. De app leest
deze vlag nu terug voordat zij een bestaande groep uitbreidt en geeft hem ongewijzigd
door: één speaker toevoegen maakt van een blijvende groep geen gewone meer.

**Groepsvolume** werkt op alle speakers tegelijk en verschuift de waarden zonder de
onderlinge afstanden aan te tasten. Keuken op 60 en woonkamer op 30 — de groep 10 omhoog
geeft 70 en 40, niet 70 en 35. Onder die schuif heeft elke speaker van de groep zijn
eigen. **Op de hele groep toepassen** zet iedereen op dezelfde waarde, net als de toets
**=** in het tabblad Afspelen.

---

## 8. Het tabblad Instellingen

![Bekende speakers](../images/07-reglages-enceintes.jpg)

**Gekozen speaker**: naam, adres en model. Dit is de speaker die jij hebt gekozen — niet
per se degene die de opdrachten krijgt. Volgt hij een groep, dan zegt een amberkleurige
regel waar de opdrachten werkelijk heen gaan, en waarom.

**Taal**: Nederlands, Frans, Engels, Duits, Spaans. De wijziging geldt meteen, zonder
herstart, en wordt onthouden.

**Thema**: Systeem, Licht of Donker. "Systeem" volgt de weergave-instelling van de
telefoon, inclusief de automatische omschakeling 's avonds.

**Zoeken**: de netwerkscan en het handmatig invoeren van een IP-adres. Het zoeken vraagt
eerst mDNS en doorzoekt het subnet alleen als dat niets oplevert. De scan blijft beperkt
tot privé-adresbereiken en bereikt dus nooit een mobiel netwerk of een VPN-tunnel.

**Bekende speakers**: de onthouden lijst. **Gebruiken** schakelt over, **✕** vergeet
hem. Een speaker met de melding **"Geen STR — klaar voor installatie"** antwoordt prima
maar heeft geen agent; verderop verschijnt dan een uitlegblok met een link naar de site
van het STR-project.

**Diagnose**: *Speaker bevragen* gaat over de **gekozen** speaker — naam, model,
apparaat-ID, firmwareversie, de agent met zijn poort en zijn live op 17008 en 8888
gelezen versie, de staat van de meldingen, het bassbereik, de lijst met herkende
API-eindpunten, en wat elke bekende speaker over de huidige zone verklaart. Dit is de
informatie die bij een probleemmelding hoort. *Logocache legen* dwingt het opnieuw
ophalen van zenderafbeeldingen af.

**Over**: de versie, de vermelding van het STR-project met een link naar zijn site in de
gekozen taal, en de juridische vermeldingen.

---

## 9. Gebaren

**Horizontaal vegen** gaat naar het volgende of vorige tabblad: Afspelen → Presets →
Bronnen → Groepen → Instellingen. Het gebaar moet vlot zijn; langzaam slepen wordt niet
als tabbladwissel gelezen.

Een veeg die **op een schuifregelaar** begint, wordt door die regelaar opgevangen: dat is
bewust, anders zou het volume niet meer in te stellen zijn. Begin het gebaar op een vrij
stuk.

---

## 10. Wat te doen als…

**De scan vindt geen enkele speaker.**
Controleer of de telefoon op dezelfde wifi zit als de speakers, en niet op het gastnetwerk
of op mobiele data. Sommige modems schermen apparaten van elkaar af — die instelling heet
meestal *AP-isolatie* of *clientmodus*. Ken je het IP-adres, voer het dan handmatig in bij
Instellingen.

**De speaker verschijnt maar reageert nergens op.**
Waarschijnlijk staat hij in diepe slaapstand. Wek hem met de aan-uitknop op het tabblad
Afspelen, of druk op een toets op de speaker zelf.

**Een preset doet niets.**
Een lege toets heeft niets te starten. Kijk ook of de speaker niet uitstaat.

**Er worden geen zenderlogo's getoond.**
Leeg de logocache bij de diagnose. Blijft het bij één bepaalde zender, dan ligt het aan
diens eigen afbeeldingsserver.

**Bij een speaker ontbreekt de agentversie.**
Ofwel is de agent er niet geïnstalleerd, ofwel is hij te oud om zijn versienummer te
publiceren. Voer de scan opnieuw uit.

**De groep komt niet tot stand.**
Beide speakers moeten aanstaan en bereikbaar zijn. Mist er één de STR-agent, dan verloopt
het groeperen via de firmware: minder betrouwbaar, en soms zonder melding geweigerd.

**De app staat in de verkeerde taal.**
Instellingen → Taal. Jouw keuze gaat voor op de taal van de telefoon.

---

## 11. Bekende beperkingen

- **Geen regeling voor hoge tonen.** De API van de speaker biedt die niet.
- **De app installeert de STR-agent niet.** Dat vergt systeemtoegang tot de speaker en een
  herstart; dat is het werk van het STR-programma voor pc of Mac.
- **Spotify wordt vanuit Spotify bediend**, via Spotify Connect.
- **Geen DLNA-bibliotheek doorbladeren**, vooralsnog.
- **Alleen Android.** Er is geen iOS-versie.

---

## 12. Vermeldingen

SoundReborn Remote is een persoonlijk project onder MIT-licentie.

Het wordt **niet geproduceerd of goedgekeurd door Bose Corporation**. "Bose" en
"SoundTouch" zijn merken van Bose Corporation, hier uitsluitend genoemd om de
compatibiliteit aan te geven.

Het staat **los van het STR-project**: daardoor niet gebouwd, onderhouden of ondersteund.
Problemen met deze app meld je
[in de eigen repository](https://github.com/ben240374/SoundRebornRemote/issues), niet bij
het STR-project.

De agent **STR (SoundTouch Reborn)** is het werk van Jens Roggenfelder
([JRpersonal](https://github.com/JRpersonal/streborn)) — <https://st-reborn.de>. Zonder
hem zou deze app niets te besturen hebben.

De zendergids komt van [radio-browser.info](https://www.radio-browser.info).

De code is geschreven met Claude (Anthropic).
