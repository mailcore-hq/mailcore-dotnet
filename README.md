# SDK Mailcore para .NET

[Código-fonte](https://github.com/mailcore-hq/mailcore-dotnet) · [Problemas e sugestões](https://github.com/mailcore-hq/mailcore-dotnet/issues)

Cliente assíncrono da API Mailcore, com **151 operações em 30 classes de recursos**, cancelamento por `CancellationToken`, respostas com os bytes originais e downloads em fluxo. Compartilhe o cliente durante o ciclo de vida da aplicação para reutilizar conexões HTTP.

Pacote **`Mailcore`**, versão **`0.1.0-alpha.1`**. O pacote oferece os targets `net8.0`, `net10.0` e `net11.0`; a validação também executa em .NET 9 usando o target compatível `net8.0`. A existência de um target de compatibilidade não estende o suporte de segurança do runtime escolhido.

## Instalação

Para versões disponíveis no NuGet, instale o pacote na aplicação consumidora:

```sh
dotnet add package Mailcore --version 0.1.0-alpha.1
```

## Desenvolvimento e instalação local

Uma aplicação consumidora usa suas ferramentas .NET habituais. O ambiente de desenvolvimento e verificação desta biblioteca é executado em Docker, sem instalar o SDK .NET no host.

Para desenvolver com o checkout local, a aplicação pode referenciar o projeto:

```xml
<ItemGroup>
  <ProjectReference Include="../mailcore-dotnet/src/Mailcore/Mailcore.csproj" />
</ItemGroup>
```

O caminho do exemplo considera a aplicação e o checkout `mailcore-dotnet` no mesmo diretório pai; ajuste a referência à organização do seu projeto.

A distribuição gera o `.nupkg` efetivo e o pacote de símbolos. A [verificação](docs/verificacao.md) mostra como instalar o pacote local em consumidores fora do checkout, com dependências resolvidas offline. O pacote não inclui cópias de suas dependências.

## Enviar o primeiro e-mail

Configure `MAILCORE_API_KEY` no ambiente protegido da aplicação. Use um remetente com domínio verificado e uma chave com permissão de envio. Persista a chave de idempotência e o conteúdo da intenção antes de chamar a API.

```csharp
using Mailcore;

using var cliente = new Cliente();

var pedido = new
{
    remetente = "notificacoes@seudominio.com.br",
    destinatarios = new[] { "cliente@example.com" },
    assunto = "Pedido recebido",
    html = "<p>Recebemos seu pedido.</p>",
    texto = "Recebemos seu pedido."
};

var opcoes = new OpcoesRequisicao
{
    ChaveIdempotencia = "pedido-123-confirmacao"
};

try
{
    var resposta = await cliente.Emails.EnviarAsync(pedido, opcoes);
    var dados = resposta.Json();
    Console.WriteLine(dados.GetProperty("id").GetString());
}
catch (ErroMailcore erro)
{
    // Classifique erro.Codigo e erro.Status sem registrar a mensagem privada da API.
    Console.Error.WriteLine(erro.Message);
}
```

HTTP 202 registra a aceitação da intenção. Eventos e webhooks acompanham a entrega. Em uma aplicação de longa duração, mantenha a instância do cliente durante seu ciclo de vida e descarte-a no encerramento.

## Injeção de dependências e integrações

`AddMailcore` registra `Cliente` e `ICliente` com `IHttpClientFactory`. O [exemplo ASP.NET](exemplos/AspNet/README.md) demonstra uma Minimal API e um controller, com mensagens sintéticas na verificação. A aplicação escolhe sua autenticação, seu roteamento e a persistência das intenções.

O pacote opcional [Mailcore.FluentEmail](src/Mailcore.FluentEmail/README.md) integra o contrato `ISender` do FluentEmail. O SDK base pode ser usado sem essa dependência. Cada mensagem continua usando sua chave de idempotência estável; a integração não gera uma nova intenção automaticamente.

O pacote opcional [Mailcore.AspNetCore](src/Mailcore.AspNetCore/README.md) oferece `MapMailcoreWebhook`, autenticando os bytes originais antes de chamar a aplicação, com leitura limitada a 8 MiB. Os três pacotes usam a versão `0.1.0-alpha.1`; adicione as integrações opcionais conforme a necessidade da aplicação.

## Recursos, respostas e erros

`Cliente` oferece `Emails`, `Lotes`, `Dominios`, `ChavesApi`, `Contatos`, `Segmentos`, `Topicos`, `PropriedadesContatos`, `Modelos`, `Webhooks`, `Campanhas`, `Automacoes`, `Eventos`, `Recebimento`, `Caixas` e a gestão de concessões CLI. Recursos subordinados usam propriedades, como `Contatos.Exportacoes`, `Modelos.Versoes` e `Caixas.Conversas.Emails`.

Todos os métodos de operação terminam em `Async`, retornam `Task<Resposta>` e recebem opções e `CancellationToken` no final. Pedidos podem usar objetos serializáveis ou dicionários com os nomes dos campos do contrato HTTP. A [cobertura](docs/cobertura.md) lista as 151 operações.

`Resposta` mantém `Status`, `Cabecalhos`, `Dados` e `TotalBytes`. `Dados` retorna uma cópia dos bytes originais. `Json()` retorna um `JsonElement` independente de um documento que precise ser descartado. A escolha de `GetDouble`, `GetDecimal` ou de um DTO determina as conversões numéricas feitas pela aplicação; os bytes e `GetRawText()` permanecem disponíveis.

O comportamento padrão lança `ErroMailcore` para falhas da API ou do SDK. A exceção separa código, mensagem privada, status opcional e cabeçalhos; sua representação textual padrão omite a mensagem de negócio. Cancelamento solicitado pelo chamador é sinalizado por `OperationCanceledException`; prazo total excedido usa o código `prazo_excedido`.

As variantes de download terminadas em `FluxoAsync` entregam blocos ao consumidor, sem acumular o arquivo em `Dados`. O retorno conserva os metadados e a quantidade lida. O chamador controla a persistência do arquivo e o tratamento de um resultado parcial.

`Assinatura.Verificar` autentica webhooks com HMAC-SHA256 dos bytes originais, incluindo a janela temporal e os cabeçalhos assinados. A aplicação continua responsável por deduplicar o identificador do evento em armazenamento durável.

Consulte o [guia de uso](docs/uso.md) para configuração, opções, paginação, modelos, lotes, arquivos, idempotência e webhooks. Os ensaios locais não medem capacidade de entrega SMTP nem desempenho em produção.
