using System;
using System.Windows;
using System.Windows.Threading;

namespace NvmeDriverSwitch
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            DispatcherUnhandledException += OnDispatcherUnhandledException;
            AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
            base.OnStartup(e);
        }

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            Report(e.Exception);
            e.Handled = true;
        }

        private void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            Report(e.ExceptionObject as Exception);
        }

        private static void Report(Exception ex)
        {
            MessageBox.Show(
                ex == null ? "Unbekannter Fehler." : ex.ToString(),
                "NVMe Stack Pilot - unerwarteter Fehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
