namespace SoundRebornRemote.App.Services;

/// <summary>
/// Regroupe une rafale d'appels en un seul, après un court silence.
/// Indispensable pour les curseurs : un glissement produit des dizaines de valeurs
/// par seconde, et le firmware SoundTouch se bloque si on les lui envoie toutes.
/// </summary>
public sealed class Debouncer
{
    private readonly TimeSpan _delay;
    private CancellationTokenSource? _cts;

    public Debouncer(TimeSpan delay) => _delay = delay;

    public void Debounce(Func<CancellationToken, Task> action)
    {
        _cts?.Cancel();
        _cts?.Dispose();

        var cts = new CancellationTokenSource();
        _cts = cts;

        _ = Task.Run(async () =>
        {
            try
            {
                await Task.Delay(_delay, cts.Token).ConfigureAwait(false);
                await action(cts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Une nouvelle valeur est arrivée : celle-ci est caduque.
            }
            catch (Exception)
            {
                // L'appelant affiche ses propres erreurs ; ici on ne fait pas tomber l'app.
            }
        });
    }

    public void Cancel()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }
}
