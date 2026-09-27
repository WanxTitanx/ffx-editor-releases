# Spira Reforge Studio

Open source desktop editor for FINAL FANTASY X / X-2 HD Remaster, built with C#,
.NET 8 and Avalonia. Windows x64 and Linux x64 are supported build targets.

[Download the latest published version](https://github.com/WanxTitanx/ffx-editor-releases/releases/latest)
· [Release history](docs/RELEASE_HISTORY.md) · [Build guide](docs/BUILDING.md)
· [GPL-3.0 license](LICENSE) · [Third-party notices](NOTICE)

## Português

Este repositório contém as fontes do Editor, os viewers, os auxiliares próprios,
os testes e os scripts de build. A fonte de desenvolvimento é **2.246.0.0**;
o binário estável preservado é **2.245.1.0**. Publicar as fontes não significa
que uma nova versão de executáveis passou pela validação de release.

O clone não depende de repositórios privados, credenciais do mantenedor, Git LFS
ou submódulos. As dependências NuGet são públicas. Os componentes de runtime
incluídos mantêm suas licenças; os pré-requisitos opcionais do Windows são
obtidos pelo script com verificação SHA-256, sem executar instaladores.

Com o **SDK .NET 8**, a partir da raiz:

```sh
dotnet run --project FFXProjectEditor/FFXProjectEditor.csproj
```

No app, selecione sua instalação/extração legítima do jogo. Para editar dados,
a pasta `master` deve conter os diretórios necessários, como `new_uspc` e `jppc`.
Áudio, modelos extraídos, dumps, saves pessoais e conversas de desenvolvimento
não fazem parte deste repositório. Recursos de memória exigem o jogo no Windows;
build e testes offline não comprovam comportamento no jogo.

IA é opcional e usa a chave que o próprio usuário configurar. Nenhum segredo
compartilhado é necessário para compilar ou usar os recursos locais do Editor.

Veja [BUILDING.md](docs/BUILDING.md) para preparar o pacote Windows completo,
recompilar os auxiliares, publicar para Linux e configurar dados de teste locais.

## English

This repository includes the editor source, product viewers, first-party helper
source, tests and build scripts. Development source is **2.246.0.0**; the latest
preserved binary release is **2.245.1.0**. The source snapshot is not a new binary
release or a claim of in-game validation.

Install the **.NET 8 SDK**, clone this repository and run the command above.
There are no private Git dependencies, shared API keys, Git LFS requirements or
required submodules. See the [build guide](docs/BUILDING.md) for native Linux
requirements and optional Windows packaging dependencies.

Bring your own legitimate game installation/extraction. Game corpora, personal
saves and development transcripts are excluded. AI features are optional and
use user-supplied credentials. Existing release assets retain their original
names and SHA-256 hashes so installed launchers continue to use the same URLs.

Spira Reforge Studio is an unofficial community project, not affiliated with
Square Enix. Third-party components retain their respective licenses and
attributions; see [NOTICE](NOTICE) and [the dependency ledger](release/tool-dependencies.json).
