// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de Recebimento no contrato público Mailcore.</summary>
public sealed class Recebimento
{
    private readonly Transporte _transporte;

    internal Recebimento(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => "/receiving",
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
        string id,
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/receiving/{Caminho.Segmento(id)}",
            null,
            FormatoCorpo.Nenhum,
            filtros,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação listar_anexos do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAnexosAsync(
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/receiving/{Caminho.Segmento(id)}/attachments",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação obter_anexo do contrato público Mailcore.</summary>
    public Task<Resposta> ObterAnexoAsync(
        string id,
        string anexoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/receiving/{Caminho.Segmento(id)}/attachments/{Caminho.Segmento(anexoId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação baixar_original do contrato público Mailcore.</summary>
    public Task<Resposta> BaixarOriginalAsync(
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/receiving/{Caminho.Segmento(id)}/raw",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Bytes,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação baixar_anexo do contrato público Mailcore.</summary>
    public Task<Resposta> BaixarAnexoAsync(
        string id,
        string anexoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/receiving/{Caminho.Segmento(id)}/attachments/{Caminho.Segmento(anexoId)}/download",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Bytes,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Entrega blocos ao consumidor sem acumular o download na resposta.</summary>
    public Task<Resposta> BaixarOriginalFluxoAsync(
        string id,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consumir,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () =>
            {
                if (consumir is null)
                {
                    throw ErroMailcore.Configuracao("Informe um consumidor para o download.");
                }

                return $"/receiving/{Caminho.Segmento(id)}/raw";
            },
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Bytes,
            false,
            opcoes,
            cancelamento,
            consumir);
    }

    /// <summary>Entrega blocos ao consumidor sem acumular o download na resposta.</summary>
    public Task<Resposta> BaixarAnexoFluxoAsync(
        string id,
        string anexoId,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask> consumir,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () =>
            {
                if (consumir is null)
                {
                    throw ErroMailcore.Configuracao("Informe um consumidor para o download.");
                }

                return $"/receiving/{Caminho.Segmento(id)}/attachments/{Caminho.Segmento(anexoId)}/download";
            },
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Bytes,
            false,
            opcoes,
            cancelamento,
            consumir);
    }
}
