using System.Threading;
using System.Threading.Tasks;

namespace SimpleText.Core.Updates;

/// <summary>
/// The auto-update contract both frontends implement, so the check → notify → apply flow and its
/// UI are identical across the WinUI (MSIX) and Avalonia (Velopack) builds. A check stashes any
/// pending update inside the service; applying it downloads/stages the update so it takes effect
/// on the next launch. Implementations must never throw — failures surface as
/// <see cref="UpdateStatus.None"/> or <see cref="UpdateOutcome.Failed"/>.
/// </summary>
public interface IUpdateService
{
    /// <summary>Checks for a newer release and remembers it for <see cref="ApplyPendingUpdateAsync"/>.</summary>
    Task<UpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default);

    /// <summary>Downloads/stages the pending update (a no-op if none is pending).</summary>
    Task<UpdateOutcome> ApplyPendingUpdateAsync(CancellationToken cancellationToken = default);
}
