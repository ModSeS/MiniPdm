using System.IO;
using System.Windows;
using MiniPdm.Core;
using MiniPdm.Infrastructure;

namespace MiniPdm.Desktop;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            // Единственная точка композиции. Обработчики окна не содержат бизнес-логики.
            string? configured = Environment.GetEnvironmentVariable("MINIPDM_CONNECTION");
            string dataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MiniPdm");
            if (configured is null) Directory.CreateDirectory(dataFolder);
            var store = new SqlitePdmStore(configured ?? $"Data Source={Path.Combine(dataFolder, "minipdm.db")}");
            store.Initialize();
            var cad = new JsonCadAdapter();
            var viewModel = new MainViewModel(store, new ImportService(cad, cad, store), new BomService(store), new DesktopDialogs());
            new MainWindow { DataContext = viewModel }.Show();
            viewModel.RefreshCommand.Execute(null);
        }
        catch (Exception ex)
        {
            MessageBox.Show("Не удалось запустить приложение: " + ex.Message, "Мини-PDM", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
