namespace ClubeDaOnca.Domain.Seguranca;

/// <summary>Regras da equipe da associação (quem administra, atende na secretaria e trabalha na portaria).</summary>
public static class RegrasEquipe
{
    /// <summary>Perfis que o Admin da unidade pode dar; os de sócio e o SuperAdmin ficam fora desta tela.</summary>
    public static readonly IReadOnlyList<string> PerfisEquipe = [Perfis.Admin, Perfis.Secretaria, Perfis.Portaria];

    /// <summary>Converte os perfis informados para a grafia oficial; nulo se algum não for de equipe.</summary>
    public static List<string>? Normalizar(IEnumerable<string>? perfis)
    {
        var lista = new List<string>();
        foreach (var p in perfis ?? [])
        {
            var oficial = PerfisEquipe.FirstOrDefault(e => string.Equals(e, p?.Trim(), StringComparison.OrdinalIgnoreCase));
            if (oficial is null) return null;
            if (!lista.Contains(oficial)) lista.Add(oficial);
        }
        return lista;
    }

    /// <summary>
    /// Motivo pelo qual a mudança deixaria a unidade sem administração ou o próprio Admin sem acesso (nulo = permitida).
    /// </summary>
    /// <param name="ehVoce">A conta alterada é de quem está fazendo a mudança.</param>
    /// <param name="continuaAdminAtivo">Depois da mudança, a conta ainda é Admin e não está bloqueada.</param>
    /// <param name="outrosAdminsAtivos">Quantos outros Admins ativos a unidade tem.</param>
    public static string? ValidarMudanca(bool ehVoce, bool eraAdminAtivo, bool continuaAdminAtivo, int outrosAdminsAtivos) => true switch
    {
        _ when ehVoce && eraAdminAtivo && !continuaAdminAtivo => "Você não pode tirar o seu próprio acesso de administrador. Peça a outro Admin.",
        _ when eraAdminAtivo && !continuaAdminAtivo && outrosAdminsAtivos == 0 => "A unidade precisa ter pelo menos um administrador ativo.",
        _ => null,
    };
}
