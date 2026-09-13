using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class MultiroomPage : ContentPage
{
    private readonly MultiroomViewModel _viewModel;

    public MultiroomPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<MultiroomViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (_viewModel.RefreshCommand.CanExecute(null))
        {
            _viewModel.RefreshCommand.Execute(null);
        }
    }
}
