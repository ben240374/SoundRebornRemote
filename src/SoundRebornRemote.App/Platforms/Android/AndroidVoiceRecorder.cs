using Android.Media;
using SoundRebornRemote.App.Services;

namespace SoundRebornRemote.App;

/// <summary>
/// Le micro du téléphone, pour le message vocal : MediaRecorder, AAC en flux ADTS,
/// mono, 44,1 kHz, 96 kbit/s. Une minute pèse environ 700 Ko, bien en deçà des
/// 8 Mo que l'agent STR accepte.
///
/// Rien ici ne doit faire tomber l'application : un échec du micro remonte en
/// exception à Start, et Stop rend faux plutôt que de lever. Toutes les méthodes
/// passent par un verrou : l'appui, le relâchement et le décompte peuvent se croiser.
/// </summary>
public sealed class AndroidVoiceRecorder : IVoiceRecorder
{
    /// <summary>En deçà, le fichier ne contient pas de son exploitable.</summary>
    private const long MinUsefulBytes = 64;

    private readonly object _gate = new();

    private MediaRecorder? _recorder;
    private string? _path;

    public bool IsSupported => true;

    public async Task<bool> EnsurePermissionAsync()
    {
        try
        {
            return await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var status = await Permissions.CheckStatusAsync<Permissions.Microphone>();

                if (status != PermissionStatus.Granted)
                {
                    status = await Permissions.RequestAsync<Permissions.Microphone>();
                }

                return status == PermissionStatus.Granted;
            });
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Start(string path)
    {
        lock (_gate)
        {
            CancelLocked();

            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            if (File.Exists(path))
            {
                File.Delete(path);
            }

            // Depuis Android 12, le constructeur sans contexte est obsolète.
            var recorder = OperatingSystem.IsAndroidVersionAtLeast(31)
                ? new MediaRecorder(Android.App.Application.Context)
                : new MediaRecorder();

            try
            {
                recorder.SetAudioSource(AudioSource.Mic);
                recorder.SetOutputFormat(OutputFormat.AacAdts);
                recorder.SetAudioEncoder(AudioEncoder.Aac);
                recorder.SetAudioChannels(1);
                recorder.SetAudioSamplingRate(44100);
                recorder.SetAudioEncodingBitRate(96000);
                recorder.SetOutputFile(path);
                recorder.Prepare();
                recorder.Start();
            }
            catch (Exception)
            {
                recorder.Release();
                TryDelete(path);
                throw;
            }

            _recorder = recorder;
            _path = path;
        }
    }

    public bool Stop()
    {
        lock (_gate)
        {
            if (_recorder is null || _path is null)
            {
                return false;
            }

            var path = _path;
            var ok = StopLocked();

            if (!ok || !File.Exists(path) || new FileInfo(path).Length < MinUsefulBytes)
            {
                TryDelete(path);
                return false;
            }

            return true;
        }
    }

    public void Cancel()
    {
        lock (_gate)
        {
            CancelLocked();
        }
    }

    /// <summary>Arrête et libère le micro. Vrai quand le fichier a été terminé proprement.</summary>
    private bool StopLocked()
    {
        var recorder = _recorder;
        _recorder = null;
        _path = null;

        if (recorder is null)
        {
            return false;
        }

        try
        {
            // Lève quand l'arrêt suit le départ de trop près : rien n'a été enregistré.
            recorder.Stop();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
        finally
        {
            try
            {
                recorder.Release();
            }
            catch (Exception)
            {
                // Déjà libéré.
            }
        }
    }

    private void CancelLocked()
    {
        var path = _path;
        StopLocked();

        if (path is not null)
        {
            TryDelete(path);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception)
        {
            // Sans importance : le fichier sera écrasé au prochain message.
        }
    }
}
