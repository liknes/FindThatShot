using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;

namespace VideoArchiveManager.Core.Services.Ai;

// Faithful C# port of OpenAI CLIP's SimpleTokenizer (byte-level BPE). Loads the
// standard bpe_simple_vocab_16e6 merges file (gzip or plain text), builds the
// byte<->unicode table and merge ranks, and turns a caption / query into the
// [BOS] … [EOS] (+zero-pad) token id sequence the CLIP text encoder expects.
public sealed class ClipTokenizer
{
    private const int BosId = 49406; // <|startoftext|>
    private const int EosId = 49407; // <|endoftext|>

    private static readonly Regex Pattern = new(
        @"<\|startoftext\|>|<\|endoftext\|>|'s|'t|'re|'ve|'m|'ll|'d|[\p{L}]+|[\p{N}]|[^\s\p{L}\p{N}]+",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly Dictionary<int, char> _byteToUnicode;
    private readonly Dictionary<string, int> _encoder;
    private readonly Dictionary<(string, string), int> _bpeRanks;
    private readonly Dictionary<string, string> _cache = new();

    public ClipTokenizer(string vocabFilePath)
    {
        if (!File.Exists(vocabFilePath))
            throw new FileNotFoundException("CLIP BPE vocabulary not found.", vocabFilePath);

        _byteToUnicode = BytesToUnicode();

        var merges = ReadMerges(vocabFilePath);

        // Vocabulary: 256 byte chars, then those + "</w>", then the merged
        // pairs, then the two special tokens — matching the reference order so
        // the resulting ids line up with the model's text embedding table.
        var vocab = new List<string>(_byteToUnicode.Values.Select(c => c.ToString()));
        vocab.AddRange(_byteToUnicode.Values.Select(c => c + "</w>"));
        foreach (var (a, b) in merges) vocab.Add(a + b);
        vocab.Add("<|startoftext|>");
        vocab.Add("<|endoftext|>");

        _encoder = new Dictionary<string, int>(vocab.Count);
        for (var i = 0; i < vocab.Count; i++) _encoder[vocab[i]] = i;

        _bpeRanks = new Dictionary<(string, string), int>(merges.Count);
        for (var i = 0; i < merges.Count; i++) _bpeRanks[merges[i]] = i;
    }

    // Returns a fixed-length (contextLength) id sequence: BOS, tokens, EOS,
    // then 0-padded, alongside the matching attention mask (1 up to and
    // including EOS, 0 thereafter).
    public (long[] InputIds, long[] AttentionMask) Tokenize(string text, int contextLength)
    {
        var ids = new List<int> { BosId };
        foreach (var token in BpeEncode(text))
        {
            if (ids.Count >= contextLength - 1) break;
            ids.Add(token);
        }
        ids.Add(EosId);

        var inputIds = new long[contextLength];
        var mask = new long[contextLength];
        for (var i = 0; i < ids.Count && i < contextLength; i++)
        {
            inputIds[i] = ids[i];
            mask[i] = 1;
        }
        return (inputIds, mask);
    }

    private IEnumerable<int> BpeEncode(string text)
    {
        var cleaned = WhitespaceClean(text).ToLowerInvariant();
        foreach (Match m in Pattern.Matches(cleaned))
        {
            // Re-encode the matched substring through the byte->unicode table.
            var sb = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(m.Value))
            {
                sb.Append(_byteToUnicode[b]);
            }
            var token = sb.ToString();

            foreach (var bpeToken in Bpe(token).Split(' '))
            {
                if (_encoder.TryGetValue(bpeToken, out var id)) yield return id;
            }
        }
    }

    private string Bpe(string token)
    {
        if (_cache.TryGetValue(token, out var cached)) return cached;
        if (token.Length == 0) return token;

        // word = chars, last char carries the end-of-word marker.
        var word = new List<string>();
        for (var i = 0; i < token.Length; i++)
        {
            word.Add(i == token.Length - 1 ? token[i] + "</w>" : token[i].ToString());
        }

        if (word.Count == 1)
        {
            var single = word[0];
            _cache[token] = single;
            return single;
        }

        while (true)
        {
            // Find the highest-priority (lowest rank) adjacent pair.
            (string, string) best = default;
            var bestRank = int.MaxValue;
            var found = false;
            for (var i = 0; i < word.Count - 1; i++)
            {
                var pair = (word[i], word[i + 1]);
                if (_bpeRanks.TryGetValue(pair, out var rank) && rank < bestRank)
                {
                    bestRank = rank;
                    best = pair;
                    found = true;
                }
            }
            if (!found) break;

            var first = best.Item1!;
            var second = best.Item2!;
            var newWord = new List<string>(word.Count);
            var idx = 0;
            while (idx < word.Count)
            {
                var j = IndexOf(word, first, idx);
                if (j < 0)
                {
                    newWord.AddRange(word.GetRange(idx, word.Count - idx));
                    break;
                }
                newWord.AddRange(word.GetRange(idx, j - idx));
                idx = j;

                if (word[idx] == first && idx < word.Count - 1 && word[idx + 1] == second)
                {
                    newWord.Add(first + second);
                    idx += 2;
                }
                else
                {
                    newWord.Add(word[idx]);
                    idx += 1;
                }
            }

            word = newWord;
            if (word.Count == 1) break;
        }

        var result = string.Join(" ", word);
        _cache[token] = result;
        return result;
    }

    private static int IndexOf(List<string> list, string value, int start)
    {
        for (var i = start; i < list.Count; i++)
        {
            if (list[i] == value) return i;
        }
        return -1;
    }

    // Reads the merges out of the standard CLIP vocab file. The reference loader
    // skips the first line (a header) and takes 48894 merges
    // (49152 - 256 - 2 + 1), which together with the 512 byte tokens and the 2
    // specials yields the 49408-entry vocabulary.
    private static List<(string, string)> ReadMerges(string path)
    {
        string content;
        if (path.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
        {
            using var fs = File.OpenRead(path);
            using var gz = new GZipStream(fs, CompressionMode.Decompress);
            using var reader = new StreamReader(gz, Encoding.UTF8);
            content = reader.ReadToEnd();
        }
        else
        {
            content = File.ReadAllText(path, Encoding.UTF8);
        }

        var lines = content.Split('\n');
        var merges = new List<(string, string)>();
        const int take = 49152 - 256 - 2 + 1; // 48894
        for (var i = 1; i < lines.Length && merges.Count < take; i++)
        {
            var line = lines[i].Trim();
            if (line.Length == 0) continue;
            var parts = line.Split(' ');
            if (parts.Length != 2) continue;
            merges.Add((parts[0], parts[1]));
        }
        return merges;
    }

    // GPT-2 / CLIP byte<->unicode mapping: a reversible map from the 256 byte
    // values to printable unicode code points so BPE never sees raw control
    // bytes or whitespace it can't represent.
    private static Dictionary<int, char> BytesToUnicode()
    {
        var bs = new List<int>();
        for (var i = (int)'!'; i <= '~'; i++) bs.Add(i);
        for (var i = 0xA1; i <= 0xAC; i++) bs.Add(i);
        for (var i = 0xAE; i <= 0xFF; i++) bs.Add(i);

        var cs = new List<int>(bs);
        var n = 0;
        for (var b = 0; b < 256; b++)
        {
            if (!bs.Contains(b))
            {
                bs.Add(b);
                cs.Add(256 + n);
                n++;
            }
        }

        var map = new Dictionary<int, char>(256);
        for (var i = 0; i < bs.Count; i++) map[bs[i]] = (char)cs[i];
        return map;
    }

    private static string WhitespaceClean(string text)
    {
        var collapsed = Regex.Replace(text, @"\s+", " ");
        return collapsed.Trim();
    }
}
