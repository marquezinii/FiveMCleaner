using System.Windows;
using UserControl = System.Windows.Controls.UserControl;

namespace Ralven.App.Views.Pages;

public partial class GamesPage : UserControl
{
    public GamesPage() => InitializeComponent();

    private void OpenFiveM_Click(object sender, RoutedEventArgs e)
    {
        if (Window.GetWindow(this) is MainWindow shell)
        {
            shell.RequestNavigateToFiveM();
        }
    }
}
