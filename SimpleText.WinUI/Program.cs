using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.Windows.AppLifecycle;

namespace SimpleText.WinUI;

public static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();

        // Single instance: the first launch owns the key; later launches forward their
        // activation (including any file argument) to it and exit, so two instances can
        // never race on the shared session file.
        var mainInstance = AppInstance.FindOrRegisterForKey("SimpleText.WinUI");
        var activationArgs = AppInstance.GetCurrent().GetActivatedEventArgs();
        if (!mainInstance.IsCurrent)
        {
            mainInstance.RedirectActivationToAsync(activationArgs).AsTask().Wait();
            return;
        }

        AppInstance.GetCurrent().Activated += (_, e) => App.OnRedirectedActivation(e);

        Application.Start(static p =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            _ = new App();
        });
    }
}
