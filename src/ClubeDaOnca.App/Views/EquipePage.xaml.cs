using ClubeDaOnca.App.ViewModels;

namespace ClubeDaOnca.App.Views;

public partial class EquipePage : ContentPage
{
    private readonly EquipeViewModel _vm;

    public EquipePage(EquipeViewModel vm)
    {
        InitializeComponent();
        BindingContext = _vm = vm;
    }

    // Carrega pelo OnAppearing: o EventToCommandBehavior do evento Appearing não dispara o comando.
    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _vm.CarregarCommand.ExecuteAsync(null);
    }
}
