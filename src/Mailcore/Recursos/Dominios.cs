// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de Dominios no contrato público Mailcore.</summary>
public sealed class Dominios
{
    private readonly Transporte _transporte;

    internal Dominios(Transporte transporte)
    {
        _transporte = transporte;
    }

    /// <summary>Executa a operação criar do contrato público Mailcore.</summary>
    public Task<Resposta> CriarAsync(
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/domains",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação listar do contrato público Mailcore.</summary>
    public Task<Resposta> ListarAsync(
        IReadOnlyDictionary<string, object?>? filtros = null,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => "/domains",
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
            () => $"/domains/{Caminho.Segmento(id)}",
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
        string id,
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Patch,
            () => $"/domains/{Caminho.Segmento(id)}",
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
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Delete,
            () => $"/domains/{Caminho.Segmento(id)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação registros_dns do contrato público Mailcore.</summary>
    public Task<Resposta> RegistrosDnsAsync(
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Get,
            () => $"/domains/{Caminho.Segmento(id)}/dns",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação verificar do contrato público Mailcore.</summary>
    public Task<Resposta> VerificarAsync(
        string id,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => $"/domains/{Caminho.Segmento(id)}/verify",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
