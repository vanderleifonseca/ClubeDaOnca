using ClubeDaOnca.App.Services;

namespace ClubeDaOnca.App.Views;

public partial class GestaoPage : ContentPage
{
    private readonly SessaoService _sessao;
    private readonly IClubeApi _api;

    public GestaoPage(SessaoService sessao, IClubeApi api)
    {
        InitializeComponent();
        _sessao = sessao;
        _api = api;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        // Portaria só lê carteirinhas; o cadastro é da secretaria e do admin.
        CartaoSocios.IsVisible = _sessao.EhSecretaria;
        CartaoPortaria.IsVisible = _sessao.EhGestao;
        CartaoReservas.IsVisible = _sessao.EhSecretaria;
        CartaoEspacos.IsVisible = _sessao.EhSecretaria;
        CartaoFinanceiro.IsVisible = _sessao.EhSecretaria;
        CartaoEventos.IsVisible = _sessao.EhSecretaria;
        CartaoComunicados.IsVisible = _sessao.EhSecretaria;
        CartaoAssociacao.IsVisible = _sessao.EhAdmin;
        CartaoEquipe.IsVisible = _sessao.EhAdmin;
        CartaoAssociacoes.IsVisible = _sessao.EhSuperAdmin;
        CartaoOcorrencias.IsVisible = _sessao.EhAdmin; // ocorrências vão para o Admin, não para a secretaria
        if (_sessao.EhAdmin) await AtualizarResumoOcorrenciasAsync();
    }

    private async Task AtualizarResumoOcorrenciasAsync()
    {
        try
        {
            var r = await _api.ResumoOcorrenciasAsync();
            var partes = new List<string>();
            if (r.Abertas > 0) partes.Add(r.Abertas == 1 ? "1 aberta" : $"{r.Abertas} abertas");
            if (r.EmAndamento > 0) partes.Add($"{r.EmAndamento} em andamento");
            if (r.ParaMim > 0) partes.Add(r.ParaMim == 1 ? "1 com você" : $"{r.ParaMim} com você");
            ResumoOcorrencias.Text = string.Join(" · ", partes);
            ResumoOcorrencias.IsVisible = partes.Count > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or Refit.ApiException)
        {
            ResumoOcorrencias.IsVisible = false; // offline: o cartão continua abrindo a lista
        }
    }

    private async void AbrirEquipe(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("equipe");

    private async void AbrirOcorrencias(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("ocorrencias", new ShellNavigationQueryParameters { ["gestao"] = true });

    private async void AbrirAssociacao(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("associacao", new ShellNavigationQueryParameters { ["atual"] = true });

    private async void AbrirAssociacoes(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("associacoes");

    private async void AbrirComunicados(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("comunicados");

    private async void AbrirEventos(object? sender, TappedEventArgs e) =>
        await Shell.Current.GoToAsync("eventos-gestao", new ShellNavigationQueryParameters { ["gestao"] = true });

    private async void AbrirFinanceiro(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("gestao-financeiro");

    private async void AbrirReservas(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("gestao-reservas");

    private async void AbrirEspacos(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("espacos");

    private async void AbrirSocios(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("socios");

    private async void AbrirPortaria(object? sender, TappedEventArgs e) => await Shell.Current.GoToAsync("portaria");
}
