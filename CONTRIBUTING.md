# Contribuir

Código e documentação usam português do Brasil, preservando nomes obrigatórios de APIs e protocolos. Confira as rotas e DTOs públicos do backend antes de alterar um recurso. Não introduza endpoints por inferência.

O desenvolvimento usa Docker; SDK .NET e ferramentas NuGet permanecem nos contêineres. Verifique a geração com `python3 scripts/gerar_recursos.py --verificar`. A fixture de contratos é independente do gerador e deve acompanhar alterações comprovadas na API.

Compile com avisos tratados como erros e execute os testes em cada perfil. Os ensaios usam dados sintéticos, TLS privado e rede sem acesso externo. Não configure chaves reais nesses testes. Preserve cancelamento, limites, propriedade dos recursos, precisão dos dados e privacidade dos erros.

Consulte [a verificação](docs/verificacao.md) para matriz, pacote local e API isolada. Não publique artefatos nesta etapa.
