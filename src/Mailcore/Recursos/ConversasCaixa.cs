// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de ConversasCaixa no contrato público Mailcore.</summary>
public sealed class ConversasCaixa
{
    private readonly Transporte _transporte;

    /// <summary>Retorna as operações de emails.</summary>
    public EmailsCaixa Emails { get; }

    /// <summary>Retorna as operações de etiquetas.</summary>
    public EtiquetasConversaCaixa Etiquetas { get; }

    internal ConversasCaixa(Transporte transporte)
    {
        _transporte = transporte;
        Emails = new EmailsCaixa(transporte);
        Etiquetas = new EtiquetasConversaCaixa(transporte);
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        string caixaId,
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads",
            null,
            FormatoCorpo.Nenhum,
            filtros,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação buscar do contrato público Mailcore.</summary>
    public Task<Resposta> BuscarAsync(
        string caixaId,
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/search",
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
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação atualizar do contrato público Mailcore.</summary>
    public Task<Resposta> AtualizarAsync(
        string caixaId,
        string conversaId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Patch,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}",
            pedido,
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
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Delete,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/threads/{Caminho.Segmento(conversaId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
