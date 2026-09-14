# SoundReborn Remote — User manual

Version 0.9.79 · Android 7 and later

> Languages: [Français](fr.md) · **English** · [Deutsch](de.md) · [Nederlands](nl.md) · [Español](es.md)

---

## Contents

1. [What you need first](#1-what-you-need-first)
2. [Installing](#2-installing)
3. [First run](#3-first-run)
4. [The Play tab](#4-the-play-tab)
5. [The Presets tab](#5-the-presets-tab)
6. [The Sources tab](#6-the-sources-tab)
7. [The Groups tab](#7-the-groups-tab)
8. [The Settings tab](#8-the-settings-tab)
9. [Gestures](#9-gestures)
10. [What to do if…](#10-what-to-do-if)
11. [Known limits](#11-known-limits)
12. [Notices](#12-notices)

---

## 1. What you need first

SoundReborn Remote controls **Bose SoundTouch** speakers from an Android phone or
tablet. Everything happens on your local network: no account, no online service, no data
leaving your home.

Three conditions:

- **Phone and speakers on the same Wi-Fi.** Beware of guest networks and routers that
  isolate devices from each other — nothing will be found in that case.
- **The STR agent installed on the speakers.** It replaces what the Bose cloud provided
  until it was shut down on 6 May 2026. Without it the app still works, but is reduced
  to what the firmware alone exposes: volume, bass, presets already stored. Station
  search, assigning presets and reliable grouping all go through the agent.
  Installation is done with the STR tool for PC or Mac — <https://st-reborn.de>.
  **This app does not install it**; it only flags the speakers that lack it.
- **Android 7 or later.**

---

## 2. Installing

The app is not distributed through the Play Store. Download it from the project's
*Releases* page: <https://github.com/ben240374/SoundRebornRemote/releases>

1. Download the `.apk` onto the phone.
2. Open it. Android will ask permission to install from an unknown source — grant it to
   whichever app did the downloading, usually the browser.
3. Install, then open.

No special permission is requested on first launch: the app touches neither contacts,
nor location, nor storage.

---

## 3. First run

The app starts in the phone's language when it knows it — French, English, German,
Dutch, Spanish — and in English otherwise. You can change this at any time.

On first launch no speaker is known. Go to **Settings**, then **Scan the local network**.

![Settings](../images/06-reglages.jpg)

The search works in two stages. It first asks devices to announce themselves (mDNS),
which is the fast path, two or three seconds. If nobody answers, it queries all 254
addresses of the subnet on port 8090 one by one — a few seconds longer, but it works
even where multicast is filtered.

Speakers found appear under **KNOWN SPEAKERS**. Tap **Use** on the one you want to
control. It is remembered: on the next launch the app reconnects to it on its own.

If the scan finds nothing but you know the speaker's IP address, type it in the field
provided and tap **Add**.

---

## 4. The Play tab

The main screen, the one you open to act quickly.

![Play](../images/01-lecture.jpg)

### Choosing the speaker

At the top, one card per known speaker, with its model and STR agent version. The card
outlined in blue is the one being controlled; tap another card to switch, without going
through the settings.

The small dot on the right shows the link: **live** means the speaker pushes its changes
in real time, **polling** that the app re-reads them at intervals. While polling, a
change made from another remote takes a second or two to appear.

### Now playing

The artwork, title and source of what is playing. For a radio station, its logo and
name; for Spotify, the album cover.

### Controls

Four keys: stop, previous, play/pause, next. Depending on the source some have no
effect — a live radio stream cannot be rewound.

The green **Standby** bar turns the speaker off; once off, it reads **Turn on**.

### Volume

Mute, minus, slider, plus. The percentage is shown on the right.

When the speaker is part of a group, this slider becomes **the volume of the whole
group**: moving it shifts every speaker by the same amount, which preserves the balance
you set between them. Individual volumes remain available in the Groups tab.

### Bass and presets

![Bass and presets](../images/02-lecture-graves.jpg)

Bass ranges from -10 to +10 depending on the model. **There is no treble control**: the
speaker's API exposes none — this is not an omission in the app.

The six presets are repeated here to save a tab change. The tile outlined in green is
the one playing.

### Group with

One chip per other speaker. Tap it to add that speaker to the group; a tick appears. Tap
again to remove it. The controlled speaker is the **master**: it streams, the others
follow.

The **Refresh** link at the bottom re-reads everything: current title, artwork, volume,
group state.

---

## 5. The Presets tab

![Presets](../images/03-preselections.jpg)

### The six keys

The same six as the physical keys on the speaker. Each tile shows the station logo and
its bitrate. Tap to start playback; the green outline marks the one playing.

Logos are downloaded and then kept locally. When one is missing, the station publishes no
usable image, or its server is not answering.

### Find a station

The search box queries **radio-browser.info**, an open directory maintained by
volunteers. Two drop-downs narrow the search to a country and a language, and **Top
list** shows the most-listened stations without typing anything.

The **Bose-compatible only** box is ticked by default, and is best left that way: it
filters out the formats SoundTouch speakers cannot decode (Ogg, Opus, FLAC), which would
produce silence with no error message.

Tap a result and an action sheet opens with

- **Listen** — plays the station straight away, storing nothing;
- **① Assign to this key** if the key is free, or **① Replace Bel RTL** if it is taken —
  the name of the station already there is shown, so you do not overwrite one you cared
  about.

**Assigning requires the STR agent.** Without it only immediate listening is possible;
storing is then done by holding the key on the speaker itself, or from the STR desktop
app.

---

## 6. The Sources tab

![Sources](../images/04-sources.jpg)

**Bluetooth**, **AUX** and **Standby** at the top: three direct shortcuts.

**Spotify** is not driven from this app. Open Spotify on your phone and pick the speaker
in the device selector (Spotify Connect): it appears there as soon as the STR agent is
running.

**Speaker sources** lists what the speaker declares, with its state: `READY` means
available, `UNAVAILABLE` that the source exists but cannot be used — typically a
streaming service whose account is no longer reachable since the cloud shutdown.

**Play a stream** accepts the address of a direct audio stream — an `.mp3` or an
`.m3u8` — and starts it on the speaker. Useful for a web radio missing from the
directory. This needs the STR agent.

**Recently played** shows the history kept by the agent: tap a line to start it again.

---

## 7. The Groups tab

![Groups](../images/05-groupes.jpg)

**Master speaker** states which one streams. To change master, change the active speaker
in the Play tab.

**Speakers to group**: tick those that should follow, then **Group**. **Dissolve**
breaks up the whole group.

The **Sticky group** option asks the agent to re-form the group at the next playback,
rather than letting it fall apart when the music stops.

**Group volume** acts on every speaker at once. Below that slider, each speaker in the
group has its own, so you can turn the kitchen down without touching the living room.
**Apply to the whole group** sets everyone to the same value.

One behaviour is worth knowing: the group slider **shifts** volumes while keeping the
gaps. If the kitchen is at 60 and the living room at 30, raising the group by 10 gives 70
and 40 — not 70 and 35.

---

## 8. The Settings tab

![Known speakers](../images/07-reglages-enceintes.jpg)

**Active speaker**: name, address, model and agent version.

**Language**: French, English, German, Dutch, Spanish. The change is immediate, with no
restart, and is remembered.

**Theme**: System, Light or Dark. "System" follows the phone's display setting,
including its automatic switch in the evening.

**Search**: the network scan, and manual entry by IP address.

**Known speakers**: the remembered list. **Use** switches to one, **✕** forgets it. A
speaker marked **"No STR — ready for installation"** answers fine but has no agent; an
explanatory block then appears further down, with a link to the STR project site.

**Diagnostics**: *Query the active speaker* shows what the speaker actually declares —
name, model, device ID, firmware version, presence of the agent, state of notifications,
bass range, list of recognised API endpoints. This is the information to attach to a
problem report. *Clear the logo cache* forces station images to be downloaded again.

**About**: the version, credit to the STR project with a link to its site in the chosen
language, and the legal notices.

---

## 9. Gestures

**Swiping horizontally** moves to the next or previous tab: Play → Presets → Sources →
Groups → Settings. The gesture must be brisk; a slow drag is not read as a tab change.

A swipe that starts **on a slider** is taken by the slider: that is deliberate, otherwise
the volume would become impossible to set. Start the gesture on a neutral area.

---

## 10. What to do if…

**The scan finds no speaker.**
Check that the phone is on the same Wi-Fi as the speakers, not on the guest network nor
on mobile data. Some routers isolate devices from one another — the setting is usually
called *AP isolation* or *client mode*. If you know the IP address, enter it by hand in
Settings.

**The speaker appears but nothing responds.**
It is probably in deep standby. Wake it with the power button on the Play tab, or press a
physical key on the speaker.

**A preset does nothing.**
An empty key has nothing to start. Check too that the speaker is not switched off.

**No station logos are shown.**
Clear the logo cache in the diagnostics. If it persists for one particular station, its
own image server is the cause.

**The agent version is missing on a speaker.**
Either the agent is not installed there, or it is too old to publish its version number.
Run the scan again.

**The group will not form.**
Both speakers must be on and reachable. If one of them has no STR agent, grouping falls
back to the firmware, which is less reliable and sometimes refuses without a message.

**The app is in the wrong language.**
Settings → Language. Your choice overrides the phone's language.

---

## 11. Known limits

- **No treble control.** The speaker's API exposes none.
- **The app does not install the STR agent.** That requires system access to the speaker
  and a reboot; it is the job of the STR tool for PC or Mac.
- **Spotify is driven from Spotify**, through Spotify Connect.
- **No DLNA library browsing** at this stage.
- **Android only.** There is no iOS version.

---

## 12. Notices

SoundReborn Remote is a personal project, released under the MIT licence.

It is **neither produced nor endorsed by Bose Corporation**. "Bose" and "SoundTouch" are
trademarks of Bose Corporation, named here only to indicate compatibility.

It is **independent of the STR project**: not built, maintained or supported by it.
Problems with this app should be reported
[on its own repository](https://github.com/ben240374/SoundRebornRemote/issues), not to
the STR project.

The **STR (SoundTouch Reborn)** agent is the work of Jens Roggenfelder
([JRpersonal](https://github.com/JRpersonal/streborn)) — <https://st-reborn.de>. Without
it, this app would have nothing to control.

The station directory is provided by [radio-browser.info](https://www.radio-browser.info).

The code was written with Claude (Anthropic).
