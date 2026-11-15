using System.Net;
using System.Reflection;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using global::FluentEmail.Core;
using Mailcore;
using Mailcore.FluentEmail;

namespace Mailcore.ConsumidorFluentEmail;

internal static class Programa
{
    private sealed class TransporteSintetico : HttpMessageHandler
    {
        public int Requisicoes { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage pedido, CancellationToken token)
        {
            token.ThrowIfCancellationRequested(); Requisicoes++;
            if (pedido.Headers.Authorization?.ToString() != "Bearer mc_sintetica" ||
                !pedido.Headers.GetValues("Idempotency-Key").SequenceEqual(["intencao-fluentemail"]))
                throw new InvalidOperationException("O emissor não conservou o contrato.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Accepted)
            {
                Content = new StringContent("{\"id\":\"sintetico\",\"estado\":\"aceita\"}", Encoding.UTF8, "application/json")
            });
        }
    }

    public static async Task<int> Main()
    {
        try
        {
            var pasta = Path.GetFullPath(Environment.GetEnvironmentVariable("MAILCORE_CONSUMIDOR") ?? throw new InvalidOperationException());
            var local = Path.GetFullPath(typeof(EmissorMailcore).Assembly.Location);
            if (!local.StartsWith(pasta + Path.DirectorySeparatorChar, StringComparison.Ordinal) || local.StartsWith("/sdk/", StringComparison.Ordinal))
                throw new InvalidOperationException();
            using var transporte = new TransporteSintetico();
            using var http = new HttpClient(transporte);
            using var cliente = new Cliente(http, "mc_sintetica");
            var emissor = new EmissorMailcore(cliente, _ => "intencao-fluentemail");
            var email = new Email("origem@example.test", "Equipe").To("destino@example.test", "Pessoa")
                .Subject("Olá 📬").Body("<b>Dados sintéticos</b>", true);
            var resultado = await emissor.SendAsync(email);
            if (!resultado.Successful || resultado.MessageId != "sintetico" || transporte.Requisicoes != 1)
                throw new InvalidOperationException();
            var framework = typeof(EmissorMailcore).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                resultado = "aprovado", dotnet = Environment.Version.ToString(),
                framework_biblioteca = "net" + framework[".NETCoreApp,Version=v".Length..],
                origem_dll_fora_checkout = true, requisicoes_http = transporte.Requisicoes, rede_real = false
            }));
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine("{\"resultado\":\"falhou\",\"falha\":\"Contrato do pacote opcional não satisfeito; detalhes privados omitidos.\"}");
            return 1;
        }
    }
}
