namespace ProseForge.Game;

// Whether the player asked their phone for less motion: iOS's Reduce Motion, or Android's
// "Remove animations" (animator scale 0). The unlock's shake and flash are skipped when it's on.
public static class Motion
{
    public static bool Reduced
    {
        get
        {
            try
            {
#if ANDROID
                var resolver = Android.App.Application.Context.ContentResolver;
                return Android.Provider.Settings.Global.GetFloat(resolver, Android.Provider.Settings.Global.AnimatorDurationScale, 1f) == 0f;
#elif IOS || MACCATALYST
                return UIKit.UIAccessibility.IsReduceMotionEnabled;
#else
                return false;
#endif
            }
            catch (Exception)
            {
                return false;   // a phone that won't say gets the full celebration, as every other animation does
            }
        }
    }
}
