namespace Mailcore;

public sealed record OpcoesAssinatura
{
    public DateTimeOffset? Agora { get; init; }
    public TimeSpan Tolerancia { get; init; } = TimeSpan.FromMinutes(5);
}
