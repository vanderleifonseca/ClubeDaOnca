using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClubeDaOnca.Api.Infrastructure;
using ClubeDaOnca.Api.Infrastructure.Email;
using ClubeDaOnca.Contracts.Associacoes;
using ClubeDaOnca.Contracts.Auth;
using ClubeDaOnca.Contracts.Ocorrencias;
using ClubeDaOnca.Contracts.Socios;
using ClubeDaOnca.Domain.Seguranca;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace ClubeDaOnca.Api.Tests;

public class OcorrenciasTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01];
    private static readonly byte[] Png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D];

    private sealed record Unidade(HttpClient Admin, Guid AssociacaoId);

    private async Task<HttpClient> ClienteAsync(string login, string senha)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/login", new LoginRequest(login, senha));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<Unidade> NovaUnidadeAsync()
    {
        var super = await ClienteAsync(ApiFactory.AdminEmail, ApiFactory.AdminSenha);
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var resp = await super.PostAsJsonAsync("/associacoes", new NovaAssociacaoRequest(
            $"Clube Oco {sufixo}", null, null, null, null, "Admin Oco", $"oco{sufixo}@teste.local"));
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<AssociacaoCriadaResponse>())!;
        return new Unidade(await ClienteAsync(u.AdminLogin, u.SenhaProvisoria), u.Associacao.Id);
    }

    /// <summary>Não há tela de cadastro da equipe: cria a conta direto no Identity.</summary>
    private async Task<(HttpClient Cliente, string Id, string Email)> UsuarioEquipeAsync(Guid associacaoId, string perfil, string nome)
    {
        using var scope = factory.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<Usuario>>();
        var email = $"{perfil.ToLowerInvariant()}{Guid.NewGuid():N}@teste.local";
        var u = new Usuario { UserName = email, Email = email, EmailConfirmed = true, Nome = nome, AssociacaoId = associacaoId };
        Assert.True((await users.CreateAsync(u, "Senha@1234")).Succeeded);
        await users.AddToRoleAsync(u, perfil);
        return (await ClienteAsync(email, "Senha@1234"), u.Id, email);
    }

    private async Task<HttpClient> SocioAsync(HttpClient admin)
    {
        var cpf = CpfTeste.Proximo();
        var resp = await admin.PostAsJsonAsync("/socios", new SalvarSocioRequest(
            $"Sócio {cpf[..4]}", cpf, $"oco{cpf}@teste.local", "(45) 99876-5432", new DateOnly(1985, 1, 1), "Individual"));
        resp.EnsureSuccessStatusCode();
        var criado = (await resp.Content.ReadFromJsonAsync<SocioCriadoResponse>())!;
        await SocioTeste.AtivarAsync(admin, criado.Socio.Id);
        return await ClienteAsync(criado.Login, criado.SenhaProvisoria!);
    }

    private static MultipartFormDataContent Formulario(string titulo = "Lâmpada queimada", string categoria = "Manutencao",
        string descricao = "A lâmpada do poste da quadra 2 está apagada.", params byte[][] fotos)
    {
        var form = new MultipartFormDataContent
        {
            { new StringContent(titulo), "titulo" },
            { new StringContent(categoria), "categoria" },
            { new StringContent("Quadra 2"), "local" },
            { new StringContent(descricao), "descricao" },
        };
        var i = 0;
        foreach (var f in fotos)
        {
            var parte = new ByteArrayContent(f);
            parte.Headers.ContentType = new MediaTypeHeaderValue(f.Length > 0 && f[0] == 0x89 ? "image/png" : "image/jpeg");
            form.Add(parte, "fotos", $"foto{++i}.jpg");
        }
        return form;
    }

    private static async Task<OcorrenciaCriadaResponse> RegistrarAsync(HttpClient c, MultipartFormDataContent? form = null)
    {
        var resp = await c.PostAsync("/ocorrencias", form ?? Formulario());
        Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<OcorrenciaCriadaResponse>())!;
    }

    [Fact]
    public async Task Socio_registra_com_fotos_e_autor_e_data_vem_do_token()
    {
        var u = await NovaUnidadeAsync();
        var socio = await SocioAsync(u.Admin);
        var me = (await socio.GetFromJsonAsync<UsuarioDto>("/auth/me"))!;

        var antes = DateTimeOffset.UtcNow.AddSeconds(-5);
        var criada = await RegistrarAsync(socio, Formulario(fotos: [Jpeg, Png]));
        Assert.True(criada.Numero > 0);

        var o = (await socio.GetFromJsonAsync<OcorrenciaDto>($"/ocorrencias/{criada.Id}"))!;
        Assert.Equal(me.Nome, o.RegistradaPor);
        Assert.Equal("Aberta", o.Status);
        Assert.True(o.RegistradaEm >= antes);
        Assert.Equal(2, o.Fotos.Count);
        var mov = Assert.Single(o.Movimentos);
        Assert.Equal("Registrada", mov.Tipo);
        Assert.Equal(me.Nome, mov.Autor);

        var foto = await socio.GetAsync($"/ocorrencias/{criada.Id}/fotos/{o.Fotos[1]}");
        foto.EnsureSuccessStatusCode();
        Assert.Equal("image/png", foto.Content.Headers.ContentType?.MediaType);

        var minhas = (await socio.GetFromJsonAsync<List<OcorrenciaResumoDto>>("/ocorrencias/minhas"))!;
        Assert.Equal(criada.Id, Assert.Single(minhas).Id);

        // A administração recebe na caixa geral.
        var caixa = (await u.Admin.GetFromJsonAsync<List<OcorrenciaResumoDto>>("/ocorrencias"))!;
        Assert.Contains(caixa, x => x.Id == criada.Id && x.Responsavel == null);
        Assert.Equal(1, (await u.Admin.GetFromJsonAsync<ResumoOcorrenciasDto>("/ocorrencias/resumo"))!.Abertas);
    }

    [Fact]
    public async Task Socio_nao_ve_ocorrencia_de_outro_nem_a_caixa_da_administracao()
    {
        var u = await NovaUnidadeAsync();
        var a = await SocioAsync(u.Admin);
        var b = await SocioAsync(u.Admin);
        var criada = await RegistrarAsync(a);

        Assert.Equal(HttpStatusCode.NotFound, (await b.GetAsync($"/ocorrencias/{criada.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await b.PostAsJsonAsync($"/ocorrencias/{criada.Id}/comentarios", new ComentarioOcorrenciaRequest("oi"))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await b.GetAsync("/ocorrencias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await a.PostAsJsonAsync($"/ocorrencias/{criada.Id}/status",
            new AlterarStatusOcorrenciaRequest("Resolvida", "feito"))).StatusCode);

        // O autor pode complementar.
        var resp = await a.PostAsJsonAsync($"/ocorrencias/{criada.Id}/comentarios", new ComentarioOcorrenciaRequest("Continua apagada à noite."));
        resp.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Admin_encaminha_para_outro_admin_que_resolve()
    {
        var u = await NovaUnidadeAsync();
        var (admin2, adm2Id, adm2Email) = await UsuarioEquipeAsync(u.AssociacaoId, Perfis.Admin, "Adm Maria");
        var portaria = (await UsuarioEquipeAsync(u.AssociacaoId, Perfis.Portaria, "Porteiro João")).Cliente;
        var criada = await RegistrarAsync(portaria);

        var responsaveis = (await u.Admin.GetFromJsonAsync<List<ResponsavelDto>>("/ocorrencias/responsaveis"))!;
        Assert.Contains(responsaveis, r => r.Id == adm2Id && r.Perfil == Perfis.Admin);
        Assert.DoesNotContain(responsaveis, r => r.Nome == "Porteiro João");

        var resp = await u.Admin.PostAsJsonAsync($"/ocorrencias/{criada.Id}/encaminhar",
            new EncaminharOcorrenciaRequest(adm2Id, "Chamar o eletricista."));
        Assert.True(resp.IsSuccessStatusCode, await resp.Content.ReadAsStringAsync());
        var o = (await resp.Content.ReadFromJsonAsync<OcorrenciaDto>())!;
        Assert.Equal(adm2Id, o.ResponsavelId);
        Assert.Equal("EmAndamento", o.Status);
        var enc = o.Movimentos.Single(m => m.Tipo == "Encaminhada");
        Assert.Equal("Admin Oco", enc.Autor);
        Assert.Equal("Adm Maria", enc.Para);
        Assert.Equal("Chamar o eletricista.", enc.Texto);

        var emails = factory.Services.GetRequiredService<EmailMemoria>().Enviados;
        Assert.Contains(emails, m => m.Para == adm2Email && m.Assunto.Contains($"nº {criada.Numero}"));

        var paraMim = (await admin2.GetFromJsonAsync<List<OcorrenciaResumoDto>>("/ocorrencias?paraMim=true"))!;
        Assert.Equal(criada.Id, Assert.Single(paraMim).Id);
        Assert.Equal(1, (await admin2.GetFromJsonAsync<ResumoOcorrenciasDto>("/ocorrencias/resumo"))!.ParaMim);

        // Concluir exige descrever a solução.
        Assert.Equal(HttpStatusCode.BadRequest, (await admin2.PostAsJsonAsync($"/ocorrencias/{criada.Id}/status",
            new AlterarStatusOcorrenciaRequest("Resolvida", " "))).StatusCode);
        resp = await admin2.PostAsJsonAsync($"/ocorrencias/{criada.Id}/status",
            new AlterarStatusOcorrenciaRequest("Resolvida", "Lâmpada trocada."));
        resp.EnsureSuccessStatusCode();
        o = (await resp.Content.ReadFromJsonAsync<OcorrenciaDto>())!;
        Assert.Equal("Resolvida", o.Status);
        Assert.NotNull(o.ResolvidaEm);
        var st = o.Movimentos.Last();
        Assert.Equal(("StatusAlterado", "Adm Maria", "EmAndamento", "Resolvida"), (st.Tipo, st.Autor, st.StatusAnterior, st.StatusNovo));

        // Quem registrou acompanha o andamento.
        var vista = (await portaria.GetFromJsonAsync<OcorrenciaDto>($"/ocorrencias/{criada.Id}"))!;
        Assert.Equal("Resolvida", vista.Status);
        Assert.Equal(3, vista.Movimentos.Count);
    }

    [Fact]
    public async Task Secretaria_nao_recebe_nem_trata_ocorrencias()
    {
        var u = await NovaUnidadeAsync();
        var (secretaria, secId, _) = await UsuarioEquipeAsync(u.AssociacaoId, Perfis.Secretaria, "Sec Ana");
        var socio = await SocioAsync(u.Admin);
        var criada = await RegistrarAsync(socio);

        Assert.Equal(HttpStatusCode.Forbidden, (await secretaria.GetAsync("/ocorrencias")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secretaria.GetAsync("/ocorrencias/resumo")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await secretaria.GetAsync($"/ocorrencias/{criada.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secretaria.PostAsJsonAsync($"/ocorrencias/{criada.Id}/status",
            new AlterarStatusOcorrenciaRequest("Resolvida", "feito"))).StatusCode);

        var responsaveis = (await u.Admin.GetFromJsonAsync<List<ResponsavelDto>>("/ocorrencias/responsaveis"))!;
        Assert.DoesNotContain(responsaveis, r => r.Id == secId);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.Admin.PostAsJsonAsync($"/ocorrencias/{criada.Id}/encaminhar",
            new EncaminharOcorrenciaRequest(secId, null))).StatusCode);

        // A secretaria ainda pode registrar as próprias ocorrências.
        await RegistrarAsync(secretaria);
    }

    [Fact]
    public async Task Nao_encaminha_para_socio_nem_para_equipe_de_outra_unidade()
    {
        var u = await NovaUnidadeAsync();
        var outra = await NovaUnidadeAsync();
        var socio = await SocioAsync(u.Admin);
        var me = (await socio.GetFromJsonAsync<UsuarioDto>("/auth/me"))!;
        var (_, secOutraId, _) = await UsuarioEquipeAsync(outra.AssociacaoId, Perfis.Admin, "Adm Outra");
        var criada = await RegistrarAsync(socio);

        Assert.Equal(HttpStatusCode.BadRequest, (await u.Admin.PostAsJsonAsync($"/ocorrencias/{criada.Id}/encaminhar",
            new EncaminharOcorrenciaRequest(me.Id, null))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await u.Admin.PostAsJsonAsync($"/ocorrencias/{criada.Id}/encaminhar",
            new EncaminharOcorrenciaRequest(secOutraId, null))).StatusCode);

        // E a outra unidade nem enxerga a ocorrência.
        Assert.Equal(HttpStatusCode.NotFound, (await outra.Admin.GetAsync($"/ocorrencias/{criada.Id}")).StatusCode);
        Assert.DoesNotContain((await outra.Admin.GetFromJsonAsync<List<OcorrenciaResumoDto>>("/ocorrencias"))!, x => x.Id == criada.Id);
    }

    [Fact]
    public async Task Recusa_quarta_foto_arquivo_que_nao_e_imagem_e_campos_vazios()
    {
        var u = await NovaUnidadeAsync();
        var socio = await SocioAsync(u.Admin);

        Assert.Equal(HttpStatusCode.BadRequest, (await socio.PostAsync("/ocorrencias", Formulario(fotos: [Jpeg, Jpeg, Jpeg, Jpeg]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await socio.PostAsync("/ocorrencias", Formulario(fotos: [[0x25, 0x50, 0x44, 0x46, 0x2D]]))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await socio.PostAsync("/ocorrencias", Formulario(titulo: " "))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await socio.PostAsync("/ocorrencias", Formulario(categoria: "3"))).StatusCode);
        Assert.Empty((await socio.GetFromJsonAsync<List<OcorrenciaResumoDto>>("/ocorrencias/minhas"))!);
    }
}
