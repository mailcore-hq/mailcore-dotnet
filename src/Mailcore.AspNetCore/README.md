# Webhooks Mailcore em ASP.NET Core

Pacote opcional `Mailcore.AspNetCore`, versão `0.1.0-alpha.1`. Ele usa o SDK base e o framework compartilhado ASP.NET Core; o núcleo `Mailcore` continua independente de aplicações web.

## Instalação

Para versões disponíveis no NuGet, instale o pacote na aplicação consumidora:

```sh
dotnet add package Mailcore.AspNetCore --version 0.1.0-alpha.1
```

## Uso

```csharp
using Mailcore.AspNetCore;

app.MapMailcoreWebhook(
    "/webhooks/email",
    segredoWebhook,
    async (evento, contexto, cancelamento) =>
    {
        await ProcessarEventoAsync(evento, contexto, cancelamento);
    });
```

`segredoWebhook` pertence ao ambiente protegido da aplicação. `ProcessarEventoAsync` representa seu processamento; a aplicação escolhe autorização, persistência e roteamento. A extensão retorna `RouteHandlerBuilder`, permitindo configurar metadados e políticas do endpoint explicitamente.

O endpoint aceita POST, lê os bytes originais de forma assíncrona e autentica a assinatura antes de chamar a aplicação. O limite é 8 MiB, tanto para `Content-Length` conhecido como durante a leitura em fluxo. Não há leitura ilimitada, gravação automática em disco ou registro de corpo, segredo ou cabeçalhos pelo SDK.

Uma assinatura inválida retorna HTTP 400 com código genérico `assinatura_invalida`. Corpo acima do limite retorna HTTP 413 com `corpo_excedido`. O callback concluído com sucesso resulta em HTTP 204. Falhas do processamento da aplicação continuam na pipeline ASP.NET, sem serem transformadas em erros de assinatura.

O callback recebe `HttpContext` e `RequestAborted`; aguarde as operações e respeite o token. O SDK não força o encerramento de código da aplicação que ignore cancelamento. Ele também não deduplica nem mantém estado dos eventos: use `svix-id` em armazenamento durável e decida como confirmar o processamento no seu fluxo.

`OpcoesAssinatura` permite reduzir a tolerância temporal e injetar o relógio em testes. A tolerância padrão e máxima é de cinco minutos. A validação criptográfica do segredo, Base64 e cabeçalhos é feita pelo verificador do SDK; segredos inválidos nunca chegam ao callback.
