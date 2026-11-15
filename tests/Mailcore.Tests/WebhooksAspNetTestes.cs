using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using Mailcore.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Mailcore.Tests;

/// <summary>Pipeline Kestrel real em loopback, sem TestHost, SMTP ou portas publicadas.</summary>
public sealed class WebhooksAspNetTestes
{
    private static readonly DateTimeOffset Agora = DateTimeOffset.Parse("2026-11-14T00:00:00Z", CultureInfo.InvariantCulture);
    private static readonly byte[] Chave = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
    private static readonly string Segredo = "whsec_" + Convert.ToBase64String(Chave);
    private static readonly byte[] Corpo = Encoding.UTF8.GetBytes("{\"type\":\"email.received\",\"created_at\":\"2026-11-14T00:00:00Z\",\"data\":{\"email_id\":\"mensagem-sintetica\",\"texto\":\"Olá 📬\",\"ativo\":false,\"nulo\":null}}");

    [Fact]
    public async Task AssinaturaValidaInvocaAplicacaoComEventoContextoETokenAsync()
    {
        var observado = new TaskCompletionSource<(EventoWebhook Evento, string Caminho, bool TokenCorreto)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var servidor = await Servidor.IniciarAsync((evento, contexto, cancelamento) =>
        {
            contexto.Response.Headers["X-Aplicacao"] = "sintetica";
            observado.TrySetResult((evento, contexto.Request.Path.Value ?? "", cancelamento == contexto.RequestAborted));
            return Task.CompletedTask;
        });
        using var pedido = PedidoAssinado(Corpo);
        using var resposta = await servidor.Http.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal("", await resposta.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "sintetica" }, resposta.Headers.GetValues("X-Aplicacao"));
        var resultado = await observado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal("email.received", resultado.Evento.Tipo);
        Assert.Equal("Olá 📬", resultado.Evento.Dados.GetProperty("texto").GetString());
        Assert.False(resultado.Evento.Dados.GetProperty("ativo").GetBoolean());
        Assert.Equal("/receber", resultado.Caminho);
        Assert.True(resultado.TokenCorreto);
    }

    [Fact]
    public async Task CorpoAlteradoRecebeErroGenericoSemProcessamentoAsync()
    {
        var chamadas = 0;
        await using var servidor = await Servidor.IniciarAsync((_, _, _) =>
        {
            Interlocked.Increment(ref chamadas);
            return Task.CompletedTask;
        });
        using var pedido = PedidoAssinado(Corpo);
        pedido.Content = new ByteArrayContent([.. Corpo, (byte)' ']);
        using var resposta = await servidor.Http.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        var texto = await resposta.Content.ReadAsStringAsync();
        Assert.Equal("{\"erro\":{\"codigo\":\"assinatura_invalida\"}}", texto);
        Assert.DoesNotContain(Segredo, texto, StringComparison.Ordinal);
        Assert.DoesNotContain("mensagem-sintetica", texto, StringComparison.Ordinal);
        Assert.DoesNotContain("Olá", texto, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref chamadas));
    }

    [Fact]
    public async Task CabecalhosAusentesDuplicadosEInvalidosNaoChegamAoCallbackAsync()
    {
        var chamadas = 0;
        await using var servidor = await Servidor.IniciarAsync((_, _, _) =>
        {
            Interlocked.Increment(ref chamadas);
            return Task.CompletedTask;
        });
        foreach (var nome in new[] { "svix-id", "svix-timestamp", "svix-signature" })
        {
            using var pedido = PedidoAssinado(Corpo);
            pedido.Headers.Remove(nome);
            using var resposta = await servidor.Http.SendAsync(pedido);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }
        foreach (var nome in new[] { "svix-id", "svix-timestamp", "svix-signature" })
        {
            var linha = await EnviarCabecalhoDuplicadoAsync(servidor, nome);
            Assert.StartsWith("HTTP/1.1 400", linha, StringComparison.Ordinal);
        }
        using (var pedido = PedidoAssinado(Corpo))
        {
            pedido.Headers.Remove("svix-signature");
            Assert.True(pedido.Headers.TryAddWithoutValidation("svix-signature", "v2,invalida"));
            using var resposta = await servidor.Http.SendAsync(pedido);
            Assert.Equal(HttpStatusCode.BadRequest, resposta.StatusCode);
        }
        Assert.Equal(0, Volatile.Read(ref chamadas));
    }

    [Fact]
    public async Task CorpoDeclaradoAcimaDoLimiteERecusadoAntesDaLeituraAsync()
    {
        var chamadas = 0;
        await using var servidor = await Servidor.IniciarAsync((_, _, _) =>
        {
            Interlocked.Increment(ref chamadas);
            return Task.CompletedTask;
        });
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var conexao = new TcpClient();
        await conexao.ConnectAsync(IPAddress.Loopback, servidor.Endereco.Port, cancelamento.Token);
        await using var rede = conexao.GetStream();
        var cabecalhos = Encoding.ASCII.GetBytes("POST /receber HTTP/1.1\r\nHost: 127.0.0.1\r\nContent-Length: 8388609\r\nConnection: close\r\n\r\n");
        await rede.WriteAsync(cabecalhos, cancelamento.Token);
        var linha = await LerLinhaAsync(rede, cancelamento.Token);
        Assert.StartsWith("HTTP/1.1 413", linha, StringComparison.Ordinal);
        Assert.Equal(0, Volatile.Read(ref chamadas));
    }

    [Fact]
    public async Task CorpoChunkedSemComprimentoContinuaLimitadoAOitoMiBAsync()
    {
        var chamadas = 0;
        await using var servidor = await Servidor.IniciarAsync((_, _, _) =>
        {
            Interlocked.Increment(ref chamadas);
            return Task.CompletedTask;
        });
        var corpo = CorpoComTamanho(8 * 1024 * 1024 + 1);
        using var pedido = PedidoAssinado(corpo);
        pedido.Content = new ConteudoEmPartes(corpo);
        using var resposta = await servidor.Http.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, resposta.StatusCode);
        Assert.Equal("{\"erro\":{\"codigo\":\"corpo_excedido\"}}", await resposta.Content.ReadAsStringAsync());
        Assert.Equal(0, Volatile.Read(ref chamadas));
    }

    [Fact]
    public async Task CorpoComOitoMiBExatosEProcessadoAsync()
    {
        var recebido = new TaskCompletionSource<EventoWebhook>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var servidor = await Servidor.IniciarAsync((evento, _, _) =>
        {
            recebido.TrySetResult(evento);
            return Task.CompletedTask;
        });
        var corpo = CorpoComTamanho(8 * 1024 * 1024);
        using var pedido = PedidoAssinado(corpo);
        using var resposta = await servidor.Http.SendAsync(pedido);
        Assert.Equal(HttpStatusCode.NoContent, resposta.StatusCode);
        Assert.Equal("email.received", (await recebido.Task.WaitAsync(TimeSpan.FromSeconds(5))).Tipo);
    }

    [Fact]
    public async Task RequestAbortedCancelaLeituraEProcessamentoNativosAsync()
    {
        var iniciouLeitura = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var leituraCancelada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var leituraEncerrada = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var chamadas = 0;
        await using (var servidor = await Servidor.IniciarAsync((_, _, _) =>
        {
            Interlocked.Increment(ref chamadas);
            return Task.CompletedTask;
        }, app => app.Use(async (HttpContext contexto, RequestDelegate proximo) =>
        {
            iniciouLeitura.TrySetResult(contexto.RequestAborted);
            try { await proximo(contexto); }
            finally { leituraEncerrada.TrySetResult(); }
        })))
        {
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            using var conexao = new TcpClient();
            await conexao.ConnectAsync(IPAddress.Loopback, servidor.Endereco.Port, prazo.Token);
            await using var rede = conexao.GetStream();
            var parcial = Encoding.ASCII.GetBytes("POST /receber HTTP/1.1\r\nHost: 127.0.0.1\r\nTransfer-Encoding: chunked\r\n\r\n1\r\n{\r\n");
            await rede.WriteAsync(parcial, prazo.Token);
            await rede.FlushAsync(prazo.Token);
            var tokenServidor = await iniciouLeitura.Task.WaitAsync(TimeSpan.FromSeconds(5));
            using var registro = tokenServidor.Register(() => leituraCancelada.TrySetResult());
            conexao.Client.LingerState = new LingerOption(true, 0);
            conexao.Dispose();
            await leituraCancelada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await leituraEncerrada.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, Volatile.Read(ref chamadas));
        }

        var iniciouProcessamento = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var processamentoCancelado = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var servidor = await Servidor.IniciarAsync(async (_, contexto, token) =>
        {
            Assert.Equal(contexto.RequestAborted, token);
            iniciouProcessamento.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                processamentoCancelado.TrySetResult();
                throw;
            }
        }))
        {
            using var cancelamento = new CancellationTokenSource();
            using var pedido = PedidoAssinado(Corpo);
            var envio = servidor.Http.SendAsync(pedido, cancelamento.Token);
            await iniciouProcessamento.Task.WaitAsync(TimeSpan.FromSeconds(5));
            cancelamento.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => envio);
            await processamentoCancelado.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
    }

    [Fact]
    public async Task ValidaArgumentosEFalhasDaAplicacaoContinuamNaPipelineAsync()
    {
        await using (var app = CriarAplicacao())
        {
            Func<EventoWebhook, HttpContext, CancellationToken, Task> tratar = (_, _, _) => Task.CompletedTask;
            Assert.Throws<ArgumentNullException>(() => ExtensoesWebhooks.MapMailcoreWebhook(null!, "/receber", Segredo, tratar));
            Assert.Throws<ArgumentException>(() => app.MapMailcoreWebhook("", Segredo, tratar));
            Assert.Throws<ArgumentException>(() => app.MapMailcoreWebhook("/receber", "", tratar));
            Assert.Throws<ArgumentNullException>(() => app.MapMailcoreWebhook("/receber", Segredo, null!));
            Assert.Throws<ArgumentException>(() => app.MapMailcoreWebhook("/receber", Segredo, tratar, new OpcoesAssinatura { Tolerancia = TimeSpan.Zero }));
        }
        var esperado = new ErroMailcore("assinatura_invalida", "detalhe-privado-aplicacao");
        var capturado = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var servidor = await Servidor.IniciarAsync((_, _, _) => throw esperado, app =>
            app.Use(async (HttpContext contexto, RequestDelegate proximo) =>
            {
                try { await proximo(contexto); }
                catch (Exception erro)
                {
                    capturado.TrySetResult(erro);
                    contexto.Response.StatusCode = 598;
                }
            }));
        using var pedido = PedidoAssinado(Corpo);
        using var resposta = await servidor.Http.SendAsync(pedido);
        Assert.Equal(598, (int)resposta.StatusCode);
        Assert.Same(esperado, await capturado.Task.WaitAsync(TimeSpan.FromSeconds(5)));
        Assert.DoesNotContain("detalhe-privado-aplicacao", await resposta.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    private static HttpRequestMessage PedidoAssinado(byte[] corpo)
    {
        const string id = "msg_sintetico_endpoint";
        var instante = Agora.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var bytes = Encoding.ASCII.GetBytes(id + "." + instante + ".").Concat(corpo).ToArray();
        var assinatura = "v1," + Convert.ToBase64String(HMACSHA256.HashData(Chave, bytes));
        var pedido = new HttpRequestMessage(HttpMethod.Post, "/receber") { Content = new ByteArrayContent(corpo) };
        pedido.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        pedido.Headers.Add("svix-id", id);
        pedido.Headers.Add("svix-timestamp", instante);
        pedido.Headers.Add("svix-signature", assinatura);
        return pedido;
    }

    private static byte[] CorpoComTamanho(int tamanho)
    {
        var prefixo = Encoding.ASCII.GetBytes("{\"type\":\"email.received\",\"created_at\":\"2026-11-14T00:00:00Z\",\"data\":{\"texto\":\"");
        var sufixo = Encoding.ASCII.GetBytes("\"}}");
        var corpo = new byte[tamanho];
        Array.Fill(corpo, (byte)'a');
        prefixo.CopyTo(corpo, 0);
        sufixo.CopyTo(corpo, corpo.Length - sufixo.Length);
        return corpo;
    }

    private static async Task<string> EnviarCabecalhoDuplicadoAsync(Servidor servidor, string nome)
    {
        using var pedido = PedidoAssinado(Corpo);
        using var cancelamento = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        using var conexao = new TcpClient();
        await conexao.ConnectAsync(IPAddress.Loopback, servidor.Endereco.Port, cancelamento.Token);
        await using var rede = conexao.GetStream();
        var cabecalhos = new StringBuilder("POST /receber HTTP/1.1\r\nHost: 127.0.0.1\r\nConnection: close\r\nContent-Length: ")
            .Append(Corpo.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        foreach (var campo in pedido.Headers)
        {
            var valor = campo.Value.Single();
            cabecalhos.Append(campo.Key).Append(": ").Append(valor).Append("\r\n");
            if (campo.Key.Equals(nome, StringComparison.OrdinalIgnoreCase))
            {
                // Linhas físicas distintas, sem a combinação com vírgula feita pelo HttpClient.
                cabecalhos.Append(campo.Key.ToUpperInvariant()).Append(": ").Append(valor).Append("\r\n");
            }
        }
        cabecalhos.Append("\r\n");
        await rede.WriteAsync(Encoding.ASCII.GetBytes(cabecalhos.ToString()), cancelamento.Token);
        await rede.WriteAsync(Corpo, cancelamento.Token);
        return await LerLinhaAsync(rede, cancelamento.Token);
    }

    private static WebApplication CriarAplicacao()
    {
        var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { EnvironmentName = Environments.Production });
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(opcoes => opcoes.Listen(IPAddress.Loopback, 0));
        return builder.Build();
    }

    private static async Task<string> LerLinhaAsync(Stream rede, CancellationToken token)
    {
        using var acumulado = new MemoryStream();
        var um = new byte[1];
        while (acumulado.Length < 4096)
        {
            await rede.ReadExactlyAsync(um, token);
            if (um[0] == 10)
            {
                return Encoding.ASCII.GetString(acumulado.ToArray()).TrimEnd('\r');
            }
            acumulado.WriteByte(um[0]);
        }
        throw new InvalidDataException("Resposta do laboratório excedida.");
    }

    private sealed class Servidor : IAsyncDisposable
    {
        private readonly WebApplication _app;
        internal Uri Endereco { get; }
        internal HttpClient Http { get; }

        private Servidor(WebApplication app, Uri endereco)
        {
            _app = app;
            Endereco = endereco;
            Http = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false })
            {
                BaseAddress = endereco,
                Timeout = TimeSpan.FromSeconds(10)
            };
        }

        internal static async Task<Servidor> IniciarAsync(
            Func<EventoWebhook, HttpContext, CancellationToken, Task> tratar,
            Action<WebApplication>? configurar = null)
        {
            var app = CriarAplicacao();
            configurar?.Invoke(app);
            app.MapMailcoreWebhook("/receber", Segredo, tratar, new OpcoesAssinatura { Agora = Agora });
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            try
            {
                await app.StartAsync(prazo.Token);
                var enderecos = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!;
                return new Servidor(app, new Uri(enderecos.Addresses.Single()));
            }
            catch
            {
                await app.DisposeAsync();
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            Http.Dispose();
            using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try { await _app.StopAsync(prazo.Token); }
            finally { await _app.DisposeAsync(); }
        }
    }

    private sealed class ConteudoEmPartes(byte[] corpo) : HttpContent
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => GravarAsync(stream, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => GravarAsync(stream, cancellationToken);
        private async Task GravarAsync(Stream stream, CancellationToken token)
        {
            for (var inicio = 0; inicio < corpo.Length; inicio += 16384)
            {
                await stream.WriteAsync(corpo.AsMemory(inicio, Math.Min(16384, corpo.Length - inicio)), token);
            }
        }
    }

}
