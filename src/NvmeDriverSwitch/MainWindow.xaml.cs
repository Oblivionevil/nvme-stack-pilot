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
            Loaded += (s, e) => _vm.Start();
        }
    }
}
