namespace Mailcore;

/// <summary>Contrato de recursos para injeção de dependências.</summary>
public interface ICliente : IDisposable
{
    Recursos.Emails Emails { get; }
    Recursos.Lotes Lotes { get; }
    Recursos.Dominios Dominios { get; }
    Recursos.ChavesApi ChavesApi { get; }
    Recursos.Contatos Contatos { get; }
    Recursos.Segmentos Segmentos { get; }
    Recursos.Topicos Topicos { get; }
    Recursos.PropriedadesContatos PropriedadesContatos { get; }
    Recursos.Campanhas Campanhas { get; }
    Recursos.Modelos Modelos { get; }
    Recursos.Automacoes Automacoes { get; }
    Recursos.Webhooks Webhooks { get; }
    Recursos.Recebimento Recebimento { get; }
    Recursos.Caixas Caixas { get; }
    Recursos.Eventos Eventos { get; }
    Recursos.CliMailcore Cli { get; }
}
