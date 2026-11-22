namespace SoundReborn.Core.Voice;

/// <summary>
/// Ajoute du silence avant et après un message AAC (flux ADTS).
///
/// Pourquoi : l'enceinte n'ouvre son amplificateur que quelques centaines de
/// millisecondes après le début d'un clip, et le premier mot disparaît. L'agent STR
/// le compense lui-même pour le MP3 (environ 800 ms de silence en tête,
/// docs/announce-tts.md), mais sert les autres formats tels quels : un message
/// court enregistré en AAC pouvait ainsi être avalé en entier — une coupure de la
/// musique, sans un son. On ajoute donc nous-mêmes ce silence, et un peu après, pour
/// que la fin ne soit pas coupée non plus.
///
/// Les trames de silence copient l'en-tête de la première trame du message (profil,
/// fréquence, canaux) et portent un bloc AAC-LC mono vide (4 octets : un élément
/// mono, gain global, aucune bande, fin) qui se décode en silence numérique, quelle
/// que soit la fréquence. Le flux reste ainsi uniforme. Tout ce qui n'a pas la forme
/// attendue — autre profil, stéréo, en-tête avec CRC, pas d'ADTS du tout — est rendu
/// tel quel : mieux vaut un message sans marge qu'un message illisible.
/// </summary>
public static class AdtsPadding
{
    /// <summary>Silence ajouté en tête : de quoi laisser l'amplificateur s'ouvrir.</summary>
    public static TimeSpan LeadIn { get; set; } = TimeSpan.FromMilliseconds(800);

    /// <summary>Silence ajouté à la fin.</summary>
    public static TimeSpan Tail { get; set; } = TimeSpan.FromMilliseconds(400);

    /// <summary>Taille d'une trame ADTS de silence : en-tête de 7 octets, bloc vide de 4.</summary>
    private const int SilentFrameLength = 11;

    /// <summary>Le bloc AAC-LC mono vide : SCE, gain global, max_sfb = 0, fin de bloc.</summary>
    private static readonly byte[] SilentBlock = { 0x01, 0x18, 0x20, 0x07 };

    /// <summary>Échantillons par trame AAC.</summary>
    private const int SamplesPerFrame = 1024;

    private static readonly int[] SampleRates =
    {
        96000, 88200, 64000, 48000, 44100, 32000, 24000, 22050, 16000, 12000, 11025, 8000, 7350,
    };

    /// <summary>
    /// Le message encadré de silence, ou le message tel quel quand il n'est pas un
    /// flux ADTS AAC-LC mono sans CRC.
    /// </summary>
    public static byte[] Pad(byte[] clip)
    {
        ArgumentNullException.ThrowIfNull(clip);

        if (!TryReadHeader(clip, out var rate))
        {
            return clip;
        }

        var frame = SilentFrame(clip);
        var lead = FramesFor(LeadIn, rate);
        var tail = FramesFor(Tail, rate);

        var result = new byte[clip.Length + (lead + tail) * SilentFrameLength];
        var offset = 0;

        for (var i = 0; i < lead; i++, offset += SilentFrameLength)
        {
            frame.CopyTo(result, offset);
        }

        clip.CopyTo(result, offset);
        offset += clip.Length;

        for (var i = 0; i < tail; i++, offset += SilentFrameLength)
        {
            frame.CopyTo(result, offset);
        }

        return result;
    }

    /// <summary>
    /// Lit l'en-tête de la première trame. Vrai pour un flux ADTS MPEG-4 ou MPEG-2,
    /// profil LC, mono, sans CRC, dont la longueur de trame tient dans le message.
    /// </summary>
    private static bool TryReadHeader(byte[] clip, out int sampleRate)
    {
        sampleRate = 0;

        if (clip.Length < 7)
        {
            return false;
        }

        // Synchro 0xFFF, couche 00, protection_absent = 1 (pas de CRC).
        if (clip[0] != 0xFF || (clip[1] & 0xF6) != 0xF0 || (clip[1] & 0x01) != 0x01)
        {
            return false;
        }

        var profile = (clip[2] >> 6) & 0x03;          // 1 = AAC LC
        var rateIndex = (clip[2] >> 2) & 0x0F;
        var channels = ((clip[2] & 0x01) << 2) | (clip[3] >> 6);
        var frameLength = ((clip[3] & 0x03) << 11) | (clip[4] << 3) | (clip[5] >> 5);

        if (profile != 1 || channels != 1 || rateIndex >= SampleRates.Length ||
            frameLength < 7 || frameLength > clip.Length)
        {
            return false;
        }

        sampleRate = SampleRates[rateIndex];
        return true;
    }

    /// <summary>Une trame de silence qui reprend l'en-tête de la première trame du message.</summary>
    private static byte[] SilentFrame(byte[] clip)
    {
        var frame = new byte[SilentFrameLength];
        Array.Copy(clip, frame, 7);

        // Longueur de trame (13 bits), plénitude du tampon 0x7FF (débit variable),
        // un seul bloc de données.
        frame[3] = (byte)((frame[3] & 0xFC) | ((SilentFrameLength >> 11) & 0x03));
        frame[4] = (byte)((SilentFrameLength >> 3) & 0xFF);
        frame[5] = (byte)(((SilentFrameLength & 0x07) << 5) | 0x1F);
        frame[6] = 0xFC;

        SilentBlock.CopyTo(frame, 7);
        return frame;
    }

    private static int FramesFor(TimeSpan duration, int sampleRate)
        => duration <= TimeSpan.Zero
            ? 0
            : (int)Math.Ceiling(duration.TotalSeconds * sampleRate / SamplesPerFrame);
}
