using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using FFXProjectEditor.Core.LLM;
using Xunit;

namespace FFXProjectEditor.Tests.Core
{
    /// <summary>
    /// Agent-mode tool surface (Jarvis-UI 2026-09-15): PathGuard confinement,
    /// budgets por turno, describe estruturado e o gate de submit_proposal
    /// (LlmGuard real — aceite exige receita provada + hash real + adapter).
    /// Zero rede: executores rodam direto sobre um workspace temporário.
    /// </summary>
    public class AgentToolingTests : IDisposable
    {
        readonly string _root = Path.Combine(Path.GetTempPath(), "agenttools-" + Guid.NewGuid().ToString("N"));
        readonly AgentToolRegistry _registry = new();

        public AgentToolingTests()
        {
            Directory.CreateDirectory(_root);
            Directory.CreateDirectory(Path.Combine(_root, "sub"));
            File.WriteAllBytes(Path.Combine(_root, "a.bin"), Enumerable.Range(0, 256).Select(i => (byte)i).ToArray());
            File.WriteAllText(Path.Combine(_root, "sub", "note.txt"), "hello spira");
        }

        public void Dispose() { try { Directory.Delete(_root, true); } catch { } }

        AgentToolContext Ctx() => new()
        {
            WorkspaceRoot = _root,
            OutputRoot = Path.Combine(_root, "out"),
            Settings = new LlmSessionSettings
            {
                IsEnabled = true,
                Provider = "test",
                Endpoint = new Uri("http://127.0.0.1:1"),
                ModelId = "m",
                AllowedDataKinds = new[] { LlmDataKind.DiffContext },
                CapabilityFlags = LlmCapabilityFlag.ByteReplace,
                MaxResponseBytes = 4096,
                MaxProposalBytes = 2048,
                RequestTimeout = TimeSpan.FromSeconds(10),
            },
        };

        static LlmToolCall Call(string name, string args) =>
            new() { Id = "t1", Name = name, ArgumentsJson = args };

        // ── confinamento ──────────────────────────────────────────────────────

        [Fact]
        public void ReadBytes_Traversal_Blocked()
        {
            string r = _registry.Execute(Call("read_bytes",
                "{\"path\":\"../../etc/passwd\",\"offset\":0,\"length\":16}"), Ctx());
            Assert.StartsWith("error: path blocked", r);
        }

        [Fact]
        public void ListDir_AbsolutePath_Blocked()
        {
            string r = _registry.Execute(Call("list_dir", "{\"path\":\"/etc\"}"), Ctx());
            Assert.StartsWith("error: path blocked", r);
        }

        // ── leituras felizes ──────────────────────────────────────────────────

        [Fact]
        public void ReadBytes_ReturnsHexAndSize()
        {
            string r = _registry.Execute(Call("read_bytes",
                "{\"path\":\"a.bin\",\"offset\":0,\"length\":16}"), Ctx());
            Assert.Contains("size 256", r);
            Assert.Contains("000102030405060708090a0b0c0d0e0f", r);
        }

        [Fact]
        public void ReadBytes_ClampsLengthToCap()
        {
            string r = _registry.Execute(Call("read_bytes",
                "{\"path\":\"a.bin\",\"offset\":0,\"length\":8192}"), Ctx());
            Assert.Contains("size 256", r); // leu até o fim do arquivo, não estoura
        }

        [Fact]
        public void Sha256File_ReturnsRealHash()
        {
            string expected = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            string r = _registry.Execute(Call("sha256_file", "{\"path\":\"a.bin\"}"), Ctx());
            Assert.Contains(expected, r);
        }

        [Fact]
        public void FindFiles_MatchesByName()
        {
            string r = _registry.Execute(Call("find_files", "{\"pattern\":\"note\"}"), Ctx());
            Assert.Contains("note.txt", r);
            Assert.DoesNotContain("a.bin", r);
        }

        [Fact]
        public void ListDir_ShowsEntries()
        {
            string r = _registry.Execute(Call("list_dir", "{}"), Ctx());
            Assert.Contains("a.bin", r);
            Assert.Contains("sub/", r);
        }

        // ── describe ──────────────────────────────────────────────────────────

        [Fact]
        public void DescribeFile_UnknownFormat_FallsBackToIdentity()
        {
            string r = _registry.Execute(Call("describe_file", "{\"path\":\"a.bin\"}"), Ctx());
            using var doc = JsonDocument.Parse(r);
            Assert.Equal("unknown", doc.RootElement.GetProperty("format").GetString());
            Assert.Equal(256, doc.RootElement.GetProperty("size").GetInt64());
            Assert.True(doc.RootElement.GetProperty("sha256").GetString()!.Length == 64);
        }

        [Fact]
        public void DescribeFile_Monster_ExposesMeasuredStatOffsets()
        {
            // Fixture real (vanilla m000.bin) — copia pro workspace temporário.
            string fixture = Path.Combine(AppContext.BaseDirectory, "Fixtures", "Monster", "m000.bin");
            Assert.True(File.Exists(fixture), "monster fixture missing");
            File.Copy(fixture, Path.Combine(_root, "m000.bin"));
            byte[] fileBytes = File.ReadAllBytes(Path.Combine(_root, "m000.bin"));

            string r = _registry.Execute(Call("describe_file", "{\"path\":\"m000.bin\"}"), Ctx());
            using var doc = JsonDocument.Parse(r);
            var rootEl = doc.RootElement;

            Assert.Equal("monster", rootEl.GetProperty("format").GetString());
            Assert.Equal(fileBytes.Length, rootEl.GetProperty("size").GetInt64());

            // patchContract: a receita executável + o shape exato do proposal.
            var contract = rootEl.GetProperty("patchContract");
            Assert.Equal("byte-patch-t1", contract.GetProperty("recipeId").GetString());
            Assert.Equal("byte-patch", contract.GetProperty("capabilityId").GetString());

            // statOffsets: cada campo reporta fileOffset+width MEDIDOS — self-check:
            // os bytes reais do arquivo naquele offset decodificam pro valor reportado.
            var offsets = rootEl.GetProperty("statOffsets").EnumerateArray()
                .ToDictionary(e => e.GetProperty("name").GetString()!);
            Assert.Equal(12, offsets.Count);

            foreach (var (name, el) in offsets)
            {
                int off = (int)el.GetProperty("fileOffset").GetInt64();
                int width = el.GetProperty("width").GetInt32();
                long value = el.GetProperty("value").GetInt64();
                Assert.InRange(off, 0, fileBytes.Length - width);
                long actual = width switch
                {
                    4 => BitConverter.ToUInt32(fileBytes, off),
                    2 => BitConverter.ToUInt16(fileBytes, off),
                    _ => fileBytes[off],
                };
                Assert.Equal(value, actual); // offset medido bate com o parse real
            }

            // HP (u32) e poisonDamage (u8) — larguras do layout real.
            Assert.Equal(4, offsets["hp"].GetProperty("width").GetInt32());
            Assert.Equal(1, offsets["poisonDamage"].GetProperty("width").GetInt32());
        }

        // ── submit_proposal (gate real) ───────────────────────────────────────

        static string ProposalJson(string relPath, string beforeHash) => $$"""
            {
              "ProposalId": "p-test-1",
              "CapabilityId": "monster-file",
              "RecipeId": "monster-file-t1",
              "Operation": 0,
              "Target": {
                "RelativePath": "{{relPath}}",
                "FileVersion": "pc-hd",
                "Offset": 0,
                "Length": 2,
                "NewBytesBase64": "AAA="
              },
              "BeforeHash": "{{beforeHash}}",
              "AfterHash": "0000000000000000000000000000000000000000000000000000000000000000",
              "SemanticChange": "test",
              "Justification": "test",
              "Diff": { "BeforeBytesHex": "0001", "AfterBytesHex": "0000", "ChangedFields": ["x"], "HumanSummary": "test" },
              "Verifications": [ { "Kind": "hash-after", "Required": true } ],
              "Provider": "test", "ModelId": "m", "PromptTemplateVersion": "v1",
              "CreatedAt": "2026-09-15T00:00:00Z"
            }
            """;

        [Fact]
        public void SubmitProposal_HashMismatch_RejectedWithReason()
        {
            string bad = new string('0', 64);
            string r = _registry.Execute(Call("submit_proposal",
                "{\"proposal\":" + ProposalJson("a.bin", bad) + "}"), Ctx());
            Assert.StartsWith("rejected:", r);
            Assert.Contains("BeforeHashMismatch", r);
        }

        [Fact]
        public void SubmitProposal_UnknownRecipe_Rejected()
        {
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            string proposal = ProposalJson("a.bin", hash).Replace("monster-file-t1", "recipe-nao-existe");
            string r = _registry.Execute(Call("submit_proposal", "{\"proposal\":" + proposal + "}"), Ctx());
            Assert.StartsWith("rejected:", r);
            Assert.Contains("UnknownRecipe", r);
        }

        [Fact]
        public void SubmitProposal_Valid_AcceptedAndRaisesEvent()
        {
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            var ctx = Ctx();
            PatchProposal? got = null;
            ctx.ProposalAccepted += p => got = p;

            string r = _registry.Execute(Call("submit_proposal",
                "{\"proposal\":" + ProposalJson("a.bin", hash) + "}"), ctx);

            Assert.StartsWith("accepted:", r);
            Assert.NotNull(got);
            Assert.Equal("monster-file-t1", got!.RecipeId);
        }

        [Fact]
        public void SubmitProposal_InjectionField_RejectedByStrictSchema()
        {
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            string proposal = ProposalJson("a.bin", hash)
                .Replace("\"ProposalId\"", "\"evil\":\"x\",\"ProposalId\"");
            string r = _registry.Execute(Call("submit_proposal", "{\"proposal\":" + proposal + "}"), Ctx());
            Assert.StartsWith("error:", r); // strict schema rejeita campo desconhecido
        }

        [Fact]
        public void SubmitProposal_AfterHash_ComputedLocally()
        {
            // O modelo não consegue prever SHA-256 — o tool aplica o ByteReplace em
            // memória e fixa o hash real (vira verificação verdadeira no executor).
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            var ctx = Ctx();
            PatchProposal? got = null;
            ctx.ProposalAccepted += p => got = p;

            // payload "AAA=" = 00 00 nos offsets 0..1 (originais 00 01 → muda).
            string r = _registry.Execute(Call("submit_proposal",
                "{\"proposal\":" + ProposalJson("a.bin", hash) + "}"), ctx);

            Assert.StartsWith("accepted:", r);
            Assert.NotNull(got);
            // Esperado: cópia de a.bin com [0..1] = 00 00.
            byte[] expected = File.ReadAllBytes(Path.Combine(_root, "a.bin"));
            expected[0] = 0; expected[1] = 0;
            string expectedAfter = Convert.ToHexString(SHA256.HashData(expected)).ToLowerInvariant();
            Assert.Equal(expectedAfter, got!.AfterHash);
            Assert.NotEqual(new string('0', 64), got.AfterHash); // o placeholder foi sobrescrito
        }

        [Fact]
        public void SubmitProposal_OutOfBoundsOffset_Rejected()
        {
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            string proposal = ProposalJson("a.bin", hash)
                .Replace("\"Offset\": 0,", "\"Offset\": 200,")
                .Replace("\"Length\": 2,", "\"Length\": 100,")
                .Replace("AAA=", new string('A', 134) + "=="); // 136 chars base64 = 100 bytes
            string r = _registry.Execute(Call("submit_proposal", "{\"proposal\":" + proposal + "}"), Ctx());
            Assert.StartsWith("rejected:", r);
            Assert.Contains("out of bounds", r);
        }

        [Fact]
        public void SubmitProposal_NoOpPayload_Rejected()
        {
            // payload idêntico aos bytes atuais (00 01 nos offsets 0..1) → no-op honesto.
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            string proposal = ProposalJson("a.bin", hash).Replace("AAA=", "AAE="); // 00 01
            string r = _registry.Execute(Call("submit_proposal", "{\"proposal\":" + proposal + "}"), Ctx());
            Assert.StartsWith("rejected:", r);
            Assert.Contains("no-op", r);
        }

        [Fact]
        public void SubmitProposal_MissingEnvelopeMetadata_FilledFromSession()
        {
            // provider/modelId/createdAt/proposalId são fatos da sessão — o modelo não
            // tem como saber; o tool preenche antes do strict-parse (Jarvis-UI).
            string hash = Convert.ToHexString(
                SHA256.HashData(File.ReadAllBytes(Path.Combine(_root, "a.bin")))).ToLowerInvariant();
            var node = System.Text.Json.Nodes.JsonNode.Parse(
                ProposalJson("a.bin", hash))!.AsObject();
            foreach (string k in new[] { "ProposalId", "Provider", "ModelId", "PromptTemplateVersion", "CreatedAt" })
                node.Remove(k);
            string proposal = node.ToJsonString();
            var ctx = Ctx();
            PatchProposal? got = null;
            ctx.ProposalAccepted += p => got = p;

            string r = _registry.Execute(Call("submit_proposal",
                "{\"proposal\":" + proposal + "}"), ctx);

            Assert.StartsWith("accepted:", r);
            Assert.NotNull(got);
            Assert.Equal("test", got!.Provider);          // preenchido pela sessão
            Assert.Equal("agent-v1", got.PromptTemplateVersion);
            Assert.StartsWith("p-", got.ProposalId);      // id gerado
        }

        // ── budgets ───────────────────────────────────────────────────────────

        [Fact]
        public void ToolCallBudget_Enforced()
        {
            var ctx = Ctx();
            for (int i = 0; i < AgentToolContext.MaxToolCallsPerTurn; i++)
                _registry.Execute(Call("list_dir", "{}"), ctx);
            string r = _registry.Execute(Call("list_dir", "{}"), ctx);
            Assert.StartsWith("error: tool-call budget", r);
        }

        [Fact]
        public void ListRecipes_ShowsExecutableFlags()
        {
            string r = _registry.Execute(Call("list_recipes", "{}"), Ctx());
            using var doc = JsonDocument.Parse(r);
            var rows = doc.RootElement.EnumerateArray().ToList();
            Assert.True(rows.Count >= 5);
            var monsterFile = rows.First(x => x.GetProperty("recipeId").GetString() == "monster-file-t1");
            Assert.True(monsterFile.GetProperty("executable").GetBoolean());
            var ppp = rows.First(x => x.GetProperty("recipeId").GetString() == "ppp-sclmove-t3");
            Assert.False(ppp.GetProperty("executable").GetBoolean()); // sem adapter → honesto
        }
    }
}
