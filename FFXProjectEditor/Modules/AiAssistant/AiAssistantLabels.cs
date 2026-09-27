using FFXProjectEditor.Resources;
using System;

namespace FFXProjectEditor.Modules.AiAssistant
{
    /// <summary>
    /// Forward das labels do Assistente IA para Strings (migração completa da lane Jarvis-MAGIC-IA, 2026-08-16).
    /// As chaves reais vivem em Strings.resx (EN), Strings.pt.resx (PT-BR) e Strings.cs (props).
    /// Este arquivo existe apenas para: (a) a helper RejectReason(), que mapeia enum→texto sem armazenar novas chaves;
    /// (b) compatibilidade retroativa do DataModel — poderíamos migrar para Strings direto, mas esta classe é fina
    /// e mantém o diff do DataModel pequeno. Se quiser consolidar, basta trocar AiAssistantLabels.X → Strings.X no DataModel.
    /// </summary>
    public static class AiAssistantLabels
    {
        public static string ApiKeyField => Strings.AiAssistant_ApiKeyField;
        public static string ProviderField => Strings.AiAssistant_ProviderField;
        public static string EndpointField => Strings.AiAssistant_EndpointField;
        public static string ModelField => Strings.AiAssistant_ModelField;
        public static string CommandField => Strings.AiAssistant_CommandField;
        public static string CommandHint => Strings.AiAssistant_CommandHint;
        public static string ConnectButton => Strings.AiAssistant_ConnectButton;
        public static string ClearButton => Strings.AiAssistant_ClearButton;
        public static string SendButton => Strings.AiAssistant_SendButton;
        public static string SessionSection => Strings.AiAssistant_SessionSection;
        public static string HistorySection => Strings.AiAssistant_HistorySection;
        public static string OutputSection => Strings.AiAssistant_OutputSection;
        public static string ConfirmRemoteCheck => Strings.AiAssistant_ConfirmRemoteCheck;
        public static string Splitter => Strings.AiAssistant_Splitter;
        public static string SessionNotConnected => Strings.AiAssistant_SessionNotConnected;
        public static string SessionNoKey => Strings.AiAssistant_SessionNoKey;
        public static string SessionConnected => Strings.AiAssistant_SessionConnected;
        public static string SessionFailed => Strings.AiAssistant_SessionFailed;
        public static string SessionCleared => Strings.AiAssistant_SessionCleared;
        public static string RemoteConfirmNeeded => Strings.AiAssistant_RemoteConfirmNeeded;
        public static string TitleRemoteConfirm => Strings.AiAssistant_TitleRemoteConfirm;
        public static string TitleProposal => Strings.AiAssistant_TitleProposal;
        public static string TitleResult => Strings.AiAssistant_TitleResult;

        public static string RejectReason(FFXProjectEditor.Core.LLM.LlmRejectionReason reason) => reason switch
        {
            FFXProjectEditor.Core.LLM.LlmRejectionReason.OptInDisabled => Strings.U_Aia_RejectOptIn,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.UnknownRecipe => Strings.U_Aia_RejectUnknownRecipe,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.MissingPrecondition => Strings.U_Aia_RejectMissingPrecondition,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.OperationNotAllowed => Strings.U_Aia_RejectOperationNotAllowed,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.LimitsExceeded => Strings.U_Aia_RejectLimitsExceeded,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.BeforeHashMismatch => Strings.U_Aia_RejectBeforeHashMismatch,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.MalformedPayload => Strings.U_Aia_RejectMalformedPayload,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.RemoteEndpointNotConfirmed => Strings.U_Aia_RejectRemoteNotConfirmed,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.VerificationFailed => Strings.U_Aia_RejectVerificationFailed,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.RoundTripFailed => Strings.U_Aia_RejectRoundTripFailed,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.RegressionFailed => Strings.U_Aia_RejectRegressionFailed,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.Cancelled => Strings.U_Aia_RejectCancelled,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.Timeout => Strings.U_Aia_RejectTimeout,
            FFXProjectEditor.Core.LLM.LlmRejectionReason.SecretDetected => Strings.U_Aia_RejectSecretDetected,
            _ => string.Empty
        };
    }
}
