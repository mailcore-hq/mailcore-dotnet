using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Mailcore;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;

namespace Mailcore.Exemplo.AspNet;

/// <summary>Exemplo de integração do SDK em Minimal API e controller ASP.NET.</summary>
public static class Program
{
    /// <summary>Usa --verificar-exemplo para testar os dois fluxos sem serviço externo ou SMTP.</summary>
    public static async Task<int> Main(string[] argumentos)
    {
        var verificar = argumentos.Contains("--verificar-exemplo", StringComparer.Ordinal);
        var opcoes = argumentos.Where(arg => arg != "--verificar-exemplo").ToArray();
        var construtor = WebApplication.CreateBuilder(opcoes);
        construtor.Services.AddControllers();
        var http = construtor.Services.AddMailcore(configuracao =>
        {
            configuracao.ChaveApi = verificar ? "mc_sintetica_exemplo" : Environment.GetEnvironmentVariable("MAILCORE_API_KEY");
        });
        var capturador = verificar ? new TransporteSintetico() : null;
        if (capturador is not null)
        {
            http.ConfigurePrimaryHttpMessageHandler(() => capturador);
            construtor.WebHost.UseUrls("http://127.0.0.1:0");
            construtor.Logging.ClearProviders();
        }

        await using var app = construtor.Build();
        app.MapPost("/exemplos/minimo", async (PedidoConfirmacao pedido, ICliente cliente, CancellationToken cancelamento) =>
        {
            try
            {
                var resultado = await Confirmacao.EnviarAsync(pedido, cliente, cancelamento).ConfigureAwait(false);
                return Results.Accepted(value: resultado);
            }
            catch (ErroMailcore) { return Results.Problem("O serviço de envio não concluiu a intenção.", statusCode: 502); }
        });
        app.MapControllers();
        if (!verificar)
        {
            await app.RunAsync().ConfigureAwait(false);
            return 0;
        }

        try
        {
            await app.StartAsync().ConfigureAwait(false);
            var servidor = app.Services.GetRequiredService<IServer>();
            var endereco = servidor.Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            using var requisicoes = new HttpClient(new SocketsHttpHandler { UseProxy = false, UseCookies = false, AllowAutoRedirect = false });
            var pedido = new PedidoConfirmacao("origem@example.test", "destino@example.test", "confirmacao-sintetica");
            foreach (var rota in new[] { "/exemplos/minimo", "/exemplos/controlador" })
            {
                using var resposta = await requisicoes.PostAsJsonAsync(endereco + rota, pedido).ConfigureAwait(false);
                if (resposta.StatusCode != HttpStatusCode.Accepted) throw new InvalidOperationException();
                using var dados = JsonDocument.Parse(await resposta.Content.ReadAsByteArrayAsync().ConfigureAwait(false));
                if (dados.RootElement.GetProperty("estado").GetString() != "aceita") throw new InvalidOperationException();
            }
            if (capturador!.Quantidade != 2) throw new InvalidOperationException();
            await app.StopAsync().ConfigureAwait(false);
            Console.WriteLine(JsonSerializer.Serialize(new { resultado = "aprovado", cenarios = 2, dotnet = Environment.Version.ToString(), rede_externa = false, smtp = false }));
            return 0;
        }
        catch
        {
            Console.WriteLine("{\"resultado\":\"falhou\",\"falha\":\"O exemplo ASP.NET não satisfez o contrato; detalhes omitidos.\"}");
            return 1;
        }
    }
}

internal sealed class TransporteSintetico : HttpMessageHandler
{
    private int _quantidade;
    internal int Quantidade => _quantidade;
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken cancelamento)
    {
        if (pedido.RequestUri!.AbsolutePath != "/v1/emails" || pedido.Method != HttpMethod.Post ||
            pedido.Headers.Authorization?.Parameter != "mc_sintetica_exemplo" ||
            !pedido.Headers.TryGetValues("Idempotency-Key", out var chaves) || chaves.Single() != "confirmacao-sintetica")
            throw new InvalidOperationException();
        using var json = JsonDocument.Parse(await pedido.Content!.ReadAsByteArrayAsync(cancelamento).ConfigureAwait(false));
        if (json.RootElement.GetProperty("destinatarios")[0].GetString() != "destino@example.test") throw new InvalidOperationException();
        var indice = Interlocked.Increment(ref _quantidade);
        return new HttpResponseMessage(HttpStatusCode.Accepted)
        {
            Content = new StringContent($"{{\"id\":\"email-sintetico-{indice}\",\"estado\":\"aceita\"}}", Encoding.UTF8, "application/json")
        };
    }
}
