namespace Mailcore.Interno;

internal static class Caminho
{
    internal static string Segmento(string valor)
    {
        Json.ValidarTexto(valor);
        if (valor is "" or "." or ".." || valor.Any(c => c < 32 || c == 127))
            throw ErroMailcore.Configuracao("Informe um identificador válido, sem controles.");
        return Uri.EscapeDataString(valor);
    }

    internal static string Indice(int valor) => valor >= 0 ? valor.ToString(System.Globalization.CultureInfo.InvariantCulture)
        : throw ErroMailcore.Configuracao("O índice do anexo deve ser não negativo.");
}
