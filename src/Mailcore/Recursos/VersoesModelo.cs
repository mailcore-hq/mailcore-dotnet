// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de VersoesModelo no contrato público Mailcore.</summary>
public sealed class VersoesModelo
{
    private readonly Transporte _transporte;

    internal VersoesModelo(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        string modeloId,
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/templates/{Caminho.Segmento(modeloId)}/versions",
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
        string modeloId,
        string versaoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/templates/{Caminho.Segmento(modeloId)}/versions/{Caminho.Segmento(versaoId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação restaurar do contrato público Mailcore.</summary>
    public Task<Resposta> RestaurarAsync(
        string modeloId,
        string versaoId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/templates/{Caminho.Segmento(modeloId)}/versions/{Caminho.Segmento(versaoId)}/restore",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação prever do contrato público Mailcore.</summary>
    public Task<Resposta> PreverAsync(
        string modeloId,
        string versaoId,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/templates/{Caminho.Segmento(modeloId)}/versions/{Caminho.Segmento(versaoId)}/preview",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Usa um objeto vazio para o corpo opcional da operação.</summary>
    public Task<Resposta> PreverAsync(
        string modeloId,
        string versaoId,
        CancellationToken cancelamento = default)
    {
        return PreverAsync(modeloId, versaoId, new Dictionary<string, object?>(), null, cancelamento);
    }
}
