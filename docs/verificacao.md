# Verificação

O pacote base e os complementos oferecem targets `net8.0`, `net10.0` e `net11.0`. A matriz executa em SDKs/runtimes .NET 8, 9, 10 e 11; o consumidor .NET 9 usa o target `net8.0`. As imagens Microsoft estão fixadas por digest em `testes/imagens.json`.

Em 14/11/2026, **217 testes passaram em cada um dos quatro perfis**, totalizando 868 execuções sem falhas ou casos ignorados. O [relatório da matriz](matriz-dotnet.json) registra SDKs e runtimes efetivamente utilizados.

As versões .NET 10 e 11 têm suporte Microsoft ativo nesta data. .NET 8/9 são verificadas para compatibilidade; seu suporte Microsoft terminou em 10/11/2026. Um target do SDK não prolonga o ciclo de segurança do runtime. Consulte a [política oficial .NET](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## Matriz em Docker

```bash
python3 scripts/verificar.py
```

Use `--perfis 11` para um perfil durante o desenvolvimento. A biblioteca é compilada com SDK 11, C# 12, nullable habilitado, avisos tratados como erros e dependências NuGet bloqueadas. Cada imagem final contém o SDK/runtime correspondente e executa seus próprios processos de teste.

Os testes usam rede `none`, raiz somente para leitura, limites de memória/processos e diretório temporário próprio. Elaboração do pacote, compilação e testes não instalam SDK .NET no host. O script confirma os totais do arquivo TRX antes de registrar uma aprovação.

Os casos cobrem:

- 151 operações por HTTP local real, com fixture independente do gerador: verbos, caminhos, corpos, filtros, opções e respostas.
- TLS com CA, hostname e finalidade `serverAuth`; certificado restrito a `clientAuth` é recusado antes de enviar HTTP.
- Prazos totais, cancelamento com token original, encerramento de chamadas e descarte de respostas/streams tardios de provedores que ignoram cancelamento.
- Bytes originais, precisão JSON, UTF-8/UTF-16, números não finitos, limites de corpo/cabeçalhos e respostas truncadas.
- Downloads progressivos de 8 MiB, retenção dos blocos, falhas do consumidor, pooling e oito operações concorrentes.
- Idempotência sem fila de retentativas do SDK, HTTP 503 e queda após recebimento da intenção, sem reemissão observada nesses casos.
- Propriedade e imutabilidade das configurações, erros sanitizados, modo sem exceções e `IHttpClientFactory`.
- Oito testes de HMAC, 15 de FluentEmail e oito grupos Kestrel para webhooks ASP.NET, além dos contratos e protocolo do núcleo.

O transporte nativo negocia HTTP/2 quando disponível e admite HTTP/1.1. A fixture sintética HTTPS fala HTTP/1.1. Retentativas internas de transporte antes da aceitação seguem as regras da plataforma; não há retentativa de negócio. Falha ou cancelamento não comprova rejeição de uma intenção pelo servidor.

## Pacotes e consumidores offline

Os três pacotes locais são `Mailcore`, `Mailcore.FluentEmail` e `Mailcore.AspNetCore`, versão `0.1.0-alpha.1`. Cada um tem `.nupkg` e `.snupkg`. A biblioteca base funciona sem os complementos.

```bash
mkdir -p dist/pacotes
docker run --rm --network none \
  --mount type=bind,src="$(pwd)/dist/pacotes",dst=/pacotes \
  mailcore/dotnet-testes:11 python3 scripts/preparar_distribuicao.py
```

O preparador constrói e inspeciona os arquivos NuGet, DLLs, XML de documentação, símbolos e metadados. Preserva a licença MIT e os avisos das dependências públicas. O código-fonte próprio é incorporado aos símbolos para depuração sem depender de um repositório remoto fictício. Dependências permanecem separadas das DLLs do SDK.

Para instalar os pacotes efetivos em projetos fora do checkout:

```bash
mkdir -p dist/instalacao
docker run --rm --network none \
  --tmpfs /tmp:rw,exec,nosuid,size=768m \
  --mount type=bind,src="$(pwd)/dist/pacotes",dst=/pacotes,readonly \
  --mount type=bind,src="$(pwd)/dist/instalacao",dst=/relatorios \
  mailcore/dotnet-testes:11 python3 scripts/instalar_consumidor.py \
  --perfil 11 --relatorio /relatorios/instalacao-dotnet-11.json
```

Repita com imagens e perfis 8, 9 e 10. Cada consumidor usa diretório NuGet novo e configuração que aponta somente para o feed local. Não utiliza configurações ou credenciais NuGet do host. O código é compilado e executado com o SDK/runtime real de cada perfil; o ensaio verifica também a DLL escolhida pelo NuGet.

A [instalação offline](instalacao-local.json) passou nos quatro perfis com os três pacotes. O [registro de distribuição](distribuicao.json) contém seus checksums. Os [27 arquivos DLL/PDB/XML](conteudo-compilado.json) dos targets dos três pacotes são idênticos aos compilados presentes nas quatro imagens finais.

O consumidor base exercita 30 recursos, 151 operações, três fluxos e casos de HMAC/JSON/cancelamento com handler sintético. Consumidores adicionais instalam os complementos por `PackageReference`: um envio FluentEmail sintético e um endpoint ASP.NET autenticado/inválido, sem iniciar servidor. Esses casos comprovam instalação; os contratos de rede pertencem à suíte HTTP e à integração seguinte.

## API e PostgreSQL isolados

O [relatório de API](integracao-api.json) confirma dez grupos contra o backend real, quatro downloads em fluxo, oito leituras concorrentes, 23 migrações e TLS verificado na API e no PostgreSQL (`verify-full`). A persistência sintética foi de três mensagens, três tarefas de envio e um recebido pendente. Não há worker, SMTP, banco de produção, deploy ou portas publicadas.

O controlador precisa acessar o daemon Docker para criar e remover os recursos exclusivos do ensaio. Exemplo para mantenedores com acesso ao backend, runtime local da API e cache público Cargo previamente preparados:

```bash
# Execute na raiz deste SDK, com o backend em ../../mailcore.
diretorio_sdk="$(pwd)"
diretorio_backend="$(cd ../../mailcore && pwd)"
diretorio_cache_cargo="${CARGO_HOME:-$HOME/.cargo}/registry"

docker run --rm --stop-timeout 30 --network none \
  --mount type=bind,src=/var/run/docker.sock,dst=/var/run/docker.sock \
  --mount "type=bind,src=$diretorio_backend,dst=$diretorio_backend,readonly" \
  --mount "type=bind,src=$diretorio_sdk,dst=$diretorio_sdk" \
  --workdir "$diretorio_sdk" \
  --entrypoint python3 mailcore/dotnet-testes:11 testes/controlar_api.py \
  --backend "$diretorio_backend" \
  --imagem-cliente mailcore/dotnet-testes:11 \
  --framework-cliente net11.0 \
  --cache-cargo "$diretorio_cache_cargo"
```

Os binds têm os mesmos caminhos no host e no controlador porque o daemon resolve os contêineres filhos. O cache Cargo é público e montado para leitura, sem credenciais. Os relatórios identificam o fingerprint dos fontes, o binário utilizado e a limpeza.

O [ensaio de interrupção intencional](integracao-api-interrupcao.json) enviou SIGTERM depois de observar PostgreSQL, guardião, rede interna e volume temporário. A listagem posterior do daemon confirmou zero recursos remanescentes. O resultado de operação interrompida nesse relatório é esperado.

## ASP.NET e limites da evidência

O [exemplo ASP.NET](../exemplos/AspNet/README.md) compila Minimal API e MVC. Seus dois cenários usam DI e o SDK real com handler sintético, sem SMTP ou portas públicas. O [relatório do exemplo](../exemplos/AspNet/verificacao.json) registra as ferramentas.

Esses ensaios verificam integração, protocolo, instalação e aceitação/persistência na API. Não medem entrega SMTP nem estabelecem equivalência de desempenho em produção. Os pacotes continuam alpha locais, sem publicação no NuGet.
