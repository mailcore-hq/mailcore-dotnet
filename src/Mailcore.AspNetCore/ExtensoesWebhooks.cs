using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Mailcore.AspNetCore;

/// <summary>Recebe webhooks autenticados sem expor seu corpo em erros ou logs do SDK.</summary>
public static class ExtensoesWebhooks
{
    private const int LimiteCorpo = 8 * 1024 * 1024;

    /// <summary>Mapeia POST, autentica os bytes originais e aguarda o processamento da aplicação.</summary>
    public static RouteHandlerBuilder MapMailcoreWebhook(
        this IEndpointRouteBuilder endpoints,
        string caminho,
        string segredo,
        Func<EventoWebhook, HttpContext, CancellationToken, Task> tratar,
        OpcoesAssinatura? opcoes = null)
    {
        ArgumentNullException.ThrowIfNull(endpoints);
        ArgumentNullException.ThrowIfNull(tratar);
        if (string.IsNullOrWhiteSpace(caminho))
        {
            throw new ArgumentException("Informe um caminho para o webhook.", nameof(caminho));
        }
        if (string.IsNullOrWhiteSpace(segredo))
        {
            throw new ArgumentException("Informe o segredo do webhook.", nameof(segredo));
        }
        if (opcoes is not null && (opcoes.Tolerancia <= TimeSpan.Zero || opcoes.Tolerancia > TimeSpan.FromMinutes(5)))
        {
            throw new ArgumentException("Use tolerância positiva de até cinco minutos.", nameof(opcoes));
        }

        Func<HttpContext, Task> executar = async contexto =>
        {
            var cancelamento = contexto.RequestAborted;
            cancelamento.ThrowIfCancellationRequested();
            var comprimento = contexto.Request.ContentLength;
            if (comprimento is > LimiteCorpo)
            {
                await RecusarAsync(contexto, StatusCodes.Status413PayloadTooLarge, "corpo_excedido").ConfigureAwait(false);
                return;
            }

            EventoWebhook evento;
            using (var corpo = new MemoryStream())
            {
                var buffer = new byte[32768];
                while (true)
                {
                    var tamanho = await contexto.Request.Body.ReadAsync(buffer, cancelamento).ConfigureAwait(false);
                    if (tamanho == 0)
                    {
                        break;
                    }
                    if (tamanho > LimiteCorpo - corpo.Length)
                    {
                        await RecusarAsync(contexto, StatusCodes.Status413PayloadTooLarge, "corpo_excedido").ConfigureAwait(false);
                        return;
                    }
                    corpo.Write(buffer, 0, tamanho);
                }

                cancelamento.ThrowIfCancellationRequested();
                var cabecalhos = contexto.Request.Headers.ToDictionary(
                    campo => campo.Key,
                    campo => (IReadOnlyList<string>)campo.Value.Select(valor => valor ?? "").ToArray(),
                    StringComparer.OrdinalIgnoreCase);
                try
                {
                    evento = Assinatura.Verificar(corpo.GetBuffer().AsMemory(0, (int)corpo.Length), cabecalhos, segredo, opcoes);
                }
                catch (ErroMailcore erro) when (erro.Codigo == "assinatura_invalida")
                {
                    await RecusarAsync(contexto, StatusCodes.Status400BadRequest, "assinatura_invalida").ConfigureAwait(false);
                    return;
                }
            }

            cancelamento.ThrowIfCancellationRequested();
            await tratar(evento, contexto, cancelamento).ConfigureAwait(false);
            contexto.Response.StatusCode = StatusCodes.Status204NoContent;
        };

        return endpoints.MapPost(caminho, executar);
    }

    private static async Task RecusarAsync(HttpContext contexto, int status, string codigo)
    {
        contexto.Response.StatusCode = status;
        contexto.Response.ContentType = "application/json; charset=utf-8";
        await contexto.Response.WriteAsync("{\"erro\":{\"codigo\":\"" + codigo + "\"}}", contexto.RequestAborted).ConfigureAwait(false);
    }
}
