using ClubeDaOnca.Domain.Associacoes;

namespace ClubeDaOnca.Domain.Ocorrencias;

public enum CategoriaOcorrencia
{
    Manutencao = 1,
    Seguranca = 2,
    Limpeza = 3,
    Conduta = 4,
    Outros = 5,
}

public enum StatusOcorrencia
{
    Aberta = 1,
    EmAndamento = 2,
    Resolvida = 3,
}

public enum TipoMovimentoOcorrencia
{
    Registrada = 1,
    Encaminhada = 2,
    StatusAlterado = 3,
    Comentario = 4,
}

/// <summary>
/// Ocorrência registrada por qualquer usuário e tratada pela administração (usuários Admin da associação).
/// O registro original não muda; tudo o que acontece depois entra na linha do tempo (<see cref="Movimentos"/>).
/// </summary>
public sealed class Ocorrencia : IPertenceAssociacao
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid AssociacaoId { get; set; }

    /// <summary>Número sequencial gerado pelo banco ("ocorrência nº 1024").</summary>
    public int Numero { get; set; }

    public string Titulo { get; set; } = string.Empty;
    public CategoriaOcorrencia Categoria { get; set; } = CategoriaOcorrencia.Outros;
    public string? Local { get; set; }
    public string Descricao { get; set; } = string.Empty;
    public StatusOcorrencia Status { get; set; } = StatusOcorrencia.Aberta;

    /// <summary>Usuário que registrou (do token, nunca informado pelo app).</summary>
    public string RegistradaPorId { get; set; } = string.Empty;
    public string RegistradaPorNome { get; set; } = string.Empty;
    public DateTimeOffset RegistradaEm { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Usuário Admin que cuida da ocorrência; nulo = caixa geral da administração.</summary>
    public string? ResponsavelId { get; set; }
    public string? ResponsavelNome { get; set; }

    public DateTimeOffset AtualizadaEm { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ResolvidaEm { get; set; }

    public List<FotoOcorrencia> Fotos { get; set; } = [];
    public List<MovimentoOcorrencia> Movimentos { get; set; } = [];
}

public sealed class FotoOcorrencia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OcorrenciaId { get; set; }
    public byte[] Conteudo { get; set; } = [];
    public string TipoConteudo { get; set; } = "image/jpeg";
    public int Tamanho { get; set; }
    public DateTimeOffset EnviadaEm { get; set; } = DateTimeOffset.UtcNow;
}

/// <summary>Passo da linha do tempo da ocorrência: quem fez, quando e o quê. Só é incluído, nunca alterado.</summary>
public sealed class MovimentoOcorrencia
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid OcorrenciaId { get; set; }
    public TipoMovimentoOcorrencia Tipo { get; set; }
    public string? AutorId { get; set; }
    public string AutorNome { get; set; } = string.Empty;
    public DateTimeOffset Em { get; set; } = DateTimeOffset.UtcNow;
    public string? Texto { get; set; }
    public StatusOcorrencia? StatusAnterior { get; set; }
    public StatusOcorrencia? StatusNovo { get; set; }

    /// <summary>No encaminhamento: para quem foi.</summary>
    public string? ParaId { get; set; }
    public string? ParaNome { get; set; }
}

public static class RegrasOcorrencia
{
    public const int MaximoFotos = 3;
    public const int TamanhoMaximoFoto = 2 * 1024 * 1024;
    public static readonly IReadOnlyList<string> TiposFoto = ["image/jpeg", "image/png"];

    /// <summary>Motivo pelo qual a mudança de etapa não é aceita (nulo = aceita).</summary>
    public static string? ValidarTransicao(StatusOcorrencia atual, StatusOcorrencia novo, string? observacao) => (atual, novo) switch
    {
        _ when atual == novo => "A ocorrência já está nessa etapa.",
        (_, StatusOcorrencia.Aberta) => "Uma ocorrência não volta para Aberta; use Em andamento.",
        (_, StatusOcorrencia.Resolvida) when string.IsNullOrWhiteSpace(observacao) => "Descreva a solução para concluir a ocorrência.",
        (StatusOcorrencia.Resolvida, _) when string.IsNullOrWhiteSpace(observacao) => "Informe o motivo para reabrir a ocorrência.",
        _ => null,
    };

    /// <summary>Quem registrou vê as próprias; a administração (Admin) vê todas da associação.</summary>
    public static bool PodeVer(Ocorrencia o, string? usuarioId, bool administrativo) =>
        administrativo || (usuarioId is not null && o.RegistradaPorId == usuarioId);

    /// <summary>Confere uma foto pelo tipo, tamanho e assinatura do arquivo (nulo = válida).</summary>
    public static string? ValidarFoto(ReadOnlySpan<byte> conteudo, string? tipoInformado)
    {
        if (conteudo.Length == 0) return "Foto vazia.";
        if (conteudo.Length > TamanhoMaximoFoto) return "Cada foto pode ter no máximo 2 MB.";
        var tipo = TipoPelaAssinatura(conteudo);
        if (tipo is null) return "Envie fotos em JPEG ou PNG.";
        if (tipoInformado is { Length: > 0 } t && !TiposFoto.Contains(t.ToLowerInvariant())) return "Envie fotos em JPEG ou PNG.";
        return null;
    }

    /// <summary>Tipo real do arquivo pelos primeiros bytes (JPEG ou PNG), ou nulo.</summary>
    public static string? TipoPelaAssinatura(ReadOnlySpan<byte> c)
    {
        if (c.Length >= 3 && c[0] == 0xFF && c[1] == 0xD8 && c[2] == 0xFF) return "image/jpeg";
        if (c.Length >= 8 && c[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })) return "image/png";
        return null;
    }

    public static string NomeStatus(StatusOcorrencia s) => s switch
    {
        StatusOcorrencia.Aberta => "Aberta",
        StatusOcorrencia.EmAndamento => "Em andamento",
        _ => "Resolvida",
    };

    public static string NomeCategoria(CategoriaOcorrencia c) => c switch
    {
        CategoriaOcorrencia.Manutencao => "Manutenção",
        CategoriaOcorrencia.Seguranca => "Segurança",
        CategoriaOcorrencia.Limpeza => "Limpeza",
        CategoriaOcorrencia.Conduta => "Conduta",
        _ => "Outros",
    };
}
