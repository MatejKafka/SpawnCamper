using System.Windows;

namespace ProcessTracer.Server.UI;

public partial class App {
	private MainWindow? _mainWindow;

	private void OnStartup(object sender, StartupEventArgs e) {
		_mainWindow = new MainWindow();
		MainWindow = _mainWindow;
		_mainWindow.Show();
	}
}