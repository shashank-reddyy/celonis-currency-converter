using System.Configuration;
using System.Globalization;
using System.Windows;
using System.Windows.Markup;
using Challenge.Caching;
using Challenge.Services;
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

            CacheSettings cacheSettings;
            try
            {
                cacheSettings = CacheSettings.FromApplicationSettings();
            }
            catch (ConfigurationErrorsException ex)
            {
                MessageBox.Show($"The application settings are invalid:\n{ex.Message}", "Configuration error",
                    MessageBoxButton.OK, MessageBoxImage.Error);
                Shutdown(1);
                return;
            }

            // Composition root: the dependencies are created here and injected into the view model.
            _conversionController = new ConversionController();
            var exchangeRateService = new CachingExchangeRateService(
                _conversionController, cacheSettings.CreateEvictionStrategy());
            var viewModel = new MainViewModel(exchangeRateService);

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