namespace Mailcore;

/// <summary>Cliente assíncrono compartilhável; feche ao encerrar o ciclo de vida da aplicação.</summary>
public sealed class Cliente : ICliente
{
    private readonly Interno.Transporte _transporte;
    public Recursos.Emails Emails { get; }
    public Recursos.Lotes Lotes { get; }
    public Recursos.Dominios Dominios { get; }
    public Recursos.ChavesApi ChavesApi { get; }
    public Recursos.Contatos Contatos { get; }
    public Recursos.Segmentos Segmentos { get; }
    public Recursos.Topicos Topicos { get; }
    public Recursos.PropriedadesContatos PropriedadesContatos { get; }
    public Recursos.Campanhas Campanhas { get; }
    public Recursos.Modelos Modelos { get; }
    public Recursos.Automacoes Automacoes { get; }
    public Recursos.Webhooks Webhooks { get; }
    public Recursos.Recebimento Recebimento { get; }
    public Recursos.Caixas Caixas { get; }
    public Recursos.Eventos Eventos { get; }
    public Recursos.CliMailcore Cli { get; }

    public Cliente(string? chave = null, Configuracao? configuracao = null)
        : this(new Interno.Transporte(chave, configuracao)) { }
    public Cliente(Configuracao configuracao) : this(null, configuracao) { }
    public Cliente(HttpClient http, string? chave = null, Configuracao? configuracao = null)
        : this(new Interno.Transporte(chave, configuracao, http ?? throw ErroMailcore.Configuracao("Informe o cliente HTTP."))) { }
    private Cliente(Interno.Transporte transporte)
    {
        _transporte = transporte;
        Emails = new Recursos.Emails(transporte);
        Lotes = new Recursos.Lotes(transporte);
        Dominios = new Recursos.Dominios(transporte);
        ChavesApi = new Recursos.ChavesApi(transporte);
        Contatos = new Recursos.Contatos(transporte);
        Segmentos = new Recursos.Segmentos(transporte);
        Topicos = new Recursos.Topicos(transporte);
        PropriedadesContatos = new Recursos.PropriedadesContatos(transporte);
        Campanhas = new Recursos.Campanhas(transporte);
        Modelos = new Recursos.Modelos(transporte);
        Automacoes = new Recursos.Automacoes(transporte);
        Webhooks = new Recursos.Webhooks(transporte);
        Recebimento = new Recursos.Recebimento(transporte);
        Caixas = new Recursos.Caixas(transporte);
        Eventos = new Recursos.Eventos(transporte);
        Cli = new Recursos.CliMailcore(transporte);
    }
    public void Dispose() => _transporte.Dispose();
    public override string ToString() => "Cliente { Credenciais = protegidas }";
}
