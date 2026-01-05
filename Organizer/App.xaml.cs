using Organizer.ViewModels;
using Syncfusion.Licensing;

namespace Organizer;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
        SyncfusionLicenseProvider.RegisterLicense("Ngo9BigBOggjHTQxAR8/V1JFaF1cX2hIfEx/WmFZfVtgcV9GY1ZTTWY/P1ZhSXxWd0RiWH5bcnRVTmddVUJ9XEM=");
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var mainPage = Current?.Handler?.MauiContext?.Services.GetService<MainPage>();

        if (mainPage != null)
        {
            return new Window(mainPage);
        }
        else
        {
            var databaseService = Current?.Handler?.MauiContext?.Services.GetService<DatabaseService>();
            if (databaseService == null)
            {
                throw new InvalidOperationException("DatabaseService is not registered in the service provider.");
            }
            var vm = new MainViewModel(databaseService);
            return new Window(new MainPage(vm));
        }
    }
}