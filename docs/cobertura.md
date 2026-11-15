# Cobertura do SDK .NET

A versão `0.1.0-alpha.1` oferece **16 recursos principais, 30 classes de recursos e 151 operações assíncronas**. O pacote principal é `Mailcore`. Targets: `net8.0`, `net10.0` e `net11.0`; a validação também usa .NET 9 consumindo o target compatível `net8.0`.

As classes pertencem a `Mailcore.Recursos`. Propriedades subordinadas reutilizam o transporte do cliente. Conveniências de argumentos opcionais não representam operações adicionais.

| Classe | Métodos | Operações |
| --- | --- | ---: |
| `Emails` | `EnviarAsync`, `ListarAsync`, `ObterAsync`, `ReagendarAsync`, `CancelarAsync`, `ObterPorIdempotenciaAsync`, `EventosAsync`, `BaixarAnexoAsync` | 8 |
| `Lotes` | `EnviarAsync`, `ObterPorIdempotenciaAsync` | 2 |
| `Dominios` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync`, `RegistrosDnsAsync`, `VerificarAsync` | 7 |
| `ChavesApi` | `CriarAsync`, `ListarAsync`, `RenomearAsync`, `RevogarAsync` | 4 |
| `SegmentosContato` | `ListarAsync`, `AdicionarAsync`, `RemoverAsync` | 3 |
| `TopicosContato` | `ListarAsync`, `AtualizarAsync` | 2 |
| `ImportacoesContatos` | `PreverAsync`, `CriarAsync`, `ObterAsync` | 3 |
| `ExportacoesContatos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `BaixarAsync`, `BaixarFluxoAsync` | 5 |
| `Contatos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync`, `AdicionarEmLoteAsync`, `AlterarEmLoteAsync` | 7 |
| `Segmentos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `Topicos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `PropriedadesContatos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `VersoesModelo` | `ListarAsync`, `ObterAsync`, `RestaurarAsync`, `PreverAsync` | 4 |
| `PastasModelo` | `ListarAsync`, `CriarAsync`, `AtualizarAsync`, `RemoverAsync` | 4 |
| `Modelos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync`, `PreverAsync`, `ObterPreviaAsync`, `TestarAsync`, `PublicarAsync`, `DuplicarAsync` | 10 |
| `EventosWebhook` | `ListarAsync`, `ObterAsync`, `ListarTentativasAsync`, `ReenviarAsync` | 4 |
| `Webhooks` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `Campanhas` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync`, `DuplicarAsync`, `RevisarAsync`, `EnviarAsync`, `CancelarAsync`, `PreverAsync`, `TestarAsync`, `LerPreviaAsync`, `ObterMetricasAsync`, `ListarDestinatariosAsync`, `ListarLinksClicadosAsync` | 15 |
| `ExecucoesAutomacoes` | `ListarAsync`, `ObterAsync` | 2 |
| `Automacoes` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync`, `DuplicarAsync`, `PararAsync` | 7 |
| `Eventos` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync`, `EnviarAsync` | 6 |
| `Recebimento` | `ListarAsync`, `ObterAsync`, `ListarAnexosAsync`, `ObterAnexoAsync`, `BaixarOriginalAsync`, `BaixarAnexoAsync`, `BaixarOriginalFluxoAsync`, `BaixarAnexoFluxoAsync` | 8 |
| `EmailsCaixa` | `ListarAsync`, `ObterAsync`, `ResponderAsync`, `EncaminharAsync` | 4 |
| `EtiquetasConversaCaixa` | `AdicionarAsync`, `SubstituirAsync`, `RemoverAsync` | 3 |
| `ConversasCaixa` | `ListarAsync`, `BuscarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `EtiquetasCaixa` | `ListarAsync`, `ObterAsync`, `CriarAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `RascunhosCaixa` | `ListarAsync`, `ObterAsync`, `CriarAsync`, `AtualizarAsync`, `RemoverAsync`, `EnviarAsync` | 6 |
| `Caixas` | `CriarAsync`, `ListarAsync`, `ObterAsync`, `AtualizarAsync`, `RemoverAsync` | 5 |
| `ConcessoesCli` | `ListarAsync`, `RevogarAsync` | 2 |
| `CliMailcore` | Agrega `ConcessoesCli`. | 0 |
| **Total** | **151 operações** | **151** |

## Contrato HTTP

Pedidos mantêm os nomes dos campos públicos do Mailcore. A serialização distingue objetos e arrays e preserva `null` e `false` dentro do corpo. Filtros nulos são omitidos da query. Identificadores são escapados como um único segmento de URI, preservando o prefixo da origem.

As operações retornam `Task<Resposta>` e aceitam `OpcoesRequisicao` e `CancellationToken` no final. O comportamento padrão sinaliza falhas por `ErroMailcore`. O cancelamento solicitado pelo chamador usa `OperationCanceledException`. Respostas conservam status, cabeçalhos e bytes originais; escolhas de conversão numérica pertencem à aplicação.

O SDK não cria funcionalidades ausentes no servidor. Permissões, escopos, revisões, estados e validações de negócio permanecem na API. HTTP 202 confirma aceitação da intenção, não a entrega SMTP.

## Composição e processamento

O envio direto permite HTML/texto, Cc/Cco, cabeçalhos e anexos Base64/CID. O envio individual por modelo aceita anexos Base64 sem CID e não combina HTML/texto, Cc/Cco, cabeçalhos ou nome de exibição no remetente. Lotes são atômicos, contêm de 1 a 100 mensagens e não aceitam anexos, Cc/Cco, cabeçalhos ou nome de exibição.

Importações usam CSV UTF-8 no JSON; o limite de negócio é 8 MiB. O limite geral de serialização não amplia limites menores dos endpoints. O SDK não processa importações, envia SMTP ou executa workers.

Cada listagem consulta uma página. O SDK conserva o envelope e o cursor recebido e não percorre páginas automaticamente. Conflitos de revisão não são resolvidos alterando ou reaplicando silenciosamente o pedido.

## Downloads e gestão

Os métodos `BaixarFluxoAsync`, `BaixarOriginalFluxoAsync` e `BaixarAnexoFluxoAsync` fornecem blocos ao consumidor. O resultado conserva metadados e o total de bytes, com `Dados` vazio. O consumidor controla persistência e tratamento de arquivos parciais. Downloads comuns acumulam bytes em memória.

URLs temporárias são dados sensíveis; o SDK não segue URLs de metadados enviando credenciais. Webhooks de recebimento não são busca automática de conteúdo remoto. Concessões CLI permitem consultar e revogar, sem iniciar login interativo ou renovar credenciais.

O contrato foi conferido com o fingerprint backend `d31c825268a64c858276827f47d27e506f543421327f487d7165e511359e248f`. Os testes de transporte usam fixture independente por AST do SDK Node; os testes com API real verificam cenários de persistência separadamente.

As integrações opcionais `Mailcore.FluentEmail` e `Mailcore.AspNetCore` não acrescentam operações à contagem de 151. A primeira adapta o contrato `ISender`; a segunda mapeia webhooks ASP.NET com autenticação dos bytes originais, leitura limitada a 8 MiB e cancelamento por `RequestAborted`. As integrações opcionais são pacotes NuGet separados e mantêm o contrato do SDK base.

Consulte [uso](uso.md) e [verificação](verificacao.md) para exemplos, limitações e evidências.
