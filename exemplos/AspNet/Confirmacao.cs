namespace Mailcore.Exemplo.AspNet;

/// <summary>Dados selecionados pelo fluxo autorizado da aplicação.</summary>
public sealed record PedidoConfirmacao(string Remetente, string Destinatario, string ChaveIdempotencia);

/// <summary>Identificador e estado da intenção aceita.</summary>
public sealed record ConfirmacaoAceita(string Id, string Estado);

/// <summary>Lógica compartilhada pelos exemplos de Minimal API e controller.</summary>
public static class Confirmacao
{
    /// <summary>Não confunde a aceitação HTTP com a entrega da mensagem.</summary>
    public static async Task<ConfirmacaoAceita> EnviarAsync(PedidoConfirmacao pedido, ICliente cliente,
        CancellationToken cancelamento)
    {
        var dados = new Dictionary<string, object?>
        {
            ["remetente"] = pedido.Remetente,
            ["destinatarios"] = new[] { pedido.Destinatario },
            ["assunto"] = "Pedido recebido",
            ["html"] = "<p>Recebemos seu pedido.</p>",
            ["texto"] = "Recebemos seu pedido."
        };
        var resposta = await cliente.Emails.EnviarAsync(dados,
            new OpcoesRequisicao { ChaveIdempotencia = pedido.ChaveIdempotencia }, cancelamento).ConfigureAwait(false);
        var json = resposta.Json();
        return new ConfirmacaoAceita(json.GetProperty("id").GetString()!, json.GetProperty("estado").GetString()!);
    }
}
