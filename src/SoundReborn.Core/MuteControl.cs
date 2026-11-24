using System.Net;

namespace SoundReborn.Core;

/// <summary>
/// Coupe ou rend le son d'une enceinte dans un état DÉCIDÉ, jamais par bascule.
///
/// Avec l'agent STR : POST /api/box/mute, le MUTE de l'enceinte elle-même. Sans
/// agent, ou avec un agent trop ancien (404) : la touche MUTE du firmware, qui
/// bascule — elle n'est donc pressée que si l'enceinte n'est pas déjà dans l'état
/// voulu.
///
/// Pourquoi pas toujours la touche : sur une enceinte avec agent, une écriture
/// directe sur le port Bose n'est pas reconnue par STR comme l'une des siennes ;
/// dans un groupe permanent, l'agent du maître peut alors la répercuter sur les
/// autres enceintes. Et une bascule envoyée deux fois s'annule.
/// </summary>
public static class MuteControl
{
    public static async Task<bool> SetAsync(StrApiClient? agent, BoseApiClient bose, bool muted, CancellationToken ct = default)
    {
        if (agent is not null)
        {
            try
            {
                return await agent.SetMuteAsync(muted, ct).ConfigureAwait(false);
            }
            catch (SpeakerException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
            {
                // Agent antérieur à /api/box/mute : la touche du firmware, ci-dessous.
            }
        }

        var state = await bose.GetVolumeAsync(ct).ConfigureAwait(false);

        if (state.IsMuted != muted)
        {
            await bose.PressKeyAsync(RemoteKey.Mute, ct).ConfigureAwait(false);
        }

        return muted;
    }
}
