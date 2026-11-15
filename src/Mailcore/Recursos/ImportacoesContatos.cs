// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de ImportacoesContatos no contrato público Mailcore.</summary>
public sealed class ImportacoesContatos
{
    private readonly Transporte _transporte;

    internal ImportacoesContatos(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação prever do contrato público Mailcore.</summary>
    public Task<Resposta> PreverAsync(
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/contacts/imports/preview",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação criar do contrato público Mailcore.</summary>
    public Task<Resposta> CriarAsync(
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/contacts/imports",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação obter do contrato público Mailcore.</summary>
    public Task<Resposta> ObterAsync(
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/contacts/imports/{Caminho.Segmento(id)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
