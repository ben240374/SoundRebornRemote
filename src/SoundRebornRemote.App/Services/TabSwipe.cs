namespace SoundRebornRemote.App.Services;

/// <summary>
/// Changement d'onglet par balayage horizontal.
///
/// Cette classe ne fait que déplacer la sélection : la reconnaissance du geste
/// est faite côté Android, dans MainActivity, en observant les touchers sans les
/// consommer. Un SwipeGestureRecognizer posé sur le ScrollView des pages
/// paraissait plus simple, mais il capte le toucher et bloque tout le
/// défilement, vertical comme horizontal. Rien n'est donc attaché aux vues.
/// </summary>
public static class TabSwipe
{
    /// <summary>Évite qu'un geste un peu long ne fasse sauter deux onglets.</summary>
    private static readonly TimeSpan Cooldown = TimeSpan.FromMilliseconds(350);

    private static DateTime _lastMove = DateTime.MinValue;

    /// <summary>
    /// Déplace la sélection d'un onglet : +1 vers la droite de la barre
    /// (Lecture -> Présélections), -1 vers la gauche. À appeler sur le fil
    /// principal.
    /// </summary>
    public static void Move(int step)
    {
        if (step == 0 || DateTime.UtcNow - _lastMove < Cooldown)
        {
            return;
        }

        if (Shell.Current?.CurrentItem is not ShellItem bar)
        {
            return;
        }

        var sections = bar.Items;
        var current = bar.CurrentItem;

        if (current is null || sections.Count == 0)
        {
            return;
        }

        var index = sections.IndexOf(current);
        var next = index + step;

        // Pas de bouclage : arrivé au dernier onglet, un balayage de plus ne
        // ramène pas au premier. C'est le comportement d'une barre d'onglets,
        // et cela évite le grand saut involontaire.
        if (index < 0 || next < 0 || next >= sections.Count)
        {
            return;
        }

        _lastMove = DateTime.UtcNow;
        bar.CurrentItem = sections[next];
    }
}
