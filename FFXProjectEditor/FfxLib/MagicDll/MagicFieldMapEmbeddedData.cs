namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Embedded PPP family fallback for <see cref="MagicFieldMap"/>. The JSON retains the
    /// reviewed payload schemas. <see cref="KnownReadOnlyFamilies"/> adds canonical family names
    /// observed in the local handler catalog without granting them a window or write permission.
    /// Source: <c>work/magic_editor/field_map.json</c> and <c>work/layer_c/h_catalog.json</c>.
    /// </summary>
    internal static class MagicFieldMapEmbeddedData
    {
        /// <summary>
        /// Canonical handler names that have no reviewed payload/write schema. These entries are
        /// deliberately materialized as parse/read-only and must never inherit an inferred window.
        /// </summary>
        internal static readonly string[] KnownReadOnlyFamilies =
        {
            "pppDrawMatrix",
            "pppDrawMatrixFront",
            "pppDrawMatrixFrontLoop",
            "pppDrawMatrixLoop",
            "pppDrawMatrixNoRot",
            "pppDrawMatrixWood",
            "pppDrawMatrixWoodLoop",
            "pppDrawShapeRev",
            "pppDummyFunc",
            "pppEiZCrctDisPos",
            "pppFaceAp",
            "pppKeAcmCicEx",
            "pppKeAcmSolid",
            "pppKeDMat",
            "pppKeDMatFr",
            "pppKeDMatPht",
            "pppKeDMatPhtFr",
            "pppKeMatPht",
            "pppKeMvYpEff",
            "pppKeOfsMatXYZ",
            "pppKeParMatR",
            "pppKeShpDtt",
            "pppKeShpTail",
            "pppKeShpTail2",
            "pppKeShpTail2X",
            "pppKeShpTail3XImm",
            "pppKeShpTailLc",
            "pppKeShpTailX",
            "pppKeThCp",
            "pppKeThCpSft",
            "pppKeThLz",
            "pppKeThRes",
            "pppKeThRes128",
            "pppKeThRes128x4",
            "pppKeThRes128x8",
            "pppKeThRes16",
            "pppKeThRes16x16",
            "pppKeThRes16x24",
            "pppKeThRes16x4",
            "pppKeThRes16x64",
            "pppKeThRes16x8",
            "pppKeThRes24",
            "pppKeThRes24x16",
            "pppKeThRes24x4",
            "pppKeThRes24x8",
            "pppKeThRes255",
            "pppKeThRes255x4",
            "pppKeThRes32",
            "pppKeThRes32x16",
            "pppKeThRes32x24",
            "pppKeThRes32x32",
            "pppKeThRes32x4",
            "pppKeThRes32x8",
            "pppKeThRes40",
            "pppKeThRes40x16",
            "pppKeThRes40x4",
            "pppKeThRes40x8",
            "pppKeThRes48",
            "pppKeThRes48x16",
            "pppKeThRes48x4",
            "pppKeThRes48x8",
            "pppKeThRes64",
            "pppKeThRes64x16",
            "pppKeThRes64x4",
            "pppKeThRes64x8",
            "pppKeThRes8",
            "pppKeThRes8x128",
            "pppKeThRes8x4",
            "pppMatrixFront",
            "pppMatrixLoc",
            "pppMatrixLoop",
            "pppMatrixXYZ",
            "pppMatrixXZY",
            "pppMatrixYXZ",
            "pppMatrixYZX",
            "pppMatrixZXY",
            "pppMatrixZYX",
            "pppNeiDrawMdlPointLight",
            "pppNeiDrawMdlTsPointLight",
            "pppSMatrix",
            "pppSRandUpHCV",
        };

        /// <summary>JSON ASCII (ensure_ascii) com as famílias payload_consumer.</summary>
        public const string Json = """
        {
         "pppAccele": {
          "opcode": "pppAccele",
          "handler_addr": "0x75B830",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "accel_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao X por frame (double-layer: layerB += delta; layerA += layerB)"
           },
           {
            "name": "accel_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao Y por frame (double-layer: layerB += delta; layerA += layerB)"
           },
           {
            "name": "accel_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao Z por frame (double-layer: layerB += delta; layerA += layerB)"
           },
           {
            "name": "accel_w",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao W por frame (double-layer: layerB += delta; layerA += layerB)"
           }
          ],
          "usage": "Acelera\u00e7\u00e3o (double-layer: layerB += delta; layerA += layerB)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A500"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppAccele",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "double",
          "notes": [
           "Double-layer: layerB(node[1]+a1+160..172) += program+16..28 f32[4] (DENTRO do match); layerA(node[0]+a1+160..172) += layerB SEMPRE (fora do match).",
           "BYTE-IDENTICO a 0x75BED0 (pppMove) \u2014 mesmo codigo, 2 opcodes. Clone group confirmado por get_bytes.",
           "Estado +0xA0..+0xAC do doc dispatch antigo = +160..+172 decimal = MESMA janela do padrao BoneAnim (0xA0=160, 0xAC=172). SEM conflito.",
           "Aux 0x75B900: SetFromGlobals layerB <- flt_C0A004..flt_C0A010."
          ],
          "semantics_category": "outro"
         },
         "pppAngAccele": {
          "opcode": "pppAngAccele",
          "handler_addr": "0x75B940",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "accel_x",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "aceleracao angular X por frame (s32, unidades PPP)"
           },
           {
            "name": "accel_y",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "aceleracao angular Y por frame (s32, unidades PPP)"
           },
           {
            "name": "accel_z",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "aceleracao angular Z por frame (s32, unidades PPP)"
           },
           {
            "name": "accel_w",
            "offset": 28,
            "width": 4,
            "type": "s32",
            "semantics": "aceleracao angular W por frame (s32, unidades PPP)"
           }
          ],
          "usage": "Acelera\u00e7\u00e3o angular (mira/tracking de alvo \u2014 T4 observado)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A528"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppAngAccele",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "double",
          "notes": [
           "Double-layer (mesmo padrao Accele/Move). Window real = program+16..28 (16B f32[4]).",
           "Raw yonishi 36B: o handler le apenas 16B; resto do payload nao-consumido por este handler (pode ser lido por outros slots/handlers do mesmo efeito). window_conflict_with_writer: FALSE (janela editavel 16B cabe no raw).",
           "BYTE-IDENTICO a 0x75BFE0 (pppAngMove).",
           "Aux 0x75B9B0: SetFromGlobals layerB <- (256.0f,256.0f @ 0xC8F508, word_C8F510, dword_C8F514). 256.0 = provavel angulo default em unidades PPP."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppAngMove": {
          "opcode": "pppAngMove",
          "handler_addr": "0x75BFE0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular X por frame (s32, unidades PPP)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular Y por frame (s32, unidades PPP)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular Z por frame (s32, unidades PPP)"
           },
           {
            "name": "delta_w",
            "offset": 28,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular W por frame (s32, unidades PPP)"
           }
          ],
          "usage": "Movimento angular",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A5C8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppAngMove",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "double",
          "notes": [
           "Double-layer (mesmo padrao AngAccele). BYTE-IDENTICO a 0x75B940 (pppAngAccele).",
           "Aux 0x75C050: SetFromGlobals layerB <- (256,256,word_C8F510,dword_C8F514)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppAngMoveLoop": {
          "opcode": "pppAngMoveLoop",
          "handler_addr": "0x75C2A0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular X por frame (loop, double-layer int32)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular Y por frame (loop, double-layer int32)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "delta angular Z por frame (loop, double-layer int32)"
           }
          ],
          "usage": "Movimento angular em loop",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b540"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppAngMoveLoop",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 12,
          "notes": [
           "3x s32 (+16/+20/+24) += node1+160..168 (match); node0 += node1 sempre. Double-layer int32.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppAngle": {
          "opcode": "pppAngle",
          "handler_addr": "0x75CF20",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "angle_x",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "angulo X de rotacao (s32, unidades PPP; 256 = default)"
           },
           {
            "name": "angle_y",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "angulo Y de rotacao (s32, unidades PPP; 256 = default)"
           },
           {
            "name": "angle_z",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "angulo Z de rotacao (s32, unidades PPP; 256 = default)"
           },
           {
            "name": "angle_w",
            "offset": 28,
            "width": 4,
            "type": "s32",
            "semantics": "angulo W de rotacao (s32, unidades PPP; 256 = default)"
           }
          ],
          "usage": "\u00c2ngulo (rota\u00e7\u00e3o; mesmo vetor do Scale \u2014 T4 observado)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A668"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppAngle",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "single",
          "notes": [
           "SINGLE-layer (igual Point/Scale): node[0]+160..172 += program+16..28. Semantic-identico a Scale/Point mas NAO byte-identico (ordem de load difere).",
           "Aux 0x75CF70: SetFromGlobals node[0] <- (256,256,word_C8F510,dword_C8F514)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppAngleLoop": {
          "opcode": "pppAngleLoop",
          "handler_addr": "0x75CFB0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "angle_x",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "angulo X por frame (loop, com init de handle)"
           },
           {
            "name": "angle_y",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "angulo Y por frame (loop, com init de handle)"
           },
           {
            "name": "angle_z",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "angulo Z por frame (loop, com init de handle)"
           }
          ],
          "usage": "\u00c2ngulo em loop (com init de handle)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b590"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppAngleLoop",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 12,
          "notes": [
           "3x s32 (+16/+20/+24) += node0+160..168. Init: node0+176=handle do prog quando nulo. Single-node.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppColAccele": {
          "opcode": "pppColAccele",
          "handler_addr": "0x75BB30",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "delta_r",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal R (u16) \u2014 igual ColMove (decompile 0x75BB30)"
           },
           {
            "name": "delta_g",
            "offset": 10,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal G (u16)"
           },
           {
            "name": "delta_b",
            "offset": 12,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal B (u16)"
           },
           {
            "name": "delta_a",
            "offset": 14,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal A (u16)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.4 decompile 0x75BB30: payload 4x u16 @+8..+15, byte-identico ao ColMove]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3a578"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppColAccele",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppColMove": {
          "opcode": "pppColMove",
          "handler_addr": "0x75C480",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "delta_r",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal R (u16)"
           },
           {
            "name": "delta_g",
            "offset": 10,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal G (u16)"
           },
           {
            "name": "delta_b",
            "offset": 12,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal B (u16)"
           },
           {
            "name": "delta_a",
            "offset": 14,
            "width": 2,
            "type": "u16",
            "semantics": "delta de cor canal A (u16)"
           }
          ],
          "usage": "Cor/movimento de cor (word4)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A618"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppColMove",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 8,
          "layer_mode": "double",
          "notes": [
           "UNICO U1 com janela 8B: delta u16[4] @ program+8..+15 (a2[2..3]); record 16B = 8B prefixo + 8B payload.",
           "layerB(node[1]+160..166) += u16[4] (match); layerA(node[0]+160..166) += layerB SEMPRE.",
           "ATENCAO (writer): a t0 entry 151 (alias) aponta para 0x75C090 (SclMove float4, janela 16B) \u2014 handler REAL depende do fp.h local. Schema DIRECT (word4/8B) vale para o handler 0x75C480; se o fp.h local ligar pppColMove a 0x75C090, usar schema SclMove.",
           "Aux 0x75C500: SetFromGlobals layerB <- (256.0f,256.0f @ 0xC8F508) apenas 8B. CHAR_Tidus (0xC8B500) = ponteiro node base global, nome ENGANOSO do pass."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppColor": {
          "opcode": "pppColor",
          "handler_addr": "0x75D2E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "pppColor (decompile lotes 2E/2F \u2014 acesso real em +12)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA7 sweep: FFX_FieldMap_AccumulateVec4Word \u2014 4x u16 RGBA @+8..+0xE acumulados em a1+0xA0]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppColor",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppDrawFilter": {
          "opcode": "pppDrawFilter",
          "handler_addr": "0x757370",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 12,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "wrap_x",
            "offset": 12,
            "width": 4,
            "type": "u32",
            "semantics": "wrap X (u32 << 8), clamp [0, wrap<<8)"
           },
           {
            "name": "wrap_y",
            "offset": 16,
            "width": 4,
            "type": "u32",
            "semantics": "wrap Y (u32 << 8), clamp [0, wrap<<8)"
           },
           {
            "name": "delta_x",
            "offset": 28,
            "width": 4,
            "type": "s32",
            "semantics": "delta X por frame (node+164)"
           },
           {
            "name": "delta_y",
            "offset": 32,
            "width": 4,
            "type": "s32",
            "semantics": "delta Y por frame (node+168)"
           }
          ],
          "usage": "Filtro de proje\u00e7\u00e3o com wrap X/Y (<<8) \u2014 SEM match word [ONDA1b decompile: FFX_BattleModel_ClampWrapProjectionOffsets \u2014 wrapX u32@+12<<8, wrapY@+16<<8, deltas s32@+28/+32, clamp wraparound; NAO checa id]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawFilter",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 24,
          "notes": [
           "wrapX u32<<8 +12, wrapY<<8 +16, deltas s32 +28/+32 += node+164/+168; clamp [0, wrap<<8). SEM match word.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "outro"
         },
         "pppDrawMdl": {
          "opcode": "pppDrawMdl",
          "handler_addr": "0x737830",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 136
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "sub_struct_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "base de sub-struct (v28 = a2+16) passada ao commit do drawable (matriz/estado de textura)"
           },
           {
            "name": "sub_struct_80",
            "offset": 80,
            "width": 4,
            "type": "s32",
            "semantics": "base de sub-struct (v25 = a2+80) usada na construcao do drawable"
           },
           {
            "name": "recurso_modelo",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "id de recurso de modelo/textura: Std_IdentityFunc(a2+144) -> ptr de recurso"
           },
           {
            "name": "recurso_modelo_2",
            "offset": 148,
            "width": 4,
            "type": "s32",
            "semantics": "2o id de recurso: Std_IdentityFunc(a2+148)"
           },
           {
            "name": "coord_base_x",
            "offset": 160,
            "width": 4,
            "type": "s32",
            "semantics": "par de coordenadas x: dword em (a2+base_tabela)+160 reembalado (WordPair_Reinterleave/ShortVector) p/ texcoords; base_tabela = **(a4+12)"
           },
           {
            "name": "coord_base_y",
            "offset": 164,
            "width": 4,
            "type": "s32",
            "semantics": "par de coordenadas y: dword em (a2+base_tabela)+164; copiado p/ global C8F588"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: le a2+16/+80/+144/+148 (transform, matriz, ptrs recurso)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3a848"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdl",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdl2": {
          "opcode": "pppDrawMdl2",
          "handler_addr": "0x7385F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 136
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_144",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: mesmo layout do DrawMdl (type F)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3ad98"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdl2",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdl3": {
          "opcode": "pppDrawMdl3",
          "handler_addr": "0x739580",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 4
          },
          "match_word": null,
          "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "pppDrawMdl3 (decompile lotes 2E/2F \u2014 acesso real em +4)"
           }
          ],
          "usage": "Draw variante 3 (key + magic id 272/273/378)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC86490"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdl3",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 4,
          "notes": [
           "Mesmo nucleo DrawMdlSemi: key dword em record+4 (guard != 0xFFFF). Janela editavel = key 4B.",
           "Draw handler (t2 keyhole). Mesmo nucleo do DrawMdlSemi (packing 13-bit + descritores + key) com extras: guard key em v55+4; FFX_Magic_GetCurrentMagicId() 272/273/378; FFX_Locale_GetCurrentId; FFX_FieldEngine_Dispatch_65E010 no inicio.",
           "Nome antigo FFX_BattleCmd_ProcessAbilitySetup ENGANOSO -> FFX_PppHandler_DrawMdl3."
          ],
          "semantics_category": "outro"
         },
         "pppDrawMdlBS": {
          "opcode": "pppDrawMdlBS",
          "handler_addr": "0x737830",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 136
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_144",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_148",
            "offset": 148,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: alias do addr 0x737830 (mesmo corpo do DrawMdl)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3dcc8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlBS",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlCamera": {
          "opcode": "pppDrawMdlCamera",
          "handler_addr": "0x73D9F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: vec4 em a2+4 + gate dword em a2+12]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b680"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlCamera",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlCameraLoop": {
          "window": {
           "start": 12,
           "width": 30
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337, 0x73FB10): f32s +12..+32 + u8s +33..+41 = 30B + key +4."
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x73FB10",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_DrawMdlCameraLoop (Build VFX texture, __usercall draw-builder); janela derivada mantida]",
          "fields": [
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_28",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_33",
            "offset": 33,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_34",
            "offset": 34,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_35",
            "offset": 35,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_36",
            "offset": 36,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_38",
            "offset": 38,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_39",
            "offset": 39,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_40",
            "offset": 40,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_41",
            "offset": 41,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlInf": {
          "opcode": "pppDrawMdlInf",
          "handler_addr": "0x73BA70",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 148
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_4",
            "offset": 4,
            "width": 1,
            "type": "u8",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +4)"
           },
           {
            "name": "param_u8_6",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +6)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +8)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +12)"
           },
           {
            "name": "param_s32_32",
            "offset": 32,
            "width": 4,
            "type": "s32",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +32)"
           },
           {
            "name": "param_s32_36",
            "offset": 36,
            "width": 4,
            "type": "s32",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +36)"
           },
           {
            "name": "param_u8_37",
            "offset": 37,
            "width": 1,
            "type": "u8",
            "semantics": "pppDrawMdlInf (decompile lotes 2E/2F \u2014 acesso real em +37)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: vec4s a2+4/+8/+12 + ptrs a2+144/+148]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b2c0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlInf",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlLoop": {
          "opcode": "pppDrawMdlLoop",
          "handler_addr": "0x73E370",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "tex_key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso de textura/modelo (0xFFFF = skip) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 0 (node[2]+160) aplicado por frame"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 1 (node[2]+164) aplicado por frame"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 2 (node[2]+168) aplicado por frame"
           },
           {
            "name": "delta_3",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 3 (node[2]+172) aplicado por frame"
           },
           {
            "name": "delta_4",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 4 (node[2]+176) aplicado por frame"
           },
           {
            "name": "delta_5",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 5 (node[2]+180) aplicado por frame"
           }
          ],
          "usage": "Draw em loop (com INIT)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b630"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlLoop",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 24,
          "notes": [
           "6x f32 (+8..+28); INIT quando node+160 nulo (164=a3+8, 176=a3+20); key +4.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlLoopDisPos": {
          "opcode": "pppDrawMdlLoopDisPos",
          "handler_addr": "0x73F2F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "tex_key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso de textura/modelo (0xFFFF = skip) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 0 (node[2]+160) aplicado por frame"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 1 (node[2]+164) aplicado por frame"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 2 (node[2]+168) aplicado por frame"
           },
           {
            "name": "delta_3",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 3 (node[2]+172) aplicado por frame"
           },
           {
            "name": "delta_4",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 4 (node[2]+176) aplicado por frame"
           },
           {
            "name": "delta_5",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 5 (node[2]+180) aplicado por frame"
           },
           {
            "name": "flag_0",
            "offset": 33,
            "width": 1,
            "type": "u8",
            "semantics": "flag anim 0 (byte +33; +38 seleciona node alternativo) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "flag_1",
            "offset": 34,
            "width": 1,
            "type": "u8",
            "semantics": "flag anim 1 (byte +34; +38 seleciona node alternativo) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "flag_2",
            "offset": 35,
            "width": 1,
            "type": "u8",
            "semantics": "flag anim 2 (byte +35; +38 seleciona node alternativo) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "flag_3",
            "offset": 36,
            "width": 1,
            "type": "u8",
            "semantics": "flag anim 3 (byte +36; +38 seleciona node alternativo) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "flag_4",
            "offset": 37,
            "width": 1,
            "type": "u8",
            "semantics": "flag anim 4 (byte +37; +38 seleciona node alternativo) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "flag_5",
            "offset": 38,
            "width": 1,
            "type": "u8",
            "semantics": "flag anim 5 (byte +38; +38 seleciona node alternativo) - fora da janela, read-only",
            "editable": false
           }
          ],
          "usage": "Draw loop com posi\u00e7\u00e3o deslocada (flags +33..+38)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b7e8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlLoopDisPos",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 24,
          "notes": [
           "6x f32 (+8..+28); key +4; flags +33..+38 (byte +38 seleciona node alternativo).",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlLoopZ": {
          "opcode": "pppDrawMdlLoopZ",
          "handler_addr": "0x73EB00",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "tex_key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso de textura/modelo (0xFFFF = skip) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 0 (node[2]+160) aplicado por frame"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 1 (node[2]+164) aplicado por frame"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 2 (node[2]+168) aplicado por frame"
           },
           {
            "name": "delta_3",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 3 (node[2]+172) aplicado por frame"
           },
           {
            "name": "delta_4",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 4 (node[2]+176) aplicado por frame"
           },
           {
            "name": "delta_5",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 5 (node[2]+180) aplicado por frame"
           }
          ],
          "usage": "Draw loop Z (magic id 258/259/244/272/313/122)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b7c0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlLoopZ",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 24,
          "notes": [
           "6x f32 (+8..+28); key +4; checks magic id 258/259/244/272/313/122.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlPSim": {
          "opcode": "pppDrawMdlPSim",
          "handler_addr": "0x73D130",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 28
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_28",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: slot a2+4 e 6 floats a2+8..+0x1F (type I/sim)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b4c8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlPSim",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlRev": {
          "opcode": "pppDrawMdlRev",
          "handler_addr": "0x7404D0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 136
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_144",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_148",
            "offset": 148,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: render c/ transform, le a2+16/+80/+144/+148]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3dcf0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlRev",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlSea": {
          "opcode": "pppDrawMdlSea",
          "handler_addr": "0x73ACA0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 4
          },
          "match_word": null,
          "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
          "fields": [
           {
            "name": "key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso (guard key != 0xFFFF; 0xFFFF = skip build)"
           }
          ],
          "usage": "Draw variante sea (corpo n\u00e3o re-decompilado)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3ACF8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlSea",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 4,
          "notes": [
           "HIPO: mesmo padrao draw (key em +4) \u2014 corpo nao re-decompilado nesta sessao; entry 0xC3ACF8 +0xC = 0x73ACA0, aux 0x73ABC0/0x73AC00, release 0x73AC30.",
           "Draw handler (name_ptr 0xB51278='pppDrawMdlSea'). Aux +0x1C/+0x20 = 0x73ABC0/0x73AC00; +0x24 = 0x73AC30 (release).",
           "Nome antigo FFX_FaceAnimation_Sequencer_structural ENGANOSO -> FFX_PppHandler_DrawMdlSea. Decompile pendente de refinamento."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppDrawMdlSemi": {
          "window": {
           "start": 4,
           "width": 8
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337, 0x737BE0): key +4 + f32 +8 = 8B."
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x737BE0",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_DrawMdlSemi (Build VFX drawable type F sub, __usercall); janela derivada mantida]",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_8",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlSemi2": {
          "opcode": "pppDrawMdlSemi2",
          "handler_addr": "0x738B00",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 152
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_144",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_148",
            "offset": 148,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: le a2+16/+80/+144/+148 e u16 em a2+166 (type H)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b2e8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlSemi2",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlSemi3": {
          "opcode": "pppDrawMdlSemi3",
          "handler_addr": "0x739DF0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 136
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_144",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_148",
            "offset": 148,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham modelo/textura com key de recurso (0xFFFF = skip); variantes: camera, loop, sea, semi-transparente, PSim, reverse [ONDA7 sweep: locale, le a2+16/+80/+144/+148]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b3b0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlSemi3",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlTs": {
          "opcode": "pppDrawMdlTs",
          "handler_addr": "0x738000",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 24
          },
          "match_word": null,
          "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
          "fields": [
           {
            "name": "tex_key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso de textura/modelo (0xFFFF = skip) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 0 (node[2]+160) aplicado por frame"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 1 (node[2]+164) aplicado por frame"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 2 (node[2]+168) aplicado por frame"
           },
           {
            "name": "delta_3",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 3 (node[2]+172) aplicado por frame"
           },
           {
            "name": "delta_4",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 4 (node[2]+176) aplicado por frame"
           },
           {
            "name": "delta_5",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 5 (node[2]+180) aplicado por frame"
           }
          ],
          "usage": "Draw textura com deltas init + key de recurso",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A898"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlTs",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 24,
          "notes": [
           "Le payload: +0 id (match inst+12), +4 key (0xFFFF=skip), +8..+28 = 6x f32 (init deltas state+160..180). Janela editavel = 24B deltas.",
           "Draw handler com match: +0 u32 id (== inst+12); +4 resource key s32 (0xFFFF=skip); +8..+28 = 6 f32 deltas init state+160..180. Guard FFX_PppStatePausedFlag.",
           "Acumula estado node[2] (records[2]): integracao 160+=164, 172+=176; aplica deltas quando match. Projecao fixed-point 65536.0; handles modelo/textura inst+144/+148; larg/alt do descritor (+28 dados modelo, u16 +22).",
           "Nome antigo FFX_MagicHost_BuildVfxTexture_TypeF -> FFX_PppHandler_DrawMdlTs."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlTs2": {
          "window": {
           "start": 8,
           "width": 28
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337, 0x738F80): +8..+32 f32s (fim +35) + key +4 = 28B."
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x738F80",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_DrawMdlTs2; janela derivada mantida]",
          "fields": [
           {
            "name": "drawable",
            "offset": 8,
            "width": 4,
            "type": "u32",
            "semantics": "drawable u32@+8 \u2014 recurso de textura (cadeia draw)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_28",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlTs3": {
          "opcode": "pppDrawMdlTs3",
          "handler_addr": "0x73A430",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "tex_key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso de textura/modelo (0xFFFF = skip) - fora da janela, read-only",
            "editable": false
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 0 (node[2]+160) aplicado por frame"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 1 (node[2]+164) aplicado por frame"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 2 (node[2]+168) aplicado por frame"
           },
           {
            "name": "delta_3",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 3 (node[2]+172) aplicado por frame"
           },
           {
            "name": "delta_4",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 4 (node[2]+176) aplicado por frame"
           },
           {
            "name": "delta_5",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta init 5 (node[2]+180) aplicado por frame"
           },
           {
            "name": "byte_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "byte de controle"
           },
           {
            "name": "byte_33",
            "offset": 33,
            "width": 1,
            "type": "u8",
            "semantics": "byte de controle"
           },
           {
            "name": "byte_34",
            "offset": 34,
            "width": 1,
            "type": "u8",
            "semantics": "byte de controle"
           },
           {
            "name": "byte_35",
            "offset": 35,
            "width": 1,
            "type": "u8",
            "semantics": "byte de controle"
           },
           {
            "name": "byte_36",
            "offset": 36,
            "width": 1,
            "type": "u8",
            "semantics": "byte de controle"
           }
          ],
          "usage": "Variante locale (magic id checks)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b130"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppDrawMdlTs3",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 24,
          "notes": [
           "6x f32 (+8..+28) init deltas; key +4. Variante locale (magic id checks).",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawRain": {
          "opcode": "pppDrawRain",
          "handler_addr": "0x7563B0",
          "payload_consumer": true,
          "editable": true,
          "window": null,
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "chuva",
          "status": "SEM_PAYLOAD",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawRain",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppDrawShape": {
          "opcode": "pppDrawShape",
          "handler_addr": "0x740AA0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 12,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "flag_dispatch",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag bit0 (a2+12 & 1): se 0, percorre a chain de drawables e faz dispatch por slot"
           },
           {
            "name": "slot_offset_base",
            "offset": 32,
            "width": 4,
            "type": "s32",
            "semantics": "base de offset de slot: v7 = 4*slot(a4) + *(a2+32) \u2014 indice na lista de recursos do slot"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: FFX_MagicHost_DispatchVfxDrawableBySlot \u2014 flag@+12, tabela@+32]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShape",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawShapeCamera": {
          "opcode": "pppDrawShapeCamera",
          "handler_addr": "0x747600",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: dispatch draw (tex_id@+4, count@+8)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeCamera",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawShapeCameraDisPos": {
          "opcode": "pppDrawShapeCameraDisPos",
          "handler_addr": "0x748860",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: idem DrawShapeCamera]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeCameraDisPos",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "KR-resource"
         },
         "pppDrawShapeField": {
          "opcode": "pppDrawShapeField",
          "handler_addr": "0x742D80",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: FFX_KR_LoadTableForCurrentLanguage]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeField",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "KR-resource"
         },
         "pppDrawShapeFieldGlobal": {
          "opcode": "pppDrawShapeFieldGlobal",
          "handler_addr": "0x7463E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: DispatchEffectDraw_D]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeFieldGlobal",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawShapeFieldRev": {
          "opcode": "pppDrawShapeFieldRev",
          "handler_addr": "0x744100",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: BuildTableDrawableFromEntry]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeFieldRev",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "KR-resource"
         },
         "pppDrawShapeFieldSpd": {
          "opcode": "pppDrawShapeFieldSpd",
          "handler_addr": "0x7452D0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 72
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_64",
            "offset": 64,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: knobs 3xf32@+64/68/72 pos + ctrl@+4/+8]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeFieldSpd",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "KR-resource"
         },
         "pppDrawShapeX": {
          "opcode": "pppDrawShapeX",
          "handler_addr": "0x740F90",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 12,
           "width": 24
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "desenham shapes geom\u00e9tricos (campo, c\u00e2mera, reverso, velocidade) [ONDA7 sweep: dispatcher alt (flag@+12, tabela@+32)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppDrawShapeX",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppEiWfacc": {
          "opcode": "pppEiWfacc",
          "handler_addr": "0x75BC20",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 20
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "spring_a",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+4 \u2014 parametro de mola"
           },
           {
            "name": "spring_b",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+8 \u2014 parametro de mola"
           },
           {
            "name": "spring_c",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+12 \u2014 parametro de mola"
           },
           {
            "name": "spring_d",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+16 \u2014 parametro de mola"
           },
           {
            "name": "spring_e",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+20 \u2014 parametro de mola"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.12 decompile: FieldMap_ApplySpringForce (5 valores de forca de mola)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppEiWfacc",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppEiWindFun": {
          "opcode": "pppEiWindFun",
          "handler_addr": "0x75D540",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 36
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "init_0",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "init 0 (acc/vel/pos, node+160..+180)"
           },
           {
            "name": "init_1",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "init 1 (acc/vel/pos, node+164..+180)"
           },
           {
            "name": "init_2",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "init 2 (acc/vel/pos, node+168..+180)"
           },
           {
            "name": "init_3",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "init 3 (acc/vel/pos, node+172..+180)"
           },
           {
            "name": "init_4",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "init 4 (acc/vel/pos, node+176..+180)"
           },
           {
            "name": "init_5",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "init 5 (acc/vel/pos, node+180..+180)"
           },
           {
            "name": "delta_0",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "delta 0 por frame (magnitude/norm de vento, node+188..+196)"
           },
           {
            "name": "delta_1",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "delta 1 por frame (magnitude/norm de vento, node+192..+196)"
           },
           {
            "name": "delta_2",
            "offset": 48,
            "width": 4,
            "type": "f32",
            "semantics": "delta 2 por frame (magnitude/norm de vento, node+196..+196)"
           }
          ],
          "usage": "Vento: init acc/vel/pos + deltas por frame + magnitude/norm [ONDA1b decompile: FieldMap_ApplyNodeVelocity \u2014 id u32@+0, 6xf32 init@+16..+36 (2x triplas acc/vel/pos), deltas f32@+40/+44/+48; Euler vel+=acc; pos+=vel]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppEiWindFun",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 36,
          "notes": [
           "6x f32 init (+16..+36) node+160..180 + 3x f32 deltas (+40/+44/+48) node+188..196; magnitude/norm de vento; Euler.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppFpPointLight": {
          "opcode": "pppFpPointLight",
          "handler_addr": "0x75D8E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "ilumina\u00e7\u00e3o (7) [ONDA6.18: addr 0x75D8E0 = FieldMap_DebugOverlayRenderTriangle (debug, NAO handler de luz) \u2014 addr errado] [ONDA7 sweep: debug overlay tri (f32@+4, u8@+8, f32@+12)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppFpPointLight",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppFpPointLightModel": {
          "opcode": "pppFpPointLightModel",
          "handler_addr": "0x75DA20",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "ilumina\u00e7\u00e3o (7) [ONDA7 sweep: debug overlay mesh (+u32 id@+16)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppFpPointLightModel",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppFpPointLightModelScl": {
          "opcode": "pppFpPointLightModelScl",
          "handler_addr": "0x75DE60",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "ilumina\u00e7\u00e3o (7) [ONDA7 sweep: debug overlay rect (+u32 id@+16)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppFpPointLightModelScl",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppFpPointLightVsf": {
          "opcode": "pppFpPointLightVsf",
          "handler_addr": "0x75DC20",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 14
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_16",
            "offset": 16,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "ilumina\u00e7\u00e3o (7) [ONDA7 sweep: light: size f32@+4, flag b@+8, sort f32@+12, mesh s16@+16]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppFpPointLightVsf",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppFpPointLightVsfScl": {
          "opcode": "pppFpPointLightVsfScl",
          "handler_addr": "0x75E080",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 14
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_16",
            "offset": 16,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "ilumina\u00e7\u00e3o (7) [ONDA7 sweep: idem Vsf + scale extra h[+12]]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppFpPointLightVsfScl",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppKeBornRnd": {
          "opcode": "pppKeBornRnd",
          "handler_addr": "0x7585C0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 21
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s16_4",
            "offset": 4,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_6",
            "offset": 6,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_20",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "randomiza\u00e7\u00e3o de nascimento de part\u00edculas [ONDA6.16 sweep: nascimento random (le a2+20)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeBornRnd2": {
          "opcode": "pppKeBornRnd2",
          "handler_addr": "0x7588A0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 25
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_u16_4",
            "offset": 4,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_13",
            "offset": 13,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_14",
            "offset": 14,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_24",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "randomiza\u00e7\u00e3o de nascimento de part\u00edculas [ONDA6.16 sweep: nascimento random v2 (le a2+24)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd2",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeBornRnd3": {
          "opcode": "pppKeBornRnd3",
          "handler_addr": "0x758A50",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 20
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_nascimento",
            "offset": 4,
            "width": 2,
            "type": "u16",
            "semantics": "parametro passado ao callback de trigger zone (nascimento)"
           },
           {
            "name": "param_byte_6",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "parametro byte passado ao callback"
           },
           {
            "name": "mask_gate",
            "offset": 7,
            "width": 1,
            "type": "u8",
            "semantics": "mask byte: != 0xFF e testado com bitmask (v1 & byte) como gate do handler"
           },
           {
            "name": "recurso_1",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "id de recurso/slot 1: deve ser != -1 (gate)"
           },
           {
            "name": "recurso_2",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "id de recurso/slot 2: deve ser != -1 (gate)"
           }
          ],
          "usage": " [ONDA7 sweep: born rnd zone: u16@+4, b@+6/7/8, dw@+12/+20]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd3",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Render"
         },
         "pppKeBornRnd5": {
          "opcode": "pppKeBornRnd5",
          "handler_addr": "0x759010",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 21
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "countdown",
            "offset": 4,
            "width": 2,
            "type": "u16",
            "semantics": "u16@+4 \u2014 parametro de trigger (nascimento)"
           },
           {
            "name": "flag_active",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+12 \u2014 flag de ativacao"
           },
           {
            "name": "trigger",
            "offset": 13,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+13 \u2014 trigger de nascimento"
           },
           {
            "name": "flag2",
            "offset": 14,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+14 \u2014 flag secundaria"
           },
           {
            "name": "ref_a",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+16 \u2014 ref de node (-1 = nulo)"
           },
           {
            "name": "ref_b",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+24 \u2014 ref de node (-1 = nulo)"
           }
          ],
          "usage": "nascimento random com trigger zone (decompile Onda 6.5: FieldMap_WalkStructEntry_Sub)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd5",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeBornRnd6": {
          "opcode": "pppKeBornRnd6",
          "handler_addr": "0x7592F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 17
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "countdown",
            "offset": 4,
            "width": 2,
            "type": "u16",
            "semantics": "u16@+4 \u2014 parametro de trigger (nascimento)"
           },
           {
            "name": "flag",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+6 \u2014 flag"
           },
           {
            "name": "trigger",
            "offset": 7,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+7 \u2014 trigger de nascimento"
           },
           {
            "name": "flag2",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+8 \u2014 flag secundaria"
           },
           {
            "name": "ref_a",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+12 \u2014 ref de node (-1 = nulo)"
           },
           {
            "name": "ref_b",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+20 \u2014 ref de node (-1 = nulo)"
           }
          ],
          "usage": "nascimento random com trigger zone (decompile Onda 6.5: FieldMap_WalkStructEntry2_Sub)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd6",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeDrct": {
          "opcode": "pppKeDrct",
          "handler_addr": "0x75E520",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "pos_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+16 \u2014 posicao X (copiada p/ node)"
           },
           {
            "name": "pos_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+20 \u2014 posicao Y"
           },
           {
            "name": "pos_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+24 \u2014 posicao Z"
           }
          ],
          "usage": " [ONDA6.12 decompile: FFX_Pmcom_MatchAndCopyPosition (match id, copia 3x f32 p/ node)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeDrct",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "UI/Debug"
         },
         "pppKeGrvEff": {
          "opcode": "pppKeGrvEff",
          "handler_addr": "0x759740",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeGrvEff (decompile lotes 2E/2F \u2014 acesso real em +4)"
           }
          ],
          "usage": "gravidade do efeito / gravidade direcionada a target [ONDA6.4: addr 0x759740 = FFX_FieldMap_CheckAndRenderObject (NAO handler PPP) \u2014 classificar por outro addr se existir] [ONDA7 sweep: gravidade: idx@+4, base@+8, obj alvo; match a2+12]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b388"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeGrvEff",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Render"
         },
         "pppKeGrvTgt": {
          "opcode": "pppKeGrvTgt",
          "handler_addr": "0x7598A0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "gravidade do efeito / gravidade direcionada a target [ONDA6.16 sweep: gravidade alvo (le a2+8)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeGrvTgt",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeHitBall": {
          "opcode": "pppKeHitBall",
          "handler_addr": "0x759920",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 5
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "impacto/colis\u00e3o (check por pixel) [ONDA6.16 sweep: hit ball (le a2+4)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeHitBall",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeHitChkPxB": {
          "opcode": "pppKeHitChkPxB",
          "handler_addr": "0x759AA0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeHitChkPxB (decompile lotes 2E/2F \u2014 acesso real em +4)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeHitChkPxB (decompile lotes 2E/2F \u2014 acesso real em +8)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeHitChkPxB (decompile lotes 2E/2F \u2014 acesso real em +12)"
           }
          ],
          "usage": "impacto/colis\u00e3o (check por pixel) [ONDA7 sweep: hit chk: idx@+4, base@+8, flag b@+12]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeHitChkPxB",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppKeLnsArnd": {
          "opcode": "pppKeLnsArnd",
          "handler_addr": "0x75A0E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_11",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "efeitos de lente (ao redor, coluna, canto, flash, loop) [ONDA7 sweep: lens arnd: rgba b@+8..11, f32@+12]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3aa28"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsArnd",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Render"
         },
         "pppKeLnsArndT": {
          "opcode": "pppKeLnsArndT",
          "handler_addr": "0x75A280",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_11",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA7 sweep: idem Arnd no slot a2 + match word]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3bb08"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsArndT",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Light/Glare"
         },
         "pppKeLnsClm": {
          "opcode": "pppKeLnsClm",
          "handler_addr": "0x75A490",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 14
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "pos_x_fixo",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "posicao X em fixed-point: multiplicado por scale global (ppvMng+112 u16) e >>12"
           },
           {
            "name": "pos_y_fixo",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "posicao Y: x ppvMng+114 >> 12"
           },
           {
            "name": "pos_z_fixo",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "posicao Z: x ppvMng+116 >> 12"
           },
           {
            "name": "pos_w_fixo",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "posicao W/extra: x ppvMng+118 >> 12"
           },
           {
            "name": "param_float",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "parametro float (a3+12) repassado ao render de particulas"
           },
           {
            "name": "param_u16",
            "offset": 20,
            "width": 2,
            "type": "u16",
            "semantics": "parametro u16 convertido a float (v21) repassado ao render"
           }
          ],
          "usage": " [ONDA7 sweep: lens clm: b@+8..11, f32@+12, u16@+20]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3aa50"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsClm",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Render"
         },
         "pppKeLnsClmT": {
          "opcode": "pppKeLnsClmT",
          "handler_addr": "0x75A630",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 14
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_11",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_20",
            "offset": 20,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA7 sweep: idem Clm no slot a2 + match word]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3bb58"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsClmT",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Light/Glare"
         },
         "pppKeLnsCrn": {
          "opcode": "pppKeLnsCrn",
          "handler_addr": "0x75A850",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "pos_x_fixo",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "pos X em fixo 4.12 (x ppvMng+112 >> 12)"
           },
           {
            "name": "pos_y_fixo",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "pos Y em fixo 4.12 (x ppvMng+114 >> 12)"
           },
           {
            "name": "pos_z_fixo",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "pos Z em fixo 4.12 (x ppvMng+116 >> 12)"
           },
           {
            "name": "pos_w_fixo",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "pos W em fixo 4.12 (x ppvMng+118 >> 12)"
           },
           {
            "name": "param_float",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "parametro float (audio-sync)"
           },
           {
            "name": "s16_16",
            "offset": 16,
            "width": 2,
            "type": "u16",
            "semantics": "parametro s16"
           },
           {
            "name": "s16_18",
            "offset": 18,
            "width": 2,
            "type": "u16",
            "semantics": "parametro s16"
           }
          ],
          "usage": " [ONDA7 sweep: lens crn: b@+8..11, f32@+12, s16@+16, s16@+18]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3aa78"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsCrn",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Render"
         },
         "pppKeLnsCrnT": {
          "opcode": "pppKeLnsCrnT",
          "handler_addr": "0x75A9F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_11",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_16",
            "offset": 16,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_18",
            "offset": 18,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA7 sweep: RGBA(8-11)+float(12)+u16(16/18); aplica cor/pos ao no]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3bb80"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsCrnT",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Light/Glare"
         },
         "pppKeLnsFls": {
          "opcode": "pppKeLnsFls",
          "handler_addr": "0x75AC40",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_11",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_16",
            "offset": 16,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_18",
            "offset": 18,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA7 sweep: RGBA(8-11)+float(12)+u16(16/18); render particles audio-sync]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3aaa0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsFls",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Render"
         },
         "pppKeLnsFlsT": {
          "opcode": "pppKeLnsFlsT",
          "handler_addr": "0x75ADF0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_8",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_9",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_11",
            "offset": 11,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_16",
            "offset": 16,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_18",
            "offset": 18,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA7 sweep: mesmo payload do Fls, apply no no]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3bb30"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsFlsT",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Light/Glare"
         },
         "pppKeLnsLp": {
          "opcode": "pppKeLnsLp",
          "handler_addr": "0x75B070",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 10
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_1",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "parametro float (spring)"
           },
           {
            "name": "param_2",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "parametro float (spring)"
           },
           {
            "name": "modo",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "modo"
           },
           {
            "name": "byte_13",
            "offset": 13,
            "width": 1,
            "type": "u8",
            "semantics": "byte de controle"
           },
           {
            "name": "offset_64",
            "offset": 64,
            "width": 4,
            "type": "f32",
            "semantics": "pos/param (estado)"
           },
           {
            "name": "offset_68",
            "offset": 68,
            "width": 4,
            "type": "f32",
            "semantics": "pos/param (estado)"
           },
           {
            "name": "offset_72",
            "offset": 72,
            "width": 4,
            "type": "f32",
            "semantics": "pos/param (estado)"
           }
          ],
          "usage": " [ONDA7 sweep: float(4/8)+byte(12/13); offset spring/mola da lente]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3aa00"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsLp",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Transform"
         },
         "pppKeLnsLpSft": {
          "opcode": "pppKeLnsLpSft",
          "handler_addr": "0x75A020",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 8
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "lens_shift_x",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "deslocamento suave da lente em X (acumulado no estado do node)"
           },
           {
            "name": "lens_shift_y",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "deslocamento suave da lente em Y (acumulado no estado do node)"
           }
          ],
          "usage": " [ONDA7b sweep: INFRA \u2014 so le a2[0]=target id; acumula transform nos nos internos]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3ac58"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsLpSft",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 8,
          "notes": [
           "FASE B 2026-08-03: decompile 0x75A020 - 2 floats de payload (lens soft shift). Reclassificado de SEM_PAYLOAD para FECHADO."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppKeLnsLpT": {
          "opcode": "pppKeLnsLpT",
          "handler_addr": "0x75B250",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 14,
           "width": 3
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u8_14",
            "offset": 14,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_15",
            "offset": 15,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_16",
            "offset": 16,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA7 sweep: bytes(14/15/16); multiplicadores/shift + byte de estado]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3bae0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeLnsLpT",
          "handler_slot": 12,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Light/Glare"
         },
         "pppKeMatSN": {
          "opcode": "pppKeMatSN",
          "handler_addr": "0x7340F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 169
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeMatSN (decompile lotes 2E/2F \u2014 acesso real em +16)"
           }
          ],
          "usage": " [ONDA6.16 sweep: material SN (le a2+168)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeMatSN",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppKeMdlDtt": {
          "opcode": "pppKeMdlDtt",
          "handler_addr": "0x72E870",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 169
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "keyframe_id",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "id de keyframe/slot do record: comparado com program+0 (id da key atual) p/ aplicar delta"
           },
           {
            "name": "recurso_modelo",
            "offset": 144,
            "width": 4,
            "type": "s32",
            "semantics": "id de recurso (modelo/textura): Std_IdentityFunc(a2+144)"
           },
           {
            "name": "pos_x",
            "offset": 160,
            "width": 4,
            "type": "f32",
            "semantics": "coordenada X de transform: lida+escrita (acumula delta program+8); hi/lo words lidas como s16 fixo p/ render"
           },
           {
            "name": "pos_y",
            "offset": 164,
            "width": 4,
            "type": "f32",
            "semantics": "coordenada Y: lida+escrita (acumula delta program+12)"
           },
           {
            "name": "pos_z",
            "offset": 168,
            "width": 4,
            "type": "f32",
            "semantics": "coordenada Z: lida (acumula delta program+16 se modo<4); se modo>=4 vira ptr de cadeia custom"
           }
          ],
          "usage": " [ONDA6.16 sweep: model detail (le a2+168)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeMdlDtt",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppKeMdlTfd": {
          "opcode": "pppKeMdlTfd",
          "handler_addr": "0x7498E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 49
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_32",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_44",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_48",
            "offset": 48,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_52",
            "offset": 52,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA1b decompile: FFX_KR_AccumulateTableOffset_A \u2014 acumulador KR (mesma estrutura do Uv2/Uv3: 3xVec3 + drawable)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeMdlTfd",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 0x749A20): 9 f32 (X +8/+12/+16, Y +32/+36/+40, Z +44/+48/+52) + u8@+28 + u8@+56; double-layer a1+160..196; build batch drawable.",
           "Corrigido: cat\u00e1logo automatico dizia NAO le payload \u2014 refutado."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppKeShpTail3": {
          "opcode": "pppKeShpTail3",
          "handler_addr": "0x750390",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 32,
           "width": 48
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u16_32",
            "offset": 32,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_34",
            "offset": 34,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_36",
            "offset": 36,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_38",
            "offset": 38,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_40",
            "offset": 40,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_42",
            "offset": 42,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_44",
            "offset": 44,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_46",
            "offset": 46,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_48",
            "offset": 48,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_50",
            "offset": 50,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_52",
            "offset": 52,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_54",
            "offset": 54,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_56",
            "offset": 56,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_58",
            "offset": 58,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_60",
            "offset": 60,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_62",
            "offset": 62,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_64",
            "offset": 64,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_66",
            "offset": 66,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_68",
            "offset": 68,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_70",
            "offset": 70,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_72",
            "offset": 72,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_74",
            "offset": 74,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_76",
            "offset": 76,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_78",
            "offset": 78,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "caudas de shape [ONDA7 sweep: 24x u16 (32..78); deltas/offsets da cauda]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3ac30"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeShpTail3",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "KR-resource"
         },
         "pppKeShpTail3X": {
          "opcode": "pppKeShpTail3X",
          "handler_addr": "0x751D80",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 32,
           "width": 48
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u16_32",
            "offset": 32,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_34",
            "offset": 34,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_36",
            "offset": 36,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_38",
            "offset": 38,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_40",
            "offset": 40,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_42",
            "offset": 42,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_44",
            "offset": 44,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_46",
            "offset": 46,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_48",
            "offset": 48,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_50",
            "offset": 50,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_52",
            "offset": 52,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_54",
            "offset": 54,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_56",
            "offset": 56,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_58",
            "offset": 58,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_60",
            "offset": 60,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_62",
            "offset": 62,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_64",
            "offset": 64,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_66",
            "offset": 66,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_68",
            "offset": 68,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_70",
            "offset": 70,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_72",
            "offset": 72,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_74",
            "offset": 74,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_76",
            "offset": 76,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_78",
            "offset": 78,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "caudas de shape [ONDA7 sweep: idem Tail3; acumula frame delta + 24x u16]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b860"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeShpTail3X",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppKeShpTailPht": {
          "opcode": "pppKeShpTailPht",
          "handler_addr": "0x754000",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 28,
           "width": 68
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_float_28",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_32",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_48",
            "offset": 48,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_50",
            "offset": 50,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_52",
            "offset": 52,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_54",
            "offset": 54,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_56",
            "offset": 56,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_58",
            "offset": 58,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_60",
            "offset": 60,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_62",
            "offset": 62,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_64",
            "offset": 64,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_66",
            "offset": 66,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_68",
            "offset": 68,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_70",
            "offset": 70,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_72",
            "offset": 72,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_74",
            "offset": 74,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_76",
            "offset": 76,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_78",
            "offset": 78,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_80",
            "offset": 80,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_82",
            "offset": 82,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_84",
            "offset": 84,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_86",
            "offset": 86,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_88",
            "offset": 88,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_90",
            "offset": 90,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_92",
            "offset": 92,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_94",
            "offset": 94,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "caudas de shape [ONDA6.13 decompile 0x754000: init de array de structs (KR table)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3dea8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeShpTailPht",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Draw"
         },
         "pppKeTh": {
          "window": {
           "start": 16,
           "width": 72
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337, 0x736110): +16..+47 16x u16 canais bone, +48..+83 9x f32, +84 u16 angulo deg->rad, +86/+87 u8 flags. Acessos +356..+368 = programa (nao payload). Correcao: schema antigo apontava 0x736F50 (que e o KeThSft)."
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x736F50",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_KeTh (grow de thread de animacao); janela derivada mantida]",
          "fields": [
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeTh (decompile lotes 2E/2F \u2014 acesso real em +16)"
           },
           {
            "name": "param_s32_18",
            "offset": 18,
            "width": 4,
            "type": "s32",
            "semantics": "pppKeTh (decompile lotes 2E/2F \u2014 acesso real em +18)"
           }
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppKeThHitBorn": {
          "opcode": "pppKeThHitBorn",
          "handler_addr": "0x759E70",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "gest\u00e3o de threads de anima\u00e7\u00e3o [ONDA6.11: 0x759E70 = FieldMap_WalkStructTransparencyNodes (le a2+12 u8) \u2014 payload marginal, manter SEM_PAYLOAD] [ONDA7 sweep: FieldMap_WalkStructTransparencyNodes: s32@+4 target, s32@+8 offset node, u8@+12 flag]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3e218"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeThHitBorn",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Behavior"
         },
         "pppKeThSft": {
          "window": {
           "start": 8,
           "width": 49
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337, 0x736F50): +8..+38 16x u16 canais, +40/+44/+48/+52 4x f32, +56 u8 flag. (Schema antigo de pppKeTh aplicado corretamente ao KeThSft.)"
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x7371F0",
          "usage": " [ONDA1b addr resolvido pppKeThSftCon (construtor) / handler na cadeia KeTh; janela derivada mantida]",
          "fields": [
           {
            "name": "param_u16_8",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_10",
            "offset": 10,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_12",
            "offset": 12,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_14",
            "offset": 14,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_16",
            "offset": 16,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_18",
            "offset": 18,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_20",
            "offset": 20,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_22",
            "offset": 22,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_24",
            "offset": 24,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_26",
            "offset": 26,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_28",
            "offset": 28,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_30",
            "offset": 30,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_32",
            "offset": 32,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_34",
            "offset": 34,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_36",
            "offset": 36,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u16_38",
            "offset": 38,
            "width": 2,
            "type": "u16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_44",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_48",
            "offset": 48,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_52",
            "offset": 52,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_56",
            "offset": 56,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppKeThTp": {
          "opcode": "pppKeThTp",
          "handler_addr": "0x736E40",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 20
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "pos_x",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+4 \u2014 posicao X (set transform)"
           },
           {
            "name": "pos_y",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+8 \u2014 posicao Y"
           },
           {
            "name": "pos_z",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+12 \u2014 posicao Z"
           },
           {
            "name": "ref_a",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+16 \u2014 ref de node (-1 = nulo)"
           },
           {
            "name": "ref_b",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+20 \u2014 ref de node (-1 = nulo)"
           }
          ],
          "usage": "gest\u00e3o de threads de anima\u00e7\u00e3o [ONDA6.11 decompile 0x736E40: set transform + acumula em node]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3ae38"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeThTp",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppKeThTp2": {
          "opcode": "pppKeThTp2",
          "handler_addr": "0x736E40",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 20
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_20",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "gest\u00e3o de threads de anima\u00e7\u00e3o [ONDA7 sweep: FieldMap_SetTransformAndAccumulate (alias do KeThTp): pos xyz f32@+4/8/12 + refs@+16/20]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3dc78"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppKeThTp2",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Behavior"
         },
         "pppKeZCrct": {
          "opcode": "pppKeZCrct",
          "handler_addr": "0x735230",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 49
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_28",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_32",
            "offset": 32,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_44",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_48",
            "offset": 48,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "corre\u00e7\u00e3o de profundidade Z [ONDA6.16 sweep: correcao Z (le a2+48)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppKeZCrct",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppKeZCrctShp": {
          "payload_consumer": true,
          "window": {
           "start": 32,
           "width": 16
          },
          "notes": [
           "KNOB de LUZ provado 2026-08-02 (0x735590): +32/+36/+40/+44 4xf32 luz; monta matriz lighting em node+160."
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x735590",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_KeZCrctShp (SetupLightingConstants \u2014 4xf32 luz, rodada anterior); janela derivada mantida]",
          "fields": [
           {
            "name": "param_float_32",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_44",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "outro"
         },
         "pppMatrixScl": {
          "opcode": "pppMatrixScl",
          "handler_addr": "0x734240",
          "payload_consumer": true,
          "editable": true,
          "match_word": null,
          "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
          "fields": [
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_36",
            "offset": 36,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_56",
            "offset": 56,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_64",
            "offset": 64,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_68",
            "offset": 68,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_72",
            "offset": 72,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "transforma\u00e7\u00f5es (8)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A7A8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppMatrixScl",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload. Copia posicoes dual-node: node[1]+160..168 -> a3[4]/[9]/[14]; node[0]+160..168 -> a3[16..18] (a3 = buffer de saida).",
           "Sem guard, sem match. Chamada FFX_BtlUI_HudParty_GetElement = provavel mis-identification do decompilador.",
           "Nome antigo FFX_MagicHost_CopyDualReferencePosition -> FFX_PppHandler_MatrixScl.",
           "NAO le payload do record (handler de transform/draw/allocation \u2014 decompile prova). Janela 0 e correta; nenhum operando editavel."
          ],
          "semantics_category": "outro",
          "window_note": "A1 RE 2026-08-12: window decompilada {64,12} (CopyDualReferencePosition) N?O aplicada como edit?vel ? teste RT0 espera FieldWindow==null; matrizes s?o exib?veis."
         },
         "pppMove": {
          "opcode": "pppMove",
          "handler_addr": "0x75BED0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta de movimento X por frame (double-layer)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta de movimento Y por frame (double-layer)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta de movimento Z por frame (double-layer)"
           },
           {
            "name": "delta_w",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta de movimento W por frame (double-layer)"
           }
          ],
          "usage": "Movimento/translac\u00e3o (byte-id\u00eantico ao Accele)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A5A0"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppMove",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "double",
          "notes": [
           "Double-layer (mesmo padrao Accele). Raw 20B vs window 16B: 4B extras do payload nao sao lidos por este handler (dword1 do prefixo + possivel param de outro handler).",
           "BYTE-IDENTICO a 0x75B830 (pppAccele).",
           "Aux 0x75BFA0: SetFromGlobals layerB <- flt_C0A004..flt_C0A010."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppMoveLoop": {
          "opcode": "pppMoveLoop",
          "handler_addr": "0x75C1A0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+16 \u2014 delta X por frame (loop)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+20 \u2014 delta Y por frame (loop)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+24 \u2014 delta Z por frame (loop)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.10 decompile: FieldMap_AccumulateTripleLayerDelta (3x f32@+16/20/24, camada tripla node0+=node1+=delta)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3d660"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppMoveLoop",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppNeiLightEikyo": {
          "opcode": "pppNeiLightEikyo",
          "handler_addr": "0x75D6C0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 25
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "ilumina\u00e7\u00e3o (7) [ONDA6.16 sweep: luz vizinha (le a2+24)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppNeiLightEikyo",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppNeiPointLight": {
          "opcode": "pppNeiPointLight",
          "handler_addr": "0x75D7F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "delta de luz x por frame (acc->vel->pos, node+160)"
           },
           {
            "name": "delta_y",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta de luz y por frame (acc->vel->pos, node+164)"
           },
           {
            "name": "delta_z",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta de luz z por frame (acc->vel->pos, node+168)"
           }
          ],
          "usage": "Point light de vizinhan\u00e7a (acc/vel/pos + handles) [ONDA1b decompile: FieldMap_AccumulateVelocityWithHandle \u2014 id u32@+0, f32@+4/+8/+12 += node acc/vel/pos; handles@node+172/+176]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppNeiPointLight",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 12,
          "notes": [
           "3x f32 (+4/+8/+12) += node+160/164/168; node+172/+176 = handles vizinhos; Euler acc->vel->pos.",
           "Decompile completo 2026-07-31 (IDA 13338)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppParMatrix": {
          "opcode": "pppParMatrix",
          "handler_addr": "0x734710",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 5
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "transforma\u00e7\u00f5es (8) [ONDA6.16 sweep: parent matrix (le a2+4)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppParMatrix",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppPoint": {
          "opcode": "pppPoint",
          "handler_addr": "0x75C540",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta de posicao X por frame (single-layer)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta de posicao Y por frame (single-layer)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta de posicao Z por frame (single-layer)"
           },
           {
            "name": "delta_w",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta de posicao W por frame (single-layer)"
           }
          ],
          "usage": "Posi\u00e7\u00e3o/ponto (single-layer)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A640"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppPoint",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "single",
          "notes": [
           "SINGLE-layer: node[0]+a1+160..172 += program+16..28 f32[4] (somente no match). Sem propagacao entre camadas.",
           "BYTE-IDENTICO a 0x75D0D0 (pppScale). 4.306 samples = maior familia U1 \u2014 melhor candidato a proxima T3.",
           "Aux 0x75C5B0: SetFromGlobals node[0] <- flt_C0A004..flt_C0A010 (single-layer usa node[0], nao node[1]). Nome antigo ResetBoneToDefault enganoso \u2014 nao reseta, seta globais."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppPointAp": {
          "opcode": "pppPointAp",
          "handler_addr": "0x7574B0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 5
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "pppPointAp (decompile lotes 2E/2F \u2014 acesso real em +4)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "pppPointAp (decompile lotes 2E/2F \u2014 acesso real em +8)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.16 sweep: point apply (le a2+4)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3ab68"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppPointAp",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppPointLoop": {
          "opcode": "pppPointLoop",
          "handler_addr": "0x75C5F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.16 sweep: FFX_BoneAnim_CheckAndApplyTransform \u2014 3x f32@+16/20/24]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b568"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppPointLoop",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Transform"
         },
         "pppPointRAp": {
          "opcode": "pppPointRAp",
          "handler_addr": "0x757580",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 29
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_16",
            "offset": 16,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_24",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_28",
            "offset": 28,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.16 sweep: point reverse apply (le a2+28)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3e100"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppPointRAp",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppRandDownFV": {
          "opcode": "pppRandDownFV",
          "handler_addr": "0x730FD0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandDownFV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandDownFloat": {
          "opcode": "pppRandDownFloat",
          "handler_addr": "0x72F930",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandDownFloat",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandDownHCV": {
          "opcode": "pppRandDownHCV",
          "handler_addr": "0x732460",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_8",
            "offset": 8,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_10",
            "offset": 10,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_12",
            "offset": 12,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_14",
            "offset": 14,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_16",
            "offset": 16,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandDownHCV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandDownIV": {
          "opcode": "pppRandDownIV",
          "handler_addr": "0x731640",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandDownIV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandFV": {
          "opcode": "pppRandFV",
          "handler_addr": "0x730BA0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandFV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandFloat": {
          "opcode": "pppRandFloat",
          "handler_addr": "0x72F590",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandFloat",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandHCV": {
          "opcode": "pppRandHCV",
          "handler_addr": "0x731F90",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 9
          },
          "match_word": null,
          "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
          "fields": [
           {
            "name": "range_0",
            "offset": 8,
            "width": 2,
            "type": "s16",
            "semantics": "range de randomizacao do canal 0 (canal i += range_i*(rand-1))"
           },
           {
            "name": "range_1",
            "offset": 10,
            "width": 2,
            "type": "s16",
            "semantics": "range de randomizacao do canal 1 (canal i += range_i*(rand-1))"
           },
           {
            "name": "range_2",
            "offset": 12,
            "width": 2,
            "type": "s16",
            "semantics": "range de randomizacao do canal 2 (canal i += range_i*(rand-1))"
           },
           {
            "name": "range_3",
            "offset": 14,
            "width": 2,
            "type": "s16",
            "semantics": "range de randomizacao do canal 3 (canal i += range_i*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 16,
            "width": 1,
            "type": "u8",
            "semantics": "flag RNG (0 = rand unico; !=0 = soma de 2 rands)"
           }
          ],
          "usage": "Random half-color-vector (RNG real srand/RandomFloat01)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A550-alias"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppRandHCV",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 9,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337, 0x731F90): +8..+15 4x s16 ranges + +16 u8 flag = 9B; RNG real srand/RandomFloat01; state160 = rand 0..~512; canal i += range_i*(rand-1)."
          ],
          "semantics_category": "Random"
         },
         "pppRandIV": {
          "opcode": "pppRandIV",
          "handler_addr": "0x7311E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandIV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandUpFV": {
          "opcode": "pppRandUpFV",
          "handler_addr": "0x730DD0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandUpFV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppRandUpHCV": {
          "opcode": "pppRandUpHCV",
          "handler_addr": "0x732200",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_8",
            "offset": 8,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_10",
            "offset": 10,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_12",
            "offset": 12,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_14",
            "offset": 14,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppRandUpHCV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppSRandCV": {
          "opcode": "pppSRandCV",
          "handler_addr": "0x732EA0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppSRandCV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Transform"
         },
         "pppSRandDownFV": {
          "opcode": "pppSRandDownFV",
          "handler_addr": "0x732C10",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppSRandDownFV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppSRandDownHCV": {
          "opcode": "pppSRandDownHCV",
          "handler_addr": "0x733DE0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 13
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+4 \u2014 offset node alvo (-1 = default)"
           },
           {
            "name": "ranges",
            "offset": 8,
            "width": 8,
            "type": "u16",
            "semantics": "4x s16 ranges @+8..+15 (4 canais)"
           },
           {
            "name": "param_s16_10",
            "offset": 10,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_12",
            "offset": 12,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_14",
            "offset": 14,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "ease",
            "offset": 16,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+16 \u2014 parametro de ease"
           }
          ],
          "usage": "aleat\u00f3rio (16) [ONDA6.18 decompile: FFX_MagicHost_ApplyTransformEase_I (variante Down)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppSRandDownHCV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppSRandFV": {
          "opcode": "pppSRandFV",
          "handler_addr": "0x7326C0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 33
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16) [ONDA6.16 sweep: SRandFV EXISTE (addr 0x7326C0, le a2+32) \u2014 CORRECAO do fantasma]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppSRandFV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppSRandHCV": {
          "opcode": "pppSRandHCV",
          "handler_addr": "0x7337B0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 13
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "s32@+4 \u2014 offset node alvo (-1 = default)"
           },
           {
            "name": "ranges",
            "offset": 8,
            "width": 8,
            "type": "u16",
            "semantics": "4x s16 ranges @+8..+15 (4 canais)"
           },
           {
            "name": "param_s16_10",
            "offset": 10,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_12",
            "offset": 12,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_14",
            "offset": 14,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "ease",
            "offset": 16,
            "width": 1,
            "type": "u8",
            "semantics": "u8@+16 \u2014 parametro de ease"
           }
          ],
          "usage": "aleat\u00f3rio (16) [ONDA6.18 decompile: FFX_MagicHost_ApplyTransformEase_G (4x s16 ranges, ease random)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppSRandHCV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppSRandUpFV": {
          "opcode": "pppSRandUpFV",
          "handler_addr": "0x732980",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 32
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "aleat\u00f3rio (16)",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppSRandUpFV",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio).",
           "KNOB provado 2026-08-02 por decompile (lote 2 Rand)"
          ],
          "semantics_category": "Random"
         },
         "pppScale": {
          "opcode": "pppScale",
          "handler_addr": "0x75D0D0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "scale_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "fator de escala X acumulado por frame do bone"
           },
           {
            "name": "scale_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "fator de escala Y acumulado por frame do bone"
           },
           {
            "name": "scale_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "fator de escala Z acumulado por frame do bone"
           },
           {
            "name": "scale_w",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "fator de escala W acumulado por frame do bone"
           }
          ],
          "usage": "Escala (acumulador por frame do bone \u2014 T4 observado)",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A690"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppScale",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "layer_mode": "single",
          "notes": [
           "SINGLE-layer (igual Point). BYTE-IDENTICO a 0x75C540 (pppPoint).",
           "T4 observado 2026-07-31: mutacao 42.5->2.0 deixou cast do Power Break visivelmente mais lento (acumulador por frame do bone).",
           "Aux 0x75D140: SetFromGlobals node[0] <- flt_C0A004..flt_C0A010."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppScaleLoop": {
          "opcode": "pppScaleLoop",
          "handler_addr": "0x75D180",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.16 sweep: idem loop \u2014 3x f32@+16/20/24]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b5b8"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppScaleLoop",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Transform"
         },
         "pppSclAccele": {
          "opcode": "pppSclAccele",
          "handler_addr": "0x75B9F0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular)",
          "fields": [
           {
            "name": "accel_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao de escala X por frame (double-layer)"
           },
           {
            "name": "accel_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao de escala Y por frame (double-layer)"
           },
           {
            "name": "accel_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao de escala Z por frame (double-layer)"
           },
           {
            "name": "accel_w",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "aceleracao de escala W por frame (double-layer)"
           }
          ],
          "usage": "Acelera\u00e7\u00e3o de escala",
          "status": "FECHADO",
          "entry_addrs": [
           {
            "table": 0,
            "entry_addr": "0xC3A550",
            "index": 2,
            "slot": 8,
            "name_ptr": "0xB50720"
           },
           {
            "table": 0,
            "entry_addr": "0xC3BBF8",
            "index": 147,
            "slot": 8,
            "alias_of": "0xC3A550"
           },
           {
            "table": 1,
            "entry_addr": "0xC3EE28",
            "slot": 8
           },
           {
            "table": 2,
            "entry_addr": "0xC860F8",
            "index": 3,
            "slot": 8,
            "name_ptr": "0xB50720"
           }
          ],
          "handler_name_canonical": "FFX_BoneAnim_AccumulateDoubleLayerFloat4",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "notes": [
           "CONTRATO FECHADO (handler gate C2): handler em +0x08 (program-level, 3 args: a1=contexto, a2=program, a3=entry/slot).",
           "Window real = program+16..+28 (16B, f32[4]) - IDENTICO a pppSclMove/pppScale. No record de 32B do callback: record+0x10..+0x1F.",
           "DIFF vs pppScale (0x75D0D0): SclAccele escreve em DOIS vetores (double layer). layerB += delta (persiste entre frames, cresce linear); layerA += layerB (cresce quadratico) => aceleracao de escala do bone. pppScale acumula num vetor unico.",
           "Match word, guard, args, window sao identicos ao padrao provado pppScale/BoneAnim.",
           "+0x1C/+0x20 = FFX_PppHandler_SclAccele_SetFromGlobals: inicializa layerB a partir de flt_C0A004..flt_C0A010 (mesmos globais do FFX_PppHandler_Accele_SetFromGlobalQ 0x75B900) - confirma hipotese do PPP_DISPATCH_TABLE_RE \u00a73.3 para opcodes U1.",
           "Nomes antigos 'FieldMap_*' eram ENGANOSOS (nao e FieldMap; e sistema FFX_BoneAnim). Renames aplicados na COPY 2026-07-31; propagar para DB canonica.",
           "raw_width_yonishi=24 inclui header/type do sample; window mutavel = 16B (width_reconciled=16)."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppSclMove": {
          "opcode": "pppSclMove",
          "handler_addr": "0x75C090",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 16
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag@0x230FD34",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta de escala X por frame (double-layer)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "delta de escala Y por frame (double-layer)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "delta de escala Z por frame (double-layer)"
           },
           {
            "name": "delta_w",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "delta de escala W por frame (double-layer)"
           }
          ],
          "usage": "Movimento de escala (double-layer) \u2014 T3/T4 PROVADOS",
          "status": "FECHADO",
          "entry_addrs": [
           "0xC3A5F0",
           "0xC86198"
          ],
          "handler_name_canonical": "FFX_PppHandler_SclMove",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": 24,
          "width_reconciled": 16,
          "notes": [
           "width_reconciled: record runtime 32B = 8B prefixo (dword0 = match word, dword1 nao lido pelo handler) + 24B payload; operandos em payload+8 = record+0x10..+0x1F (16B = 4 floats). Bate com T3 (janela [record+0x10, record+0x20)) e com U1_PROVEN_SCHEMAS (raw_payload_width=24, runtime_operand_offset=8, runtime_operand_width=16, callback_record_width=32).",
           "DECISAO de nomenclatura: FFX_PppHandler_SclMove (nao FFX_BoneAnim_*) - segue o padrao ja aplicado no dispatcher (FFX_PppHandler_Accele); nome FieldMap_* antigo era enganoso. O handler E a variante double-layer do padrao BoneAnim ApplyDelta (guard+match+float4 identicos a 0x75D0D0), mas escreve em 2 camadas (record[0] e record[1] de a3+12) em vez de 1.",
           "Estado escrito: MESMO vetor do pppScale/pppAngle (+160..+172 decimal = +0xA0..+0xAC hex) - bone/node state vector, em 2 camadas cujos offsets vem do record em a3+12.",
           "Handler COMPARTILHADO: t0 entry 151 = pppColMove e t1 entry 50 = pppMoveLoop apontam para 0x75C090. CORRECAO ao doc do dispatcher: 'pppSclMove idx 151' e na verdade pppColMove (name_ptr 0xB50848=b'pppColMove').",
           "Aux 0x75C160: mesmo fn em +0x1C (resource alloc) e +0x20 (section callback), como pppAccele (0x75B900). Sem guard, sem match word, sem leitura de operandos - apenas inicializa o estado do layer1 com a posicao global F.",
           "Retorno do handler: v5+a1 (ptr layer1) normal; record ptr quando pausado. Nao observado uso critico do retorno.",
           "Cross-check writer: U1_PROVEN_SCHEMAS['pppSclMove'] (host_handler 0x75C090, raw_payload_width 24, FLOAT4, runtime_operand_offset 8, runtime_operand_width 16, callback_record_width 32) BATE 100% com o contrato. Sem discrepancia para pppSclMove.",
           "DISCREPANCIA potencial para C3: DIRECT_OPERAND_SCHEMAS['pppColMove'] assume handler 0x75C480 (word4, janela 8B), mas a t0 entry 151 de pppColMove usa 0x75C090 (float4, janela 16B). O writer valida so handler_index local; se um fp.h local ligar pppColMove a 0x75C090, o schema DIRECT (width 8) seria insuficiente. Resolver handler real por entry do fp.h local antes de writer de pppColMove."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppSclMoveLoop": {
          "opcode": "pppSclMoveLoop",
          "handler_addr": "0x75C380",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 16,
           "width": 12
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "delta_x",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+16 \u2014 delta X por frame (loop)"
           },
           {
            "name": "delta_y",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+20 \u2014 delta Y por frame (loop)"
           },
           {
            "name": "delta_z",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "f32@+24 \u2014 delta Z por frame (loop)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.10 decompile: FFX_BoneAnim_ApplyDeltaFloat3 (3x f32@+16/20/24, idem)]",
          "status": "FECHADO",
          "entry_addrs": [
           "0xc3b748"
          ],
          "handler_name_canonical": "FFX_PppHandler_pppSclMoveLoop",
          "handler_slot": 8,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio); contrato de dispatch provado. Decompile disponivel no PppHandlerBehaviorCatalog."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppVertexAp": {
          "opcode": "pppVertexAp",
          "handler_addr": "0x757930",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 5
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "flag_frame",
            "offset": 4,
            "width": 2,
            "type": "u16",
            "semantics": "bit15 (0x8000)=anim desligada/parada (retorna sem atualizar); low15 = frame/param inicial (usado como indice)"
           },
           {
            "name": "frame_count",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "contador de frames (loops): numero de batches capturados por update"
           },
           {
            "name": "timer",
            "offset": 7,
            "width": 1,
            "type": "u8",
            "semantics": "valor do timer: gravado no record runtime em +162 (decrementado por frame ate 0)"
           },
           {
            "name": "modo",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "modo de anim: 0=sequencial (frame++ com reset por limite da tabela), 1=random (sorteia frames com RNG)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA7 sweep: FFX_MagicHost_UpdateVfxRandomAnimation: u16@+4 indice (bit 0x8000), u8@+6/7/8 (count/delay/modo)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppVertexAp",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Random"
         },
         "pppVertexApAt": {
          "opcode": "pppVertexApAt",
          "handler_addr": "0x7583D0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 5
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "model_index",
            "offset": 4,
            "width": 2,
            "type": "u16",
            "semantics": "\u00edndice do modelo/vertex (bit 0x8000 = flag de paridade)"
           },
           {
            "name": "vertex_count",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "n\u00ba de v\u00e9rtices a aplicar (loop de a2+6)"
           },
           {
            "name": "delay_frames",
            "offset": 7,
            "width": 1,
            "type": "u8",
            "semantics": "delay p/ node+162 (decrementa por frame); 0 \u2192 re-executa quando id bate"
           },
           {
            "name": "order_mode",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "0 = sequencial \u00b7 1 = RNG (srand + sub_72F500)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.6 decompile 0x7583D0: vertex apply com timing + random]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppVertexApAt",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 5,
          "notes": [
           "DECOMPILE 2026-08-02 (0x7583D0): payload de record real l\u00ea a2+4(u16), +6(u8), +7(u8), +8(u8).",
           "Corrigido de FECHADO nominal (fields vazio) para FECHADO com 4 campos edit\u00e1veis."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppVertexApDisPos": {
          "opcode": "pppVertexApDisPos",
          "handler_addr": "0x757D20",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 2
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "pppVertexApDisPos (decompile lotes 2E/2F \u2014 acesso real em +4)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.7 decompile: FFX_KR_ProcessAnimWithCaptureBatch (u16 id@+0, u16 indice@+4 bit 0x8000)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppVertexApDisPos",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "KR-resource"
         },
         "pppVertexApLc": {
          "opcode": "pppVertexApLc",
          "handler_addr": "0x7580E0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 2
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "pppVertexApLc (decompile lotes 2E/2F \u2014 acesso real em +4)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.7 decompile: FFX_KR_ProcessAnimWithCaptureBatch_B (variante B, mesmo padrao)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppVertexApLc",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "KR-resource"
         },
         "pppVertexAttend": {
          "opcode": "pppVertexAttend",
          "handler_addr": "0x75D430",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 13
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s16_12",
            "offset": 12,
            "width": 2,
            "type": "s16",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.16 sweep: vertex attend (le a2+12)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppVertexAttend",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Field/Node/Anim"
         },
         "pppVtMime": {
          "opcode": "pppVtMime",
          "handler_addr": "0x72EC00",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 21
          },
          "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "bone/transform (24, knobs na se\u00e7\u00e3o A) [ONDA6.16 sweep: vertex mime (le a2+20)]",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppVtMime",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "NAO le payload direto (catalogo payload_indexes vazio)."
          ],
          "semantics_category": "Transform"
         },
         "pppKeHmgEff": {
          "opcode": "pppKeHmgEff",
          "handler_addr": "0x759CE0",
          "handler_exe_dispatch_idx": [
           315
          ],
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 12
          },
          "window_known": false,
          "match_word": null,
          "guard": null,
          "fields": [
           {
            "name": "param_float_4",
            "offset": 4,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_12",
            "offset": 12,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA1 decompile 0x759CE0: KNOB HUD/portrait \u2014 lerp f32@+4, refs node +8/+12, guard PausedFlag, match program[0]==ctx[+12]]",
          "status": "FECHADO",
          "extra": true,
          "corpus_slots": null,
          "corpus_dlls": null,
          "fp_h_tables": 9,
          "yonishi_files": 10,
          "notes": [
           "EXE fn map: handler 0x759CE0 (vizinhos no catalogo: FFX_FieldMap_ApplyOffsetTransform 0x759AA0, FieldMap_AccumulateNodeTransforms 0x75A020).",
           "YONISHI_INVENTORY: 10 arquivos; Kernel; 'Homing effect'.",
           "Nao contado como opcode no CORPUS_AUDIT (fp.h local nao resolveu fn nas DLLs Steam; indice global aproximou outro nome).",
           "Window UNKNOWN: nao esta no catalogo de 139 -> requer decompile de 0x759CE0."
          ],
          "semantics_category": "outro"
         },
         "pppKeMdlTfdUv3": {
          "opcode": "pppKeMdlTfdUv3",
          "handler_addr": "0x74A0C0",
          "handler_exe_dispatch_idx": [
           364
          ],
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 57
          },
          "window_known": false,
          "match_word": null,
          "guard": null,
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_32",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_44",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_48",
            "offset": 48,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_52",
            "offset": 52,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_56",
            "offset": 56,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_57",
            "offset": 57,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_58",
            "offset": 58,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_59",
            "offset": 59,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_60",
            "offset": 60,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": " [ONDA1 decompile 0x74A0C0 (renomeado FFX_PppHandler_KeMdlTfdUv3): drawable@+4, 3xVec3 acumulado @+8..+52, flags@+56..+60, globals@+20/+24, stride@+28 \u2014 janela +4..+60]",
          "status": "FECHADO",
          "extra": true,
          "corpus_slots": null,
          "corpus_dlls": null,
          "fp_h_tables": 2,
          "yonishi_files": 2,
          "notes": [
           "EXE fn map: handler 0x74A0C0 (vizinho: FFX_KR_AccumulateTableOffset_A 0x7498E0 pppKeMdlTfd).",
           "YONISHI_INVENTORY: 2 arquivos; Kernel; 'Model transform data' variante UV3.",
           "Nao contado como opcode no CORPUS_AUDIT.",
           "Window UNKNOWN: requer decompile de 0x74A0C0."
          ],
          "semantics_category": "outro"
         },
         "pppRandChar": {
          "opcode": "pppRandChar",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 6
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x72FB00 pattern D: target u32@+4, delta u8@+8, flag u8@+9]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-01).",
           "Catalogo EXE-only: docs/reverse/PPP_EXE_ONLY_OPCODES_20260801.md"
          ],
          "handler_addr": "0x72FB00",
          "semantics_category": "Random"
         },
         "pppRandUpChar": {
          "opcode": "pppRandUpChar",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 6
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x72FCD0 pattern E: idem RandChar + media 0.5 na flag]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x72FCD0",
          "semantics_category": "Random"
         },
         "pppRandDownChar": {
          "opcode": "pppRandDownChar",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 6
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x72FEB0 pattern F: idem RandChar (sinal invertido)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x72FEB0",
          "semantics_category": "Random"
         },
         "pppRandShort": {
          "opcode": "pppRandShort",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 7
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x730090 pattern G: target u32@+4, delta u16@+8, flag u8@+10]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x730090",
          "semantics_category": "Random"
         },
         "pppRandUpShort": {
          "opcode": "pppRandUpShort",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 7
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandShort + media 0.5 (familia Up)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x730260",
          "semantics_category": "Random"
         },
         "pppRandDownShort": {
          "opcode": "pppRandDownShort",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 7
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_10",
            "offset": 10,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandShort (familia Down)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x730430",
          "semantics_category": "Random"
         },
         "pppRandInt": {
          "opcode": "pppRandInt",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x730610 pattern J: target u32@+4, delta u32@+8, flag u8@+12]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 0x730610/0x731860/0x749A20)."
          ],
          "handler_addr": "0x730610",
          "semantics_category": "Random"
         },
         "pppRandUpInt": {
          "opcode": "pppRandUpInt",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandInt (familia Up)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x7307F0",
          "semantics_category": "Random"
         },
         "pppRandDownInt": {
          "opcode": "pppRandDownInt",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandInt (familia Down)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x7309C0",
          "semantics_category": "Random"
         },
         "pppRandCV": {
          "opcode": "pppRandCV",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x731860 pattern S: target u32@+4, 4x delta s8@+8..+11, flag u8@+12]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 0x730610/0x731860/0x749A20)."
          ],
          "handler_addr": "0x731860",
          "semantics_category": "Random"
         },
         "pppRandUpCV": {
          "opcode": "pppRandUpCV",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandCV (familia Up)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x731AD0",
          "semantics_category": "Random"
         },
         "pppRandDownCV": {
          "opcode": "pppRandDownCV",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 4,
           "width": 9
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "node_target",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "offset do node alvo (-1 = default)"
           },
           {
            "name": "range",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "faixa do random (canal += range*(rand-1))"
           },
           {
            "name": "flag",
            "offset": 9,
            "width": 1,
            "type": "u8",
            "semantics": "0 = 2x um rand; !=0 = soma de 2 rands"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandCV (familia Down)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337)."
          ],
          "handler_addr": "0x731D30",
          "semantics_category": "Random"
         },
         "pppKeMdlTfdUv2": {
          "opcode": "pppKeMdlTfdUv2",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 49
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso (0xFFFF = skip build)"
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao (acc/vel/pos)"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_3",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao (grupo 2)"
           },
           {
            "name": "delta_4",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_5",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_6",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_7",
            "offset": 48,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_8",
            "offset": 52,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "flag_tex",
            "offset": 56,
            "width": 1,
            "type": "u8",
            "semantics": "flag de textura (byte)"
           }
          ],
          "usage": "Draw modelo com acumulacao (EXE-only, familia KeMdlTfd)",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-01).",
           "Catalogo EXE-only: docs/reverse/PPP_EXE_ONLY_OPCODES_20260801.md"
          ],
          "semantics_category": "outro"
         },
         "pppKeMdlTfdUv": {
          "opcode": "pppKeMdlTfdUv",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 49
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag",
          "fields": [
           {
            "name": "key",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "chave de recurso (0xFFFF = skip build)"
           },
           {
            "name": "delta_0",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao (acc/vel/pos)"
           },
           {
            "name": "delta_1",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_2",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_3",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao (grupo 2)"
           },
           {
            "name": "delta_4",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_5",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_6",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_7",
            "offset": 48,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "delta_8",
            "offset": 52,
            "width": 4,
            "type": "f32",
            "semantics": "delta acumulacao"
           },
           {
            "name": "flag_tex",
            "offset": 56,
            "width": 1,
            "type": "u8",
            "semantics": "flag de textura (byte)"
           }
          ],
          "usage": "Draw modelo com acumulacao (EXE-only, familia KeMdlTfd) [ONDA1 familia provada: Uv2 (0x749D60) e Uv3 (0x74A0C0) decompilados \u2014 estrutura identica (acumulador 3xVec3 + batch drawable)]",
          "status": "FECHADO",
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337 \u2014 handler compartilhado 0x749A20 com o base KeMdlTfd 49B)."
          ],
          "handler_addr": "0x749D60",
          "semantics_category": "outro"
         },
         "pppKeMdlTfd2": {
          "window": {
           "start": 4,
           "width": 26
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337 \u2014 difere do padrao da familia!)"
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x749C10",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_KeMdlTfd2; janela derivada mantida]",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_20",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_24",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_28",
            "offset": 28,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_29",
            "offset": 29,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "outro"
         },
         "pppKeMdlTfd3": {
          "window": {
           "start": 4,
           "width": 30
          },
          "payload_consumer": true,
          "notes": [
           "Janela PROVADA por decompile (2026-08-02, IDA 13337 \u2014 difere do padrao da familia!)"
          ],
          "status": "FECHADO",
          "editable": true,
          "handler_addr": "0x749F60",
          "usage": " [ONDA1b addr resolvido FFX_PppHandler_KeMdlTfd3; janela derivada mantida]",
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_20",
            "offset": 20,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_24",
            "offset": 24,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_28",
            "offset": 28,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_29",
            "offset": 29,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_30",
            "offset": 30,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_31",
            "offset": 31,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_33",
            "offset": 33,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "outro"
         },
         "pppRandUpFloat": {
          "window": {
           "start": 4,
           "width": 9
          },
          "payload_consumer": true,
          "notes": [
           "KNOB provado 2026-08-02 (0x72F760): +4 target, +8 f32 scale, +12 flag; alvo += scale*rand."
          ],
          "status": "FECHADO",
          "editable": true,
          "fields": [
           {
            "name": "param_s32_4",
            "offset": 4,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_8",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "Random"
         },
         "pppRandUpIV": {
          "window": {
           "start": 16,
           "width": 17
          },
          "payload_consumer": true,
          "notes": [
           "KNOB provado 2026-08-02 (0x731430): +16/+20/+24 3x int32, +32 flag; alvo[i] += int32_i*rand."
          ],
          "status": "FECHADO",
          "fields": [
           {
            "name": "target",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "target u32@+4 \u2014 offset do campo a mutar (rel. node); -1 = ppvDbgTemp"
           },
           {
            "name": "delta",
            "offset": 8,
            "width": 4,
            "type": "f32",
            "semantics": "delta@+8 \u2014 valor somado a *target (int)(rng * delta - delta)"
           },
           {
            "name": "flag_double_random",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "flag u8 \u2014 1 = double-random (2 sorteios)"
           },
           {
            "name": "param_u8_32",
            "offset": 32,
            "width": 1,
            "type": "u8",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "editable": true,
          "semantics_category": "Random"
         },
         "pppKeOfsPt": {
          "payload_consumer": true,
          "window": {
           "start": 4,
           "width": 40
          },
          "notes": [
           "KNOB provado 2026-08-02 (0x75CE20): +4 target, +16/+20/+24 3xf32 delta, +32/+36/+40 3xf32 set; +44 u16 ref 0xFFFF=noop."
          ],
          "status": "FECHADO",
          "editable": true,
          "fields": [
           {
            "name": "param_float_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_32",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_float_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "outro"
         },
         "pppDrawHook": {
          "opcode": "pppDrawHook",
          "handler_addr": "0x757460",
          "payload_consumer": true,
          "editable": true,
          "status": "FECHADO",
          "fields": [
           {
            "name": "hook_callback",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "valor/indice passado ao callback custom de draw (0 desliga)"
           }
          ],
          "window": {
           "start": 4,
           "width": 4
          },
          "width_reconciled": 4,
          "notes": [
           "FASE B 2026-08-03: decompile 0x757460 - 1 u32 de payload (callback hook). Reclassificado de SEM_PAYLOAD para FECHADO."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppNeiChrPointLight": {
          "opcode": "pppNeiChrPointLight",
          "handler_addr": "0x737280",
          "payload_consumer": true,
          "editable": true,
          "status": "SEM_PAYLOAD",
          "fields": [
           {
            "name": "param_s32_40",
            "offset": 40,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           },
           {
            "name": "param_s32_44",
            "offset": 44,
            "width": 4,
            "type": "s32",
            "semantics": "campo lido pelo handler (decompile)"
           }
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppKeAcmCic": {
          "opcode": "pppKeAcmCic",
          "handler_addr": "0x75B780",
          "payload_consumer": true,
          "editable": true,
          "status": "FECHADO",
          "fields": [
           {
            "name": "mode",
            "offset": 4,
            "width": 1,
            "type": "u8",
            "semantics": "modo do ciclo (2 = ativo; outro = chama sub_644D90)"
           }
          ],
          "notes": [
           "FASE B 2026-08-03: decompile 0x75B780 - 1 u8 de payload (mode). Reclassificado de SEM_PAYLOAD para FECHADO."
          ],
          "window": {
           "start": 4,
           "width": 1
          },
          "width_reconciled": 1,
          "semantics_category": "outro"
         }
        }
""";
    }
}
