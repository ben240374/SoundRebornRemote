using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class PresetsPage : ContentPage
{
    private readonly PresetsViewModel _viewModel;

    public PresetsPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<PresetsViewModel>();
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
