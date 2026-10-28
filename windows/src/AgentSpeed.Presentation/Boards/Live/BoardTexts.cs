using System.Globalization;
using AgentSpeed.Core;
using AgentSpeed.Core.Intelligence;
using AgentSpeed.Core.Money;
using AgentSpeed.Presentation.Cost;
using AgentSpeed.Presentation.Formatting;
using AgentSpeed.Presentation.Heat;
using AgentSpeed.Presentation.History;
using AgentSpeed.Presentation.Localization;
using AgentSpeed.Presentation.Settings.Orbs;
using AgentSpeed.Presentation.Titles;

namespace AgentSpeed.Presentation.Boards.Live;

/// <summary>
/// The words a board's tick carries (DESIGN 2c Content): the speed title in the General title theme, the footer's
/// <c>boards.today</c> and <c>boards.peak</c>, today's cost of the board's sources (DESIGN 9), and the live metrics of the hero
/// and of each row (DESIGN 2c, Live metrics). Without a cost display, costs read in US dollars and every source counts.
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

    /// <summary>The sources whose costs show (METRICS 9.4).</summary>
    public SourceSet Billable => Cost?.Billable ?? SourceSet.Everything;

    /// <summary>
    /// The hero's 智力, 思考 and 費用 (DESIGN 2c, Live metrics: Values): the index rounded half away from zero, METRICS 7 of the
    /// thinking rate, both <c>–</c> while quiet; the cost of the billable sources in the section 9 rate form, <c>≥</c> when partly
    /// priced, <c>–</c> when nothing is, the subscription tag when every source is excluded, a real 0 otherwise.
    /// </summary>
    public BoardHeroReadouts Hero(IntelligenceSums sums, CostReading cost)
    {
        ArgumentNullException.ThrowIfNull(cost);
        var money = cost.State switch
        {
            CostState.SubscriptionOnly => Tag(),
            CostState.NothingPriced => Unknown(),
            _ => Money(cost.MicroUsdPerSecond, cost.State == CostState.Partial),
        };

        if (sums.Shown is not { } shown)
        {
            return new BoardHeroReadouts(BoardHeroReadouts.Dash, null, BoardHeroReadouts.Dash, true, false, money);
        }

        var thinking = sums.LiveThinking;
        return new BoardHeroReadouts(shown.ToString(CultureInfo.InvariantCulture), shown, SpeedFormat.Speed(thinking), false,
            ThinkingZeroHold.ReadsZero(thinking), money);
    }

    /// <summary>
    /// A row's 智力 cell and, for a live group row at M or L with its second line, its cost; with the tooltip lines of a group
    /// row for the switched-on readouts (DESIGN 2c, Ranking rows and Tooltip).
    /// </summary>
    public BoardRowReadout? Row(BoardEntry entry, BoardReadouts readouts, double live)
    {
        ArgumentNullException.ThrowIfNull(entry);
        if (entry.Kind is not (BoardEntryKind.Group or BoardEntryKind.Sub))
        {
            return null;
        }

        var group = entry.Kind == BoardEntryKind.Group;
        var agent = group ? entry.Group.Root : entry.Agent;
        var intelligence = group ? entry.Group.Intelligence : agent?.Intelligence ?? AgentIntelligence.None;
        var text = intelligence.Shown is { } shown ? shown.ToString(CultureInfo.InvariantCulture) : BoardHeroReadouts.Dash;
        // The 智力 words follow the speed only while the column shows.
        var spoken = !readouts.Intelligence ? string.Empty : intelligence.Form switch
        {
            AgentIntelligenceForm.Live => Localizer.Format("a11y.agent.intel", text),
            AgentIntelligenceForm.Resting => Localizer.Format("a11y.agent.grade", text),
            _ => string.Empty,
        };

        var busy = live > 0.05;
        var cost = group && busy ? GroupMoney(entry.Group.Cost) : null;
        var lines = new List<string>(4);
        if (group && readouts.Intelligence)
        {
            if (intelligence.Form == AgentIntelligenceForm.Live)
            {
                var share = intelligence.Sums.ThinkingBasis > 0 ? HistoryText.Percent(intelligence.ThinkingShare) : BoardHeroReadouts.Dash;
                lines.Add(Localizer.Format("intel.tooltip", text, share));
            }

            if (entry.Group.Root.Grade is { } grade)
            {
                lines.Add(IntelligenceText.Grade(grade, Localizer));
            }
        }

        if (group && (readouts.Cost || readouts.RowCost) && cost is { } money)
        {
            switch (money.State)
            {
                case BoardMoneyState.Tag:
                    lines.Add(Localizer.Format("boards.tooltip.subscription", OrbNaming.SourceName(entry.Group.Root.Source, Localizer)));
                    break;
                case BoardMoneyState.Unknown:
                    lines.Add(Localizer["cost.unknown"]);
                    break;
                default:
                    lines.Add(Localizer.Format("boards.tooltip.cost", (money.AtLeast ? CostDisplay.AtLeastSign : string.Empty) + money.Number + " " + money.Unit));
                    if (money.AtLeast)
                    {
                        lines.Add(Localizer["cost.partial"]);
                    }

                    break;
            }
        }

        if (group && readouts.Intelligence)
        {
            lines.Add(Localizer["intel.disclaimer"]);
        }

        return new BoardRowReadout(text, intelligence.Form, spoken, cost, lines);
    }

    private BoardMoney GroupMoney(GroupCost cost) => cost.State switch
    {
        CostState.SubscriptionOnly => Tag(),
        CostState.NothingPriced => Unknown(),
        _ => Money(cost.MicroUsdPerSecond, cost.State == CostState.Partial),
    };

    private BoardMoney Money(double microUsdPerSecond, bool partial)
    {
        var formatted = Cost is { } display ? display.RateMoney(microUsdPerSecond)
            : MoneyFormat.Format(MoneyFormat.Rate(microUsdPerSecond, 1m), Usd, Localizer.LanguageCode, MoneyForm.Rate);
        var number = formatted.Number;
        var zero = number == "0";
        var atLeast = partial && !zero;
        var spoken = atLeast ? Localizer.Format("a11y.orb.costAtLeast", number) : number;
        return new BoardMoney(number, Localizer.Format("cost.unitPerSecond", formatted.Symbol), atLeast, zero ? BoardMoneyState.Zero : BoardMoneyState.Value,
            spoken, CurrencyName);
    }

    // The unit stays known (the sub-gauge sizes its unit cell with it) though neither form draws it.
    private BoardMoney Unknown() => new(CostDisplay.Dash, UnitText, false, BoardMoneyState.Unknown, Localizer["cost.unknown"], CurrencyName);

    private BoardMoney Tag() => new(Localizer["cost.subscription.tag"], UnitText, false, BoardMoneyState.Tag, Localizer["cost.subscription.tag"],
        CurrencyName);

    private string UnitText => Localizer.Format("cost.unitPerSecond", Cost?.Symbol ?? Usd.SymbolFor(Localizer.LanguageCode));

    private string CurrencyName => Cost?.CurrencyName ?? Localizer["currency." + Currency.UsdCode];

    private static Currency Usd => CurrencyTable.Bundled.CurrencyOrUsd(Currency.UsdCode);

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
