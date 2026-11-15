// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de SegmentosContato no contrato público Mailcore.</summary>
public sealed class SegmentosContato
{
    private readonly Transporte _transporte;

    internal SegmentosContato(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        string contatoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/contacts/{Caminho.Segmento(contatoId)}/segments",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação adicionar do contrato público Mailcore.</summary>
    public Task<Resposta> AdicionarAsync(
        string contatoId,
        string segmentoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/contacts/{Caminho.Segmento(contatoId)}/segments",
            new Dictionary<string, object?> { ["segmento_id"] = segmentoId },
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            true,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação remover do contrato público Mailcore.</summary>
    public Task<Resposta> RemoverAsync(
        string contatoId,
        string segmentoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Delete,
            () => $"/contacts/{Caminho.Segmento(contatoId)}/segments/{Caminho.Segmento(segmentoId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
