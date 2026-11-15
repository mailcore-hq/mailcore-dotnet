using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Mailcore;

/// <summary>Autentica os bytes originais do webhook antes de interpretar seu conteúdo.</summary>
public static class Assinatura
{
    public static EventoWebhook Verificar(ReadOnlyMemory<byte> corpo,
        IReadOnlyDictionary<string, IReadOnlyList<string>> cabecalhos, string segredo,
        OpcoesAssinatura? opcoes = null)
    {
        try
        {
            opcoes ??= new OpcoesAssinatura();
            if (corpo.Length > 8 * 1024 * 1024 || opcoes.Tolerancia <= TimeSpan.Zero
                || opcoes.Tolerancia > TimeSpan.FromMinutes(5)) throw Invalida();
            var id = Unico(cabecalhos, "svix-id", 256);
            var timestamp = Unico(cabecalhos, "svix-timestamp", 16);
            var assinaturas = Unico(cabecalhos, "svix-signature", 4096);
            if (timestamp[0] == '0' || !timestamp.All(char.IsAsciiDigit)
                || !long.TryParse(timestamp, NumberStyles.None, CultureInfo.InvariantCulture, out var segundos)) throw Invalida();
            var diferenca = ((opcoes.Agora ?? DateTimeOffset.UtcNow) - DateTimeOffset.FromUnixTimeSeconds(segundos)).Duration();
            if (diferenca > opcoes.Tolerancia || !segredo.StartsWith("whsec_", StringComparison.Ordinal) || segredo.Length > 1030) throw Invalida();
            var chave = Base64(segredo[6..]);
            if (chave.Length is < 16 or > 768) throw Invalida();
            using var hmac = IncrementalHash.CreateHMAC(HashAlgorithmName.SHA256, chave);
            hmac.AppendData(Encoding.ASCII.GetBytes(id + "." + timestamp + "."));
            hmac.AppendData(corpo.Span);
            var esperado = hmac.GetHashAndReset();
            var candidatos = assinaturas.Split(' ');
            if (candidatos.Length > 32) throw Invalida();
            var valida = false;
            foreach (var candidato in candidatos)
            {
                if (!candidato.StartsWith("v1,", StringComparison.Ordinal)) continue;
                try { valida |= CryptographicOperations.FixedTimeEquals(esperado, Base64(candidato[3..])); }
                catch (ErroMailcore) { /* Assinaturas inválidas não impedem a rotação. */ }
            }
            if (!valida) throw Invalida();
            var evento = Interno.Json.Ler(corpo.ToArray());
            if (evento.ValueKind != JsonValueKind.Object || !evento.TryGetProperty("type", out var tipo)
                || tipo.ValueKind != JsonValueKind.String || string.IsNullOrEmpty(tipo.GetString())
                || !evento.TryGetProperty("created_at", out var criado) || criado.ValueKind != JsonValueKind.String
                || string.IsNullOrEmpty(criado.GetString()) || !evento.TryGetProperty("data", out var dados)) throw Invalida();
            return new EventoWebhook(tipo.GetString()!, criado.GetString()!, dados);
        }
        catch (Exception) { throw Invalida(); }
    }

    private static string Unico(IReadOnlyDictionary<string, IReadOnlyList<string>> campos, string nome, int limite)
    {
        var encontrados = campos.Where(x => x.Key.Equals(nome, StringComparison.OrdinalIgnoreCase)).ToArray();
        if (encontrados.Length != 1 || encontrados[0].Value.Count != 1) throw Invalida();
        var valor = encontrados[0].Value[0];
        if (string.IsNullOrEmpty(valor) || valor.Length > limite || !valor.All(x => x is >= (char)32 and <= (char)126)) throw Invalida();
        return valor;
    }
    private static byte[] Base64(string texto)
    {
        try
        {
            var valor = Convert.FromBase64String(texto);
            return Convert.ToBase64String(valor) == texto ? valor : throw Invalida();
        }
        catch (Exception) { throw Invalida(); }
    }
    private static ErroMailcore Invalida() => new("assinatura_invalida", "Não foi possível autenticar o webhook.");
}
