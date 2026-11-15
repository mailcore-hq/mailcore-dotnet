using System.Collections.ObjectModel;

namespace Mailcore.Interno;

internal static class Cabecalhos
{
    internal static IReadOnlyDictionary<string, IReadOnlyList<string>> Copiar(
        IReadOnlyDictionary<string, IReadOnlyList<string>>? origem)
    {
        var destino = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        if (origem is not null)
            foreach (var (nome, valores) in origem)
            {
                if (nome is null || valores is null || valores.Any(v => v is null))
                    throw ErroMailcore.Configuracao("Informe nomes e valores de cabeçalho válidos.");
                if (!destino.TryAdd(nome, Array.AsReadOnly(valores.ToArray())))
                    throw ErroMailcore.Configuracao("Não repita nomes de cabeçalho com capitalização diferente.");
            }
        return new ReadOnlyDictionary<string, IReadOnlyList<string>>(destino);
    }
}
