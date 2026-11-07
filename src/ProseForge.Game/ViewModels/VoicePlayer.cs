using Plugin.Maui.Audio;

namespace ProseForge.Game.ViewModels;

// Plays one voice at a time. A missing or broken file is reported, never thrown: the game still
// works silently.
public sealed class VoicePlayer(IAudioManager audio) : IDisposable
{
    private IAudioPlayer? player;

    public async Task<bool> PlayAsync(string asset)
    {
        try
        {
            player?.Dispose();
            player = audio.CreatePlayer(await FileSystem.OpenAppPackageFileAsync(asset));
            player.Play();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    public void Pause() => player?.Pause();

    public void Dispose() => player?.Dispose();
}
