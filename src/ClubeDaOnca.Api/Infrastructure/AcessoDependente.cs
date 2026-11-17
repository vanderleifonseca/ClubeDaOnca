using ClubeDaOnca.Api.Endpoints;
using ClubeDaOnca.Contracts.Socios;
using ClubeDaOnca.Domain.Seguranca;
using ClubeDaOnca.Domain.Socios;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClubeDaOnca.Api.Infrastructure;

/// <summary>
/// Login próprio do dependente (opcional, só com e-mail): perfil Dependente, senha provisória e
/// acesso apenas à própria carteirinha. Inativar o dependente bloqueia a conta.
/// </summary>
public static class AcessoDependente
{
    public static string MensagemEmailEmUso(string email) =>
        $"O e-mail {email} já tem conta no sistema. Use outro e-mail para o dependente ou deixe em branco.";

    /// <summary>Cria a conta se o dependente tem e-mail e ainda não tem login. Devolve o acesso criado ou o erro.</summary>
    public static async Task<(AcessoDependenteDto? Acesso, string? Erro)> CriarAsync(
        UserManager<Usuario> users, AppDbContext db, Dependente d, CancellationToken ct)
    {
        if (d.UsuarioId is not null || string.IsNullOrWhiteSpace(d.Email)) return (null, null);
        var email = d.Email.Trim().ToLowerInvariant();
        if (await users.FindByEmailAsync(email) is not null) return (null, MensagemEmailEmUso(email));

        var senha = SociosEndpoints.GerarSenhaProvisoria();
        var usuario = new Usuario
        {
            UserName = email, Email = email, Nome = d.Nome, PhoneNumber = d.Telefone, DeveTrocarSenha = true,
            AssociacaoId = db.Tenant.Exigir(),
        };
        // CPF no login só se nenhuma outra conta usa (ex.: funcionário com o mesmo CPF).
        if (d.Cpf is { } cpf && !await users.Users.AnyAsync(u => u.Cpf == cpf, ct)) usuario.Cpf = cpf;

        var criado = await users.CreateAsync(usuario, senha);
        if (!criado.Succeeded) return (null, string.Join(" ", criado.Errors.Select(e => e.Description)));
        await users.AddToRoleAsync(usuario, Perfis.Dependente);

        d.UsuarioId = usuario.Id;
        await db.SaveChangesAsync(ct);
        return (new AcessoDependenteDto(d.Id, d.Nome, email, senha), null);
    }

    /// <summary>Nova senha provisória para quem já tem login (as sessões abertas são encerradas).</summary>
    public static async Task<(AcessoDependenteDto? Acesso, string? Erro)> RedefinirSenhaAsync(
        UserManager<Usuario> users, AppDbContext db, Dependente d, CancellationToken ct)
    {
        var usuario = d.UsuarioId is null ? null : await users.FindByIdAsync(d.UsuarioId);
        if (usuario is null) return (null, "O dependente ainda não tem login.");
        var senha = SociosEndpoints.GerarSenhaProvisoria();
        await users.RemovePasswordAsync(usuario);
        var r = await users.AddPasswordAsync(usuario, senha);
        if (!r.Succeeded) return (null, string.Join(" ", r.Errors.Select(e => e.Description)));
        usuario.DeveTrocarSenha = true;
        await users.UpdateAsync(usuario);
        await RevogarSessoesAsync(db, usuario.Id, ct);
        return (new AcessoDependenteDto(d.Id, d.Nome, usuario.Email!, senha), null);
    }

    /// <summary>Acompanha o e-mail do cadastro no login (o e-mail é o usuário de acesso).</summary>
    public static async Task<string?> AtualizarEmailAsync(UserManager<Usuario> users, Dependente d, string? novoEmail)
    {
        if (d.UsuarioId is null) return null;
        if (string.IsNullOrWhiteSpace(novoEmail)) return "Este dependente tem login próprio: o e-mail é obrigatório.";
        var usuario = await users.FindByIdAsync(d.UsuarioId);
        if (usuario is null || string.Equals(usuario.Email, novoEmail, StringComparison.OrdinalIgnoreCase)) return null;
        if (await users.FindByEmailAsync(novoEmail) is not null) return MensagemEmailEmUso(novoEmail);
        await users.SetEmailAsync(usuario, novoEmail);
        await users.SetUserNameAsync(usuario, novoEmail);
        return null;
    }

    /// <summary>Dependente inativo não entra no app; reativado, volta a entrar.</summary>
    public static async Task DefinirBloqueioAsync(UserManager<Usuario> users, AppDbContext db, Dependente d, bool ativo, CancellationToken ct)
    {
        var usuario = d.UsuarioId is null ? null : await users.FindByIdAsync(d.UsuarioId);
        if (usuario is null) return;
        if (ativo)
        {
            await users.SetLockoutEndDateAsync(usuario, null);
            return;
        }
        await users.SetLockoutEnabledAsync(usuario, true);
        await users.SetLockoutEndDateAsync(usuario, DateTimeOffset.MaxValue);
        await RevogarSessoesAsync(db, usuario.Id, ct);
    }

    public static Task RevogarSessoesAsync(AppDbContext db, string usuarioId, CancellationToken ct) =>
        db.RefreshTokens.Where(r => r.UsuarioId == usuarioId && r.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevogadoEm, DateTimeOffset.UtcNow), ct);
}
