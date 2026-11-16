using System.Net;
using System.Security.Claims;
using ClubeDaOnca.Api.Infrastructure;
using ClubeDaOnca.Api.Infrastructure.Email;
using ClubeDaOnca.Contracts.Ocorrencias;
using ClubeDaOnca.Domain.Ocorrencias;
using ClubeDaOnca.Domain.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ClubeDaOnca.Api.Endpoints;

/// <summary>
/// Ocorrências: qualquer usuário registra (com até 3 fotos) e elas vão para a administração, isto é,
/// os usuários Admin da associação (não a secretaria). Eles encaminham entre si, mudam a etapa e comentam.
/// Tudo fica na linha do tempo com autor e data.
/// </summary>
public static class OcorrenciasEndpoints
{
    public static IEndpointRouteBuilder MapOcorrencias(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/ocorrencias").WithTags("Ocorrências").RequireAuthorization();

        g.MapPost("/", RegistrarAsync);

        g.MapGet("/minhas", async (UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
            Results.Ok(await Resumos(db.Ocorrencias.Where(o => o.RegistradaPorId == eu.Id)).ToListAsync(ct)));

        g.MapGet("/", async (string? status, bool? paraMim, UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
        {
            var q = db.Ocorrencias.AsQueryable();
            if (!string.IsNullOrWhiteSpace(status))
            {
                if (!TentarStatus(status, out var s))
                    return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use Aberta, EmAndamento ou Resolvida."] });
                q = q.Where(o => o.Status == s);
            }
            if (paraMim == true) q = q.Where(o => o.ResponsavelId == eu.Id);
            return Results.Ok(await Resumos(q).ToListAsync(ct));
        }).RequireAuthorization("Admin");

        g.MapGet("/resumo", async (UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
        {
            var abertas = await db.Ocorrencias.CountAsync(o => o.Status == StatusOcorrencia.Aberta, ct);
            var andamento = await db.Ocorrencias.CountAsync(o => o.Status == StatusOcorrencia.EmAndamento, ct);
            var paraMim = await db.Ocorrencias.CountAsync(o => o.ResponsavelId == eu.Id && o.Status != StatusOcorrencia.Resolvida, ct);
            return Results.Ok(new ResumoOcorrenciasDto(abertas, andamento, paraMim));
        }).RequireAuthorization("Admin");

        g.MapGet("/responsaveis", async (UserManager<Usuario> users, TenantAtual tenant, CancellationToken ct) =>
            Results.Ok(await ResponsaveisAsync(users, tenant.Exigir()))).RequireAuthorization("Admin");

        g.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
        {
            var o = await db.Ocorrencias.AsNoTracking().Include(x => x.Movimentos).FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null || !RegrasOcorrencia.PodeVer(o, eu.Id, Administrativo(user))) return NaoEncontrada();
            var fotos = await db.FotosOcorrencia.AsNoTracking().Where(f => f.OcorrenciaId == id)
                .OrderBy(f => f.EnviadaEm).Select(f => f.Id).ToListAsync(ct);
            return Results.Ok(Dto(o, fotos));
        });

        g.MapGet("/{id:guid}/fotos/{fotoId:guid}", async (Guid id, Guid fotoId, ClaimsPrincipal user, UsuarioAtual eu, AppDbContext db, CancellationToken ct) =>
        {
            var o = await db.Ocorrencias.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
            if (o is null || !RegrasOcorrencia.PodeVer(o, eu.Id, Administrativo(user))) return NaoEncontrada();
            var f = await db.FotosOcorrencia.AsNoTracking().FirstOrDefaultAsync(x => x.Id == fotoId && x.OcorrenciaId == id, ct);
            return f is null ? NaoEncontrada() : Results.File(f.Conteudo, f.TipoConteudo);
        });

        g.MapPost("/{id:guid}/encaminhar", EncaminharAsync).RequireAuthorization("Admin");
        g.MapPost("/{id:guid}/status", AlterarStatusAsync).RequireAuthorization("Admin");
        g.MapPost("/{id:guid}/comentarios", ComentarAsync);

        return app;
    }

    private static async Task<IResult> RegistrarAsync(HttpRequest http, UsuarioAtual eu, AppDbContext db, CancellationToken ct)
    {
        if (!http.HasFormContentType)
            return Results.Problem(title: "Envie a ocorrência como formulário (multipart/form-data).", statusCode: StatusCodes.Status415UnsupportedMediaType);
        if (eu.Id is null) return Results.Unauthorized();

        var form = await http.ReadFormAsync(ct);
        var titulo = form["titulo"].ToString().Trim();
        var descricao = form["descricao"].ToString().Trim();
        var local = form["local"].ToString().Trim();
        var categoriaTexto = form["categoria"].ToString();

        var erros = new Dictionary<string, string[]>();
        if (titulo.Length is 0 or > 150) erros["titulo"] = ["Informe o título (até 150 caracteres)."];
        if (descricao.Length is 0 or > 2000) erros["descricao"] = ["Descreva a ocorrência (até 2000 caracteres)."];
        if (local.Length > 100) erros["local"] = ["O local pode ter até 100 caracteres."];
        if (!Enum.TryParse<CategoriaOcorrencia>(categoriaTexto, true, out var categoria) || !Enum.IsDefined(categoria) || int.TryParse(categoriaTexto, out _))
            erros["categoria"] = ["Categoria inválida. Use Manutencao, Seguranca, Limpeza, Conduta ou Outros."];

        var fotos = new List<FotoOcorrencia>();
        if (form.Files.Count > RegrasOcorrencia.MaximoFotos)
            erros["fotos"] = [$"Envie no máximo {RegrasOcorrencia.MaximoFotos} fotos."];
        else
            foreach (var arquivo in form.Files)
            {
                if (arquivo.Length > RegrasOcorrencia.TamanhoMaximoFoto) { erros["fotos"] = ["Cada foto pode ter no máximo 2 MB."]; break; }
                using var ms = new MemoryStream();
                await arquivo.CopyToAsync(ms, ct);
                var bytes = ms.ToArray();
                if (RegrasOcorrencia.ValidarFoto(bytes, arquivo.ContentType) is { } erroFoto) { erros["fotos"] = [erroFoto]; break; }
                fotos.Add(new FotoOcorrencia { Conteudo = bytes, Tamanho = bytes.Length, TipoConteudo = RegrasOcorrencia.TipoPelaAssinatura(bytes)! });
            }
        if (erros.Count > 0) return Results.ValidationProblem(erros);

        var agora = DateTimeOffset.UtcNow;
        var o = new Ocorrencia
        {
            Titulo = titulo, Categoria = categoria, Local = local.Length == 0 ? null : local, Descricao = descricao,
            RegistradaPorId = eu.Id, RegistradaPorNome = eu.Nome, RegistradaEm = agora, AtualizadaEm = agora, Fotos = fotos,
        };
        o.Movimentos.Add(new MovimentoOcorrencia
        {
            Tipo = TipoMovimentoOcorrencia.Registrada, AutorId = eu.Id, AutorNome = eu.Nome, Em = agora, StatusNovo = StatusOcorrencia.Aberta,
        });
        db.Ocorrencias.Add(o);
        await db.SaveChangesAsync(ct);
        return Results.Created($"/ocorrencias/{o.Id}", new OcorrenciaCriadaResponse(o.Id, o.Numero));
    }

    private static async Task<IResult> EncaminharAsync(Guid id, EncaminharOcorrenciaRequest req, UsuarioAtual eu, TenantAtual tenant,
        AppDbContext db, UserManager<Usuario> users, IEnvioEmail email, RelogioClube relogio, ILoggerFactory logs, CancellationToken ct)
    {
        var o = await db.Ocorrencias.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NaoEncontrada();
        var observacao = req.Observacao?.Trim();
        if (observacao?.Length > 2000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["observacao"] = ["A observação pode ter até 2000 caracteres."] });

        var destino = string.IsNullOrWhiteSpace(req.ResponsavelId) ? null : await users.FindByIdAsync(req.ResponsavelId);
        if (destino is null || destino.AssociacaoId != tenant.Exigir()
            || !await users.IsInRoleAsync(destino, Perfis.Admin))
            return Results.ValidationProblem(new Dictionary<string, string[]>
                { ["responsavelId"] = ["Escolha um usuário administrador (Admin) desta associação."] });
        if (destino.Id == o.ResponsavelId)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["responsavelId"] = ["A ocorrência já está com esse usuário."] });

        var agora = DateTimeOffset.UtcNow;
        var statusAnterior = o.Status;
        o.ResponsavelId = destino.Id;
        o.ResponsavelNome = destino.Nome;
        if (o.Status == StatusOcorrencia.Aberta) o.Status = StatusOcorrencia.EmAndamento;
        o.AtualizadaEm = agora;
        db.MovimentosOcorrencia.Add(new MovimentoOcorrencia
        {
            OcorrenciaId = o.Id, Tipo = TipoMovimentoOcorrencia.Encaminhada, AutorId = eu.Id, AutorNome = eu.Nome, Em = agora,
            Texto = string.IsNullOrEmpty(observacao) ? null : observacao, ParaId = destino.Id, ParaNome = destino.Nome,
            StatusAnterior = statusAnterior, StatusNovo = o.Status,
        });
        await db.SaveChangesAsync(ct);

        // Aviso por e-mail ao novo responsável; se falhar, o encaminhamento continua valendo.
        if (!string.IsNullOrWhiteSpace(destino.Email))
        {
            try
            {
                var associacao = await db.Associacoes.AsNoTracking().Where(a => a.Id == o.AssociacaoId).Select(a => a.Nome).FirstOrDefaultAsync(ct);
                await email.EnviarAsync(new MensagemEmail(destino.Email, destino.Nome,
                    $"Ocorrência nº {o.Numero} encaminhada para você — {o.Titulo}",
                    HtmlEncaminhamento(o, eu.Nome, observacao, relogio), [], associacao), ct);
            }
            catch (Exception ex)
            {
                logs.CreateLogger("Ocorrencias").LogWarning(ex, "Falha ao avisar {Email} sobre a ocorrência {Numero}", destino.Email, o.Numero);
            }
        }
        return Results.Ok(await DtoAsync(db, o.Id, ct));
    }

    private static async Task<IResult> AlterarStatusAsync(Guid id, AlterarStatusOcorrenciaRequest req, UsuarioAtual eu, AppDbContext db, CancellationToken ct)
    {
        var o = await db.Ocorrencias.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null) return NaoEncontrada();
        if (!TentarStatus(req.Status, out var novo))
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = ["Use EmAndamento ou Resolvida."] });
        var observacao = req.Observacao?.Trim();
        var erro = observacao?.Length > 2000 ? "A observação pode ter até 2000 caracteres." : RegrasOcorrencia.ValidarTransicao(o.Status, novo, observacao);
        if (erro is not null) return Results.ValidationProblem(new Dictionary<string, string[]> { ["status"] = [erro] });

        var agora = DateTimeOffset.UtcNow;
        db.MovimentosOcorrencia.Add(new MovimentoOcorrencia
        {
            OcorrenciaId = o.Id, Tipo = TipoMovimentoOcorrencia.StatusAlterado, AutorId = eu.Id, AutorNome = eu.Nome, Em = agora,
            Texto = string.IsNullOrEmpty(observacao) ? null : observacao, StatusAnterior = o.Status, StatusNovo = novo,
        });
        o.Status = novo;
        o.ResolvidaEm = novo == StatusOcorrencia.Resolvida ? agora : null;
        o.AtualizadaEm = agora;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await DtoAsync(db, o.Id, ct));
    }

    private static async Task<IResult> ComentarAsync(Guid id, ComentarioOcorrenciaRequest req, ClaimsPrincipal user, UsuarioAtual eu, AppDbContext db, CancellationToken ct)
    {
        var o = await db.Ocorrencias.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (o is null || !RegrasOcorrencia.PodeVer(o, eu.Id, Administrativo(user))) return NaoEncontrada();
        var texto = req.Texto?.Trim() ?? string.Empty;
        if (texto.Length is 0 or > 2000)
            return Results.ValidationProblem(new Dictionary<string, string[]> { ["texto"] = ["Escreva o comentário (até 2000 caracteres)."] });

        var agora = DateTimeOffset.UtcNow;
        db.MovimentosOcorrencia.Add(new MovimentoOcorrencia
        {
            OcorrenciaId = o.Id, Tipo = TipoMovimentoOcorrencia.Comentario, AutorId = eu.Id, AutorNome = eu.Nome, Em = agora, Texto = texto,
        });
        o.AtualizadaEm = agora;
        await db.SaveChangesAsync(ct);
        return Results.Ok(await DtoAsync(db, o.Id, ct));
    }

    private static async Task<List<ResponsavelDto>> ResponsaveisAsync(UserManager<Usuario> users, Guid associacaoId)
    {
        var admins = await users.GetUsersInRoleAsync(Perfis.Admin);
        return admins.Where(u => u.AssociacaoId == associacaoId && !(u.LockoutEnd > DateTimeOffset.UtcNow)) // fora os bloqueados
            .Select(u => new ResponsavelDto(u.Id, u.Nome, Perfis.Admin)).OrderBy(r => r.Nome).ToList();
    }

    private static IQueryable<OcorrenciaResumoDto> Resumos(IQueryable<Ocorrencia> q) => q.AsNoTracking()
        .OrderBy(o => o.Status == StatusOcorrencia.Resolvida).ThenByDescending(o => o.AtualizadaEm).Take(200)
        .Select(o => new OcorrenciaResumoDto(o.Id, o.Numero, o.Titulo, o.Categoria.ToString(), o.Local, o.Status.ToString(),
            o.RegistradaPorNome, o.RegistradaEm, o.ResponsavelId, o.ResponsavelNome, o.AtualizadaEm, o.Fotos.Count));

    private static async Task<OcorrenciaDto> DtoAsync(AppDbContext db, Guid id, CancellationToken ct)
    {
        var o = await db.Ocorrencias.AsNoTracking().Include(x => x.Movimentos).FirstAsync(x => x.Id == id, ct);
        var fotos = await db.FotosOcorrencia.AsNoTracking().Where(f => f.OcorrenciaId == id).OrderBy(f => f.EnviadaEm).Select(f => f.Id).ToListAsync(ct);
        return Dto(o, fotos);
    }

    private static OcorrenciaDto Dto(Ocorrencia o, IReadOnlyList<Guid> fotos) => new(
        o.Id, o.Numero, o.Titulo, o.Categoria.ToString(), o.Local, o.Descricao, o.Status.ToString(),
        o.RegistradaPorId, o.RegistradaPorNome, o.RegistradaEm, o.ResponsavelId, o.ResponsavelNome, o.AtualizadaEm, o.ResolvidaEm,
        fotos,
        o.Movimentos.OrderBy(m => m.Em).Select(m => new MovimentoOcorrenciaDto(m.Tipo.ToString(), m.AutorNome, m.Em, m.Texto,
            m.StatusAnterior?.ToString(), m.StatusNovo?.ToString(), m.ParaNome)).ToList());

    /// <summary>Quem trata ocorrências: só o Admin da associação.</summary>
    private static bool Administrativo(ClaimsPrincipal user) => user.IsInRole(Perfis.Admin);

    private static bool TentarStatus(string? texto, out StatusOcorrencia status) =>
        Enum.TryParse(texto, true, out status) && Enum.IsDefined(status) && !int.TryParse(texto, out _);

    private static IResult NaoEncontrada() =>
        Results.Problem(title: "Ocorrência não encontrada.", statusCode: StatusCodes.Status404NotFound);

    private static string HtmlEncaminhamento(Ocorrencia o, string de, string? observacao, RelogioClube relogio)
    {
        static string H(string? s) => WebUtility.HtmlEncode(s ?? string.Empty).Replace("\n", "<br>");
        return $"""
            <p>{H(de)} encaminhou para você a <strong>ocorrência nº {o.Numero}</strong>.</p>
            <p><strong>{H(o.Titulo)}</strong> · {H(RegrasOcorrencia.NomeCategoria(o.Categoria))}{(o.Local is null ? "" : " · " + H(o.Local))}</p>
            <p>{H(o.Descricao)}</p>
            <p>Registrada por {H(o.RegistradaPorNome)} em {relogio.ParaLocal(o.RegistradaEm):dd/MM/yyyy HH:mm}.</p>
            {(string.IsNullOrEmpty(observacao) ? "" : $"<p><em>Observação: {H(observacao)}</em></p>")}
            <p>Abra o app, em Gestão › Ocorrências, para tomar as providências.</p>
            """;
    }
}
