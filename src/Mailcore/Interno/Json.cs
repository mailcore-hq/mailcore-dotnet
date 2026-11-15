using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Mailcore.Interno;

internal static class Json
{
    private const int Limite = 50 * 1024 * 1024;
    private static readonly UTF8Encoding Utf8 = new(false, true);
    private static readonly JsonSerializerOptions Opcoes = CriarOpcoes();

    private static JsonSerializerOptions CriarOpcoes()
    {
        var opcoes = new JsonSerializerOptions
        {
            MaxDepth = 100,
            PropertyNameCaseInsensitive = false,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        opcoes.Converters.Add(new TextoIntegro());
        opcoes.Converters.Add(new CaractereIntegro());
        opcoes.Converters.Add(new InteiroGrande());
        opcoes.MakeReadOnly(populateMissingResolver: true);
        return opcoes;
    }

    internal static byte[]? Codificar(object? dados, FormatoCorpo formato)
    {
        if (formato == FormatoCorpo.Nenhum)
        {
            if (dados is not null) throw ErroMailcore.Configuracao("A operação não aceita corpo.");
            return null;
        }
        try
        {
            using var saida = new SaidaLimitada();
            JsonSerializer.Serialize(saida, dados, Opcoes);
            var bytes = saida.ToArray();
            using var documento = Abrir(bytes);
            var esperado = formato == FormatoCorpo.Objeto ? JsonValueKind.Object : JsonValueKind.Array;
            if (documento.RootElement.ValueKind != esperado)
                throw ErroMailcore.Configuracao("Use objeto JSON ou array conforme a operação.");
            return bytes;
        }
        catch (Exception) { throw ErroMailcore.Configuracao("Use JSON válido, texto Unicode íntegro e números finitos, até 50 MiB."); }
    }

    internal static JsonElement Ler(byte[] dados)
    {
        using var documento = Abrir(dados);
        return documento.RootElement.Clone();
    }

    internal static T? Decodificar<T>(byte[] dados)
    {
        using var _ = Abrir(dados);
        try { return JsonSerializer.Deserialize<T>(dados, Opcoes); }
        catch (Exception) { throw Invalido(); }
    }

    internal static void Validar(byte[] dados) { using var _ = Abrir(dados); }
    private static JsonDocument Abrir(byte[] dados)
    {
        JsonDocument? documento = null;
        try
        {
            Utf8.GetCharCount(dados);
            documento = JsonDocument.Parse(dados, new JsonDocumentOptions { MaxDepth = 100 });
            ValidarElemento(documento.RootElement);
            return documento;
        }
        catch (Exception)
        {
            documento?.Dispose();
            throw Invalido();
        }
    }

    internal static void ValidarUtf8(byte[] dados)
    {
        try { Utf8.GetCharCount(dados); }
        catch (DecoderFallbackException) { throw Invalido(); }
    }

    private static void ValidarElemento(JsonElement elemento)
    {
        switch (elemento.ValueKind)
        {
            case JsonValueKind.Object:
                var nomes = new HashSet<string>(StringComparer.Ordinal);
                foreach (var propriedade in elemento.EnumerateObject())
                {
                    ValidarTexto(propriedade.Name);
                    if (!nomes.Add(propriedade.Name)) throw Invalido();
                    ValidarElemento(propriedade.Value);
                }
                break;
            case JsonValueKind.Array:
                foreach (var item in elemento.EnumerateArray()) ValidarElemento(item);
                break;
            case JsonValueKind.String:
                ValidarTexto(elemento.GetString()!);
                break;
        }
    }

    internal static void ValidarTexto(string texto)
    {
        if (texto is null) throw ErroMailcore.Configuracao("Informe texto válido.");
        for (var i = 0; i < texto.Length; i++)
        {
            if (char.IsHighSurrogate(texto[i]))
            {
                if (++i >= texto.Length || !char.IsLowSurrogate(texto[i]))
                    throw ErroMailcore.Configuracao("Texto Unicode inválido.");
            }
            else if (char.IsLowSurrogate(texto[i])) throw ErroMailcore.Configuracao("Texto Unicode inválido.");
        }
    }

    private static ErroMailcore Invalido() => new("resposta_invalida", "O conteúdo JSON ou UTF-8 é inválido.");

    private sealed class TextoIntegro : JsonConverter<string>
    {
        public override string? Read(ref Utf8JsonReader leitor, Type tipo, JsonSerializerOptions opcoes)
        {
            var valor = leitor.GetString();
            if (valor is not null) ValidarTexto(valor);
            return valor;
        }
        public override void Write(Utf8JsonWriter escritor, string valor, JsonSerializerOptions opcoes)
        { ValidarTexto(valor); escritor.WriteStringValue(valor); }
        public override void WriteAsPropertyName(Utf8JsonWriter escritor, string valor, JsonSerializerOptions opcoes)
        { ValidarTexto(valor); escritor.WritePropertyName(valor); }
    }

    private sealed class CaractereIntegro : JsonConverter<char>
    {
        public override char Read(ref Utf8JsonReader leitor, Type tipo, JsonSerializerOptions opcoes)
        {
            var texto = leitor.GetString() ?? throw Invalido();
            ValidarTexto(texto);
            return texto.Length == 1 ? texto[0] : throw Invalido();
        }
        public override void Write(Utf8JsonWriter escritor, char valor, JsonSerializerOptions opcoes)
        { var texto = valor.ToString(); ValidarTexto(texto); escritor.WriteStringValue(texto); }
    }

    private sealed class InteiroGrande : JsonConverter<BigInteger>
    {
        public override BigInteger Read(ref Utf8JsonReader leitor, Type tipo, JsonSerializerOptions opcoes)
        {
            using var documento = JsonDocument.ParseValue(ref leitor);
            return BigInteger.Parse(documento.RootElement.GetRawText(), NumberStyles.Integer, CultureInfo.InvariantCulture);
        }
        public override void Write(Utf8JsonWriter escritor, BigInteger valor, JsonSerializerOptions opcoes)
        {
            var texto = valor.ToString(CultureInfo.InvariantCulture);
            if (texto.Length > 10000) throw ErroMailcore.Configuracao("O número JSON ultrapassa o limite.");
            escritor.WriteRawValue(texto);
        }
    }

    private sealed class SaidaLimitada : MemoryStream
    {
        public override void Write(byte[] buffer, int inicio, int tamanho)
        { Conferir(tamanho); base.Write(buffer, inicio, tamanho); }
        public override void Write(ReadOnlySpan<byte> buffer)
        { Conferir(buffer.Length); base.Write(buffer); }
        public override void WriteByte(byte valor) { Conferir(1); base.WriteByte(valor); }
        private void Conferir(int tamanho)
        { if (tamanho > Limite - Length) throw ErroMailcore.Configuracao("O corpo ultrapassa 50 MiB."); }
    }
}
