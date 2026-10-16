using System.Windows;
using System.Windows.Threading;

namespace Basilisco;

public partial class App : Application
{
    void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show($"Ha ocurrido un error inesperado:\n\n{e.Exception.Message}",
            "Basilisco", MessageBoxButton.OK, MessageBoxImage.Error);
        e.Handled = true;
    }
}
