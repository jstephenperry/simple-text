namespace SimpleText.Core.Updates;

/// <summary>The result of applying a pending update.</summary>
public enum UpdateOutcome
{
    /// <summary>Nothing was pending to apply.</summary>
    NoUpdatePending,

    /// <summary>The update was downloaded/staged and will be applied the next time the app starts.</summary>
    ReadyOnNextLaunch,

    /// <summary>The update was handed to an external installer (Windows App Installer) to finish.</summary>
    LaunchedInstaller,

    /// <summary>Applying the update failed.</summary>
    Failed,
}
