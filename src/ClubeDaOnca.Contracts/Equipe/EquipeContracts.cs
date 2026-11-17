namespace ClubeDaOnca.Contracts.Equipe;

/// <summary>Pessoa da equipe da associação (Admin, Secretaria ou Portaria).</summary>
/// <param name="Perfis">Perfis de equipe da conta (Admin, Secretaria, Portaria).</param>
/// <param name="EhSocio">A conta também é de um sócio (titular ou dependente).</param>
/// <param name="EhVoce">É a conta de quem está consultando.</param>
public sealed record MembroEquipeDto(
    string Id,
    string Nome,
    string Email,
    IReadOnlyList<string> Perfis,
    bool Bloqueado,
    bool AguardandoPrimeiroAcesso,
    bool EhSocio,
    bool EhVoce);

/// <param name="Perfis">Um ou mais entre Admin, Secretaria e Portaria.</param>
public sealed record NovoMembroEquipeRequest(string Nome, string Email, IReadOnlyList<string> Perfis);

/// <param name="SenhaProvisoria">Nula quando a pessoa já tinha conta (ex.: um sócio que passou a ajudar na secretaria).</param>
public sealed record MembroEquipeCriadoResponse(MembroEquipeDto Membro, string Login, string? SenhaProvisoria);

/// <param name="Perfis">Perfis de equipe; lista vazia tira a pessoa da equipe (a conta de sócio, se houver, continua).</param>
public sealed record AlterarPerfisEquipeRequest(IReadOnlyList<string> Perfis);

public sealed record SenhaRedefinidaResponse(string Login, string SenhaProvisoria);
