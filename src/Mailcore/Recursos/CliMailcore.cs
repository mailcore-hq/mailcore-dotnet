// Gerado por scripts/gerar_recursos.py a partir das operações públicas Mailcore.
using Mailcore.Interno;

namespace Mailcore.Recursos;

/// <summary>Operações de CliMailcore no contrato público Mailcore.</summary>
public sealed class CliMailcore
{
    /// <summary>Retorna as operações de concessoes.</summary>
    public ConcessoesCli Concessoes { get; }

    internal CliMailcore(Transporte transporte)
    {
        Concessoes = new ConcessoesCli(transporte);
    }
}
