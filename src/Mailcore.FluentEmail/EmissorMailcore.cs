using FluentEmail.Core;
using FluentEmail.Core.Interfaces;
using FluentEmail.Core.Models;

namespace Mailcore.FluentEmail;

/// <summary>Emissor FluentEmail que usa o cliente Mailcore e a chave persistida de cada intenção.</summary>
public sealed class EmissorMailcore : ISender
{
    private const int MaximoAnexos = 10;
    private const int MaximoBytesAnexos = 5 * 1024 * 1024;
    private readonly ICliente _cliente;
    private readonly Func<IFluentEmail, string> _obterChaveIdempotencia;

    /// <summary>O seletor deve recuperar uma chave estável, previamente persistida para a mensagem.</summary>
    public EmissorMailcore(ICliente cliente, Func<IFluentEmail, string> obterChaveIdempotencia)
    {
        ArgumentNullException.ThrowIfNull(cliente);
        ArgumentNullException.ThrowIfNull(obterChaveIdempotencia);
        _cliente = cliente;
        _obterChaveIdempotencia = obterChaveIdempotencia;
    }

    /// <summary>Implementação síncrona exigida pelo contrato ISender; prefira SendAsync.</summary>
    public SendResponse Send(IFluentEmail email, CancellationToken? token = null)
        => SendAsync(email, token).ConfigureAwait(false).GetAwaiter().GetResult();

    /// <summary>Envia a mensagem usando a chave retornada pelo seletor da aplicação.</summary>
    public async Task<SendResponse> SendAsync(IFluentEmail email, CancellationToken? token = null)
    {
        var cancelamento = token ?? CancellationToken.None;
        cancelamento.ThrowIfCancellationRequested();
        try
        {
            ArgumentNullException.ThrowIfNull(email);
            var chave = _obterChaveIdempotencia(email);
            return await EnviarAsync(email, chave, cancelamento).ConfigureAwait(false);
        }
        catch (OperationCanceledException) { throw; }
        catch { return Falha("Não foi possível obter a chave persistida da intenção."); }
    }

    /// <summary>Envia com uma chave explícita e estável, sem gerar identificadores automaticamente.</summary>
    public async Task<SendResponse> EnviarAsync(IFluentEmail email, string chaveIdempotencia,
        CancellationToken cancelamento = default)
    {
        cancelamento.ThrowIfCancellationRequested();
        try
        {
            ArgumentNullException.ThrowIfNull(email);
            if (string.IsNullOrEmpty(chaveIdempotencia) || chaveIdempotencia.Length > 256 ||
                chaveIdempotencia.Any(c => c < 33 || c > 126))
                return Falha("Informe uma chave de idempotência estável e válida para a intenção.");

            var pedido = await PrepararAsync(email, cancelamento).ConfigureAwait(false);
            var resposta = await _cliente.Emails.EnviarAsync(pedido,
                new OpcoesRequisicao { ChaveIdempotencia = chaveIdempotencia }, cancelamento)
                .ConfigureAwait(false);
            if (!resposta.Sucesso)
                return Falha("A API Mailcore recusou a intenção de envio.");

            var json = resposta.Json();
            if (!json.TryGetProperty("id", out var identificador) ||
                identificador.ValueKind != System.Text.Json.JsonValueKind.String ||
                string.IsNullOrEmpty(identificador.GetString()))
                return Falha("A API não forneceu o identificador da intenção aceita.");
            return new SendResponse { MessageId = identificador.GetString()! };
        }
        catch (OperationCanceledException) { throw; }
        catch (ErroMailcore) { return Falha("A API Mailcore não concluiu a intenção de envio."); }
        catch { return Falha("Não foi possível preparar ou enviar a mensagem pelo Mailcore."); }
    }

    private static async Task<Dictionary<string, object?>> PrepararAsync(IFluentEmail email,
        CancellationToken cancelamento)
    {
        var dados = email.Data ?? throw new ArgumentException("Mensagem ausente.");
        var pedido = new Dictionary<string, object?>
        {
            ["remetente"] = Remetente(dados.FromAddress),
            ["destinatarios"] = dados.ToAddresses.Select(Endereco).ToArray(),
            ["assunto"] = dados.Subject
        };

        if (dados.IsHtml)
        {
            pedido["html"] = dados.Body;
            if (!string.IsNullOrEmpty(dados.PlaintextAlternativeBody))
                pedido["texto"] = dados.PlaintextAlternativeBody;
        }
        else { pedido["texto"] = dados.Body; }

        if (dados.CcAddresses.Count != 0) pedido["cc"] = dados.CcAddresses.Select(Endereco).ToArray();
        if (dados.BccAddresses.Count != 0) pedido["cco"] = dados.BccAddresses.Select(Endereco).ToArray();
        if (dados.ReplyToAddresses.Count > 1) throw new ArgumentException("Use um endereço de resposta.");
        if (dados.ReplyToAddresses.Count == 1) pedido["responder_para"] = Endereco(dados.ReplyToAddresses[0]);

        var cabecalhos = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (dados.Priority == Priority.High)
        {
            cabecalhos["X-Priority"] = "1";
            cabecalhos["X-MSMail-Priority"] = "High";
        }
        else if (dados.Priority == Priority.Low)
        {
            cabecalhos["X-Priority"] = "5";
            cabecalhos["X-MSMail-Priority"] = "Low";
        }
        foreach (var campo in dados.Headers) cabecalhos[campo.Key] = campo.Value;
        if (cabecalhos.Count != 0) pedido["cabecalhos"] = cabecalhos;
        if (dados.Tags.Count != 0)
            pedido["tags"] = dados.Tags.Distinct(StringComparer.Ordinal)
                .ToDictionary(tag => tag, _ => "1", StringComparer.Ordinal);

        if (dados.Attachments.Count > MaximoAnexos) throw new ArgumentException("Quantidade de anexos excedida.");
        if (dados.Attachments.Count != 0)
        {
            var anexos = new List<Dictionary<string, object?>>();
            var total = 0;
            foreach (var anexo in dados.Attachments)
            {
                cancelamento.ThrowIfCancellationRequested();
                var bytes = await LerAnexoAsync(anexo.Data, MaximoBytesAnexos - total, cancelamento)
                    .ConfigureAwait(false);
                total += bytes.Length;
                var item = new Dictionary<string, object?>
                {
                    ["nome"] = anexo.Filename,
                    ["tipo_conteudo"] = anexo.ContentType,
                    ["conteudo_base64"] = Convert.ToBase64String(bytes)
                };
                if (anexo.IsInline)
                {
                    if (string.IsNullOrEmpty(anexo.ContentId)) throw new ArgumentException("Informe o CID do anexo inline.");
                    item["id_conteudo"] = anexo.ContentId;
                }
                anexos.Add(item);
            }
            pedido["anexos"] = anexos;
        }
        return pedido;
    }

    private static string Endereco(Address endereco)
    {
        ArgumentNullException.ThrowIfNull(endereco);
        var valor = endereco.EmailAddress;
        if (string.IsNullOrEmpty(valor) || valor.Any(c => char.IsControl(c) || c is '<' or '>'))
            throw new ArgumentException("Endereço inválido.");
        return valor;
    }

    private static string Remetente(Address endereco)
    {
        var valor = Endereco(endereco);
        if (string.IsNullOrEmpty(endereco.Name)) return valor;
        if (endereco.Name.Any(c => char.IsControl(c) || c is '<' or '>'))
            throw new ArgumentException("Nome de remetente inválido.");
        return $"{endereco.Name} <{valor}>";
    }

    private static async Task<byte[]> LerAnexoAsync(Stream entrada, int limite, CancellationToken cancelamento)
    {
        ArgumentNullException.ThrowIfNull(entrada);
        if (!entrada.CanRead || limite < 0) throw new ArgumentException("Anexo inválido.");
        var posicao = entrada.CanSeek ? entrada.Position : (long?)null;
        try
        {
            if (posicao.HasValue && entrada.Length - posicao.Value > limite)
                throw new ArgumentException("Anexos ultrapassam o limite.");
            using var saida = new MemoryStream();
            var buffer = new byte[32 * 1024];
            while (true)
            {
                var quantidade = await entrada.ReadAsync(buffer.AsMemory(), cancelamento).ConfigureAwait(false);
                if (quantidade == 0) return saida.ToArray();
                if (quantidade > limite - saida.Length) throw new ArgumentException("Anexos ultrapassam o limite.");
                await saida.WriteAsync(buffer.AsMemory(0, quantidade), cancelamento).ConfigureAwait(false);
            }
        }
        finally
        {
            if (posicao.HasValue) entrada.Position = posicao.Value;
        }
    }

    private static SendResponse Falha(string mensagem)
        => new() { ErrorMessages = new List<string> { "Mailcore: " + mensagem } };
}
