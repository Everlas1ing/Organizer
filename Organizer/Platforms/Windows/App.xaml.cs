using Microsoft.UI.Xaml;

namespace Organizer.WinUI
{

    public partial class App : MauiWinUIApplication
    {

        public App()
        {
            this.InitializeComponent();
        }

        protected override MauiApp CreateMauiApp() => MauiProgram.CreateMauiApp();
    }

}
