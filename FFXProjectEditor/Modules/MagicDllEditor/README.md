# MagicDllEditor — Módulo do FFXProjectEditor

**Checkpoint posterior — EgoStdMat, 2026-10-08:** tabela local de cinco
rotações distinguida da tabela PPP de16; dez corpos completos/byte-exatos,
290 witnesses e35 matrizes nativas isoladas conferidos. O inspector mostra
composição de matriz somente com código, tabela/HIGHLOW e constantes intactos.
**837/837** MAGIC+i18n, **57/57** no preservado, build sem DevTools e QA
PT-BR/X11 passaram. [Contrato e limites](../../../docs/reverse/magic_dlls/MAGIC_EGO_STANDARD_MATRIX_20261008.md).
A meta integral permanece ativa para outras matrizes/câmeras e variantes.

**Checkpoint posterior — operações no inspector, 2026-10-08:** os callbacks
Ego/Egd agora mostram contratos nativos comprovados nos nove idiomas.
O reconhecimento exige código/relocations/layout/base e célula exatos;
alterações incompatíveis voltam à inspeção sem anotação. São32 pares
específicos, além de retornos sem atualização, em230 perfis de código.
**824/824** MAGIC+i18n, **44/44** no preservado, build sem DevTools e QA
nativa PT-BR/X11 passaram. [Contrato e evidências](../../../docs/reverse/magic_dlls/MAGIC_EGO_CALLBACK_INSPECTOR_CONTRACTS_20261008.md).
Callbacks/instâncias continuam somente leitura; a meta integral permanece ativa.

**Checkpoint posterior — corpos Ego/Egd, 2026-10-08:** 80 tipos, 235 formas
de callbacks e 289 corpos conferidos contra os bytes das DLLs; cinco falhas
de decompile preservadas como disassembly completa. Sete helpers matemáticos
do host e onze pares de funções tiveram seus callees/imports/constantes
conferidos entre os EXEs. Corrigidas as descrições VertexAp/VertexApAt de
Aux4, lotes de captura e delay zero, mantendo layout e proteções de escrita.
**818/818** MAGIC+i18n, **38/38** no preservado e build Release sem DevTools
passaram após a correção. [Contrato e limites](../../../docs/reverse/magic_dlls/MAGIC_EGO_CALLBACK_BODY_COVERAGE_20261008.md).
A meta integral permanece ativa para semântica restante e dispatch das instâncias.

**Checkpoint posterior — Ego/Egd, 2026-10-08:** inspector dos descritores nativos integrado, com símbolos/RVAs, word+16, vínculos e callbacks somente leitura; tabela0387atual tem49entradas. **818/818** MAGIC+i18n, **38/38** no preservado e UI nativa PT-BR/X11 inspecionada. [Contrato e limites](../../../docs/reverse/magic_dlls/MAGIC_EGO_DESCRIPTOR_DISPATCH_20261008.md). A meta integral continua ativa para corpos/dispatch de instâncias ainda não provados semanticamente.

**Checkpoint posterior, 2026-10-08 — Aux4:** VertexAttend +12 agora valida referências de patterns, streams de índices/descriptor, capacidade e âncoras. **806/806** MAGIC+i18n no estado corrigido, com controle negativo previamente reproduzido. Atlas das 716/705 DLLs e **859 consumers completos/byte-exatos** do EXE preservado em [contrato](../../../docs/reverse/magic_dlls/MAGIC_NATIVE_CODE_ATLAS_20261008.md). A meta integral segue ativa, incluindo semântica dos despachos internos e comparação com o EXE instalado.

**Checkpoint vigente, 2026-10-08 — Jarvis-MAGIC:** **802/802** MAGIC+i18n no estado fonte atual, incluindo três novos contratos EOT, e **22/22** no foco do inspector atual. Os checks anteriores de **44/44** UI/i18n e **19/19** no corpus preservado permanecem aplicáveis ao produto inalterado; detalhes no [handoff](../../../docs/ai/handoff/MAGIC_DLL_EDITOR_COMPLETION_2026-10-03.md). A RE de [0344](../../../docs/reverse/magic_dlls/MAGIC_0344_RUNTIME_CONTRACT_20261007.md), [0533](../../../docs/reverse/magic_dlls/MAGIC_0533_OVERLAY_TASKS_20261008.md) e [0387](../../../docs/reverse/magic_dlls/MAGIC_0387_OVERLAY_TASKS_20261008.md) corrige contratos históricos de lifecycle/recursos. A meta integral de RE permanece em andamento; esse checkpoint não é aceitação Windows, gameplay/RT2 ou revisão independente.

**Correção posterior, 2026-10-07 — DrawShape:** referências agora usam a tabela de flipbooks Aux3, com validação/âncoras próprias. O seletor mostra animação de sprites, sem opções de índices de geometria indevidos. 795/795 MAGIC+i18n,31/31 controles/preview,44/44 UI/i18n final e19/19 no preservado. [Contrato](../../../docs/reverse/magic_dlls/PPP_SHAPE_RESOURCE_BINDINGS_20261007.md) e [lifecycle0003](../../../docs/reverse/magic_dlls/MAGIC_0003_OVERLAY_LIFECYCLE_20261007.md). A RE completa das demais callbacks próprias continua em andamento.

**Interface nativa — checkpoint posterior 2026-10-07:** inspector dos exports/tabela integrado, uma interface por DLL, com ponteiro de dados separado das callbacks e todos os elementos somente leitura. 793/793 MAGIC+i18n e 26/26 no preservado, incluindo todos os inputs. InitMagicPRX e vínculo dos thunks PPP reconciliados; callbacks próprias do overlay ainda em RE. [Contrato dos exports](../../../docs/reverse/magic_dlls/MAGIC_DLL_NATIVE_INTERFACE_20261007.md) e [thunks PPP](../../../docs/reverse/magic_dlls/MAGIC_DLL_PPP_THUNKS_20261007.md). Esses resultados não são validação Windows ou gameplay.

**Estado vigente, 2026-10-07 — Jarvis-MAGIC:** caudas, callbacks residuais e words de cor integrados. O mapa contém 223 famílias e 1.294 campos tipados, com referências/contagens protegidas; zero campo fora dos records nos dois corpora. Testes: 774/774 MAGIC+i18n, 85/85 no preservado e 75/75 após a última apresentação da UI. Release sem DevTools com zero erros. Isto é evidência local Linux, sem revisão independente, aceitação Windows ou RT2. A investigação semântica do código próprio das 496 identidades de DLL e contratos de referências permanece em andamento. [Checkpoint atual](../../../docs/ai/handoff/MAGIC_DLL_EDITOR_COMPLETION_2026-10-03.md) e [inventário de código](../../../docs/reverse/magic_dlls/MAGIC_DLL_CODE_INVENTORY_20261007.md).

Editor de efeitos de magia do FFX HD: abre `magic_XXXX.dll` com **todas as estruturas declaradas**
(Root → Descriptors → Programs → Slots → Keyframes → Fields), edita campos dentro da janela provada
(write-back confinado + SHA + backup + RT0) e permite **adicionar campos** (grow — experimental).

**Guia histórico:** `docs/reverse/_archive/playbook/MAGIC_DLL_EDITOR_USAGE_20260801.md`. As contagens e limites antigos não substituem os checkpoints atuais abaixo.
**Specs da frente:** `work/magic_editor/` (PARSER_SPEC, WRITEBACK_SPEC, UI_DESIGN, field_map, CORPUS_AUDIT)

## Estrutura

- `MagicDllEditor_Control.axaml` — UI (toolbar + TreeView + painel de edição + log)
- `MagicDllEditor_ViewModel.cs` — estado, comandos, montagem da árvore
- `MagicDllDocument_Wrapper.cs` — núcleo técnico (TryLoad/TrySaveCopy/TryApplyFieldEdit/TryGrowRecord/TryRestoreBackup/Rt0Check)
- `MagicDllEditor_TreeNodes.cs` — nós da árvore (MagicNode + filhos + campos)
- `MagicEffectNameCatalog.cs` — nomes dos efeitos (catálogo noclip)

Parser: `FFXProjectEditor/FfxLib/MagicDll/` (PE → .data → roots → programs → slots → fields).

## Testes

```
dotnet test --filter MagicDll -c Release
```

## Regras de segurança (não negociáveis)

1. **Nunca salvar sobre a Steam Library** (wrapper bloqueia `IsSteamLibraryPath`).
2. O writer genérico bloqueia `+0x00..+0x07`. Os operandos +4 comprovados usam um writer dedicado com âncoras privadas; não existe exceção genérica para o prefixo.
3. **Grow = experimental** (pointer-trust): só em cópia; re-parse obrigatório; RT2 pendente.
4. Backup `.bak` + SHA antes/depois em toda gravação; restore hash-gated.

**Atualização 2026-10-03 · Jarvis-MAGIC:** sequências nativas integradas; filtro preserva
edições pendentes e a comparação do clone inclui records posteriores. Slots de um
record conservam os campos diretamente na árvore. Contratos, corpus, testes e limites
atuais: `docs/reverse/magic_dlls/PPP_KEYFRAME_EDITOR_20261003.md`.

**Tempos PPP:** pasta Tempos com início/duração/loops e tempo de cada keyframe,
em frames inteiros. Writer independente do payload, com validação conjunta e
rollback; ticks especiais permanecem somente leitura. Contrato e verificação:
`docs/reverse/magic_dlls/PPP_TIMING_EDITOR_20261003.md`.

**Handlers nativos e KeTh:** resolução pela ordem das entradas nativas, incluindo
limites entre tabelas contíguas identificados por relocations; índices conflitantes
entre instâncias ficam RAW. KeTh tem 28 operandos confirmados, KeThSft mantém seus
21 operandos com endereço Exec corrigido. Campos de estado do host foram retirados
do schema de DrawMdl. U16/U8 aceitam decimal e hexadecimal com `0x`.
Provas por versão do executável, testes e lacunas restantes:
`docs/reverse/magic_dlls/PPP_NATIVE_HANDLERS_20261003.md`.

**Instâncias PPP:** diretórios de recursos tipo 8 e fluxos nativos verificados
agora associam cada root à sua tabela. As nove DLLs com múltiplas tabelas resolvem
seus handlers, inclusive em cópias renomeadas e com payload alterado. Código sem
identidade verificada conserva o fallback por consenso. Restam 453 slots RAW em
0507, cujo caminho usa outra interface do host. Contrato/evidência:
`docs/reverse/magic_dlls/PPP_RESOURCE_BINDINGS_20261003.md`.

**Matriz/desenho/PVA:** onze definições revisadas no IDA; nove famílias recebem os
39 operandos reais (37 editáveis sob o guard atual), e duas operações exclusivamente
do host deixam de expor falso payload. O corpus observado não tem mais campos de
schema fora dos strides nativos. Prefixos protegidos permanecem bloqueados.
Contrato/limites: `docs/reverse/magic_dlls/PPP_MATRIX_DRAW_SCHEMAS_20261003.md`.

**C1 e diagnóstico U1 (DevTools):** impulsos por timestamp, sequência completa de
keyframes, integração por eixo e aliases de estado por programa substituem o antigo
modelo cumulativo. Ângulos são apresentados em unidades nativas. O comando continua
restrito a DevTools e não substitui a prévia NoClip; os campos/descrições C1 ficam
no catálogo do produto. Escopo e provas: `docs/reverse/magic_dlls/PPP_C1_NATIVE_DIAGNOSTICS_20261003.md`.

**Coordenadas dos recursos PPP:** descritores agora expõem pontos XYZ s16, com edição
confinada aos arrays existentes e participação em save-copy/revert/filtro/diff.
Contagens, ponteiros, topologia e R6 permanecem protegidos. O corpus atual716 tem
2.432.271 coordenadas editáveis; 205 descritores seguem bloqueados pela validação
conservadora dos buffers vizinhos. Contrato e pendências:
`docs/reverse/magic_dlls/PPP_GEOMETRY_EDITOR_20261003.md`.

**Atualização 2026-10-07 — recursos e operandos nativos:** corrigida a extensão do
bloco de normais, com fronteira independente e alinhamento relativo ao root; os
205 descritores antes bloqueados agora passam. XYZ, tempos e payload participam
da mesma transação que os operandos +4 comprovados de KeLnsLp/KeGrvTgt e as
referências de DrawMdl/DrawMdl2/Semi2/Semi3/DrawShape. R6 genérico segue fechado.
Referências usam opções com topologia e textura compatíveis. Fonte e limites:
`docs/reverse/magic_dlls/PPP_RESOURCE_OPERANDS_20261007.md`.

**Interface0507:** perfil verificado de overlay de campo agora visível, com callbacks
somente para inspeção. O inicializador não registra seus recursos PPP; dicas
automáticas de fp.h não criam associação para os453slots embutidos. Contrato:
`docs/reverse/magic_dlls/PPP_FIELD_OVERLAY_0507_20261007.md`.

**Vetores aleatórios — 2026-10-07:** nove schemas reconciliados com o nativo. Campos
inexistentes +8/+12 removidos; amplitudes int32 das variantes IV agora expostas em
+16/+20/+24, ao lado das amplitudes float32 FV e do modo+32. Destino+4 segue
protegido. Provas: `docs/reverse/magic_dlls/PPP_RANDOM_VECTOR_SCHEMAS_20261007.md`.

**Aleatórios escalares — 2026-10-07:** Float/Char/Short/Int e variantes Up/Down
reconciliados: amplitude+8 f32/u8/u16/u32, modo+12/+9/+10/+12, destino+4 s32
protegido. RandUpFloat72F760 corrigido. Provas e escopo de presença no corpus:
`docs/reverse/magic_dlls/PPP_RANDOM_SCALAR_SCHEMAS_20261007.md`.


## Magic DLL Editor — vetores aleatórios CV/HCV, 2026-10-07

Lane Jarvis-MAGIC. Doze contratos nativos corrigidos: quatro amplitudes s8/s16, modo u8 e destino +4 protegido. S8 integrado com limites de -128 a 127; três schemas ausentes adicionados. **501/501** testes MAGIC e localização, **27/27** no corpus preservado, Release sem DevTools com zero erros e 446 warnings. Tela real PT-BR inspecionada em magic_0472; fontes e IDB original preservadas. Provas e limites: `docs/reverse/magic_dlls/PPP_RANDOM_PACKED_SCHEMAS_20261007.md`. Auditoria dos demais consumidores e gates continuam pendentes; meta integral ativa.


## Magic DLL Editor — desenho, referências e loops, 2026-10-07

Lane Jarvis-MAGIC. Nove schemas reconciliados nos consumidores de desenho/textura, projeção e emissor; quatro schemas de desenho em loop corrigidos e seis vetores em loop confirmados. Foram capturados e comparados 24 ranges nativos no IDA da VM, com matches ambíguos explicitados nos relatórios. O writer agora respeita permissões individuais, exige um campo declarado e protege aliases de campos somente leitura. Referências de programa/estado do emissor são tipadas e protegidas; seus controles continuam graváveis.

Verificação final: **559/559** MAGIC e localização (`loop-final.trx`), **59/59** focados no corpus preservado (`loop-preserved.trx`), zero skips. Release sem DevTools: zero erros e 446 warnings. UI PT-BR real de KeZCrct, DrawMdlTs2 e referência protegida de KeBornRnd5 inspecionada; Xvfb próprio encerrado. As 716 DLLs atuais e 705 preservadas mantêm seus hashes. Base original do IDA manteve SHA `BAB0B5B213FF01EABD5CA5E2EA8950B8766F79887A990462D44CA3AA2DDB368A`; ver recibos `re/draw-loop-*-vm-state.json`.

Contratos e limites: `docs/reverse/magic_dlls/PPP_DRAW_TEXTURE_SCHEMAS_20261007.md` e `docs/reverse/magic_dlls/PPP_LOOP_SCHEMAS_20261007.md`. As dez famílias Loop não têm slots observados nos corpora: seus testes são nativos/decoder/UI, sem alegação de save em asset real ou gameplay.

**Próximo trabalho concreto:** demais variantes de DrawMdl/DrawShape e helpers de recursos, operadores/callbacks ainda sem revisão completa, edição limitada de índices de programa/estado e referências +4 adicionais. O inventário `schema-native-capture-inventory.json` conta presença de capturas, não conclusão de ABI. Windows do produto, revisão independente e eventual gameplay são gates separados. Meta integral permanece ativa; nenhum commit, publicação ou runtime foi feito.


## Magic DLL Editor — variantes de desenho e cadeia de helpers, 2026-10-07

Lane Jarvis-MAGIC. Doze schemas DrawMdl/DrawShape reconciliados com 47 funções nativas completas: alocação, inicialização, transformação, desenho e atualização. Agora há 365 operandos tipados; 31 referências/contagens/dados de serviço permanecem protegidos. DrawMdlRev perdeu os falsos campos +144/+148 do host. PSim expõe índices s16 para inspeção e preserva os vetores sobrescritos pela gravidade ou sem interpretação final comprovada. Contagens que dimensionam alocações permanecem somente leitura. Labels e hints estão nos nove recursos de idioma.

**581/581** MAGIC e localização (`draw-variant-final.trx`), **23/23** focados no corpus preservado (`draw-variant-preserved.trx`), zero skips. Build Release sem DevTools: zero erros e 446 warnings. UI real PT-BR/X11 1280×820 de magic_0153 / DrawMdlSemi +8 inspecionada; recibo `ui-draw-variant-semi-pt.png.json`. Xvfb próprio encerrado. Corpora 716/705 mantêm todos os hashes (`draw-variant-source-preservation.json`); base original do IDA SHA BAB0B5B213FF01EABD5CA5E2EA8950B8766F79887A990462D44CA3AA2DDB368A e sem processos IDA/Python (`re/draw-variant-final-vm-state.json`).

Fonte: `docs/reverse/magic_dlls/PPP_DRAW_VARIANTS_20261007.md`. Apenas Semi aparece nestas doze famílias no corpus preservado (36 slots, stride 12); as outras onze têm prova nativa/decoder/UI, sem falsa alegação de asset/gameplay. Meta integral ativa. Próximos consumidores: operadores de matriz e callbacks/recursos restantes; contratos limitados de referências ainda abertos. Não houve commit/push/deploy/runtime.


## Magic DLL Editor — operações de matriz, 2026-10-07

Lane Jarvis-MAGIC. Vinte e quatro famílias receberam contratos nativos: 20 operações do estado da instância, duas tabelas reservadas sem callbacks e duas entradas com callback somente ret. Foram capturados 22 ranges completos no IDA da VM; tabelas e bytes dos dois EXEs conferidos. Os seletores de MatrixXYZ vêm dos metadados de dispatch, não de floats do payload. MapCount permanece 223; os campos e janelas de escrita dessas famílias continuam ausentes. UI distingue os três contratos em nove idiomas e corrige a mensagem genérica de payload decomposto.

Verificação: **614/614** MAGIC e localização; **34/34** no corpus preservado; **46/46** foco/UI/localização após a correção final da descrição. Build final sem DevTools: zero erros e 446 warnings. UI nativa PT-BR/X11 de magic_0003 / MatrixXYZ reinspecionada em `ui-matrix-operation-pt-final.png` (recibo com SHA da fonte). Xvfb próprio encerrado. Hashes das 716/705 DLLs intactos; IDB original BAB0B5B213FF01EABD5CA5E2EA8950B8766F79887A990462D44CA3AA2DDB368A intacto e nenhum processo IDA/Python no recibo `re/matrix-final-vm-state.json`.

Prova/limites: `docs/reverse/magic_dlls/PPP_MATRIX_OPERATIONS_20261007.md`. O callback ret tem 69.298 matches por um byte; sua identidade vem das referências de tabela, não desse padrão. Restam outros consumidores e contratos limitados de referências, revisão independente e Windows/runtime quando autorizados. Meta integral ativa, sem commit/push/deploy/gameplay.


## Magic DLL Editor — emissores, força e prefixos de mola, 2026-10-07

Lane Jarvis-MAGIC. Sete schemas KeBornRnd/2/3/6, EiWfacc, KeGrvEff e KeLnsLpT corrigidos; EiWindFun e LnsLpSft confirmados. Treze ranges nativos/callbacks capturados e bytes comparados. Índices de programa e offsets de estado ficam tipados e protegidos; bytes de controle ausentes foram integrados. EiWfacc +20 tem restrição nativa `non_zero`: rejeita zero/NaN/infinito sem modificar bytes/SHA. Writer dedicado +4 admite LnsLpSft e LnsLpT após prova dos loads/delegação; R6 genérico inalterado.

**631/631** MAGIC+i18n; depois do refinamento do hint genérico, **51/51** foco/strings/UI. **18/18** emissor/coverage no corpus preservado, zero skips. Save/readback reais de Born2/3/6 e Sft+4 preservam todos os outros bytes; GrvEff rejeita referências. Testes de divisor são fixture sintética, sem presença inventada de EiWfacc. Release final sem DevTools: zero erros, 446 warnings. Tela real PT-BR/X11 1280×820 de magic_0070 / LnsLpSft +4 reinspecionada em `ui-spring-shift-prefix-pt-final.png`; Xvfb encerrado. Hashes de 716/705 DLLs intactos; original IDB BAB0B5B…368A intacto, sem processos IDA/Python no recibo `re/emitter-physics-final-vm-state.json`.

Provas/limites: `docs/reverse/magic_dlls/PPP_EMITTER_PHYSICS_20261007.md`. Meta integral ativa. Próximos consumidores: Lns Arnd/Clm/Crn/Fls e variantes T, lights/physics e operadores restantes; editar referências exige contrato limitado próprio. Nenhum commit/publicação/deploy/gameplay.


## Magic DLL Editor — KeLns Arnd/Clm/Crn/Fls e variantes T, 2026-10-07

Lane Jarvis-MAGIC. Oito schemas reconciliados com 18 funções completas no IDA/PE. Índice KR +4 tipado e protegido; Clm/T ganha seed u8 +22 e janela correta. NonZero nativo agora valida períodos/divisores u16, além do float anterior; zero cancela a transação sem alterar bytes/SHA. Labels/hints seguem os nove idiomas.

**653/653** MAGIC+i18n, **40/40** foco/cobertura no corpus preservado; zero skips. Oito save/readback reais com comparação integral e seis controles negativos de período zero. Release sem DevTools: zero erros, 446 warnings. UI real PT-BR/X11 1280×820, magic_0070 / Clm+22 (u8=8) inspecionada; recibo ui-lens-seed-pt.png.json. Xvfb encerrado. Fontes 716/705 e IDB original BAB0B5B…368A preservados, VM sem IDA/Python (`re/lens-final-vm-state.json`).

Provas: `docs/reverse/magic_dlls/PPP_LENS_SCHEMAS_20261007.md`. Todas estas oito famílias têm ocorrência no corpus; isto não é gameplay/RT2. Meta integral ativa. Próxima frente: lights, recursos/operadores restantes e contratos limitados de referências. Nenhum commit/push/deploy/runtime.


## Magic DLL Editor — iluminação, filtro e chuva, 2026-10-07

Lane Jarvis-MAGIC. Dez layouts reconciliados com 17 funções completas no IDA/PE. FpModel ganha depth +4 e type +8; índices de modelo/animação em +16 protegidos. NeiChr tem dez seletores de programa/estado s32, sem falsos floats. Filter e Rain passam a expor a cadeia delegada real; count +8 e referências estruturais preservados para inspeção. Writer dedicado +4 admite os floats provados de Nei/Fp/Rain, mantendo R6 genérico fechado. Dois casos Nei reais gravam prefixo/payload juntos e comparam todos os bytes.

**666/666** MAGIC+i18n (`light-final-verified.trx`), **26/26** foco/cobertura no preservado, zero skips. Release sem DevTools: zero erros, 446 warnings. UI nativa PT-BR/X11 1280×820 magic_0430 / NeiLightEikyo +4 inspecionada, `ui-light-prefix-pt.png.json`; Xvfb encerrado. Corpora 716/705 sem drift; original IDB BAB0B5B…368A intacto e VM sem IDA/Python (`re/light-final-vm-state.json`).

Prova/limites: `docs/reverse/magic_dlls/PPP_LIGHT_RENDER_SCHEMAS_20261007.md`. Apenas NeiEikyo/NeiPoint/NeiChr têm slots observados deste conjunto; Fp/filter/rain têm prova nativa/decoder, sem inventar asset/gameplay. Meta integral ativa. Restam operadores e consumidores restantes e writers limitados de referências/contagens, além dos gates separados de revisão independente e Windows/runtime. Nenhum commit/push/deploy/gameplay.


## Magic DLL Editor — alvos, animação e PVA de recursos, 2026-10-07

Lane Jarvis-MAGIC. Doze schemas reconciliados com 20 ranges nativos/callbacks/initializers. Índices e offsets em ThTp/2, HitChk, HitBorn, PointRAp e Hmg ficam tipados e protegidos; DisPos ganha layout u16/u8 real. Endereços Uv2/OfsPt resolvidos por tabelas; OfsPt +44 ref de recurso preservada. Tfd2/3/Uv2/Uv3 têm campos e janelas completos, inclusive aliases do record. Writer dedicado +4 admite coeficientes provados de HitBall/ThTp/2/Hmg/PointRAp; R6 genérico fechado.

**690/690** MAGIC+i18n (`resource-operator-accepted.trx`), **37/37** corpus preservado, **50/50** foco/fixtures/strings após corrigir assert antigo de janela, zero skips. Três save/readback reais de prefixo e três de PVA com comparação integral; referências rejeitadas em quatro assets. Release sem DevTools: zero erros, 446 warnings. UI real PT-BR/X11 magic_0005 / ThTp+4=10 inspecionada (`ui-electric-target-prefix-pt.png.json`), Xvfb encerrado. Corpora 716/705 sem drift; original IDB BAB0B5B…368A preservada e VM sem IDA/Python (`re/resource-operator-final-vm-state.json`).

Provas/limites: `docs/reverse/magic_dlls/PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md`. A callback Exec de ThTp/2 é igual, mas os initializers +172=0/1 são distintos; não declarar lifecycle idêntico. Meta integral ativa. Restam tails e consumidores/callbacks residuais, reconciliação completa de referências e gates separados. Nenhum commit/push/deploy/gameplay.
