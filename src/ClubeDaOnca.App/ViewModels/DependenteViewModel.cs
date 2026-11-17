using ClubeDaOnca.App.Services;
using ClubeDaOnca.Contracts.Socios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClubeDaOnca.App.ViewModels;

/// <summary>
/// Cadastro/edição de dependente. Rota: dependente?socioId={guid}
/// (+ parâmetros "dependente" ao editar e "titular" com o nome do titular).
/// Sem socioId ("rascunho"): dependente de um sócio ainda não salvo; volta para a tela do sócio com os dados.
/// </summary>
public partial class DependenteViewModel(IClubeApi api) : ObservableObject, IQueryAttributable
{
    private Guid _socioId;
    private Guid? _dependenteId;
    private bool _rascunho;

    public IReadOnlyList<Opcao> Parentescos => Opcoes.Parentescos;
    public IReadOnlyList<Opcao> Generos => Opcoes.Generos;
    public DateTime Hoje => DateTime.Today;

    [ObservableProperty] public partial string TituloPagina { get; set; } = "Novo dependente";
    [ObservableProperty] public partial string Cabecalho { get; set; } = "Dependente";
    [ObservableProperty] public partial string Nome { get; set; } = string.Empty;
    [ObservableProperty] public partial string Cpf { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime DataNascimento { get; set; } = DateTime.Today.AddYears(-10);
    [ObservableProperty] public partial Opcao? Parentesco { get; set; } = Opcoes.Parentescos[1];
    [ObservableProperty] public partial Opcao? Genero { get; set; } = Opcoes.Generos[0];
    [ObservableProperty] public partial string Email { get; set; } = string.Empty;
    [ObservableProperty] public partial string Telefone { get; set; } = string.Empty;
    [ObservableProperty] public partial string Profissao { get; set; } = string.Empty;
    [ObservableProperty] public partial bool Ocupado { get; set; }
    [ObservableProperty] public partial string? Erro { get; set; }

    /// <summary>Dependente já salvo: permite criar o login dele ou gerar nova senha.</summary>
    [ObservableProperty] public partial bool PodeGerarAcesso { get; set; }
    [ObservableProperty] public partial string TextoAcesso { get; set; } = "🔑  Criar login do dependente";

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("socioId", out var s) && Guid.TryParse(s?.ToString(), out var socioId))
            _socioId = socioId;
        _rascunho = query.TryGetValue("rascunho", out var r) && r is true;
        if (query.TryGetValue("rascunhoAtual", out var ra) && ra is SalvarDependenteRequest atual) PreencherRascunho(atual);
        if (query.TryGetValue("titular", out var t) && t is string titular && !string.IsNullOrWhiteSpace(titular))
            Cabecalho = $"Dependente de {titular}";

        if (query.TryGetValue("dependente", out var d) && d is DependenteDto dep)
        {
            _dependenteId = dep.Id;
            TituloPagina = "Editar dependente";
            Nome = dep.Nome;
            Cpf = Opcoes.FormatarCpf(dep.Cpf);
            DataNascimento = dep.DataNascimento.ToDateTime(TimeOnly.MinValue);
            Parentesco = Opcoes.Parentescos.FirstOrDefault(p => p.Valor == dep.Parentesco);
            Genero = Opcoes.Generos.FirstOrDefault(g => g.Valor == dep.Genero) ?? Opcoes.Generos[0];
            Email = dep.Email ?? string.Empty;
            Telefone = dep.Telefone ?? string.Empty;
            Profissao = dep.Profissao ?? string.Empty;
            if (dep.TitularNome is { } nomeTitular) Cabecalho = $"Dependente de {nomeTitular}";
            PodeGerarAcesso = dep.Ativo;
            TextoAcesso = dep.TemAcesso ? "🔑  Gerar nova senha do dependente" : "🔑  Criar login do dependente";
        }
    }

    private void PreencherRascunho(SalvarDependenteRequest d)
    {
        TituloPagina = "Editar dependente";
        Nome = d.Nome;
        Cpf = Opcoes.FormatarCpf(d.Cpf);
        DataNascimento = d.DataNascimento.ToDateTime(TimeOnly.MinValue);
        Parentesco = Opcoes.Parentescos.FirstOrDefault(p => p.Valor == d.Parentesco);
        Genero = Opcoes.Generos.FirstOrDefault(g => g.Valor == d.Genero) ?? Opcoes.Generos[0];
        Email = d.Email ?? string.Empty;
        Telefone = d.Telefone ?? string.Empty;
        Profissao = d.Profissao ?? string.Empty;
    }

    [RelayCommand]
    private async Task GerarAcessoAsync()
    {
        if (_dependenteId is not { } id) return;
        var temLogin = TextoAcesso.Contains("nova senha");
        if (!await Shell.Current.DisplayAlertAsync("Login do dependente", temLogin
                ? $"Gerar uma nova senha provisória para {Nome}? A senha atual deixa de valer."
                : $"Criar o login de {Nome} com o e-mail cadastrado? Ele verá a própria carteirinha no celular dele.",
                temLogin ? "Gerar" : "Criar", "Cancelar")) return;
        Erro = null;
        Ocupado = true;
        try
        {
            var acesso = await api.GerarAcessoDependenteAsync(_socioId, id);
            TextoAcesso = "🔑  Gerar nova senha do dependente";
            await MostrarAcessoAsync(acesso);
        }
        catch (Exception ex)
        {
            Erro = ErroApi.Mensagem(ex);
        }
        finally
        {
            Ocupado = false;
        }
    }

    public static Task MostrarAcessoAsync(AcessoDependenteDto a) =>
        Shell.Current.DisplayAlertAsync($"Login de {a.Nome}",
            $"Login: {a.Login}\nSenha provisória: {a.SenhaProvisoria}\n\n" +
            "Entregue ao dependente. Com ele, a carteirinha aparece no celular dele. A senha é trocada no primeiro acesso.", "OK");

    [RelayCommand]
    private async Task SalvarAsync()
    {
        Erro = null;
        Ocupado = true;
        try
        {
            var req = new SalvarDependenteRequest(Nome, string.IsNullOrWhiteSpace(Cpf) ? null : Cpf,
                DateOnly.FromDateTime(DataNascimento), Parentesco?.Valor ?? "Outro",
                Genero?.Valor ?? "NaoInformado", Email, Telefone, Profissao);

            if (_rascunho)
            {
                // Sócio ainda não salvo: só confere o básico e devolve os dados; a API valida tudo ao salvar o sócio.
                if (string.IsNullOrWhiteSpace(Nome) || Nome.Trim().Length < 3) { Erro = "Informe o nome completo."; return; }
                if (Parentesco is null) { Erro = "Escolha o parentesco."; return; }
                await Shell.Current.GoToAsync("..", new ShellNavigationQueryParameters { ["dependenteNovo"] = req });
                return;
            }

            if (_dependenteId is { } id)
                await api.AtualizarDependenteAsync(_socioId, id, req);
            else if ((await api.CriarDependenteAsync(_socioId, req)).Acesso is { } acesso)
                await MostrarAcessoAsync(acesso);

            await Shell.Current.GoToAsync("..");
        }
        catch (Exception ex)
        {
            Erro = ErroApi.Mensagem(ex);
        }
        finally
        {
            Ocupado = false;
        }
    }
}
