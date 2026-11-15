using System.Globalization;
using System.Reflection;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Mailcore.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Logging;

namespace Mailcore.ConsumidorAspNetCore;

internal static class Programa
{
    public static async Task<int> Main()
    {
        try
        {
            var pasta = Path.GetFullPath(Environment.GetEnvironmentVariable("MAILCORE_CONSUMIDOR") ?? throw new InvalidOperationException());
            var local = Path.GetFullPath(typeof(ExtensoesWebhooks).Assembly.Location);
            if (!local.StartsWith(pasta + Path.DirectorySeparatorChar, StringComparison.Ordinal) || local.StartsWith("/sdk/", StringComparison.Ordinal))
                throw new InvalidOperationException();
            var corpo = Encoding.UTF8.GetBytes("{\"type\":\"email.received\",\"created_at\":\"2026-11-14T00:00:00Z\",\"data\":{\"ativo\":false,\"nulo\":null}}");
            var chave = Encoding.ASCII.GetBytes("0123456789abcdef0123456789abcdef");
            var instante = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            var assinatura = Convert.ToBase64String(HMACSHA256.HashData(chave,
                Encoding.ASCII.GetBytes("msg_sintetico." + instante + ".").Concat(corpo).ToArray()));
            var construtor = WebApplication.CreateBuilder();
            construtor.Logging.ClearProviders();
            await using var app = construtor.Build();
            var processados = 0;
            app.MapMailcoreWebhook("/webhook-sintetico", "whsec_" + Convert.ToBase64String(chave), (evento, _, token) =>
            {
                token.ThrowIfCancellationRequested();
                if (evento.Tipo != "email.received") throw new InvalidOperationException();
                processados++;
                return Task.CompletedTask;
            });
            var endpoint = ((IEndpointRouteBuilder)app).DataSources.SelectMany(fonte => fonte.Endpoints)
                .OfType<RouteEndpoint>().Single(item => item.RoutePattern.RawText == "/webhook-sintetico");
            foreach (var adulterar in new[] { false, true })
            {
                using var entrada = new MemoryStream(adulterar ? corpo.Concat(new byte[] { 32 }).ToArray() : corpo);
                using var saida = new MemoryStream();
                var contexto = new DefaultHttpContext { RequestServices = app.Services };
                contexto.Request.Body = entrada; contexto.Request.ContentLength = entrada.Length;
                contexto.Request.Method = "POST"; contexto.Request.Path = "/webhook-sintetico";
                contexto.Request.Headers["svix-id"] = "msg_sintetico";
                contexto.Request.Headers["svix-timestamp"] = instante;
                contexto.Request.Headers["svix-signature"] = "v1," + assinatura;
                contexto.Response.Body = saida;
                await endpoint.RequestDelegate!(contexto);
                if (contexto.Response.StatusCode != (adulterar ? 400 : 204)) throw new InvalidOperationException();
            }
            if (processados != 1) throw new InvalidOperationException();
            var framework = typeof(ExtensoesWebhooks).Assembly.GetCustomAttribute<TargetFrameworkAttribute>()!.FrameworkName;
            Console.WriteLine(JsonSerializer.Serialize(new
            {
                resultado = "aprovado", dotnet = Environment.Version.ToString(),
                framework_biblioteca = "net" + framework[".NETCoreApp,Version=v".Length..],
                origem_dll_fora_checkout = true, webhook_valido = true, corpo_adulterado_recusado = true,
                servidor_iniciado = false, rede_real = false
            }));
            return 0;
        }
        catch (Exception)
        {
            Console.WriteLine("{\"resultado\":\"falhou\",\"falha\":\"Contrato do pacote ASP.NET Core não satisfeito; detalhes privados omitidos.\"}");
            return 1;
        }
    }
}
