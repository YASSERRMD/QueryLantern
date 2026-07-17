namespace QueryLantern.Services;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using QueryLantern.Adapters;

/// <summary>
/// Grounding / hallucination guard. Verifies that every numeric figure in a natural-language answer
/// can be traced back to a value that actually appears in the returned result sets. Flags any figure
/// that cannot be found in the data so the UI can warn the user.
/// </summary>
public sealed class AnswerGroundingService
{
    private static readonly Regex FigureRegex =
        new(@"-?\d{1,3}(?:[., ]\d{3})*(?:\.\d+)?", RegexOptions.Compiled);

    public GroundingResult Check(string answer, IReadOnlyList<QueryResult> results, IReadOnlyList<QueryResult>? stepResults = null)
    {
        var all = results.ToList();
        if (stepResults is not null)
        {
            all.AddRange(stepResults);
        }

        var present = new HashSet<string>(CollectValues(all), StringComparer.Ordinal);
        var claims = new List<ClaimCheck>();
        foreach (var fig in FigureRegex.Matches(answer).Select(m => m.Value))
        {
            var normalized = Normalize(fig);
            if (string.IsNullOrEmpty(normalized) || !present.Contains(normalized))
            {
                claims.Add(new ClaimCheck(fig, false));
            }
            else
            {
                claims.Add(new ClaimCheck(fig, true));
            }
        }

        return new GroundingResult(claims, false, false);
    }

    public IReadOnlyList<string> UntracedFigures(string answer, IReadOnlyList<QueryResult> results)
    {
        return Check(answer, results).Claims.Where(c => !c.Traced).Select(c => c.Figure).ToList();
    }

    private static IEnumerable<string> CollectValues(IReadOnlyList<QueryResult> results)
    {
        foreach (var r in results)
        {
            if (r?.Rows is null)
            {
                continue;
            }

            foreach (var row in r.Rows)
            {
                if (row is null)
                {
                    continue;
                }

                foreach (var cell in row)
                {
                    if (cell is null)
                    {
                        continue;
                    }

                    var text = cell switch
                    {
                        System.IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
                        _ => cell.ToString()
                    };
                    yield return Normalize(text ?? string.Empty);
                }
            }
        }
    }

    private static string Normalize(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return new string(value.Where(c => char.IsDigit(c) || c == '.').ToArray());
    }
}
