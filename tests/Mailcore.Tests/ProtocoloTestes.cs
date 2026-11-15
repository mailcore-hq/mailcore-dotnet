using System.Diagnostics;
using System.Net;
using System.Numerics;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailcore.Tests;

[CollectionDefinition("HTTPS", DisableParallelization = true)]
public sealed class ColecaoHttps;

public sealed class LaboratorioHttps : IAsyncLifetime
{
    private Process _processo = null!;
    private readonly SemaphoreSlim _trava = new(1);
    public JsonElement Dados { get; private set; }
    public async Task InitializeAsync()
    {
        _processo = Process.Start(new ProcessStartInfo("python3", Path.Combine(AppContext.BaseDirectory, "Suporte", "servidor_https.py"))
        { RedirectStandardInput = true, RedirectStandardOutput = true, UseShellExecute = false })!;
        Dados = await LerAsync();
    }
    private async Task<JsonElement> LerAsync()
    {
        var linha = await _processo.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(15));
        using var documento = JsonDocument.Parse(linha ?? throw new InvalidOperationException("Laboratório encerrou prematuramente."));
        return documento.RootElement.Clone();
    }
    public async Task<JsonElement> ComandoAsync(string acao, object? configuracao = null)
    {
        await _trava.WaitAsync();
        try
        {
            await _processo.StandardInput.WriteLineAsync(JsonSerializer.Serialize(new { acao, configuracao }));
            await _processo.StandardInput.FlushAsync();
            return await LerAsync();
        }
        finally { _trava.Release(); }
    }
    public Task ConfigurarAsync(object configuracao) => ComandoAsync("configurar", configuracao);
    public Task<JsonElement> RegistrosAsync() => ComandoAsync("registros");
    public Configuracao Configuracao() => new()
    {
        UrlBase = Dados.GetProperty("base").GetString()!,
        CertificadoCa = File.ReadAllBytes(Dados.GetProperty("ca").GetString()!), ChaveApi = "mc_sintetica_local"
    };
    public async Task DisposeAsync()
    {
        if (!_processo.HasExited)
        {
            await ComandoAsync("encerrar");
            try { await _processo.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10)); }
            catch (TimeoutException) { _processo.Kill(entireProcessTree: true); }
        }
        _processo.Dispose(); _trava.Dispose();
    }
}

[Collection("HTTPS")]
public sealed class ProtocoloTestes(LaboratorioHttps laboratorio) : IClassFixture<LaboratorioHttps>, IAsyncLifetime
{
    private Cliente _cliente = null!;
    public async Task InitializeAsync()
    { await laboratorio.ConfigurarAsync(new { modo = "normal" }); _cliente = new Cliente(laboratorio.Configuracao()); }
    public Task DisposeAsync() { _cliente.Dispose(); return Task.CompletedTask; }

    [Fact]
    public async Task EnvioPreservaJsonIdempotenciaEMetadados()
    {
        var pedido = new { texto = "Olá 📬", ativo = false, nulo = (object?)null, objeto = new { }, lista = Array.Empty<object>() };
        var resposta = await _cliente.Emails.EnviarAsync(pedido, new() { ChaveIdempotencia = "intencao-estavel" });
        Assert.Equal(200, resposta.Status);
        Assert.Equal("email-sintetico", resposta.Json().GetProperty("id").GetString());
        var registro = (await laboratorio.RegistrosAsync())[0];
        Assert.Equal("intencao-estavel", registro.GetProperty("cabecalhos").GetProperty("idempotency-key").GetString());
        Assert.Equal("identity", registro.GetProperty("cabecalhos").GetProperty("accept-encoding").GetString());
        Assert.StartsWith("application/json", registro.GetProperty("cabecalhos").GetProperty("content-type").GetString());
        var json = registro.GetProperty("jsonCorpo");
        Assert.Equal(JsonValueKind.Object, json.GetProperty("objeto").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.GetProperty("lista").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("nulo").ValueKind);
        Assert.False(json.GetProperty("ativo").GetBoolean());
        Assert.Equal("Olá 📬", json.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task JsonPreservaBytesNumerosGrandesECopiasDefensivas()
    {
        var bytes = Encoding.UTF8.GetBytes(" {\"inteiro\":123456789012345678901234567890,\"decimal\":0.12345678901234567890123456789,\"ativo\":false,\"nulo\":null} ");
        await RespostaAsync(bytes);
        var resposta = await _cliente.Emails.ObterAsync("id");
        Assert.Equal(bytes, resposta.Dados);
        Assert.Equal("0.12345678901234567890123456789", resposta.Json().GetProperty("decimal").GetRawText());
        var mapa = resposta.Decodificar<Dictionary<string, JsonElement>>()!;
        Assert.Equal("123456789012345678901234567890", mapa["inteiro"].GetRawText());
        var copia = resposta.Dados; copia[0] = 99;
        Assert.Equal(bytes, resposta.Dados);
        var campo = (IDictionary<string, IReadOnlyList<string>>)resposta.Cabecalhos;
        Assert.Throws<NotSupportedException>(() => campo.Add("x", []));
    }

    [Fact]
    public async Task NumerosETextoDoPedidoSaoCodificadosSemConversaoIntermediaria()
    {
        await _cliente.Emails.EnviarAsync(new { inteiro = BigInteger.Parse("123456789012345678901234567890"), valor = 10.00100m });
        var registro = (await laboratorio.RegistrosAsync())[0];
        var bruto = Encoding.UTF8.GetString(Convert.FromBase64String(registro.GetProperty("corpoBase64").GetString()!));
        Assert.Contains("123456789012345678901234567890", bruto);
        Assert.Contains("10.00100", bruto);
    }

    [Fact]
    public async Task TlsValidaAutoridadeEHostname()
    {
        foreach (var alterar in new Action<Configuracao>[]
        {
            c => c.CertificadoCa = null,
            c => c.CertificadoCa = File.ReadAllBytes(laboratorio.Dados.GetProperty("outraCa").GetString()!),
            c => c.UrlBase = c.UrlBase.Replace("127.0.0.1", "localhost", StringComparison.Ordinal)
        })
        {
            var configuracao = laboratorio.Configuracao(); alterar(configuracao);
            using var cliente = new Cliente(configuracao);
            var erro = await Assert.ThrowsAsync<ErroMailcore>(() => cliente.Emails.ObterAsync("id"));
            Assert.Equal("falha_de_rede", erro.Codigo);
        }
    }

    [Fact]
    public async Task HandshakeSemRespostaRespeitaPrazoTotal()
    {
        var c = laboratorio.Configuracao(); c.UrlBase = laboratorio.Dados.GetProperty("tlsParado").GetString()!;
        c.TempoLimite = TimeSpan.FromMilliseconds(150);
        using var cliente = new Cliente(c);
        var inicio = Stopwatch.StartNew();
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => cliente.Emails.ObterAsync("id"));
        Assert.Equal("prazo_excedido", erro.Codigo);
        Assert.True(inicio.Elapsed < TimeSpan.FromSeconds(2));
    }

    [Fact]
    public async Task MutacoesCom503EQuedaAposRecebimentoChegamUmaVez()
    {
        foreach (var chamada in new Func<Task<Resposta>>[] { () => _cliente.Emails.EnviarAsync(new { texto = "sintetico" }), () => _cliente.Emails.CancelarAsync("id") })
        {
            await laboratorio.ConfigurarAsync(new { modo = "erro", status = 503, repetirApos = "0" });
            var erro = await Assert.ThrowsAsync<ErroMailcore>(chamada);
            Assert.Equal(503, erro.Status);
            Assert.Single((await laboratorio.RegistrosAsync()).EnumerateArray());
            await laboratorio.ConfigurarAsync(new { modo = "queda" });
            await Assert.ThrowsAsync<ErroMailcore>(chamada);
            Assert.Single((await laboratorio.RegistrosAsync()).EnumerateArray());
        }
    }

    [Theory]
    [InlineData(301)] [InlineData(302)] [InlineData(303)] [InlineData(307)] [InlineData(308)]
    public async Task RedirecionamentoNaoTransportaCredenciais(int status)
    {
        await laboratorio.ConfigurarAsync(new { modo = "redirecionamento", status });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.EnviarAsync(new { }));
        Assert.Equal("redirecionamento_recusado", erro.Codigo);
        Assert.Equal(status, erro.Status);
        Assert.Single((await laboratorio.RegistrosAsync()).EnumerateArray());
    }

    [Fact]
    public async Task CookiesECabecalhosSensiveisNaoSaoMantidos()
    {
        await laboratorio.ConfigurarAsync(new { modo = "cookies" });
        for (var i = 0; i < 2; i++)
        {
            var r = await _cliente.Emails.ObterAsync("id");
            Assert.False(r.Cabecalhos.ContainsKey("set-cookie"));
            Assert.False(r.Cabecalhos.ContainsKey("authorization"));
            Assert.False(r.Cabecalhos.ContainsKey("proxy-authorization"));
            Assert.Equal("requisicao-sintetica", Assert.Single(r.Cabecalhos["x-request-id"]));
        }
        Assert.All((await laboratorio.RegistrosAsync()).EnumerateArray(), r => Assert.False(r.GetProperty("cabecalhos").TryGetProperty("cookie", out _)));
    }

    [Fact]
    public async Task ErroDeNegocioMantemMetadadosEMensagemPrivada()
    {
        await laboratorio.ConfigurarAsync(new { modo = "erro", status = 429 });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
        Assert.Equal("recusada", erro.Codigo);
        Assert.Equal(429, erro.Status);
        Assert.Equal("Pedido sintético recusado.", erro.Mensagem);
        Assert.Equal("7", Assert.Single(erro.Cabecalhos["retry-after"]));
        Assert.DoesNotContain("sintético recusado", erro.Message);
        Assert.DoesNotContain("sintético recusado", erro.ToString());
        Assert.Null(erro.InnerException);
        await laboratorio.ConfigurarAsync(new { modo = "erro", status = 502, json = false });
        erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
        Assert.Equal("erro_http", erro.Codigo);
        Assert.DoesNotContain("detalhe-privado", erro.Mensagem);
    }

    [Fact]
    public async Task ModoSemExcecoesRetornaErroMasNaoEngoleCancelamento()
    {
        var c = laboratorio.Configuracao(); c.LancarExcecoes = false;
        using var cliente = new Cliente(c);
        await laboratorio.ConfigurarAsync(new { modo = "erro", status = 409 });
        var resposta = await cliente.Emails.ObterAsync("id");
        Assert.False(resposta.Sucesso); Assert.Equal(409, resposta.Status);
        Assert.Equal("recusada", resposta.Erro!.Codigo);
        using var cancelado = new CancellationTokenSource(); cancelado.Cancel();
        var erro = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cliente.Emails.ObterAsync("id", cancelamento: cancelado.Token));
        Assert.Equal(cancelado.Token, erro.CancellationToken);
    }

    [Fact]
    public async Task LimitesAnunciadosDuranteLeituraEDeCabecalhos()
    {
        await laboratorio.ConfigurarAsync(new { modo = "respostaGrande", tamanho = 16 * 1024 * 1024 + 1 });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
        Assert.Equal("resposta_excedida", erro.Codigo);
        await laboratorio.ConfigurarAsync(new { modo = "fluxo", partes = new[] { "YWJj", "ZGVm" } });
        erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", (_, _) => ValueTask.CompletedTask,
            new() { LimiteRespostaBytes = 5 }));
        Assert.Equal("resposta_excedida", erro.Codigo);
        await laboratorio.ConfigurarAsync(new { modo = "cabecalhosGrandes" });
        await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
    }

    [Theory]
    [InlineData("gzip")] [InlineData("identity, gzip")]
    public async Task CodificacaoNaoNegociadaRecusada(string valor)
    {
        await RespostaAsync(Encoding.UTF8.GetBytes("{}"), new Dictionary<string, object> { ["Content-Encoding"] = valor });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
        Assert.Equal("codificacao_inesperada", erro.Codigo);
    }

    [Fact]
    public async Task JsonInvalidoETruncamentoNaoSaoSucesso()
    {
        foreach (var corpo in new[] { "", "{", "{} {}", "{\"a\":1,\"a\":2}", "{\"x\":\"\\uD800\"}" })
        {
            await RespostaAsync(Encoding.UTF8.GetBytes(corpo));
            var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
            Assert.Equal("resposta_invalida", erro.Codigo);
        }
        await RespostaAsync([255]);
        await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
        foreach (var moldura in new[] { "tamanho", "chunked" })
        {
            await laboratorio.ConfigurarAsync(new { modo = "truncada", moldura, corpo = Convert.ToBase64String("{}"u8) });
            await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id"));
        }
    }

    [Fact]
    public async Task Fluxo8MiBEProgressivoEBlocosPodemSerRetidos()
    {
        await laboratorio.ConfigurarAsync(new { modo = "fluxoGrande", esperarConsumidor = true });
        var partes = new List<ReadOnlyMemory<byte>>();
        var r = await _cliente.Recebimento.BaixarOriginalFluxoAsync("id", async (parte, _) =>
        { partes.Add(parte); if (partes.Count == 1) await laboratorio.ComandoAsync("liberar"); });
        Assert.Empty(r.Dados); Assert.Equal(8 * 1024 * 1024, r.TotalBytes);
        Assert.True(partes.Count > 1);
        Assert.Equal(r.TotalBytes, partes.Sum(x => (long)x.Length));
        Assert.All(partes, x => Assert.All(x.ToArray(), valor => Assert.Equal((byte)'a', valor)));
    }

    [Fact]
    public async Task PrazoIncluiLeituraEEsperaDoConsumidor()
    {
        await laboratorio.ConfigurarAsync(new { modo = "fluxo", partes = Enumerable.Repeat("YWJj", 20).ToArray(), pausa = 0.04 });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", (_, _) => ValueTask.CompletedTask,
            new() { TempoLimite = TimeSpan.FromMilliseconds(150) }));
        Assert.Equal("prazo_excedido", erro.Codigo);
        await laboratorio.ConfigurarAsync(new { modo = "fluxo" });
        erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", async (_, _) => await Task.Delay(500),
            new() { TempoLimite = TimeSpan.FromMilliseconds(100) }));
        Assert.Equal("prazo_excedido", erro.Codigo);
    }

    [Fact]
    public async Task CancelamentoDuranteLeituraPropagaTokenOriginal()
    {
        await laboratorio.ConfigurarAsync(new { modo = "fluxo", partes = Enumerable.Repeat("YWJj", 20).ToArray(), pausa = 0.1 });
        using var cancelado = new CancellationTokenSource();
        var erro = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", (_, _) =>
        { cancelado.Cancel(); return ValueTask.CompletedTask; }, cancelamento: cancelado.Token));
        Assert.Equal(cancelado.Token, erro.CancellationToken);
    }

    [Fact]
    public async Task ExcecaoConsumidorESanitizadaEErroExplicitoPreservado()
    {
        await laboratorio.ConfigurarAsync(new { modo = "fluxo" });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", (_, _) => throw new InvalidOperationException("detalhe-privado")));
        Assert.Equal("erro_consumidor", erro.Codigo); Assert.Null(erro.InnerException);
        Assert.DoesNotContain("detalhe-privado", erro.ToString());
        var esperado = new ErroMailcore("consumidor_interrompeu", "sintetica");
        erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", (_, _) => throw esperado));
        Assert.Same(esperado, erro);
    }

    [Fact]
    public async Task ClienteCompartilhadoReusaPoolEOitoLeiturasConcorrentes()
    {
        await _cliente.Emails.ObterAsync("id"); await _cliente.Emails.ObterAsync("id");
        Assert.Single((await laboratorio.RegistrosAsync()).EnumerateArray().Select(x => x.GetProperty("conexao").GetInt32()).Distinct());
        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(i => _cliente.Emails.ObterAsync("id" + i)));
        Assert.All(respostas, x => Assert.True(x.Sucesso));
    }

    [Fact]
    public async Task ConfiguracaoEObtidaPorCopiaEChavePorFornecedorNaoFazFallback()
    {
        var c = laboratorio.Configuracao(); using var cliente = new Cliente(c);
        c.UrlBase = "https://nao-usar.example.test"; c.CertificadoCa![0] = 0;
        Assert.True((await cliente.Emails.ObterAsync("id")).Sucesso);
        c = laboratorio.Configuracao(); c.ChaveApi = null;
        c.FornecedorChave = _ => ValueTask.FromException<string>(new InvalidOperationException("segredo-privado"));
        using var invalido = new Cliente(c);
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => invalido.Emails.ObterAsync("id"));
        Assert.Equal("configuracao_invalida", erro.Codigo); Assert.Null(erro.InnerException);
        Assert.DoesNotContain("segredo-privado", erro.ToString());
    }

    [Fact]
    public async Task ClienteHttpInjetadoNaoEModificadoNemEncerrado()
    {
        using var http = new HttpClient(new ProvedorSintetico(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }))
        { BaseAddress = new Uri("https://outra.example.test"), Timeout = TimeSpan.FromSeconds(42) };
        http.DefaultRequestHeaders.Add("X-Aplicacao", "original");
        var cliente = new Cliente(http, "sintetica");
        Assert.True((await cliente.Emails.ObterAsync("id")).Sucesso);
        cliente.Dispose();
        Assert.Equal("https://outra.example.test/", http.BaseAddress!.ToString());
        Assert.Equal(TimeSpan.FromSeconds(42), http.Timeout);
        Assert.Null(http.DefaultRequestHeaders.Authorization);
        Assert.True((await http.GetAsync("https://sintetico.example.test")).IsSuccessStatusCode);
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => cliente.Emails.ObterAsync("id"));
        Assert.Equal("cliente_fechado", erro.Codigo);
    }

    [Fact]
    public async Task DisposeInterrompeLeituraEmCurso()
    {
        await laboratorio.ConfigurarAsync(new { modo = "fluxo", partes = Enumerable.Repeat("YWJj", 20).ToArray(), pausa = 0.1 });
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Recebimento.BaixarOriginalFluxoAsync("id", (_, _) =>
        { _cliente.Dispose(); return ValueTask.CompletedTask; }));
        Assert.Equal("cancelado", erro.Codigo);
    }

    [Fact]
    public async Task ProvedorHttpQueFalhaNaoExpoeCausaPrivada()
    {
        using var http = new HttpClient(new ProvedorSintetico(_ => throw new InvalidOperationException("segredo-privado")));
        using var cliente = new Cliente(http, "sintetica");
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => cliente.Emails.ObterAsync("id"));
        Assert.Null(erro.InnerException); Assert.DoesNotContain("segredo-privado", erro.ToString());
    }

    [Fact]
    public async Task ValidacaoLocalRecusaUnicodeFormasNumerosECabecalhos()
    {
        foreach (var corpo in new object?[] { null, Array.Empty<object>(), new { texto = "\uD800" }, new { texto = '\uD800' }, new { texto = new[] { '\uD800' } }, new { numero = double.NaN }, new { numero = double.PositiveInfinity } })
        {
            var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.EnviarAsync(corpo!));
            Assert.Equal("configuracao_invalida", erro.Codigo);
        }
        foreach (var nome in new[] { "Authorization", "HOST", "Cookie", "Content-Length", "", "x\r\ny" })
        {
            var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync("id", new()
            { Cabecalhos = new Dictionary<string, IReadOnlyList<string>> { [nome] = ["sintetico"] } }));
            Assert.Equal("configuracao_invalida", erro.Codigo);
        }
        foreach (var id in new[] { "", ".", "..", "a\nb", "\uD800" }) await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.ObterAsync(id));
        Assert.Empty((await laboratorio.RegistrosAsync()).EnumerateArray());
    }

    [Fact]
    public async Task LimiteJsonConsideraBytesEscapadosAntesDaRede()
    {
        var texto = new string('\\', 26 * 1024 * 1024);
        var erro = await Assert.ThrowsAsync<ErroMailcore>(() => _cliente.Emails.EnviarAsync(new { csv = texto }));
        Assert.Equal("configuracao_invalida", erro.Codigo);
        Assert.Empty((await laboratorio.RegistrosAsync()).EnumerateArray());
    }

    [Fact]
    public async Task InjecaoDependenciasForneceClienteEHandlerConfiguravel()
    {
        var servicos = new ServiceCollection();
        servicos.AddMailcore(c => c.ChaveApi = "sintetica")
            .ConfigurePrimaryHttpMessageHandler(() => new ProvedorSintetico(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") }));
        using var provedor = servicos.BuildServiceProvider();
        using var cliente = provedor.GetRequiredService<ICliente>();
        Assert.True((await cliente.Emails.ObterAsync("id")).Sucesso);
    }

    private Task RespostaAsync(byte[] bytes, object? cabecalhos = null) => laboratorio.ConfigurarAsync(new
    { modo = "resposta", corpo = Convert.ToBase64String(bytes), cabecalhos });

    private sealed class ProvedorSintetico(Func<HttpRequestMessage, HttpResponseMessage> executar) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        { cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(executar(request)); }
    }
}
