using ClubeDaOnca.App.ViewModels;

namespace ClubeDaOnca.App.Views;

public partial class OcorrenciaPage : ContentPage
{
    private readonly OcorrenciaViewModel _vm;

    public OcorrenciaPage(OcorrenciaViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.CarregarCommand.ExecuteAsync(null);
    }
}
