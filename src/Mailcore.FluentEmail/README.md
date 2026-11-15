# Mailcore para FluentEmail

Pacote opcional `Mailcore.FluentEmail`, versão `0.1.0-alpha.1`. O SDK base não depende de FluentEmail. Esta integração usa `FluentEmail.Core` 3.0.2 e aceita HTML/texto alternativo, cópias, resposta, cabeçalhos, prioridade, tags e anexos/CID conforme o contrato Mailcore.

## Instalação

Para versões disponíveis no NuGet, instale o pacote na aplicação consumidora:

```sh
dotnet add package Mailcore.FluentEmail --version 0.1.0-alpha.1
```

## Uso

```csharp
using Mailcore;
using Mailcore.FluentEmail;
using Microsoft.Extensions.DependencyInjection;

var emissor = new EmissorMailcore(cliente, email => RecuperarChavePersistida(email));
email.Sender = emissor;
var resultado = await email.SendAsync(cancelamento);
```

`RecuperarChavePersistida` pertence ao fluxo da aplicação. Ele deve recuperar uma chave estável já associada à intenção, sem gerar outra a cada tentativa. Também há `emissor.EnviarAsync(email, chavePersistida, cancelamento)` para uma chave explícita. A integração não altera tags ou cabeçalhos para armazenar controles ocultos e não cria UUIDs automaticamente.

Para DI, registre `ICliente` com a extensão do SDK e encadeie `AddMailcore` no builder FluentEmail:

```csharp
services.AddMailcore(configuracao =>
    configuracao.ChaveApi = Environment.GetEnvironmentVariable("MAILCORE_API_KEY"));

services.AddFluentEmail("notificacoes@seudominio.com.br")
    .AddMailcore((servicos, email) => RecuperarChavePersistida(email));
```

O seletor deve ser seguro para o modo de concorrência usado pela aplicação. O emissor não compartilha estado mutável entre mensagens. As representações de erro do `SendResponse` são sanitizadas; cancelamento continua sendo `OperationCanceledException`.

O contrato `ISender` exige `Send` síncrono e `SendAsync`; prefira a variante assíncrona em aplicações. O núcleo Mailcore permanece assíncrono.

Destinatários, Cc/Cco e resposta são convertidos para endereços sem nome. O remetente mantém seu nome. Mailcore aceita um único endereço de resposta. Tags do FluentEmail têm somente nome e são enviadas com valor `"1"`. Anexos são limitados a dez, somando 5 MiB; dados são lidos com limite e sem descartar o stream da aplicação. Em streams reposicionáveis, a posição é restaurada. Streams não reposicionáveis são consumidos e precisam ser reconstruídos para uma nova tentativa. Um anexo inline exige `ContentId` explícito.

HTTP 202 confirma a aceitação da intenção. Acompanhe eventos ou webhooks para confirmar a entrega. Use o cliente Mailcore diretamente para modelos, agendamento e outras operações sem representação no contrato `IFluentEmail`.
