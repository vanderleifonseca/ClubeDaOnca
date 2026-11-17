using System.Collections.ObjectModel;
using ClubeDaOnca.App.Services;
using ClubeDaOnca.Contracts.Equipe;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace ClubeDaOnca.App.ViewModels;

public sealed record MembroLinha(MembroEquipeDto Dto)
{
    public string Nome => Dto.Nome + (Dto.EhVoce ? " (você)" : "");
    public string Email => Dto.Email;
    public string Perfis => string.Join(" · ", Dto.Perfis.Select(EquipeViewModel.NomePerfil)) + (Dto.EhSocio ? " · também é sócio" : "");
    public string? Situacao => Dto.Bloqueado ? "🔒 Acesso bloqueado"
        : Dto.AguardandoPrimeiroAcesso ? "⏳ Ainda não trocou a senha provisória" : null;
    public bool TemSituacao => Situacao is not null;
}

/// <summary>Equipe da associação: administradores, secretaria e portaria. Rota: equipe (só Admin).</summary>
public partial class EquipeViewModel(IClubeApi api) : ObservableObject
{
    private static readonly (string Rotulo, string[] Perfis)[] OpcoesPerfil =
    [
        ("Administrador (recebe ocorrências e gerencia a unidade)", ["Admin"]),
        ("Secretaria (sócios, reservas, financeiro, eventos)", ["Secretaria"]),
        ("Portaria (lê carteirinhas)", ["Portaria"]),
        ("Secretaria e portaria", ["Secretaria", "Portaria"]),
    ];

    public ObservableCollection<MembroLinha> Itens { get; } = [];

    [ObservableProperty] public partial bool Carregando { get; set; }
    [ObservableProperty] public partial bool Ocupado { get; set; }
    [ObservableProperty] public partial string? Erro { get; set; }

    public static string NomePerfil(string p) => p switch
    {
        "Admin" => "Administrador",
        "Secretaria" => "Secretaria",
        "Portaria" => "Portaria",
        _ => p,
    };

    [RelayCommand]
    private async Task CarregarAsync()
    {
        Erro = null;
        Carregando = true;
        try
        {
            var lista = await api.EquipeAsync();
            Itens.Clear(); // só depois de buscar: cargas simultâneas não duplicam a lista
            foreach (var m in lista) Itens.Add(new MembroLinha(m));
        }
        catch (Exception ex)
        {
            Erro = ErroApi.Mensagem(ex);
        }
        finally
        {
            Carregando = false;
        }
    }

    private static async Task<string[]?> EscolherPerfilAsync(string titulo)
    {
        var escolha = await Shell.Current.DisplayActionSheetAsync(titulo, "Cancelar", null, OpcoesPerfil.Select(o => o.Rotulo).ToArray());
        return OpcoesPerfil.FirstOrDefault(o => o.Rotulo == escolha).Perfis;
    }

    [RelayCommand]
    private async Task AdicionarAsync()
    {
        var nome = await Shell.Current.DisplayPromptAsync("Nova pessoa na equipe", "Nome completo:", "Continuar", "Cancelar", maxLength: 150);
        if (string.IsNullOrWhiteSpace(nome)) return;
        var email = await Shell.Current.DisplayPromptAsync("Nova pessoa na equipe", "E-mail (será o login):", "Continuar", "Cancelar",
            maxLength: 256, keyboard: Keyboard.Email);
        if (string.IsNullOrWhiteSpace(email)) return;
        if (await EscolherPerfilAsync("Perfil de acesso") is not { } perfis) return;

        await Executar(async () =>
        {
            var criado = await api.AdicionarMembroEquipeAsync(new NovoMembroEquipeRequest(nome, email, perfis));
            await Shell.Current.DisplayAlertAsync($"{criado.Membro.Nome} entrou na equipe", criado.SenhaProvisoria is null
                ? $"A pessoa já tinha conta no clube e passa a usar o mesmo login: {criado.Login}"
                : $"Login: {criado.Login}\nSenha provisória: {criado.SenhaProvisoria}\n\n" +
                  "Anote e entregue à pessoa. Esta senha não será exibida novamente e precisa ser trocada no primeiro acesso.", "OK");
            await CarregarAsync();
        });
    }

    [RelayCommand]
    private async Task AcoesAsync(MembroLinha linha)
    {
        var m = linha.Dto;
        if (m.EhVoce)
        {
            await Shell.Current.DisplayAlertAsync(m.Nome, "Esta é a sua conta. Para mudar o seu perfil ou bloquear o acesso, peça a outro administrador.", "OK");
            return;
        }
        const string Perfil = "Mudar perfil", Senha = "Gerar nova senha provisória", Bloquear = "Bloquear acesso",
            Desbloquear = "Desbloquear acesso", Tirar = "Tirar da equipe";
        var escolha = await Shell.Current.DisplayActionSheetAsync(m.Nome, "Cancelar", null,
            Perfil, Senha, m.Bloqueado ? Desbloquear : Bloquear, Tirar);

        switch (escolha)
        {
            case Perfil:
                if (await EscolherPerfilAsync($"Perfil de {m.Nome}") is { } perfis)
                    await Executar(async () => { await api.AlterarPerfisEquipeAsync(m.Id, new AlterarPerfisEquipeRequest(perfis)); await CarregarAsync(); });
                break;

            case Senha:
                if (!await Shell.Current.DisplayAlertAsync("Nova senha", $"Gerar uma senha provisória para {m.Nome}? A senha atual deixa de valer.", "Gerar", "Cancelar")) break;
                await Executar(async () =>
                {
                    var r = await api.RedefinirSenhaEquipeAsync(m.Id);
                    await Shell.Current.DisplayAlertAsync("Senha provisória", $"Login: {r.Login}\nSenha provisória: {r.SenhaProvisoria}\n\n" +
                        "Entregue à pessoa. Ela troca a senha no primeiro acesso.", "OK");
                    await CarregarAsync();
                });
                break;

            case Bloquear:
                if (!await Shell.Current.DisplayAlertAsync("Bloquear acesso", $"{m.Nome} não conseguirá mais entrar no app até ser desbloqueado.", "Bloquear", "Cancelar")) break;
                await Executar(async () => { await api.BloquearMembroEquipeAsync(m.Id); await CarregarAsync(); });
                break;

            case Desbloquear:
                await Executar(async () => { await api.DesbloquearMembroEquipeAsync(m.Id); await CarregarAsync(); });
                break;

            case Tirar:
                var aviso = m.EhSocio ? " A conta de sócio continua funcionando." : " A conta deixa de ter acesso à área de gestão.";
                if (!await Shell.Current.DisplayAlertAsync("Tirar da equipe", $"Tirar {m.Nome} da equipe?{aviso}", "Tirar", "Cancelar")) break;
                await Executar(async () => { await api.AlterarPerfisEquipeAsync(m.Id, new AlterarPerfisEquipeRequest([])); await CarregarAsync(); });
                break;
        }
    }

    private async Task Executar(Func<Task> acao)
    {
        Erro = null;
        Ocupado = true; // não usa Carregando: ligar o IsRefreshing do RefreshView dispararia outra carga
        try { await acao(); }
        catch (Exception ex) { Erro = ErroApi.Mensagem(ex); }
        finally { Ocupado = false; }
    }
}
