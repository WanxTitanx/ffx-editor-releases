using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FFXProjectEditor.Core.LLM;
using FFXProjectEditor.Modules.AiAssistant;
using Xunit;

namespace FFXProjectEditor.Tests.Modules.AiAssistant
{
    /// <summary>
    /// Loop do agente (Jarvis-UI 2026-09-15): o runner depende do delegate AgentRound —
    /// os testes alimentam respostas roteirizadas (tool_calls → texto) sem rede real.
    /// </summary>
    public class AiAgentRunnerTests : IDisposable
    {
        readonly string _root = Path.Combine(Path.GetTempPath(), "agentrun-" + Guid.NewGuid().ToString("N"));

        public AiAgentRunnerTests()
        {
            Directory.CreateDirectory(_root);
            File.WriteAllBytes(Path.Combine(_root, "a.bin"), new byte[] { 1, 2, 3, 4 });
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        AgentToolContext Ctx() => new()
        {
            WorkspaceRoot = _root,
            Settings = new LlmSessionSettings
            {
                IsEnabled = true, Provider = "t", Endpoint = new Uri("http://127.0.0.1:1"), ModelId = "m",
                AllowedDataKinds = new[] { LlmDataKind.DiffContext }, CapabilityFlags = LlmCapabilityFlag.ByteReplace,
                MaxResponseBytes = 4096, MaxProposalBytes = 2048, RequestTimeout = TimeSpan.FromSeconds(5),
            },
        };

        sealed class Sink
        {
            public readonly List<(string Title, string Detail)> Rows = new();
            public readonly List<PatchProposal> Proposals = new();
        }

        static AiAgentRunner Runner(Sink sink)
        {
            var r = new AiAgentRunner();
            r.Row += (t, d, _) => sink.Rows.Add((t, d));
            r.ProposalPending += p => sink.Proposals.Add(p);
            return r;
        }

        [Fact]
        public async Task AgentLoop_ExecutesToolCalls_ThenFinalText()
        {
            var sink = new Sink();
            var runner = Runner(sink);
            var captured = new List<IReadOnlyList<LlmChatMessage>>();
            int round = 0;

            AgentRound scripted = (msgs, tools, ct) =>
            {
                captured.Add(msgs.ToList());
                round++;
                if (round == 1)
                    return Task.FromResult(new LlmResult
                    {
                        Reason = LlmRejectionReason.None,
                        FinishReason = "tool_calls",
                        ToolCalls = new[] { new LlmToolCall
                        {
                            Id = "c1", Name = "read_bytes",
                            ArgumentsJson = "{\"path\":\"a.bin\",\"offset\":0,\"length\":4}",
                        }},
                    });
                return Task.FromResult(new LlmResult
                {
                    Reason = LlmRejectionReason.None, FinishReason = "stop",
                    Message = "done — header says 01020304",
                });
            };

            await runner.RunAsync(scripted, Ctx(), "inspect a.bin", CancellationToken.None);

            // round 2 recebeu: system + user + assistant(tool_calls) + tool result
            Assert.Equal(4, captured[1].Count);
            Assert.Equal("tool", captured[1][3].Role);
            Assert.Equal("c1", captured[1][3].ToolCallId);
            Assert.Contains("01020304", captured[1][3].Content!);
            // UI viu a chamada, o resultado e a resposta final
            Assert.Contains(sink.Rows, r => r.Title == "⚙ read_bytes");
            Assert.Contains(sink.Rows, r => r.Title == "Agent" && r.Detail.Contains("done"));
        }

        [Fact]
        public async Task AgentLoop_EndpointError_PropagatesAsRow()
        {
            var sink = new Sink();
            var runner = Runner(sink);
            AgentRound failing = (_, _, _) => Task.FromResult(new LlmResult
            {
                Reason = LlmRejectionReason.Timeout, Message = "timed out",
            });

            await runner.RunAsync(failing, Ctx(), "hi", CancellationToken.None);

            Assert.Contains(sink.Rows, r => r.Title == "Agent" && r.Detail == "timed out");
        }

        [Fact]
        public async Task AgentLoop_MaxRounds_StopsCleanly()
        {
            var sink = new Sink();
            var runner = Runner(sink);
            AgentRound loopForever = (_, _, _) => Task.FromResult(new LlmResult
            {
                Reason = LlmRejectionReason.None, FinishReason = "tool_calls",
                ToolCalls = new[] { new LlmToolCall { Id = "x", Name = "list_dir", ArgumentsJson = "{}" } },
            });

            await runner.RunAsync(loopForever, Ctx(), "loop", CancellationToken.None);

            Assert.Contains(sink.Rows, r => r.Detail.Contains("max agent rounds"));
        }

        [Fact]
        public async Task AgentLoop_Cancellation_Propagates()
        {
            var sink = new Sink();
            var runner = Runner(sink);
            AgentRound canceller = (_, _, ct) => { ct.ThrowIfCancellationRequested(); return Task.FromResult(new LlmResult()); };
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => runner.RunAsync(canceller, Ctx(), "x", cts.Token));
        }
    }
}
