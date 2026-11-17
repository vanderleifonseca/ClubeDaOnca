using System.Collections.ObjectModel;
using ClubeDaOnca.App.Services;
using ClubeDaOnca.Contracts.Ocorrencias;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClubeDaOnca.App.ViewModels;

public static class TextosOcorrencia
{
    public static string Status(string? s) => s switch
    {
        "Aberta" => "🟠 Aberta",
        "EmAndamento" => "🔵 Em andamento",
        "Resolvida" => "🟢 Resolvida",
        null => "",
        _ => s,
    };

    public static string Data(DateTimeOffset d) => d.ToLocalTime().ToString("dd/MM/yyyy HH:mm");
}

public sealed record OcorrenciaLinha(OcorrenciaResumoDto Dto)
{
    public string Titulo => $"nº {Dto.Numero} · {Dto.Titulo}";
    public string Status => TextosOcorrencia.Status(Dto.Status);
    public string Detalhe => $"{OpcaoCategoria.NomeDe(Dto.Categoria)}" + (Dto.Local is null ? "" : $" · {Dto.Local}") +
                             (Dto.Fotos > 0 ? $" · 📷 {Dto.Fotos}" : "");
    public string Registro => $"Registrada por {Dto.RegistradaPor} em {TextosOcorrencia.Data(Dto.RegistradaEm)}";
    public string Responsavel => Dto.Responsavel is null ? "Com a administração (sem responsável)" : $"Com {Dto.Responsavel}";
}

/// <summary>Lista de ocorrências. Rota: ocorrencias (?gestao=true para a caixa da administração, só Admin).</summary>
public partial class OcorrenciasViewModel(IClubeApi api, RegistroOcorrencia registro) : ObservableObject, IQueryAttributable
{
    public static readonly IReadOnlyList<string> Filtros = ["Todas", "Para mim", "Abertas", "Em andamento", "Resolvidas"];

    public ObservableCollection<OcorrenciaLinha> Itens { get; } = [];
    public IReadOnlyList<string> OpcoesFiltro => Filtros;

    [ObservableProperty] public partial bool Gestao { get; set; }
    [ObservableProperty] public partial string Filtro { get; set; } = "Todas";
    [ObservableProperty] public partial string Titulo { get; set; } = "Minhas ocorrências";
    [ObservableProperty] public partial bool Vazia { get; set; }
    [ObservableProperty] public partial bool Carregando { get; set; }
    [ObservableProperty] public partial string? Erro { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        Gestao = query.TryGetValue("gestao", out var g) && g is true;
        if (query.TryGetValue("filtro", out var f) && f is string filtro && Filtros.Contains(filtro)) Filtro = filtro;
        Titulo = Gestao ? "Ocorrências" : "Minhas ocorrências";
    }

    partial void OnFiltroChanged(string value)
    {
        if (Gestao) _ = CarregarAsync();
    }

    [RelayCommand]
    private async Task CarregarAsync()
    {
        Erro = null;
        Carregando = true;
        try
        {
            var lista = !Gestao ? await api.MinhasOcorrenciasAsync() : Filtro switch
            {
                "Para mim" => await api.OcorrenciasAsync(paraMim: true),
                "Abertas" => await api.OcorrenciasAsync("Aberta"),
                "Em andamento" => await api.OcorrenciasAsync("EmAndamento"),
                "Resolvidas" => await api.OcorrenciasAsync("Resolvida"),
                _ => await api.OcorrenciasAsync(),
            };
            Itens.Clear(); // só depois de buscar: cargas simultâneas não duplicam a lista
            foreach (var o in lista) Itens.Add(new OcorrenciaLinha(o));
            Vazia = Itens.Count == 0;
        }
        catch (Exception ex)
        {
            Erro = ErroApi.Mensagem(ex);
        }
        finally
        {
            Carregando = false;
        }
    }

    [RelayCommand]
    private async Task NovaAsync()
    {
        if (await registro.AbrirAsync() is not null) await CarregarAsync();
    }

    [RelayCommand]
    private Task AbrirAsync(OcorrenciaLinha linha) =>
        Shell.Current.GoToAsync("ocorrencia", new ShellNavigationQueryParameters { ["id"] = linha.Dto.Id });
}

public sealed record MovimentoLinha(MovimentoOcorrenciaDto Dto)
{
    public string Titulo => Dto.Tipo switch
    {
        "Registrada" => "📝 Registrada",
        "Encaminhada" => $"➡️ Encaminhada para {Dto.Para}",
        "StatusAlterado" => $"🔄 {TextosOcorrencia.Status(Dto.StatusAnterior)} → {TextosOcorrencia.Status(Dto.StatusNovo)}",
        "Comentario" => "💬 Comentário",
        _ => Dto.Tipo,
    };
    public string Autor => $"{Dto.Autor} · {TextosOcorrencia.Data(Dto.Em)}";
    public string? Texto => Dto.Texto;
    public bool TemTexto => !string.IsNullOrWhiteSpace(Dto.Texto);
}

public sealed record FotoLinha(Guid Id, ImageSource Imagem, string Arquivo);

/// <summary>Detalhe da ocorrência com fotos e linha do tempo. Rota: ocorrencia?id=...</summary>
public partial class OcorrenciaViewModel(IClubeApi api, SessaoService sessao) : ObservableObject, IQueryAttributable
{
    private Guid _id;
    private OcorrenciaDto? _atual;

    public ObservableCollection<MovimentoLinha> Movimentos { get; } = [];
    public ObservableCollection<FotoLinha> Fotos { get; } = [];

    [ObservableProperty] public partial string Titulo { get; set; } = "Ocorrência";
    [ObservableProperty] public partial string Status { get; set; } = string.Empty;
    [ObservableProperty] public partial string Detalhe { get; set; } = string.Empty;
    [ObservableProperty] public partial string Descricao { get; set; } = string.Empty;
    [ObservableProperty] public partial string Registro { get; set; } = string.Empty;
    [ObservableProperty] public partial string Responsavel { get; set; } = string.Empty;
    [ObservableProperty] public partial bool TemFotos { get; set; }
    [ObservableProperty] public partial bool Administrativo { get; set; }
    [ObservableProperty] public partial bool Ocupado { get; set; }
    [ObservableProperty] public partial string? Erro { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
    {
        if (query.TryGetValue("id", out var v) && v is Guid id) _id = id;
    }

    [RelayCommand]
    private async Task CarregarAsync()
    {
        if (_id == Guid.Empty) return;
        Administrativo = sessao.EhAdmin;
        await Executar(async () =>
        {
            var o = await api.OcorrenciaAsync(_id);
            var carregarFotos = _atual is null || !_atual.Fotos.SequenceEqual(o.Fotos);
            Mostrar(o);
            if (carregarFotos) await CarregarFotosAsync(o);
        });
    }

    private void Mostrar(OcorrenciaDto o)
    {
        _atual = o;
        Titulo = $"Ocorrência nº {o.Numero}";
        Status = TextosOcorrencia.Status(o.Status);
        Detalhe = $"{o.Titulo}\n{OpcaoCategoria.NomeDe(o.Categoria)}" + (o.Local is null ? "" : $" · {o.Local}");
        Descricao = o.Descricao;
        Registro = $"Registrada por {o.RegistradaPor} em {TextosOcorrencia.Data(o.RegistradaEm)}";
        Responsavel = o.Responsavel is null ? "Responsável: administração (ainda sem responsável)" : $"Responsável: {o.Responsavel}";
        Movimentos.Clear();
        foreach (var m in o.Movimentos.Reverse()) Movimentos.Add(new MovimentoLinha(m)); // mais recente primeiro
    }

    private async Task CarregarFotosAsync(OcorrenciaDto o)
    {
        var fotos = new List<FotoLinha>();
        foreach (var fotoId in o.Fotos)
        {
            using var resp = await api.FotoOcorrenciaAsync(o.Id, fotoId);
            resp.EnsureSuccessStatusCode();
            var bytes = await resp.Content.ReadAsByteArrayAsync();
            var ext = resp.Content.Headers.ContentType?.MediaType == "image/png" ? "png" : "jpg";
            var arquivo = Path.Combine(FileSystem.CacheDirectory, $"ocorrencia-{o.Numero}-{fotoId:N}.{ext}");
            await File.WriteAllBytesAsync(arquivo, bytes);
            fotos.Add(new FotoLinha(fotoId, ImageSource.FromFile(arquivo), arquivo));
        }
        Fotos.Clear();
        foreach (var f in fotos) Fotos.Add(f);
        TemFotos = Fotos.Count > 0;
    }

    [RelayCommand]
    private Task AmpliarFotoAsync(FotoLinha foto) =>
        Launcher.Default.OpenAsync(new OpenFileRequest("Foto da ocorrência", new ReadOnlyFile(foto.Arquivo)));

    [RelayCommand]
    private async Task EncaminharAsync()
    {
        if (_atual is null) return;
        List<ResponsavelDto> lista;
        try { lista = await api.ResponsaveisOcorrenciaAsync(); }
        catch (Exception ex) { Erro = ErroApi.Mensagem(ex); return; }

        var opcoes = lista.Where(r => r.Id != _atual.ResponsavelId).ToList();
        if (opcoes.Count == 0)
        {
            await Shell.Current.DisplayAlertAsync("Encaminhar", "Não há outro administrador nesta associação. Cadastre em Gestão › Equipe.", "OK");
            return;
        }
        var rotulos = opcoes.Select(r => $"{r.Nome} ({r.Perfil})").ToArray();
        var escolha = await Shell.Current.DisplayActionSheetAsync("Encaminhar para", "Cancelar", null, rotulos);
        var idx = Array.IndexOf(rotulos, escolha);
        if (idx < 0) return;

        var obs = await Shell.Current.DisplayPromptAsync("Encaminhar ocorrência",
            $"Orientação para {opcoes[idx].Nome} (opcional):", "Encaminhar", "Cancelar", maxLength: 2000);
        if (obs is null) return;
        await Executar(async () => Mostrar(await api.EncaminharOcorrenciaAsync(_id, new EncaminharOcorrenciaRequest(opcoes[idx].Id, obs))));
    }

    [RelayCommand]
    private async Task MudarEtapaAsync()
    {
        if (_atual is null) return;
        var opcoes = new List<(string Rotulo, string Status, string Pergunta)>();
        if (_atual.Status != "EmAndamento")
            opcoes.Add(_atual.Status == "Resolvida"
                ? ("Reabrir (Em andamento)", "EmAndamento", "Por que a ocorrência está sendo reaberta?")
                : ("Em andamento", "EmAndamento", "Observação (opcional):"));
        if (_atual.Status != "Resolvida") opcoes.Add(("Resolvida", "Resolvida", "O que foi feito para resolver?"));

        var escolha = await Shell.Current.DisplayActionSheetAsync("Mudar etapa", "Cancelar", null, opcoes.Select(o => o.Rotulo).ToArray());
        var op = opcoes.FirstOrDefault(o => o.Rotulo == escolha);
        if (op.Status is null) return;

        var obs = await Shell.Current.DisplayPromptAsync(op.Rotulo, op.Pergunta, "Confirmar", "Cancelar", maxLength: 2000);
        if (obs is null) return;
        await Executar(async () => Mostrar(await api.AlterarStatusOcorrenciaAsync(_id, new AlterarStatusOcorrenciaRequest(op.Status, obs))));
    }

    [RelayCommand]
    private async Task ComentarAsync()
    {
        var texto = await Shell.Current.DisplayPromptAsync("Comentário", "Escreva o comentário:", "Enviar", "Cancelar", maxLength: 2000);
        if (string.IsNullOrWhiteSpace(texto)) return;
        await Executar(async () => Mostrar(await api.ComentarOcorrenciaAsync(_id, new ComentarioOcorrenciaRequest(texto))));
    }

    private async Task Executar(Func<Task> acao)
    {
        Erro = null;
        Ocupado = true;
        try { await acao(); }
        catch (Exception ex) { Erro = ErroApi.Mensagem(ex); }
        finally { Ocupado = false; }
    }
}
