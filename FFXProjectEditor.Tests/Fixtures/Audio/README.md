# Audio fixtures (FMOD FEV banks)

Real game banks used by `Core/FevReaderListBoundTests.cs` (FEV RIFF LIST-bound
regression, queue item 6 of `docs/reverse/FFX_LOST_WORK_RECOVERY_2026-09-15.md`).
Delivered next to the test assembly by the `Fixtures\**\*.fev` content glob in
`FFXProjectEditor.Tests.csproj`.

## Files

| File | Bytes | sha256 | Provenance |
|---|---|---|---|
| `0328.fev` | 1,010 | `021df0be2edd280e30570c1dd7d2ab154d4a3e407b0271c159971f83c896cc68` | FFX PC (remaster) vanilla SFX bank — `ffx_data/gamedata/ps3data/sound_pc/sfx/us/0328.fev` (host copy: `/mnt/nvme-samsung/FFX Extracted/FFX/...`; byte-identical to the PS4 dump copy under `/mnt/nvme-xpg/FFX_Data/GameData/PS3Data/Sound_PS4/SFX/US/`) |
| `ffx2_music.fev` | 26,180 | `379c86392cb426b4f770eb81d930b3dd68269a1083f350b2f7bc1a43ac458ba7` | FFX-2 PC (remaster) vanilla music bank — `ffx-2_data/gamedata/ps3data/sound_pc/music/ffx2_music.fev` (host copy: `/mnt/nvme-samsung/FFX Extracted/FFX2/...`). This is the sample that exposed the LIST overflow in the 2026-09-15 script validation. |

Both banks share the SFX-family layout: `FMT `@0x0C, `LIST`@0x18 whose body ends
exactly at EOF (`8 + riffSize == fileSize`, RIFF u32@0x04 = fileSize-8), with
sub-chunks OBCT/PROP/LGCY/EPRP/STRR(LANG)/... inside. `0328.fev` is the same family
as the lost work file: OBCT 268 B, PROP 9 B, LGCY body at 0x152 — the exact offsets
hard-coded in `research_tools/Audio/fev_lgcy_parse.py`.

## The lost 404,335 B target (preservation record)

The forensic target of `fev_lgcy_parse.py` was a SFX-family `.fev` of exactly
**404,335 bytes** (script arithmetic: `end = body + 403997` where `body = 0x152`,
i.e. end-of-file = 0x152 + 403,997 = 404,335; the offset family matches every
`sound_pc/sfx` bank). Search performed 2026-09-14/15 (FEV-FIX lane):

- `/mnt/nvme-samsung` (FFX Extracted FFX+FFX2, FFX Mods): 6,335+ `.fev` scanned — absent;
- `/mnt/nvme-xpg` (FFX_Data PS4 dump): 4,428 `.fev` scanned — absent;
- `/mnt/ssd-kingston/ffx-reconstructed` (PS3 FFXX2HDREMASTER + PS4 extractions): included in scan — absent;
- full git history (`git log --all`, 1,267 distinct historical `.fev` blobs) — absent (largest: `0000.fev` 472,330);
- independent confirmation: `artifacts/2026-09-15/script-validation/battlemap-save-qa.md` §5.2 already concluded "404335B não presente no host".

Verdict: the file was a session working copy (not a vanilla bank — no vanilla `.fev`
has that size; largest SFX banks are `0000.fev` 472,330 US / 428,938 JP, `9999.fev`
41,708) and is presumed lost with the archived `/mnt` marathon artifacts (uploaded
to MEGA, not reachable from this host — no rclone remote configured). If a MEGA
restore ever surfaces it, drop it here as `target_404335.fev` and extend
`FevReaderListBoundTests` with a theory row.
