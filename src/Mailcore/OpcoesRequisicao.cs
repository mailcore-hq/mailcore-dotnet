namespace Mailcore;

/// <summary>Opções independentes da chamada. O token de cancelamento é um argumento próprio.</summary>
public sealed record OpcoesRequisicao
{
    public string? ChaveIdempotencia { get; init; }
    public TimeSpan? TempoLimite { get; init; }
    public long? LimiteRespostaBytes { get; init; }
    public IReadOnlyDictionary<string, IReadOnlyList<string>>? Cabecalhos { get; init; }
    public override string ToString() => "OpcoesRequisicao { Conteudo = protegido }";
}
