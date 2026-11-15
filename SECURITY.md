# Segurança

Use a biblioteca no servidor e mantenha a chave em variável de ambiente protegida ou fornecedor de credenciais seguro para concorrência. Não coloque chaves em código, URLs, repositórios ou mensagens públicas. Use escopos mínimos e uma chave por integração.

O transporte nativo verifica certificado, hostname e finalidade de servidor TLS. Não segue redirecionamentos, não usa proxy automático, não mantém cookies e não negocia compressão. HTTP exige opção explícita e se limita a loopback em testes. O cliente HTTP injetado pertence à aplicação, incluindo seus handlers e políticas de segurança; o SDK não altera suas propriedades nem o encerra.

Pedidos, respostas, webhooks e `ErroMailcore.Mensagem` podem conter dados pessoais. As representações textuais padrão omitem o conteúdo. A aplicação precisa tratar o que registra ou serializa; não retorne a exceção inteira ao navegador. O SDK não persiste mensagens, credenciais ou eventos. Armazenamento e criptografia em repouso pertencem à aplicação.

Verifique a assinatura do webhook sobre os bytes originais e deduplique o identificador em armazenamento durável. A verificação criptográfica não mantém estado contra repetição.

Comunique vulnerabilidades ao responsável pelo projeto em canal privado. Inclua versão e exemplo mínimo com dados sintéticos, sem publicar chaves, dados de clientes ou provas contra sistemas de terceiros.
