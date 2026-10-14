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
        "semantics": "Component X. Acceleration impulse at a matching Q12 timestamp; the acceleration channel is added to velocity each local frame."
      },
      {
        "name": "accel_y",
        "offset": 20,
        "width": 4,
        "type": "f32",
        "semantics": "Component Y. Acceleration impulse at a matching Q12 timestamp; the acceleration channel is added to velocity each local frame."
      },
      {
        "name": "accel_z",
        "offset": 24,
        "width": 4,
        "type": "f32",
        "semantics": "Component Z. Acceleration impulse at a matching Q12 timestamp; the acceleration channel is added to velocity each local frame."
      },
      {
        "name": "accel_w",
        "offset": 28,
        "width": 4,
        "type": "f32",
        "semantics": "Component W. Acceleration impulse at a matching Q12 timestamp; the acceleration channel is added to velocity each local frame."
      }
    ],
    "usage": "Acceleration impulse at a matching Q12 timestamp; the acceleration channel is added to velocity each local frame.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
        "semantics": "Component X. Signed angular-acceleration impulse at a matching Q12 timestamp; that channel is added to angular velocity each local frame."
      },
      {
        "name": "accel_y",
        "offset": 20,
        "width": 4,
        "type": "s32",
        "semantics": "Component Y. Signed angular-acceleration impulse at a matching Q12 timestamp; that channel is added to angular velocity each local frame."
      },
      {
        "name": "accel_z",
        "offset": 24,
        "width": 4,
        "type": "s32",
        "semantics": "Component Z. Signed angular-acceleration impulse at a matching Q12 timestamp; that channel is added to angular velocity each local frame."
      },
      {
        "name": "accel_w",
        "offset": 28,
        "width": 4,
        "type": "s32",
        "semantics": "Component W. Signed angular-acceleration impulse at a matching Q12 timestamp; that channel is added to angular velocity each local frame."
      }
    ],
    "usage": "Signed angular-acceleration impulse at a matching Q12 timestamp; that channel is added to angular velocity each local frame.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
        "semantics": "Component X. Signed angular-velocity impulse at a matching Q12 timestamp; that channel is added to angles each local frame."
      },
      {
        "name": "delta_y",
        "offset": 20,
        "width": 4,
        "type": "s32",
        "semantics": "Component Y. Signed angular-velocity impulse at a matching Q12 timestamp; that channel is added to angles each local frame."
      },
      {
        "name": "delta_z",
        "offset": 24,
        "width": 4,
        "type": "s32",
        "semantics": "Component Z. Signed angular-velocity impulse at a matching Q12 timestamp; that channel is added to angles each local frame."
      },
      {
        "name": "delta_w",
        "offset": 28,
        "width": 4,
        "type": "s32",
        "semantics": "Component W. Signed angular-velocity impulse at a matching Q12 timestamp; that channel is added to angles each local frame."
      }
    ],
    "usage": "Signed angular-velocity impulse at a matching Q12 timestamp; that channel is added to angles each local frame.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
        "semantics": "Component X. Signed native angle delta applied once on a matching Q12 timestamp. No degree conversion or 256-unit default is inferred."
      },
      {
        "name": "angle_y",
        "offset": 20,
        "width": 4,
        "type": "s32",
        "semantics": "Component Y. Signed native angle delta applied once on a matching Q12 timestamp. No degree conversion or 256-unit default is inferred."
      },
      {
        "name": "angle_z",
        "offset": 24,
        "width": 4,
        "type": "s32",
        "semantics": "Component Z. Signed native angle delta applied once on a matching Q12 timestamp. No degree conversion or 256-unit default is inferred."
      },
      {
        "name": "angle_w",
        "offset": 28,
        "width": 4,
        "type": "s32",
        "semantics": "Component W. Signed native angle delta applied once on a matching Q12 timestamp. No degree conversion or 256-unit default is inferred."
      }
    ],
    "usage": "Signed native angle delta applied once on a matching Q12 timestamp. No degree conversion or 256-unit default is inferred.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
            "name": "param_u16_8",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 0 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_10",
            "offset": 10,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 1 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_12",
            "offset": 12,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 2 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_14",
            "offset": 14,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 3 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           }
          ],
          "usage": "Four separate raw color-word impulses. Matching-time accumulation, then native state integration for ColMove/ColAccele.",
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
           "Native callback tables in both preserved and installed executables and exact IDA bytes verified. See PPP_LEGACY_RESIDUAL_CONTRACTS_20261007.md.",
           "State and resource references stay protected; no payload fields are inferred from host-state offsets. Raw word wrap does not prove an absolute color range or a gamma/color-space model."
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
            "name": "param_u16_8",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 0 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_10",
            "offset": 10,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 1 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_12",
            "offset": 12,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 2 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_14",
            "offset": 14,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 3 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           }
          ],
          "usage": "Four separate raw color-word impulses. Matching-time accumulation, then native state integration for ColMove/ColAccele.",
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
           "Native callback tables in both preserved and installed executables and exact IDA bytes verified. See PPP_LEGACY_RESIDUAL_CONTRACTS_20261007.md.",
           "State and resource references stay protected; no payload fields are inferred from host-state offsets. Raw word wrap does not prove an absolute color range or a gamma/color-space model."
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
            "name": "param_u16_8",
            "offset": 8,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 0 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_10",
            "offset": 10,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 1 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_12",
            "offset": 12,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 2 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           },
           {
            "name": "param_u16_14",
            "offset": 14,
            "width": 2,
            "type": "u16",
            "semantics": "Raw channel 3 impulse; native modulo-16-bit accumulation, not an absolute 8-bit color."
           }
          ],
          "usage": "Four separate raw color-word impulses. Matching-time accumulation, then native state integration for ColMove/ColAccele.",
          "status": "FECHADO",
          "entry_addrs": [],
          "handler_name_canonical": "FFX_PppHandler_pppColor",
          "handler_slot": null,
          "args": 3,
          "raw_width_yonishi": null,
          "width_reconciled": 0,
          "notes": [
           "Native callback tables in both preserved and installed executables and exact IDA bytes verified. See PPP_LEGACY_RESIDUAL_CONTRACTS_20261007.md.",
           "State and resource references stay protected; no payload fields are inferred from host-state offsets. Raw word wrap does not prove an absolute color range or a gamma/color-space model."
          ],
          "semantics_category": "Delta/Accum"
         },
         "pppDrawFilter": {
  "opcode": "pppDrawFilter",
  "handler_addr": "0x757370",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 30
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_u32_8",
      "offset": 8,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_u32_12",
      "offset": 12,
      "width": 4,
      "type": "u32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u32_16",
      "offset": 16,
      "width": 4,
      "type": "u32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u32_20",
      "offset": 20,
      "width": 4,
      "type": "u32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u32_24",
      "offset": 24,
      "width": 4,
      "type": "u32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_s32_28",
      "offset": 28,
      "width": 4,
      "type": "s32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_s32_32",
      "offset": 32,
      "width": 4,
      "type": "s32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_37",
      "offset": 37,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    }
  ],
  "usage": "Filtro de projeção com wrap X/Y (<<8) — SEM match word [ONDA1b decompile: FFX_BattleModel_ClampWrapProjectionOffsets — wrapX u32@+12<<8, wrapY@+16<<8, deltas s32@+28/+32, clamp wraparound; NAO checa id]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawFilter",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 24,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
  ],
  "semantics_category": "outro"
},
         "pppDrawMdl": {
    "opcode": "pppDrawMdl",
    "handler_addr": "0x737830",
    "payload_consumer": false,
    "editable": false,
    "window": null,
    "match_word": "program[0] == ctx[+12]",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
    "fields": [],
    "usage": "Draw builder. Reads the protected record resource selector at +4; transform, model and texture state comes from the host context.",
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
      "Verified in VM IDA 2026-10-03: 0x737830 uses a3 for the record and a2 for host state. Reads at a2+16/+80/+144/+148/+160/+164 are not payload fields.",
      "Removed the former overlapping host-state schema. Resource selector a3+4 remains protected by the existing record-prefix contract. See PPP_NATIVE_HANDLERS_20261003.md."
    ],
    "semantics_category": "VFX-Build"
  },
         "pppDrawMdl2": {
    "opcode": "pppDrawMdl2",
    "handler_addr": "0x7385F0",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 8,
      "width": 1
    },
    "match_word": "program[0] == ctx[+12]",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
    "fields": [
      {
        "name": "render_flags",
        "offset": 8,
        "width": 1,
        "type": "u8",
        "semantics": "Native render selector; bits 0/1 and value 4 select draw paths (0x73889E..0x7388F2)."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
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
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "semantics_category": "VFX-Build"
  },
         "pppDrawMdl3": {
  "opcode": "pppDrawMdl3",
  "handler_addr": "0x739580",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 5
  },
  "match_word": null,
  "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Geometry descriptor index; 0xFFFF disables drawing.",
      "editable": false
    },
    {
      "name": "byte_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_9",
      "offset": 9,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_10",
      "offset": 10,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_11",
      "offset": 11,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
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
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
  ],
  "semantics_category": "outro"
},
         "pppDrawMdlBS": {
          "opcode": "pppDrawMdlBS",
          "handler_addr": "0x737830",
          "payload_consumer": false,
          "editable": false,
          "window": null,
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [],
          "usage": "Native model-render alias of DrawMdl. +144/+148 belong to the host, not the serialized record.",
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
           "Native callback tables in both preserved and installed executables and exact IDA bytes verified. See PPP_LEGACY_RESIDUAL_CONTRACTS_20261007.md.",
           "State and resource references stay protected; no payload fields are inferred from host-state offsets. Raw word wrap does not prove an absolute color range or a gamma/color-space model."
          ],
          "semantics_category": "VFX-Build"
         },
         "pppDrawMdlCamera": {
  "opcode": "pppDrawMdlCamera",
  "handler_addr": "0x73D9F0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 34
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native record operand verified through the handler and its helper chain.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_33",
      "offset": 33,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u16_34",
      "offset": 34,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_37",
      "offset": 37,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_38",
      "offset": 38,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_39",
      "offset": 39,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_40",
      "offset": 40,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_41",
      "offset": 41,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
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
    "Native read/alias/helper reconciliation 2026-10-07 in owned IDA snapshot; see PPP_DRAW_VARIANTS_20261007.md. Protected resource/service references remain inspection-only."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawMdlCameraLoop": {
  "window": {
    "start": 8,
    "width": 34
  },
  "payload_consumer": true,
  "notes": [
    "Full native bodies and camera initialization helper captured in owned IDA snapshot 2026-10-07; exact preserved PE byte checks. See PPP_LOOP_SCHEMAS_20261007.md."
  ],
  "status": "FECHADO",
  "editable": true,
  "handler_addr": "0x73FB10",
  "usage": " [ONDA1b addr resolvido FFX_PppHandler_DrawMdlCameraLoop (Build VFX texture, __usercall draw-builder); janela derivada mantida]",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "Native geometry descriptor index; 0xFFFF disables drawing. R6 keeps this index read-only."
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "byte_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native camera-loop drawing or texture-animation control byte."
    },
    {
      "name": "byte_33",
      "offset": 33,
      "width": 1,
      "type": "u8",
      "semantics": "Native camera-loop drawing or texture-animation control byte."
    },
    {
      "name": "byte_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native camera-loop drawing or texture-animation control byte."
    },
    {
      "name": "byte_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native camera-loop drawing or texture-animation control byte."
    },
    {
      "name": "texture_override",
      "offset": 36,
      "width": 2,
      "type": "u16",
      "semantics": "Native draw texture word override; low byte 0xFF preserves the descriptor default."
    },
    {
      "name": "byte_38",
      "offset": 38,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_39",
      "offset": 39,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_40",
      "offset": 40,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_41",
      "offset": 41,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
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
    "start": 8,
    "width": 137
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native record operand verified through the handler and its helper chain.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_128",
      "offset": 128,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_129",
      "offset": 129,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u16_130",
      "offset": 130,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_132",
      "offset": 132,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_133",
      "offset": 133,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_134",
      "offset": 134,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_135",
      "offset": 135,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_136",
      "offset": 136,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_137",
      "offset": 137,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_140",
      "offset": 140,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_144",
      "offset": 144,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
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
    "Native read/alias/helper reconciliation 2026-10-07 in owned IDA snapshot; see PPP_DRAW_VARIANTS_20261007.md. Protected resource/service references remain inspection-only."
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
    "width": 30
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "Native geometry descriptor index; 0xFFFF disables drawing. R6 keeps this index read-only."
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "texture_override",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native draw texture word override; low byte 0xFF preserves the descriptor default."
    },
    {
      "name": "byte_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_37",
      "offset": 37,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
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
    "Full native bodies and camera initialization helper captured in owned IDA snapshot 2026-10-07; exact preserved PE byte checks. See PPP_LOOP_SCHEMAS_20261007.md."
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
    "width": 31
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "Native geometry descriptor index; 0xFFFF disables drawing. R6 keeps this index read-only."
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "texture_override",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native draw texture word override; low byte 0xFF preserves the descriptor default."
    },
    {
      "name": "byte_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_37",
      "offset": 37,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_38",
      "offset": 38,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    }
  ],
  "usage": "Draw loop com posição deslocada (flags +33..+38)",
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
    "Full native bodies and camera initialization helper captured in owned IDA snapshot 2026-10-07; exact preserved PE byte checks. See PPP_LOOP_SCHEMAS_20261007.md."
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
    "width": 31
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "Native geometry descriptor index; 0xFFFF disables drawing. R6 keeps this index read-only."
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Initial native texture position, copied when the instance state initializes."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture velocity or acceleration impulse at the matching keyframe timestamp."
    },
    {
      "name": "texture_override",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native draw texture word override; low byte 0xFF preserves the descriptor default."
    },
    {
      "name": "byte_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_37",
      "offset": 37,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_38",
      "offset": 38,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
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
    "Full native bodies and camera initialization helper captured in owned IDA snapshot 2026-10-07; exact preserved PE byte checks. See PPP_LOOP_SCHEMAS_20261007.md."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawMdlPSim": {
  "opcode": "pppDrawMdlPSim",
  "handler_addr": "0x73D130",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 122
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native record operand verified through the handler and its helper chain.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u16_32",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_40",
      "offset": 40,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_48",
      "offset": 48,
      "width": 4,
      "type": "f32",
      "semantics": "Loaded into a temporary vector that the native gravity service overwrites before insertion; preserved read-only.",
      "editable": false
    },
    {
      "name": "native_f32_52",
      "offset": 52,
      "width": 4,
      "type": "f32",
      "semantics": "Loaded into a temporary vector that the native gravity service overwrites before insertion; preserved read-only.",
      "editable": false
    },
    {
      "name": "native_f32_56",
      "offset": 56,
      "width": 4,
      "type": "f32",
      "semantics": "Loaded into a temporary vector that the native gravity service overwrites before insertion; preserved read-only.",
      "editable": false
    },
    {
      "name": "native_u8_64",
      "offset": 64,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u32_80",
      "offset": 80,
      "width": 4,
      "type": "u32",
      "semantics": "Native service vector DWORD copied unchanged; interpretation remains unverified, preserved read-only.",
      "editable": false
    },
    {
      "name": "native_u32_84",
      "offset": 84,
      "width": 4,
      "type": "u32",
      "semantics": "Native service vector DWORD copied unchanged; interpretation remains unverified, preserved read-only.",
      "editable": false
    },
    {
      "name": "native_u32_88",
      "offset": 88,
      "width": 4,
      "type": "u32",
      "semantics": "Native service vector DWORD copied unchanged; interpretation remains unverified, preserved read-only.",
      "editable": false
    },
    {
      "name": "native_u32_96",
      "offset": 96,
      "width": 4,
      "type": "u32",
      "semantics": "Native service vector DWORD copied unchanged; interpretation remains unverified, preserved read-only.",
      "editable": false
    },
    {
      "name": "native_u32_100",
      "offset": 100,
      "width": 4,
      "type": "u32",
      "semantics": "Native service vector DWORD copied unchanged; interpretation remains unverified, preserved read-only.",
      "editable": false
    },
    {
      "name": "native_u32_104",
      "offset": 104,
      "width": 4,
      "type": "u32",
      "semantics": "Native service vector DWORD copied unchanged; interpretation remains unverified, preserved read-only.",
      "editable": false
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_116",
      "offset": 116,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_117",
      "offset": 117,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_118",
      "offset": 118,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_119",
      "offset": 119,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_120",
      "offset": 120,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_s16_122",
      "offset": 122,
      "width": 2,
      "type": "s16",
      "semantics": "Signed animation-table index used for allocation and mesh lookup; preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s16_124",
      "offset": 124,
      "width": 2,
      "type": "s16",
      "semantics": "Signed animation-table index used for allocation and mesh lookup; preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s16_126",
      "offset": 126,
      "width": 2,
      "type": "s16",
      "semantics": "Signed animation-table index used for allocation and mesh lookup; preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s16_128",
      "offset": 128,
      "width": 2,
      "type": "s16",
      "semantics": "Signed animation-table index used for allocation and mesh lookup; preserved read-only pending bounded reference validation.",
      "editable": false
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
    "Native read/alias/helper reconciliation 2026-10-07 in owned IDA snapshot; see PPP_DRAW_VARIANTS_20261007.md. Protected resource/service references remain inspection-only."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawMdlRev": {
  "opcode": "pppDrawMdlRev",
  "handler_addr": "0x7404D0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 5
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native record operand verified through the handler and its helper chain.",
      "editable": false
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_9",
      "offset": 9,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_10",
      "offset": 10,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_11",
      "offset": 11,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
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
    "Native read/alias/helper reconciliation 2026-10-07 in owned IDA snapshot; see PPP_DRAW_VARIANTS_20261007.md. Protected resource/service references remain inspection-only."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawMdlSea": {
  "opcode": "pppDrawMdlSea",
  "handler_addr": "0x73ACA0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 80
  },
  "match_word": null,
  "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native record operand verified through the handler and its helper chain.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_80",
      "offset": 80,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_81",
      "offset": 81,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u16_82",
      "offset": 82,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_84",
      "offset": 84,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_85",
      "offset": 85,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_86",
      "offset": 86,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    },
    {
      "name": "native_u8_87",
      "offset": 87,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the handler and its helper chain."
    }
  ],
  "usage": "Draw variante sea (corpo não re-decompilado)",
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
    "Native read/alias/helper reconciliation 2026-10-07 in owned IDA snapshot; see PPP_DRAW_VARIANTS_20261007.md. Protected resource/service references remain inspection-only."
  ],
  "semantics_category": "Field/Node/Anim"
},
         "pppDrawMdlSemi": {
  "window": {
    "start": 8,
    "width": 2
  },
  "payload_consumer": true,
  "notes": [
    "Native read/alias/helper reconciliation 2026-10-07 in owned IDA snapshot; see PPP_DRAW_VARIANTS_20261007.md. Protected resource/service references remain inspection-only."
  ],
  "status": "FECHADO",
  "editable": true,
  "handler_addr": "0x737BE0",
  "usage": " [ONDA1b addr resolvido FFX_PppHandler_DrawMdlSemi (Build VFX drawable type F sub, __usercall); janela derivada mantida]",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native record operand verified through the handler and its helper chain.",
      "editable": false
    },
    {
      "name": "native_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the handler and its helper chain."
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
      "start": 8,
      "width": 3
    },
    "match_word": "program[0] == ctx[+12]",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
    "fields": [
      {
        "name": "render_word",
        "offset": 8,
        "width": 2,
        "type": "u16",
        "semantics": "Unsigned render word copied from record+8 at 0x738E1E."
      },
      {
        "name": "render_flags",
        "offset": 10,
        "width": 1,
        "type": "u8",
        "semantics": "Native render selector read at 0x738DBC."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
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
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "semantics_category": "VFX-Build"
  },
         "pppDrawMdlSemi3": {
    "opcode": "pppDrawMdlSemi3",
    "handler_addr": "0x739DF0",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 8,
      "width": 7
    },
    "match_word": "program[0] == ctx[+12]",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
    "fields": [
      {
        "name": "render_word",
        "offset": 8,
        "width": 2,
        "type": "u16",
        "semantics": "Unsigned render word read at 0x73A1D2."
      },
      {
        "name": "render_flags",
        "offset": 10,
        "width": 1,
        "type": "u8",
        "semantics": "Native render selector read at 0x73A0E1."
      },
      {
        "name": "flag_11",
        "offset": 11,
        "width": 1,
        "type": "u8",
        "semantics": "Raw nonzero/zero branch flag at record+11; no narrower enum is inferred."
      },
      {
        "name": "flag_12",
        "offset": 12,
        "width": 1,
        "type": "u8",
        "semantics": "Raw nonzero/zero branch flag at record+12; no narrower enum is inferred."
      },
      {
        "name": "flag_13",
        "offset": 13,
        "width": 1,
        "type": "u8",
        "semantics": "Raw nonzero/zero branch flag at record+13; no narrower enum is inferred."
      },
      {
        "name": "flag_14",
        "offset": 14,
        "width": 1,
        "type": "u8",
        "semantics": "Raw nonzero/zero branch flag at record+14; no narrower enum is inferred."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
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
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
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
      "semantics": "Geometry descriptor index; 0xFFFF disables drawing. Read-only pending the dedicated resource contract.",
      "editable": false
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
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
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawMdlTs2": {
  "window": {
    "start": 8,
    "width": 25
  },
  "payload_consumer": true,
  "notes": [
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
  ],
  "status": "FECHADO",
  "editable": true,
  "handler_addr": "0x738F80",
  "usage": " [ONDA1b addr resolvido FFX_PppHandler_DrawMdlTs2; janela derivada mantida]",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Geometry descriptor index; 0xFFFF disables drawing. Read-only pending the dedicated resource contract.",
      "editable": false
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "byte_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Geometry descriptor index; 0xFFFF disables drawing. Read-only pending the dedicated resource contract.",
      "editable": false
    },
    {
      "name": "delta_0",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_1",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_2",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_3",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_4",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "delta_5",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native impulse into texture position/velocity/acceleration state on the matching record timestamp."
    },
    {
      "name": "byte_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_33",
      "offset": 33,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
    },
    {
      "name": "byte_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native draw control byte."
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
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawRain": {
  "opcode": "pppDrawRain",
  "handler_addr": "0x7563B0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 4,
    "width": 52
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u32_8",
      "offset": 8,
      "width": 4,
      "type": "u32",
      "semantics": "Unsigned allocation and iteration count used for 48-byte elements; preserved read-only.",
      "editable": false
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_48",
      "offset": 48,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_52",
      "offset": 52,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
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
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
  ],
  "semantics_category": "Render"
},
         "pppDrawShape": {
    "opcode": "pppDrawShape",
    "handler_addr": "0x740AA0",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 8,
      "width": 2
    },
    "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
    "fields": [
      {
        "name": "draw_word_delta",
        "offset": 8,
        "width": 2,
        "type": "u16",
        "semantics": "Unsigned word added to the renderer batch word; record read in helper 0x740B30 at 0x740EF7."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. The Direct callback delegates record reads to 0x740B30.",
    "status": "FECHADO",
    "entry_addrs": [],
    "handler_name_canonical": "FFX_PppHandler_pppDrawShape",
    "handler_slot": null,
    "args": 3,
    "raw_width_yonishi": null,
    "width_reconciled": 0,
    "notes": [
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "semantics_category": "VFX-Build"
  },
         "pppDrawShapeCamera": {
  "opcode": "pppDrawShapeCamera",
  "handler_addr": "0x747600",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 217
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_u16_12",
      "offset": 12,
      "width": 2,
      "type": "u16",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_48",
      "offset": 48,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_52",
      "offset": 52,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_56",
      "offset": 56,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_128",
      "offset": 128,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_132",
      "offset": 132,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_136",
      "offset": 136,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_144",
      "offset": 144,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_148",
      "offset": 148,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_152",
      "offset": 152,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_160",
      "offset": 160,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_164",
      "offset": 164,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_168",
      "offset": 168,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_176",
      "offset": 176,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_177",
      "offset": 177,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_178",
      "offset": 178,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_179",
      "offset": 179,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_180",
      "offset": 180,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_192",
      "offset": 192,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_196",
      "offset": 196,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_200",
      "offset": 200,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_208",
      "offset": 208,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_212",
      "offset": 212,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_216",
      "offset": 216,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_224",
      "offset": 224,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    }
  ],
  "usage": "desenham shapes geométricos (campo, câmera, reverso, velocidade) [ONDA7 sweep: dispatch draw (tex_id@+4, count@+8)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeCamera",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native helper-chain and exact-byte reconciliation 2026-10-07, owned IDA snapshot. See PPP_DRAW_VARIANTS_20261007.md. Allocation counts and resource indices are inspection-only."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawShapeCameraDisPos": {
  "opcode": "pppDrawShapeCameraDisPos",
  "handler_addr": "0x748860",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 217
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_u16_12",
      "offset": 12,
      "width": 2,
      "type": "u16",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_48",
      "offset": 48,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_52",
      "offset": 52,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_56",
      "offset": 56,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_128",
      "offset": 128,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_132",
      "offset": 132,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_136",
      "offset": 136,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_144",
      "offset": 144,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_148",
      "offset": 148,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_152",
      "offset": 152,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_160",
      "offset": 160,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_164",
      "offset": 164,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_168",
      "offset": 168,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_176",
      "offset": 176,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_177",
      "offset": 177,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_178",
      "offset": 178,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_179",
      "offset": 179,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_180",
      "offset": 180,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_192",
      "offset": 192,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_196",
      "offset": 196,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_200",
      "offset": 200,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_208",
      "offset": 208,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_212",
      "offset": 212,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_216",
      "offset": 216,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_224",
      "offset": 224,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    }
  ],
  "usage": "desenham shapes geométricos (campo, câmera, reverso, velocidade) [ONDA7 sweep: idem DrawShapeCamera]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeCameraDisPos",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native helper-chain and exact-byte reconciliation 2026-10-07, owned IDA snapshot. See PPP_DRAW_VARIANTS_20261007.md. Allocation counts and resource indices are inspection-only."
  ],
  "semantics_category": "KR-resource"
},
         "pppDrawShapeField": {
  "opcode": "pppDrawShapeField",
  "handler_addr": "0x742D80",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 233
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_u16_12",
      "offset": 12,
      "width": 2,
      "type": "u16",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_128",
      "offset": 128,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_132",
      "offset": 132,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_136",
      "offset": 136,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_144",
      "offset": 144,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_148",
      "offset": 148,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_152",
      "offset": 152,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_160",
      "offset": 160,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_164",
      "offset": 164,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_168",
      "offset": 168,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_176",
      "offset": 176,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_180",
      "offset": 180,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_184",
      "offset": 184,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_192",
      "offset": 192,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_193",
      "offset": 193,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_194",
      "offset": 194,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_195",
      "offset": 195,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_196",
      "offset": 196,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_208",
      "offset": 208,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_212",
      "offset": 212,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_216",
      "offset": 216,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_224",
      "offset": 224,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_228",
      "offset": 228,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_232",
      "offset": 232,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_240",
      "offset": 240,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    }
  ],
  "usage": "desenham shapes geométricos (campo, câmera, reverso, velocidade) [ONDA7 sweep: FFX_KR_LoadTableForCurrentLanguage]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeField",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native helper-chain and exact-byte reconciliation 2026-10-07, owned IDA snapshot. See PPP_DRAW_VARIANTS_20261007.md. Allocation counts and resource indices are inspection-only."
  ],
  "semantics_category": "KR-resource"
},
         "pppDrawShapeFieldGlobal": {
  "opcode": "pppDrawShapeFieldGlobal",
  "handler_addr": "0x7463E0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 233
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_u16_12",
      "offset": 12,
      "width": 2,
      "type": "u16",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_128",
      "offset": 128,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_132",
      "offset": 132,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_136",
      "offset": 136,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_144",
      "offset": 144,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_148",
      "offset": 148,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_152",
      "offset": 152,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_160",
      "offset": 160,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_164",
      "offset": 164,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_168",
      "offset": 168,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_176",
      "offset": 176,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_180",
      "offset": 180,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_184",
      "offset": 184,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_192",
      "offset": 192,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_193",
      "offset": 193,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_194",
      "offset": 194,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_195",
      "offset": 195,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_196",
      "offset": 196,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_208",
      "offset": 208,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_212",
      "offset": 212,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_216",
      "offset": 216,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_224",
      "offset": 224,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_228",
      "offset": 228,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_232",
      "offset": 232,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_240",
      "offset": 240,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    }
  ],
  "usage": "desenham shapes geométricos (campo, câmera, reverso, velocidade) [ONDA7 sweep: DispatchEffectDraw_D]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeFieldGlobal",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native helper-chain and exact-byte reconciliation 2026-10-07, owned IDA snapshot. See PPP_DRAW_VARIANTS_20261007.md. Allocation counts and resource indices are inspection-only."
  ],
  "semantics_category": "VFX-Build"
},
         "pppDrawShapeFieldRev": {
  "opcode": "pppDrawShapeFieldRev",
  "handler_addr": "0x744100",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 234
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_u16_12",
      "offset": 12,
      "width": 2,
      "type": "u16",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_128",
      "offset": 128,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_132",
      "offset": 132,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_136",
      "offset": 136,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_144",
      "offset": 144,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_148",
      "offset": 148,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_152",
      "offset": 152,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_160",
      "offset": 160,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_164",
      "offset": 164,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_168",
      "offset": 168,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_176",
      "offset": 176,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_180",
      "offset": 180,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_184",
      "offset": 184,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_192",
      "offset": 192,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_193",
      "offset": 193,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_194",
      "offset": 194,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_195",
      "offset": 195,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_196",
      "offset": 196,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_208",
      "offset": 208,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_212",
      "offset": 212,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_216",
      "offset": 216,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_224",
      "offset": 224,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_228",
      "offset": 228,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_232",
      "offset": 232,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_240",
      "offset": 240,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_241",
      "offset": 241,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    }
  ],
  "usage": "desenham shapes geométricos (campo, câmera, reverso, velocidade) [ONDA7 sweep: BuildTableDrawableFromEntry]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeFieldRev",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native helper-chain and exact-byte reconciliation 2026-10-07, owned IDA snapshot. See PPP_DRAW_VARIANTS_20261007.md. Allocation counts and resource indices are inspection-only."
  ],
  "semantics_category": "KR-resource"
},
         "pppDrawShapeFieldSpd": {
  "opcode": "pppDrawShapeFieldSpd",
  "handler_addr": "0x7452D0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 217
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "KR resource index or unchecked allocation/iteration count; preserved read-only pending a bounded structural contract.",
      "editable": false
    },
    {
      "name": "native_u16_12",
      "offset": 12,
      "width": 2,
      "type": "u16",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_64",
      "offset": 64,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_68",
      "offset": 68,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_72",
      "offset": 72,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_80",
      "offset": 80,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_84",
      "offset": 84,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_88",
      "offset": 88,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_96",
      "offset": 96,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_100",
      "offset": 100,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_104",
      "offset": 104,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_112",
      "offset": 112,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_116",
      "offset": 116,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_120",
      "offset": 120,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_128",
      "offset": 128,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_132",
      "offset": 132,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_136",
      "offset": 136,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_144",
      "offset": 144,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_148",
      "offset": 148,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_152",
      "offset": 152,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_160",
      "offset": 160,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_164",
      "offset": 164,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_168",
      "offset": 168,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_176",
      "offset": 176,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_177",
      "offset": 177,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_178",
      "offset": 178,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_179",
      "offset": 179,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_180",
      "offset": 180,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_192",
      "offset": 192,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_196",
      "offset": 196,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_200",
      "offset": 200,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_208",
      "offset": 208,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_212",
      "offset": 212,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_f32_216",
      "offset": 216,
      "width": 4,
      "type": "f32",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    },
    {
      "name": "native_u8_224",
      "offset": 224,
      "width": 1,
      "type": "u8",
      "semantics": "Native operand traced through allocation, initialization, transform and draw helpers."
    }
  ],
  "usage": "desenham shapes geométricos (campo, câmera, reverso, velocidade) [ONDA7 sweep: knobs 3xf32@+64/68/72 pos + ctrl@+4/+8]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeFieldSpd",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native helper-chain and exact-byte reconciliation 2026-10-07, owned IDA snapshot. See PPP_DRAW_VARIANTS_20261007.md. Allocation counts and resource indices are inspection-only."
  ],
  "semantics_category": "KR-resource"
},
         "pppDrawShapeX": {
  "opcode": "pppDrawShapeX",
  "handler_addr": "0x740F90",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 7
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "tex_key",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Geometry descriptor index loaded by native helper 0x741020; 0xFFFF disables drawing.",
      "editable": false
    },
    {
      "name": "param_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Native helper advances animation accumulator by this unsigned 16-bit step."
    },
    {
      "name": "byte_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native helper draw control byte."
    },
    {
      "name": "byte_13",
      "offset": 13,
      "width": 1,
      "type": "u8",
      "semantics": "Native helper draw control byte."
    },
    {
      "name": "byte_14",
      "offset": 14,
      "width": 1,
      "type": "u8",
      "semantics": "Native helper draw control byte."
    }
  ],
  "usage": "Native drawable dispatcher; payload fields proven in its helper 0x741020.",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeX",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
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
      "name": "native_s32_4",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Finite nonzero divisor used by the native spring-force update.",
      "non_zero": true
    }
  ],
  "usage": "bone/transform (24, knobs na seção A) [ONDA6.12 decompile: FieldMap_ApplySpringForce (5 valores de forca de mola)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppEiWfacc",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    }
  ],
  "usage": "iluminação (7) [ONDA6.18: addr 0x75D8E0 = FieldMap_DebugOverlayRenderTriangle (debug, NAO handler de luz) — addr errado] [ONDA7 sweep: debug overlay tri (f32@+4, u8@+8, f32@+12)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppFpPointLight",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u32_16",
      "offset": 16,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    }
  ],
  "usage": "iluminação (7) [ONDA7 sweep: debug overlay mesh (+u32 id@+16)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppFpPointLightModel",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u32_16",
      "offset": 16,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    }
  ],
  "usage": "iluminação (7) [ONDA7 sweep: debug overlay rect (+u32 id@+16)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppFpPointLightModelScl",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_s16_16",
      "offset": 16,
      "width": 2,
      "type": "s16",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    }
  ],
  "usage": "iluminação (7) [ONDA7 sweep: light: size f32@+4, flag b@+8, sort f32@+12, mesh s16@+16]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppFpPointLightVsf",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_s16_16",
      "offset": 16,
      "width": 2,
      "type": "s16",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    }
  ],
  "usage": "iluminação (7) [ONDA7 sweep: idem Vsf + scale extra h[+12]]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppFpPointLightVsfScl",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
    "width": 24
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_s16_4",
      "offset": 4,
      "width": 2,
      "type": "s16",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s16_6",
      "offset": 6,
      "width": 2,
      "type": "s16",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_9",
      "offset": 9,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_10",
      "offset": 10,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    }
  ],
  "usage": "randomização de nascimento de partículas [ONDA6.16 sweep: nascimento random (le a2+20)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
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
    "width": 28
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u16_4",
      "offset": 4,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_13",
      "offset": 13,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_14",
      "offset": 14,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_28",
      "offset": 28,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    }
  ],
  "usage": "randomização de nascimento de partículas [ONDA6.16 sweep: nascimento random v2 (le a2+24)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppKeBornRnd2",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
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
    "width": 24
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u16_4",
      "offset": 4,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_6",
      "offset": 6,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_7",
      "offset": 7,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
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
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
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
    "width": 28
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "countdown",
      "offset": 4,
      "width": 2,
      "type": "u16",
      "semantics": "Native animation-table selector passed to helper 0x769340; preserved read-only in the record prefix."
    },
    {
      "name": "flag_active",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "u8@+12 — flag de ativacao"
    },
    {
      "name": "trigger",
      "offset": 13,
      "width": 1,
      "type": "u8",
      "semantics": "u8@+13 — trigger de nascimento"
    },
    {
      "name": "flag2",
      "offset": 14,
      "width": 1,
      "type": "u8",
      "semantics": "u8@+14 — flag secundaria"
    },
    {
      "name": "ref_a",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Signed program-table index used by callback 0x7590B0; read-only until a bounded program-reference contract is available.",
      "editable": false
    },
    {
      "name": "state_offset_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "editable": false,
      "semantics": "Signed offset into the newly allocated native instance state; preserved read-only."
    },
    {
      "name": "ref_b",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Signed reference guard tested against -1 by the native emitter; preserved read-only.",
      "editable": false
    },
    {
      "name": "state_offset_28",
      "offset": 28,
      "width": 4,
      "type": "s32",
      "editable": false,
      "semantics": "Signed offset into the newly allocated native instance state; preserved read-only."
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
    "Native main/helper/callback chain 0x759010 -> 0x769340 -> 0x7590B0 captured and byte-checked 2026-10-07. References are inspection-only; no unchecked native-state addressing is exposed."
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
    "width": 24
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u16_4",
      "offset": 4,
      "width": 2,
      "type": "u16",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_6",
      "offset": 6,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_7",
      "offset": 7,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
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
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "native_s32_4",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain.",
      "editable": false
    }
  ],
  "usage": "gravidade do efeito / gravidade direcionada a target [ONDA6.4: addr 0x759740 = FFX_FieldMap_CheckAndRenderObject (NAO handler PPP) — classificar por outro addr se existir] [ONDA7 sweep: gravidade: idx@+4, base@+8, obj alvo; match a2+12]",
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
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
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
      "width": 8
    },
    "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
    "fields": [
      {
        "name": "target_0",
        "offset": 4,
        "width": 4,
        "type": "f32",
        "semantics": "Float assigned to target state+160; record+4 is owned by the explicit native operand contract; the generic prefix guard stays closed."
      },
      {
        "name": "target_1",
        "offset": 8,
        "width": 4,
        "type": "f32",
        "semantics": "Float assigned to target state+164. The following host-state pointer is synthesized, not read from record+12."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
    "status": "FECHADO",
    "entry_addrs": [],
    "handler_name_canonical": "FFX_PppHandler_pppKeGrvTgt",
    "handler_slot": null,
    "args": 3,
    "raw_width_yonishi": null,
    "width_reconciled": 0,
    "notes": [
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
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
    "width": 9
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_s32_4",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_u8_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "usage": "impacto/colisão (check por pixel) [ONDA7 sweep: hit chk: idx@+4, base@+8, flag b@+12]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppKeHitChkPxB",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
    "width": 15
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
    },
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
      "semantics": "Native nonzero period/divisor used by the delegated renderer.",
      "non_zero": true
    },
    {
      "name": "native_seed",
      "offset": 22,
      "width": 1,
      "type": "u8",
      "semantics": "Native initializer seed passed to its per-entry random phase setup."
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
    "width": 15
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
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
      "semantics": "Native nonzero period/divisor used by the delegated renderer.",
      "non_zero": true
    },
    {
      "name": "native_seed",
      "offset": 22,
      "width": 1,
      "type": "u8",
      "semantics": "Native initializer seed passed to its per-entry random phase setup."
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
    },
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
      "semantics": "Native nonzero period/divisor used by the delegated renderer.",
      "non_zero": true
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
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
      "semantics": "Native nonzero period/divisor used by the delegated renderer.",
      "non_zero": true
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
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
      "semantics": "Native nonzero period/divisor used by the delegated renderer.",
      "non_zero": true
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "resource_index",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "editable": false,
      "semantics": "KR resource-table index used by the native initializer; 0xFFFF is the disabled sentinel. Preserved read-only."
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
      "semantics": "Native nonzero period/divisor used by the delegated renderer.",
      "non_zero": true
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
    "Full handler/initializer/renderer chain and byte checks in owned IDA snapshot 2026-10-07; see PPP_LENS_SCHEMAS_20261007.md. KR indices stay inspection-only; native divisors cannot be zero."
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
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
    "fields": [
      {
        "name": "param_1",
        "offset": 4,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to spring state+156; record+4 is owned by the explicit native operand contract; the generic prefix guard stays closed."
      },
      {
        "name": "param_2",
        "offset": 8,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to spring state+168."
      },
      {
        "name": "modo",
        "offset": 12,
        "width": 1,
        "type": "u8",
        "semantics": "Byte copied to spring state+142."
      },
      {
        "name": "byte_13",
        "offset": 13,
        "width": 1,
        "type": "u8",
        "semantics": "Byte copied to spring state+143; nonzero enables direction transformation."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
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
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
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
    "start": 4,
    "width": 13
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_13",
      "offset": 13,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_14",
      "offset": 14,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_15",
      "offset": 15,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
    },
    {
      "name": "native_u8_16",
      "offset": 16,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the emitter/physics handler and its callback chain."
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
    "Exact native loads and delegated callback reconciliation in owned IDA snapshot 2026-10-07. See PPP_EMITTER_PHYSICS_20261007.md. Program/state references remain inspection-only."
  ],
  "semantics_category": "Light/Glare"
},
         "pppKeMatSN": {
    "opcode": "pppKeMatSN",
    "handler_addr": "0x7340F0",
    "payload_consumer": false,
    "editable": false,
    "window": null,
    "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
    "fields": [],
    "usage": "Host-state matrix operation; no direct record payload. Common keyframe timing remains available.",
    "status": "FECHADO",
    "entry_addrs": [],
    "handler_name_canonical": "FFX_PppHandler_pppKeMatSN",
    "handler_slot": null,
    "args": 3,
    "raw_width_yonishi": null,
    "width_reconciled": 0,
    "notes": [
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "semantics_category": "Transform"
  },
         "pppKeMdlDtt": {
    "opcode": "pppKeMdlDtt",
    "handler_addr": "0x72E870",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 8,
      "width": 14
    },
    "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
    "fields": [
      {
        "name": "position_delta",
        "offset": 8,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native position accumulator at matching Q12 keyframes."
      },
      {
        "name": "velocity_delta",
        "offset": 12,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native velocity accumulator at matching Q12 keyframes."
      },
      {
        "name": "acceleration_delta",
        "offset": 16,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native acceleration accumulator at matching Q12 keyframes."
      },
      {
        "name": "mode",
        "offset": 20,
        "width": 1,
        "type": "u8",
        "semantics": "Native draw/integration mode. Values below 4 use acceleration; values >=4 use a pointer-bearing host-state path. The record field is a byte, not a pointer."
      },
      {
        "name": "render_byte",
        "offset": 21,
        "width": 1,
        "type": "u8",
        "semantics": "Unsigned rendering argument read from record+21 at 0x72EA97."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
    "status": "FECHADO",
    "entry_addrs": [],
    "handler_name_canonical": "FFX_PppHandler_pppKeMdlDtt",
    "handler_slot": null,
    "args": 3,
    "raw_width_yonishi": null,
    "width_reconciled": 0,
    "notes": [
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
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
      "width": 21
    },
    "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
    "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
    "fields": [
      {
        "name": "position_delta",
        "offset": 8,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native position accumulator at matching Q12 keyframes."
      },
      {
        "name": "velocity_delta",
        "offset": 12,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native velocity accumulator at matching Q12 keyframes."
      },
      {
        "name": "acceleration_delta",
        "offset": 16,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native acceleration accumulator at matching Q12 keyframes."
      },
      {
        "name": "draw_arg_0",
        "offset": 20,
        "width": 4,
        "type": "s32",
        "semantics": "First integer argument to FFX_FieldMap_StoreIntIntFloatGlobals."
      },
      {
        "name": "draw_arg_1",
        "offset": 24,
        "width": 4,
        "type": "s32",
        "semantics": "Second integer argument to FFX_FieldMap_StoreIntIntFloatGlobals."
      },
      {
        "name": "stride_quads",
        "offset": 28,
        "width": 1,
        "type": "u8",
        "semantics": "Unsigned value multiplied by four for the native drawable stride."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
    "status": "FECHADO",
    "entry_addrs": [],
    "handler_name_canonical": "FFX_PppHandler_pppKeMdlTfd",
    "handler_slot": null,
    "args": 3,
    "raw_width_yonishi": null,
    "width_reconciled": 0,
    "notes": [
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "semantics_category": "Delta/Accum"
  },
         "pppKeShpTail3": {
          "opcode": "pppKeShpTail3",
          "handler_addr": "0x750390",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 72
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u32_4",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "KR resource-table index; native lookup is unbounded by this schema.",
            "editable": false
           },
           {
            "name": "param_u32_8",
            "offset": 8,
            "width": 4,
            "type": "u32",
            "semantics": "Animation phase increment; raw wrapping DWORD, resource-dependent interpretation.",
            "editable": false
           },
           {
            "name": "param_f32_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation.",
            "non_zero": true
           },
           {
            "name": "param_f32_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_u8_28",
            "offset": 28,
            "width": 1,
            "type": "u8",
            "semantics": "Trail segment count; native interpolation divides by count minus one. Safe range is not established.",
            "editable": false
           },
           {
            "name": "param_u8_29",
            "offset": 29,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u16_30",
            "offset": 30,
            "width": 2,
            "type": "u16",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_s16_32",
            "offset": 32,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_34",
            "offset": 34,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_36",
            "offset": 36,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_38",
            "offset": 38,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_40",
            "offset": 40,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_42",
            "offset": 42,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_44",
            "offset": 44,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_46",
            "offset": 46,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_48",
            "offset": 48,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_50",
            "offset": 50,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_52",
            "offset": 52,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_54",
            "offset": 54,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_56",
            "offset": 56,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_58",
            "offset": 58,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_60",
            "offset": 60,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_62",
            "offset": 62,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_64",
            "offset": 64,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_66",
            "offset": 66,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_68",
            "offset": 68,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_70",
            "offset": 70,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_72",
            "offset": 72,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_74",
            "offset": 74,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_76",
            "offset": 76,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_78",
            "offset": 78,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           }
          ],
          "usage": "Verified native trail update and render operands; signed PVA impulses and finite renderer scalars.",
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
           "Owned Windows IDA snapshot 2026-10-07: update 0x750390, render 0x750700, init/reset and both executable callback tables captured. See PPP_TAIL_SCHEMAS_20261007.md.",
           "Captured bytes match preserved executable SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; normalized corresponding functions verified in installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d.",
           "Resource index, animation phase increment and segment count remain inspection-only. Prefix and native-stride guards remain in force; width divisor +12 must be finite and nonzero."
          ],
          "semantics_category": "Trail/PVA"
         },
         "pppKeShpTail3X": {
          "opcode": "pppKeShpTail3X",
          "handler_addr": "0x751D80",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 75
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u32_4",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "KR resource-table index; native lookup is unbounded by this schema.",
            "editable": false
           },
           {
            "name": "param_u32_8",
            "offset": 8,
            "width": 4,
            "type": "u32",
            "semantics": "Animation phase increment; raw wrapping DWORD, resource-dependent interpretation.",
            "editable": false
           },
           {
            "name": "param_f32_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation.",
            "non_zero": true
           },
           {
            "name": "param_f32_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_u8_28",
            "offset": 28,
            "width": 1,
            "type": "u8",
            "semantics": "Trail segment count; native interpolation divides by count minus one. Safe range is not established.",
            "editable": false
           },
           {
            "name": "param_u8_29",
            "offset": 29,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u16_30",
            "offset": 30,
            "width": 2,
            "type": "u16",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_s16_32",
            "offset": 32,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_34",
            "offset": 34,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_36",
            "offset": 36,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_38",
            "offset": 38,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_40",
            "offset": 40,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_42",
            "offset": 42,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_44",
            "offset": 44,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_46",
            "offset": 46,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_48",
            "offset": 48,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_50",
            "offset": 50,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_52",
            "offset": 52,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_54",
            "offset": 54,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_56",
            "offset": 56,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_58",
            "offset": 58,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_60",
            "offset": 60,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_62",
            "offset": 62,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_64",
            "offset": 64,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_66",
            "offset": 66,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_68",
            "offset": 68,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_70",
            "offset": 70,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_72",
            "offset": 72,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_74",
            "offset": 74,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_76",
            "offset": 76,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_78",
            "offset": 78,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_u8_80",
            "offset": 80,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u8_81",
            "offset": 81,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u8_82",
            "offset": 82,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           }
          ],
          "usage": "Verified native trail update and render operands; signed PVA impulses and finite renderer scalars.",
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
           "Owned Windows IDA snapshot 2026-10-07: update 0x751D80, render 0x7520B0, init/reset and both executable callback tables captured. See PPP_TAIL_SCHEMAS_20261007.md.",
           "Captured bytes match preserved executable SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; normalized corresponding functions verified in installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d.",
           "Resource index, animation phase increment and segment count remain inspection-only. Prefix and native-stride guards remain in force; width divisor +12 must be finite and nonzero."
          ],
          "semantics_category": "Trail/PVA"
         },
         "pppKeShpTailPht": {
          "opcode": "pppKeShpTailPht",
          "handler_addr": "0x754000",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
           "width": 93
          },
          "match_word": "program[0] == ctx[+12]",
          "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) \u2014 exceto DrawFilter (sem guard/match)",
          "fields": [
           {
            "name": "param_u32_4",
            "offset": 4,
            "width": 4,
            "type": "u32",
            "semantics": "KR resource-table index; native lookup is unbounded by this schema.",
            "editable": false
           },
           {
            "name": "param_u32_8",
            "offset": 8,
            "width": 4,
            "type": "u32",
            "semantics": "Animation phase increment; raw wrapping DWORD, resource-dependent interpretation.",
            "editable": false
           },
           {
            "name": "param_f32_12",
            "offset": 12,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation.",
            "non_zero": true
           },
           {
            "name": "param_f32_16",
            "offset": 16,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_20",
            "offset": 20,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_24",
            "offset": 24,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer scalar; +12 also divides the width interpolation."
           },
           {
            "name": "param_f32_28",
            "offset": 28,
            "width": 4,
            "type": "f32",
            "semantics": "Floating position / velocity / acceleration impulse added by the native update callback."
           },
           {
            "name": "param_f32_32",
            "offset": 32,
            "width": 4,
            "type": "f32",
            "semantics": "Floating position / velocity / acceleration impulse added by the native update callback."
           },
           {
            "name": "param_f32_36",
            "offset": 36,
            "width": 4,
            "type": "f32",
            "semantics": "Floating position / velocity / acceleration impulse added by the native update callback."
           },
           {
            "name": "param_f32_40",
            "offset": 40,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer geometry scale."
           },
           {
            "name": "param_f32_44",
            "offset": 44,
            "width": 4,
            "type": "f32",
            "semantics": "Native trail renderer geometry scale."
           },
           {
            "name": "param_s16_48",
            "offset": 48,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_50",
            "offset": 50,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_52",
            "offset": 52,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_54",
            "offset": 54,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_56",
            "offset": 56,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_58",
            "offset": 58,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_60",
            "offset": 60,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_62",
            "offset": 62,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 position impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_64",
            "offset": 64,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_66",
            "offset": 66,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_68",
            "offset": 68,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_70",
            "offset": 70,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_72",
            "offset": 72,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_74",
            "offset": 74,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_76",
            "offset": 76,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_78",
            "offset": 78,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 velocity impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_80",
            "offset": 80,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 0 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_82",
            "offset": 82,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 1 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_84",
            "offset": 84,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 2 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_86",
            "offset": 86,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 3 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_88",
            "offset": 88,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 4 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_90",
            "offset": 90,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 5 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_92",
            "offset": 92,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 6 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_s16_94",
            "offset": 94,
            "width": 2,
            "type": "s16",
            "semantics": "Channel 7 acceleration impulse; 16-bit wrapping accumulation, signed state read in the render callback."
           },
           {
            "name": "param_u8_96",
            "offset": 96,
            "width": 1,
            "type": "u8",
            "semantics": "Trail segment count; native interpolation divides by count minus one. Safe range is not established.",
            "editable": false
           },
           {
            "name": "param_u8_97",
            "offset": 97,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u8_98",
            "offset": 98,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u8_99",
            "offset": 99,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           },
           {
            "name": "param_u8_100",
            "offset": 100,
            "width": 1,
            "type": "u8",
            "semantics": "Native trail renderer control operand."
           }
          ],
          "usage": "Verified native trail update and render operands; signed PVA impulses and finite renderer scalars.",
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
           "Owned Windows IDA snapshot 2026-10-07: update 0x754000, render 0x7543F0, init/reset and both executable callback tables captured. See PPP_TAIL_SCHEMAS_20261007.md.",
           "Captured bytes match preserved executable SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; normalized corresponding functions verified in installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d.",
           "Resource index, animation phase increment and segment count remain inspection-only. Prefix and native-stride guards remain in force; width divisor +12 must be finite and nonzero."
          ],
          "semantics_category": "Trail/PVA"
         },
         "pppKeTh": {
    "window": {
      "start": 16,
      "width": 72
    },
    "payload_consumer": true,
    "notes": [
      "Native FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; verified in VM IDA 2026-10-03. Dispatch +8 = 0x736110; 0x736F50 belongs to pppKeThSft.",
      "Record pointer a3/v9/v15: +16..+47 sixteen u16 additions (0x73629D..0x73632E), +48..+83 nine f32 reads, +84 u16 angle, +86/+87 bytes (0x73633B..0x7363D1). Host-state offsets are not record operands."
    ],
    "status": "FECHADO",
    "editable": true,
    "handler_addr": "0x736110",
    "usage": "Animation state operands consumed by pppKeTh Exec. Resource selectors at +4/+8 remain protected.",
    "fields": [
      {
        "name": "param_u16_16",
        "offset": 16,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 0; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_18",
        "offset": 18,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 1; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_20",
        "offset": 20,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 2; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_22",
        "offset": 22,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 3; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_24",
        "offset": 24,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 4; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_26",
        "offset": 26,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 5; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_28",
        "offset": 28,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 6; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_30",
        "offset": 30,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 7; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_32",
        "offset": 32,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 8; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_34",
        "offset": 34,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 9; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_36",
        "offset": 36,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 10; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_38",
        "offset": 38,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 11; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_40",
        "offset": 40,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 12; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_42",
        "offset": 42,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 13; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_44",
        "offset": 44,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 14; native 0x73629D..0x73632E."
      },
      {
        "name": "param_u16_46",
        "offset": 46,
        "width": 2,
        "type": "u16",
        "semantics": "Raw 16-bit delta added to animation state channel 15; native 0x73629D..0x73632E."
      },
      {
        "name": "param_float_48",
        "offset": 48,
        "width": 4,
        "type": "f32",
        "semantics": "Assigned to animation state +104. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_52",
        "offset": 52,
        "width": 4,
        "type": "f32",
        "semantics": "Added to animation state +28. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_56",
        "offset": 56,
        "width": 4,
        "type": "f32",
        "semantics": "Target term used with state +28 to derive the animation rate. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_60",
        "offset": 60,
        "width": 4,
        "type": "f32",
        "semantics": "Assigned to animation state +64. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_64",
        "offset": 64,
        "width": 4,
        "type": "f32",
        "semantics": "Assigned to animation state +72. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_68",
        "offset": 68,
        "width": 4,
        "type": "f32",
        "semantics": "Assigned to animation state +76. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_72",
        "offset": 72,
        "width": 4,
        "type": "f32",
        "semantics": "Added to animation state +52. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_76",
        "offset": 76,
        "width": 4,
        "type": "f32",
        "semantics": "Added to animation state +108. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_float_80",
        "offset": 80,
        "width": 4,
        "type": "f32",
        "semantics": "Added to animation state +60. Native record read at 0x73633B..0x7363D1."
      },
      {
        "name": "param_u16_84",
        "offset": 84,
        "width": 2,
        "type": "u16",
        "semantics": "Unsigned angle in degrees converted to radians at 0x736392..0x7363A4."
      },
      {
        "name": "param_u8_86",
        "offset": 86,
        "width": 1,
        "type": "u8",
        "semantics": "Raw byte assigned to animation state +80; native 0x7363AE/0x7363B5."
      },
      {
        "name": "param_u8_87",
        "offset": 87,
        "width": 1,
        "type": "u8",
        "semantics": "Raw byte assigned to animation state +81; native 0x7363AE/0x7363B5."
      }
    ],
    "semantics_category": "Delta/Accum",
    "entry_addrs": [
      "0xC3AE88",
      "0xC3C530",
      "0xC3FE90"
    ],
    "handler_slot": 8
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "native_s32_4",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_u8_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "usage": "gestão de threads de animação [ONDA6.11: 0x759E70 = FieldMap_WalkStructTransparencyNodes (le a2+12 u8) — payload marginal, manter SEM_PAYLOAD] [ONDA7 sweep: FieldMap_WalkStructTransparencyNodes: s32@+4 target, s32@+8 offset node, u8@+12 flag]",
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
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
      "Verified in VM IDA 2026-10-03 and matched to FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced. Host dispatch entry +8 = 0x736F50; +28/+32 = constructor 0x7371F0.",
      "Record a2: sixteen u16 deltas +8..+39 (0x736FD0..0x737084), four f32 deltas +40..+55 (0x736F92..0x736FC5), byte +56 (0x736F81). Native window [8,57)."
    ],
    "status": "FECHADO",
    "editable": true,
    "handler_addr": "0x736F50",
    "usage": "Animation deltas consumed by pppKeThSft Exec at matching Q12 keyframes; 0x7371F0 is its constructor.",
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
    "semantics_category": "Field/Node/Anim",
    "entry_addrs": [
      "0xC3AE60",
      "0xC3C508",
      "0xC3FE68"
    ],
    "handler_slot": 8
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    }
  ],
  "usage": "gestão de threads de animação [ONDA6.11 decompile 0x736E40: set transform + acumula em node]",
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
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    }
  ],
  "usage": "gestão de threads de animação [ONDA7 sweep: FieldMap_SetTransformAndAccumulate (alias do KeThTp): pos xyz f32@+4/8/12 + refs@+16/20]",
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
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
  ],
  "semantics_category": "Behavior"
},
         "pppKeZCrct": {
  "opcode": "pppKeZCrct",
  "handler_addr": "0x735230",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 16,
    "width": 33
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "param_float_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_44",
      "offset": 44,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_u8_48",
      "offset": 48,
      "width": 1,
      "type": "u8",
      "semantics": "Native transform composition mode byte (branches distinguish 0/1/2)."
    }
  ],
  "usage": "correção de profundidade Z [ONDA6.16 sweep: correcao Z (le a2+48)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppKeZCrct",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
  ],
  "semantics_category": "Transform"
},
         "pppKeZCrctShp": {
  "payload_consumer": true,
  "window": {
    "start": 16,
    "width": 33
  },
  "notes": [
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
  ],
  "status": "FECHADO",
  "editable": true,
  "handler_addr": "0x735590",
  "usage": "Native billboard transform/depth offset; payload contains translation, row scales and composition mode.",
  "fields": [
    {
      "name": "param_float_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_float_44",
      "offset": 44,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection/transform float operand; +16..+28 translation vector, +32..+44 row-scale vector."
    },
    {
      "name": "param_u8_48",
      "offset": 48,
      "width": 1,
      "type": "u8",
      "semantics": "Native transform composition mode byte (branches distinguish 0/1/2)."
    }
  ],
  "semantics_category": "outro"
},
         "pppMatrixScl": {
    "opcode": "pppMatrixScl",
    "handler_addr": "0x734240",
    "payload_consumer": false,
    "editable": false,
    "match_word": null,
    "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
    "fields": [],
    "usage": "Host-state matrix operation; no direct record payload. Common keyframe timing remains available.",
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
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "semantics_category": "outro",
    "window_note": "A1 RE 2026-08-12: window decompilada {64,12} (CopyDualReferencePosition) N?O aplicada como edit?vel ? teste RT0 espera FieldWindow==null; matrizes s?o exib?veis.",
    "window": null
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
        "semantics": "Component X. Velocity impulse at a matching Q12 timestamp; the velocity channel is added to position each local frame."
      },
      {
        "name": "delta_y",
        "offset": 20,
        "width": 4,
        "type": "f32",
        "semantics": "Component Y. Velocity impulse at a matching Q12 timestamp; the velocity channel is added to position each local frame."
      },
      {
        "name": "delta_z",
        "offset": 24,
        "width": 4,
        "type": "f32",
        "semantics": "Component Z. Velocity impulse at a matching Q12 timestamp; the velocity channel is added to position each local frame."
      },
      {
        "name": "delta_w",
        "offset": 28,
        "width": 4,
        "type": "f32",
        "semantics": "Component W. Velocity impulse at a matching Q12 timestamp; the velocity channel is added to position each local frame."
      }
    ],
    "usage": "Velocity impulse at a matching Q12 timestamp; the velocity channel is added to position each local frame.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
    "width": 24
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    }
  ],
  "usage": "iluminação (7) [ONDA6.16 sweep: luz vizinha (le a2+24)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppNeiLightEikyo",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native light/render operand verified in the handler and helper chain."
    }
  ],
  "usage": "Point light de vizinhança (acc/vel/pos + handles) [ONDA1b decompile: FieldMap_AccumulateVelocityWithHandle — id u32@+0, f32@+4/+8/+12 += node acc/vel/pos; handles@node+172/+176]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppNeiPointLight",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 12,
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
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
        "semantics": "Component X. Position delta applied once when the Q12 keyframe timestamp matches."
      },
      {
        "name": "delta_y",
        "offset": 20,
        "width": 4,
        "type": "f32",
        "semantics": "Component Y. Position delta applied once when the Q12 keyframe timestamp matches."
      },
      {
        "name": "delta_z",
        "offset": 24,
        "width": 4,
        "type": "f32",
        "semantics": "Component Z. Position delta applied once when the Q12 keyframe timestamp matches."
      },
      {
        "name": "delta_w",
        "offset": 28,
        "width": 4,
        "type": "f32",
        "semantics": "Component W. Position delta applied once when the Q12 keyframe timestamp matches."
      }
    ],
    "usage": "Position delta applied once when the Q12 keyframe timestamp matches.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
    ],
    "semantics_category": "Delta/Accum"
  },
         "pppPointAp": {
          "opcode": "pppPointAp",
          "handler_addr": "0x7574B0",
          "payload_consumer": true,
          "editable": true,
          "window": {
           "start": 8,
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
            "semantics": "Native target program / state offset.",
            "editable": false
           },
           {
            "name": "param_s32_8",
            "offset": 8,
            "width": 4,
            "type": "s32",
            "semantics": "Native target program / state offset.",
            "editable": false
           },
           {
            "name": "param_u8_12",
            "offset": 12,
            "width": 1,
            "type": "u8",
            "semantics": "Native byte delay between position copies.",
            "editable": true
           }
          ],
          "usage": "Copies native position to a referenced target state and reloads a byte delay.",
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
           "Native callback tables in both preserved and installed executables and exact IDA bytes verified. See PPP_LEGACY_RESIDUAL_CONTRACTS_20261007.md.",
           "State and resource references stay protected; no payload fields are inferred from host-state offsets. Raw word wrap does not prove an absolute color range or a gamma/color-space model."
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
    "width": 25
  },
  "match_word": "program[0] == ctx[+12]",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (se setado, retorna sem acumular) — exceto DrawFilter (sem guard/match)",
  "fields": [
    {
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_u8_28",
      "offset": 28,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "usage": "bone/transform (24, knobs na seção A) [ONDA6.16 sweep: point reverse apply (le a2+28)]",
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
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
    "width": 9
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native random delta amplitude read as f32."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 10,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 12,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 14,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 16,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
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
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
    "width": 9
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native random delta amplitude read as f32."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
  ],
  "semantics_category": "Random"
},
         "pppRandHCV": {
  "opcode": "pppRandHCV",
  "handler_addr": "0x731F90",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 4,
    "width": 13
  },
  "match_word": null,
  "guard": "U2/U3: sem FFX_PppStatePausedFlag na maioria (ver notas por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 10,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 12,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 14,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 16,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
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
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 10,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 12,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 14,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 16,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
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
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
    "width": 9
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 9,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 10,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 11,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
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
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 10,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 12,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 14,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 16,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
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
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 10,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 12,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 14,
      "width": 2,
      "type": "s16",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 16,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
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
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
    "width": 29
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Float32 amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
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
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
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
        "semantics": "Component X. Scale-channel delta applied once when the Q12 keyframe timestamp matches; not an absolute scale."
      },
      {
        "name": "scale_y",
        "offset": 20,
        "width": 4,
        "type": "f32",
        "semantics": "Component Y. Scale-channel delta applied once when the Q12 keyframe timestamp matches; not an absolute scale."
      },
      {
        "name": "scale_z",
        "offset": 24,
        "width": 4,
        "type": "f32",
        "semantics": "Component Z. Scale-channel delta applied once when the Q12 keyframe timestamp matches; not an absolute scale."
      },
      {
        "name": "scale_w",
        "offset": 28,
        "width": 4,
        "type": "f32",
        "semantics": "Component W. Scale-channel delta applied once when the Q12 keyframe timestamp matches; not an absolute scale."
      }
    ],
    "usage": "Scale-channel delta applied once when the Q12 keyframe timestamp matches; not an absolute scale.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
        "semantics": "Component X. Scale-acceleration impulse at a matching Q12 timestamp; that channel is added to scale velocity each local frame."
      },
      {
        "name": "accel_y",
        "offset": 20,
        "width": 4,
        "type": "f32",
        "semantics": "Component Y. Scale-acceleration impulse at a matching Q12 timestamp; that channel is added to scale velocity each local frame."
      },
      {
        "name": "accel_z",
        "offset": 24,
        "width": 4,
        "type": "f32",
        "semantics": "Component Z. Scale-acceleration impulse at a matching Q12 timestamp; that channel is added to scale velocity each local frame."
      },
      {
        "name": "accel_w",
        "offset": 28,
        "width": 4,
        "type": "f32",
        "semantics": "Component W. Scale-acceleration impulse at a matching Q12 timestamp; that channel is added to scale velocity each local frame."
      }
    ],
    "usage": "Scale-acceleration impulse at a matching Q12 timestamp; that channel is added to scale velocity each local frame.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
        "semantics": "Component X. Scale-velocity impulse at a matching Q12 timestamp; that channel is added to scale each local frame."
      },
      {
        "name": "delta_y",
        "offset": 20,
        "width": 4,
        "type": "f32",
        "semantics": "Component Y. Scale-velocity impulse at a matching Q12 timestamp; that channel is added to scale each local frame."
      },
      {
        "name": "delta_z",
        "offset": 24,
        "width": 4,
        "type": "f32",
        "semantics": "Component Z. Scale-velocity impulse at a matching Q12 timestamp; that channel is added to scale each local frame."
      },
      {
        "name": "delta_w",
        "offset": 28,
        "width": 4,
        "type": "f32",
        "semantics": "Component W. Scale-velocity impulse at a matching Q12 timestamp; that channel is added to scale each local frame."
      }
    ],
    "usage": "Scale-velocity impulse at a matching Q12 timestamp; that channel is added to scale each local frame.",
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
      "Native dispatch and IDA decompile/byte capture checked 2026-10-03 against preserved FFX.exe 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced.",
      "Installed FFX.exe 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has equivalent non-address instruction bytes for this handler.",
      "Runtime vector aliases come from the slot parameter-offset array. Do not mix unrelated programs or repeatedly apply a one-shot keyframe impulse."
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
            "semantics": "Aux4 pattern-table index, not an initial frame. Bit 0x8000 bypasses animation processing; the remaining value selects an 8-byte Aux4 entry. The record prefix remains read-only."
           },
           {
            "name": "frame_count",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "Number of capture batches attempted per update, not a geometry vertex count. Sequential mode advances the runtime cursor against the signed count in the selected Aux4 entry."
           },
           {
            "name": "timer",
            "offset": 7,
            "width": 1,
            "type": "u8",
            "semantics": "Loaded into the runtime U16 delay at +162, then decremented during the same update. A zero load wraps to 0xFFFF. The original handler also has a conditional delay scale."
           },
           {
            "name": "modo",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "Capture order: 0 advances the pattern cursor; 1 uses RNG; other values process the delay without generating a batch."
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
           "Complete native 0x757930 reads record +4/+6/+7/+8. Aux4 ownership, capture count and U16 delay decrement are reconciled in MAGIC_EGO_CALLBACK_BODY_COVERAGE_20261008.md."
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
            "semantics": "Aux4 pattern-table index. Bit 0x8000 bypasses processing; it is not a parity flag. The index selects the pattern's geometry descriptor and index stream; the record prefix remains read-only."
           },
           {
            "name": "vertex_count",
            "offset": 6,
            "width": 1,
            "type": "u8",
            "semantics": "Number of capture batches attempted per update, not a geometry vertex count. The existing field identifier is retained for schema compatibility."
           },
           {
            "name": "delay_frames",
            "offset": 7,
            "width": 1,
            "type": "u8",
            "semantics": "Loaded into the runtime U16 delay at +162, then decremented during the same update. A zero load wraps to 0xFFFF; it does not request immediate repetition."
           },
           {
            "name": "order_mode",
            "offset": 8,
            "width": 1,
            "type": "u8",
            "semantics": "Capture order: 0 advances the pattern cursor; 1 uses RNG; other values process the delay without generating a batch."
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
           "Native 0x7583F9..0x75858B proves the Aux4 index, high-bit bypass, capture-batch loop and same-update U16 decrement. Prefix +4..+7 stays protected by the writer contract."
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
    "width": 5
  },
  "match_word": "program[0] == ctx[+12] (padrao; DrawFilter nao checa id)",
  "guard": "FFX_PppStatePausedFlag @ 0x230FD34 (padrao; verificar por familia)",
  "fields": [
    {
      "name": "native_u16_4",
      "offset": 4,
      "width": 2,
      "type": "u16",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_u8_6",
      "offset": 6,
      "width": 1,
      "type": "u8",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_u8_7",
      "offset": 7,
      "width": 1,
      "type": "u8",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_u8_8",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "usage": "bone/transform (24, knobs na seção A) [ONDA6.7 decompile: FFX_KR_ProcessAnimWithCaptureBatch (u16 id@+0, u16 indice@+4 bit 0x8000)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppVertexApDisPos",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
      "semantics": "Native animation index; high bit disables updates. Prefix remains read-only.",
      "editable": false
    },
    {
      "name": "frame_count",
      "offset": 6,
      "width": 1,
      "type": "u8",
      "semantics": "Native emission count, preserved read-only in the record prefix.",
      "editable": false
    },
    {
      "name": "timer",
      "offset": 7,
      "width": 1,
      "type": "u8",
      "semantics": "Native delay copied to the host timer, preserved read-only in the record prefix.",
      "editable": false
    },
    {
      "name": "modo",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native animation mode: sequential for 0, random for 1."
    }
  ],
  "usage": "bone/transform (24, knobs na seção A) [ONDA6.7 decompile: FFX_KR_ProcessAnimWithCaptureBatch_B (variante B, mesmo padrao)]",
  "status": "FECHADO",
  "entry_addrs": [],
  "handler_name_canonical": "FFX_PppHandler_pppVertexApLc",
  "handler_slot": null,
  "args": 3,
  "raw_width_yonishi": null,
  "width_reconciled": 0,
  "notes": [
    "Native record/host separation checked in the owned IDA snapshot and exact PE bytes on 2026-10-07. See PPP_DRAW_TEXTURE_SCHEMAS_20261007.md."
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
           "Native 75D430 reads +12 as signed Aux4 pattern index. 757930/7585C0 establish signed count +2 and U16 vertex index streams. See MAGIC_NATIVE_CODE_ATLAS_20261008.md."
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
      "name": "native_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    }
  ],
  "usage": " [ONDA1 decompile 0x759CE0: KNOB HUD/portrait — lerp f32@+4, refs node +8/+12, guard PausedFlag, match program[0]==ctx[+12]]",
  "status": "FECHADO",
  "extra": true,
  "corpus_slots": null,
  "corpus_dlls": null,
  "fp_h_tables": 9,
  "yonishi_files": 10,
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
    "start": 8,
    "width": 53
  },
  "window_known": false,
  "match_word": null,
  "guard": null,
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native UV3 record alias v7 loads the helper global/control operand."
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native UV3 record alias v7 loads the helper global/control operand."
    },
    {
      "name": "native_u8_28",
      "offset": 28,
      "width": 1,
      "type": "u8",
      "semantics": "Native UV3 record alias v7 loads the helper global/control operand."
    },
    {
      "name": "native_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_44",
      "offset": 44,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_48",
      "offset": 48,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_52",
      "offset": 52,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_56",
      "offset": 56,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_57",
      "offset": 57,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_58",
      "offset": 58,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_59",
      "offset": 59,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_60",
      "offset": 60,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "usage": " [ONDA1 decompile 0x74A0C0 (renomeado FFX_PppHandler_KeMdlTfdUv3): drawable@+4, 3xVec3 acumulado @+8..+52, flags@+56..+60, globals@+20/+24, stride@+28 — janela +4..+60]",
  "status": "FECHADO",
  "extra": true,
  "corpus_slots": null,
  "corpus_dlls": null,
  "fp_h_tables": 2,
  "yonishi_files": 2,
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native random delta amplitude read as u8."
    },
    {
      "name": "double_random",
      "offset": 9,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x72FB00 pattern D: target u32@+4, delta u8@+8, flag u8@+9]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native random delta amplitude read as u8."
    },
    {
      "name": "double_random",
      "offset": 9,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x72FCD0 pattern E: idem RandChar + media 0.5 na flag]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 1,
      "type": "u8",
      "semantics": "Native random delta amplitude read as u8."
    },
    {
      "name": "double_random",
      "offset": 9,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x72FEB0 pattern F: idem RandChar (sinal invertido)]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Native random delta amplitude read as u16."
    },
    {
      "name": "double_random",
      "offset": 10,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x730090 pattern G: target u32@+4, delta u16@+8, flag u8@+10]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Native random delta amplitude read as u16."
    },
    {
      "name": "double_random",
      "offset": 10,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandShort + media 0.5 (familia Up)]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Native random delta amplitude read as u16."
    },
    {
      "name": "double_random",
      "offset": 10,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandShort (familia Down)]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 4,
      "type": "u32",
      "semantics": "Native random delta amplitude read as u32."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x730610 pattern J: target u32@+4, delta u32@+8, flag u8@+12]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 4,
      "type": "u32",
      "semantics": "Native random delta amplitude read as u32."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandInt (familia Up)]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 4,
      "type": "u32",
      "semantics": "Native random delta amplitude read as u32."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandInt (familia Down)]",
  "status": "FECHADO",
  "notes": [
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 9,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 10,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 11,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile 0x731860 pattern S: target u32@+4, 4x delta s8@+8..+11, flag u8@+12]",
  "status": "FECHADO",
  "notes": [
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 9,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 10,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 11,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandCV (familia Up)]",
  "status": "FECHADO",
  "notes": [
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_component_0",
      "offset": 8,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 0."
    },
    {
      "name": "random_component_1",
      "offset": 9,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 1."
    },
    {
      "name": "random_component_2",
      "offset": 10,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 2."
    },
    {
      "name": "random_component_3",
      "offset": 11,
      "width": 1,
      "type": "s8",
      "semantics": "Signed packed native random amplitude for component 3."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native random helper mode byte."
    }
  ],
  "usage": "Random (EXE-only, familia Rand) [ONDA1 decompile: idem RandCV (familia Down)]",
  "status": "FECHADO",
  "notes": [
    "Native packed reads reconciled 2026-10-07: four signed amplitudes and one mode byte. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
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
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_28",
      "offset": 28,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_44",
      "offset": 44,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_48",
      "offset": 48,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_52",
      "offset": 52,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_56",
      "offset": 56,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "usage": "Draw modelo com acumulacao (EXE-only, familia KeMdlTfd)",
  "status": "FECHADO",
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
  ],
  "semantics_category": "outro",
  "handler_addr": "0x749D60"
},
         "pppKeMdlTfdUv": {
    "opcode": "pppKeMdlTfdUv",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 8,
      "width": 48
    },
    "match_word": "program[0] == ctx[+12]",
    "guard": "FFX_PppStatePausedFlag",
    "fields": [
      {
        "name": "position_delta",
        "offset": 8,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native position accumulator at matching Q12 keyframes."
      },
      {
        "name": "velocity_delta",
        "offset": 12,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native velocity accumulator at matching Q12 keyframes."
      },
      {
        "name": "acceleration_delta",
        "offset": 16,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native acceleration accumulator at matching Q12 keyframes."
      },
      {
        "name": "draw_arg_0",
        "offset": 20,
        "width": 4,
        "type": "s32",
        "semantics": "First integer argument to FFX_FieldMap_StoreIntIntFloatGlobals."
      },
      {
        "name": "draw_arg_1",
        "offset": 24,
        "width": 4,
        "type": "s32",
        "semantics": "Second integer argument to FFX_FieldMap_StoreIntIntFloatGlobals."
      },
      {
        "name": "stride_quads",
        "offset": 28,
        "width": 1,
        "type": "u8",
        "semantics": "Unsigned value multiplied by four for the native drawable stride."
      },
      {
        "name": "u_position_delta",
        "offset": 32,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native position accumulator at matching Q12 keyframes."
      },
      {
        "name": "u_velocity_delta",
        "offset": 36,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native velocity accumulator at matching Q12 keyframes."
      },
      {
        "name": "u_acceleration_delta",
        "offset": 40,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native acceleration accumulator at matching Q12 keyframes."
      },
      {
        "name": "v_position_delta",
        "offset": 44,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native position accumulator at matching Q12 keyframes."
      },
      {
        "name": "v_velocity_delta",
        "offset": 48,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native velocity accumulator at matching Q12 keyframes."
      },
      {
        "name": "v_acceleration_delta",
        "offset": 52,
        "width": 4,
        "type": "f32",
        "semantics": "Delta added to the native acceleration accumulator at matching Q12 keyframes."
      }
    ],
    "usage": "Verified native record operands; host-state offsets are excluded. ",
    "status": "FECHADO",
    "notes": [
      "Verified in VM IDA 2026-10-03 against preserved FFX.exe SHA-256 78ce34397da5e6f49b72c2aebadedaf4cd3f6720e1949d46a1b8ed67d3db5ced; dispatch entries and captured native bytes checked.",
      "Corresponding installed executable 0537b2a1047f3266e73495cd4e35f63f0777f4231d417699f979954686da686d has the same non-address instruction bytes for these consumers. Addresses in this schema refer to the preserved executable.",
      "Record operands are separated from host state and descriptor/slot fields. See PPP_MATRIX_DRAW_SCHEMAS_20261003.md. Prefix guards and native stride checks remain in force."
    ],
    "handler_addr": "0x749A20",
    "semantics_category": "outro"
  },
         "pppKeMdlTfd2": {
  "window": {
    "start": 8,
    "width": 22
  },
  "payload_consumer": true,
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
  ],
  "status": "FECHADO",
  "editable": true,
  "handler_addr": "0x749C10",
  "usage": " [ONDA1b addr resolvido FFX_PppHandler_KeMdlTfd2; janela derivada mantida]",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_28",
      "offset": 28,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_29",
      "offset": 29,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    }
  ],
  "semantics_category": "outro"
},
         "pppKeMdlTfd3": {
  "window": {
    "start": 8,
    "width": 26
  },
  "payload_consumer": true,
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
  ],
  "status": "FECHADO",
  "editable": true,
  "handler_addr": "0x749F60",
  "usage": " [ONDA1b addr resolvido FFX_PppHandler_KeMdlTfd3; janela derivada mantida]",
  "fields": [
    {
      "name": "native_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    },
    {
      "name": "native_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_28",
      "offset": 28,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_29",
      "offset": 29,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_30",
      "offset": 30,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_31",
      "offset": 31,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_32",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u8_33",
      "offset": 33,
      "width": 1,
      "type": "u8",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
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
    "Native scalar reads reconciled 2026-10-07; exact amplitude width and mode offset. See PPP_RANDOM_SCALAR_SCHEMAS_20261007.md."
  ],
  "status": "FECHADO",
  "editable": true,
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "random_amplitude",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native random delta amplitude read as f32."
    },
    {
      "name": "double_random",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "semantics_category": "Random",
  "opcode": "pppRandUpFloat",
  "handler_addr": "0x72F760"
},
         "pppRandUpIV": {
  "window": {
    "start": 4,
    "width": 29
  },
  "payload_consumer": true,
  "notes": [
    "Native record reads reconciled 2026-10-07: +4 s32 destination, +16/+20/+24 amplitudes, +32 u8. No +8/+12 operands. See PPP_RANDOM_VECTOR_SCHEMAS_20261007.md."
  ],
  "status": "FECHADO",
  "fields": [
    {
      "name": "target",
      "offset": 4,
      "width": 4,
      "type": "s32",
      "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
    },
    {
      "name": "amplitude_x",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native X random delta."
    },
    {
      "name": "amplitude_y",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native Y random delta."
    },
    {
      "name": "amplitude_z",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Signed integer amplitude for the native Z random delta."
    },
    {
      "name": "double_random",
      "offset": 32,
      "width": 1,
      "type": "u8",
      "semantics": "Nonzero selects the native two-draw random mode."
    }
  ],
  "editable": true,
  "semantics_category": "Random",
  "opcode": "pppRandUpIV",
  "handler_addr": "0x731430"
},
         "pppKeOfsPt": {
  "payload_consumer": true,
  "window": {
    "start": 16,
    "width": 32
  },
  "notes": [
    "Complete native loads/callback-table/initializer reconciliation 2026-10-07; see PPP_RESOURCE_OPERATOR_SCHEMAS_20261007.md. Resource/program/state references stay inspection-only."
  ],
  "status": "FECHADO",
  "editable": true,
  "fields": [
    {
      "name": "native_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Native record operand verified through the resource operator and its helper/initializer."
    },
    {
      "name": "native_u32_44",
      "offset": 44,
      "width": 4,
      "type": "u32",
      "semantics": "Native resource/program index or state offset preserved read-only pending bounded validation.",
      "editable": false
    }
  ],
  "semantics_category": "outro",
  "handler_addr": "0x75CE20"
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
      "name": "native_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_28",
      "offset": 28,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_32",
      "offset": 32,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_36",
      "offset": 36,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_40",
      "offset": 40,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    },
    {
      "name": "native_s32_44",
      "offset": 44,
      "width": 4,
      "type": "s32",
      "semantics": "Native resource/program index or state offset, preserved read-only pending bounded reference validation.",
      "editable": false
    }
  ],
  "semantics_category": "Delta/Accum",
  "window": {
    "start": 8,
    "width": 40
  },
  "notes": [
    "Full native handler/slot/renderer/allocator witness 2026-10-07. See PPP_LIGHT_RENDER_SCHEMAS_20261007.md. Structural references/counts stay inspection-only."
  ]
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
         },
  "pppSRandUpCV": {
    "opcode": "pppSRandUpCV",
    "handler_addr": "0x7331B0",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 4,
      "width": 9
    },
    "fields": [
      {
        "name": "target",
        "offset": 4,
        "width": 4,
        "type": "s32",
        "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
      },
      {
        "name": "random_component_0",
        "offset": 8,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 0."
      },
      {
        "name": "random_component_1",
        "offset": 9,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 1."
      },
      {
        "name": "random_component_2",
        "offset": 10,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 2."
      },
      {
        "name": "random_component_3",
        "offset": 11,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 3."
      },
      {
        "name": "double_random",
        "offset": 12,
        "width": 1,
        "type": "u8",
        "semantics": "Native random helper mode byte."
      }
    ],
    "notes": [
      "Live native read/type/byte reconciliation 2026-10-07. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
    ],
    "status": "FECHADO",
    "semantics_category": "Rand"
  },
  "pppSRandDownCV": {
    "opcode": "pppSRandDownCV",
    "handler_addr": "0x7334B0",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 4,
      "width": 9
    },
    "fields": [
      {
        "name": "target",
        "offset": 4,
        "width": 4,
        "type": "s32",
        "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
      },
      {
        "name": "random_component_0",
        "offset": 8,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 0."
      },
      {
        "name": "random_component_1",
        "offset": 9,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 1."
      },
      {
        "name": "random_component_2",
        "offset": 10,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 2."
      },
      {
        "name": "random_component_3",
        "offset": 11,
        "width": 1,
        "type": "s8",
        "semantics": "Signed packed native random amplitude for component 3."
      },
      {
        "name": "double_random",
        "offset": 12,
        "width": 1,
        "type": "u8",
        "semantics": "Native random helper mode byte."
      }
    ],
    "notes": [
      "Live native read/type/byte reconciliation 2026-10-07. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
    ],
    "status": "FECHADO",
    "semantics_category": "Rand"
  },
  "pppSRandUpHCV": {
    "opcode": "pppSRandUpHCV",
    "handler_addr": "0x733AD0",
    "payload_consumer": true,
    "editable": true,
    "window": {
      "start": 4,
      "width": 13
    },
    "fields": [
      {
        "name": "target",
        "offset": 4,
        "width": 4,
        "type": "s32",
        "semantics": "Signed host-relative destination offset; -1 selects shared scratch. Preserved read-only by R6."
      },
      {
        "name": "random_component_0",
        "offset": 8,
        "width": 2,
        "type": "s16",
        "semantics": "Signed packed native random amplitude for component 0."
      },
      {
        "name": "random_component_1",
        "offset": 10,
        "width": 2,
        "type": "s16",
        "semantics": "Signed packed native random amplitude for component 1."
      },
      {
        "name": "random_component_2",
        "offset": 12,
        "width": 2,
        "type": "s16",
        "semantics": "Signed packed native random amplitude for component 2."
      },
      {
        "name": "random_component_3",
        "offset": 14,
        "width": 2,
        "type": "s16",
        "semantics": "Signed packed native random amplitude for component 3."
      },
      {
        "name": "double_random",
        "offset": 16,
        "width": 1,
        "type": "u8",
        "semantics": "Native random helper mode byte."
      }
    ],
    "notes": [
      "Live native read/type/byte reconciliation 2026-10-07. See PPP_RANDOM_PACKED_SCHEMAS_20261007.md."
    ],
    "status": "FECHADO",
    "semantics_category": "Rand"
  },
"pppMatrixXYZ": {
  "opcode": "pppMatrixXYZ",
  "handler_addr": "0x72D470",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72D470",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixXZY": {
  "opcode": "pppMatrixXZY",
  "handler_addr": "0x72D650",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72D650",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixYXZ": {
  "opcode": "pppMatrixYXZ",
  "handler_addr": "0x72D830",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72D830",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixYZX": {
  "opcode": "pppMatrixYZX",
  "handler_addr": "0x72DA10",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72DA10",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixZXY": {
  "opcode": "pppMatrixZXY",
  "handler_addr": "0x72DBF0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72DBF0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixZYX": {
  "opcode": "pppMatrixZYX",
  "handler_addr": "0x72DDD0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72DDD0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixLoop": {
  "opcode": "pppMatrixLoop",
  "handler_addr": "0x72D290",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x72D290",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixLoc": {
  "opcode": "pppMatrixLoc",
  "handler_addr": "0x734200",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734200",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrix": {
  "opcode": "pppDrawMatrix",
  "handler_addr": "0x734C40",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734C40",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrixLoop": {
  "opcode": "pppDrawMatrixLoop",
  "handler_addr": "0x734C70",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734C70",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrixFront": {
  "opcode": "pppDrawMatrixFront",
  "handler_addr": "0x734CA0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734CA0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrixNoRot": {
  "opcode": "pppDrawMatrixNoRot",
  "handler_addr": "0x734D20",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734D20",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrixWood": {
  "opcode": "pppDrawMatrixWood",
  "handler_addr": "0x734DC0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734DC0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md.",
    "Primary Draw callback 0x734DC0 and auxiliary callback 0x734E60 are both verified; neither reads the serialized command payload."
  ]
},
"pppKeDMat": {
  "opcode": "pppKeDMat",
  "handler_addr": "0x734F80",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734F80",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppKeDMatFr": {
  "opcode": "pppKeDMatFr",
  "handler_addr": "0x734FC0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734FC0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppKeDMatPht": {
  "opcode": "pppKeDMatPht",
  "handler_addr": "0x735050",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x735050",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppKeDMatPhtFr": {
  "opcode": "pppKeDMatPhtFr",
  "handler_addr": "0x7350F0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x7350F0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppKeMatPht": {
  "opcode": "pppKeMatPht",
  "handler_addr": "0x7342B0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x7342B0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppKeOfsMatXYZ": {
  "opcode": "pppKeOfsMatXYZ",
  "handler_addr": "0x7344D0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x7344D0",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppKeParMatR": {
  "opcode": "pppKeParMatR",
  "handler_addr": "0x734950",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Transform",
  "real_func": "0x734950",
  "usage": "Verified native matrix operation on instance/dispatch state; no serialized payload operand is exposed.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppSMatrix": {
  "opcode": "pppSMatrix",
  "handler_addr": null,
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_RESERVED_NULL",
  "semantics_category": "Transform",
  "real_func": null,
  "usage": "Verified null callback table in the pinned PC executables.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppMatrixFront": {
  "opcode": "pppMatrixFront",
  "handler_addr": null,
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_RESERVED_NULL",
  "semantics_category": "Transform",
  "real_func": null,
  "usage": "Verified null callback table in the pinned PC executables.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrixWoodLoop": {
  "opcode": "pppDrawMatrixWoodLoop",
  "handler_addr": "0x728AC0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Transform",
  "real_func": "0x728AC0",
  "usage": "Verified shared native return-only callback.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDrawMatrixFrontLoop": {
  "opcode": "pppDrawMatrixFrontLoop",
  "handler_addr": "0x728AC0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Transform",
  "real_func": "0x728AC0",
  "usage": "Verified shared native return-only callback.",
  "notes": [
    "Owned IDA/PE exact-byte and callback-table verification 2026-10-07. See PPP_MATRIX_OPERATIONS_20261007.md."
  ]
},
"pppDummyFunc": {
  "opcode": "pppDummyFunc",
  "handler_addr": null,
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_RESERVED_NULL",
  "semantics_category": "Native dispatch",
  "real_func": null,
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppFaceAp": {
  "opcode": "pppFaceAp",
  "handler_addr": "0x757FB0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Native dispatch",
  "real_func": "0x757FB0",
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeAcmSolid": {
  "opcode": "pppKeAcmSolid",
  "handler_addr": "0x75B770",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Native dispatch",
  "real_func": "0x75B770",
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeMvYpEff": {
  "opcode": "pppKeMvYpEff",
  "handler_addr": "0x75E400",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x75E400",
  "usage": "Derives native angle state from movement state; does not read serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeShpTail3XImm": {
  "opcode": "pppKeShpTail3XImm",
  "handler_addr": "0x728AC0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Native dispatch",
  "real_func": "0x728AC0",
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeShpTailLc": {
  "opcode": "pppKeShpTailLc",
  "handler_addr": "0x7550A0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7550A0",
  "usage": "Initializes native trail history state; update and draw callbacks return immediately.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThCp": {
  "opcode": "pppKeThCp",
  "handler_addr": "0x75B7C0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Native dispatch",
  "real_func": "0x75B7C0",
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThCpSft": {
  "opcode": "pppKeThCpSft",
  "handler_addr": "0x75B7F0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Native dispatch",
  "real_func": "0x75B7F0",
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThLz": {
  "opcode": "pppKeThLz",
  "handler_addr": "0x75B800",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_NO_OP",
  "semantics_category": "Native dispatch",
  "real_func": "0x75B800",
  "usage": "Verified native host-state operation with no serialized record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes": {
  "opcode": "pppKeThRes",
  "handler_addr": "0x736E10",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736E10",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 3, stride 224 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes128": {
  "opcode": "pppKeThRes128",
  "handler_addr": "0x736750",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736750",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 128, stride 6224 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes128x4": {
  "opcode": "pppKeThRes128x4",
  "handler_addr": "0x736780",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736780",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 128, stride 6224 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes128x8": {
  "opcode": "pppKeThRes128x8",
  "handler_addr": "0x7367B0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7367B0",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 128, stride 6224 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes16": {
  "opcode": "pppKeThRes16",
  "handler_addr": "0x7367E0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7367E0",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 16, stride 848 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes16x16": {
  "opcode": "pppKeThRes16x16",
  "handler_addr": "0x736810",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736810",
  "usage": "Native host-state chain initializer: 16 linked block(s), byte parameter 16, stride 848 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes16x24": {
  "opcode": "pppKeThRes16x24",
  "handler_addr": "0x736840",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736840",
  "usage": "Native host-state chain initializer: 24 linked block(s), byte parameter 16, stride 848 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes16x4": {
  "opcode": "pppKeThRes16x4",
  "handler_addr": "0x736870",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736870",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 16, stride 848 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes16x64": {
  "opcode": "pppKeThRes16x64",
  "handler_addr": "0x7368A0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7368A0",
  "usage": "Native host-state chain initializer: 64 linked block(s), byte parameter 16, stride 848 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes16x8": {
  "opcode": "pppKeThRes16x8",
  "handler_addr": "0x7368D0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7368D0",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 16, stride 848 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes24": {
  "opcode": "pppKeThRes24",
  "handler_addr": "0x736900",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736900",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 24, stride 1232 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes24x16": {
  "opcode": "pppKeThRes24x16",
  "handler_addr": "0x736930",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736930",
  "usage": "Native host-state chain initializer: 16 linked block(s), byte parameter 24, stride 1232 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes24x4": {
  "opcode": "pppKeThRes24x4",
  "handler_addr": "0x736960",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736960",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 24, stride 1232 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes24x8": {
  "opcode": "pppKeThRes24x8",
  "handler_addr": "0x736990",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736990",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 24, stride 1232 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes255": {
  "opcode": "pppKeThRes255",
  "handler_addr": "0x7369C0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7369C0",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 255, stride 12320 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes255x4": {
  "opcode": "pppKeThRes255x4",
  "handler_addr": "0x7369F0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x7369F0",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 255, stride 12320 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes32": {
  "opcode": "pppKeThRes32",
  "handler_addr": "0x736A20",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736A20",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 32, stride 1616 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes32x16": {
  "opcode": "pppKeThRes32x16",
  "handler_addr": "0x736A50",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736A50",
  "usage": "Native host-state chain initializer: 16 linked block(s), byte parameter 32, stride 1616 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes32x24": {
  "opcode": "pppKeThRes32x24",
  "handler_addr": "0x736A80",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736A80",
  "usage": "Native host-state chain initializer: 24 linked block(s), byte parameter 32, stride 1616 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes32x32": {
  "opcode": "pppKeThRes32x32",
  "handler_addr": "0x736AB0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736AB0",
  "usage": "Native host-state chain initializer: 32 linked block(s), byte parameter 32, stride 1616 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes32x4": {
  "opcode": "pppKeThRes32x4",
  "handler_addr": "0x736AE0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736AE0",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 32, stride 1616 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes32x8": {
  "opcode": "pppKeThRes32x8",
  "handler_addr": "0x736B10",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736B10",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 32, stride 1616 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes40": {
  "opcode": "pppKeThRes40",
  "handler_addr": "0x736B40",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736B40",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 40, stride 2000 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes40x16": {
  "opcode": "pppKeThRes40x16",
  "handler_addr": "0x736B70",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736B70",
  "usage": "Native host-state chain initializer: 16 linked block(s), byte parameter 40, stride 2000 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes40x4": {
  "opcode": "pppKeThRes40x4",
  "handler_addr": "0x736BA0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736BA0",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 40, stride 2000 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes40x8": {
  "opcode": "pppKeThRes40x8",
  "handler_addr": "0x736BD0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736BD0",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 40, stride 2000 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes48": {
  "opcode": "pppKeThRes48",
  "handler_addr": "0x736C00",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736C00",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 48, stride 2384 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes48x16": {
  "opcode": "pppKeThRes48x16",
  "handler_addr": "0x736C30",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736C30",
  "usage": "Native host-state chain initializer: 16 linked block(s), byte parameter 48, stride 2384 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes48x4": {
  "opcode": "pppKeThRes48x4",
  "handler_addr": "0x736C60",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736C60",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 48, stride 2384 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes48x8": {
  "opcode": "pppKeThRes48x8",
  "handler_addr": "0x736C90",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736C90",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 48, stride 2384 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes64": {
  "opcode": "pppKeThRes64",
  "handler_addr": "0x736CC0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736CC0",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 64, stride 3152 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes64x16": {
  "opcode": "pppKeThRes64x16",
  "handler_addr": "0x736CF0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736CF0",
  "usage": "Native host-state chain initializer: 16 linked block(s), byte parameter 64, stride 3152 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes64x4": {
  "opcode": "pppKeThRes64x4",
  "handler_addr": "0x736D20",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736D20",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 64, stride 3152 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes64x8": {
  "opcode": "pppKeThRes64x8",
  "handler_addr": "0x736D50",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736D50",
  "usage": "Native host-state chain initializer: 8 linked block(s), byte parameter 64, stride 3152 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes8": {
  "opcode": "pppKeThRes8",
  "handler_addr": "0x736D80",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736D80",
  "usage": "Native host-state chain initializer: 1 linked block(s), byte parameter 8, stride 464 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes8x128": {
  "opcode": "pppKeThRes8x128",
  "handler_addr": "0x736DB0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736DB0",
  "usage": "Native host-state chain initializer: 128 linked block(s), byte parameter 8, stride 464 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppKeThRes8x4": {
  "opcode": "pppKeThRes8x4",
  "handler_addr": "0x736DE0",
  "payload_consumer": false,
  "editable": false,
  "window": null,
  "fields": [],
  "status": "NATIVE_HOST_STATE_ONLY",
  "semantics_category": "Native state",
  "real_func": "0x736DE0",
  "usage": "Native host-state chain initializer: 4 linked block(s), byte parameter 8, stride 464 bytes. Constants belong to the callback, not record operands.",
  "notes": [
    "Owned Windows IDA/PE byte and native callback-table verification 2026-10-07. See PPP_NATIVE_RESERVE_CONTRACTS_20261007.md.",
    "No write window is inferred from instance state, allocation constants, null callbacks or return-only callbacks."
  ]
},
"pppDrawShapeRev": {
  "opcode": "pppDrawShapeRev",
  "handler_addr": "0x741660",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 8
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native KR resource-table index.",
      "editable": false
    },
    {
      "name": "param_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Native animation phase increment."
    },
    {
      "name": "param_u8_12",
      "offset": 12,
      "width": 1,
      "type": "u8",
      "semantics": "Native renderer flag; +15 seeds axis flips during initialization."
    },
    {
      "name": "param_u8_13",
      "offset": 13,
      "width": 1,
      "type": "u8",
      "semantics": "Native renderer flag; +15 seeds axis flips during initialization."
    },
    {
      "name": "param_u8_14",
      "offset": 14,
      "width": 1,
      "type": "u8",
      "semantics": "Native renderer flag; +15 seeds axis flips during initialization."
    },
    {
      "name": "param_u8_15",
      "offset": 15,
      "width": 1,
      "type": "u8",
      "semantics": "Native renderer flag; +15 seeds axis flips during initialization."
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3b798"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppDrawShapeRev",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Native draw",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppEiZCrctDisPos": {
  "opcode": "pppEiZCrctDisPos",
  "handler_addr": "0x735FA0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 16,
    "width": 33
  },
  "fields": [
    {
      "name": "param_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection position offset."
    },
    {
      "name": "param_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection position offset."
    },
    {
      "name": "param_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native projection position offset."
    },
    {
      "name": "param_f32_32",
      "offset": 32,
      "width": 4,
      "type": "f32",
      "semantics": "Matrix row scale passed on the stack to the vec4 helper."
    },
    {
      "name": "param_f32_36",
      "offset": 36,
      "width": 4,
      "type": "f32",
      "semantics": "Matrix row scale passed on the stack to the vec4 helper."
    },
    {
      "name": "param_f32_40",
      "offset": 40,
      "width": 4,
      "type": "f32",
      "semantics": "Matrix row scale passed on the stack to the vec4 helper."
    },
    {
      "name": "param_u8_48",
      "offset": 48,
      "width": 1,
      "type": "u8",
      "semantics": "Native projection-space selector."
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3b810"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppEiZCrctDisPos",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Native draw",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppKeShpDtt": {
  "opcode": "pppKeShpDtt",
  "handler_addr": "0x7550F0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 18
  },
  "fields": [
    {
      "name": "param_f32_4",
      "offset": 4,
      "width": 4,
      "type": "f32",
      "semantics": "Native PVA impulse."
    },
    {
      "name": "param_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native PVA impulse."
    },
    {
      "name": "param_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native PVA impulse."
    },
    {
      "name": "param_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail width scale."
    },
    {
      "name": "param_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail width scale."
    },
    {
      "name": "param_u8_24",
      "offset": 24,
      "width": 1,
      "type": "u8",
      "semantics": "Native renderer control."
    },
    {
      "name": "param_u8_25",
      "offset": 25,
      "width": 1,
      "type": "u8",
      "semantics": "Native renderer control."
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3ac08"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppKeShpDtt",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Trail/PVA",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppKeShpTail": {
  "opcode": "pppKeShpTail",
  "handler_addr": "0x74C570",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 26
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native KR resource index.",
      "editable": false
    },
    {
      "name": "param_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Resource-dependent animation phase increment.",
      "editable": false
    },
    {
      "name": "param_s16_16",
      "offset": 16,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_s16_18",
      "offset": 18,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_s16_20",
      "offset": 20,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_s16_22",
      "offset": 22,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail geometry coefficient."
    },
    {
      "name": "param_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail geometry coefficient."
    },
    {
      "name": "param_u16_32",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native segment count; safe editing range is not established.",
      "editable": false
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3a8e8"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppKeShpTail",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Trail/PVA",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppKeShpTailX": {
  "opcode": "pppKeShpTailX",
  "handler_addr": "0x74D570",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 29
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native KR resource index.",
      "editable": false
    },
    {
      "name": "param_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Resource-dependent animation phase increment.",
      "editable": false
    },
    {
      "name": "param_s16_16",
      "offset": 16,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_s16_18",
      "offset": 18,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_s16_20",
      "offset": 20,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_s16_22",
      "offset": 22,
      "width": 2,
      "type": "s16",
      "semantics": "Signed native trail color component."
    },
    {
      "name": "param_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail geometry coefficient."
    },
    {
      "name": "param_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail geometry coefficient."
    },
    {
      "name": "param_u16_32",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native segment count; safe editing range is not established.",
      "editable": false
    },
    {
      "name": "param_u8_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    },
    {
      "name": "param_u8_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    },
    {
      "name": "param_u8_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3b220"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppKeShpTailX",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Trail/PVA",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppKeShpTail2": {
  "opcode": "pppKeShpTail2",
  "handler_addr": "0x74E570",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 27
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native KR resource index.",
      "editable": false
    },
    {
      "name": "param_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Resource-dependent animation phase increment.",
      "editable": false
    },
    {
      "name": "param_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail width interpolation scalar; Tail2 has explicit zero guards."
    },
    {
      "name": "param_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail width interpolation scalar; Tail2 has explicit zero guards."
    },
    {
      "name": "param_u8_20",
      "offset": 20,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_21",
      "offset": 21,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_22",
      "offset": 22,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_23",
      "offset": 23,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_24",
      "offset": 24,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_25",
      "offset": 25,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_26",
      "offset": 26,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_27",
      "offset": 27,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail geometry coefficient."
    },
    {
      "name": "param_u16_32",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native segment count; safe editing range is not established.",
      "editable": false
    },
    {
      "name": "param_u8_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3a910"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppKeShpTail2",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Trail/PVA",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppKeShpTail2X": {
  "opcode": "pppKeShpTail2X",
  "handler_addr": "0x74F5B0",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 30
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native KR resource index.",
      "editable": false
    },
    {
      "name": "param_u16_8",
      "offset": 8,
      "width": 2,
      "type": "u16",
      "semantics": "Resource-dependent animation phase increment.",
      "editable": false
    },
    {
      "name": "param_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail width interpolation scalar; Tail2 has explicit zero guards.",
      "non_zero": true
    },
    {
      "name": "param_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail width interpolation scalar; Tail2 has explicit zero guards."
    },
    {
      "name": "param_u8_20",
      "offset": 20,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_21",
      "offset": 21,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_22",
      "offset": 22,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_23",
      "offset": 23,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_24",
      "offset": 24,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_25",
      "offset": 25,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_26",
      "offset": 26,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_u8_27",
      "offset": 27,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail packed-color component."
    },
    {
      "name": "param_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native trail geometry coefficient."
    },
    {
      "name": "param_u16_32",
      "offset": 32,
      "width": 2,
      "type": "u16",
      "semantics": "Native segment count; safe editing range is not established.",
      "editable": false
    },
    {
      "name": "param_u8_34",
      "offset": 34,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    },
    {
      "name": "param_u8_35",
      "offset": 35,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    },
    {
      "name": "param_u8_36",
      "offset": 36,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    },
    {
      "name": "param_u8_37",
      "offset": 37,
      "width": 1,
      "type": "u8",
      "semantics": "Native trail renderer control."
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3b478"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppKeShpTail2X",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Trail/PVA",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppNeiDrawMdlPointLight": {
  "opcode": "pppNeiDrawMdlPointLight",
  "handler_addr": "0x74A2B0",
  "payload_consumer": true,
  "editable": false,
  "window": {
    "start": 8,
    "width": 40
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native model-resource index.",
      "editable": false
    },
    {
      "name": "param_s32_8",
      "offset": 8,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_12",
      "offset": 12,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_16",
      "offset": 16,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_20",
      "offset": 20,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_24",
      "offset": 24,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_28",
      "offset": 28,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_32",
      "offset": 32,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_36",
      "offset": 36,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_40",
      "offset": 40,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_44",
      "offset": 44,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3de08"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppNeiDrawMdlPointLight",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Light/resource",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
},
"pppNeiDrawMdlTsPointLight": {
  "opcode": "pppNeiDrawMdlTsPointLight",
  "handler_addr": "0x74AF40",
  "payload_consumer": true,
  "editable": true,
  "window": {
    "start": 8,
    "width": 64
  },
  "fields": [
    {
      "name": "param_u32_4",
      "offset": 4,
      "width": 4,
      "type": "u32",
      "semantics": "Native model-resource index.",
      "editable": false
    },
    {
      "name": "param_f32_8",
      "offset": 8,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture PVA impulse, on a matching timestamp."
    },
    {
      "name": "param_f32_12",
      "offset": 12,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture PVA impulse, on a matching timestamp."
    },
    {
      "name": "param_f32_16",
      "offset": 16,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture PVA impulse, on a matching timestamp."
    },
    {
      "name": "param_f32_20",
      "offset": 20,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture PVA impulse, on a matching timestamp."
    },
    {
      "name": "param_f32_24",
      "offset": 24,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture PVA impulse, on a matching timestamp."
    },
    {
      "name": "param_f32_28",
      "offset": 28,
      "width": 4,
      "type": "f32",
      "semantics": "Native texture PVA impulse, on a matching timestamp."
    },
    {
      "name": "param_s32_32",
      "offset": 32,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_36",
      "offset": 36,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_40",
      "offset": 40,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_44",
      "offset": 44,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_48",
      "offset": 48,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_52",
      "offset": 52,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_56",
      "offset": 56,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_60",
      "offset": 60,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_64",
      "offset": 64,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    },
    {
      "name": "param_s32_68",
      "offset": 68,
      "width": 4,
      "type": "s32",
      "semantics": "Program index / native state offset used by the shared point-light consumer.",
      "editable": false
    }
  ],
  "status": "FECHADO",
  "entry_addrs": [
    "0xc3de58"
  ],
  "handler_name_canonical": "FFX_PppHandler_pppNeiDrawMdlTsPointLight",
  "usage": "Verified native serialized operands and helper chain; protected references and counts are inspection-only.",
  "semantics_category": "Light/resource",
  "notes": [
    "Owned Windows IDA snapshot and both executable callback tables verified 2026-10-07. See PPP_RESIDUAL_PAYLOAD_SCHEMAS_20261007.md.",
    "Exact preserved executable bytes and normalized installed counterparts captured. Stack arguments were checked where old decompiler prototypes omit them; no host-state offsets are exposed as record fields.",
    "Generic prefix guard R6 and individual field permissions remain in force. No arbitrary program, state, allocation count or resource index editing is granted."
  ]
}
}
""";
    }
}
