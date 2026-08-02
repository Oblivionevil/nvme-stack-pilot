using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using NvmeDriverSwitch.Infrastructure;
using NvmeDriverSwitch.ViewModels;

namespace NvmeDriverSwitch
{
    public partial class MainWindow : Window
    {
        private const int WmSettingChange = 0x001A;
        private const int WmThemeChanged = 0x031A;
        private const string ImmersiveColorSet = "ImmersiveColorSet";

        private HwndSource _hwndSource;
        private readonly MainViewModel _vm = new MainViewModel();

        public MainWindow()
        {
            InitializeComponent();
            DataContext = _vm;

            // Die Hoehe folgt dem Inhalt, damit neue Bereiche nach einem Poll sofort
            // sichtbar sind. Gedeckelt auf die Arbeitsflaeche minus Rand, damit das
            // Fenster bei sehr langem Inhalt nicht ueber den Bildschirm hinauswaechst;
            // erst dann uebernimmt der ScrollViewer.
            MaxHeight = Math.Max(MinHeight, SystemParameters.WorkArea.Height - 24);

            Loaded += (s, e) => _vm.Start();
        }

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var handle = new WindowInteropHelper(this).Handle;
            WindowTheme.ApplyTo(handle);

            _hwndSource = HwndSource.FromHwnd(handle);
            if (_hwndSource != null)
                _hwndSource.AddHook(WndProc);
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            bool themeChanged = msg == WmThemeChanged ||
                (msg == WmSettingChange &&
                 string.Equals(Marshal.PtrToStringUni(lParam), ImmersiveColorSet, StringComparison.OrdinalIgnoreCase));

            if (themeChanged)
                WindowTheme.ApplyTo(hwnd);

            return IntPtr.Zero;
        }
    }
}
