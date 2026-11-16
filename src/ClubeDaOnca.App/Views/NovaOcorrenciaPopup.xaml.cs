using ClubeDaOnca.App.ViewModels;
using ClubeDaOnca.Contracts.Ocorrencias;
using CommunityToolkit.Maui.Views;

namespace ClubeDaOnca.App.Views;

public partial class NovaOcorrenciaPopup : Popup<OcorrenciaCriadaResponse>
{
    public NovaOcorrenciaPopup(NovaOcorrenciaViewModel vm)
    {
        InitializeComponent();
        vm.Fechar = async criada =>
        {
            if (criada is null) await CloseAsync();
            else await CloseAsync(criada);
        };
        BindingContext = vm;
    }
}
