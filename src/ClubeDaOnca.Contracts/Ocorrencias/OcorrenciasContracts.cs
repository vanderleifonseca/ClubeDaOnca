namespace ClubeDaOnca.Contracts.Ocorrencias;

/// <summary>Campos do formulário (multipart) de nova ocorrência; as fotos vão no campo "fotos".</summary>
/// <param name="Categoria">Manutencao, Seguranca, Limpeza, Conduta ou Outros.</param>
public sealed record NovaOcorrenciaRequest(string Titulo, string Categoria, string? Local, string Descricao);

public sealed record OcorrenciaCriadaResponse(Guid Id, int Numero);

/// <param name="Status">Aberta, EmAndamento ou Resolvida.</param>
public sealed record OcorrenciaResumoDto(
    Guid Id,
    int Numero,
    string Titulo,
    string Categoria,
    string? Local,
    string Status,
    string RegistradaPor,
    DateTimeOffset RegistradaEm,
    string? ResponsavelId,
    string? Responsavel,
    DateTimeOffset AtualizadaEm,
    int Fotos);

/// <param name="Tipo">Registrada, Encaminhada, StatusAlterado ou Comentario.</param>
public sealed record MovimentoOcorrenciaDto(
    string Tipo,
    string Autor,
    DateTimeOffset Em,
    string? Texto,
    string? StatusAnterior,
    string? StatusNovo,
    string? Para);

public sealed record OcorrenciaDto(
    Guid Id,
    int Numero,
    string Titulo,
    string Categoria,
    string? Local,
    string Descricao,
    string Status,
    string RegistradaPorId,
    string RegistradaPor,
    DateTimeOffset RegistradaEm,
    string? ResponsavelId,
    string? Responsavel,
    DateTimeOffset AtualizadaEm,
    DateTimeOffset? ResolvidaEm,
    IReadOnlyList<Guid> Fotos,
    IReadOnlyList<MovimentoOcorrenciaDto> Movimentos);

public sealed record EncaminharOcorrenciaRequest(string ResponsavelId, string? Observacao);

/// <param name="Status">EmAndamento ou Resolvida (observação obrigatória para concluir e para reabrir).</param>
public sealed record AlterarStatusOcorrenciaRequest(string Status, string? Observacao);

public sealed record ComentarioOcorrenciaRequest(string Texto);

/// <summary>Usuário administrador (Admin) que pode receber ocorrências.</summary>
public sealed record ResponsavelDto(string Id, string Nome, string Perfil);

public sealed record ResumoOcorrenciasDto(int Abertas, int EmAndamento, int ParaMim);
