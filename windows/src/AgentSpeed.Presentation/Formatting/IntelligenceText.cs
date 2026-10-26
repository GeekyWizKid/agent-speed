using AgentSpeed.Core.Intelligence;
using AgentSpeed.Presentation.Localization;

namespace AgentSpeed.Presentation.Formatting;

/// <summary>The live intelligence's wording outside the orb readout (DESIGN 10, Intelligence readout; section 4 tooltip).</summary>
public static class IntelligenceText
{
    /// <summary><c>等級 124 · 旗艦</c>: an agent's grade and class.</summary>
    public static string Grade(ModelGrade grade, Localizer localizer)
    {
        ArgumentNullException.ThrowIfNull(localizer);
        var name = grade.Class switch
        {
            ModelClass.Flagship => localizer["intel.class.flagship"],
            ModelClass.Standard => localizer["intel.class.standard"],
            _ => localizer["intel.class.light"],
        };
        return localizer.Format("intel.grade", grade.Value.ToString(localizer.Culture), name);
    }
}
