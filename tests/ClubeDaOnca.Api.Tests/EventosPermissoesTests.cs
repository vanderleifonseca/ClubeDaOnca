using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ClubeDaOnca.Contracts.Associacoes;
using ClubeDaOnca.Contracts.Auth;
using ClubeDaOnca.Contracts.Equipe;
using ClubeDaOnca.Contracts.Eventos;
using ClubeDaOnca.Contracts.Reservas;
using ClubeDaOnca.Contracts.Socios;

namespace ClubeDaOnca.Api.Tests;

/// <summary>Eventos: Admin e presidente gerenciam; a secretaria só consulta; o sócio comum não gerencia.</summary>
public class EventosPermissoesTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    private sealed record Unidade(HttpClient Admin, HttpClient Presidente, HttpClient Secretaria, HttpClient Socio);

    private async Task<HttpClient> ClienteAsync(string login, string senha)
    {
        var client = factory.CreateClient();
        var resp = await client.PostAsJsonAsync("/auth/login", new LoginRequest(login, senha));
        resp.EnsureSuccessStatusCode();
        var auth = (await resp.Content.ReadFromJsonAsync<AuthResponse>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.AccessToken);
        return client;
    }

    private async Task<Unidade> UnidadeAsync()
    {
        var super = await ClienteAsync(ApiFactory.AdminEmail, ApiFactory.AdminSenha);
        var sufixo = Guid.NewGuid().ToString("N")[..8];
        var u = (await (await super.PostAsJsonAsync("/associacoes", new NovaAssociacaoRequest(
            $"Clube EvP {sufixo}", null, null, null, null, "Admin", $"evp{sufixo}@teste.local"))).Content
            .ReadFromJsonAsync<AssociacaoCriadaResponse>())!;
        var admin = await ClienteAsync(u.AdminLogin, u.SenhaProvisoria);

        var m = (await (await admin.PostAsJsonAsync($"/associacoes/{u.Associacao.Id}/mandatos", new SalvarMandatoRequest(
            "Pedro Presidente", null, $"presev{sufixo}@teste.local", DateOnly.FromDateTime(DateTime.Today).AddMonths(-1))))
            .Content.ReadFromJsonAsync<MandatoDto>())!;
        var presidente = await ClienteAsync(m.Login!, m.SenhaProvisoria!);

        var sec = (await (await admin.PostAsJsonAsync("/equipe", new NovoMembroEquipeRequest("Sec Rita", $"secev{sufixo}@teste.local", ["Secretaria"])))
            .Content.ReadFromJsonAsync<MembroEquipeCriadoResponse>())!;
        var secretaria = await ClienteAsync(sec.Login, sec.SenhaProvisoria!);

        var cpf = CpfTeste.Proximo();
        var s = (await (await admin.PostAsJsonAsync("/socios", new SalvarSocioRequest(
            $"Sócio {cpf[..4]}", cpf, $"evp{cpf}@teste.local", null, new DateOnly(1985, 1, 1), "Individual"))).Content
            .ReadFromJsonAsync<SocioCriadoResponse>())!;
        await SocioTeste.AtivarAsync(admin, s.Socio.Id);
        var socio = await ClienteAsync(s.Login, s.SenhaProvisoria!);

        return new Unidade(admin, presidente, secretaria, socio);
    }

    private static async Task<Guid> LocalAsync(HttpClient admin)
    {
        var resp = await admin.PostAsJsonAsync("/espacos", new SalvarEspacoRequest(
            "Salão", null, true, new TimeOnly(8, 0), new TimeOnly(0, 0), 60, [0, 1, 2, 3, 4, 5, 6], 0, 60, 5, 0m, false, 0, "Salao", 100));
        resp.EnsureSuccessStatusCode();
        return (await resp.Content.ReadFromJsonAsync<EspacoDto>())!.Id;
    }

    private static SalvarEventoRequest Evento(Guid local, int dias = 9) => new(
        "Baile da Onça", "Banda ao vivo", DateTime.Today.AddDays(dias).AddHours(20), DateTime.Today.AddDays(dias).AddHours(23),
        50, true, true, true, 24, [local], [new SalvarAmbienteRequest(null, "Mesa para 4", 4, 2, 1, 0m)]);

    [Fact]
    public async Task Presidente_gerencia_eventos_de_ponta_a_ponta()
    {
        var u = await UnidadeAsync();
        var local = await LocalAsync(u.Admin);

        Assert.Equal(new PermissoesEventosDto(true, true), await u.Presidente.GetFromJsonAsync<PermissoesEventosDto>("/eventos/permissoes"));

        var criado = await u.Presidente.PostAsJsonAsync("/eventos", Evento(local));
        Assert.True(criado.StatusCode == HttpStatusCode.Created, await criado.Content.ReadAsStringAsync());
        var e = (await criado.Content.ReadFromJsonAsync<EventoDto>())!;
        (await u.Presidente.PutAsJsonAsync($"/eventos/{e.Id}", Evento(local) with { Titulo = "Baile da Onça 2026" })).EnsureSuccessStatusCode();
        (await u.Presidente.PostAsync($"/eventos/{e.Id}/publicar", null)).EnsureSuccessStatusCode();

        var div = await u.Presidente.PostAsJsonAsync($"/eventos/{e.Id}/divulgar", new DivulgarEventoRequest("Venha!", ["App"]));
        Assert.Equal(HttpStatusCode.Created, div.StatusCode);
        (await u.Presidente.GetAsync($"/eventos/{e.Id}/post")).EnsureSuccessStatusCode();
        (await u.Presidente.GetAsync($"/eventos/{e.Id}/reservas")).EnsureSuccessStatusCode();
        (await u.Presidente.PostAsJsonAsync($"/eventos/{e.Id}/cancelar", new CancelarEventoRequest("Chuva"))).EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task Secretaria_so_consulta_e_socio_nao_gerencia()
    {
        var u = await UnidadeAsync();
        var local = await LocalAsync(u.Admin);
        var e = (await (await u.Admin.PostAsJsonAsync("/eventos", Evento(local))).Content.ReadFromJsonAsync<EventoDto>())!;

        Assert.Equal(new PermissoesEventosDto(true, false), await u.Secretaria.GetFromJsonAsync<PermissoesEventosDto>("/eventos/permissoes"));
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Secretaria.PostAsJsonAsync("/eventos", Evento(local, 12))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Secretaria.PutAsJsonAsync($"/eventos/{e.Id}", Evento(local))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Secretaria.PostAsync($"/eventos/{e.Id}/publicar", null)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Secretaria.PostAsJsonAsync($"/eventos/{e.Id}/divulgar",
            new DivulgarEventoRequest("x", ["App"]))).StatusCode);

        // Consulta: vê o rascunho na lista da gestão e as reservas.
        var todos = (await u.Secretaria.GetFromJsonAsync<List<EventoDto>>("/eventos?todos=true"))!;
        Assert.Contains(todos, x => x.Id == e.Id);
        (await u.Secretaria.GetAsync($"/eventos/{e.Id}/reservas")).EnsureSuccessStatusCode();

        // Sócio comum (não presidente): nem gerencia nem vê rascunhos.
        Assert.Equal(new PermissoesEventosDto(false, false), await u.Socio.GetFromJsonAsync<PermissoesEventosDto>("/eventos/permissoes"));
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Socio.PostAsJsonAsync("/eventos", Evento(local, 14))).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await u.Socio.GetAsync($"/eventos/{e.Id}/reservas")).StatusCode);
        Assert.DoesNotContain((await u.Socio.GetFromJsonAsync<List<EventoDto>>("/eventos?todos=true"))!, x => x.Id == e.Id);
    }

    [Fact]
    public async Task Divulgar_exige_evento_publicado()
    {
        var u = await UnidadeAsync();
        var local = await LocalAsync(u.Admin);
        var e = (await (await u.Admin.PostAsJsonAsync("/eventos", Evento(local))).Content.ReadFromJsonAsync<EventoDto>())!;
        Assert.Equal(HttpStatusCode.Conflict, (await u.Admin.PostAsJsonAsync($"/eventos/{e.Id}/divulgar",
            new DivulgarEventoRequest("Venha!", ["App"]))).StatusCode);
    }
}
