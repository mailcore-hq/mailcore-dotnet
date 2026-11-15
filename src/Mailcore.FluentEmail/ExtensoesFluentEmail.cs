using FluentEmail.Core;
using FluentEmail.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Mailcore.FluentEmail;

/// <summary>Registro opcional do emissor na cadeia FluentEmail.</summary>
public static class ExtensoesFluentEmail
{
    /// <summary>Usa o ICliente já registrado e uma chave persistida para cada mensagem.</summary>
    public static FluentEmailServicesBuilder AddMailcore(this FluentEmailServicesBuilder construtor,
        Func<IServiceProvider, IFluentEmail, string> obterChaveIdempotencia)
    {
        ArgumentNullException.ThrowIfNull(construtor);
        ArgumentNullException.ThrowIfNull(obterChaveIdempotencia);
        construtor.Services.AddTransient<ISender>(servicos => new EmissorMailcore(
            servicos.GetRequiredService<ICliente>(), email => obterChaveIdempotencia(servicos, email)));
        return construtor;
    }
}
