using ClubeDaOnca.Api.Infrastructure;
using ClubeDaOnca.Contracts.Equipe;
using ClubeDaOnca.Domain.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClubeDaOnca.Api.Endpoints;

/// <summary>
/// Equipe da associação: o Admin cadastra administradores, secretaria e portaria da sua unidade,
/// troca perfis, bloqueia o acesso e gera nova senha provisória. Contas não são apagadas (o histórico cita quem fez).
/// </summary>
public static class EquipeEndpoints
{
    public static IEndpointRouteBuilder MapEquipe(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/equipe").WithTags("Equipe").RequireAuthorization("Admin");

        g.MapGet("/", async (UserManager<Usuario> users, TenantAtual tenant, UsuarioAtual eu) =>
        {
            var membros = await MembrosAsync(users, tenant.Exigir());
            var lista = new List<MembroEquipeDto>();
            foreach (var u in membros.OrderBy(u => u.Nome)) lista.Add(await DtoAsync(users, u, eu.Id));
            return Results.Ok(lista);
        });

        g.MapPost("/", async (NovoMembroEquipeRequest req, UserManager<Usuario> users, TenantAtual tenant, UsuarioAtual eu) =>
        {
            var erros = new Dictionary<string, string[]>();
            var nome = req.Nome?.Trim() ?? string.Empty;
            var email = req.Email?.Trim().ToLowerInvariant() ?? string.Empty;
            if (nome.Length is < 3 or > 150) erros["nome"] = ["Informe o nome (3 a 150 caracteres)."];
            if (!email.Contains('@') || email.Length > 256) erros["email"] = ["Informe um e-mail válido. Ele é o login de acesso."];
            var perfis = RegrasEquipe.Normalizar(req.Perfis);
            if (perfis is null || perfis.Count == 0) erros["perfis"] = ["Escolha o perfil: Admin, Secretaria ou Portaria."];
            if (erros.Count > 0) return Results.ValidationProblem(erros);

            var associacaoId = tenant.Exigir();
            string? senha = null;
            var usuario = await users.FindByEmailAsync(email);
            if (usuario is not null)
            {
                // Conta existente (ex.: sócio que vai ajudar na secretaria): só ganha os perfis.
                if (usuario.AssociacaoId != associacaoId)
                    return Conflito("Este e-mail já tem conta em outra associação.");
                var atuais = await users.GetRolesAsync(usuario);
                if (atuais.Intersect(RegrasEquipe.PerfisEquipe).Any())
                    return Conflito("Esta pessoa já faz parte da equipe. Altere os perfis dela na lista.");
            }
            else
            {
                senha = SociosEndpoints.GerarSenhaProvisoria();
                usuario = new Usuario { UserName = email, Email = email, Nome = nome, AssociacaoId = associacaoId, DeveTrocarSenha = true };
                var criado = await users.CreateAsync(usuario, senha);
                if (!criado.Succeeded)
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["email"] = criado.Errors.Select(e => e.Description).ToArray() });
            }
            await users.AddToRolesAsync(usuario, perfis!);
            return Results.Created($"/equipe/{usuario.Id}", new MembroEquipeCriadoResponse(await DtoAsync(users, usuario, eu.Id), email, senha));
        });

        g.MapPut("/{id}/perfis", async (string id, AlterarPerfisEquipeRequest req, UserManager<Usuario> users, TenantAtual tenant, UsuarioAtual eu) =>
        {
            var perfis = RegrasEquipe.Normalizar(req.Perfis);
            if (perfis is null)
                return Results.ValidationProblem(new Dictionary<string, string[]> { ["perfis"] = ["Use Admin, Secretaria ou Portaria."] });
            var (u, erro) = await BuscarAsync(users, tenant.Exigir(), id);
            if (u is null) return erro!;

            var atuais = (await users.GetRolesAsync(u)).Intersect(RegrasEquipe.PerfisEquipe).ToList();
            var bloqueado = await users.IsLockedOutAsync(u);
            var motivo = RegrasEquipe.ValidarMudanca(u.Id == eu.Id, atuais.Contains(Perfis.Admin) && !bloqueado,
                perfis.Contains(Perfis.Admin) && !bloqueado, await OutrosAdminsAtivosAsync(users, tenant.Exigir(), u.Id));
            if (motivo is not null) return Conflito(motivo);

            var remover = atuais.Except(perfis).ToList();
            var incluir = perfis.Except(atuais).ToList();
            if (remover.Count > 0) await users.RemoveFromRolesAsync(u, remover);
            if (incluir.Count > 0) await users.AddToRolesAsync(u, incluir);
            return Results.Ok(await DtoAsync(users, u, eu.Id));
        });

        g.MapPost("/{id}/bloquear", async (string id, UserManager<Usuario> users, TenantAtual tenant, UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
        {
            var (u, erro) = await BuscarAsync(users, tenant.Exigir(), id);
            if (u is null) return erro!;
            if (u.Id == eu.Id) return Conflito("Você não pode bloquear a sua própria conta.");
            var ehAdmin = await users.IsInRoleAsync(u, Perfis.Admin);
            var motivo = RegrasEquipe.ValidarMudanca(false, ehAdmin && !await users.IsLockedOutAsync(u), false,
                await OutrosAdminsAtivosAsync(users, tenant.Exigir(), u.Id));
            if (motivo is not null) return Conflito(motivo);

            await users.SetLockoutEnabledAsync(u, true);
            await users.SetLockoutEndDateAsync(u, DateTimeOffset.MaxValue);
            await RevogarSessoesAsync(db, u.Id, ct); // sai de todos os aparelhos ao vencer o token atual
            return Results.Ok(await DtoAsync(users, u, eu.Id));
        });

        g.MapPost("/{id}/desbloquear", async (string id, UserManager<Usuario> users, TenantAtual tenant, UsuarioAtual eu) =>
        {
            var (u, erro) = await BuscarAsync(users, tenant.Exigir(), id);
            if (u is null) return erro!;
            await users.SetLockoutEndDateAsync(u, null);
            await users.ResetAccessFailedCountAsync(u);
            return Results.Ok(await DtoAsync(users, u, eu.Id));
        });

        g.MapPost("/{id}/redefinir-senha", async (string id, UserManager<Usuario> users, TenantAtual tenant, UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
        {
            var (u, erro) = await BuscarAsync(users, tenant.Exigir(), id);
            if (u is null) return erro!;
            if (u.Id == eu.Id) return Conflito("Para mudar a sua própria senha, use \"Trocar senha\".");

            var senha = SociosEndpoints.GerarSenhaProvisoria();
            await users.RemovePasswordAsync(u);
            var r = await users.AddPasswordAsync(u, senha);
            if (!r.Succeeded) return Results.Problem(title: string.Join(" ", r.Errors.Select(e => e.Description)));
            u.DeveTrocarSenha = true;
            await users.UpdateAsync(u);
            await RevogarSessoesAsync(db, u.Id, ct);
            return Results.Ok(new SenhaRedefinidaResponse(u.Email!, senha));
        });

        return app;
    }

    private static async Task<List<Usuario>> MembrosAsync(UserManager<Usuario> users, Guid associacaoId)
    {
        var membros = new Dictionary<string, Usuario>();
        foreach (var perfil in RegrasEquipe.PerfisEquipe)
            foreach (var u in await users.GetUsersInRoleAsync(perfil))
                if (u.AssociacaoId == associacaoId) membros.TryAdd(u.Id, u);
        return membros.Values.ToList();
    }

    private static async Task<int> OutrosAdminsAtivosAsync(UserManager<Usuario> users, Guid associacaoId, string exceto) =>
        (await users.GetUsersInRoleAsync(Perfis.Admin))
            .Count(u => u.AssociacaoId == associacaoId && u.Id != exceto && !(u.LockoutEnd > DateTimeOffset.UtcNow));

    /// <summary>Conta da mesma unidade e da equipe; as demais (sócios, outras unidades) respondem 404.</summary>
    private static async Task<(Usuario? Usuario, IResult? Erro)> BuscarAsync(UserManager<Usuario> users, Guid associacaoId, string id)
    {
        var u = await users.FindByIdAsync(id);
        if (u is null || u.AssociacaoId != associacaoId || !(await users.GetRolesAsync(u)).Intersect(RegrasEquipe.PerfisEquipe).Any())
            return (null, Results.Problem(title: "Pessoa da equipe não encontrada.", statusCode: StatusCodes.Status404NotFound));
        return (u, null);
    }

    private static Task RevogarSessoesAsync(AppDbContext db, string usuarioId, CancellationToken ct) =>
        db.RefreshTokens.Where(r => r.UsuarioId == usuarioId && r.RevogadoEm == null)
            .ExecuteUpdateAsync(s => s.SetProperty(r => r.RevogadoEm, DateTimeOffset.UtcNow), ct);

    private static async Task<MembroEquipeDto> DtoAsync(UserManager<Usuario> users, Usuario u, string? euId)
    {
        var perfis = await users.GetRolesAsync(u);
        return new MembroEquipeDto(u.Id, u.Nome, u.Email ?? string.Empty,
            RegrasEquipe.PerfisEquipe.Where(perfis.Contains).ToList(),
            u.LockoutEnd > DateTimeOffset.UtcNow, u.DeveTrocarSenha,
            perfis.Contains(Perfis.Titular) || perfis.Contains(Perfis.Dependente), u.Id == euId);
    }

    private static IResult Conflito(string mensagem) => Results.Problem(title: mensagem, statusCode: StatusCodes.Status409Conflict);
}
