// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de EtiquetasConversaCaixa no contrato público Mailcore.</summary>
public sealed class EtiquetasConversaCaixa
{
    private readonly Transporte _transporte;

    internal EtiquetasConversaCaixa(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação adicionar do contrato público Mailcore.</summary>
    public Task<Resposta> AdicionarAsync(
        string caixaId,
        string conversaId,
        object ids,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/labels",
            new Dictionary<string, object?> { ["ids"] = ids },
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação substituir do contrato público Mailcore.</summary>
    public Task<Resposta> SubstituirAsync(
        string caixaId,
        string conversaId,
        object ids,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Put,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/labels",
            new Dictionary<string, object?> { ["ids"] = ids },
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação remover do contrato público Mailcore.</summary>
    public Task<Resposta> RemoverAsync(
        string caixaId,
        string conversaId,
        string etiquetaId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Delete,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}/labels/{Caminho.Segmento(etiquetaId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
