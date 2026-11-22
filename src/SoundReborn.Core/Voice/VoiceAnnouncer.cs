namespace SoundReborn.Core.Voice;

/// <summary>
/// Format du message vocal enregistré sur le téléphone : AAC en flux ADTS, ce
/// qu'Android enregistre sans rien ajouter, et le format de beaucoup de radios que
/// l'enceinte joue déjà. Essayé avec succès sur une SoundTouch 20 (agent 1.8.3).
/// </summary>
public static class VoiceFormat
{
    public const string Extension = "aac";

    public const string ContentType = "audio/aac";
}

/// <summary>Ce qu'est devenu un message vocal envoyé à l'enceinte.</summary>
public enum VoiceOutcome
{
    /// <summary>L'agent a téléchargé le clip : il le joue.</summary>
    Delivered,

    /// <summary>Le téléphone n'a pas d'adresse sur le réseau de l'enceinte.</summary>
    NoNetwork,

    /// <summary>L'agent a accepté l'annonce mais n'est jamais venu chercher le clip.</summary>
    NotFetched,
}

/// <summary>
/// Envoie un message vocal à une enceinte : le téléphone sert le clip, l'agent STR
/// vient le chercher (POST /api/announce avec « url »), le joue, puis reprend ce
/// que l'enceinte jouait.
/// </summary>
public static class VoiceAnnouncer
{
    /// <summary>Au-delà, l'agent refuse le clip (8 Mo).</summary>
    public const int MaxClipBytes = 8 * 1024 * 1024;

    /// <summary>
    /// Temps laissé à l'agent pour venir chercher le clip après avoir répondu.
    /// Il le télécharge normalement avant de répondre ; ce délai couvre un agent
    /// qui répondrait d'abord.
    /// </summary>
    public static TimeSpan FetchGrace { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// Sert le clip, demande l'annonce, et attend que l'agent l'ait téléchargé.
    /// <paramref name="volume"/> (1 à 100) est le volume de l'annonce ; l'agent remet
    /// ensuite celui d'avant. Null : le volume de l'enceinte.
    /// Un refus de l'agent (404 d'un agent sans annonce, 4xx…) remonte en
    /// <see cref="SpeakerException"/>. Une réponse perdue après que le clip est
    /// parti compte comme un succès : l'enceinte a de quoi jouer.
    /// </summary>
    public static async Task<VoiceOutcome> SendClipAsync(
        StrApiClient agent,
        byte[] clip,
        int? volume = null,
        CancellationToken ct = default)
    {
        var local = ClipServer.LocalAddressFor(agent.Host, agent.Port);

        if (local is null)
        {
            return VoiceOutcome.NoNetwork;
        }

        // Du silence en tête et en fin : l'enceinte ouvre son amplificateur avec un
        // temps de retard et avalait le début du message (voir AdtsPadding).
        var padded = AdtsPadding.Pad(clip);

        // Le serveur n'écoute que sur l'adresse du réseau de l'enceinte (le Wi-Fi),
        // jamais sur les données mobiles.
        using var server = ClipServer.Start(padded, VoiceFormat.ContentType, VoiceFormat.Extension, local);

        try
        {
            await agent.AnnounceAsync(null, null, server.UrlFor(local), volume, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (server.Served.IsCompleted && ex is OperationCanceledException or HttpRequestException)
        {
            // Le clip est parti ; seule la réponse s'est perdue, ou a trop tardé
            // (un agent qui ne répond qu'à la fin d'un long message).
        }

        if (!server.Served.IsCompleted)
        {
            await Task.WhenAny(server.Served, Task.Delay(FetchGrace, ct)).ConfigureAwait(false);
        }

        return server.Served.IsCompleted ? VoiceOutcome.Delivered : VoiceOutcome.NotFetched;
    }
}
