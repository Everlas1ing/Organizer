using Organizer.ViewModels;
using Syncfusion.Maui.Scheduler;
using Microsoft.Maui.Controls; 

namespace Organizer;

public partial class MainPage : ContentPage
{
    private readonly MainViewModel _vm;

    public MainPage(MainViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _vm = viewModel;
    }

    private async void Scheduler_Tapped(object sender, SchedulerTappedEventArgs e)
    {
        if (_vm == null) return;

        if (e.Element == SchedulerElement.Appointment && e.Appointments != null && e.Appointments.Count > 0)
        {
            if (e.Appointments[0] is SchedulerAppointment appointment)
            {
                await _vm.ShowTaskDetailsCommand.ExecuteAsync(appointment);
            }
            return;
        }

        if (e.Element == SchedulerElement.SchedulerCell && e.Date != null)
        {
            _vm.ShowAddFormAt(e.Date.Value);
        }
    }
}