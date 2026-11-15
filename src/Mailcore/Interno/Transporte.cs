using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mailcore.Interno;

internal sealed class Transporte : IDisposable
{
    private static readonly HashSet<string> Reservados = new(StringComparer.OrdinalIgnoreCase)
    { "authorization", "proxy-authorization", "cookie", "host", "content-length", "content-type", "accept", "connection", "transfer-encoding", "idempotency-key", "user-agent", "accept-encoding", "trailer", "te", "upgrade" };
    private readonly Configuracao _configuracao;
    private readonly HttpClient _http;
    private readonly bool _proprio;
    private readonly CancellationTokenSource _encerramento = new();
    private readonly object _trava = new();
    private bool _fechado;

    internal Transporte(string? chave, Configuracao? configuracao, HttpClient? http = null)
    {
        _configuracao = (configuracao ?? new Configuracao()).Copiar();
        if (chave is not null && _configuracao.ChaveApi is not null)
            throw ErroMailcore.Configuracao("Informe a chave em apenas um lugar.");
        _configuracao.ChaveApi = chave ?? _configuracao.ChaveApi;
        ConfiguracaoValidada.Validar(_configuracao);
        if (http is not null && _configuracao.CertificadoCa is not null)
            throw ErroMailcore.Configuracao("Configure a CA no cliente HTTP injetado.");
        _proprio = http is null;
        _http = http ?? new HttpClient(ConfiguracaoValidada.CriarHandler(_configuracao)) { Timeout = Timeout.InfiniteTimeSpan };
    }

    internal async Task<Resposta> ExecutarAsync(HttpMethod metodo, Func<string> caminho, object? corpo,
        FormatoCorpo formatoCorpo, IReadOnlyDictionary<string, object?>? consulta,
        FormatoResposta formato, bool aceitarVazio, OpcoesRequisicao? opcoes, CancellationToken cancelamento,
        Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>? consumidor = null)
    {
        int? status = null;
        IReadOnlyDictionary<string, IReadOnlyList<string>> cabecalhos = Cabecalhos.Copiar(null);
        try
        {
            cancelamento.ThrowIfCancellationRequested();
            CancellationToken fim;
            lock (_trava)
            {
                if (_fechado) throw NovoErro("cliente_fechado", "O cliente já foi encerrado.");
                fim = _encerramento.Token;
            }
            opcoes ??= new OpcoesRequisicao();
            var prazo = ConfiguracaoValidada.Prazo(opcoes.TempoLimite ?? _configuracao.TempoLimite);
            var limite = ConfiguracaoValidada.Limite(opcoes.LimiteRespostaBytes ?? _configuracao.LimiteRespostaBytes);
            using var vinculado = CancellationTokenSource.CreateLinkedTokenSource(cancelamento, fim);
            vinculado.CancelAfter(prazo);
            var token = vinculado.Token;
            var rota = caminho();
            ValidarRota(rota);
            var bytes = Json.Codificar(corpo, formatoCorpo);
            var url = _configuracao.UrlBase.TrimEnd('/') + rota + MontarConsulta(consulta);
            var adicionais = Cabecalhos.Copiar(opcoes.Cabecalhos);
            using var pedido = new HttpRequestMessage(metodo, url)
            { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
            foreach (var (nome, valores) in adicionais)
            {
                if (string.IsNullOrEmpty(nome) || Reservados.Contains(nome) || !nome.All(CaractereNome))
                    throw ErroMailcore.Configuracao("Cabeçalho reservado ou nome inválido.");
                foreach (var valor in valores)
                {
                    if (!valor.All(c => c is >= (char)32 and <= (char)126))
                        throw ErroMailcore.Configuracao("Use cabeçalhos ASCII sem controles.");
                    if (!pedido.Headers.TryAddWithoutValidation(nome, valor))
                        throw ErroMailcore.Configuracao("Cabeçalho HTTP inválido.");
                }
            }
            var chave = await ResolverChaveAsync(token).ConfigureAwait(false);
            pedido.Headers.Authorization = new AuthenticationHeaderValue("Bearer", chave);
            pedido.Headers.UserAgent.ParseAdd("mailcore-dotnet/0.1.0-alpha.1");
            pedido.Headers.AcceptEncoding.ParseAdd("identity");
            pedido.Headers.Accept.ParseAdd(formato == FormatoResposta.Json ? "application/json"
                : formato == FormatoResposta.Texto ? "text/html" : "application/octet-stream");
            if (opcoes.ChaveIdempotencia is not null)
            {
                ConfiguracaoValidada.Chave(opcoes.ChaveIdempotencia);
                if (opcoes.ChaveIdempotencia.Length > 256)
                    throw ErroMailcore.Configuracao("Idempotência aceita até 256 caracteres ASCII visíveis.");
                pedido.Headers.Add("Idempotency-Key", opcoes.ChaveIdempotencia);
            }
            if (bytes is not null || metodo != HttpMethod.Get)
                pedido.Content = new CorpoUnico(bytes ?? [], bytes is not null);
            token.ThrowIfCancellationRequested();
            using var retorno = await AguardarRecursoAsync(
                _http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead, token), token).ConfigureAwait(false);
            status = (int)retorno.StatusCode;
            cabecalhos = CapturarCabecalhos(retorno);
            ValidarMetadados(retorno, limite, cabecalhos);
            if (status is >= 300 and < 400)
                throw NovoErro("redirecionamento_recusado", "A API retornou redirecionamento recusado.", status, cabecalhos);
            var fluxo = consumidor is not null && status is >= 200 and < 300;
            using var acumulado = new MemoryStream();
            using var entrada = await AguardarRecursoAsync(retorno.Content.ReadAsStreamAsync(token), token).ConfigureAwait(false);
            var buffer = new byte[32768];
            long total = 0;
            while (true)
            {
                var tamanho = await entrada.ReadAsync(buffer, token).AsTask().WaitAsync(token).ConfigureAwait(false);
                if (tamanho == 0) break;
                if (tamanho > limite - total)
                    throw NovoErro("resposta_excedida", "A resposta ultrapassa o limite de bytes.", status, cabecalhos);
                total += tamanho;
                if (fluxo)
                {
                    try
                    {
                        var parte = buffer.AsMemory(0, tamanho).ToArray();
                        await consumidor!(parte, token).AsTask().WaitAsync(token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                    catch (ErroMailcore) { throw; }
                    catch (Exception) { throw NovoErro("erro_consumidor", "Não foi possível consumir o download.", status, cabecalhos); }
                }
                else acumulado.Write(buffer, 0, tamanho);
            }
            token.ThrowIfCancellationRequested();
            var anunciado = retorno.Content.Headers.ContentLength;
            if (anunciado is not null && anunciado != total)
                throw NovoErro("resposta_incompleta", "A resposta terminou incompleta.", status, cabecalhos);
            var dados = acumulado.ToArray();
            if (status is < 200 or >= 300)
            {
                var codigo = "erro_http";
                var mensagem = "A API retornou erro HTTP.";
                try
                {
                    var envelope = Json.Ler(dados);
                    if (envelope.ValueKind == JsonValueKind.Object && envelope.TryGetProperty("erro", out var erro)
                        && erro.ValueKind == JsonValueKind.Object && erro.TryGetProperty("codigo", out var campoCodigo)
                        && campoCodigo.ValueKind == JsonValueKind.String && erro.TryGetProperty("mensagem", out var campoMensagem)
                        && campoMensagem.ValueKind == JsonValueKind.String)
                    { codigo = campoCodigo.GetString()!; mensagem = campoMensagem.GetString()!; }
                }
                catch (ErroMailcore) { /* Corpos de intermediários não entram nas exceções. */ }
                throw NovoErro(codigo, mensagem, status, cabecalhos);
            }
            if (!fluxo)
            {
                try
                {
                    if (formato == FormatoResposta.Json && !(dados.Length == 0 && (status == 204 || aceitarVazio))) Json.Validar(dados);
                    else if (formato == FormatoResposta.Texto) Json.ValidarUtf8(dados);
                }
                catch (ErroMailcore) { throw NovoErro("resposta_invalida", "A API retornou conteúdo inválido.", status, cabecalhos); }
            }
            token.ThrowIfCancellationRequested();
            return new Resposta(dados, status.Value, cabecalhos, total);
        }
        catch (OperationCanceledException) when (cancelamento.IsCancellationRequested)
        { throw new OperationCanceledException(cancelamento); }
        catch (OperationCanceledException)
        {
            bool fechado;
            lock (_trava) fechado = _fechado;
            return TratarErro(NovoErro(fechado ? "cancelado" : "prazo_excedido",
                fechado ? "O cliente foi encerrado durante a operação." : "A operação excedeu o prazo.", status, cabecalhos));
        }
        catch (ErroMailcore erro) { return TratarErro(erro); }
        catch (Exception)
        {
            return TratarErro(NovoErro(status is null ? "falha_de_rede" : "resposta_incompleta",
                "Não foi possível concluir a comunicação com a API.", status, cabecalhos));
        }
    }

    private Resposta TratarErro(ErroMailcore erro) => _configuracao.LancarExcecoes ? throw erro : Resposta.DeErro(erro);

    private static async Task<T> AguardarRecursoAsync<T>(Task<T> tarefa, CancellationToken token) where T : class, IDisposable
    {
        try { return await tarefa.WaitAsync(token).ConfigureAwait(false); }
        catch
        {
            // Provedores que ignoram cancelamento podem concluir depois do prazo.
            // A resposta/stream tardio continua sob responsabilidade do SDK.
            _ = tarefa.ContinueWith(static concluida =>
            {
                if (concluida.Status == TaskStatus.RanToCompletion)
                {
                    try { concluida.Result?.Dispose(); }
                    catch (Exception) { /* Não expor falhas privadas durante a limpeza tardia. */ }
                }
                else { _ = concluida.Exception; }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            throw;
        }
    }

    private async ValueTask<string> ResolverChaveAsync(CancellationToken token)
    {
        try
        {
            var chave = _configuracao.FornecedorChave is { } fornecedor
                ? await fornecedor(token).AsTask().WaitAsync(token).ConfigureAwait(false)
                : _configuracao.ChaveApi ?? Environment.GetEnvironmentVariable("MAILCORE_API_KEY");
            return ConfiguracaoValidada.Chave(chave);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception) { throw ErroMailcore.Configuracao("Não foi possível obter uma chave de API válida."); }
    }

    private static IReadOnlyDictionary<string, IReadOnlyList<string>> CapturarCabecalhos(HttpResponseMessage resposta)
    {
        var dados = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (nome, valores) in resposta.Headers.Concat(resposta.Content.Headers))
        {
            if (nome.Equals("Set-Cookie", StringComparison.OrdinalIgnoreCase) || nome.Equals("Authorization", StringComparison.OrdinalIgnoreCase)
                || nome.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase)) continue;
            dados[nome] = Array.AsReadOnly(dados.TryGetValue(nome, out var anteriores) ? anteriores.Concat(valores).ToArray() : valores.ToArray());
        }
        return Cabecalhos.Copiar(dados);
    }

    private static void ValidarMetadados(HttpResponseMessage resposta, long limite,
        IReadOnlyDictionary<string, IReadOnlyList<string>> seguro)
    {
        var status = (int)resposta.StatusCode;
        var cabecalhos = resposta.Headers.Concat(resposta.Content.Headers).ToArray();
        var tamanho = cabecalhos.Sum(x => x.Value.Sum(v => Encoding.UTF8.GetByteCount(x.Key) + Encoding.UTF8.GetByteCount(v) + 4L));
        if (tamanho > 65536) throw NovoErro("resposta_excedida", "Cabeçalhos excederam o limite.", status, seguro);
        foreach (var valor in resposta.Content.Headers.ContentEncoding)
            foreach (var token in valor.Split(','))
                if (token.Trim() is not "" && !token.Trim().Equals("identity", StringComparison.OrdinalIgnoreCase))
                    throw NovoErro("codificacao_inesperada", "A resposta usa codificação não negociada.", status, seguro);
        if (resposta.Content.Headers.ContentLength is { } comprimento && (comprimento < 0 || comprimento > limite))
            throw NovoErro("resposta_excedida", "A resposta ultrapassa o limite de bytes.", status, seguro);
    }

    private static string MontarConsulta(IReadOnlyDictionary<string, object?>? consulta)
    {
        if (consulta is null) return "";
        var partes = new List<string>();
        foreach (var (chave, valor) in consulta)
        {
            if (valor is null) continue;
            Json.ValidarTexto(chave);
            var texto = valor switch
            {
                string s => s,
                bool b => b ? "true" : "false",
                char c => c.ToString(),
                double n when double.IsFinite(n) => n.ToString("R", CultureInfo.InvariantCulture),
                float n when float.IsFinite(n) => n.ToString("R", CultureInfo.InvariantCulture),
                byte or sbyte or short or ushort or int or uint or long or ulong or decimal or System.Numerics.BigInteger => ((IFormattable)valor).ToString(null, CultureInfo.InvariantCulture),
                JsonElement { ValueKind: JsonValueKind.Null } => null,
                JsonElement n when n.ValueKind is JsonValueKind.String or JsonValueKind.Number or JsonValueKind.True or JsonValueKind.False => n.ValueKind == JsonValueKind.String ? n.GetString() : n.GetRawText(),
                _ => throw ErroMailcore.Configuracao("Filtros aceitam apenas valores simples e números finitos.")
            };
            if (texto is null) continue;
            Json.ValidarTexto(texto);
            partes.Add(Uri.EscapeDataString(chave) + "=" + Uri.EscapeDataString(texto));
        }
        return partes.Count == 0 ? "" : "?" + string.Join('&', partes);
    }

    private static bool CaractereNome(char c) => char.IsAsciiLetterOrDigit(c) || "!#$%&'*+-.^_`|~".Contains(c);
    private static void ValidarRota(string rota)
    {
        if (string.IsNullOrEmpty(rota) || !rota.StartsWith('/') || rota.StartsWith("//", StringComparison.Ordinal)
            || rota.Any(c => c <= 32 || c == 127 || c is '?' or '#' or '\\') || rota.Split('/').Any(x => x is "." or ".."))
            throw ErroMailcore.Configuracao("Caminho de recurso inválido.");
    }
    private static ErroMailcore NovoErro(string codigo, string mensagem, int? status = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? cabecalhos = null) => new(codigo, mensagem, status, cabecalhos);

    private sealed class CorpoUnico : HttpContent
    {
        private readonly byte[] _dados;
        private int _gravacoes;
        internal CorpoUnico(byte[] dados, bool json)
        {
            _dados = dados;
            if (json) Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        }
        protected override bool TryComputeLength(out long length) { length = _dados.Length; return true; }
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => GravarAsync(stream, CancellationToken.None);
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context, CancellationToken cancellationToken) => GravarAsync(stream, cancellationToken);
        private async Task GravarAsync(Stream destino, CancellationToken token)
        {
            if (Interlocked.Increment(ref _gravacoes) != 1) throw NovoErro("repeticao_recusada", "O corpo da intenção não pode ser reemitido automaticamente.");
            await destino.WriteAsync(_dados, token).ConfigureAwait(false);
        }
    }

    public void Dispose()
    {
        lock (_trava) { if (_fechado) return; _fechado = true; }
        try { _encerramento.Cancel(); }
        finally { _encerramento.Dispose(); if (_proprio) _http.Dispose(); }
    }
}
