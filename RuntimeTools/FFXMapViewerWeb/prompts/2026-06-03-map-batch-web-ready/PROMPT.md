# Prompt - preparar todos os mapas para o FFXMapViewerWeb

Voce e o proximo executor trabalhando no repo:

```text
C:\Users\wande\Documents\ffx-editor-main
```

Fale com o usuario como `Jarvis`.

## Objetivo

Preparar todas as entidades `map/<area>/<field_token>` listadas em:

```text
RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.csv
RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.txt
```

para serem abertas pelo app web:

```text
RuntimeTools\FFXMapViewerWeb
```

O resultado desejado e que cada mapa exportavel tenha seus arquivos web-ready
salvos de forma organizada dentro da propria pasta do viewer, sem tocar nos
assets originais do jogo.

Se for viavel fazer um software viewer de PC primeiro, tente esse caminho de
forma conservadora. Se isso nao couber com seguranca nesta rodada, implemente o
fallback web: uma forma clara de escolher no navegador qual mapa carregar.

## Contexto obrigatorio

Leia antes de propor ou editar:

```text
docs\ai\SHARED_CONTEXT.md
docs\ai\SESSION_HANDOFF.md
PORT_STATUS.md
KNOWLEDGE_BASE.md
RuntimeTools\FFXMapViewerWeb\README.md
RuntimeTools\PhyreMapExportLab\README.md
RuntimeTools\Ps3MapViewerLab\README.md
docs\history\FFX_PS3_MAPVIEWER_PROJECT_PLAN_2026-06-02.md
docs\history\FFX_PHYRE_MAP_EXPORT_LAB_10_STEP_PLAN_2026-06-02.md
docs\history\FFX_PHYRE_MAP_MATERIAL_IDA_FINDINGS_2026-06-03.md
docs\reverse\ps3data_graphs_2026-06-03\ps3data-graph-summary.json
docs\reverse\ps3data_graphs_2026-06-03\ps3data-map-btlmap-fieldid-bridge.csv
```

Estado atual importante:

- `FFXMapViewerWeb` hoje carrega um unico piloto: `map/azit/azit00`.
- `PhyreMapExportLab` ja aceita `--area`, mas parte do `MapAreaResolver` ainda
  tem gates especificos do piloto `azit00`.
- Antes de batch completo, generalize os gates para outros mapas. Nao force os
  asserts de tamanho/contagem do `azit00` em todos os mapas.
- Material/textura ainda e candidato estrutural, nao prova engine-exact.
- Nao promover AGS, collision, walkmesh, triggers, portas, atores, camera ou
  estado runtime como resolvidos.

## Entradas

Corpus HD:

```text
D:\FFX Extracted\FFX\ffx_data\gamedata\ps3data
```

Lista de mapas:

```text
RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.csv
```

Quantidade esperada:

```text
299 entidades no formato map/<area>/<field_token>
```

Outlier conhecido fora desse formato:

```text
map/hdao_settings.bin
```

Nao inclua esse outlier no batch visual.

## Saida organizada esperada

Crie os outputs web-ready dentro de:

```text
RuntimeTools\FFXMapViewerWeb\public\maps\
```

Estrutura recomendada:

```text
RuntimeTools\FFXMapViewerWeb\public\maps\catalog.json
RuntimeTools\FFXMapViewerWeb\public\maps\status.json
RuntimeTools\FFXMapViewerWeb\public\maps\status.csv
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\map_<area>_<field_token>.static-textured-phyre-slots.gltf
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\map_<area>_<field_token>.map-export-manifest.json
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\map_<area>_<field_token>.static-export-report.json
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\map_<area>_<field_token>.texture-binding-report.json
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\map_<area>_<field_token>.material-slot-analysis.json
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\*.png
RuntimeTools\FFXMapViewerWeb\public\maps\<area>\<field_token>\*.gltf-validator.json
```

Se os outputs ficarem grandes demais para versionar, ainda assim mantenha essa
estrutura local e documente no README que `public\maps\` e gerado/local. Nao
copie assets originais `.phyre` para dentro do viewer; copie somente assets
convertidos/gerados necessarios para o glTF abrir no navegador.

## Plano de execucao

### Fase 1 - Auditoria curta

1. Confirme `git status --short`.
2. Confirme que os 299 mapas existem no corpus.
3. Rode build:

```powershell
dotnet build RuntimeTools\Ps3MapViewerLab\Ps3MapViewerLab.csproj
dotnet build RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj
```

4. Rode ou atualize o catalogo:

```powershell
dotnet run --project RuntimeTools\Ps3MapViewerLab\Ps3MapViewerLab.csproj -- catalog --portable --output work\ps3_mapviewer_lab
```

### Fase 2 - Generalizar o PhyreMapExportLab

Objetivo: transformar o exporter de piloto unico em exporter batch seguro.

Adicione ou ajuste:

- gates genericos para qualquer `map/<area>/<field_token>`;
- preservacao dos gates especificos do `azit00` apenas como smoke/pilot;
- comando `batch`;
- opcao `--list <csv>`;
- opcao `--output-root <dir>` ou uso claro de `--output`;
- modo `--skip-failed`;
- relatorio por mapa com `success`, `blocked`, `missing`, `descriptor_failed`,
  `texture_partial`, `validator_failed`, etc.

Comando alvo:

```powershell
dotnet run --project RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj -- batch --portable --skip-failed --list RuntimeTools\FFXMapViewerWeb\prompts\2026-06-03-map-batch-web-ready\map-entities.csv --output-root RuntimeTools\FFXMapViewerWeb\public\maps
```

O batch deve:

- criar uma pasta por mapa;
- emitir glTF e sidecars quando possivel;
- nao abortar o lote inteiro quando um mapa falhar;
- registrar todos os bloqueios em `status.json` e `status.csv`;
- marcar explicitamente o `decisionBand` de cada saida;
- manter zero escrita no corpus `D:\FFX Extracted\...`.

### Fase 3 - Validacao automatica

Para cada mapa exportado com glTF:

- rode glTF validator quando a toolchain estiver disponivel;
- salve `*.gltf-validator.json` ao lado do glTF;
- se possivel, gere screenshot F3D nao-blank em uma subpasta `screenshots`;
- registre no catalogo se passou, falhou ou foi pulado.

Toolchain local conhecida:

```powershell
$nodeRoot = "$env:LOCALAPPDATA\Programs\nodejs-portable\node-v24.15.0-win-x64"
$env:Path = "$nodeRoot;$env:Path"
$env:NODE_PATH = (Resolve-Path work\node_tooling\node_modules).Path
```

F3D conhecido:

```powershell
C:\Program Files\F3D\bin\f3d.exe
```

Nao bloqueie todo o projeto se validator ou F3D estiverem ausentes; marque o
campo como `tool_missing`.

### Fase 4 - Atualizar o FFXMapViewerWeb

Se um software viewer de PC real nao ficar pronto nesta rodada, implemente o
fallback web.

O web app deve:

- carregar `./public/maps/catalog.json`;
- mostrar seletor/lista pesquisavel de mapas;
- agrupar por `area`;
- exibir status por mapa;
- carregar o glTF selecionado;
- atualizar a URL com `?map=map/azit/azit00` ou equivalente;
- aceitar abrir direto por query string;
- preservar controles atuais: Orbit, Walk, Enter Walk, Fit, Inside, Top, Reset,
  Grid, Wireframe, Axes, Speed;
- preservar leitura de manifest/report/validator;
- tolerar mapas bloqueados ou sem glTF com mensagem curta e status tecnico;
- evitar texto promocional de landing page. A primeira tela deve ser a
  ferramenta.

Arquivos esperados:

```text
RuntimeTools\FFXMapViewerWeb\index.html
RuntimeTools\FFXMapViewerWeb\app.js
RuntimeTools\FFXMapViewerWeb\styles.css
RuntimeTools\FFXMapViewerWeb\README.md
```

### Fase 5 - Viewer de PC, se viavel

Antes de investir no fallback web, avalie brevemente se cabe um viewer de PC:

- standalone Avalonia ou extensao read-only do editor;
- sem WebView obrigatorio se isso complicar;
- usando os mesmos glTFs gerados;
- selector por area/mapa;
- sem writer.

Se o viewer de PC exigir arquitetura grande, deixe como plano futuro e conclua
o seletor web. Nao pare a entrega esperando o viewer desktop perfeito.

## Gates de aceitacao

Minimo aceitavel:

- `dotnet build RuntimeTools\PhyreMapExportLab\PhyreMapExportLab.csproj` passa.
- `dotnet build RuntimeTools\Ps3MapViewerLab\Ps3MapViewerLab.csproj` passa.
- `RuntimeTools\FFXMapViewerWeb\public\maps\catalog.json` existe.
- `RuntimeTools\FFXMapViewerWeb\public\maps\status.csv` existe.
- Pelo menos `map/azit/azit00` continua abrindo no web viewer.
- O web viewer permite escolher mapas pelo navegador quando houver mais de um
  exportado.
- Mapas que nao exportarem ficam listados com motivo, nao desaparecem.

Gate bom:

- batch roda nos 299 mapas com `--skip-failed`;
- todos os sucessos tem sidecars;
- validator e F3D rodam nos sucessos quando toolchain existir;
- Playwright/Browser smoke abre o servidor local e troca pelo menos 3 mapas:
  `map/azit/azit00`, um mapa com status success diferente, e um mapa blocked
  ou partial;
- canvas/browser screenshot nao-blank para os mapas success.

## Servidor local para testar

Use HTTP, nao `file://`:

```powershell
cd C:\Users\wande\Documents\ffx-editor-main
python -m http.server 8765 --bind 127.0.0.1
```

Abrir:

```text
http://127.0.0.1:8765/RuntimeTools/FFXMapViewerWeb/index.html
```

Exemplo esperado apos o seletor:

```text
http://127.0.0.1:8765/RuntimeTools/FFXMapViewerWeb/index.html?map=map/azit/azit00
```

## Linguagem de evidencia

Use estes termos:

- `catalog_ready`
- `static_visual_candidate`
- `texture_binding_structural_candidate`
- `validator_pass`
- `f3d_nonblank`
- `browser_nonblank`
- `material_partial`
- `ags_blocked`
- `runtime_unverified`
- `engine_exact_blocked`

Nao use:

- `engine exact`
- `runtime proved`
- `material solved`
- `collision solved`
- `walkmesh solved`
- `map editor`

a menos que exista prova nova e documentada no repo.

## Documentacao obrigatoria ao finalizar

Atualize:

```text
RuntimeTools\FFXMapViewerWeb\README.md
docs\ai\SESSION_HANDOFF.md
```

Se criar um relatorio maior, salve em:

```text
docs\history\FFX_MAPVIEWER_WEB_BATCH_READY_2026-06-03.md
```

O handoff deve ser curto e apontar para esse relatorio, sem duplicar tudo.

## Resultado que o usuario quer

O usuario quer poder dizer: "quero ver `map/kino/kino07`" e abrir no navegador
sem procurar manualmente glTF, manifest e report.

Se nao der para deixar todos renderizando, deixe pelo menos todos catalogados e
separados por status, com os exportados abrindo no seletor do browser.
