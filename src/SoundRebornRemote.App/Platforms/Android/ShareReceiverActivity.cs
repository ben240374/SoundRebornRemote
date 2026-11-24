using Android.App;
using Android.Content;
using Android.OS;

namespace SoundRebornRemote.App;

/// <summary>
/// Reçoit un texte partagé depuis une autre application (« Partager →
/// SoundReborn Remote » dans Deezer) et le passe à MainActivity.
///
/// Pourquoi une activité à part, sans écran : une application qui partage lance
/// la cible dans SA propre tâche. MainActivity lancée là deviendrait une seconde
/// fenêtre MAUI, avec des pages déjà affichées dans la première — ce qui plante.
/// Cette activité-ci ne fait que relayer : elle rouvre MainActivity dans la tâche
/// de l'application (NewTask + ClearTop + SingleTop : l'instance existante reçoit
/// le texte par OnNewIntent, sinon une nouvelle démarre normalement) et se ferme.
/// </summary>
[Activity(
    Exported = true,
    NoHistory = true,
    ExcludeFromRecents = true,
    TaskAffinity = "",
    Theme = "@android:style/Theme.NoDisplay")]
[IntentFilter(new[] { global::Android.Content.Intent.ActionSend },
    Categories = new[] { global::Android.Content.Intent.CategoryDefault },
    DataMimeType = "text/plain")]
public class ShareReceiverActivity : Activity
{
    /// <summary>L'action, propre à l'application, de l'intention envoyée à MainActivity.</summary>
    public const string SharedTextAction = "soundreborn.intent.action.SHARED_TEXT";

    public const string SharedTextExtra = "soundreborn.shared_text";

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        try
        {
            var received = Intent;
            var text = received?.Action == global::Android.Content.Intent.ActionSend
                ? received.GetStringExtra(global::Android.Content.Intent.ExtraText)
                : null;

            if (!string.IsNullOrWhiteSpace(text))
            {
                var forward = new global::Android.Content.Intent(this, typeof(MainActivity));
                forward.SetAction(SharedTextAction);
                forward.PutExtra(SharedTextExtra, text);
                forward.AddFlags(ActivityFlags.NewTask | ActivityFlags.ClearTop | ActivityFlags.SingleTop);
                StartActivity(forward);
            }
        }
        catch (Exception)
        {
            // Un partage raté ne doit rien casser : l'activité se ferme simplement.
        }

        Finish();
    }
}
