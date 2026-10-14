# Validação offline dos contratos Hooks

Lane Jarvis — integração Editor 2026-09-28. Este executável valida arquivos em um processo separado; não carrega a DLL de gameplay, não abre o jogo e não escreve saves.

## Build explícito

```sh
python3 scripts/build_hook_contracts.py --hooks-root /caminho/ffx-hooks --output /nova/pasta/validadores
```

Linux requer compilador C++17 e OpenSSL de desenvolvimento; Windows usa `cl` em um Developer Prompt MSVC e BCrypt. Os hashes dos consumidores são fixados em `sources.json`; deriva bloqueia o build. O binário e seu inventário ficam exclusivamente no destino escolhido.

Selecione `hook-contracts` na janela de authoring. Também pode ser colocado em `tools/hook-contracts/` ao lado do executável do Editor para descoberta local. A descoberta não executa o helper nem habilita OnlyMod. A versão transitória Workshop é consultada antes de interpretar o pedido/economia v5.

## Contratos

- `abilities`: valida 40 IDs compartilhados, nomes canônicos/placeholders, payloads, reservas e cobertura de preços. As quatro definições pendentes permanecem identificadas como pendentes pelo catálogo.
- `elemental-schema`: parser canônico; `PARSED RT0` não é admissão de dados.
- `elemental`: parser + `PackAdmission` + prova dos arquivos completos de monstros.
- `rewards`: parser nativo de TSV para 4096 IDs.
- `workshop-state`, `workshop-plan`: core canônico v5; plano e vencedor/custos do vencedor nunca são serializados.
- `ascension`: ledger de 680 bytes vinculado a save, peça e habilidade; somente leitura.
- `arcana`: decoder canônico do registro de 276 bytes; somente leitura.

Todos os sucessos de admissão são **RT0**, sem equivalência com gameplay/RT2. O perfil `HookCompatibilityProfile` é metadado do Editor; somente as chaves `mod007.*` listadas pelo consumidor pertencem ao `requires` do manifesto Elemental.

## Idiomas

O exportador MOD-006 chama `tools/text_languages/pack.py` e `check_received_pack.py` do checkout Hooks explicitamente selecionado. O produtor verifica tokens, limites originais de bytes e largura, gera FTC/atlas/sombra e só publica o diretório completo após o preflight nativo e controles negativos. A referência privada fica fora de `languages/pt-BR/` e não deve ser instalada como tradução. `TextLanguageValidate` pode ser construído pelo runner `tools/text_languages/run_checks.py --output /nova/pasta` no Linux; o próprio projeto Hooks documenta o runner MSVC.

Nenhum artefato do jogo faz parte deste diretório de fontes. A pasta `work/hooks-editor-complete/` da execução guarda pacotes privados, inventários, testes e capturas, fora do versionamento.
