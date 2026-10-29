using System.Globalization;
using System.Windows;

namespace AgentSpeed.TaskScore;

/// <summary>Text helpers of the score card's drawn elements.</summary>
internal static class TaskCardText
{
    /// <summary>The culture of the element's language (the app's, set on the card window), for line breaking and fonts.</summary>
    public static CultureInfo Culture(FrameworkElement element)
    {
        ArgumentNullException.ThrowIfNull(element);
        try
        {
            return element.Language.GetSpecificCulture();
        }
        catch (InvalidOperationException)
        {
            return CultureInfo.CurrentUICulture;
        }
    }
}
