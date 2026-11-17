using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClubeDaOnca.Contracts.Associacoes;
using ClubeDaOnca.Contracts.Auth;
using ClubeDaOnca.Contracts.Carteirinha;
using ClubeDaOnca.Contracts.Socios;
using ClubeDaOnca.Domain.Carteirinha;

namespace ClubeDaOnca.Api.Tests;

/// <summary>Dependentes no cadastro do novo sócio e login próprio do dependente para ver a carteirinha.</summary>
public class DependentesAcessoTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private async Task<(HttpClient Cliente, AuthResponse Auth)> EntrarAsync(string login, string senha)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/login", new LoginRequest(login, senha));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return (client, auth);
    }

    private async Task<HttpClient> AdminAsync()
    {
        var super = (await EntrarAsync(ApiFactory.AdminEmail, ApiFactory.AdminSenha)).Cliente;
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var u = (await (await super.PostAsJsonAsync("/associacoes", new NovaAssociacaoRequest(
            $"Clube Dep {sufixo}", null, null, null, null, "Admin", $"dep{sufixo}@teste.local"))).Content
            .ReadFromJsonAsync<AssociacaoCriadaResponse>())!;
        return (await EntrarAsync(u.AdminLogin, u.SenhaProvisoria)).Cliente;
    }

    private static SalvarSocioRequest Titular(string cpf, params SalvarDependenteRequest[] dependentes) => new(
        "Rafael Lima", cpf, $"tit{cpf}@teste.local", "(45) 99876-5432", new DateOnly(1980, 5, 5), "Familiar",
        Dependentes: dependentes);

    private static string EmailUnico() => $"depl{Guid.NewGuid():N}@teste.local";

    private async Task<HttpClient> SemLogin() => await Task.FromResult(factory.CreateClient());

    [Fact]
    public async Task Novo_socio_com_dependentes_e_login_proprio_para_quem_tem_email()
    {
        var admin = await AdminAsync();
        var emailFilha = EmailUnico();
        var resp = await admin.PostAsJsonAsync("/socios", Titular(CpfTeste.Proximo(),
            new SalvarDependenteRequest("Júlia Lima", CpfTeste.Proximo(), new DateOnly(2005, 2, 2), "Filho", "Feminino", emailFilha),
            new SalvarDependenteRequest("Theo Lima", null, new DateOnly(2018, 9, 9), "Filho", "Masculino")));
        Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        var criado = (await resp.Content.ReadFromJsonAsync<SocioCriadoResponse>())!;

        Assert.Equal(2, criado.Socio.Dependentes.Count);
        var acesso = Assert.Single(criado.Acessos!);
        Assert.Equal("Júlia Lima", acesso.Nome);
        Assert.Equal(emailFilha, acesso.Login);
        Assert.True(criado.Socio.Dependentes.Single(d => d.Nome == "Júlia Lima").TemAcesso);
        Assert.False(criado.Socio.Dependentes.Single(d => d.Nome == "Theo Lima").TemAcesso);
        await SocioTeste.AtivarAsync(admin, criado.Socio.Id);

        // A filha entra e vê só a carteirinha dela; o QR passa na portaria.
        var (filha, auth) = await EntrarAsync(acesso.Login, acesso.SenhaProvisoria);
        Assert.Contains("Dependente", auth.Usuario.Perfis);
        Assert.True(auth.Usuario.DeveTrocarSenha);
        var cart = Assert.Single((await filha.GetFromJsonAsync<List<CarteirinhaDto>>("/carteirinha"))!);
        Assert.Equal(("Júlia Lima", "Dependente", acesso.DependenteId), (cart.Nome, cart.Tipo, cart.PessoaId));

        var qr = QrCarteirinha.Gerar(cart.PessoaId, Convert.FromBase64String(cart.ChaveQr), DateTimeOffset.UtcNow);
        var r = await admin.PostAsJsonAsync("/portaria/validar", new ValidarQrRequest(qr));
        var leitura = (await r.Content.ReadFromJsonAsync<ResultadoAcessoDto>())!;
        Assert.True(leitura.Liberado, leitura.Motivo);
        Assert.Equal("Júlia Lima", leitura.Nome);

        // O titular continua vendo as três carteirinhas.
        var titular = (await EntrarAsync(criado.Login, criado.SenhaProvisoria!)).Cliente;
        Assert.Equal(3, (await titular.GetFromJsonAsync<List<CarteirinhaDto>>("/carteirinha"))!.Count);

        // O dependente não acessa o que é do titular nem da secretaria.
        Assert.Equal(HttpStatusCode.Forbidden, (await filha.GetAsync("/socios")).StatusCode);
    }

    [Fact]
    public async Task Cadastro_com_dependente_invalido_nao_grava_nada()
    {
        var admin = await AdminAsync();
        var cpfTitular = CpfTeste.Proximo();
        var cpfRepetido = CpfTeste.Proximo();
        var resp = await admin.PostAsJsonAsync("/socios", Titular(cpfTitular,
            new SalvarDependenteRequest("Ana Lima", cpfRepetido, new DateOnly(2010, 1, 1), "Filho"),
            new SalvarDependenteRequest("Bia Lima", cpfRepetido, new DateOnly(2012, 1, 1), "Filho")));
        Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        Assert.Contains("CPF repetido", await resp.Content.ReadAsStringAsync());

        // E-mail que já tem conta (o do próprio admin de teste) → 409 e o titular não é criado.
        resp = await admin.PostAsJsonAsync("/socios", Titular(cpfTitular,
            new SalvarDependenteRequest("Ana Lima", null, new DateOnly(2010, 1, 1), "Filho", null, ApiFactory.AdminEmail)));
        Assert.Equal(HttpStatusCode.Conflict, resp.StatusCode);
        var lista = (await admin.GetFromJsonAsync<PaginaDto<SocioResumoDto>>($"/socios?busca={cpfTitular}"))!;
        Assert.Empty(lista.Itens);
    }

    [Fact]
    public async Task Inativar_bloqueia_login_e_acesso_gera_nova_senha()
    {
        var admin = await AdminAsync();
        var criado = (await (await admin.PostAsJsonAsync("/socios", Titular(CpfTeste.Proximo()))).Content.ReadFromJsonAsync<SocioCriadoResponse>())!;
        var socioId = criado.Socio.Id;

        // Dependente antigo sem e-mail: gerar acesso exige o e-mail.
        var semEmail = (await (await admin.PostAsJsonAsync($"/socios/{socioId}/dependentes",
            new SalvarDependenteRequest("Caio Lima", null, new DateOnly(2008, 3, 3), "Filho"))).Content.ReadFromJsonAsync<DependenteCriadoResponse>())!;
        Assert.Null(semEmail.Acesso);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsync($"/socios/{socioId}/dependentes/{semEmail.Dependente.Id}/acesso", null)).StatusCode);

        // Com e-mail, o login nasce junto.
        var comEmail = (await (await admin.PostAsJsonAsync($"/socios/{socioId}/dependentes",
            new SalvarDependenteRequest("Lia Lima", null, new DateOnly(2004, 4, 4), "Conjuge", null, EmailUnico()))).Content.ReadFromJsonAsync<DependenteCriadoResponse>())!;
        var acesso = comEmail.Acesso!;
        var (_, sessao) = await EntrarAsync(acesso.Login, acesso.SenhaProvisoria);

        (await admin.DeleteAsync($"/socios/{socioId}/dependentes/{comEmail.Dependente.Id}")).EnsureSuccessStatusCode();
        var anon = await SemLogin();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/login", new LoginRequest(acesso.Login, acesso.SenhaProvisoria))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/refresh", new RefreshRequest(sessao.RefreshToken))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/socios/{socioId}/dependentes/{comEmail.Dependente.Id}/acesso", null)).StatusCode);

        // Reativado, volta a entrar; e a secretaria pode gerar nova senha.
        (await admin.PostAsync($"/socios/{socioId}/dependentes/{comEmail.Dependente.Id}/reativar", null)).EnsureSuccessStatusCode();
        var nova = (await (await admin.PostAsync($"/socios/{socioId}/dependentes/{comEmail.Dependente.Id}/acesso", null)).Content.ReadFromJsonAsync<AcessoDependenteDto>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/login", new LoginRequest(acesso.Login, acesso.SenhaProvisoria))).StatusCode);
        var (dep, _) = await EntrarAsync(nova.Login, nova.SenhaProvisoria);
        Assert.Single((await dep.GetFromJsonAsync<List<CarteirinhaDto>>("/carteirinha"))!);

        // Trocar o e-mail do cadastro troca o login.
        var novoEmail = EmailUnico();
        (await admin.PutAsJsonAsync($"/socios/{socioId}/dependentes/{comEmail.Dependente.Id}",
            new SalvarDependenteRequest("Lia Lima", null, new DateOnly(2004, 4, 4), "Conjuge", null, novoEmail))).EnsureSuccessStatusCode();
        await EntrarAsync(novoEmail, nova.SenhaProvisoria);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/socios/{socioId}/dependentes/{comEmail.Dependente.Id}",
            new SalvarDependenteRequest("Lia Lima", null, new DateOnly(2004, 4, 4), "Conjuge"))).StatusCode); // e-mail obrigatório com login
    }
}
