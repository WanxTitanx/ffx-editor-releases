using System.Collections.Generic;

namespace FFXProjectEditor.Modules.Common;

/// <summary>
/// Contrato para módulos que suportam captura/restauração de estado no histórico de navegação
/// (back/forward). Implementado por UserControls que o <c>Main_Window</c> hospeda no <c>ContentFrame</c>.
/// </summary>
/// <remarks>
/// <b>Jarvis-UI v2.160.1.0 (Fase D §16).</b> Antes deste contrato, <c>Main_Window</c> fazia pattern-match
/// em tipos concretos (<c>MonEditorSelector_Control</c>, <c>KernelCommands_Control</c>) pra capturar
/// estado; isso não escala pra ~38 módulos. Agora <c>Main_Window</c> pergunta ao controle se ele é
/// <see cref="IRestorableModule"/>; se for, captura/restaura estado opaco via este contrato.
/// <para>
/// <b>Semântica do retorno:</b>
/// <list type="bullet">
/// <item><description><see cref="CaptureState"/> retornando <c>null</c> = módulo stateless
/// (ou sem estado significativo pra restaurar). Back/forward ainda volta pro módulo, só não restaura
/// sub-seleção. É o caso de explorers read-only.</description></item>
/// <item><description><see cref="CaptureState"/> retornando um <see cref="Dictionary{TKey, TValue}"/>
/// não-vazio = o módulo tem estado (SelectedIndex, FilterText, ScrollOffset, tab ativa...) que deve ser
/// reposto no restore. As chaves são livres (convenção do módulo); o <c>Main_Window</c> é opaco ao conteúdo.</description></item>
/// </list>
/// </para>
/// <para><b>Convenção de chaves sugerida:</b> <c>selectedIndex</c> (int?), <c>filterText</c> (string),
/// <c>scrollOffset</c> (double), <c>activeTab</c> (int?). O módulo decide o que capturar.</para>
/// <para><b>Idempotência:</b> <see cref="RestoreState"/> deve ser seguro de chamar com <c>null</c>
/// (não-op) ou com um dicionário vazio.</para>
/// </remarks>
public interface IRestorableModule
{
    /// <summary>
    /// Captura o estado atual do módulo (seleção, filtro, scroll, tab ativa...) num dicionário opaco.
    /// Retorna <c>null</c> se o módulo é stateless ou não tem estado significativo pra restaurar.
    /// </summary>
    /// <returns>Dicionário de estado, ou <c>null</c>.</returns>
    /// <remarks>
    /// <b>Jarvis-UI Fase D §D4:</b> a implementação padrão retorna <c>null</c> (stateless). Controles que só
    /// precisam declarar a interface (explorers read-only, hubs sem filtro) herdam esse default sem escrever
    /// nada; controles com estado (filtro/seleção) fazem override explícito.
    /// </remarks>
    Dictionary<string, object?>? CaptureState() => null;

    /// <summary>
    /// Restaura o estado previamente capturado por <see cref="CaptureState"/>.
    /// Seguro chamar com <c>null</c> ou dicionário vazio (não-op).
    /// </summary>
    /// <param name="state">Estado capturado, ou <c>null</c>.</param>
    /// <remarks>
    /// <b>Jarvis-UI Fase D §D4:</b> a implementação padrão é não-op. Controles stateless herdam sem escrever.
    /// </remarks>
    void RestoreState(Dictionary<string, object?>? state) { }
}
