// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de Lotes no contrato público Mailcore.</summary>
public sealed class Lotes
{
    private readonly Transporte _transporte;

    internal Lotes(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação enviar do contrato público Mailcore.</summary>
    public Task<Resposta> EnviarAsync(
        object dados,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/emails/batch",
            dados,
            FormatoCorpo.Lista,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação obter_por_idempotencia do contrato público Mailcore.</summary>
    public Task<Resposta> ObterPorIdempotenciaAsync(
        string chave,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/emails/batch/idempotency/{Caminho.Segmento(chave)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
