using System.Net;
using System.Text;
using System.Text.Json;
using FluentEmail.Core;
using FluentEmail.Core.Models;
using Mailcore.FluentEmail;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Mailcore.Tests;

public sealed class FluentEmailTestes
{
    [Fact]
    public async Task RegistraEmissorNaCadeiaFluentEmailComClienteDaFactory()
    {
        using var handler = new Capturador();
        var servicos = new ServiceCollection();
        servicos.AddMailcore(opcoes => opcoes.ChaveApi = "mc_sintetica")
            .ConfigurePrimaryHttpMessageHandler(() => handler);
        servicos.AddFluentEmail("origem@example.test")
            .AddMailcore((_, _) => "intencao-da-aplicacao");
        using var provedor = servicos.BuildServiceProvider();
        var emissor = provedor.GetRequiredService<global::FluentEmail.Core.Interfaces.ISender>();
        Assert.True((await emissor.SendAsync(Mensagem())).Successful);
        Assert.Equal("intencao-da-aplicacao", handler.Chave);
    }

    [Fact]
    public async Task ConverteCamposCompostosETornaIdempotenciaExplicita()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var email = Mensagem();
        email.Data.CcAddresses.Add(new Address("copia@example.test", "Cópia"));
        email.Data.BccAddresses.Add(new Address("oculta@example.test", "Oculta"));
        email.Data.ReplyToAddresses.Add(new Address("resposta@example.test", "Resposta"));
        email.Data.PlaintextAlternativeBody = "Olá 📬";
        email.Data.Priority = Priority.High;
        email.Data.Headers["X-Origem"] = "sintetica";
        email.Data.Tags.Add("origem");
        email.Data.Tags.Add("origem");
        using var entrada = new MemoryStream(new byte[] { 0, 255, 128 });
        email.Data.Attachments.Add(new Attachment { Data = entrada, ContentType = "image/png", Filename = "marca.png", IsInline = true, ContentId = "marca" });
        var emissor = new EmissorMailcore(cliente, mensagem => ReferenceEquals(mensagem, email) ? "intencao-persistida" : throw new InvalidOperationException());

        var resultado = await emissor.SendAsync(email);
        Assert.True(resultado.Successful);
        Assert.Equal("email-sintetico", resultado.MessageId);
        Assert.Equal("intencao-persistida", handler.Chave);
        using var json = JsonDocument.Parse(handler.Corpo!);
        var pedido = json.RootElement;
        Assert.Equal("Equipe <origem@example.test>", pedido.GetProperty("remetente").GetString());
        Assert.Equal("destino@example.test", pedido.GetProperty("destinatarios")[0].GetString());
        Assert.Equal("copia@example.test", pedido.GetProperty("cc")[0].GetString());
        Assert.Equal("oculta@example.test", pedido.GetProperty("cco")[0].GetString());
        Assert.Equal("resposta@example.test", pedido.GetProperty("responder_para").GetString());
        Assert.Equal("Olá 📬", pedido.GetProperty("texto").GetString());
        Assert.Equal("1", pedido.GetProperty("tags").GetProperty("origem").GetString());
        Assert.Equal("1", pedido.GetProperty("cabecalhos").GetProperty("X-Priority").GetString());
        Assert.Equal("sintetica", pedido.GetProperty("cabecalhos").GetProperty("X-Origem").GetString());
        Assert.Equal("marca", pedido.GetProperty("anexos")[0].GetProperty("id_conteudo").GetString());
        Assert.Equal("AP+A", pedido.GetProperty("anexos")[0].GetProperty("conteudo_base64").GetString());
        Assert.Equal(0, entrada.Position);
        Assert.True(entrada.CanRead);
        Assert.Equal(2, email.Data.Tags.Count);
        Assert.False(email.Data.Headers.ContainsKey("Idempotency-Key"));
    }

    [Fact]
    public async Task ReutilizaStreamReposicionavelSemMudarCorpoDaIntencao()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var email = Mensagem();
        using var entrada = new MemoryStream(new byte[] { 9, 1, 2, 3 });
        entrada.Position = 1;
        email.Data.Attachments.Add(new Attachment { Data = entrada, Filename = "dado.bin", ContentType = "application/octet-stream" });
        var emissor = new EmissorMailcore(cliente, _ => "mesma-intencao");
        Assert.True((await emissor.SendAsync(email)).Successful);
        var primeiro = handler.Corpo;
        Assert.True((await emissor.SendAsync(email)).Successful);
        Assert.Equal(primeiro, handler.Corpo);
        Assert.Equal(1, entrada.Position);
        Assert.True(entrada.CanRead);
    }

    [Fact]
    public async Task EnviarComChaveExplicitaNaoInvocaSeletor()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var emissor = new EmissorMailcore(cliente, _ => throw new InvalidOperationException("detalhe-privado"));
        var resultado = await emissor.EnviarAsync(Mensagem(), "chave-explicita");
        Assert.True(resultado.Successful);
        Assert.Equal("chave-explicita", handler.Chave);
    }

    [Fact]
    public void ImplementaContratoSincronoSemCriarEnvioSincronoNoNucleo()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var emissor = new EmissorMailcore(cliente, _ => "contrato-sincrono");
        Assert.True(emissor.Send(Mensagem()).Successful);
        Assert.Equal(1, handler.Quantidade);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ErrosDaApiNaoExibemMensagemPrivada(bool lancar)
    {
        using var handler = new Capturador(HttpStatusCode.TooManyRequests, "{\"erro\":{\"codigo\":\"quota\",\"mensagem\":\"detalhe-privado-sintetico\"}}");
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica", new Configuracao { LancarExcecoes = lancar });
        var emissor = new EmissorMailcore(cliente, _ => "intencao-estavel");
        var resultado = await emissor.SendAsync(Mensagem());
        Assert.False(resultado.Successful);
        Assert.All(resultado.ErrorMessages, mensagem => Assert.DoesNotContain("detalhe-privado", mensagem));
    }

    [Fact]
    public async Task ErroDoSeletorNaoExibeDetalhesPrivadosNemExecutaRede()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var emissor = new EmissorMailcore(cliente, _ => throw new InvalidOperationException("chave-privada-sintetica"));
        var resultado = await emissor.SendAsync(Mensagem());
        Assert.False(resultado.Successful);
        Assert.DoesNotContain("chave-privada", string.Join("", resultado.ErrorMessages));
        Assert.Equal(0, handler.Quantidade);
    }

    [Theory]
    [InlineData("")]
    [InlineData("quebra\nlinha")]
    public async Task ChaveInvalidaNaoGeraOutraAutomaticamente(string chave)
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var emissor = new EmissorMailcore(cliente, _ => chave);
        Assert.False((await emissor.SendAsync(Mensagem())).Successful);
        Assert.Equal(0, handler.Quantidade);
    }

    [Fact]
    public async Task CancelamentoPropagaSemConverterEmFalhaDeNegocio()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var emissor = new EmissorMailcore(cliente, _ => "intencao-estavel");
        using var sinal = new CancellationTokenSource();
        sinal.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => emissor.SendAsync(Mensagem(), sinal.Token));
        Assert.Equal(0, handler.Quantidade);
    }

    [Fact]
    public async Task MaisDeUmEnderecoDeRespostaRecusaAntesDaRede()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var email = Mensagem();
        email.Data.ReplyToAddresses.Add(new Address("um@example.test"));
        email.Data.ReplyToAddresses.Add(new Address("dois@example.test"));
        var emissor = new EmissorMailcore(cliente, _ => "intencao-estavel");
        Assert.False((await emissor.SendAsync(email)).Successful);
        Assert.Equal(0, handler.Quantidade);
    }

    [Fact]
    public async Task LimiteDeAnexosEvitaLeituraIntegralEDeixaStreamAberto()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        using var entrada = new MemoryStream(new byte[5 * 1024 * 1024 + 1]);
        var email = Mensagem();
        email.Data.Attachments.Add(new Attachment { Data = entrada, ContentType = "application/octet-stream", Filename = "grande.bin" });
        var emissor = new EmissorMailcore(cliente, _ => "intencao-estavel");
        Assert.False((await emissor.SendAsync(email)).Successful);
        Assert.True(entrada.CanRead);
        Assert.Equal(0, entrada.Position);
        Assert.Equal(0, handler.Quantidade);
    }

    [Fact]
    public async Task CorpoTextoNaoCriaHtmlArtificial()
    {
        using var handler = new Capturador();
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var email = Mensagem();
        email.Data.IsHtml = false;
        email.Data.Body = "Texto simples";
        var emissor = new EmissorMailcore(cliente, _ => "intencao-estavel");
        Assert.True((await emissor.SendAsync(email)).Successful);
        using var json = JsonDocument.Parse(handler.Corpo!);
        Assert.False(json.RootElement.TryGetProperty("html", out _));
        Assert.Equal("Texto simples", json.RootElement.GetProperty("texto").GetString());
    }

    [Fact]
    public async Task AceiteSemIdentificadorNaoViraSucessoSemConteudo()
    {
        using var handler = new Capturador(HttpStatusCode.Accepted, "{\"id\":null,\"estado\":\"aceita\"}");
        using var http = new HttpClient(handler);
        using var cliente = new Cliente(http, "mc_sintetica");
        var emissor = new EmissorMailcore(cliente, _ => "intencao-estavel");
        Assert.False((await emissor.SendAsync(Mensagem())).Successful);
    }

    private static IFluentEmail Mensagem()
        => new Email("origem@example.test", "Equipe").To("destino@example.test", "Pessoa").Subject("Olá 📬").Body("<b>Olá 📬</b>", true);

    private sealed class Capturador : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _resposta;
        public string? Corpo { get; private set; }
        public string? Chave { get; private set; }
        public int Quantidade { get; private set; }
        public Capturador(HttpStatusCode status = HttpStatusCode.Accepted, string resposta = "{\"id\":\"email-sintetico\",\"estado\":\"aceita\"}")
        { _status = status; _resposta = resposta; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
        {
            cancelamento.ThrowIfCancellationRequested();
            Quantidade++;
            Chave = pedido.Headers.TryGetValues("Idempotency-Key", out var valores) ? valores.Single() : null;
            Corpo = pedido.Content is null ? null : await pedido.Content.ReadAsStringAsync(cancelamento);
            return new HttpResponseMessage(_status) { Content = new StringContent(_resposta, Encoding.UTF8, "application/json") };
        }
    }
}
