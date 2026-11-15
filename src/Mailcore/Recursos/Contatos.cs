// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de Contatos no contrato público Mailcore.</summary>
public sealed class Contatos
{
    private readonly Transporte _transporte;

    /// <summary>Retorna as operações de segmentos.</summary>
    public SegmentosContato Segmentos { get; }

    /// <summary>Retorna as operações de topicos.</summary>
    public TopicosContato Topicos { get; }

    /// <summary>Retorna as operações de importacoes.</summary>
    public ImportacoesContatos Importacoes { get; }

    /// <summary>Retorna as operações de exportacoes.</summary>
    public ExportacoesContatos Exportacoes { get; }

    internal Contatos(Transporte transporte)
    {
        _transporte = transporte;
        Segmentos = new SegmentosContato(transporte);
        Topicos = new TopicosContato(transporte);
        Importacoes = new ImportacoesContatos(transporte);
        Exportacoes = new ExportacoesContatos(transporte);
    }

    /// <summary>Executa a operação criar do contrato público Mailcore.</summary>
    public Task<Resposta> CriarAsync(
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/contacts",
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
            () => "/contacts",
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
            () => $"/contacts/{Caminho.Segmento(id)}",
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
            () => $"/contacts/{Caminho.Segmento(id)}",
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
            () => $"/contacts/{Caminho.Segmento(id)}",
            null,
            FormatoCorpo.Nenhum,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação adicionar_em_lote do contrato público Mailcore.</summary>
    public Task<Resposta> AdicionarEmLoteAsync(
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/contacts/batch",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }

    /// <summary>Executa a operação alterar_em_lote do contrato público Mailcore.</summary>
    public Task<Resposta> AlterarEmLoteAsync(
        object pedido,
        OpcoesRequisicao? opcoes = null,
        CancellationToken cancelamento = default)
    {
        return _transporte.ExecutarAsync(
            HttpMethod.Post,
            () => "/contacts/bulk",
            pedido,
            FormatoCorpo.Objeto,
            null,
            FormatoResposta.Json,
            false,
            opcoes,
            cancelamento);
    }
}
