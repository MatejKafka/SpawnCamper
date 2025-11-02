using System.Windows;

namespace SpawnCamper.Server;

public partial class App {
	private MainWindow? _mainWindow;

	private void OnStartup(object sender, StartupEventArgs e) {
		_mainWindow = new MainWindow();
		MainWindow = _mainWindow;
		_mainWindow.Show();
	}
}