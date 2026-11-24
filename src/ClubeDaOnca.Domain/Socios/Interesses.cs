namespace ClubeDaOnca.Domain.Socios;

/// <summary>
/// Interesses que o sócio pode marcar no cadastro (usados para divulgar atividades e eventos).
/// O texto é gravado como está: para incluir um interesse novo basta acrescentá-lo aqui.
/// </summary>
public static class Interesses
{
    public static readonly IReadOnlyList<string> Todos = ["Caça", "Pesca", "Cozinha", "Pintura"];

    /// <summary>
    /// Normaliza a lista recebida: aceita maiúsculas/minúsculas e ausência de acento, remove repetidos
    /// e devolve na ordem do catálogo. Os itens desconhecidos voltam em <paramref name="invalidos"/>.
    /// </summary>
    public static List<string> Normalizar(IEnumerable<string>? valores, out List<string> invalidos)
    {
        invalidos = [];
        var escolhidos = new HashSet<string>();
        foreach (var v in valores ?? [])
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            var item = Todos.FirstOrDefault(t => string.Equals(SemAcento(t), SemAcento(v.Trim()), StringComparison.OrdinalIgnoreCase));
            if (item is null) invalidos.Add(v.Trim());
            else escolhidos.Add(item);
        }
        return Todos.Where(escolhidos.Contains).ToList();
    }

    private static string SemAcento(string texto) =>
        new(texto.Normalize(System.Text.NormalizationForm.FormD)
            .Where(c => System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark)
            .ToArray());
}
