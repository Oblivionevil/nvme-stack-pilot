using System;
using System.Globalization;
using System.Windows;
using System.Windows.Threading;
using NvmeDriverSwitch.Infrastructure;

namespace NvmeDriverSwitch
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // Die UI-Kultur folgt der Windows-Sprache und gilt damit auch fuer
            // Hintergrund-Threads (Snapshot-Lesungen in Task.Run).
            CultureInfo.DefaultThreadCurrentCulture = CultureInfo.CurrentCulture;
            CultureInfo.DefaultThreadCurrentUICulture = CultureInfo.CurrentUICulture;

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
                ex == null ? LocalizedStrings.Get("UnknownError") : ex.ToString(),
                LocalizedStrings.Get("UnexpectedErrorTitle"),
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }
}
