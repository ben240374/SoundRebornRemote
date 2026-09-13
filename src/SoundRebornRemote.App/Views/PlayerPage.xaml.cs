using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class PlayerPage : ContentPage
{
    private readonly PlayerViewModel _viewModel;

    public PlayerPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<PlayerViewModel>();
        BindingContext = _viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.OnAppearing();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _viewModel.OnDisappearing();
    }
}
