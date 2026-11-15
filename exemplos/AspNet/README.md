# Minimal API e controller ASP.NET

Exemplo compilável do cliente Mailcore com DI e `IHttpClientFactory`, usando uma Minimal API e um controller MVC. O projeto não cria uma tela do produto. A chave vem de `MAILCORE_API_KEY` no ambiente protegido do servidor.

As rotas demonstrativas `/exemplos/minimo` e `/exemplos/controlador` usam a mesma lógica de confirmação. Na aplicação real, os dados e a chave de idempotência devem vir de uma intenção persistida pelo fluxo autorizado. A aplicação define sua autenticação e seu roteamento. Uma resposta 202 confirma aceitação da intenção; acompanhe os eventos e webhooks para saber o resultado da entrega.

O exemplo referencia o checkout local do SDK para desenvolvimento conjunto. O SDK base suporta `AddMailcore` e configuração por chamada sem exigir FluentEmail.

Com ferramentas .NET disponíveis, compile a partir da raiz do SDK:

```bash
dotnet build exemplos/AspNet/Mailcore.Exemplo.AspNet.csproj -c Release
dotnet run --project exemplos/AspNet/Mailcore.Exemplo.AspNet.csproj -c Release --no-build -- --verificar-exemplo
```

Na verificação deste repositório, compilação e testes são feitos em Docker. `--verificar-exemplo` inicia um servidor somente no loopback do contêiner, exercita as duas rotas e usa transporte sintético no SDK. Não precisa de chave real, serviço externo, SMTP ou portas publicadas. O programa imprime um resultado JSON sanitizado e encerra.

O projeto usa .NET 10 por padrão e também pode ser compilado com `-p:TargetFramework=net11.0`. Os diretórios de saída e dependências do exemplo não pertencem ao pacote NuGet da biblioteca.

Para reproduzir com Docker a partir da raiz do SDK:

```bash
docker build -f exemplos/AspNet/Dockerfile -t mailcore/dotnet-aspnet:10 .
docker run --rm --network none --read-only --tmpfs /tmp:rw,nosuid,size=64m mailcore/dotnet-aspnet:10
```

O build usa o SDK e o runtime fixados por digest, com as dependências do lock local. A execução de verificação não precisa publicar portas e imprime JSON com dois cenários aprovados.
