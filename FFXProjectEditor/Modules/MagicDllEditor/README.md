# MagicDllEditor — Módulo do FFXProjectEditor

Editor de efeitos de magia do FFX HD: abre `magic_XXXX.dll` com **todas as estruturas declaradas**
(Root → Descriptors → Programs → Slots → Fields), edita campos dentro da janela provada
(write-back confinado + SHA + backup + RT0) e permite **adicionar campos** (grow — experimental).

**Guia de uso:** `docs/reverse/MAGIC_DLL_EDITOR_USAGE_20260801.md`
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
dotnet test --filter MagicDll -c Release    # 29/29 PASS
```

## Regras de segurança (não negociáveis)

1. **Nunca salvar sobre a Steam Library** (wrapper bloqueia `IsSteamLibraryPath`).
2. **Nunca editar o prefixo do record** `+0x00..+0x07` (match word — R6 bloqueia).
3. **Grow = experimental** (pointer-trust): só em cópia; re-parse obrigatório; RT2 pendente.
4. Backup `.bak` + SHA antes/depois em toda gravação; restore hash-gated.

*2026-08-01 · Jarvis-PPP-C2C3 · nada commitado*
