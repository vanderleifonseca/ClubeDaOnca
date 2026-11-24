using System.Collections.ObjectModel;
using ClubeDaOnca.App.Services;
using ClubeDaOnca.Contracts.Associacoes;
using ClubeDaOnca.Contracts.Socios;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClubeDaOnca.App.ViewModels;

public sealed record DependenteLinha(DependenteDto Dto)
{
    public string Nome => Dto.Nome;
    public string Detalhe => $"{Opcoes.Texto(Opcoes.Parentescos, Dto.Parentesco)} · {Dto.DataNascimento:dd/MM/yyyy}" +
        (Dto.Genero is "NaoInformado" ? "" : $" · {Opcoes.Texto(Opcoes.Generos, Dto.Genero)}") +
        (Dto.Cpf is null ? "" : $" · CPF {Opcoes.FormatarCpf(Dto.Cpf)}");
    public bool Ativo => Dto.Ativo;
    public string AcaoAtivo => Dto.Ativo ? "Inativar" : "Reativar";
}

/// <summary>Dependente informado no cadastro de um sócio novo (vai junto quando o sócio for salvo).</summary>
public sealed record DependenteNovoLinha(SalvarDependenteRequest Req)
{
    public string Nome => Req.Nome;
    public string Detalhe => $"{Opcoes.Texto(Opcoes.Parentescos, Req.Parentesco)} · {Req.DataNascimento:dd/MM/yyyy}" +
        (string.IsNullOrWhiteSpace(Req.Email) ? " · sem login (carteirinha no celular do titular)" : $" · login próprio: {Req.Email}");
}

/// <summary>Um interesse do catálogo, com a caixa marcada ou não.</summary>
public partial class InteresseOpcao(string nome) : ObservableObject
{
    public string Nome { get; } = nome;
    [ObservableProperty] public partial bool Selecionado { get; set; }
}

/// <summary>Uma linha do histórico: "10/11/2026 14:32 · Maria (secretaria)" + "Telefone: A → B".</summary>
public sealed record HistoricoLinha(HistoricoDto Dto)
{
    public string Cabecalho => $"{Dto.Em.ToLocalTime():dd/MM/yyyy HH:mm} · {Dto.UsuarioNome}";

    public string Texto
    {
        get
        {
            var quem = Dto.Descricao is null ? "" : $"{Dto.Descricao}: ";
            return Dto.Acao switch
            {
                "Criado" => quem.Length > 0 ? $"{Dto.ValorNovo} ({Dto.Descricao})" : Dto.ValorNovo ?? "Cadastro criado",
                "Removido" => $"{quem}removido",
                _ => $"{quem}{Dto.Campo}: {Dto.ValorAnterior ?? "(vazio)"} → {Dto.ValorNovo ?? "(vazio)"}",
            };
        }
    }
}

/// <summary>Cadastro/edição de um sócio titular, seu endereço, dependentes e histórico. Rota: socio?id={guid}.</summary>
public partial class SocioViewModel(IClubeApi api) : ObservableObject, IQueryAttributable
{
    public IReadOnlyList<Opcao> Categorias => Opcoes.Categorias;
    public IReadOnlyList<Opcao> Generos => Opcoes.Generos;
    public IReadOnlyList<Opcao> EstadosCivis => Opcoes.EstadosCivis;
    public ObservableCollection<DependenteLinha> Dependentes { get; } = [];
    public ObservableCollection<DependenteNovoLinha> DependentesNovos { get; } = [];
    public ObservableCollection<HistoricoLinha> Historico { get; } = [];
    public IReadOnlyList<InteresseOpcao> Interesses { get; } =
        ClubeDaOnca.Domain.Socios.Interesses.Todos.Select(n => new InteresseOpcao(n)).ToList();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(EhEdicao), nameof(TituloPagina))]
    public partial Guid? Id { get; set; }

    public bool EhEdicao => Id is not null;
    public string TituloPagina => EhEdicao ? "Editar sócio" : "Novo sócio";

    [ObservableProperty] public partial string Cabecalho { get; set; } = string.Empty;
    [ObservableProperty] public partial string Rodape { get; set; } = string.Empty;

    // Dados pessoais
    [ObservableProperty] public partial string Nome { get; set; } = string.Empty;
    [ObservableProperty] public partial string Cpf { get; set; } = string.Empty;
    [ObservableProperty] public partial string Email { get; set; } = string.Empty;
    [ObservableProperty] public partial string Telefone { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime DataNascimento { get; set; } = DateTime.Today.AddYears(-30);
    [ObservableProperty] public partial Opcao? Genero { get; set; } = Opcoes.Generos[0];
    [ObservableProperty] public partial string Profissao { get; set; } = string.Empty;
    [ObservableProperty] public partial string Nacionalidade { get; set; } = "Brasileira";
    [ObservableProperty] public partial Opcao? EstadoCivil { get; set; } = Opcoes.EstadosCivis[0];
    [ObservableProperty] public partial string Rg { get; set; } = string.Empty;
    [ObservableProperty] public partial DateTime DataCadastro { get; set; } = DateTime.Today;
    [ObservableProperty] public partial Opcao? Categoria { get; set; } = Opcoes.Categorias[0];
    [ObservableProperty] public partial string Situacao { get; set; } = string.Empty;

    // Endereço
    [ObservableProperty] public partial string Cep { get; set; } = string.Empty;
    [ObservableProperty] public partial string Logradouro { get; set; } = string.Empty;
    [ObservableProperty] public partial string Numero { get; set; } = string.Empty;
    [ObservableProperty] public partial string Complemento { get; set; } = string.Empty;
    [ObservableProperty] public partial string Bairro { get; set; } = string.Empty;
    [ObservableProperty] public partial string Cidade { get; set; } = string.Empty;
    [ObservableProperty] public partial string Uf { get; set; } = string.Empty;

    [ObservableProperty] public partial bool SemDependentes { get; set; } = true;
    [ObservableProperty] public partial bool SemDependentesNovos { get; set; } = true;
    private int? _editandoNovo;
    [ObservableProperty] public partial bool SemHistorico { get; set; } = true;
    [ObservableProperty] public partial bool Ocupado { get; set; }
    [ObservableProperty] public partial string? Erro { get; set; }

    public DateTime Hoje => DateTime.Today;

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && Guid.TryParse(v?.ToString(), out var id))
            Id = id;

        // Volta da tela de dependente (sócio ainda não salvo): inclui ou substitui o que estava sendo editado.
        if (query.TryGetValue("dependenteNovo", out var d) && d is SalvarDependenteRequest novo)
        {
            if (_editandoNovo is { } i && i < DependentesNovos.Count) DependentesNovos[i] = new DependenteNovoLinha(novo);
            else DependentesNovos.Add(new DependenteNovoLinha(novo));
            SemDependentesNovos = DependentesNovos.Count == 0;
        }
        _editandoNovo = null; // parâmetros de objeto do Shell são de uso único: não se repetem ao voltar
    }

    [RelayCommand]
    private Task EditarDependenteNovoAsync(DependenteNovoLinha linha)
    {
        _editandoNovo = DependentesNovos.IndexOf(linha);
        return Shell.Current.GoToAsync("dependente", new ShellNavigationQueryParameters
        {
            ["rascunho"] = true, ["rascunhoAtual"] = linha.Req, ["titular"] = string.IsNullOrWhiteSpace(Nome) ? "novo sócio" : Nome,
        });
    }

    [RelayCommand]
    private void RemoverDependenteNovo(DependenteNovoLinha linha)
    {
        DependentesNovos.Remove(linha);
        SemDependentesNovos = DependentesNovos.Count == 0;
    }

    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (Id is not { } id) return;
        await Executar(async () =>
        {
            Mostrar(await api.SocioAsync(id));
            await CarregarHistoricoAsync(id);
        });
    }

    [RelayCommand]
    private async Task BuscarCepAsync()
    {
        Erro = null;
        Ocupado = true;
        try
        {
            var e = await ViaCepService.BuscarAsync(Cep);
            if (e is null)
            {
                Erro = "CEP não encontrado (ou sem internet). Preencha o endereço manualmente.";
                return;
            }
            Logradouro = e.Logradouro ?? Logradouro;
            Bairro = e.Bairro ?? Bairro;
            Cidade = e.Cidade ?? Cidade;
            Uf = e.Uf ?? Uf;
            if (string.IsNullOrWhiteSpace(Complemento) && !string.IsNullOrWhiteSpace(e.Complemento)) Complemento = e.Complemento;
        }
        finally
        {
            Ocupado = false;
        }
    }

    [RelayCommand]
    private async Task SalvarAsync()
    {
        Erro = null;
        Ocupado = true;
        try
        {
            var req = new SalvarSocioRequest(Nome, Cpf, Email, Telefone,
                DateOnly.FromDateTime(DataNascimento), Categoria?.Valor ?? "Individual",
                Genero?.Valor ?? "NaoInformado", Profissao,
                new EnderecoDto(Cep, Logradouro, Numero, Complemento, Bairro, Cidade, Uf),
                DateOnly.FromDateTime(DataCadastro),
                Nacionalidade, EstadoCivil?.Valor ?? "NaoInformado", Rg,
                Id is null ? DependentesNovos.Select(x => x.Req).ToList() : null,
                Interesses.Where(i => i.Selecionado).Select(i => i.Nome).ToList());

            if (Id is { } id)
            {
                Mostrar(await api.AtualizarSocioAsync(id, req));
                await CarregarHistoricoAsync(id);
                await Alerta("Sócio salvo", "Os dados foram atualizados.");
            }
            else
            {
                var criado = await api.CriarSocioAsync(req);
                DependentesNovos.Clear(); // agora aparecem na lista de dependentes do título
                SemDependentesNovos = true;
                Id = criado.Socio.Id;
                Mostrar(criado.Socio);
                await CarregarHistoricoAsync(criado.Socio.Id);

                var acesso = criado.SenhaProvisoria is null
                    ? $"O sócio já tinha conta. Login: {criado.Login}"
                    : $"Login: {criado.Login}\nSenha provisória: {criado.SenhaProvisoria}\n\n" +
                      "Anote e entregue ao sócio. Esta senha não será exibida novamente e precisa ser trocada no primeiro acesso.";
                foreach (var a in criado.Acessos ?? [])
                    acesso += $"\n\nDependente {a.Nome}\nLogin: {a.Login}\nSenha provisória: {a.SenhaProvisoria}";
                await Alerta($"Título nº {criado.Socio.NumeroTitulo} criado", acesso);
            }
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

    [RelayCommand]
    private async Task AlterarSituacaoAsync()
    {
        if (Id is not { } id) return;
        var escolha = await Shell.Current.DisplayActionSheetAsync("Situação do título", "Cancelar", null,
            Opcoes.Situacoes.Select(o => o.Texto).ToArray());
        var opcao = Opcoes.Situacoes.FirstOrDefault(o => o.Texto == escolha);
        if (opcao is null || opcao.Valor == Situacao) return;

        await Executar(async () =>
        {
            Mostrar(await api.AlterarSituacaoAsync(id, new AlterarSituacaoRequest(opcao.Valor)));
            await CarregarHistoricoAsync(id);
        });
    }

    [RelayCommand]
    private async Task RenovarQrAsync()
    {
        if (Id is not { } id) return;
        var ok = await Shell.Current.DisplayAlertAsync("Renovar QR Code",
            "Os QR Codes atuais do titular e dos dependentes deixam de valer (use em caso de celular perdido). " +
            "O sócio precisa abrir o app com internet para receber o novo código. Continuar?", "Renovar", "Cancelar");
        if (!ok) return;

        await Executar(async () =>
        {
            await api.RenovarQrAsync(id);
            await CarregarHistoricoAsync(id);
            await Alerta("QR Code renovado", "Os códigos antigos não são mais aceitos na portaria.");
        });
    }

    [RelayCommand]
    private Task AbrirContratoAsync() =>
        Id is { } id ? Shell.Current.GoToAsync($"contrato?socioId={id}") : Task.CompletedTask;

    [RelayCommand]
    private Task NovoDependenteAsync() =>
        Id is { } id
            ? Shell.Current.GoToAsync($"dependente?socioId={id}", new ShellNavigationQueryParameters { ["titular"] = Nome })
            : Shell.Current.GoToAsync("dependente", new ShellNavigationQueryParameters
            {
                ["rascunho"] = true, ["titular"] = string.IsNullOrWhiteSpace(Nome) ? "novo sócio" : Nome,
            });

    [RelayCommand]
    private Task AbrirDependenteAsync(DependenteLinha? linha) =>
        Id is { } id && linha is not null
            ? Shell.Current.GoToAsync($"dependente?socioId={id}",
                new ShellNavigationQueryParameters { ["dependente"] = linha.Dto, ["titular"] = Nome })
            : Task.CompletedTask;

    [RelayCommand]
    private async Task AlternarAtivoAsync(DependenteLinha? linha)
    {
        if (Id is not { } id || linha is null) return;
        await Executar(async () =>
        {
            if (linha.Ativo) await api.InativarDependenteAsync(id, linha.Dto.Id);
            else await api.ReativarDependenteAsync(id, linha.Dto.Id);
            Mostrar(await api.SocioAsync(id));
            await CarregarHistoricoAsync(id);
        });
    }

    private async Task CarregarHistoricoAsync(Guid id)
    {
        Historico.Clear();
        foreach (var h in await api.HistoricoSocioAsync(id))
            Historico.Add(new HistoricoLinha(h));
        SemHistorico = Historico.Count == 0;
    }

    private async Task Executar(Func<Task> acao)
    {
        Erro = null;
        Ocupado = true;
        try
        {
            await acao();
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

    private void Mostrar(SocioDto s)
    {
        Nome = s.Nome;
        Cpf = Opcoes.FormatarCpf(s.Cpf);
        Email = s.Email ?? string.Empty;
        Telefone = s.Telefone ?? string.Empty;
        DataNascimento = s.DataNascimento.ToDateTime(TimeOnly.MinValue);
        Genero = Opcoes.Generos.FirstOrDefault(g => g.Valor == s.Genero) ?? Opcoes.Generos[0];
        Profissao = s.Profissao ?? string.Empty;
        Nacionalidade = s.Nacionalidade ?? string.Empty;
        EstadoCivil = Opcoes.EstadosCivis.FirstOrDefault(e => e.Valor == s.EstadoCivil) ?? Opcoes.EstadosCivis[0];
        Rg = s.Rg ?? string.Empty;
        DataCadastro = (s.DataCadastro ?? DateOnly.FromDateTime(DateTime.Today)).ToDateTime(TimeOnly.MinValue);
        Categoria = Opcoes.Categorias.FirstOrDefault(c => c.Valor == s.Categoria) ?? Opcoes.Categorias[0];
        Situacao = s.Situacao;
        foreach (var i in Interesses)
            i.Selecionado = s.Interesses?.Contains(i.Nome) == true;

        var e = s.Endereco ?? new EnderecoDto();
        Cep = e.Cep is { Length: 8 } c ? $"{c[..5]}-{c[5..]}" : e.Cep ?? string.Empty;
        Logradouro = e.Logradouro ?? string.Empty;
        Numero = e.Numero ?? string.Empty;
        Complemento = e.Complemento ?? string.Empty;
        Bairro = e.Bairro ?? string.Empty;
        Cidade = e.Cidade ?? string.Empty;
        Uf = e.Uf ?? string.Empty;

        Cabecalho = $"{s.Tipo} · Título nº {s.NumeroTitulo} · {Opcoes.Texto(Opcoes.Situacoes, s.Situacao)}" +
            $" · Contrato: {Opcoes.StatusContrato(s.StatusContrato)}" + (s.PossuiAcessoAoApp ? " · com acesso ao app" : "");
        Rodape = s.UltimaAlteracaoEm is { } em
            ? $"Cadastrado em {s.DataCadastro:dd/MM/yyyy} · Última alteração por {s.UltimaAlteracaoPor} em {em.ToLocalTime():dd/MM/yyyy HH:mm}"
            : $"Cadastrado em {s.DataCadastro:dd/MM/yyyy}";

        Dependentes.Clear();
        foreach (var d in s.Dependentes.OrderByDescending(d => d.Ativo).ThenBy(d => d.Nome))
            Dependentes.Add(new DependenteLinha(d));
        SemDependentes = Dependentes.Count == 0;
    }

    private static Task Alerta(string titulo, string mensagem) =>
        Shell.Current.DisplayAlertAsync(titulo, mensagem, "OK");
}
