namespace SoundReborn.Core.Recording;

public enum RecordEnd
{
    /// <summary>Arrêt demandé (bouton de la carte ou de la notification).</summary>
    Stopped,

    /// <summary>Les 5 minutes sont atteintes.</summary>
    MaxReached,

    /// <summary>Le fichier atteint <see cref="RecordingRules.MaxBytes"/> : flux anormalement rapide.</summary>
    SizeLimit,

    /// <summary>Fin du flux, erreur réseau ou plus rien reçu pendant <see cref="RecordingRules.Stall"/>.</summary>
    Interrupted,
}

public sealed record RecordOutcome(RecordEnd End, TimeSpan Duration, long Bytes);

/// <summary>
/// Copie un flux dans un fichier, 5 minutes au plus.
///
/// L'horloge est passée en paramètre : en production c'est TimeProvider.System,
/// dans les essais une horloge simulée, ce qui permet de vérifier l'arrêt à
/// 5 minutes sans attendre 5 minutes. La durée est le temps de réception, pas
/// une durée décodée.
/// </summary>
public static class StreamRecorder
{
    private const int BufferSize = 16 * 1024;

    public static async Task<RecordOutcome> CopyAsync(
        Stream body,
        Stream output,
        TimeProvider clock,
        IProgress<TimeSpan>? progress,
        CancellationToken stop)
    {
        var started = clock.GetUtcNow();
        var buffer = new byte[BufferSize];
        long bytes = 0;
        var lastSecond = -1L;

        // Une lecture bloquée (serveur muet) doit aussi s'arrêter à 5 minutes.
        using var max = new CancellationTokenSource(RecordingRules.MaxDuration, clock);

        TimeSpan Elapsed()
        {
            var elapsed = clock.GetUtcNow() - started;
            return elapsed > RecordingRules.MaxDuration ? RecordingRules.MaxDuration : elapsed;
        }

        RecordOutcome Done(RecordEnd end) => new(end, Elapsed(), bytes);

        while (true)
        {
            if (stop.IsCancellationRequested)
            {
                return Done(RecordEnd.Stopped);
            }

            if (max.IsCancellationRequested || clock.GetUtcNow() - started >= RecordingRules.MaxDuration)
            {
                return Done(RecordEnd.MaxReached);
            }

            int read;

            // Le délai sans données est réarmé à chaque lecture.
            using (var stall = new CancellationTokenSource(RecordingRules.Stall, clock))
            using (var linked = CancellationTokenSource.CreateLinkedTokenSource(stop, max.Token, stall.Token))
            // Le gestionnaire HTTP natif d'Android n'interrompt pas toujours une
            // lecture bloquée quand on l'annule : sans données, « Arrêter », les
            // 5 minutes et les 15 s n'aboutiraient jamais. Fermer le flux, lui,
            // débloque toujours la lecture. Toute fin d'enregistrement ferme le flux
            // de toute façon.
            using (linked.Token.Register(static state => CloseQuietly((Stream)state!), body))
            {
                try
                {
                    read = await body.ReadAsync(buffer.AsMemory(), linked.Token).ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // Annulation, flux fermé ou erreur réseau : c'est ce qui a été
                    // demandé qui dit comment l'enregistrement se termine.
                    if (stop.IsCancellationRequested)
                    {
                        return Done(RecordEnd.Stopped);
                    }

                    return max.IsCancellationRequested ? Done(RecordEnd.MaxReached) : Done(RecordEnd.Interrupted);
                }
            }

            if (read == 0)
            {
                return Done(RecordEnd.Interrupted);
            }

            // Jamais plus que MaxBytes : un serveur qui envoie anormalement vite ne
            // doit pas remplir le téléphone en cinq minutes.
            var room = (int)Math.Min(read, RecordingRules.MaxBytes - bytes);
            await output.WriteAsync(buffer.AsMemory(0, room), CancellationToken.None).ConfigureAwait(false);
            bytes += room;

            if (bytes >= RecordingRules.MaxBytes)
            {
                return Done(RecordEnd.SizeLimit);
            }

            var elapsed = Elapsed();
            var second = (long)elapsed.TotalSeconds;

            if (second != lastSecond)
            {
                lastSecond = second;
                progress?.Report(TimeSpan.FromSeconds(second));
            }
        }
    }

    private static void CloseQuietly(Stream stream)
    {
        try
        {
            stream.Dispose();
        }
        catch (Exception)
        {
            // Déjà fermé.
        }
    }
}
