using System.Windows;
using Nova.Core;

namespace Nova;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        AppLogger.Info("MainWindow initialized successfully.");
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        AppLogger.Info("MainWindow closed.");
    }
}
