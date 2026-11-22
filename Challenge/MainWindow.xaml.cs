using System.Windows;

namespace Challenge
{
    /// <summary>
    /// The view only initializes its components; all state and behavior live in <see cref="ViewModels.MainViewModel"/>.
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
        }
    }
}