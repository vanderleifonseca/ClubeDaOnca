using ClubeDaOnca.Domain.Ocorrencias;

namespace ClubeDaOnca.Domain.Tests;

public class RegrasOcorrenciaTests
{
    [Theory]
    [InlineData(StatusOcorrencia.Aberta, StatusOcorrencia.EmAndamento, null, true)]
    [InlineData(StatusOcorrencia.Aberta, StatusOcorrencia.Resolvida, "Resolvido no local", true)]
    [InlineData(StatusOcorrencia.Aberta, StatusOcorrencia.Resolvida, " ", false)] // concluir exige a solução
    [InlineData(StatusOcorrencia.EmAndamento, StatusOcorrencia.Aberta, "x", false)] // não volta para Aberta
    [InlineData(StatusOcorrencia.EmAndamento, StatusOcorrencia.EmAndamento, null, false)]
    [InlineData(StatusOcorrencia.Resolvida, StatusOcorrencia.EmAndamento, null, false)] // reabrir exige motivo
    [InlineData(StatusOcorrencia.Resolvida, StatusOcorrencia.EmAndamento, "Voltou a quebrar", true)]
    public void Transicoes_de_etapa(StatusOcorrencia atual, StatusOcorrencia novo, string? obs, bool aceita) =>
        Assert.Equal(aceita, RegrasOcorrencia.ValidarTransicao(atual, novo, obs) is null);

    [Fact]
    public void Autor_ve_as_proprias_e_administrativo_ve_todas()
    {
        var o = new Ocorrencia { RegistradaPorId = "u1" };
        Assert.True(RegrasOcorrencia.PodeVer(o, "u1", administrativo: false));
        Assert.False(RegrasOcorrencia.PodeVer(o, "u2", administrativo: false));
        Assert.False(RegrasOcorrencia.PodeVer(o, null, administrativo: false));
        Assert.True(RegrasOcorrencia.PodeVer(o, "u2", administrativo: true));
    }

    [Fact]
    public void Foto_valida_pela_assinatura_e_tamanho()
    {
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xE0];
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        byte[] pdf = [0x25, 0x50, 0x44, 0x46];

        Assert.Null(RegrasOcorrencia.ValidarFoto(jpeg, "image/jpeg"));
        Assert.Null(RegrasOcorrencia.ValidarFoto(png, null));
        Assert.NotNull(RegrasOcorrencia.ValidarFoto(pdf, "image/jpeg")); // tipo informado não basta
        Assert.NotNull(RegrasOcorrencia.ValidarFoto(jpeg, "application/pdf"));
        Assert.NotNull(RegrasOcorrencia.ValidarFoto([], "image/jpeg"));

        var grande = new byte[RegrasOcorrencia.TamanhoMaximoFoto + 1];
        jpeg.CopyTo(grande, 0);
        Assert.NotNull(RegrasOcorrencia.ValidarFoto(grande, "image/jpeg"));
    }
}
