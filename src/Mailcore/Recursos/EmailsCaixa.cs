// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de EmailsCaixa no contrato público Mailcore.</summary>
public sealed class EmailsCaixa
{
    private readonly Transporte _transporte;

    internal EmailsCaixa(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        string caixaId,
        string conversaId,
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/emails",
            null,
            FormatoCorpo.Nenhum,
            filtros,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação obter do contrato público Mailcore.</summary>
    public Task<Resposta> ObterAsync(
        string caixaId,
        string conversaId,
        string emailId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/emails/{Caminho.Segmento(emailId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação responder do contrato público Mailcore.</summary>
    public Task<Resposta> ResponderAsync(
        string caixaId,
        string conversaId,
        string emailId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/emails/{Caminho.Segmento(emailId)}/reply",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação encaminhar do contrato público Mailcore.</summary>
    public Task<Resposta> EncaminharAsync(
        string caixaId,
        string conversaId,
        string emailId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/emails/{Caminho.Segmento(emailId)}/forward",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
