// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de RascunhosCaixa no contrato público Mailcore.</summary>
public sealed class RascunhosCaixa
{
    private readonly Transporte _transporte;

    internal RascunhosCaixa(Transporte transporte)
    {
        _transporte = transporte;
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
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/drafts",
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
        string rascunhoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/drafts/{Caminho.Segmento(rascunhoId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação criar do contrato público Mailcore.</summary>
    public Task<Resposta> CriarAsync(
        string caixaId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/drafts",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Usa um objeto vazio para o corpo opcional da operação.</summary>
    public Task<Resposta> CriarAsync(
        string caixaId,
        CancellationToken cancelamento = default)
    {
        return CriarAsync(caixaId, new Dictionary<string, object?>(), null, cancelamento);
    }

    /// <summary>Executa a operação atualizar do contrato público Mailcore.</summary>
    public Task<Resposta> AtualizarAsync(
        string caixaId,
        string rascunhoId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Patch,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/drafts/{Caminho.Segmento(rascunhoId)}",
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
        string rascunhoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Delete,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/drafts/{Caminho.Segmento(rascunhoId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação enviar do contrato público Mailcore.</summary>
    public Task<Resposta> EnviarAsync(
        string caixaId,
        string rascunhoId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/inboxes/{Caminho.Segmento(caixaId)}/drafts/{Caminho.Segmento(rascunhoId)}/send",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
