ARG IMAGEM_DOTNET=mcr.microsoft.com/dotnet/sdk:11.0.100-resolute@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea
FROM mcr.microsoft.com/dotnet/sdk:11.0.100-resolute@sha256:65f3c7c6a25a239ab3e6719f2896147e7b68242ff2d8818dd70498bde6ef8bea AS ferramentas
RUN apt-get update && apt-get install -y --no-install-recommends python3 openssl ca-certificates git unzip zip && rm -rf /var/lib/apt/lists/*
COPY --from=docker:29-cli@sha256:5a7af0dcb0e46c803efe9b45371dd21cb500e5b4453250394ac6f06d49b6e6f1 /usr/local/bin/docker /usr/local/bin/docker
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 NUGET_PACKAGES=/opt/nuget DOTNET_CLI_HOME=/tmp/dotnet
WORKDIR /sdk

FROM ferramentas AS dependencias
COPY Directory.Build.props NuGet.Config ./
COPY src/Mailcore/Mailcore.csproj src/Mailcore/packages.lock.json src/Mailcore/
COPY src/Mailcore.FluentEmail/Mailcore.FluentEmail.csproj src/Mailcore.FluentEmail/packages.lock.json src/Mailcore.FluentEmail/
COPY src/Mailcore.AspNetCore/Mailcore.AspNetCore.csproj src/Mailcore.AspNetCore/packages.lock.json src/Mailcore.AspNetCore/
COPY tests/Mailcore.Tests/Mailcore.Tests.csproj tests/Mailcore.Tests/packages.lock.json tests/Mailcore.Tests/
COPY testes/IntegracaoApi/IntegracaoApi.csproj testes/IntegracaoApi/packages.lock.json testes/IntegracaoApi/
RUN dotnet restore tests/Mailcore.Tests/Mailcore.Tests.csproj --configfile NuGet.Config --locked-mode
RUN dotnet restore testes/IntegracaoApi/IntegracaoApi.csproj --configfile NuGet.Config --locked-mode

FROM dependencias AS compilacao
COPY . .
RUN dotnet build tests/Mailcore.Tests/Mailcore.Tests.csproj --no-restore -c Release
RUN dotnet build testes/IntegracaoApi/IntegracaoApi.csproj --no-restore -c Release

FROM ${IMAGEM_DOTNET} AS testes
ARG FRAMEWORK=net11.0
RUN apt-get update && apt-get install -y --no-install-recommends python3 openssl ca-certificates git unzip zip && rm -rf /var/lib/apt/lists/*
COPY --from=ferramentas /usr/local/bin/docker /usr/local/bin/docker
COPY --from=compilacao /opt/nuget /opt/nuget
COPY --from=compilacao /sdk /sdk
WORKDIR /sdk
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1 NUGET_PACKAGES=/opt/nuget DOTNET_CLI_HOME=/tmp/dotnet
ENV MAILCORE_FRAMEWORK=${FRAMEWORK}
CMD ["python3", "scripts/executar_testes.py"]
