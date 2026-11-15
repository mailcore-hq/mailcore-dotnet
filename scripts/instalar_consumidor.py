#!/usr/bin/env python3
"""Restaura e executa consumidor NuGet separado usando somente o feed local."""

import argparse
from datetime import datetime, timezone
import hashlib
import json
import os
from pathlib import Path
import shutil
import subprocess
import sys
import tempfile

from preparar_distribuicao import PACOTE, VERSAO, exigir, inventariar, validar_pacote

SDK = Path(__file__).resolve().parents[1]
PERFIS = {"8", "9", "10", "11"}
ETAPA = "configuracao"


def executar(argumentos, ambiente, pasta, prazo=300):
    retorno = subprocess.run(argumentos, cwd=pasta, env=ambiente, stdout=subprocess.PIPE,
                             stderr=subprocess.PIPE, check=False, timeout=prazo)
    exigir(retorno.returncode == 0)
    return retorno.stdout


def salvar(destino, resultado):
    destino.parent.mkdir(parents=True, exist_ok=True)
    with os.fdopen(os.open(destino, os.O_WRONLY | os.O_CREAT | os.O_TRUNC | os.O_NOFOLLOW, 0o644), "w") as arquivo:
        json.dump(resultado, arquivo, ensure_ascii=False, indent=2)
        arquivo.write("\n")


def instalar(artefatos, perfil):
    global ETAPA
    ETAPA = "verificar_manifesto"
    manifesto = json.loads((artefatos / "manifesto.json").read_bytes())
    exigir(manifesto["pacote"] == PACOTE and manifesto["versao"] == VERSAO and manifesto["publicado"] is False)
    exigir(manifesto["dependencias_fora_dll_sdk"] and manifesto["licenca_sdk_preservada"])
    for tipo, item in manifesto["artefatos"].items():
        caminho = (artefatos / item["arquivo"]).resolve()
        exigir(caminho.is_relative_to(artefatos) and hashlib.sha256(caminho.read_bytes()).hexdigest() == item["sha256"])
        dados = validar_pacote(caminho, simbolos=tipo == "simbolos")
        esperado = manifesto["arquivos_simbolos"] if tipo == "simbolos" else manifesto["arquivos_sdk"]
        exigir(dados["arquivos"] == esperado)
    for identidade, dados_pacote in manifesto.get("pacotes", {}).items():
        for tipo, item in dados_pacote["artefatos"].items():
            caminho = (artefatos / item["arquivo"]).resolve()
            exigir(caminho.is_relative_to(artefatos) and hashlib.sha256(caminho.read_bytes()).hexdigest() == item["sha256"])
            dados = validar_pacote(caminho, simbolos=tipo == "simbolos", identidade=identidade)
            esperado = dados_pacote["arquivos_simbolos"] if tipo == "simbolos" else dados_pacote["arquivos_sdk"]
            exigir(dados["arquivos"] == esperado)
    digest_feed, arquivos_feed = inventariar(artefatos / "feed")
    exigir(digest_feed == manifesto["sha256_feed"] and arquivos_feed == manifesto["arquivos_feed"])
    with tempfile.TemporaryDirectory(prefix="mailcore-dotnet-consumidor-") as temporario:
        pasta = Path(temporario)
        ambiente = os.environ.copy()
        ambiente.update({"DOTNET_CLI_TELEMETRY_OPTOUT": "1", "DOTNET_NOLOGO": "1",
                         "DOTNET_SKIP_FIRST_TIME_EXPERIENCE": "1", "DOTNET_CLI_HOME": str(pasta / "cli"),
                         "NUGET_PACKAGES": str(pasta / "nuget"), "MAILCORE_FIXTURE": str(pasta / "contratos.json"),
                         "MAILCORE_CONSUMIDOR": str(pasta)})
        for nome in ("DOTNET_ROLL_FORWARD", "DOTNET_STARTUP_HOOKS", "MSBuildSDKsPath", "MSBUILD_EXE_PATH"):
            ambiente.pop(nome, None)
        shutil.copyfile(SDK / "tests/Mailcore.Tests/Fixtures/contratos-recursos.json", pasta / "contratos.json")
        shutil.copyfile(SDK / "testes/ConsumidorInstalado/Program.cs", pasta / "Program.cs")
        (pasta / "ConsumidorInstalado.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType><UseAppHost>false</UseAppHost><TargetFramework>net{perfil}.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
    <TreatWarningsAsErrors>true</TreatWarningsAsErrors><CheckEolTargetFramework>false</CheckEolTargetFramework>
    <NuGetAudit>false</NuGetAudit><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup>
  <ItemGroup><PackageReference Include="{PACOTE}" Version="{VERSAO}" /></ItemGroup>
</Project>
''')
        config = pasta / "NuGet.Config"
        # Escapa caminhos XML sem acrescentar endpoints remotos ou credenciais.
        from xml.sax.saxutils import escape
        feed = escape(str(artefatos / "feed"), {'"': '&quot;'})
        config.write_text(f'''<?xml version="1.0" encoding="utf-8"?>
<configuration><packageSources><clear /><add key="local" value="{feed}" /></packageSources><disabledPackageSources><clear /></disabledPackageSources></configuration>''')
        ETAPA = "resolver_nuget_offline"
        executar(["dotnet", "restore", "--configfile", str(config), "--packages", str(pasta / "nuget"),
                  "--use-lock-file", "--force-evaluate"], ambiente, pasta)
        executar(["dotnet", "restore", "--locked-mode", "--configfile", str(config), "--packages", str(pasta / "nuget")], ambiente, pasta)
        ETAPA = "compilar_consumidor"
        executar(["dotnet", "build", "--no-restore", "-c", "Release"], ambiente, pasta)
        ETAPA = "executar_consumidor"
        dll = pasta / "bin/Release" / ("net" + perfil + ".0") / "ConsumidorInstalado.dll"
        exigir(dll.is_file())
        resultado = json.loads(executar(["dotnet", str(dll)], ambiente, pasta))
        exigir(resultado.get("resultado") == "aprovado" and resultado.get("recursos") == 30 and resultado.get("metodos") == 151)
        exigir(resultado.get("callbacks_fluxo") == 3 and resultado.get("requisicoes_http") == 155 and resultado.get("rede_real") is False)
        exigir(resultado.get("origem_dll_fora_checkout") is True)
        exigir(resultado.get("dotnet", "").split(".", 1)[0] == perfil)
        exigir(resultado.get("framework_biblioteca") == ("net8.0" if perfil == "9" else "net" + perfil + ".0"))
        if "Mailcore.FluentEmail" in manifesto.get("pacotes", {}):
            ETAPA = "consumidor_fluentemail"
            opcional = pasta / "opcional"
            opcional.mkdir()
            shutil.copyfile(SDK / "testes/ConsumidorFluentEmail/Program.cs", opcional / "Program.cs")
            (opcional / "ConsumidorFluentEmail.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><UseAppHost>false</UseAppHost><TargetFramework>net{perfil}.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <CheckEolTargetFramework>false</CheckEolTargetFramework><NuGetAudit>false</NuGetAudit><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup><ItemGroup><PackageReference Include="Mailcore.FluentEmail" Version="{VERSAO}" /></ItemGroup>
</Project>''')
            executar(["dotnet", "restore", "--configfile", str(config), "--packages", str(pasta / "nuget"), "--use-lock-file"], ambiente, opcional)
            executar(["dotnet", "restore", "--locked-mode", "--configfile", str(config), "--packages", str(pasta / "nuget")], ambiente, opcional)
            executar(["dotnet", "build", "--no-restore", "-c", "Release"], ambiente, opcional)
            adicional = json.loads(executar(["dotnet", str(opcional / "bin/Release" / ("net" + perfil + ".0") / "ConsumidorFluentEmail.dll")], ambiente, opcional))
            exigir(adicional["resultado"] == "aprovado" and adicional["origem_dll_fora_checkout"] and adicional["requisicoes_http"] == 1 and adicional["rede_real"] is False)
            exigir(adicional["dotnet"].split(".", 1)[0] == perfil and adicional["framework_biblioteca"] == resultado["framework_biblioteca"])
            adicional["lock_consumidor_sha256"] = hashlib.sha256((opcional / "packages.lock.json").read_bytes()).hexdigest()
            resultado["fluentemail_pacote_instalado"] = adicional
            resultado["sha256_fluentemail"] = manifesto["pacotes"]["Mailcore.FluentEmail"]["artefatos"]["pacote"]["sha256"]
        if "Mailcore.AspNetCore" in manifesto.get("pacotes", {}):
            ETAPA = "consumidor_aspnetcore"
            opcional = pasta / "aspnetcore"
            opcional.mkdir()
            shutil.copyfile(SDK / "testes/ConsumidorAspNetCore/Program.cs", opcional / "Program.cs")
            (opcional / "ConsumidorAspNetCore.csproj").write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup><OutputType>Exe</OutputType><UseAppHost>false</UseAppHost><TargetFramework>net{perfil}.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><TreatWarningsAsErrors>true</TreatWarningsAsErrors>
    <CheckEolTargetFramework>false</CheckEolTargetFramework><NuGetAudit>false</NuGetAudit><RestorePackagesWithLockFile>true</RestorePackagesWithLockFile>
  </PropertyGroup><ItemGroup><PackageReference Include="Mailcore.AspNetCore" Version="{VERSAO}" /></ItemGroup>
</Project>''')
            executar(["dotnet", "restore", "--configfile", str(config), "--packages", str(pasta / "nuget"), "--use-lock-file"], ambiente, opcional)
            executar(["dotnet", "restore", "--locked-mode", "--configfile", str(config), "--packages", str(pasta / "nuget")], ambiente, opcional)
            executar(["dotnet", "build", "--no-restore", "-c", "Release"], ambiente, opcional)
            adicional = json.loads(executar(["dotnet", str(opcional / "bin/Release" / ("net" + perfil + ".0") / "ConsumidorAspNetCore.dll")], ambiente, opcional))
            exigir(adicional["resultado"] == "aprovado" and adicional["origem_dll_fora_checkout"] and adicional["webhook_valido"] and adicional["corpo_adulterado_recusado"] and adicional["rede_real"] is False)
            exigir(adicional["dotnet"].split(".", 1)[0] == perfil and adicional["framework_biblioteca"] == resultado["framework_biblioteca"])
            adicional["lock_consumidor_sha256"] = hashlib.sha256((opcional / "packages.lock.json").read_bytes()).hexdigest()
            resultado["aspnetcore_pacote_instalado"] = adicional
            resultado["sha256_aspnetcore"] = manifesto["pacotes"]["Mailcore.AspNetCore"]["artefatos"]["pacote"]["sha256"]
        resultado.update({"data_utc": datetime.now(timezone.utc).isoformat(), "perfil": perfil,
                          "pacote": PACOTE, "versao": VERSAO,
                          "sha256_pacote": manifesto["artefatos"]["pacote"]["sha256"],
                          "sha256_simbolos": manifesto["artefatos"]["simbolos"]["sha256"],
                          "sha256_feed": digest_feed, "nuget_offline": True,
                          "lock_consumidor_sha256": hashlib.sha256((pasta / "packages.lock.json").read_bytes()).hexdigest(),
                          "compilado_por_perfil": True, "dependencias_fora_dll_sdk": True,
                          "instalacao_host": False, "publicado": False})
        return resultado


def agregar(pasta):
    resultados = [json.loads(p.read_bytes()) for p in sorted(pasta.glob("instalacao-dotnet-*.json"))]
    exigir(len(resultados) == len(PERFIS) and {item.get("perfil") for item in resultados} == PERFIS)
    exigir(all(item.get("resultado") == "aprovado" and item.get("metodos") == 151 and item.get("callbacks_fluxo") == 3 for item in resultados))
    exigir(len({item["sha256_pacote"] for item in resultados}) == 1 and len({item["sha256_simbolos"] for item in resultados}) == 1)
    exigir(len({item["sha256_feed"] for item in resultados}) == 1)
    return {"resultado": "aprovado", "data_utc": datetime.now(timezone.utc).isoformat(),
            "pacote": PACOTE, "versao": VERSAO, "publicado": False, "perfis": resultados}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--artefatos", type=Path, default=Path("/pacotes"))
    parser.add_argument("--relatorio", type=Path)
    parser.add_argument("--perfil", choices=sorted(PERFIS))
    parser.add_argument("--agregar", type=Path)
    opcoes = parser.parse_args()
    destino = opcoes.relatorio
    try:
        if opcoes.agregar:
            resultado = agregar(opcoes.agregar.resolve())
            destino = destino or opcoes.agregar / "instalacao-local.json"
        else:
            exigir(destino is not None and opcoes.perfil is not None)
            resultado = instalar(opcoes.artefatos.resolve(), opcoes.perfil)
    except Exception:
        resultado = {"resultado": "falhou", "etapa": ETAPA, "perfil": opcoes.perfil,
                     "falha": "O consumidor instalado não satisfez o contrato; detalhes privados omitidos."}
    if destino is not None:
        try:
            salvar(destino, resultado)
        except Exception:
            resultado = {"resultado": "falhou", "etapa": "relatorio", "falha": "Não foi possível registrar o ensaio local."}
    print(json.dumps(resultado, ensure_ascii=False))
    return 0 if resultado["resultado"] == "aprovado" else 1


if __name__ == "__main__":
    sys.exit(main())
