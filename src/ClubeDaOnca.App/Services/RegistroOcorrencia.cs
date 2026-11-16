using ClubeDaOnca.App.Views;
using ClubeDaOnca.Contracts.Ocorrencias;
using CommunityToolkit.Maui;
using CommunityToolkit.Maui.Extensions;

namespace ClubeDaOnca.App.Services;

/// <summary>Abre o popup de nova ocorrência a partir de qualquer tela e confirma o envio.</summary>
public sealed class RegistroOcorrencia(IServiceProvider services)
{
    /// <summary>Devolve a ocorrência criada, ou nulo se o usuário cancelou.</summary>
    public async Task<OcorrenciaCriadaResponse?> AbrirAsync()
    {
        if (Shell.Current?.CurrentPage is not { } pagina) return null;
        var popup = services.GetRequiredService<NovaOcorrenciaPopup>();
        var resultado = await pagina.ShowPopupAsync<OcorrenciaCriadaResponse>(popup,
            new PopupOptions { CanBeDismissedByTappingOutsideOfPopup = false });
        if (resultado.WasDismissedByTappingOutsideOfPopup || resultado.Result is not { } criada) return null;

        await pagina.DisplayAlertAsync("Ocorrência enviada",
            $"Ocorrência nº {criada.Numero} enviada à administração. Acompanhe o andamento em \"Minhas ocorrências\".", "OK");
        return criada;
    }
}
