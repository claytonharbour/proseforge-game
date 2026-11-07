using Plugin.Maui.Audio;

namespace ProseForge.Game.ViewModels;

// Plays one voice at a time. A missing or broken file is reported, never thrown: the game still
// works silently. Loading and playing are separate steps, so a clip that finishes loading after
// voices were switched off can be dropped instead of played.
public sealed class VoicePlayer(IAudioManager audio) : IDisposable
{
    private IAudioPlayer? player;

    // Load a clip and make it the one that plays next. ended runs on the main thread when it
    // finishes playing. A clip that is no longer wanted once loaded (current says so) is dropped
    // without touching the one already playing.
    public async Task<bool> LoadAsync(string asset, Func<bool> current, Action ended)
    {
        try
        {
            var stream = await FileSystem.OpenAppPackageFileAsync(asset);
            if (!current())
            {
                stream.Dispose();
                return false;
            }
            player?.Dispose();
            player = audio.CreatePlayer(stream);
            player.PlaybackEnded += (_, _) => MainThread.BeginInvokeOnMainThread(ended);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Play() => player?.Play();

    public void Pause() => player?.Pause();

    public void Dispose() => player?.Dispose();
}
