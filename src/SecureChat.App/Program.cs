using SecureChat.Core.Configuration;
using SecureChat.Core.Session;

namespace SecureChat.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
        Application.ThreadException += (_, e) => ShowError("Neocekivana greška", e.Exception.Message);

        AppSettings settings;
        try
        {
            settings = AppSettings.Load(Path.Combine(AppContext.BaseDirectory, "appsettings.json"));
        }
        catch (SettingsException ex)
        {
            ShowError("Neispravna konfiguracija", ex.Message);
            return;
        }

        using var login = new LoginForm();
        if (login.ShowDialog() != DialogResult.OK || login.Result is null) return;

        // Kontekst UI niti: kontroler preko njega prebacuje sve događaje na UI nit.
        SynchronizationContext context = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        var controller = new ChatController(login.Result, settings, context);
        try
        {
            Application.Run(new MainForm(controller, settings));
        }
        finally
        {
            // Preko Task.Run: blokiranje na UI niti dok DisposeAsync čeka UI kontekst bi napravilo deadlock.
            Task.Run(() => controller.DisposeAsync().AsTask()).GetAwaiter().GetResult();
        }
    }

    private static void ShowError(string title, string message) =>
        MessageBox.Show(message, $"SecureChat – {title}", MessageBoxButtons.OK, MessageBoxIcon.Error);
}