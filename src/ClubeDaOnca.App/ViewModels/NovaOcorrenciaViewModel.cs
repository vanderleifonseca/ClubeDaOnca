using System.Collections.ObjectModel;
using ClubeDaOnca.App.Services;
using ClubeDaOnca.Contracts.Ocorrencias;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Refit;

namespace ClubeDaOnca.App.ViewModels;

public sealed record OpcaoCategoria(string Valor, string Nome)
{
    public static readonly IReadOnlyList<OpcaoCategoria> Todas =
    [
        new("Manutencao", "🔧 Manutenção"),
        new("Seguranca", "🛡️ Segurança"),
        new("Limpeza", "🧹 Limpeza"),
        new("Conduta", "🗣️ Conduta"),
        new("Outros", "📌 Outros"),
    ];

    public static string NomeDe(string valor) => Todas.FirstOrDefault(c => c.Valor == valor)?.Nome ?? valor;
}

public sealed class FotoNova(byte[] conteudo, string nome, string tipo)
{
    public byte[] Conteudo { get; } = conteudo;
    public string Nome { get; } = nome;
    public string Tipo { get; } = tipo;
    public ImageSource Miniatura { get; } = ImageSource.FromStream(() => new MemoryStream(conteudo));
}

/// <summary>Formulário do popup de nova ocorrência (qualquer usuário logado).</summary>
public partial class NovaOcorrenciaViewModel(IClubeApi api) : ObservableObject
{
    private const int MaximoFotos = 3;
    private const int TamanhoMaximo = 2 * 1024 * 1024;

    /// <summary>Fecha o popup devolvendo a ocorrência criada (nulo = cancelou). Definido pelo popup.</summary>
    public Func<OcorrenciaCriadaResponse?, Task>? Fechar { get; set; }

    public IReadOnlyList<OpcaoCategoria> Categorias => OpcaoCategoria.Todas;
    public ObservableCollection<FotoNova> Fotos { get; } = [];

    [ObservableProperty] public partial string Titulo { get; set; } = string.Empty;
    [ObservableProperty] public partial OpcaoCategoria? Categoria { get; set; }
    [ObservableProperty] public partial string Local { get; set; } = string.Empty;
    [ObservableProperty] public partial string Descricao { get; set; } = string.Empty;
    [ObservableProperty] public partial bool Enviando { get; set; }
    [ObservableProperty] public partial string? Erro { get; set; }
    [ObservableProperty] public partial bool PodeAdicionarFoto { get; set; } = true;

    public bool PodeTirarFoto => MediaPicker.Default.IsCaptureSupported;

    // Reduz no próprio aparelho e tira a localização (GPS) da foto.
    private static MediaPickerOptions Opcoes(int limite = 1) => new()
    {
        Title = "Foto da ocorrência", MaximumWidth = 1600, MaximumHeight = 1600, CompressionQuality = 80,
        RotateImage = true, PreserveMetaData = false, SelectionLimit = limite,
    };

    [RelayCommand]
    private async Task TirarFotoAsync()
    {
        try
        {
            if (await Permissions.RequestAsync<Permissions.Camera>() != PermissionStatus.Granted)
            {
                Erro = "Permita o uso da câmera para fotografar a ocorrência.";
                return;
            }
            if (await MediaPicker.Default.CapturePhotoAsync(Opcoes()) is { } foto) await AdicionarAsync([foto]);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException)
        {
            Erro = "Não foi possível abrir a câmera neste aparelho.";
        }
    }

    [RelayCommand]
    private async Task EscolherFotosAsync()
    {
        try
        {
            var fotos = await MediaPicker.Default.PickPhotosAsync(Opcoes(MaximoFotos - Fotos.Count));
            await AdicionarAsync(fotos?.Where(f => f is not null).ToList() ?? []);
        }
        catch (Exception ex) when (ex is FeatureNotSupportedException or PermissionException)
        {
            Erro = "Não foi possível abrir a galeria neste aparelho.";
        }
    }

    private async Task AdicionarAsync(IReadOnlyList<FileResult> arquivos)
    {
        Erro = null;
        foreach (var f in arquivos)
        {
            if (Fotos.Count >= MaximoFotos) { Erro = $"Até {MaximoFotos} fotos por ocorrência."; break; }
            await using var stream = await f.OpenReadAsync();
            using var ms = new MemoryStream();
            await stream.CopyToAsync(ms);
            var bytes = ms.ToArray();
            if (bytes.Length > TamanhoMaximo) { Erro = "Foto grande demais (máximo 2 MB)."; continue; }
            var tipo = bytes.Length > 1 && bytes[0] == 0x89 ? "image/png" : "image/jpeg";
            Fotos.Add(new FotoNova(bytes, Path.GetFileName(f.FileName), tipo));
        }
        PodeAdicionarFoto = Fotos.Count < MaximoFotos;
    }

    [RelayCommand]
    private void RemoverFoto(FotoNova foto)
    {
        Fotos.Remove(foto);
        PodeAdicionarFoto = Fotos.Count < MaximoFotos;
    }

    [RelayCommand]
    private Task CancelarAsync() => Fechar?.Invoke(null) ?? Task.CompletedTask;

    [RelayCommand]
    private async Task EnviarAsync()
    {
        Erro = null;
        if (string.IsNullOrWhiteSpace(Titulo)) { Erro = "Informe um título curto."; return; }
        if (Categoria is null) { Erro = "Escolha a categoria."; return; }
        if (string.IsNullOrWhiteSpace(Descricao)) { Erro = "Descreva o que aconteceu."; return; }

        Enviando = true;
        try
        {
            var partes = Fotos.Select(f => new ByteArrayPart(f.Conteudo, f.Nome, f.Tipo)).ToList();
            var criada = await api.RegistrarOcorrenciaAsync(Titulo.Trim(), Categoria.Valor, Local.Trim(), Descricao.Trim(), partes);
            if (Fechar is not null) await Fechar(criada);
        }
        catch (Exception ex)
        {
            Erro = ErroApi.Mensagem(ex);
        }
        finally
        {
            Enviando = false;
        }
    }
}
