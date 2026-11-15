// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de ExportacoesContatos no contrato público Mailcore.</summary>
public sealed class ExportacoesContatos
{
    private readonly Transporte _transporte;

    internal ExportacoesContatos(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação criar do contrato público Mailcore.</summary>
    public Task<Resposta> CriarAsync(
        object filtros,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/contacts/exports",
            filtros,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Usa um objeto vazio para o corpo opcional da operação.</summary>
    public Task<Resposta> CriarAsync(
        CancellationToken cancelamento = default)
    {
        return CriarAsync(new Dictionary<string, object?>(), null, cancelamento);
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => "/contacts/exports",
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
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/contacts/exports/{Caminho.Segmento(id)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação baixar do contrato público Mailcore.</summary>
    public Task<Resposta> BaixarAsync(
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/contacts/exports/{Caminho.Segmento(id)}/download",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Bytes,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Entrega blocos ao consumidor sem acumular o download na resposta.</summary>
    public Task<Resposta> BaixarFluxoAsync(
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

                return $"/contacts/exports/{Caminho.Segmento(id)}/download";
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
