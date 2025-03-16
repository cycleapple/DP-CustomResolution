using System;
namespace CustomResolution;

/// <summary>
/// Flags for internal testing.
/// </summary>
public class DebugConfiguration
{
    public bool IsDebug { get; set; } = false;

    public SetSizeMode SetSizeMode { get; set; } = SetSizeMode.InterceptSystemConfig;
}

public enum SetSizeMode
{
    /// <summary>
    /// Intercept the system config width / height and scale it, but don't intercept window size sets.
    /// Has got a mild risk of growing / shrinking windows if any codepath tries to get->set the window size,
    /// most notably with other plugins trying to change the window size. You shouldn't mix and match those anyway tho...
    /// ... and even then, I'm probably the only one who's going to read this and care about this. -jade
    /// </summary>
    InterceptSystemConfig = 0,

    /// <summary>
    /// Legacy mode, went through more testing, works well except for the game getting confused about windowed mode size.
    /// Might revert or remove depending on how InterceptSystemConfig testing / fixups proceed.
    /// </summary>
    LegacyHookSetWindowPos = 1
}
