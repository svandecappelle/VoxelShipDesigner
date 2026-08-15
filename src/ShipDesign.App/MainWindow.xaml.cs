using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace ShipDesign.App
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var viewModel = new ViewModels.MainViewModel();
            DataContext = viewModel;
            viewModel.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(ViewModels.MainViewModel.ShipModel))
                    Dispatcher.BeginInvoke(new Action(() => Viewport.ZoomExtents()));
            };

            // The initial ship is assembled synchronously in the view model's constructor,
            // i.e. before the PropertyChanged subscription above can catch it — so zoom once more here.
            Loaded += (_, _) => Viewport.ZoomExtents();
        }
    }
}
