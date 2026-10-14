using CommunityToolkit.Mvvm.ComponentModel;
using FFXProjectEditor.Utils;
using System;

namespace FFXProjectEditor.Services
{
    /// <summary>
    /// SPIRA FORGE — espinha de enderecamento (Fase 0).
    ///
    /// Guarda o <c>field_token</c> selecionado e notifica os modos que reagem a ele.
    /// E um observable singleton (SingletonBase), 100% offline / read-only: NAO toca o
    /// jogo vivo nem o probe. O valor e um candidato estrutural vindo do bridge CSV
    /// (decision_band = field_token_bridge_candidate), nao um mapeamento confirmado.
    /// </summary>
    public sealed partial class FieldContext : SingletonBase<FieldContext>
    {
        // field_token estrutural-candidato (ex.: "azit03_a"). null = nada selecionado.
        [ObservableProperty] private string? fieldToken;

        // Contexto do bridge pro field selecionado (so leitura, alimenta os consumidores).
        [ObservableProperty] private string? selectedArea;       // ex.: "azit"
        [ObservableProperty] private string? selectedMapEntity;  // ex.: "map/azit/azit03"

        // Battle escolhido no encounter peek do Hub (= nome de pasta btl_*, ex.: "azit03_00"). O Formation
        // Editor lê isto pra pular direto pro battle. Independente do field (handoff encounter -> formação).
        [ObservableProperty] private string? selectedBattleId;

        /// <summary>Publica o battle selecionado (handoff Hub encounter peek -> Formation Editor).</summary>
        public void SelectBattle(string? battleId) => SelectedBattleId = battleId;

        /// <summary>
        /// Disparado sempre que o field selecionado MUDA. Payload = field_token (ou null).
        /// Os modos do hub assinam isto pra reagir (encounter peek, map deep-link, ...).
        /// </summary>
        public event Action<string?>? SelectedFieldChanged;

        /// <summary>
        /// Seleciona um field por token. <paramref name="area"/> e <paramref name="mapEntity"/>
        /// sao contexto opcional vindo do bridge CSV.
        /// </summary>
        public void SelectField(string? token, string? area = null, string? mapEntity = null)
        {
            SelectedArea = area;
            SelectedMapEntity = mapEntity;
            FieldToken = token; // dispara OnFieldTokenChanged -> SelectedFieldChanged
        }

        public void ClearField() => SelectField(null);

        partial void OnFieldTokenChanged(string? value)
        {
            SelectedFieldChanged?.Invoke(value);
        }
    }
}
