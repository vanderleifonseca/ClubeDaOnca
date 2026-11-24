using ClubeDaOnca.App.ViewModels;

namespace ClubeDaOnca.App.Views;

public partial class SocioPage : ContentPage
{
    private readonly SocioViewModel _vm;

    public SocioPage(SocioViewModel vm)
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

    // Tocar no nome do interesse também marca/desmarca a caixa.
    private void AoTocarInteresse(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is InteresseOpcao opcao)
            opcao.Selecionado = !opcao.Selecionado;
    }
}
