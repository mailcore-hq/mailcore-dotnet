namespace Mailcore;

/// <summary>Erro sanitizado; Mensagem expõe o detalhe de negócio somente quando consultada.</summary>
public sealed class ErroMailcore : Exception
{
    public string Codigo { get; }
    public string Mensagem { get; }
    public int? Status { get; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Cabecalhos { get; }

    public ErroMailcore(string codigo, string mensagem, int? status = null,
        IReadOnlyDictionary<string, IReadOnlyList<string>>? cabecalhos = null)
        : base(status is null ? "Mailcore: não foi possível concluir a operação."
            : $"Mailcore: operação recusada (HTTP {status}).")
    {
        Codigo = codigo; Mensagem = mensagem; Status = status;
        Cabecalhos = Interno.Cabecalhos.Copiar(cabecalhos);
    }

    public static ErroMailcore Configuracao(string mensagem) => new("configuracao_invalida", mensagem);
}
