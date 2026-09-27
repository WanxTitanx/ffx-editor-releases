using System.Collections.Generic;

namespace FFXProjectEditor.FfxLib.MagicDll
{
    /// <summary>
    /// Catálogo embutido das tabelas de handlers locais (fp.h) dos efeitos conhecidos.
    /// Fallback do <see cref="MagicFpHandlerTable"/> quando o fp.h do Yonishi não está
    /// acessível na máquina (ex.: testes rodando sem a extração do jogo).
    ///
    /// Fonte: fp.h reais na extração configurada pelo usuário (ffx_ps2/ffx/yonishi_data/dat_ov).
    /// mag_0021\par\fp.h e mag_0098\par\fp.h, extraídos em 2026-08-02 via
    /// work/c3_keys/_extract_fp_tables.py. A tabela é LOCAL ao efeito — o mesmo opcode
    /// tem índices diferentes por efeito (0021: DrawMdl=30; 0098: DrawMdl=26).
    /// </summary>
    public static class MagicKnownEffectHandlers
    {
        private static readonly IReadOnlyDictionary<int, IReadOnlyDictionary<int, string>> Known =
            new Dictionary<int, IReadOnlyDictionary<int, string>>
            {
                // magic_0021.dll — 37 handlers (pppProgTbl_FP)
                [21] = new Dictionary<int, string>
                {
                    { 0, "pppKeThRes32" },
                    { 1, "pppAccele" },
                    { 2, "pppAngAccele" },
                    { 3, "pppSclAccele" },
                    { 4, "pppColAccele" },
                    { 5, "pppMove" },
                    { 6, "pppAngMove" },
                    { 7, "pppSclMove" },
                    { 8, "pppColMove" },
                    { 9, "pppPoint" },
                    { 10, "pppAngle" },
                    { 11, "pppScale" },
                    { 12, "pppColor" },
                    { 13, "pppKeDrct" },
                    { 14, "pppRandFV" },
                    { 15, "pppRandUpFV" },
                    { 16, "pppRandIV" },
                    { 17, "pppSRandFV" },
                    { 18, "pppSMatrix" },
                    { 19, "pppMatrixXYZ" },
                    { 20, "pppMatrixYXZ" },
                    { 21, "pppMatrixLoc" },
                    { 22, "pppMatrixScl" },
                    { 23, "pppKeParMatR" },
                    { 24, "pppDrawMatrix" },
                    { 25, "pppDrawMatrixFront" },
                    { 26, "pppKeDMatFr" },
                    { 27, "pppKeThTp" },
                    { 28, "pppKeThSft" },
                    { 29, "pppKeTh" },
                    { 30, "pppDrawMdl" },
                    { 31, "pppDrawMdlTs" },
                    { 32, "pppDrawShape" },
                    { 33, "pppKeMdlDtt" },
                    { 34, "pppVertexAp" },
                    { 35, "pppKeBornRnd3" },
                    { 36, "pppKeBornRnd6" },
                },

                // magic_0098.dll — 39 handlers (ordem do .rdata do PC, 2026-08-02;
                // o fp.h do Yonishi PS2 tinha 35 e estava DESLOCADO de 26 em diante:
                // o PC inseriu KeZCrct(26)/KeZCrctShp(27)/DrawMdl3(32)/DrawShapeX(34))
                [98] = new Dictionary<int, string>
                {
                    { 0, "pppAccele" },
                    { 1, "pppAngAccele" },
                    { 2, "pppSclAccele" },
                    { 3, "pppColAccele" },
                    { 4, "pppMove" },
                    { 5, "pppAngMove" },
                    { 6, "pppSclMove" },
                    { 7, "pppColMove" },
                    { 8, "pppPoint" },
                    { 9, "pppAngle" },
                    { 10, "pppScale" },
                    { 11, "pppColor" },
                    { 12, "pppRandFV" },
                    { 13, "pppRandUpFV" },
                    { 14, "pppRandDownFV" },
                    { 15, "pppRandIV" },
                    { 16, "pppSRandFV" },
                    { 17, "pppSRandUpFV" },
                    { 18, "pppSRandDownFV" },
                    { 19, "pppSMatrix" },
                    { 20, "pppMatrixXYZ" },
                    { 21, "pppMatrixYXZ" },
                    { 22, "pppMatrixScl" },
                    { 23, "pppParMatrix" },
                    { 24, "pppDrawMatrix" },
                    { 25, "pppDrawMatrixFront" },
                    { 26, "pppKeZCrct" },
                    { 27, "pppKeZCrctShp" },
                    { 28, "pppDrawMdl" },
                    { 29, "pppDrawMdl2" },
                    { 30, "pppDrawMdlSemi2" },
                    { 31, "pppDrawMdlTs2" },
                    { 32, "pppDrawMdl3" },
                    { 33, "pppDrawShape" },
                    { 34, "pppDrawShapeX" },
                    { 35, "pppKeShpTail2" },
                    { 36, "pppVertexAp" },
                    { 37, "pppVertexApLc" },
                    { 38, "pppKeBornRnd5" },
                },
            };

        /// <summary>Retorna a tabela local do efeito, ou null quando o efeito não é conhecido.</summary>
        public static IReadOnlyDictionary<int, string>? TryGet(int magicId)
        {
            return Known.TryGetValue(magicId, out IReadOnlyDictionary<int, string>? table) ? table : null;
        }
    }
}
