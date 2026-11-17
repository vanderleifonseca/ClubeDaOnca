using System.Security.Claims;
using ClubeDaOnca.Api.Infrastructure.Contratos;
using ClubeDaOnca.Domain.Seguranca;
using Microsoft.AspNetCore.Authorization;

namespace ClubeDaOnca.Api.Infrastructure.Eventos;

/// <summary>
/// Quem cuida dos eventos: o Admin da unidade e o presidente do mandato vigente (gerenciam tudo);
/// a secretaria só consulta eventos e reservas.
/// </summary>
public sealed class PermissaoEventos(ContratoService contratos)
{
    public const string PoliticaGerir = "GerirEventos";
    public const string PoliticaVer = "VerEventos";

    private bool? _presidente;

    public async Task<bool> EhPresidenteAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        _presidente ??= ContratoService.EhPresidente(user, await contratos.MandatoVigenteAsync(ct));

    public async Task<bool> PodeGerirAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        user.IsInRole(Perfis.Admin) || await EhPresidenteAsync(user, ct);

    public async Task<bool> PodeVerAsync(ClaimsPrincipal user, CancellationToken ct = default) =>
        user.IsInRole(Perfis.Secretaria) || await PodeGerirAsync(user, ct);
}

public sealed class GerirEventosRequisito : IAuthorizationRequirement;

public sealed class VerEventosRequisito : IAuthorizationRequirement;

public sealed class GerirEventosHandler(PermissaoEventos permissao) : AuthorizationHandler<GerirEventosRequisito>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, GerirEventosRequisito requirement)
    {
        if (await permissao.PodeGerirAsync(context.User)) context.Succeed(requirement);
    }
}

public sealed class VerEventosHandler(PermissaoEventos permissao) : AuthorizationHandler<VerEventosRequisito>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, VerEventosRequisito requirement)
    {
        if (await permissao.PodeVerAsync(context.User)) context.Succeed(requirement);
    }
}
