FROM mcr.microsoft.com/dotnet/sdk:11.0.100-resolute@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS compilacao
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 NUGET_PACKAGES=/opt/nuget
WORKDIR /sdk
COPY . .
RUN dotnet restore exemplos/AspNet/Mailcore.Exemplo.AspNet.csproj --locked-mode
RUN dotnet publish exemplos/AspNet/Mailcore.Exemplo.AspNet.csproj --no-restore -c Release -o /opt/exemplo

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15-noble@sha256:bfd912c83295ce66d4b706ebb8541b9f14259b0e76fbb88b2751f58ce2399d2e
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1
WORKDIR /app
COPY --from=compilacao /opt/exemplo .
USER $APP_UID
ENTRYPOINT ["dotnet", "Mailcore.Exemplo.AspNet.dll"]
CMD ["--verificar-exemplo"]
