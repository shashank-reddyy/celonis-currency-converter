using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Challenge.ViewModels;

namespace Challenge
{
    public partial class App : Application
    {
        private ConversionController _conversionController;

        protected override async void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // WPF formats bound values with en-US by default; use the user's culture to match how amounts are parsed.
            FrameworkElement.LanguageProperty.OverrideMetadata(
                typeof(FrameworkElement),
                new FrameworkPropertyMetadata(XmlLanguage.GetLanguage(CultureInfo.CurrentCulture.IetfLanguageTag)));

            // Composition root: the dependencies are created here and injected into the view model.
            _conversionController = new ConversionController();
            var viewModel = new MainViewModel(_conversionController);

            MainWindow = new MainWindow { DataContext = viewModel };
            MainWindow.Show();

            await viewModel.InitializeAsync();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _conversionController?.Dispose();
            base.OnExit(e);
        }
    }
}