using System.Collections;
using System.Collections.Specialized;
using System.Text;
using System.Text.RegularExpressions;

/*
    packer, version 2.0 (beta) (2005/02/01)
    Copyright 2004-2005, Dean Edwards
    Web: http://dean.edwards.name/

    This software is licensed under the CC-GNU LGPL
    Web: http://creativecommons.org/licenses/LGPL/2.1/
    
    Ported to C# by Jesse Hansen, twindagger2k@msn.com
*/

// http://dean.edwards.name/packer/

namespace TestParserCore;

/// <summary>
/// Packs a javascript file into a smaller area, removing unnecessary characters from the output.
/// </summary>
public class EcmaScriptPacker //: IHttpHandler
{
    /// <summary>
    /// The encoding level to use. See http://dean.edwards.name/packer/usage/ for more info.
    /// </summary>
    public enum PackerEncoding { None = 0, Numeric = 10, Mid = 36, Normal = 62, HighAscii = 95 }

    private const string Ignore = "$1";

    /// <summary>
    /// The encoding level for this instance
    /// </summary>
    public PackerEncoding Encoding { get; set; } = PackerEncoding.Normal;

    /// <summary>
    /// Adds a subroutine to the output to speed up decoding
    /// </summary>
    public bool FastDecode { get; set; } = true;

    /// <summary>
    /// Replaces special characters
    /// </summary>
    public bool SpecialChars { get; set; }

    /// <summary>
    /// Packer enabled
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Constructor
    /// </summary>
    /// <param name="encoding">The encoding level for this instance</param>
    /// <param name="fastDecode">Adds a subroutine to the output to speed up decoding</param>
    /// <param name="specialChars">Replaces special characters</param>
    public EcmaScriptPacker(PackerEncoding encoding, bool fastDecode, bool specialChars)
    {
        Encoding = encoding;
        FastDecode = fastDecode;
        SpecialChars = specialChars;
    }

    /// <summary>
    /// Packs the script
    /// </summary>
    /// <param name="script">the script to pack</param>
    /// <returns>the packed script</returns>
    public string Pack(string script)
    {
        if (!Enabled) return script;
        script += "\n";
        script = BasicCompression(script);
        if (SpecialChars)
            script = EncodeSpecialChars(script);
        if (Encoding != PackerEncoding.None)
            script = EncodeKeywords(script);
        return script;
    }

    //zero encoding - just removal of whitespace and comments
    private string BasicCompression(string script)
    {
        var parser = new ParseMaster
        {
            // make safe
            EscapeChar = '\\'
        };
        // protect strings
        parser.Add("'[^'\\n\\r]*'", Ignore);
        parser.Add("\"[^\"\\n\\r]*\"", Ignore);
        // remove comments
        parser.Add("\\/\\/[^\\n\\r]*[\\n\\r]");
        parser.Add("\\/\\*[^*]*\\*+([^\\/][^*]*\\*+)*\\/");
        // protect regular expressions
        parser.Add("\\s+(\\/[^\\/\\n\\r\\*][^\\/\\n\\r]*\\/g?i?)", "$2");
        parser.Add("[^\\w\\$\\/'\"*)\\?:]\\/[^\\/\\n\\r\\*][^\\/\\n\\r]*\\/g?i?", Ignore);
        // remove: ;;; doSomething();
        if (SpecialChars)
            parser.Add(";;[^\\n\\r]+[\\n\\r]");
        // remove redundant semi-colons
        parser.Add(";+\\s*([};])", "$2");
        // remove white-space
        parser.Add("(\\b|\\$)\\s+(\\b|\\$)", "$2 $3");
        parser.Add("([+\\-])\\s+([+\\-])", "$2 $3");
        parser.Add("\\s+");
        // done
        return parser.Exec(script);
    }

    WordList _encodingLookup;
    private string EncodeSpecialChars(string script)
    {
        var parser = new ParseMaster();
        // replace: $name -> n, $$name -> na
        parser.Add("((\\$+)([a-zA-Z\\$_]+))(\\d*)",
            EncodeLocalVars);

        // replace: _name -> _0, double-underscore (__name) is ignored
        var regex = new Regex("\\b_[A-Za-z\\d]\\w*");

        // build the word list
        _encodingLookup = Analyze(script, regex, EncodePrivate);

        parser.Add("\\b_[A-Za-z\\d]\\w*", EncodeWithLookup);

        script = parser.Exec(script);
        return script;
    }

    private string EncodeKeywords(string script)
    {
        // escape high-ascii values already in the script (i.e. in strings)
        if (Encoding == PackerEncoding.HighAscii) script = Escape95(script);
        // create the parser
        var parser = new ParseMaster();
        var encode = GetEncoder(Encoding);

        // for high-ascii, don't encode single character low-ascii
        var regex = new Regex(
                (Encoding == PackerEncoding.HighAscii) ? "\\w\\w+" : "\\w+"
            );
        // build the word list
        _encodingLookup = Analyze(script, regex, encode);

        // encode
        parser.Add((Encoding == PackerEncoding.HighAscii) ? "\\w\\w+" : "\\w+",
            EncodeWithLookup);

        // if encoded, wrap the script in a decoding function
        return (script == string.Empty) ? "" : BootStrap(parser.Exec(script), _encodingLookup);
    }

    private string BootStrap(string packed, WordList keywords)
    {
        // packed: the packed script
        packed = "'" + Escape(packed) + "'";

        // ascii: base for encoding
        var ascii = Math.Min(keywords.Sorted.Count, (int)Encoding);
        if (ascii == 0)
            ascii = 1;

        // count: number of words contained in the script
        var count = keywords.Sorted.Count;

        // keywords: list of words contained in the script
        foreach (var key in keywords.Protected.Keys)
        {
            keywords.Sorted[(int)key] = "";
        }
        // convert from a string to an array
        var sbKeywords = new StringBuilder("'");
        foreach (var word in keywords.Sorted)
            sbKeywords.Append(word + "|");
        sbKeywords.Remove(sbKeywords.Length - 1, 1);
        var keywordsout = sbKeywords + "'.split('|')";

        string encode;
        var inline = "c";

        switch (Encoding)
        {
            case PackerEncoding.Mid:
                encode = "function(c){return c.toString(36)}";
                inline += ".toString(a)";
                break;
            case PackerEncoding.Normal:
                encode = "function(c){return(c<a?\"\":e(parseInt(c/a)))+" +
                    "((c=c%a)>35?String.fromCharCode(c+29):c.toString(36))}";
                inline += ".toString(a)";
                break;
            case PackerEncoding.HighAscii:
                encode = "function(c){return(c<a?\"\":e(c/a))+" +
                    "String.fromCharCode(c%a+161)}";
                inline += ".toString(a)";
                break;
            default:
                encode = "function(c){return c}";
                break;
        }

        // decode: code snippet to speed up decoding
        var decode = "";
        if (FastDecode)
        {
            decode = "if(!''.replace(/^/,String)){while(c--)d[e(c)]=k[c]||e(c);k=[function(e){return d[e]}];e=function(){return'\\\\w+'};c=1;}";
            if (Encoding == PackerEncoding.HighAscii)
                decode = decode.Replace("\\\\w", "[\\xa1-\\xff]");
            else if (Encoding == PackerEncoding.Numeric)
                decode = decode.Replace("e(c)", inline);
            if (count == 0)
                decode = decode.Replace("c=1", "c=0");
        }

        // boot function
        var unpack = "function(p,a,c,k,e,d){while(c--)if(k[c])p=p.replace(new RegExp('\\\\b'+e(c)+'\\\\b','g'),k[c]);return p;}";
        Regex r;
        if (FastDecode)
        {
            //insert the decoder
            r = new Regex("\\{");
            unpack = r.Replace(unpack, "{" + decode + ";", 1);
        }

        if (Encoding == PackerEncoding.HighAscii)
        {
            // get rid of the word-boundries for regexp matches
            r = new Regex("'\\\\\\\\b'\\s*\\+|\\+\\s*'\\\\\\\\b'");
            unpack = r.Replace(unpack, "");
        }
        if (Encoding == PackerEncoding.HighAscii || ascii > (int)PackerEncoding.Normal || FastDecode)
        {
            // insert the encode function
            r = new Regex("\\{");
            unpack = r.Replace(unpack, "{e=" + encode + ";", 1);
        }
        else
        {
            r = new Regex("e\\(c\\)");
            unpack = r.Replace(unpack, inline);
        }
        // no need to pack the boot function since i've already done it
        var @params = "" + packed + "," + ascii + "," + count + "," + keywordsout;
        if (FastDecode)
        {
            //insert placeholders for the decoder
            @params += ",0,{}";
        }
        // the whole thing
        return "eval(" + unpack + "(" + @params + "))\n";
    }

    private static string Escape(string input)
    {
        var r = new Regex("([\\\\'])");
        return r.Replace(input, "\\$1");
    }

    private static EncodeMethod GetEncoder(PackerEncoding encoding)
    {
        switch (encoding)
        {
            case PackerEncoding.Mid:
                return Encode36;
            case PackerEncoding.Normal:
                return Encode62;
            case PackerEncoding.HighAscii:
                return Encode95;
            default:
                return Encode10;
        }
    }

    private static string Encode10(int code)
    {
        return code.ToString();
    }

    //lookups seemed like the easiest way to do this since 
    // I don't know of an equivalent to .toString(36)
    private const string Lookup36 = "0123456789abcdefghijklmnopqrstuvwxyz";

    private static string Encode36(int code)
    {
        var encoded = "";
        var i = 0;
        do
        {
            var digit = (code / (int)Math.Pow(36, i)) % 36;
            encoded = Lookup36[digit] + encoded;
            code -= digit * (int)Math.Pow(36, i++);
        } while (code > 0);
        return encoded;
    }

    private const string Lookup62 = Lookup36 + "ABCDEFGHIJKLMNOPQRSTUVWXYZ";

    private static string Encode62(int code)
    {
        var encoded = "";
        var i = 0;
        do
        {
            var digit = (code / (int)Math.Pow(62, i)) % 62;
            encoded = Lookup62[digit] + encoded;
            code -= digit * (int)Math.Pow(62, i++);
        } while (code > 0);
        return encoded;
    }

    private static string _lookup95 = "¡¢£¤¥¦§¨©ª«¬­®¯°±²³´µ¶·¸¹º»¼½¾¿ÀÁÂÃÄÅÆÇÈÉÊËÌÍÎÏÐÑÒÓÔÕÖ×ØÙÚÛÜÝÞßàáâãäåæçèéêëìíîïðñòóôõö÷øùúûüýþÿ";

    private static string Encode95(int code)
    {
        var encoded = "";
        var i = 0;
        do
        {
            var digit = (code / (int)Math.Pow(95, i)) % 95;
            encoded = _lookup95[digit] + encoded;
            code -= digit * (int)Math.Pow(95, i++);
        } while (code > 0);
        return encoded;
    }

    private static string Escape95(string input)
    {
        var r = new Regex("[\xa1-\xff]");
        return r.Replace(input, Escape95Eval);
    }

    private static string Escape95Eval(Match match)
    {
        return "\\x" + ((int)match.Value[0]).ToString("x"); //return hexadecimal value
    }

    private static string EncodeLocalVars(Match match, int offset)
    {
        var length = match.Groups[offset + 2].Length;
        var start = length - Math.Max(length - match.Groups[offset + 3].Length, 0);
        return match.Groups[offset + 1].Value.Substring(start, length) +
            match.Groups[offset + 4].Value;
    }

    private string? EncodeWithLookup(Match match, int offset)
    {
        return (string?)_encodingLookup.Encoded[match.Groups[offset].Value];
    }

    private delegate string EncodeMethod(int code);

    private static string EncodePrivate(int code)
    {
        return "_" + code;
    }

    private static WordList Analyze(string input, Regex regex, EncodeMethod encodeMethod)
    {
        // analyse
        // retreive all words in the script
        var all = regex.Matches(input);
        WordList rtrn;
        rtrn.Sorted = new StringCollection(); // list of words sorted by frequency
        rtrn.Protected = new HybridDictionary(); // dictionary of word->encoding
        rtrn.Encoded = new HybridDictionary(); // instances of "protected" words
        if (all.Count <= 0) return rtrn;
        var unsorted = new StringCollection(); // same list, not sorted
        var @protected = new HybridDictionary(); // "protected" words (dictionary of word->"word")
        var values = new HybridDictionary(); // dictionary of charCode->encoding (eg. 256->ff)
        var count = new HybridDictionary(); // word->count
        int i = all.Count, j = 0;
        string word;
        // count the occurrences - used for sorting later
        do
        {
            word = "$" + all[--i].Value;
            if (count[word] == null)
            {
                count[word] = 0;
                unsorted.Add(word);
                // make a dictionary of all of the protected words in this script
                //  these are words that might be mistaken for encoding
                @protected["$" + (values[j] = encodeMethod(j))] = j++;
            }
            // increment the word counter
            count[word] = (int)count[word] + 1;
        } while (i > 0);
        /* prepare to sort the word list, first we must protect
                words that are also used as codes. we assign them a code
                equivalent to the word itself.
               e.g. if "do" falls within our encoding range
                    then we store keywords["do"] = "do";
               this avoids problems when decoding */
        i = unsorted.Count;
        var sortedarr = new string[unsorted.Count];
        do
        {
            word = unsorted[--i]!;
            if (@protected[word] == null) continue;
            if (word.Length > 1) sortedarr[(int)@protected[word]] = word.Substring(1);
            rtrn.Protected[(int)@protected[word]] = true;
            count[word] = 0;
        } while (i > 0);
        var unsortedarr = new string[unsorted.Count];
        unsorted.CopyTo(unsortedarr, 0);
        // sort the words by frequency
        Array.Sort(unsortedarr, new CountComparer(count));
        j = 0;
        /*because there are "protected" words in the list
              we must add the sorted words around them */
        do
        {
            if (sortedarr[i] == null)
                sortedarr[i] = unsortedarr[j++].Substring(1);
            rtrn.Encoded[sortedarr[i]] = values[i];
        } while (++i < unsortedarr.Length);
        rtrn.Sorted.AddRange(sortedarr);
        return rtrn;
    }

    private struct WordList
    {
        public StringCollection Sorted;
        public HybridDictionary Encoded;
        public HybridDictionary Protected;
    }

    private class CountComparer : IComparer
    {
        private readonly HybridDictionary _count;

        public CountComparer(HybridDictionary count)
        {
            _count = count;
        }

        #region IComparer Members

        public int Compare(object x, object y)
        {
            return (int)_count[y] - (int)_count[x];
        }

        #endregion
    }

    //#region IHttpHandler Members

    //public void ProcessRequest(HttpContext context)
    //{
    //    // try and read settings from config file
    //    if (System.Configuration.ConfigurationManager.GetSection("ecmascriptpacker") != null)
    //    {
    //        NameValueCollection cfg = (NameValueCollection)System.Configuration.ConfigurationManager.GetSection("ecmascriptpacker");
    //        if (cfg["Encoding"] != null)
    //        {
    //            switch (cfg["Encoding"].ToLower())
    //            {
    //                case "none":
    //                    Encoding = PackerEncoding.None;
    //                    break;
    //                case "numeric":
    //                    Encoding = PackerEncoding.Numeric;
    //                    break;
    //                case "mid":
    //                    Encoding = PackerEncoding.Mid;
    //                    break;
    //                case "normal":
    //                    Encoding = PackerEncoding.Normal;
    //                    break;
    //                case "highascii":
    //                case "high":
    //                    Encoding = PackerEncoding.HighAscii;
    //                    break;
    //            }
    //        }
    //        if (cfg["FastDecode"] != null)
    //        {
    //            if (cfg["FastDecode"].ToLower() == "true")
    //                FastDecode = true;
    //            else
    //                FastDecode = false;
    //        }
    //        if (cfg["SpecialChars"] != null)
    //        {
    //            if (cfg["SpecialChars"].ToLower() == "true")
    //                SpecialChars = true;
    //            else
    //                SpecialChars = false;
    //        }
    //        if (cfg["Enabled"] != null)
    //        {
    //            if (cfg["Enabled"].ToLower() == "true")
    //                Enabled = true;
    //            else
    //                Enabled = false;
    //        }
    //    }
    //    // try and read settings from URL
    //    if (context.Request.QueryString["Encoding"] != null)
    //    {
    //        switch (context.Request.QueryString["Encoding"].ToLower())
    //        {
    //            case "none":
    //                Encoding = PackerEncoding.None;
    //                break;
    //            case "numeric":
    //                Encoding = PackerEncoding.Numeric;
    //                break;
    //            case "mid":
    //                Encoding = PackerEncoding.Mid;
    //                break;
    //            case "normal":
    //                Encoding = PackerEncoding.Normal;
    //                break;
    //            case "highascii":
    //            case "high":
    //                Encoding = PackerEncoding.HighAscii;
    //                break;
    //        }
    //    }
    //    if (context.Request.QueryString["FastDecode"] != null)
    //    {
    //        if (context.Request.QueryString["FastDecode"].ToLower() == "true")
    //            FastDecode = true;
    //        else
    //            FastDecode = false;
    //    }
    //    if (context.Request.QueryString["SpecialChars"] != null)
    //    {
    //        if (context.Request.QueryString["SpecialChars"].ToLower() == "true")
    //            SpecialChars = true;
    //        else
    //            SpecialChars = false;
    //    }
    //    if (context.Request.QueryString["Enabled"] != null)
    //    {
    //        if (context.Request.QueryString["Enabled"].ToLower() == "true")
    //            Enabled = true;
    //        else
    //            Enabled = false;
    //    }
    //    //handle the request
    //    TextReader r = new StreamReader(context.Request.PhysicalPath);
    //    string jscontent = r.ReadToEnd();
    //    r.Close();
    //    context.Response.ContentType = "text/javascript";
    //    context.Response.Output.Write(Pack(jscontent));
    //}

    //public bool IsReusable
    //{
    //    get
    //    {
    //        if (System.Configuration.ConfigurationManager.GetSection("ecmascriptpacker") != null)
    //        {
    //            NameValueCollection cfg = (NameValueCollection)System.Configuration.ConfigurationManager.GetSection("ecmascriptpacker");
    //            if (cfg["IsReusable"] != null)
    //                if (cfg["IsReusable"].ToLower() == "true")
    //                    return true;
    //        }
    //        return false;
    //    }
    //}

    //#endregion
}