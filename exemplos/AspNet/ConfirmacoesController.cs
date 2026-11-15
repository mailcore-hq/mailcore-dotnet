using Microsoft.AspNetCore.Mvc;

namespace Mailcore.Exemplo.AspNet;

/// <summary>Exemplo MVC de envio com o cliente obtido por DI.</summary>
[ApiController]
[Route("exemplos/controlador")]
public sealed class ConfirmacoesController : ControllerBase
{
    private readonly ICliente _cliente;

    /// <summary>Recebe o cliente tipado criado por IHttpClientFactory.</summary>
    public ConfirmacoesController(ICliente cliente) => _cliente = cliente;

    /// <summary>Recebe a intenção preparada pelo fluxo da aplicação.</summary>
    [HttpPost]
    public async Task<IActionResult> EnviarAsync(PedidoConfirmacao pedido, CancellationToken cancelamento)
    {
        try
        {
            var resultado = await Confirmacao.EnviarAsync(pedido, _cliente, cancelamento).ConfigureAwait(false);
            return Accepted(resultado);
        }
        catch (ErroMailcore) { return Problem("O serviço de envio não concluiu a intenção.", statusCode: 502); }
    }
}
