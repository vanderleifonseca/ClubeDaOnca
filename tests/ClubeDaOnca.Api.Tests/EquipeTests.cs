using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClubeDaOnca.Contracts.Associacoes;
using ClubeDaOnca.Contracts.Auth;
using ClubeDaOnca.Contracts.Equipe;
using ClubeDaOnca.Contracts.Ocorrencias;
using ClubeDaOnca.Contracts.Socios;

namespace ClubeDaOnca.Api.Tests;

public class EquipeTests(ApiFactory factory) : IClassFixture<ApiFactory>
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

    private async Task<HttpClient> ClienteAsync(string login, string senha) => (await EntrarAsync(login, senha)).Cliente;

    private async Task<HttpClient> AdminNovaUnidadeAsync()
    {
        var super = await ClienteAsync(ApiFactory.AdminEmail, ApiFactory.AdminSenha);
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var resp = await super.PostAsJsonAsync("/associacoes", new NovaAssociacaoRequest(
            $"Clube Eq {sufixo}", null, null, null, null, "Admin Eq", $"eq{sufixo}@teste.local"));
        resp.EnsureSuccessStatusCode();
        var u = (await resp.Content.ReadFromJsonAsync<AssociacaoCriadaResponse>())!;
        return await ClienteAsync(u.AdminLogin, u.SenhaProvisoria);
    }

    private static async Task<MembroEquipeCriadoResponse> CriarAsync(HttpClient admin, string nome, params string[] perfis)
    {
        var resp = await admin.PostAsJsonAsync("/equipe",
            new NovoMembroEquipeRequest(nome, $"eq{Guid.NewGuid():N}@teste.local", perfis));
        Assert.True(resp.StatusCode == HttpStatusCode.Created, await resp.Content.ReadAsStringAsync());
        return (await resp.Content.ReadFromJsonAsync<MembroEquipeCriadoResponse>())!;
    }

    [Fact]
    public async Task Admin_cadastra_equipe_e_o_novo_admin_recebe_ocorrencias()
    {
        var admin = await AdminNovaUnidadeAsync();
        var adm2 = await CriarAsync(admin, "Adm Carla", "admin");
        var sec = await CriarAsync(admin, "Sec Bia", "Secretaria");
        Assert.NotNull(adm2.SenhaProvisoria);
        Assert.Equal(["Admin"], adm2.Membro.Perfis);
        Assert.True(adm2.Membro.AguardandoPrimeiroAcesso);

        var lista = (await admin.GetFromJsonAsync<List<MembroEquipeDto>>("/equipe"))!;
        Assert.Equal(3, lista.Count);
        Assert.Single(lista, m => m.EhVoce);

        // O novo Admin entra com a senha provisória e já pode receber ocorrências.
        var (_, auth) = await EntrarAsync(adm2.Login, adm2.SenhaProvisoria!);
        Assert.True(auth.Usuario.DeveTrocarSenha);
        Assert.Contains("Admin", auth.Usuario.Perfis);
        var resp = (await admin.GetFromJsonAsync<List<ResponsavelDto>>("/ocorrencias/responsaveis"))!;
        Assert.Contains(resp, r => r.Id == adm2.Membro.Id);
        Assert.DoesNotContain(resp, r => r.Id == sec.Membro.Id);

        // E-mail repetido não cria outra conta.
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync("/equipe",
            new NovoMembroEquipeRequest("De novo", adm2.Login, ["Portaria"]))).StatusCode);
    }

    [Fact]
    public async Task So_admin_gerencia_e_nao_ve_equipe_de_outra_unidade()
    {
        var admin = await AdminNovaUnidadeAsync();
        var outro = await AdminNovaUnidadeAsync();
        var sec = await CriarAsync(admin, "Sec Lia", "Secretaria");
        var secretaria = await ClienteAsync(sec.Login, sec.SenhaProvisoria!);

        Assert.Equal(HttpStatusCode.Forbidden, (await secretaria.GetAsync("/equipe")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await secretaria.PostAsJsonAsync("/equipe",
            new NovoMembroEquipeRequest("Xx Yy", "x@teste.local", ["Admin"]))).StatusCode);

        Assert.DoesNotContain((await outro.GetFromJsonAsync<List<MembroEquipeDto>>("/equipe"))!, m => m.Id == sec.Membro.Id);
        Assert.Equal(HttpStatusCode.NotFound, (await outro.PostAsync($"/equipe/{sec.Membro.Id}/bloquear", null)).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.PostAsJsonAsync("/equipe",
            new NovoMembroEquipeRequest("Xx Yy", "y@teste.local", ["SuperAdmin"]))).StatusCode);
    }

    [Fact]
    public async Task Socio_existente_vira_secretaria_sem_nova_senha()
    {
        var admin = await AdminNovaUnidadeAsync();
        var cpf = CpfTeste.Proximo();
        var email = $"eqsocio{cpf}@teste.local";
        var r = await admin.PostAsJsonAsync("/socios", new SalvarSocioRequest(
            $"Sócio {cpf[..4]}", cpf, email, "(45) 99876-5432", new DateOnly(1985, 1, 1), "Individual"));
        r.EnsureSuccessStatusCode();

        var resp = await admin.PostAsJsonAsync("/equipe", new NovoMembroEquipeRequest("Qualquer", email, ["Secretaria"]));
        Assert.Equal(HttpStatusCode.Created, resp.StatusCode);
        var criado = (await resp.Content.ReadFromJsonAsync<MembroEquipeCriadoResponse>())!;
        Assert.Null(criado.SenhaProvisoria);
        Assert.True(criado.Membro.EhSocio);

        // Tirar da equipe mantém a conta de sócio.
        var tirado = await admin.PutAsJsonAsync($"/equipe/{criado.Membro.Id}/perfis", new AlterarPerfisEquipeRequest([]));
        tirado.EnsureSuccessStatusCode();
        Assert.DoesNotContain((await admin.GetFromJsonAsync<List<MembroEquipeDto>>("/equipe"))!, m => m.Id == criado.Membro.Id);
    }

    [Fact]
    public async Task Unidade_nunca_fica_sem_admin_ativo()
    {
        var admin = await AdminNovaUnidadeAsync();
        var eu = (await admin.GetFromJsonAsync<List<MembroEquipeDto>>("/equipe"))!.Single(m => m.EhVoce);

        Assert.Equal(HttpStatusCode.Conflict, (await admin.PutAsJsonAsync($"/equipe/{eu.Id}/perfis",
            new AlterarPerfisEquipeRequest(["Secretaria"]))).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/equipe/{eu.Id}/bloquear", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsync($"/equipe/{eu.Id}/redefinir-senha", null)).StatusCode);

        // Com dois Admins, um pode rebaixar o outro; o que sobrou não pode tirar o próprio acesso.
        var adm2 = await CriarAsync(admin, "Adm Duda", "Admin");
        var outro = await ClienteAsync(adm2.Login, adm2.SenhaProvisoria!);
        var resp = await outro.PutAsJsonAsync($"/equipe/{eu.Id}/perfis", new AlterarPerfisEquipeRequest(["Portaria"]));
        resp.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Conflict, (await outro.PutAsJsonAsync($"/equipe/{adm2.Membro.Id}/perfis",
            new AlterarPerfisEquipeRequest(["Portaria"]))).StatusCode);
    }

    [Fact]
    public async Task Bloqueio_impede_login_e_renovacao_e_nova_senha_funciona()
    {
        var admin = await AdminNovaUnidadeAsync();
        var por = await CriarAsync(admin, "Porteiro Zé", "Portaria");
        var (_, sessao) = await EntrarAsync(por.Login, por.SenhaProvisoria!);

        var bloq = (await (await admin.PostAsync($"/equipe/{por.Membro.Id}/bloquear", null)).Content.ReadFromJsonAsync<MembroEquipeDto>())!;
        Assert.True(bloq.Bloqueado);
        var anon = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/login", new LoginRequest(por.Login, por.SenhaProvisoria!))).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/refresh", new RefreshRequest(sessao.RefreshToken))).StatusCode);

        (await admin.PostAsync($"/equipe/{por.Membro.Id}/desbloquear", null)).EnsureSuccessStatusCode();
        var nova = (await (await admin.PostAsync($"/equipe/{por.Membro.Id}/redefinir-senha", null)).Content.ReadFromJsonAsync<SenhaRedefinidaResponse>())!;
        Assert.Equal(HttpStatusCode.Unauthorized, (await anon.PostAsJsonAsync("/auth/login", new LoginRequest(por.Login, por.SenhaProvisoria!))).StatusCode);
        var (_, depois) = await EntrarAsync(nova.Login, nova.SenhaProvisoria);
        Assert.True(depois.Usuario.DeveTrocarSenha);
    }
}
