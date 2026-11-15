using System.Net;
using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Mailcore;

namespace Mailcore.IntegracaoApi;

/// <summary>Laboratório privado. Nenhum segredo ou corpo entra no relatório.</summary>
internal sealed class Ensaio : IDisposable
{
    private static readonly byte[] Png = [137, 80, 78, 71];
    private const int LimiteFixture = 1024 * 1024;
    private readonly JsonObject fixture;
    private readonly byte[] ca;
    private readonly X509Certificate2 autoridade;
    private readonly HttpClient http;
    private readonly List<Cliente> clientes = [];
    private readonly List<string> cenarios = [];
    private readonly HashSet<string> protocolos = [];
    private string etapa = "configuracao";

    private sealed class Falha : Exception
    {
        public string? Codigo { get; }
        public int? Status { get; }
        public Falha() : base("Contrato de integração não satisfeito.") { }
        public Falha(ErroMailcore erro) : this() { Codigo = erro.Codigo; Status = erro.Status; }
    }

    private Ensaio(JsonObject fixture)
    {
        this.fixture = fixture;
        ca = Encoding.ASCII.GetBytes(Texto(fixture, "certificado_ca"));
        fixture.Remove("certificado_ca");
        autoridade = X509Certificate2.CreateFromPem(Encoding.ASCII.GetString(ca));
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            UseCookies = false,
            AutomaticDecompression = DecompressionMethods.None,
            ConnectTimeout = TimeSpan.FromSeconds(2),
            SslOptions = new SslClientAuthenticationOptions
            {
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                RemoteCertificateValidationCallback = ValidarCertificado,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck
            }
        };
        http = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(10),
            DefaultRequestVersion = HttpVersion.Version20,
            DefaultVersionPolicy = HttpVersionPolicy.RequestVersionOrLower
        };
    }

    private bool ValidarCertificado(object _, X509Certificate? certificado,
        X509Chain? __, SslPolicyErrors erros)
    {
        if (certificado is null || (erros & (SslPolicyErrors.RemoteCertificateNameMismatch |
                SslPolicyErrors.RemoteCertificateNotAvailable)) != SslPolicyErrors.None) return false;
        using var cadeia = new X509Chain();
        cadeia.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        cadeia.ChainPolicy.CustomTrustStore.Add(autoridade);
        cadeia.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        cadeia.ChainPolicy.VerificationFlags = X509VerificationFlags.NoFlag;
        return certificado is X509Certificate2 folha && cadeia.Build(folha);
    }

    private static void Exigir(bool condicao) { if (!condicao) throw new Falha(); }
    private static JsonNode D(Resposta resposta) => JsonNode.Parse(resposta.Dados) ?? throw new Falha();
    private static string Texto(JsonNode? documento, string nome)
    {
        Exigir(documento is not null && documento[nome] is JsonValue valor && valor.TryGetValue<string>(out _));
        return documento![nome]!.GetValue<string>();
    }
    private static string Id(JsonNode? documento) => Texto(documento, "id");
    private static JsonArray Lista(JsonNode? documento, string nome)
        => documento?[nome] as JsonArray ?? throw new Falha();
    private static bool Numero(JsonNode? documento, string nome, long esperado)
        => documento?[nome] is JsonValue valor && valor.TryGetValue<long>(out var numero) && numero == esperado;
    private static JsonNode? No(object? valor) => valor is JsonNode no ? no.DeepClone() : JsonSerializer.SerializeToNode(valor);
    private static JsonObject M(params object?[] pares)
    {
        Exigir(pares.Length % 2 == 0);
        var mapa = new JsonObject();
        for (var i = 0; i < pares.Length; i += 2) mapa.Add((string)pares[i]!, No(pares[i + 1]));
        return mapa;
    }
    private static JsonArray A(params object?[] valores)
    {
        var lista = new JsonArray();
        foreach (var valor in valores) lista.Add(No(valor));
        return lista;
    }
    private static JsonObject Alterar(JsonObject documento, string campo, object? valor)
    {
        var copia = (JsonObject)documento.DeepClone(); copia[campo] = No(valor); return copia;
    }
    private static IReadOnlyDictionary<string, object?> Consulta(int limite)
        => new Dictionary<string, object?> { ["limite"] = limite };
    private static OpcoesRequisicao Idempotencia(string chave) => new() { ChaveIdempotencia = chave };
    private static List<string> Ids(JsonNode documento)
        => Lista(documento, "dados").Select(Id).Order(StringComparer.Ordinal).ToList();
    private static Dictionary<string, string> Cabecalhos(params string[] pares)
    {
        var mapa = new Dictionary<string, string>();
        for (var i = 0; i < pares.Length; i += 2) mapa.Add(pares[i], pares[i + 1]);
        return mapa;
    }
    private static string? Cabecalho(Resposta resposta, string nome)
        => resposta.Cabecalhos.FirstOrDefault(c => string.Equals(c.Key, nome, StringComparison.OrdinalIgnoreCase)).Value?.FirstOrDefault();

    private Cliente Cliente(string chave, bool injetado = false)
    {
        var configuracao = new Configuracao
        {
            UrlBase = "https://mailcore-api:3443/v1",
            TempoLimite = TimeSpan.FromSeconds(10),
            TempoConexao = TimeSpan.FromSeconds(2),
            CertificadoCa = injetado ? null : ca
        };
        var cliente = injetado ? new Cliente(http, chave, configuracao) : new Cliente(chave, configuracao);
        clientes.Add(cliente);
        return cliente;
    }

    /// <summary>Sessão e callback pertencem à fixture, fora da superfície do SDK.</summary>
    private async Task<JsonObject> Requisitar(string caminho, HttpMethod metodo,
        Dictionary<string, string>? cabecalhos = null, string? corpo = null)
    {
        using var pedido = new HttpRequestMessage(metodo, "https://mailcore-api:3443" + caminho)
        { Version = HttpVersion.Version20, VersionPolicy = HttpVersionPolicy.RequestVersionOrLower };
        if (corpo is not null) pedido.Content = new StringContent(corpo, Encoding.UTF8, "application/json");
        foreach (var cabecalho in cabecalhos ?? [])
        {
            if (cabecalho.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))
            {
                if (pedido.Content is not null) pedido.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(cabecalho.Value);
            }
            else Exigir(pedido.Headers.TryAddWithoutValidation(cabecalho.Key, cabecalho.Value));
        }
        pedido.Headers.AcceptEncoding.ParseAdd("identity");
        using var resposta = await http.SendAsync(pedido, HttpCompletionOption.ResponseHeadersRead);
        protocolos.Add(resposta.Version.Major == 2 ? "HTTP/2" : resposta.Version.ToString());
        await using var stream = await resposta.Content.ReadAsStreamAsync();
        var bytes = await LerLimitado(stream);
        return M("status", (int)resposta.StatusCode, "dados", bytes.Length == 0 ? null : JsonNode.Parse(bytes));
    }

    private static async Task<byte[]> LerLimitado(Stream entrada)
    {
        using var dados = new MemoryStream(); var parte = new byte[8192];
        while (true)
        {
            var quantidade = await entrada.ReadAsync(parte);
            if (quantidade == 0) return dados.ToArray();
            Exigir(dados.Length + quantidade <= LimiteFixture); dados.Write(parte, 0, quantidade);
        }
    }

    private static async Task EsperarErro(int status, Func<Task<Resposta>> acao)
    {
        try { await acao(); }
        catch (ErroMailcore erro) { if (erro.Status == status) return; throw new Falha(erro); }
        throw new Falha();
    }
    private async Task LeiturasConcorrentes(Cliente sdk, string emailId)
    {
        var respostas = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => sdk.Emails.ObterAsync(emailId)));
        foreach (var resposta in respostas) Exigir(Id(D(resposta)) == emailId);
    }
    private static async Task<(Resposta Resposta, byte[] Dados)> Fluxo(
        Func<Func<ReadOnlyMemory<byte>, CancellationToken, ValueTask>, Task<Resposta>> acao)
    {
        using var dados = new MemoryStream();
        var resposta = await acao(async (parte, token) => await dados.WriteAsync(parte, token));
        Exigir(resposta.Dados.Length == 0 && resposta.TotalBytes == dados.Length);
        return (resposta, dados.ToArray());
    }
    private void Concluir() => cenarios.Add(etapa);

    private async Task<JsonObject> Cenarios()
    {
        etapa = "prontidao";
        var fim = DateTime.UtcNow + TimeSpan.FromSeconds(30); var pronta = false;
        while (!pronta && DateTime.UtcNow < fim)
        {
            try { pronta = Numero(await Requisitar("/health/ready", HttpMethod.Get), "status", 200); }
            catch (Exception) { }
            if (!pronta) await Task.Delay(200);
        }
        Exigir(pronta);
        etapa = "criar_full_sessao_sintetica";
        var criacao = await Requisitar("/v1/api-keys", HttpMethod.Post,
            Cabecalhos("Content-Type", "application/json", "Origin", "https://localhost:17443",
                "Cookie", "__Host-mailcore=" + Texto(fixture, "token"), "X-CSRF-Token", Texto(fixture, "csrf")),
            M("nome", "Fixture .NET Full", "permissao", "full_access").ToJsonString());
        fixture.Remove("token"); fixture.Remove("csrf"); Exigir(Numero(criacao, "status", 201));
        var full = criacao["dados"] ?? throw new Falha();
        var sdk = Cliente(Texto(full, "chave")); var sdkInjetado = Cliente(Texto(full, "chave"), true);
        var remetente = "avisos@" + Texto(fixture, "nome_dominio");

        etapa = "chaves_full_sending_escopo_dump";
        var chaveEnvio = D(await sdk.ChavesApi.CriarAsync(M("nome", "Fixture .NET Sending", "permissao", "sending_access", "dominio_id", Texto(fixture, "dominio"))));
        var envio = Cliente(Texto(chaveEnvio, "chave"));
        await EsperarErro(403, () => envio.Dominios.ListarAsync());
        Exigir(!sdk.ToString()!.Contains(Texto(full, "chave"), StringComparison.Ordinal));
        Exigir(Lista(D(await sdk.ChavesApi.ListarAsync()), "dados").Any(chave => Id(chave) == Id(chaveEnvio)));
        Concluir();

        var pedido = M("remetente", "Equipe .NET <" + remetente + ">", "destinatarios", A("principal@example.test"),
            "cc", A("copia@example.test"), "cco", A("oculto@example.test"), "assunto", "Validação .NET",
            "texto", "Dados sintéticos", "html", "<p>Dados sintéticos<img src=\"cid:marca@example.test\"></p>",
            "cabecalhos", M("X-Teste-SDK", "sintetico"), "agendada_em", Texto(fixture, "agendada_em"),
            "anexos", A(M("nome", "marca.png", "tipo_conteudo", "image/png", "conteudo_base64", Convert.ToBase64String(Png), "id_conteudo", "marca@example.test")));
        etapa = "envio_composto_idempotencia_409_download_cancelamento_concorrencia";
        var idem = Idempotencia("dotnet-composto");
        var aceita = D(await envio.Emails.EnviarAsync(pedido, idem));
        Exigir(Texto(aceita, "estado") == "agendada");
        Exigir(JsonNode.DeepEquals(D(await envio.Emails.EnviarAsync(pedido, idem)), aceita));
        await EsperarErro(409, () => envio.Emails.EnviarAsync(Alterar(pedido, "assunto", "Outra intenção"), idem));
        Exigir(Id(D(await sdk.Emails.ObterPorIdempotenciaAsync("dotnet-composto"))) == Id(aceita));
        var emailId = Id(aceita); Exigir(Lista(D(await sdk.Emails.ObterAsync(emailId)), "destinatarios").Count == 3);
        Exigir((await sdk.Emails.BaixarAnexoAsync(emailId, 0)).Dados.SequenceEqual(Png));
        await LeiturasConcorrentes(sdk, emailId);
        Exigir(Texto(D(await sdk.Emails.CancelarAsync(emailId)), "estado") == "cancelada"); Concluir();

        etapa = "lote_atomico_repeticao_conflito";
        var item = M("remetente", remetente, "destinatarios", A("lote@example.test"), "assunto", "Lote .NET",
            "texto", "Dados sintéticos", "agendada_em", Texto(fixture, "agendada_em"));
        var antes = Ids(D(await sdk.Emails.ListarAsync(Consulta(100))));
        await EsperarErro(422, () => envio.Lotes.EnviarAsync(A(item, Alterar(item, "destinatarios", A("endereco-invalido"))), Idempotencia("dotnet-lote-invalido")));
        Exigir(antes.SequenceEqual(Ids(D(await sdk.Emails.ListarAsync(Consulta(100))))));
        var lote = A(item, Alterar(item, "assunto", "Segundo item .NET")); var idemLote = Idempotencia("dotnet-lote");
        var aceitaLote = D(await envio.Lotes.EnviarAsync(lote, idemLote)); Exigir(Lista(aceitaLote, "dados").Count == 2);
        Exigir(JsonNode.DeepEquals(D(await envio.Lotes.EnviarAsync(lote, idemLote)), aceitaLote));
        Exigir(JsonNode.DeepEquals(D(await sdk.Lotes.ObterPorIdempotenciaAsync("dotnet-lote")), aceitaLote));
        await EsperarErro(409, () => envio.Lotes.EnviarAsync(A(Alterar(item, "assunto", "Divergente"), lote[1]), idemLote));
        Exigir(Ids(D(await sdk.Emails.ListarAsync(Consulta(100)))).Count == antes.Count + 2);
        foreach (var email in Lista(aceitaLote, "dados")) await sdk.Emails.CancelarAsync(Id(email)); Concluir();

        etapa = "dominios_capacidades_tracking_crud";
        var dominio = D(await sdk.Dominios.AtualizarAsync(Texto(fixture, "dominio"), M("capacidades", M("envio", "enabled", "recebimento", "enabled"), "rastreamento_aberturas", true, "rastreamento_cliques", true)));
        Exigir(Texto(dominio["capacidades"], "recebimento") == "enabled" && dominio["rastreamento_aberturas"]!.GetValue<bool>());
        var novo = D(await sdk.Dominios.CriarAsync(M("nome", "dotnet-" + Texto(fixture, "organizacao").Replace("-", "", StringComparison.Ordinal) + ".example.com",
            "capacidades", M("envio", "enabled", "recebimento", "disabled"))));
        Exigir(Texto(D(await sdk.Dominios.RemoverAsync(Id(novo))), "estado") == "suspenso"); Concluir();

        etapa = "contatos_objetos_listas_null_false_importacao_csv_fluxos";
        var segmento = D(await sdk.Segmentos.CriarAsync(M("nome", "Segmento .NET")));
        var topico = D(await sdk.Topicos.CriarAsync(M("nome", "Tópico .NET", "visibilidade", "publico", "assinatura_padrao", "opt_in")));
        var contato = D(await sdk.Contatos.CriarAsync(M("email", "contato@example.test", "primeiro_nome", "Sintético", "descadastrado", false,
            "segmentos", A(Id(segmento)), "topicos", A(M("id", Id(topico), "assinatura", "opt_in")))));
        Exigir(Id(D(await sdk.Contatos.ObterAsync(Id(contato)))) == Id(contato) && contato["descadastrado"]!.GetValue<bool>() == false);
        var importacao = D(await sdk.Contatos.Importacoes.CriarAsync(M("arquivo_nome", "fixture.csv", "conteudo", "email,primeiro_nome\ncontato@example.test,Importado\n",
            "mapeamento", M("0", "email", "1", "primeiro_nome")), Idempotencia("dotnet-importacao")));
        Exigir(Texto(importacao, "estado") == "pendente" && Numero(importacao, "total", 1) && Numero(importacao, "processados", 0));
        var atualizado = D(await sdk.Contatos.AtualizarAsync(Id(contato), M("primeiro_nome", null)));
        Exigir(atualizado.AsObject().ContainsKey("primeiro_nome") && atualizado["primeiro_nome"] is null);
        var exportacao = D(await sdk.Contatos.Exportacoes.CriarAsync(M(), Idempotencia("dotnet-exportacao")));
        Exigir(Texto(exportacao, "estado") == "concluida" && Numero(exportacao, "total", 1));
        var csv = await Fluxo(consumidor => sdk.Contatos.Exportacoes.BaixarFluxoAsync(Id(exportacao), consumidor));
        Exigir(Encoding.UTF8.GetString(csv.Dados).Contains("contato@example.test", StringComparison.Ordinal));
        var csvInjetado = await Fluxo(consumidor => sdkInjetado.Contatos.Exportacoes.BaixarFluxoAsync(Id(exportacao), consumidor));
        Exigir(csv.Dados.SequenceEqual(csvInjetado.Dados));
        Exigir(Lista(D(await sdk.Contatos.Exportacoes.ListarAsync()), "dados").Count == 1); Concluir();

        etapa = "modelos_criar_pasta";
        var pasta = D(await sdk.Modelos.Pastas.CriarAsync(M("nome", "Pasta .NET")));
        etapa = "modelos_criar";
        var modelo = D(await sdk.Modelos.CriarAsync(M("nome", "Modelo .NET", "html", "<p>Olá, {{{NOME}}}.</p>",
            "pasta_id", Id(pasta), "remetente", remetente, "assunto", "Modelo sintético", "variaveis", A(M("chave", "NOME", "tipo", "string")))));
        etapa = "modelos_versoes_obter";
        var versoes = Lista(D(await sdk.Modelos.Versoes.ListarAsync(Id(modelo))), "dados"); Exigir(versoes.Count > 0);
        var versao = versoes[0] ?? throw new Falha();
        var versaoObtida = D(await sdk.Modelos.Versoes.ObterAsync(Id(modelo), Id(versao)));
        Exigir(Texto(versaoObtida, "versao_id") == Id(versao) && Id(versaoObtida) == Id(modelo));
        etapa = "modelos_prever";
        var previa = D(await sdk.Modelos.PreverAsync(Id(modelo), M("revisao", modelo["revisao"], "variaveis", M("NOME", ".NET"))));
        Exigir(Texto(previa, "html").Contains(".NET", StringComparison.Ordinal));
        var prefixo = "/v1/template-previews/" + Texto(fixture, "organizacao") + "/"; var endereco = Texto(previa, "url");
        Exigir(endereco.StartsWith(prefixo, StringComparison.Ordinal)); var previaId = endereco[prefixo.Length..];
        Exigir(previaId.Length > 0 && !previaId.Contains('/'));
        etapa = "modelos_baixar_previa";
        Exigir(Encoding.UTF8.GetString((await sdk.Modelos.ObterPreviaAsync(Texto(fixture, "organizacao"), previaId)).Dados).Contains(".NET", StringComparison.Ordinal));
        etapa = "modelos_atualizar_cas";
        var antiga = modelo["revisao"]?.DeepClone(); var modeloId = Id(modelo);
        modelo = D(await sdk.Modelos.AtualizarAsync(modeloId, M("revisao", antiga, "nome", "Modelo editado")));
        await EsperarErro(409, () => sdk.Modelos.AtualizarAsync(modeloId, M("revisao", antiga, "nome", "Conflito")));
        etapa = "modelos_publicar_restaurar";
        modelo = D(await sdk.Modelos.PublicarAsync(modeloId, M("revisao", modelo["revisao"])));
        Exigir(Texto(modelo, "status") == "published");
        modelo = D(await sdk.Modelos.Versoes.RestaurarAsync(modeloId, Id(versao), M("revisao", modelo["revisao"])));
        etapa = "modelos_pastas_versoes_previa_publicacao_cas"; Concluir();

        etapa = "marketing_objeto_vazio_crud_sem_disparo";
        var campanha = D(await sdk.Campanhas.CriarAsync(M("nome", "Campanha .NET")));
        campanha = D(await sdk.Campanhas.AtualizarAsync(Id(campanha), M("revisao", campanha["revisao"], "nome", "Campanha editada")));
        Exigir(Id(D(await sdk.Campanhas.ObterAsync(Id(campanha)))) == Id(campanha)); await sdk.Campanhas.RemoverAsync(Id(campanha));
        var evento = D(await sdk.Eventos.CriarAsync(M("nome", "dotnet.qa", "esquema", M())));
        Exigir(evento["esquema"] is JsonObject esquema && esquema.Count == 0);
        var eventoNulo = D(await sdk.Eventos.AtualizarAsync(Id(evento), M("esquema", null)));
        Exigir(eventoNulo.AsObject().ContainsKey("esquema") && eventoNulo["esquema"] is null);
        var automacao = D(await sdk.Automacoes.CriarAsync(M("nome", "Automação .NET", "estado", "inativa")));
        automacao = D(await sdk.Automacoes.AtualizarAsync(Id(automacao), M("revisao", automacao["revisao"], "nome", "Automação editada")));
        Exigir(Id(D(await sdk.Automacoes.ObterAsync(Id(automacao)))) == Id(automacao));
        await sdk.Automacoes.RemoverAsync(Id(automacao)); await sdk.Eventos.RemoverAsync(Id(evento)); Concluir();

        etapa = "caixas_etiquetas_rascunhos_cas_sem_envio";
        var caixa = D(await sdk.Caixas.CriarAsync(M("endereco", "suporte@" + Texto(fixture, "nome_dominio"), "nome", "Suporte .NET")));
        Exigir(Id(D(await sdk.Caixas.ObterAsync(Id(caixa)))) == Id(caixa));
        var etiqueta = D(await sdk.Caixas.Etiquetas.CriarAsync(Id(caixa), M("nome", "Pedidos", "cor", "#224466")));
        var rascunho = D(await sdk.Caixas.Rascunhos.CriarAsync(Id(caixa), M("para", A("cliente@example.test"), "assunto", "Pedido", "texto", "Olá")));
        var revisaoRascunho = rascunho["revisao"]?.DeepClone(); var caixaId = Id(caixa); var rascunhoId = Id(rascunho);
        rascunho = D(await sdk.Caixas.Rascunhos.AtualizarAsync(caixaId, rascunhoId, M("revisao", revisaoRascunho, "texto", "Atualizado")));
        await EsperarErro(409, () => sdk.Caixas.Rascunhos.AtualizarAsync(caixaId, rascunhoId, M("revisao", revisaoRascunho, "texto", "Conflito")));
        var corpoRascunho = D(await sdk.Caixas.Rascunhos.ObterAsync(caixaId, rascunhoId));
        Exigir(corpoRascunho.AsObject().ContainsKey("html") && corpoRascunho["html"] is null);
        await sdk.Caixas.Rascunhos.RemoverAsync(caixaId, rascunhoId); await sdk.Caixas.Etiquetas.RemoverAsync(caixaId, Id(etiqueta));
        await sdk.Caixas.RemoverAsync(caixaId); Concluir();

        etapa = "recebimento_mime_real_pendente_bytes_fluxos_padrao_injetado";
        Exigir(Lista(D(await sdk.Recebimento.ListarAsync()), "dados").Count == 0);
        var caminho = "/interno/postal/recebidos/" + Texto(fixture, "rota");
        var headers = Cabecalhos("Content-Type", "application/json", "X-Postal-Signature-256", Texto(fixture, "callback_assinatura"));
        var callbackCorpo = Texto(fixture, "callback_corpo"); var callback = await Requisitar(caminho, HttpMethod.Post, headers, callbackCorpo);
        Exigir(Numero(callback, "status", 200)); var recebido = Id(callback["dados"]);
        var repetida = await Requisitar(caminho, HttpMethod.Post, headers, callbackCorpo);
        Exigir(Numero(repetida, "status", 200) && Id(repetida["dados"]) == recebido);
        var mime = Convert.FromBase64String(Texto(fixture, "mime_esperado"));
        var detalhe = D(await sdk.Recebimento.ObterAsync(recebido, new Dictionary<string, object?> { ["formato_html"] = "cid" }));
        Exigir(Texto(detalhe, "estado_processamento") == "pendente" && JsonNode.DeepEquals(Lista(detalhe, "para"), A("real@" + Texto(fixture, "nome_dominio"))));
        var original = await sdk.Recebimento.BaixarOriginalAsync(recebido);
        Exigir(original.Status == 200 && Cabecalho(original, "content-type") == "message/rfc822" && original.Dados.SequenceEqual(mime));
        var bruto = await Fluxo(consumidor => sdk.Recebimento.BaixarOriginalFluxoAsync(recebido, consumidor)); Exigir(bruto.Dados.SequenceEqual(mime));
        var brutoInjetado = await Fluxo(consumidor => sdkInjetado.Recebimento.BaixarOriginalFluxoAsync(recebido, consumidor));
        Exigir(brutoInjetado.Dados.SequenceEqual(mime) && Cabecalho(brutoInjetado.Resposta, "content-type") == "message/rfc822");
        Exigir(Lista(D(await sdk.Recebimento.ListarAsync()), "dados").Count == 1); await EsperarErro(403, () => envio.Recebimento.ListarAsync()); Concluir();

        etapa = "cli_revogacao_e_limpeza_crud";
        Exigir(Lista(D(await sdk.Cli.Concessoes.ListarAsync(Consulta(1))), "dados").Count == 0);
        await EsperarErro(403, () => envio.Cli.Concessoes.ListarAsync());
        Resposta[] removidas = [await sdk.Contatos.RemoverAsync(Id(contato)), await sdk.Segmentos.RemoverAsync(Id(segmento)),
            await sdk.Topicos.RemoverAsync(Id(topico)), await sdk.Modelos.RemoverAsync(Id(modelo)),
            await sdk.Modelos.Pastas.RemoverAsync(Id(pasta)), await sdk.ChavesApi.RevogarAsync(Id(chaveEnvio))];
        foreach (var removida in removidas) Exigir(removida.Status is >= 200 and <= 299);
        await EsperarErro(401, () => envio.Emails.EnviarAsync(pedido, Idempotencia("dotnet-revogada"))); Concluir();
        return M("resultado", "aprovado", "cenarios", cenarios, "recebimento_processado_por_worker", false,
            "importacao_processada_por_worker", false, "callbacks_fluxo_padrao", 2, "callbacks_fluxo_http_injetado", 2,
            "http_injetado_provedor", "System.Net.Http", "leituras_concorrentes_mesmo_cliente", 8,
            "politica_http_injetado", "tls_ca_explicita_sem_proxy_cookies_redirect_decompress_timeout",
            "protocolos_http_helper_observados", protocolos);
    }

    private async Task<JsonObject> Executar()
    {
        try { return await Cenarios(); }
        catch (Exception erro)
        {
            var relatorio = M("resultado", "falhou", "etapa", etapa, "cenarios", cenarios,
                "falha", "Contrato de integração não satisfeito; detalhes privados omitidos.");
            if (erro is ErroMailcore api) relatorio["erro_http"] = M("codigo", api.Codigo, "status", api.Status);
            else if (erro is Falha { Codigo: not null } falha) relatorio["erro_http"] = M("codigo", falha.Codigo, "status", falha.Status);
            return relatorio;
        }
    }

    public void Dispose()
    {
        for (var i = clientes.Count - 1; i >= 0; i--) clientes[i].Dispose();
        http.Dispose(); autoridade.Dispose();
    }

    public static async Task<int> Main()
    {
        JsonObject relatorio;
        try
        {
            var entrada = JsonNode.Parse(await LerLimitado(Console.OpenStandardInput())) as JsonObject ?? throw new Falha();
            using var ensaio = new Ensaio(entrada); relatorio = await ensaio.Executar();
        }
        catch (Exception)
        {
            relatorio = M("resultado", "falhou", "etapa", "configuracao", "falha", "Não foi possível iniciar o cliente sintético.");
        }
        relatorio["dotnet"] = Environment.Version.ToString();
        Console.WriteLine(relatorio.ToJsonString());
        return relatorio["resultado"]?.GetValue<string>() == "aprovado" ? 0 : 1;
    }
}
