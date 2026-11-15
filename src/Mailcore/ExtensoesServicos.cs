using Mailcore.Interno;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Mailcore;

/// <summary>Integra o SDK à fábrica HTTP da aplicação sem configurar políticas de retentativa.</summary>
public static class ExtensoesServicos
{
    public static IHttpClientBuilder AddMailcore(this IServiceCollection servicos, Action<Configuracao>? configurar = null)
    {
        ArgumentNullException.ThrowIfNull(servicos);
        var configuracao = new Configuracao();
        configurar?.Invoke(configuracao);
        ConfiguracaoValidada.Validar(configuracao);
        var copia = configuracao.Copiar();
        var http = servicos.AddHttpClient("Mailcore", cliente => cliente.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => ConfiguracaoValidada.CriarHandler(copia))
            .RemoveAllLoggers();
        servicos.AddTransient<Cliente>(provedor =>
        {
            var opcoes = copia.Copiar();
            opcoes.CertificadoCa = null; // A CA pertence ao handler gerenciado pela fábrica.
            return new Cliente(provedor.GetRequiredService<IHttpClientFactory>().CreateClient("Mailcore"), configuracao: opcoes);
        });
        servicos.AddTransient<ICliente>(provedor => provedor.GetRequiredService<Cliente>());
        return http;
    }
}
