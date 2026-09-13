using SoundRebornRemote.App.Services;
using SoundRebornRemote.App.ViewModels;

namespace SoundRebornRemote.App.Views;

public partial class SourcesPage : ContentPage
{
    private readonly SourcesViewModel _viewModel;

    public SourcesPage()
    {
        InitializeComponent();

        _viewModel = ServiceHelper.Get<SourcesViewModel>();
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
