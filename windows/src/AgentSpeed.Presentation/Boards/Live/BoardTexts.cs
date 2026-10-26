using AgentSpeed.Core;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>
/// The words a board's tick carries (DESIGN 2c Content): the speed title in the General title theme, the footer's
/// <c>boards.today</c> and <c>boards.peak</c>, and today's cost of the board's sources (DESIGN 9).
/// </summary>
public sealed class BoardTexts
{
    private readonly TitleTheme _theme;

    public BoardTexts(Localizer localizer, TitleTheme theme, CostDisplay? cost = null)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        ArgumentNullException.ThrowIfNull(theme);
        Localizer = localizer;
        _theme = theme;
        Cost = cost;
    }

    public Localizer Localizer { get; }

    public CostDisplay? Cost { get; }

    public string Title(SpeedTitle title) => _theme.Label(title, Localizer);

    /// <summary>The title as accessibility reads it (not uppercased).</summary>
    public string SpokenTitle(SpeedTitle title) => _theme.Text(title, Localizer);

    public string Today(long output) => Localizer.Format("boards.today", SpeedFormat.Tokens(output));

    public string Peak(double peak) => Localizer.Format("boards.peak", SpeedFormat.Speed(peak));

    /// <summary>
    /// Today's billable cost of the sources (every enabled source for null), as the dashboard writes it (<c>≥</c> when some
    /// responses have no price, <c>–</c> when none has); null without a cost display.
    /// </summary>
    public string? TodayCost(SpeedSnapshot snapshot, SourceSet? sources)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (Cost is not { } cost)
        {
            return null;
        }

        var total = CostTally.Zero;
        long requests = 0;
        foreach (var status in snapshot.Sources)
        {
            if (!status.Enabled || status.Today.Requests <= 0 || !cost.Billable.Contains(status.Kind)
                || (sources is { } set && !set.Contains(status.Kind)))
            {
                continue;
            }

            total += status.TodayCost;
            requests += status.Today.Requests;
        }

        return requests == 0 ? cost.AmountText(0) : cost.TallyText(total, requests);
    }
}
