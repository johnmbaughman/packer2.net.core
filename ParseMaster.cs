using System.Collections;
using System.Collections.Specialized;
using System.Text.RegularExpressions;
using System.Text;

namespace TestParserCore;

/// <summary>
/// a multi-pattern parser
/// </summary>
public class ParseMaster
{
    // used to determine nesting levels
    private readonly Regex _groups = new("\\(");

    private readonly Regex _subReplace = new("\\$");

    private readonly Regex _indexed = new("^\\$\\d+$");

    private readonly Regex _escape = new("\\\\.");

    private Regex _quote = new("'");

    private readonly Regex _deleted = new("\\x01[^\\x01]*\\x01");

    /// <summary>
    /// Delegate to call when a regular expression is found.
    /// Use match.Groups[offset + &lt;group number&gt;].Value to get
    /// the correct subexpression
    /// </summary>
    public delegate string MatchGroupEvaluator(Match match, int offset);

    private static string Delete(Match match, int offset)
    {
        return "\x01" + match.Groups[offset].Value + "\x01";
    }

    /// <summary>
    /// Ignore Case?
    /// </summary>
    public bool IgnoreCase { get; set; } = false;

    /// <summary>
    /// Escape Character to use
    /// </summary>
    public char EscapeChar { get; set; } = '\0';

    /// <summary>
    /// Add an expression to be deleted
    /// </summary>
    /// <param name="expression">Regular Expression String</param>
    public void Add(string expression)
    {
        Add(expression, string.Empty);
    }

    /// <summary>
    /// Add an expression to be replaced with the replacement string
    /// </summary>
    /// <param name="expression">Regular Expression String</param>
    /// <param name="replacement">Replacement String. Use $1, $2, etc. for groups</param>
    public void Add(string expression, string replacement)
    {
        if (replacement == string.Empty)
            Add(expression, (object)new MatchGroupEvaluator(Delete));

        Add(expression, (object)replacement);
    }

    /// <summary>
    /// Add an expression to be replaced using a callback function
    /// </summary>
    /// <param name="expression">Regular expression string</param>
    /// <param name="replacement">Callback function</param>
    public void Add(string expression, MatchGroupEvaluator replacement)
    {
        Add(expression, (object)replacement);
    }

    /// <summary>
    /// Executes the parser
    /// </summary>
    /// <param name="input">input string</param>
    /// <returns>parsed string</returns>
    public string Exec(string input)
    {
        return _deleted.Replace(Unescape(GetPatterns().Replace(Escape(input), Replacement)), string.Empty);
        //long way for debugging
        /*input = escape(input);
        Regex patterns = getPatterns();
        input = patterns.Replace(input, new MatchEvaluator(replacement));
        input = DELETED.Replace(input, string.Empty);
        return input;*/
    }

    private readonly ArrayList _patterns = new();
    private void Add(string expression, object replacement)
    {
        var pattern = new Pattern
        {
            Expression = expression,
            Replacement = replacement,
            // - add 1 because each group is itself a sub-expression
            //count the number of sub-expressions
            Length = _groups.Matches(InternalEscape(expression)).Count + 1
        };

        //does the pattern deal with sup-expressions?
        if (replacement is string && _subReplace.IsMatch((string)replacement))
        {
            var sreplacement = (string)replacement;
            // a simple lookup (e.g. $2)
            if (_indexed.IsMatch(sreplacement))
            {
                pattern.Replacement = int.Parse(sreplacement.Substring(1)) - 1;
            }
        }

        _patterns.Add(pattern);
    }

    /// <summary>
    /// builds the patterns into a single regular expression
    /// </summary>
    /// <returns></returns>
    private Regex GetPatterns()
    {
        var rtrn = new StringBuilder(string.Empty);
        foreach (var pattern in _patterns)
        {
            rtrn.Append(((Pattern)pattern).ToString() + "|");
        }
        rtrn.Remove(rtrn.Length - 1, 1);
        return new Regex(rtrn.ToString(), IgnoreCase ? RegexOptions.IgnoreCase : RegexOptions.None);
    }

    /// <summary>
    /// Global replacement function. Called once for each match found
    /// </summary>
    /// <param name="match">Match found</param>
    private string Replacement(Match match)
    {
        int i = 1, j = 0;
        //loop through the patterns
        while ((Pattern)_patterns[j++] is { } pattern)
        {
            //do we have a result?
            if (match.Groups[i].Value != string.Empty)
            {
                var replacement = pattern.Replacement;
                return replacement switch
                {
                    MatchGroupEvaluator evaluator => evaluator(match, i),
                    int replacement1 => match.Groups[replacement1 + i].Value,
                    _ => ReplacementString(match, i, (string)replacement, pattern.Length)
                };
            }
            else //skip over references to sub-expressions
                i += pattern.Length;
        }
        return match.Value; //should never be hit, but you never know
    }

    /// <summary>
    /// Replacement function for complicated lookups (e.g. Hello $3 $2)
    /// </summary>
    private static string ReplacementString(Match match, int offset, string replacement, int length)
    {
        while (length > 0)
        {
            replacement = replacement.Replace("$" + length--, match.Groups[offset + length].Value);
        }
        return replacement;
    }

    private readonly StringCollection _escaped = new();

    //encode escaped characters
    private string Escape(string str)
    {
        if (EscapeChar == '\0')
            return str;
        var escaping = new Regex("\\\\(.)");
        return escaping.Replace(str, EscapeMatch);
    }

    private string EscapeMatch(Match match)
    {
        _escaped.Add(match.Groups[1].Value);
        return "\\";
    }

    //decode escaped characters
    private int _unescapeIndex = 0;
    private string Unescape(string str)
    {
        if (EscapeChar == '\0')
            return str;
        var unescaping = new Regex("\\" + EscapeChar);
        return unescaping.Replace(str, UnescapeMatch);
    }

    private string UnescapeMatch(Match match)
    {
        return "\\" + _escaped[_unescapeIndex++];
    }

    private string InternalEscape(string str)
    {
        return _escape.Replace(str, "");
    }

    //subclass for each pattern
    private class Pattern
    {
        public string Expression;
        public object Replacement;
        public int Length;

        public override string ToString()
        {
            return "(" + Expression + ")";
        }
    }
}