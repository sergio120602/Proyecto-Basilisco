using System.Windows;
using System.Windows.Threading;

namespace Basilisco;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        // Si este .exe viene de una actualización, el viejo se borra en cuanto se cierre.
        Services.Updater.CleanUpAfterUpdate(e.Args);
    }

    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Ha ocurrido un error inesperado:\n\n{e.Exception.Message}",
            "Basilisco", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
