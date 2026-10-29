using System.Collections.Frozen;
using System.Text;
using AgentSpeed.Core.TaskParsers;

namespace AgentSpeed.Core.Tasks;

/// <summary>The continuation list (METRICS 6.9 <i>Continuation</i>): a prompt in it joins the current task.</summary>
public static class Continuations
{
    private const string Trailing = ".!~,。、";

    private static readonly FrozenSet<string> List = FrozenSet.Create(
        StringComparer.Ordinal,
        "continue", "continue please", "please continue", "go on", "keep going", "proceed", "please proceed",
        "carry on", "go ahead", "resume", "ok", "okay", "ok continue", "ok go ahead", "yes", "y", "yep", "yes please",
        "sure", "do it", "lgtm",
        "繼續", "繼續吧", "請繼續", "繼續做", "接著做", "好", "好的", "好啊", "可以", "嗯", "是", "對", "行", "沒問題",
        "继续", "继续吧", "请继续", "继续做", "接着做", "对", "没问题",
        "続けて", "続けてください", "続行", "進めて", "進めてください", "はい", "お願いします", "うん", "いいよ", "どうぞ");

    /// <summary><c>is_continuation</c>: the normalised text is in the list. Null is never a continuation.</summary>
    public static bool IsContinuation(string? text) => text is not null && List.Contains(Normalize(text));

    /// <summary>
    /// <c>normalize_prompt</c>: NFKC, trimmed, trailing <c>.!~,。、</c> and whitespace removed, ASCII letters lower-cased,
    /// whitespace runs collapsed to one space. Unpaired surrogates read as U+FFFD (they can match no entry either way).
    /// </summary>
    public static string Normalize(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        var s = PyText.Trim(TaskText.Scalars(text).Normalize(NormalizationForm.FormKC));
        while (s.Length > 0 && (Trailing.Contains(s[^1], StringComparison.Ordinal) || PyText.IsSpace(s[^1])))
        {
            s = s[..^1];
        }

        var builder = new StringBuilder(s.Length);
        var pendingSpace = false;
        foreach (var c in s)
        {
            if (PyText.IsSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(c is >= 'A' and <= 'Z' ? (char)(c + 32) : c);
        }

        return builder.ToString();
    }
}
