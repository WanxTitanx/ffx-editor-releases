// PhaseTurnEdgeLab — monitor de runtime para o PhaseTurnEdgeHook.
//
// Lê o log do ffx-hooks em tempo real, filtra linhas "PhaseTurnEdge",
// e exibe a cadência dos eventos de borda de turno CTB.
//
// Uso:
//   1. Coloque phase_turn_edge.flag na pasta do jogo (ou set FFXHOOKS_ENABLE_PHASE_TURN_EDGE=1)
//   2. Inicie o jogo com ffx-hooks.dll carregada
//   3. Execute este lab: dotnet run
//
// O lab fica monitorando o arquivo de log e exibe cada evento PhaseTurnEdge
// com seu número de sequência e flag de batalha ativa.

using System.Text.RegularExpressions;

string logPath = Path.Combine(
    Environment.GetEnvironmentVariable("TEMP") ?? @"C:\Windows\Temp",
    "ffx-hooks.log");

Console.WriteLine($"=== PhaseTurnEdgeLab — Monitor do CTB Turn Edge ===");
Console.WriteLine($"Log alvo: {logPath}");
Console.WriteLine($"Aguardando eventos PhaseTurnEdge... (Ctrl+C para sair)");
Console.WriteLine();

if (!File.Exists(logPath))
{
    Console.Error.WriteLine($"Log não encontrado: {logPath}");
    Console.Error.WriteLine("Certifique-se de que o jogo está rodando com ffx-hooks.dll e phase_turn_edge.flag ativo.");
    return 1;
}

var phaseTurnPattern = new Regex(
    @"PhaseTurnEdge #(\d+) battleActive=(\d+) n6=(\d+) a2=0x([0-9A-Fa-f]+)",
    RegexOptions.Compiled);

int totalEdges = 0;
DateTime? firstEdge = null;
DateTime? lastEdge = null;

try
{
    using var fs = new FileStream(logPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);

    // Seek to end to only read new lines
    fs.Seek(0, SeekOrigin.End);

    using var sr = new StreamReader(fs);

    while (true)
    {
        string? line = sr.ReadLine();
        if (line != null)
        {
            var match = phaseTurnPattern.Match(line);
            if (match.Success)
            {
                totalEdges++;
                DateTime now = DateTime.Now;
                firstEdge ??= now;
                lastEdge = now;

                long seq = long.Parse(match.Groups[1].Value);
                uint battleActive = uint.Parse(match.Groups[2].Value);
                uint actorSlot = uint.Parse(match.Groups[3].Value);
                string actorPtr = "0x" + match.Groups[4].Value.ToUpperInvariant();

                string cadence = "";
                if (totalEdges > 1 && lastEdge.HasValue && firstEdge.HasValue)
                {
                    double elapsed = (now - firstEdge.Value).TotalSeconds;
                    double avg = elapsed / (totalEdges - 1);
                    cadence = $" | cadência média: {avg:F2}s";
                }

                Console.WriteLine(
                    "[{0:HH:mm:ss.fff}] PhaseTurnEdge #{1} battleActive={2} actorSlot={3} actorPtr={4} total={5}{6}",
                    now, seq, battleActive, actorSlot, actorPtr, totalEdges, cadence);
            }
        }
        else
        {
            Thread.Sleep(100);
        }
    }
}
catch (Exception ex)
{
    Console.Error.WriteLine($"Erro: {ex.Message}");
    return 2;
}
