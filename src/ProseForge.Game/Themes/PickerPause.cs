using ProseForge.Game.Engine;

namespace ProseForge.Game.Themes;

// The theme picker stops the game while it is open. It only undoes a pause it made itself:
// a player who paused before opening the picker is still paused after Cancel.
public sealed class PickerPause
{
    private bool ours;

    public void Open(GameSession session)
    {
        ours = session.State.CanAct && session.Act((_, s) => GameEngine.TogglePause(s));
    }

    // Cancel, or leaving for another theme: the game goes back to how the player had it, so a
    // theme they switch away from while it runs is still running when they come back.
    public void Close(GameSession session)
    {
        if (ours && session.State.Started && session.State.Paused)
        {
            session.Act((_, s) => GameEngine.TogglePause(s));
            session.Resume();   // the time spent choosing is not production time
        }
        ours = false;
    }
}
