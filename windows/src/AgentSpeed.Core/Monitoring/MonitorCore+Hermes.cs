using AgentSpeed.Core.Engine;
using AgentSpeed.Core.Hermes;

namespace AgentSpeed.Core.Monitoring;

internal sealed partial class MonitorCore
{
    private readonly HashSet<string> _hermesAgents = new(StringComparer.Ordinal);

    private IEnumerable<AgentInfo> HermesAgents() => _files.Values.Select(file => file.Parser).OfType<HermesParser>()
        .SelectMany(parser => parser.Agents).GroupBy(agent => agent.Id, StringComparer.Ordinal)
        .Select(group => group.MaxBy(agent => agent.LastLineMs));

    private void PushHermesAgents()
    {
        var current = new HashSet<string>(StringComparer.Ordinal);
        foreach (var agent in HermesAgents())
        {
            current.Add(agent.Id);
            _engine.UpdateAgent(agent);
        }

        foreach (var removed in _hermesAgents.Except(current)) _engine.RemoveAgent(removed);
        _hermesAgents.Clear();
        _hermesAgents.UnionWith(current);
    }
}
