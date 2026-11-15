using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mailcore;

namespace Mailcore.ConsumidorInstalado;

internal static class Programa
{
    private static readonly byte[] Binario = [0, 255, 65, 13, 10, 0, 128];
    private static string etapa = "configuracao";

    private sealed class TransporteSintetico : HttpMessageHandler
    {
        public int Requisicoes { get; private set; }
        public string Modo { get; set; } = "normal";
        public string Formato { get; set; } = "json";
        public bool Vazio { get; set; }
        public bool Encerrado { get; private set; }
        public string? Verbo { get; private set; }
        public string? Caminho { get; private set; }
        public byte[] Corpo { get; private set; } = [];
        public Dictionary<string, string[]> Cabecalhos { get; } = new(StringComparer.OrdinalIgnoreCase);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
        {
            cancelamento.ThrowIfCancellationRequested();
            Requisicoes++; Verbo = pedido.Method.Method; Caminho = pedido.RequestUri!.AbsolutePath;
            Corpo = pedido.Content is null ? [] : await pedido.Content.ReadAsByteArrayAsync(cancelamento);
            Cabecalhos.Clear();
            foreach (var par in pedido.Headers) Cabecalhos[par.Key] = par.Value.ToArray();
            var status = HttpStatusCode.OK;
            var dados = Formato switch
            {
                "texto" => Encoding.UTF8.GetBytes("<p>Prévia sintética 📬</p>"),
                "bytes" or "fluxo" => Binario,
                _ when Vazio => [],
                _ => Encoding.UTF8.GetBytes("{\"id\":\"sintetico\",\"objeto\":{},\"lista\":[],\"nulo\":null,\"ativo\":false}")
            };
            if (Modo == "preciso") dados = Encoding.UTF8.GetBytes("{\"inteiro\":123456789012345678901234567890,\"decimal\":0.12345678901234567890123456789,\"objeto\":{},\"lista\":[],\"ativo\":false,\"nulo\":null}");
            if (Modo == "erro")
            {
                status = HttpStatusCode.UnprocessableEntity;
                dados = Encoding.UTF8.GetBytes("{\"erro\":{\"codigo\":\"validacao\",\"mensagem\":\"detalhe-privado-sintetico\"}}");
            }
            var resposta = new HttpResponseMessage(status) { Content = new ByteArrayContent(dados), RequestMessage = pedido };
            resposta.Headers.TryAddWithoutValidation("X-Request-Id", "req-sintetica");
            resposta.Headers.TryAddWithoutValidation("Set-Cookie", "sessao=sintetica");
            return resposta;
        }

        protected override void Dispose(bool disposing)
        {
            Encerrado = true; base.Dispose(disposing);
        }
    }

    private static void Exigir(bool condicao)
    {
        if (!condicao) throw new InvalidOperationException("O consumidor instalado não satisfez o contrato.");
    }

    private static string Pascal(string valor) => string.Concat(valor.Split('_').Select(parte => char.ToUpperInvariant(parte[0]) + parte[1..]));

    private static Dictionary<string, object> Recursos(object raiz)
    {
        var encontrados = new Dictionary<string, object>();
        void Visitar(object objeto)
        {
            foreach (var propriedade in objeto.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (propriedade.PropertyType.Namespace != "Mailcore.Recursos") continue;
                var filho = propriedade.GetValue(objeto) ?? throw new InvalidOperationException();
                if (encontrados.TryAdd(filho.GetType().Name, filho)) Visitar(filho);
            }
        }
        Visitar(raiz); return encontrados;
    }

    private static object? Converter(JsonElement dado, Type tipo)
    {
        if (tipo == typeof(string)) return dado.GetString();
        if (tipo == typeof(int)) return dado.GetInt32();
        if (tipo == typeof(long)) return dado.GetInt64();
        if (tipo == typeof(IReadOnlyDictionary<string, object?>))
            return dado.ValueKind == JsonValueKind.Null ? null : dado.EnumerateObject().ToDictionary(
                item => item.Name, item => item.Value.ValueKind == JsonValueKind.Null ? null : (object?)item.Value.Clone());
        return dado.Clone();
    }

    private static object PedidoTipado()
    {
        var tipo = typeof(Cliente).Assembly.GetType("Mailcore.Modelos.PedidoEmail") ?? throw new InvalidOperationException();
        var pedido = Activator.CreateInstance(tipo) ?? throw new InvalidOperationException();
        foreach (var campo in new Dictionary<string, object>
        {
            ["Remetente"] = "origem@example.test", ["Destinatarios"] = new[] { "destino@example.test" },
            ["Assunto"] = "Olá 📬", ["Texto"] = "Dados sintéticos", ["Html"] = "<p>Dados sintéticos</p>"
        }) tipo.GetProperty(campo.Key)!.SetValue(pedido, campo.Value);
        Exigir(!pedido.ToString()!.Contains("destino@example.test", StringComparison.Ordinal));
        return pedido;
    }

    private static void Assinatura()
    {
        // Vetor independente do transporte; a autenticação ocorre sobre estes bytes originais.
        var corpo = Encoding.UTF8.GetBytes("{\"type\":\"email.received\",\"created_at\":\"2026-11-14T00:00:00Z\",\"data\":{\"ativo\":false,\"nulo\":null,\"objeto\":{},\"lista\":[]}}");
        var chave = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
        var instante = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var mensagem = Encoding.UTF8.GetBytes("msg_sintetico." + instante + ".").Concat(corpo).ToArray();
        var assinatura = Convert.ToBase64String(HMACSHA256.HashData(chave, mensagem));
        var cabecalhos = new Dictionary<string, IReadOnlyList<string>>
        {
            ["svix-id"] = ["msg_sintetico"], ["svix-timestamp"] = [instante], ["svix-signature"] = ["v1," + assinatura]
        };
        var evento = Mailcore.Assinatura.Verificar(corpo, cabecalhos, "whsec_" + Convert.ToBase64String(chave));
        Exigir(evento.Tipo == "email.received" && !evento.ToString()!.Contains("ativo", StringComparison.Ordinal));
    }

    private static async Task<Dictionary<string, object?>> Executar()
    {
        var pasta = Path.GetFullPath(Environment.GetEnvironmentVariable("MAILCORE_CONSUMIDOR") ?? throw new InvalidOperationException());
        var localDll = Path.GetFullPath(typeof(Cliente).Assembly.Location);
        Exigir(localDll.StartsWith(pasta + Path.DirectorySeparatorChar, StringComparison.Ordinal) && !localDll.StartsWith("/sdk/", StringComparison.Ordinal));
        var tfm = typeof(Cliente).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
        Exigir(tfm.StartsWith(".NETCoreApp,Version=v", StringComparison.Ordinal));
        var frameworkBiblioteca = "net" + tfm[".NETCoreApp,Version=v".Length..];
        using var fixture = JsonDocument.Parse(File.ReadAllBytes(Environment.GetEnvironmentVariable("MAILCORE_FIXTURE")!));
        Exigir(fixture.RootElement.GetArrayLength() == 151);
        using var handler = new TransporteSintetico();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        var configuracao = new Configuracao { UrlBase = "https://api.example.test/v1" };
        using var cliente = new Cliente(http, "mc_sintetica", configuracao);
        configuracao.UrlBase = "https://alterado.example.test/v1";
        var recursos = Recursos(cliente); Exigir(recursos.Count == 30);
        var metodos = 0; var callbacks = 0;
        foreach (var caso in fixture.RootElement.EnumerateArray())
        {
            etapa = caso.GetProperty("classe").GetString() + "." + caso.GetProperty("metodo").GetString();
            var recurso = recursos[caso.GetProperty("classe").GetString()!];
            var extras = caso.GetProperty("kwargs");
            handler.Formato = extras.TryGetProperty("formato", out var formato) ? formato.GetString()! : "json";
            handler.Vazio = extras.TryGetProperty("aceitar_vazio", out var vazio) && vazio.GetBoolean();
            var argsJson = caso.GetProperty("argumentos").EnumerateArray().ToArray();
            var fluxo = handler.Formato == "fluxo";
            var quantidade = argsJson.Length + 2 + (fluxo ? 1 : 0);
            var nome = Pascal(caso.GetProperty("metodo").GetString()!) + "Async";
            var metodo = recurso.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance).Single(candidato =>
                candidato.Name == nome && candidato.GetParameters().Length == quantidade && candidato.GetParameters()[^2].ParameterType == typeof(OpcoesRequisicao));
            var parametros = metodo.GetParameters(); var argumentos = new object?[quantidade];
            for (var indice = 0; indice < argsJson.Length; indice++) argumentos[indice] = Converter(argsJson[indice], parametros[indice].ParameterType);
            using var partes = new MemoryStream();
            if (fluxo)
            {
                Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consumir = (parte, token) => partes.WriteAsync(parte, token);
                argumentos[^3] = consumir; callbacks++;
            }
            argumentos[^2] = new OpcoesRequisicao { ChaveIdempotencia = "consumidor-" + metodos.ToString(CultureInfo.InvariantCulture) };
            argumentos[^1] = CancellationToken.None;
            var tarefa = metodo.Invoke(recurso, argumentos) as Task<Resposta> ?? throw new InvalidOperationException();
            var resposta = await tarefa; Exigir(resposta.Sucesso && resposta.Status == 200);
            Exigir(handler.Verbo == caso.GetProperty("verbo").GetString() && handler.Caminho == "/v1" + caso.GetProperty("caminho").GetString());
            Exigir(handler.Cabecalhos["Authorization"].SequenceEqual(["Bearer mc_sintetica"]));
            Exigir(!resposta.Cabecalhos.ContainsKey("set-cookie"));
            if (fluxo) Exigir(resposta.Dados.Length == 0 && partes.ToArray().SequenceEqual(Binario) && resposta.TotalBytes == Binario.Length);
            else if (handler.Formato is "bytes") Exigir(resposta.Dados.SequenceEqual(Binario));
            else if (!handler.Vazio && handler.Formato == "json") Exigir(resposta.Json().GetProperty("ativo").GetBoolean() == false);
            metodos++;
        }
        Exigir(metodos == 151 && callbacks == 3 && handler.Requisicoes == 151);
        handler.Formato = "json"; handler.Vazio = false;
        etapa = "pedido_tipado";
        var pedido = PedidoTipado();
        Exigir((await cliente.Emails.EnviarAsync(pedido, new OpcoesRequisicao { ChaveIdempotencia = "consumidor-tipado" })).Sucesso);
        using (var enviado = JsonDocument.Parse(handler.Corpo))
        {
            Exigir(enviado.RootElement.GetProperty("remetente").GetString() == "origem@example.test");
            Exigir(enviado.RootElement.GetProperty("destinatarios")[0].GetString() == "destino@example.test");
        }
        handler.Modo = "preciso";
        etapa = "json_preciso";
        var preciso = await cliente.Emails.ObterAsync("id"); var json = preciso.Json();
        Exigir(json.GetProperty("inteiro").GetRawText() == "123456789012345678901234567890");
        Exigir(json.GetProperty("decimal").GetRawText() == "0.12345678901234567890123456789");
        Exigir(json.GetProperty("objeto").ValueKind == JsonValueKind.Object && json.GetProperty("lista").ValueKind == JsonValueKind.Array);
        var copia = preciso.Dados; copia[0] = 0; Exigir(preciso.Dados[0] != 0);
        handler.Modo = "erro";
        etapa = "modo_erro_sem_excecao";
        using (var semExcecao = new Cliente(http, "mc_sintetica", new Configuracao { LancarExcecoes = false }))
        {
            var recusada = await semExcecao.Emails.ObterAsync("id");
            Exigir(!recusada.Sucesso && recusada.Status == 422 && recusada.Erro?.Codigo == "validacao");
            Exigir(!recusada.Erro!.ToString().Contains("detalhe-privado-sintetico", StringComparison.Ordinal));
            Exigir(!recusada.Erro.Message.Contains("detalhe-privado-sintetico", StringComparison.Ordinal));
        }
        using (var cancelamento = new CancellationTokenSource())
        {
            etapa = "cancelamento_antes_rede";
            cancelamento.Cancel();
            var cancelado = false;
            try { await cliente.Emails.ObterAsync("id", cancelamento: cancelamento.Token); }
            catch (OperationCanceledException erro) { cancelado = erro.CancellationToken == cancelamento.Token; }
            Exigir(cancelado && handler.Requisicoes == 154);
        }
        etapa = "assinatura_webhook";
        Assinatura();
        etapa = "propriedade_httpclient";
        cliente.Dispose(); Exigir(!handler.Encerrado);
        handler.Modo = "normal";
        using (var retorno = await http.GetAsync("https://api.example.test/continua-aberto")) Exigir(retorno.IsSuccessStatusCode);
        Exigir(handler.Requisicoes == 155);
        return new Dictionary<string, object?>
        {
            ["resultado"] = "aprovado", ["dotnet"] = Environment.Version.ToString(), ["framework_biblioteca"] = frameworkBiblioteca,
            ["recursos"] = recursos.Count, ["metodos"] = metodos, ["callbacks_fluxo"] = callbacks, ["operacoes_extras"] = 4,
            ["requisicoes_http"] = handler.Requisicoes, ["origem_dll_fora_checkout"] = true, ["pedido_tipado"] = true,
            ["json_preciso"] = true, ["assinatura_webhook"] = true, ["erro_sanitizado"] = true,
            ["modo_erro_sem_excecao"] = true, ["cancelamento_sem_rede"] = true, ["cliente_http_preservado"] = true,
            ["rede_real"] = false
        };
    }

    public static async Task<int> Main()
    {
        try
        {
            Console.WriteLine(JsonSerializer.Serialize(await Executar()));
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine(JsonSerializer.Serialize(new { resultado = "falhou", etapa,
                falha = "Contrato de instalação não satisfeito; detalhes privados omitidos." }));
            return 1;
        }
    }
}
