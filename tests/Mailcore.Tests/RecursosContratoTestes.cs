using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Mailcore.Tests;

/// <summary>Contratos independentes por AST do SDK Node a234dc2, exercitados por HTTP real.</summary>
public sealed class RecursosContratoTestes
{
    private static readonly byte[] Binario = [0, 255, 65, 13, 10, 0, 128];

    public static IEnumerable<object[]> Casos()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, "Fixtures", "contratos-recursos.json");
        using var documento = JsonDocument.Parse(File.ReadAllBytes(caminho));
        Assert.Equal(151, documento.RootElement.GetArrayLength());
        var indice = 0;
        foreach (var caso in documento.RootElement.EnumerateArray())
        {
            yield return [indice++, caso.GetProperty("classe").GetString() + "." + caso.GetProperty("metodo").GetString(), caso.GetRawText()];
        }
    }

    [Theory]
    [MemberData(nameof(Casos))]
    public async Task ExecutaOperacaoPelaRedeHttpRealAsync(int indice, string rotulo, string casoJson)
    {
        Assert.InRange(indice, 0, 150);
        Assert.NotEmpty(rotulo);
        using var documento = JsonDocument.Parse(casoJson);
        var caso = documento.RootElement;
        var extras = caso.GetProperty("kwargs");
        var formato = extras.TryGetProperty("formato", out var formatoJson) ? formatoJson.GetString() : "json";
        var aceitarVazio = extras.TryGetProperty("aceitar_vazio", out var vazio) && vazio.GetBoolean();
        var corpoResposta = formato switch
        {
            "texto" => Encoding.UTF8.GetBytes("<p>Prévia sintética 📬</p>"),
            "bytes" or "fluxo" => Binario,
            _ when aceitarVazio => [],
            _ => Encoding.UTF8.GetBytes("{\"id\":\"sintetico\",\"objeto\":{},\"lista\":[],\"nulo\":null,\"ativo\":false}")
        };
        await using var servidor = new ServidorContrato(corpoResposta, formato);
        using var cliente = new Cliente(new Configuracao
        {
            ChaveApi = "mc_contrato_sintetico",
            UrlBase = "http://127.0.0.1:" + servidor.Porta.ToString(CultureInfo.InvariantCulture) + "/v1",
            PermitirHttpLocal = true
        });
        var opcoes = new OpcoesRequisicao
        {
            ChaveIdempotencia = "contrato-" + indice.ToString(CultureInfo.InvariantCulture),
            TempoLimite = TimeSpan.FromSeconds(5),
            LimiteRespostaBytes = 4096,
            Cabecalhos = new Dictionary<string, IReadOnlyList<string>>
            {
                ["X-Correlacao"] = ["sintetica"],
                ["X-Multiplo"] = ["um", "dois"]
            }
        };
        using var partes = new MemoryStream();
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consumir = (parte, cancelamento) => partes.WriteAsync(parte, cancelamento);
        var recurso = EncontrarRecurso(cliente, caso.GetProperty("classe").GetString()!);
        var nomeMetodo = Pascal(caso.GetProperty("metodo").GetString()!) + "Async";
        var argumentosJson = caso.GetProperty("argumentos").EnumerateArray().ToArray();
        var quantidade = argumentosJson.Length + 2 + (formato == "fluxo" ? 1 : 0);
        var metodo = recurso.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(candidato =>
        {
            var parametros = candidato.GetParameters();
            return candidato.Name == nomeMetodo && parametros.Length == quantidade &&
                parametros[^2].ParameterType == typeof(OpcoesRequisicao) && parametros[^1].ParameterType == typeof(CancellationToken);
        });
        var parametrosMetodo = metodo.GetParameters();
        var argumentos = new object?[quantidade];
        for (var atual = 0; atual < argumentosJson.Length; atual++)
        {
            argumentos[atual] = ConverterArgumento(argumentosJson[atual], parametrosMetodo[atual].ParameterType);
        }
        if (formato == "fluxo")
        {
            argumentos[^3] = consumir;
        }
        argumentos[^2] = opcoes;
        argumentos[^1] = CancellationToken.None;
        var tarefa = Assert.IsAssignableFrom<Task<Resposta>>(metodo.Invoke(recurso, argumentos));
        var resposta = await tarefa;
        var pedido = await servidor.Pedido.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(200, resposta.Status);
        Assert.Equal(corpoResposta.LongLength, resposta.TotalBytes);
        Assert.Equal(new[] { "req-sintetica" }, resposta.Cabecalhos.Single(campo => campo.Key.Equals("x-request-id", StringComparison.OrdinalIgnoreCase)).Value);
        Assert.Equal(caso.GetProperty("verbo").GetString(), pedido.Verbo);
        var alvo = pedido.Alvo.Split('?', 2);
        Assert.Equal("/v1" + caso.GetProperty("caminho").GetString(), alvo[0]);
        var consulta = LerConsulta(alvo.Length == 2 ? alvo[1] : "");
        var esperada = extras.TryGetProperty("consulta", out var consultaEsperada)
            ? consultaEsperada.EnumerateObject().Where(p => p.Value.ValueKind != JsonValueKind.Null).ToDictionary(p => p.Name, p => TextoConsulta(p.Value))
            : new Dictionary<string, string>();
        Assert.Equal(esperada.OrderBy(p => p.Key, StringComparer.Ordinal), consulta.OrderBy(p => p.Key, StringComparer.Ordinal));
        Assert.Equal(new[] { "Bearer mc_contrato_sintetico" }, pedido.Cabecalhos["authorization"]);
        Assert.Equal(new[] { opcoes.ChaveIdempotencia }, pedido.Cabecalhos["idempotency-key"]);
        Assert.Equal(new[] { "sintetica" }, pedido.Cabecalhos["x-correlacao"]);
        Assert.Equal(new[] { "um", "dois" }, pedido.Cabecalhos["x-multiplo"].SelectMany(v => v.Split(',')).Select(v => v.Trim()));
        Assert.Equal(new[] { "identity" }, pedido.Cabecalhos["accept-encoding"]);
        if (extras.TryGetProperty("corpo", out var corpoEsperado))
        {
            using var corpo = JsonDocument.Parse(pedido.Corpo);
            ConferirJson(corpoEsperado, corpo.RootElement);
        }
        else
        {
            Assert.Empty(pedido.Corpo);
        }
        if (formato == "fluxo")
        {
            Assert.Empty(resposta.Dados);
            Assert.Equal(corpoResposta, partes.ToArray());
        }
        else
        {
            Assert.Equal(corpoResposta, resposta.Dados);
            if (formato == "json" && corpoResposta.Length > 0)
            {
                Assert.Equal("sintetico", resposta.Json().GetProperty("id").GetString());
            }
        }
    }

    private static string Pascal(string nome) => string.Concat(nome.Split('_').Select(parte => char.ToUpperInvariant(parte[0]) + parte[1..]));

    private static object EncontrarRecurso(object atual, string classe)
    {
        if (atual.GetType().Name == classe && atual.GetType().Namespace == "Mailcore.Recursos")
        {
            return atual;
        }
        foreach (var propriedade in atual.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (propriedade.PropertyType.Namespace != "Mailcore.Recursos")
            {
                continue;
            }
            var filho = propriedade.GetValue(atual);
            if (filho is null)
            {
                continue;
            }
            try
            {
                return EncontrarRecurso(filho, classe);
            }
            catch (KeyNotFoundException)
            {
                // Cada árvore possui apenas seus recursos subordinados.
            }
        }
        throw new KeyNotFoundException("Recurso não encontrado no cliente.");
    }

    private static object? ConverterArgumento(JsonElement valor, Type tipo)
    {
        if (tipo == typeof(string))
        {
            return valor.GetString();
        }
        if (tipo == typeof(int))
        {
            return valor.GetInt32();
        }
        return ConverterJson(valor);
    }

    private static object? ConverterJson(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.Object => valor.EnumerateObject().ToDictionary(p => p.Name, p => ConverterJson(p.Value)),
        JsonValueKind.Array => valor.EnumerateArray().Select(ConverterJson).ToArray(),
        JsonValueKind.String => valor.GetString(),
        JsonValueKind.Number when valor.TryGetInt64(out var inteiro) => inteiro,
        JsonValueKind.Number => valor.GetDecimal(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        JsonValueKind.Null => null,
        _ => throw new InvalidOperationException("Valor de fixture inválido.")
    };

    private static string TextoConsulta(JsonElement valor) => valor.ValueKind switch
    {
        JsonValueKind.String => valor.GetString()!,
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        _ => valor.GetRawText()
    };

    private static Dictionary<string, string> LerConsulta(string texto)
    {
        var resultado = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var parte in texto.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var campos = parte.Split('=', 2);
            resultado.Add(WebUtility.UrlDecode(campos[0]), WebUtility.UrlDecode(campos.Length > 1 ? campos[1] : ""));
        }
        return resultado;
    }

    private static void ConferirJson(JsonElement esperado, JsonElement atual)
    {
        Assert.Equal(esperado.ValueKind, atual.ValueKind);
        switch (esperado.ValueKind)
        {
            case JsonValueKind.Object:
                var propriedades = esperado.EnumerateObject().ToArray();
                Assert.Equal(propriedades.Length, atual.EnumerateObject().Count());
                foreach (var propriedade in propriedades)
                {
                    Assert.True(atual.TryGetProperty(propriedade.Name, out var dado));
                    ConferirJson(propriedade.Value, dado);
                }
                break;
            case JsonValueKind.Array:
                Assert.Equal(esperado.GetArrayLength(), atual.GetArrayLength());
                for (var indice = 0; indice < esperado.GetArrayLength(); indice++)
                {
                    ConferirJson(esperado[indice], atual[indice]);
                }
                break;
            case JsonValueKind.String:
                Assert.Equal(esperado.GetString(), atual.GetString());
                break;
            default:
                Assert.Equal(esperado.GetRawText(), atual.GetRawText());
                break;
        }
    }

    private sealed record Pedido(string Verbo, string Alvo, IReadOnlyDictionary<string, List<string>> Cabecalhos, byte[] Corpo);

    private sealed class ServidorContrato : IAsyncDisposable
    {
        private readonly TcpListener _escuta = new(IPAddress.Loopback, 0);
        private readonly CancellationTokenSource _encerramento = new(TimeSpan.FromSeconds(10));
        private readonly Task<Pedido> _pedido;

        internal ServidorContrato(byte[] corpoResposta, string? formato)
        {
            _escuta.Start(1);
            Porta = ((IPEndPoint)_escuta.LocalEndpoint).Port;
            _pedido = AtenderAsync(corpoResposta, formato, _encerramento.Token);
        }

        internal int Porta { get; }
        internal Task<Pedido> Pedido => _pedido;

        private async Task<Pedido> AtenderAsync(byte[] corpoResposta, string? formato, CancellationToken cancelamento)
        {
            using var conexao = await _escuta.AcceptTcpClientAsync(cancelamento);
            await using var rede = conexao.GetStream();
            var cabecalho = await LerCabecalhoAsync(rede, cancelamento);
            var linhas = Encoding.ASCII.GetString(cabecalho).Split("\r\n", StringSplitOptions.None);
            var inicio = linhas[0].Split(' ');
            var campos = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            foreach (var linha in linhas.Skip(1).Where(linha => linha.Length > 0))
            {
                var separador = linha.IndexOf(':');
                if (separador < 1)
                {
                    throw new InvalidDataException("Cabeçalho de laboratório inválido.");
                }
                var nome = linha[..separador].ToLowerInvariant();
                if (!campos.TryGetValue(nome, out var valores))
                {
                    valores = [];
                    campos.Add(nome, valores);
                }
                valores.Add(linha[(separador + 1)..].Trim());
            }
            byte[] corpo;
            if (campos.TryGetValue("transfer-encoding", out var codificacao) && string.Join(",", codificacao).Contains("chunked", StringComparison.OrdinalIgnoreCase))
            {
                using var acumulado = new MemoryStream();
                while (true)
                {
                    var linha = await LerLinhaAsync(rede, cancelamento);
                    var tamanho = int.Parse(linha.Split(';')[0], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                    if (tamanho == 0)
                    {
                        while (await LerLinhaAsync(rede, cancelamento) != "") { }
                        break;
                    }
                    if (tamanho > 1024 * 1024 - acumulado.Length)
                    {
                        throw new InvalidDataException("Pedido de laboratório excedido.");
                    }
                    var parte = new byte[tamanho];
                    await rede.ReadExactlyAsync(parte, cancelamento);
                    acumulado.Write(parte);
                    Assert.Equal("", await LerLinhaAsync(rede, cancelamento));
                }
                corpo = acumulado.ToArray();
            }
            else
            {
                var tamanho = campos.TryGetValue("content-length", out var comprimentos)
                    ? int.Parse(comprimentos.Single(), CultureInfo.InvariantCulture) : 0;
                if (tamanho is < 0 or > 1024 * 1024)
                {
                    throw new InvalidDataException("Pedido de laboratório excedido.");
                }
                corpo = new byte[tamanho];
                await rede.ReadExactlyAsync(corpo, cancelamento);
            }
            var tipo = formato is "bytes" or "fluxo" ? "application/octet-stream" : formato == "texto" ? "text/html; charset=utf-8" : "application/json";
            var resposta = Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nConnection: close\r\nX-Request-Id: req-sintetica\r\nContent-Type: " + tipo + "\r\nContent-Length: " + corpoResposta.Length.ToString(CultureInfo.InvariantCulture) + "\r\n\r\n");
            await rede.WriteAsync(resposta, cancelamento);
            await rede.WriteAsync(corpoResposta, cancelamento);
            await rede.FlushAsync(cancelamento);
            return new Pedido(inicio[0], inicio[1], campos, corpo);
        }

        private static async Task<byte[]> LerCabecalhoAsync(Stream rede, CancellationToken cancelamento)
        {
            using var bytes = new MemoryStream();
            var caractere = new byte[1];
            var anterior = 0;
            while (bytes.Length < 65536)
            {
                await rede.ReadExactlyAsync(caractere, cancelamento);
                bytes.WriteByte(caractere[0]);
                anterior = (anterior << 8) | caractere[0];
                if (anterior == 0x0d0a0d0a)
                {
                    return bytes.ToArray()[..^4];
                }
            }
            throw new InvalidDataException("Cabeçalhos de laboratório excedidos.");
        }

        private static async Task<string> LerLinhaAsync(Stream rede, CancellationToken cancelamento)
        {
            using var bytes = new MemoryStream();
            var caractere = new byte[1];
            while (bytes.Length < 65536)
            {
                await rede.ReadExactlyAsync(caractere, cancelamento);
                if (caractere[0] == 10)
                {
                    var linha = bytes.ToArray();
                    if (linha.Length == 0 || linha[^1] != 13)
                    {
                        throw new InvalidDataException("Linha de laboratório inválida.");
                    }
                    return Encoding.ASCII.GetString(linha, 0, linha.Length - 1);
                }
                bytes.WriteByte(caractere[0]);
            }
            throw new InvalidDataException("Linha de laboratório excedida.");
        }

        public async ValueTask DisposeAsync()
        {
            await _encerramento.CancelAsync();
            _escuta.Stop();
            try { await _pedido; }
            catch (Exception) { /* O encerramento apenas libera recursos exclusivos do laboratório. */ }
            _encerramento.Dispose();
        }
    }
}
