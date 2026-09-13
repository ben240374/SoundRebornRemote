using Android.App;
using Android.Content.PM;
using Android.OS;
using Android.Views;
using Microsoft.Maui.ApplicationModel;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

[Activity(
    Theme = "@style/Maui.SplashTheme",
    MainLauncher = true,
    LaunchMode = LaunchMode.SingleTop,
    ConfigurationChanges = ConfigChanges.ScreenSize
        | ConfigChanges.Orientation
        | ConfigChanges.UiMode
        | ConfigChanges.ScreenLayout
        | ConfigChanges.SmallestScreenSize
        | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    // ---------------------------------------------------------- Balayage d'onglet
    //
    // Le geste est reconnu ici, au niveau de l'activité, et non par un
    // SwipeGestureRecognizer posé sur les pages : un tel reconnaisseur capte le
    // toucher sur le ScrollView et bloque le défilement de la page.
    //
    // DispatchTouchEvent ne fait qu'observer le toucher au passage, puis le
    // transmet inchangé à la vue visée (« return base.… » dans tous les cas).
    // Le défilement, les curseurs et les boutons gardent donc exactement leur
    // comportement d'origine.

    /// <summary>Course horizontale minimale, en dp.</summary>
    private const double MinimumTravel = 110;

    /// <summary>Au-delà, c'est un glissement réfléchi et non un balayage.</summary>
    private const long MaximumDuration = 450;

    /// <summary>Le geste doit être franchement horizontal.</summary>
    private const double HorizontalRatio = 2.5;

    private float _downX;
    private float _downY;
    private long _downAt;
    private bool _tracking;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
    }

    public override bool DispatchTouchEvent(MotionEvent? e)
    {
        try
        {
            Observe(e);
        }
        catch (Exception)
        {
            // Un geste mal interprété ne doit jamais empêcher le toucher
            // d'atteindre l'interface.
            _tracking = false;
        }

        return base.DispatchTouchEvent(e);
    }

    private void Observe(MotionEvent? e)
    {
        if (e is null)
        {
            return;
        }

        switch (e.ActionMasked)
        {
            case MotionEventActions.Down:
                _downX = e.GetX();
                _downY = e.GetY();
                _downAt = e.EventTime;
                _tracking = true;
                break;

            case MotionEventActions.PointerDown:
                // Deux doigts : pincement ou autre, ce n'est pas un balayage.
                _tracking = false;
                break;

            case MotionEventActions.Cancel:
                _tracking = false;
                break;

            case MotionEventActions.Up:
                if (_tracking)
                {
                    Evaluate(e);
                }

                _tracking = false;
                break;
        }
    }

    private void Evaluate(MotionEvent e)
    {
        // « this. » explicite : le projet possède un dossier Resources, autant ne
        // laisser aucun doute sur la propriété visée (celle de l'activité).
        var density = this.Resources?.DisplayMetrics?.Density ?? 1f;

        if (density <= 0f)
        {
            density = 1f;
        }

        var dx = (e.GetX() - _downX) / density;
        var dy = (e.GetY() - _downY) / density;
        var elapsed = e.EventTime - _downAt;

        if (elapsed <= 0 || elapsed > MaximumDuration)
        {
            return;
        }

        if (Math.Abs(dx) < MinimumTravel)
        {
            return;
        }

        if (Math.Abs(dx) < Math.Abs(dy) * HorizontalRatio)
        {
            return;
        }

        // Vers la gauche : on avance dans la barre d'onglets.
        var step = dx < 0 ? 1 : -1;

        MainThread.BeginInvokeOnMainThread(() => TabSwipe.Move(step));
    }
}
