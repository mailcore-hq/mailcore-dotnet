using System.Net;
using System.Text;
using Xunit;

namespace Mailcore.Tests;

/// <summary>Prova descarte mesmo quando um provedor injetado termina depois do cancelamento.</summary>
public sealed class RecursosTardiosTestes
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescartaRespostaHttpQueChegaAposInterrupcaoAsync(bool cancelarPeloChamador)
    {
        using var handler = new HandlerRespostaTardia();
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var cliente = new Cliente(http, "mc_sintetica", Configuracao());
        using var sinal = new CancellationTokenSource();
        var tarefa = cliente.Emails.ObterAsync("id", cancelamento: sinal.Token);
        await handler.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await ConferirInterrupcao(tarefa, sinal, cancelarPeloChamador);
        var conteudo = new ConteudoMonitorado();
        handler.Futuro.SetResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = conteudo });
        await conteudo.Descartado.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(conteudo.FoiDescartado);

        cliente.Dispose();
        Assert.False(handler.FoiDescartado);
        using var seguinte = await http.GetAsync("https://api.example.test/continua-aberto");
        Assert.Equal(HttpStatusCode.OK, seguinte.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DescartaStreamQueChegaAposInterrupcaoAsync(bool cancelarPeloChamador)
    {
        using var conteudo = new ConteudoStreamTardio();
        using var handler = new HandlerStreamTardio(conteudo);
        using var http = new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
        using var cliente = new Cliente(http, "mc_sintetica", Configuracao());
        using var sinal = new CancellationTokenSource();
        var tarefa = cliente.Emails.ObterAsync("id", cancelamento: sinal.Token);
        await conteudo.Iniciado.Task.WaitAsync(TimeSpan.FromSeconds(3));

        await ConferirInterrupcao(tarefa, sinal, cancelarPeloChamador);
        var stream = new StreamMonitorado();
        conteudo.Futuro.SetResult(stream);
        await stream.Descartado.Task.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.True(stream.FoiDescartado);

        cliente.Dispose();
        Assert.False(handler.FoiDescartado);
        using var seguinte = await http.GetAsync("https://api.example.test/continua-aberto");
        Assert.Equal(HttpStatusCode.OK, seguinte.StatusCode);
    }

    private static Configuracao Configuracao() => new()
    {
        UrlBase = "https://api.example.test/v1",
        TempoLimite = TimeSpan.FromSeconds(1)
    };

    private static async Task ConferirInterrupcao(Task<Resposta> tarefa, CancellationTokenSource sinal,
        bool cancelarPeloChamador)
    {
        if (cancelarPeloChamador)
        {
            sinal.Cancel();
            var cancelado = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => tarefa);
            Assert.Equal(sinal.Token, cancelado.CancellationToken);
        }
        else
        {
            var erro = await Assert.ThrowsAsync<ErroMailcore>(() => tarefa);
            Assert.Equal("prazo_excedido", erro.Codigo);
            Assert.Null(erro.InnerException);
        }
    }

    private sealed class HandlerRespostaTardia : HttpMessageHandler
    {
        private int quantidade;
        public bool FoiDescartado { get; private set; }
        public TaskCompletionSource Iniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<HttpResponseMessage> Futuro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage _, CancellationToken __)
        {
            if (Interlocked.Increment(ref quantidade) == 1)
            {
                Iniciado.TrySetResult();
                return Futuro.Task;
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{}") });
        }

        protected override void Dispose(bool disposing) { FoiDescartado = true; base.Dispose(disposing); }
    }

    private sealed class HandlerStreamTardio(ConteudoStreamTardio conteudo) : HttpMessageHandler
    {
        private int quantidade;
        public bool FoiDescartado { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage _, CancellationToken __)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = Interlocked.Increment(ref quantidade) == 1 ? conteudo : new StringContent("{}")
            });
        protected override void Dispose(bool disposing) { FoiDescartado = true; base.Dispose(disposing); }
    }

    private sealed class ConteudoMonitorado() : ByteArrayContent("{}"u8.ToArray())
    {
        public bool FoiDescartado { get; private set; }
        public TaskCompletionSource Descartado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing)
        {
            FoiDescartado = true; base.Dispose(disposing); Descartado.TrySetResult();
        }
    }

    private sealed class ConteudoStreamTardio : HttpContent
    {
        public TaskCompletionSource Iniciado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource<Stream> Futuro { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override Task<Stream> CreateContentReadStreamAsync() => EsperarStream();
        protected override Task<Stream> CreateContentReadStreamAsync(CancellationToken _) => EsperarStream();
        private Task<Stream> EsperarStream() { Iniciado.TrySetResult(); return Futuro.Task; }
        protected override Task SerializeToStreamAsync(Stream _, TransportContext? __) => Task.CompletedTask;
        protected override bool TryComputeLength(out long tamanho) { tamanho = 0; return false; }
    }

    private sealed class StreamMonitorado() : MemoryStream(Encoding.UTF8.GetBytes("{}"))
    {
        public bool FoiDescartado { get; private set; }
        public TaskCompletionSource Descartado { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing)
        {
            FoiDescartado = true; base.Dispose(disposing); Descartado.TrySetResult();
        }
    }
}
