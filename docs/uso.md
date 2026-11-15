# Uso do SDK .NET

Os fragmentos usam uma instância aberta de `Cliente`. Compartilhe essa instância entre as operações da aplicação e descarte-a no encerramento de seu ciclo de vida. A biblioteca usa métodos assíncronos e não exige que a aplicação bloqueie threads para aguardar a rede.

## Configuração e credenciais

`new Cliente()` resolve a chave de `MAILCORE_API_KEY`. `new Cliente(chave)` usa uma chave explícita. Para alterar a origem e os limites:

```csharp
var configuracao = new Configuracao
{
    UrlBase = "https://mailcore.com.br/v1",
    TempoLimite = TimeSpan.FromSeconds(45),
    TempoConexao = TimeSpan.FromSeconds(10),
    LimiteRespostaBytes = 32L * 1024 * 1024
};

using var cliente = new Cliente(configuracao);
```

O prefixo da origem é preservado ao acrescentar as rotas dos recursos. HTTP exige `PermitirHttpLocal = true` e host de loopback em testes. Configuração, cabeçalhos e material de CA usados pelo cliente são copiados para que alterações posteriores no objeto fornecido não mudem silenciosamente sua política de transporte.

Chave explícita no construtor e chave em `Configuracao.ChaveApi` ao mesmo tempo são recusadas. Não inclua chaves em URLs, código, logs ou repositórios. Um fornecedor de credenciais deve ser seguro para chamadas concorrentes e tratar a aquisição conforme os prazos da aplicação.

`FornecedorChave` recebe uma função `Func<CancellationToken, ValueTask<string>>`, consultada por operação. A função deve respeitar o token recebido e retornar uma chave válida; uma falha na aquisição não deve ser tratada como autorização para usar outra chave silenciosamente.

`CertificadoCa` aceita bytes PEM de uma autoridade privada, acrescentando confiança sem desativar a verificação de hostname. Com `new Cliente(httpClient, configuracao: configuracao)`, o `HttpClient` continua pertencendo à aplicação: configure a CA, TLS, proxies, cookies e handlers nele. Não combine esse cliente com `Configuracao.CertificadoCa`; o SDK não descarta o cliente HTTP injetado ao fechar sua própria instância.

## Opções e cancelamento

```csharp
var opcoes = new OpcoesRequisicao
{
    ChaveIdempotencia = "pedido-123-confirmacao",
    TempoLimite = TimeSpan.FromSeconds(60),
    LimiteRespostaBytes = 64L * 1024 * 1024,
    Cabecalhos = new Dictionary<string, IReadOnlyList<string>>
    {
        ["X-Correlacao"] = new[] { "pedido-123" }
    }
};

using var encerramento = new CancellationTokenSource();
var resposta = await cliente.Emails.EnviarAsync(pedido, opcoes, encerramento.Token);
```

As sobrecargas sem opções usam os padrões do cliente. Cabeçalhos reservados, como `Authorization`, `Host`, `Cookie`, `Content-Type`, `Accept` e `Idempotency-Key`, não devem ser substituídos pelo dicionário de cabeçalhos HTTP. Configure a idempotência no campo próprio.

O `CancellationToken` permite interromper a espera e a leitura. Outra parte do fluxo pode chamar `encerramento.Cancel()`. Um cancelamento ou timeout não comprova que o servidor rejeitou a intenção; a resposta pode ter sido interrompida após a aceitação. Reconcilie usando a mesma chave de idempotência.

O cancelamento explícito do chamador conserva o contrato `OperationCanceledException`. O código `prazo_excedido` identifica o prazo total da operação. Consumidores de fluxo devem observar o token recebido e cooperar com o encerramento.

## Corpos, filtros e JSON

Use os campos em português do contrato HTTP, por exemplo `remetente`, `destinatarios`, `assunto`, `html`, `texto`, `revisao` e `agendada_em`. Objetos anônimos e dicionários são apropriados para pedidos. O servidor continua validando combinações, formatos, revisões, estados e permissões.

Campos `null` e `false` dentro de um pedido são preservados. A raiz de um pedido de objeto não pode ser `null` ou array; lotes exigem array na raiz. Uma operação cujo corpo é opcional oferece uma conveniência que envia `{}`, como `cliente.Modelos.DuplicarAsync(id)`. Para passar opções nessa operação, forneça explicitamente um objeto vazio ao método principal.

Filtros são `IReadOnlyDictionary<string, object?>`. Valores `null` são omitidos na query, e `false` é enviado como `false`. Consultas opcionais aceitam omissão do dicionário e permitem opções nomeadas:

```csharp
var resposta = await cliente.Emails.ListarAsync(
    filtros: new Dictionary<string, object?> { ["limite"] = 20 },
    opcoes: opcoes,
    cancelamento: cancellationToken);
```

Cada chamada de listagem consulta uma página. O SDK não percorre cursores automaticamente. Use `proximo` ou `proximo_cursor` como `antes`, conforme o recurso. Recebimento também aceita `depois`, sem combiná-lo com `antes`.

## Envio direto, modelos e lotes

O envio direto aceita composição com Cc/Cco, cabeçalhos e anexos. Destinatários e cópias usam endereços sem nome de exibição; o remetente pode ter nome. Os anexos usam `nome`, `tipo_conteudo`, `conteudo_base64` e, para CID, `id_conteudo`. Base64 não deve ter prefixo `data:`; o SDK não baixa anexos remotos.

```csharp
var pedido = new
{
    remetente = "Equipe <notificacoes@seudominio.com.br>",
    destinatarios = new[] { "cliente@example.com" },
    assunto = "Pedido recebido",
    html = "<img src=\"cid:marca\" alt=\"Marca\"><p>Recebemos seu pedido.</p>",
    anexos = new[]
    {
        new
        {
            nome = "marca.png",
            tipo_conteudo = "image/png",
            conteudo_base64 = conteudoBase64,
            id_conteudo = "marca"
        }
    }
};
```

Use modelos publicados, por ID ou alias:

```csharp
var pedido = new
{
    destinatarios = new[] { "cliente@example.com" },
    modelo = new { id = "boas-vindas", variaveis = new { NOME = "Ana" } }
};
```

O envio individual por modelo aceita anexos Base64 sem CID, mas não combina HTML/texto, Cc/Cco, cabeçalhos ou nome de exibição no remetente. Padrões publicados podem fornecer remetente, assunto e resposta. A API valida os valores das variáveis.

`cliente.Lotes.EnviarAsync(lista, opcoes, cancellationToken)` envia de 1 a 100 mensagens numa única requisição. O lote é aceito de forma atômica e conserva a ordem; o SDK não divide a lista nem implementa fila. Lotes não aceitam anexos, Cc/Cco, cabeçalhos ou nome de exibição no remetente.

O agendamento usa `agendada_em` com data ISO 8601 que inclua o fuso. Reagendamento e cancelamento dependem do estado da intenção no servidor. Conflitos de revisão HTTP 409 são entregues à aplicação; o SDK não inventa revisões nem reaplica uma edição concorrente.

## Respostas, erros e arquivos

```csharp
var resposta = await cliente.Emails.ObterAsync(id, cancelamento: cancellationToken);
var dados = resposta.Json();
var estado = dados.GetProperty("estado").GetString();
```

Os bytes originais permanecem em `Dados`; o array retornado é uma cópia. `Cabecalhos` conserva os valores permitidos com coleções somente para leitura. Respostas sem conteúdo conservam o status e um array vazio; não tente interpretá-las como JSON.

`Json()` fornece um `JsonElement` que pode ser usado sem manter um `JsonDocument` aberto. Números permanecem disponíveis em sua forma JSON via `GetRawText()`. As conversões escolhidas pela aplicação, como `GetDouble` ou `GetDecimal`, continuam sujeitas ao alcance desses tipos.

`ErroMailcore` separa `Codigo`, `Mensagem`, `Status` opcional e `Cabecalhos`. O texto padrão da exceção é sanitizado; a mensagem de negócio pode conter informações privadas. Falhas de rede não têm necessariamente um status HTTP. Registre somente os dados necessários para classificar a falha.

`Configuracao.LancarExcecoes` é `true` por padrão. Se a aplicação escolher `false`, examine `resposta.Erro` e `resposta.Sucesso` antes de interpretar o corpo. Uma falha sem status HTTP é representada com status zero na resposta de erro. Essa opção não transforma cancelamento do chamador em sucesso nem em uma resposta HTTP.

Downloads comuns acumulam a resposta em memória. As três variantes `Contatos.Exportacoes.BaixarFluxoAsync`, `Recebimento.BaixarOriginalFluxoAsync` e `Recebimento.BaixarAnexoFluxoAsync` recebem uma função assíncrona consumidora:

```csharp
await using var saida = new FileStream(destino, FileMode.CreateNew, FileAccess.Write);
var download = await cliente.Recebimento.BaixarOriginalFluxoAsync(
    id,
    (parte, token) => saida.WriteAsync(parte, token),
    opcoes,
    cancellationToken);

var totalGravado = download.TotalBytes;
```

O retorno de sucesso tem `Dados` vazio e a quantidade entregue em `TotalBytes`. O chamador trata arquivos parciais se houver erro ou cancelamento. As rotas de download são fixas e autenticadas; URLs temporárias em metadados não são seguidas automaticamente com a chave da API.

## Idempotência

Persista a chave e o conteúdo da intenção antes de enviar. Se a aplicação decidir repetir após uma resposta incerta, reutilize ambos. `Emails.ObterPorIdempotenciaAsync` e `Lotes.ObterPorIdempotenciaAsync` permitem conciliação com permissão de leitura; a retenção da chave é determinada pelo servidor.

O SDK não implementa fila de reenvio, retentativas de negócio ou backoff automático. Um cliente HTTP injetado pertence à aplicação e pode ter suas próprias políticas. Reutilize o cliente e trate limites de envio no fluxo responsável pelo processamento.

## Injeção de dependências

```csharp
using Mailcore;
using Microsoft.Extensions.DependencyInjection;

services.AddMailcore(configuracao =>
{
    configuracao.ChaveApi = Environment.GetEnvironmentVariable("MAILCORE_API_KEY");
    configuracao.TempoLimite = TimeSpan.FromSeconds(45);
});
```

O registro oferece `Cliente` e `ICliente` por meio de `IHttpClientFactory`, com o handler seguro do SDK. A extensão retorna `IHttpClientBuilder` para configurações explícitas da aplicação. Ela não instala uma política de retentativas de negócio. Configure handlers e telemetria adicionais considerando o conteúdo privado de chaves, URLs e mensagens.

O [exemplo ASP.NET](../exemplos/AspNet/README.md) percorre DI, Minimal API e controller MVC. O [pacote FluentEmail](../src/Mailcore.FluentEmail/README.md) é opcional e usa o mesmo `ICliente`. Seu seletor recupera a chave persistida de cada mensagem; ele não deve criar uma chave nova a cada tentativa.

## Verificação de webhooks

```csharp
var evento = Assinatura.Verificar(corpoBruto, cabecalhosRecebidos, segredoWebhook);
var tipo = evento.Tipo;
var dados = evento.Dados;
```

`corpoBruto` contém os bytes originais, antes de parser ou recodificação. Os cabeçalhos usam `IReadOnlyDictionary<string, IReadOnlyList<string>>`. O verificador exige `svix-id`, `svix-timestamp` e `svix-signature`, segredo `whsec_...`, Base64 canônico e HMAC-SHA256. A tolerância temporal padrão e máxima é de cinco minutos, nos dois sentidos, com corpo limitado a 8 MiB.

`OpcoesAssinatura` permite reduzir `Tolerancia` e injetar `Agora` nos testes. A falha lança `ErroMailcore` com código `assinatura_invalida` e representação textual sanitizada. O evento conserva `Tipo`, `CriadoEm` e os dados JSON; seu texto padrão omite o conteúdo.

Interprete o evento somente após a verificação. Deduplique `svix-id` em armazenamento durável, pois o helper criptográfico não mantém estado contra repetição. Mensagens e dados do evento podem conter informações privadas.

O pacote opcional [Mailcore.AspNetCore](../src/Mailcore.AspNetCore/README.md) pode mapear o endpoint:

```csharp
using Mailcore.AspNetCore;

app.MapMailcoreWebhook("/webhooks/email", segredoWebhook,
    async (evento, contexto, cancelamento) =>
    {
        await ProcessarEventoAsync(evento, contexto, cancelamento);
    });
```

`ProcessarEventoAsync` pertence à aplicação. A extensão aceita POST, limita a leitura a 8 MiB e verifica a assinatura antes do callback. Sucesso retorna HTTP 204, assinatura inválida retorna 400 genérico e corpo excedido retorna 413. O callback recebe `RequestAborted` e deve cooperar com cancelamento; suas falhas permanecem na pipeline ASP.NET. Autorização, deduplicação e persistência continuam no fluxo da aplicação. `Mailcore` é o SDK base; `Mailcore.FluentEmail` e `Mailcore.AspNetCore` são integrações opcionais.
