using System.Threading;
using System.Threading.Tasks;
using SimpleText.Core.Updates;
using Velopack;
using Velopack.Sources;

namespace SimpleText.Avalonia.Services;

/// <summary>
/// Velopack implementation of <see cref="IUpdateService"/>. Checks the project's GitHub Releases
/// (Velopack picks the channel for the running OS) and stages a found update so it applies when
/// the app next exits — matching the WinUI deferred model, so both frontends behave the same.
/// A no-op for non-installed/dev builds; never throws.
/// </summary>
internal sealed class UpdateService : IUpdateService
{
    private const string RepoUrl = "https://github.com/jstephenperry/simple-text";

    private UpdateManager? _manager;
    private global::Velopack.UpdateInfo? _pending;

    public async Task<UpdateStatus> CheckForUpdatesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            _manager = new UpdateManager(new GithubSource(RepoUrl, accessToken: null, prerelease: false));
            if (!_manager.IsInstalled)
                return UpdateStatus.None;

            _pending = await _manager.CheckForUpdatesAsync();
            return _pending != null
                ? new UpdateStatus(true, _pending.TargetFullRelease.Version.ToString())
                : UpdateStatus.None;
        }
        catch
        {
            return UpdateStatus.None;
        }
    }

    public async Task<UpdateOutcome> ApplyPendingUpdateAsync(CancellationToken cancellationToken = default)
    {
        if (_manager == null || _pending == null)
            return UpdateOutcome.NoUpdatePending;

        try
        {
            await _manager.DownloadUpdatesAsync(_pending);
            // Apply when the app next exits (no forced restart), mirroring WinUI's deferred
            // registration: the next launch runs the new version.
            _manager.WaitExitThenApplyUpdates(_pending, silent: true, restart: false);
            return UpdateOutcome.ReadyOnNextLaunch;
        }
        catch
        {
            return UpdateOutcome.Failed;
        }
    }
}
