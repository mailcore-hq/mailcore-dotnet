// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de ExecucoesAutomacoes no contrato público Mailcore.</summary>
public sealed class ExecucoesAutomacoes
{
    private readonly Transporte _transporte;

    internal ExecucoesAutomacoes(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        string automacaoId,
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/automations/{Caminho.Segmento(automacaoId)}/runs",
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
        string automacaoId,
        string execucaoId,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/automations/{Caminho.Segmento(automacaoId)}/runs/{Caminho.Segmento(execucaoId)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
