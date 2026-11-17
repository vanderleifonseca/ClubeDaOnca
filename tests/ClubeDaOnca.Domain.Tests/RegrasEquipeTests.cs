using ClubeDaOnca.Domain.Seguranca;

namespace ClubeDaOnca.Domain.Tests;

public class RegrasEquipeTests
{
    [Fact]
    public void Normaliza_perfis_de_equipe_e_recusa_os_demais()
    {
        Assert.Equal(["Admin", "Portaria"], RegrasEquipe.Normalizar(["admin", " PORTARIA ", "Admin"]));
        Assert.Empty(RegrasEquipe.Normalizar([])!);
        Assert.Null(RegrasEquipe.Normalizar(["Secretaria", "SuperAdmin"]));
        Assert.Null(RegrasEquipe.Normalizar(["Titular"]));
    }

    [Theory]
    [InlineData(true, true, false, 3, false)]  // não tira o próprio Admin
    [InlineData(false, true, false, 0, false)] // não deixa a unidade sem Admin
    [InlineData(false, true, false, 1, true)]
    [InlineData(true, true, true, 0, true)]    // continua Admin (ex.: ganhou Secretaria também)
    [InlineData(false, false, false, 0, true)] // não era Admin: indiferente
    public void Unidade_mantem_administracao(bool ehVoce, bool era, bool continua, int outros, bool permitida) =>
        Assert.Equal(permitida, RegrasEquipe.ValidarMudanca(ehVoce, era, continua, outros) is null);
}
