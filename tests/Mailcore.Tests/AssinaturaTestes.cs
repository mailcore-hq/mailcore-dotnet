using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Mailcore.Tests;

/// <summary>Vetores independentes de HMAC com bytes sintéticos e relógio determinístico.</summary>
public sealed class AssinaturaTestes
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.Parse("2026-11-14T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly byte[] Chave = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
    private static readonly byte[] Corpo = Encoding.UTF8.GetBytes("{\"type\":\"email.received\",\"created_at\":\"2026-11-14T00:00:00Z\",\"data\":{\"email_id\":\"mensagem-sintetica\",\"to\":[\"destino@example.test\"],\"texto\":\"Olá 📬\",\"ativo\":false,\"nulo\":null,\"objeto\":{},\"lista\":[]}}");

    [Fact]
    public void AutenticaVetorIndependenteEPreservaOsDados()
    {
        var vetor = CriarVetor(Corpo, Agora.ToUnixTimeSeconds());
        // Valor calculado fora do SDK por HMAC-SHA256 da biblioteca padrão Python.
        Assert.Equal("v1,kpfPDdmDStQoOESISFD6H1GvGmAk3tMJ0edyyXZ97YM=", vetor.Cabecalhos["svix-signature"].Single());
        var evento = Assinatura.Verificar(Corpo, vetor.Cabecalhos, vetor.Segredo, Opcoes());
        Assert.Equal("email.received", evento.Tipo);
        Assert.Equal("2026-11-14T00:00:00Z", evento.CriadoEm);
        Assert.False(evento.Dados.GetProperty("ativo").GetBoolean());
        Assert.Equal(JsonValueKind.Null, evento.Dados.GetProperty("nulo").ValueKind);
        Assert.Equal(JsonValueKind.Object, evento.Dados.GetProperty("objeto").ValueKind);
        Assert.Equal(JsonValueKind.Array, evento.Dados.GetProperty("lista").ValueKind);
        Assert.Equal("Olá 📬", evento.Dados.GetProperty("texto").GetString());
        Assert.DoesNotContain("destino@example.test", evento.ToString());
        Assert.DoesNotContain("mensagem-sintetica", evento.ToString());
        Recusar([.. Corpo, (byte)' '], vetor.Cabecalhos, vetor.Segredo);
    }

    [Fact]
    public void JanelaTemporalEPositivaSimetricaELimitada()
    {
        foreach (var diferenca in new long[] { -301, -300, -299, 0, 299, 300, 301 })
        {
            var vetor = CriarVetor(Corpo, Agora.ToUnixTimeSeconds() + diferenca);
            if (Math.Abs(diferenca) <= 300)
            {
                Assert.NotNull(Assinatura.Verificar(Corpo, vetor.Cabecalhos, vetor.Segredo, Opcoes()));
            }
            else
            {
                Recusar(Corpo, vetor.Cabecalhos, vetor.Segredo);
            }
        }
        var foraDaJanela = CriarVetor(Corpo, Agora.ToUnixTimeSeconds() + 31);
        Recusar(Corpo, foraDaJanela.Cabecalhos, foraDaJanela.Segredo, Opcoes() with { Tolerancia = TimeSpan.FromSeconds(30) });
        foreach (var tolerancia in new[] { TimeSpan.Zero, TimeSpan.FromSeconds(-1), TimeSpan.FromSeconds(301) })
        {
            var vetor = CriarVetor(Corpo, Agora.ToUnixTimeSeconds());
            Recusar(Corpo, vetor.Cabecalhos, vetor.Segredo, Opcoes() with { Tolerancia = tolerancia });
        }
    }

    [Fact]
    public void RecusaCabecalhosAusentesDuplicadosOuAmbiguos()
    {
        var original = CriarVetor(Corpo, Agora.ToUnixTimeSeconds());
        foreach (var nome in new[] { "svix-id", "svix-timestamp", "svix-signature" })
        {
            var ausentes = Copiar(original.Cabecalhos);
            ausentes.Remove(nome);
            Recusar(Corpo, ausentes, original.Segredo);
            var duplicados = Copiar(original.Cabecalhos);
            duplicados[nome] = [duplicados[nome][0], duplicados[nome][0]];
            Recusar(Corpo, duplicados, original.Segredo);
            var ambiguos = Copiar(original.Cabecalhos);
            ambiguos.Add(nome.ToUpperInvariant(), original.Cabecalhos[nome]);
            Recusar(Corpo, ambiguos, original.Segredo);
            var vazio = Copiar(original.Cabecalhos);
            vazio[nome] = [];
            Recusar(Corpo, vazio, original.Segredo);
        }
        foreach (var campo in new[] { ("svix-id", new string('m', 257)), ("svix-timestamp", new string('1', 17)), ("svix-signature", "v1,ÿ") })
        {
            var campos = Copiar(original.Cabecalhos);
            campos[campo.Item1] = [campo.Item2];
            Recusar(Corpo, campos, original.Segredo);
        }
        Recusar(Corpo, null!, original.Segredo);
    }

    [Fact]
    public void TimestampRecusaSinalOverflowERepresentacoesAlternativas()
    {
        foreach (var instante in new[] { "0", "-1", "001794614400", "1794614400.0", "1e10", " 1794614400", "9223372036854775807", "-9223372036854775808", "17946144000000000" })
        {
            var vetor = CriarVetor(Corpo, instante);
            Recusar(Corpo, vetor.Cabecalhos, vetor.Segredo);
        }
    }

    [Fact]
    public void ExigeVersaoBase64CanonicoEPrefixoDoSegredo()
    {
        var original = CriarVetor(Corpo, Agora.ToUnixTimeSeconds());
        var assinatura = original.Cabecalhos["svix-signature"][0][3..];
        foreach (var valor in new[] { "v2," + assinatura, assinatura, "v1," + assinatura.Replace("=", "", StringComparison.Ordinal), "v1,%%%", "v1," + new string('a', 4097) })
        {
            var campos = Copiar(original.Cabecalhos);
            campos["svix-signature"] = [valor];
            Recusar(Corpo, campos, original.Segredo);
        }
        foreach (var segredo in new[] { "", original.Segredo[6..], "whsec_%%%", original.Segredo + "\n", original.Segredo.Replace("=", "", StringComparison.Ordinal), "whsec_" + new string('a', 1025) })
        {
            Recusar(Corpo, original.Cabecalhos, segredo);
        }
        foreach (var tamanho in new[] { 15, 16, 768, 769 })
        {
            var chave = Enumerable.Repeat((byte)'a', tamanho).ToArray();
            var vetor = CriarVetor(Corpo, Agora.ToUnixTimeSeconds(), chave);
            if (tamanho is >= 16 and <= 768)
            {
                Assert.NotNull(Assinatura.Verificar(Corpo, vetor.Cabecalhos, vetor.Segredo, Opcoes()));
            }
            else
            {
                Recusar(Corpo, vetor.Cabecalhos, vetor.Segredo);
            }
        }
    }

    [Fact]
    public void RotacaoConsideraNoMaximoTrintaEDoisCandidatos()
    {
        var original = CriarVetor(Corpo, Agora.ToUnixTimeSeconds());
        var correta = original.Cabecalhos["svix-signature"][0];
        var campos = Copiar(original.Cabecalhos);
        campos["svix-signature"] = ["v2,invalida v1,invalida " + correta];
        Assert.NotNull(Assinatura.Verificar(Corpo, campos, original.Segredo, Opcoes()));
        campos["svix-signature"] = [string.Concat(Enumerable.Repeat("v1,invalida ", 31)) + correta];
        Assert.NotNull(Assinatura.Verificar(Corpo, campos, original.Segredo, Opcoes()));
        campos["svix-signature"] = [string.Concat(Enumerable.Repeat("v1,invalida ", 32)) + correta];
        Recusar(Corpo, campos, original.Segredo);
    }

    [Fact]
    public void AssinaturaValidaAindaExigeJsonUtf8EEnvelopeValidos()
    {
        var invalidos = new[] { "[]", "null", "{}", "{} {}", "{\"type\":\"email.received\",\"created_at\":\"data\"}", "{\"type\":3,\"created_at\":\"data\",\"data\":{}}", "{\"type\":\"\",\"created_at\":\"data\",\"data\":{}}", "{\"type\":\"evento\",\"created_at\":\"\",\"data\":{}}", "{\"type\":\"evento\",\"type\":\"outro\",\"created_at\":\"data\",\"data\":{}}" };
        foreach (var texto in invalidos)
        {
            var corpo = Encoding.UTF8.GetBytes(texto);
            var vetor = CriarVetor(corpo, Agora.ToUnixTimeSeconds());
            Recusar(corpo, vetor.Cabecalhos, vetor.Segredo);
        }
        byte[] utf8Invalido = [(byte)'{', (byte)'"', (byte)'x', (byte)'"', (byte)':', (byte)'"', 255, (byte)'"', (byte)'}'];
        var invalido = CriarVetor(utf8Invalido, Agora.ToUnixTimeSeconds());
        Recusar(utf8Invalido, invalido.Cabecalhos, invalido.Segredo);
        var nulo = Encoding.UTF8.GetBytes("{\"type\":\"evento\",\"created_at\":\"data\",\"data\":null}");
        var valido = CriarVetor(nulo, Agora.ToUnixTimeSeconds());
        Assert.Equal(JsonValueKind.Null, Assinatura.Verificar(nulo, valido.Cabecalhos, valido.Segredo, Opcoes()).Dados.ValueKind);
    }

    [Fact]
    public void LimiteDoCorpoEOitoMiBInclusive()
    {
        var prefixo = Encoding.ASCII.GetBytes("{\"type\":\"evento\",\"created_at\":\"data\",\"data\":{\"texto\":\"");
        var sufixo = Encoding.ASCII.GetBytes("\"}}");
        var corpo = Enumerable.Repeat((byte)'a', 8 * 1024 * 1024).ToArray();
        prefixo.CopyTo(corpo, 0);
        sufixo.CopyTo(corpo, corpo.Length - sufixo.Length);
        var vetor = CriarVetor(corpo, Agora.ToUnixTimeSeconds());
        Assert.NotNull(Assinatura.Verificar(corpo, vetor.Cabecalhos, vetor.Segredo, Opcoes()));
        corpo = [.. corpo, (byte)' '];
        vetor = CriarVetor(corpo, Agora.ToUnixTimeSeconds());
        Recusar(corpo, vetor.Cabecalhos, vetor.Segredo);
    }

    private static OpcoesAssinatura Opcoes() => new() { Agora = Agora };

    private static void Recusar(byte[] corpo, IReadOnlyDictionary<string, IReadOnlyList<string>> campos, string segredo, OpcoesAssinatura? opcoes = null)
    {
        var erro = Assert.Throws<ErroMailcore>(() => Assinatura.Verificar(corpo, campos, segredo, opcoes ?? Opcoes()));
        Assert.Equal("assinatura_invalida", erro.Codigo);
        Assert.Null(erro.InnerException);
        foreach (var texto in new[] { erro.Message, erro.ToString() })
        {
            if (segredo.Length > 0)
            {
                Assert.DoesNotContain(segredo, texto, StringComparison.Ordinal);
            }
            Assert.DoesNotContain("destino@example.test", texto);
            Assert.DoesNotContain("mensagem-sintetica", texto);
        }
    }

    private static Dictionary<string, IReadOnlyList<string>> Copiar(IReadOnlyDictionary<string, IReadOnlyList<string>> original) =>
        original.ToDictionary(campo => campo.Key, campo => (IReadOnlyList<string>)campo.Value.ToArray(), StringComparer.Ordinal);

    private static Vetor CriarVetor(byte[] corpo, long instante, byte[]? chave = null) =>
        CriarVetor(corpo, instante.ToString(CultureInfo.InvariantCulture), chave);

    private static Vetor CriarVetor(byte[] corpo, string instante, byte[]? chave = null)
    {
        chave ??= Chave;
        const string id = "msg_sintetico_independente";
        var mensagem = Encoding.ASCII.GetBytes(id + "." + instante + ".").Concat(corpo).ToArray();
        var assinatura = HMACSHA256.HashData(chave, mensagem);
        return new Vetor(new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal)
        {
            ["svix-id"] = [id],
            ["svix-timestamp"] = [instante],
            ["svix-signature"] = ["v1," + Convert.ToBase64String(assinatura)]
        }, "whsec_" + Convert.ToBase64String(chave));
    }

    private sealed record Vetor(IReadOnlyDictionary<string, IReadOnlyList<string>> Cabecalhos, string Segredo);
}
