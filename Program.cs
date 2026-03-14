namespace SimpleText;

static class Program
{
    [STAThread]
    static void Main(string[] args)
    {
        ApplicationConfiguration.Initialize();
        var form = new MainForm();
        if (args.Length > 0 && File.Exists(args[0]))
            form.OpenFileOnLoad(args[0]);
        else
            form.RestoreSessionOnLoad();
        Application.Run(form);
    }
}
