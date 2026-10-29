using System.Text;
using System.Text.Json;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tests;

/// <summary>
/// The recognisers' literal gates (<see cref="FailureRecognizer.CannotClaim"/>) never change a result: every output of
/// <c>task-parser-cases.json</c>, and outputs recombined from their lines with every command, give the same fingerprints,
/// counts, framework and canonical strings with and without the gates.
/// </summary>
public sealed class FailureGateTests
{
    [Fact]
    public void GatesNeverChangeAResult()
    {
        var root = JsonDocument.Parse(File.ReadAllBytes(FixtureLocator.PathOf("task-parser-cases.json"))).RootElement;
        var outputs = new List<string>();
        var commands = new HashSet<string>(StringComparer.Ordinal) { "" };
        Collect(root, outputs, commands);
        Assert.True(outputs.Count > 100, "fixture outputs found: " + outputs.Count);

        var lines = outputs.SelectMany(o => o.Split('\n')).Where(l => l.Length > 0).Distinct(StringComparer.Ordinal).ToArray();
        var random = new Random(20261028);
        var inputs = new List<string>(outputs);
        for (var k = 0; k < 3000; k++)
        {
            var builder = new StringBuilder();
            var start = random.Next(lines.Length);
            for (var n = random.Next(1, 40); n > 0; n--)
            {
                // Mostly runs of neighbouring lines (they keep multi-line contexts together), sometimes any line.
                var line = random.Next(4) == 0 ? lines[random.Next(lines.Length)] : lines[start++ % lines.Length];
                builder.Append(line).Append('\n');
            }

            inputs.Add(builder.ToString());
        }

        var infos = commands.Select(c => ShellCommands.Info(c, "/w")).ToArray();
        var differences = new List<string>();
        for (var k = 0; k < inputs.Count; k++)
        {
            var info = infos[k % infos.Length];
            bool? failed = (k % 3) switch { 0 => true, 1 => false, _ => null };
            var gated = Read(info, inputs[k], failed, gates: true);
            var plain = Read(info, inputs[k], failed, gates: false);
            if (gated != plain)
            {
                differences.Add($"#{k}: {plain}\n   gated: {gated}");
            }
        }

        Assert.True(differences.Count == 0, string.Join("\n", differences.Take(5)));
    }

    private static string Read(CommandInfo info, string output, bool? failed, bool gates)
    {
        FailureRecognizer.Gates = gates;
        try
        {
            var (report, canonical) = FailureParser.Detail(info, output, "/w", failed);
            return $"{string.Join(',', report.Fingerprints)} {report.FailedCount} {report.PassedCount} {report.Framework} {string.Join('|', canonical)}";
        }
        finally
        {
            FailureRecognizer.Gates = true;
        }
    }

    private static void Collect(JsonElement value, List<string> outputs, HashSet<string> commands)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var member in value.EnumerateObject())
                {
                    if (member.Name == "output" && member.Value.ValueKind == JsonValueKind.String)
                    {
                        outputs.Add(member.Value.GetString()!);
                    }
                    else if (member.Name == "command" && member.Value.ValueKind == JsonValueKind.String)
                    {
                        commands.Add(member.Value.GetString()!);
                    }
                    else
                    {
                        Collect(member.Value, outputs, commands);
                    }
                }

                break;
            case JsonValueKind.Array:
                foreach (var item in value.EnumerateArray())
                {
                    Collect(item, outputs, commands);
                }

                break;
        }
    }
}
