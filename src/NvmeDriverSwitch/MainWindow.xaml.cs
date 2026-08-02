using System;
using System.Windows;
using NvmeDriverSwitch.ViewModels;

namespace NvmeDriverSwitch
{
    public partial class MainWindow : Window
    {
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
    }
}
