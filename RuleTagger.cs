using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace YouTubeMp3
{
    // Language and genre without any AI model: the script the title is written in, words that give a song away,
    // well-known music labels and the genre the catalogue lists. Good for the clear cases; a model does much better on
    // the rest, which is why songs tagged this way say "Without a model, accuracy is low".
    internal static class RuleTagger
    {
        internal const string LowAccuracy = "Without a model, accuracy is low";

        // Unicode script ranges, checked in order. Gurmukhi before Devanagari so Punjabi in its own script isn't Hindi.
        private static readonly KeyValuePair<string, int[]>[] Scripts =
        {
            Script("Punjabi", 0x0A00, 0x0A7F), Script("Hindi", 0x0900, 0x097F), Script("Bengali", 0x0980, 0x09FF),
            Script("Gujarati", 0x0A80, 0x0AFF), Script("Odia", 0x0B00, 0x0B7F), Script("Tamil", 0x0B80, 0x0BFF),
            Script("Telugu", 0x0C00, 0x0C7F), Script("Kannada", 0x0C80, 0x0CFF), Script("Malayalam", 0x0D00, 0x0D7F),
            Script("Sinhala", 0x0D80, 0x0DFF), Script("Thai", 0x0E00, 0x0E7F), Script("Korean", 0xAC00, 0xD7AF),
            Script("Japanese", 0x3040, 0x30FF), Script("Chinese", 0x4E00, 0x9FFF), Script("Russian", 0x0400, 0x04FF),
            Script("Greek", 0x0370, 0x03FF), Script("Hebrew", 0x0590, 0x05FF), Script("Urdu", 0x0600, 0x06FF)
        };

        private static KeyValuePair<string, int[]> Script(string language, int from, int to)
        {
            return new KeyValuePair<string, int[]>(language, new[] { from, to });
        }

        // Words that name the language outright, in a title, description or channel name.
        private static readonly string[][] Named =
        {
            new[] { "Punjabi", "punjabi" }, new[] { "Hindi", "hindi", "bollywood" }, new[] { "Tamil", "tamil", "kollywood" },
            new[] { "Telugu", "telugu", "tollywood" }, new[] { "Kannada", "kannada", "sandalwood" }, new[] { "Malayalam", "malayalam", "mollywood" },
            new[] { "Marathi", "marathi" }, new[] { "Bengali", "bengali", "bangla" }, new[] { "Gujarati", "gujarati", "garba" },
            new[] { "Bhojpuri", "bhojpuri" }, new[] { "Haryanvi", "haryanvi" }, new[] { "Rajasthani", "rajasthani" },
            new[] { "Urdu", "urdu" }, new[] { "Korean", "k-pop", "kpop", "korean" }, new[] { "Spanish", "spanish", "latino", "reggaeton" },
            new[] { "Japanese", "j-pop", "jpop", "anime" }, new[] { "Arabic", "arabic" }, new[] { "French", "french" }
        };

        // Labels and channels whose songs are nearly always in one language.
        private static readonly string[][] Labels =
        {
            new[] { "Hindi", "t-series", "tseries", "zee music", "saregama", "tips official", "tips music", "yrf", "sony music india",
                "eros now", "venus", "shemaroo", "times music", "dharma", "red chillies", "aditya music hindi" },
            new[] { "Punjabi", "speed records", "white hill", "geet mp3", "jass records", "desi melodies", "brown boys", "humble music",
                "rehaan records", "amar audio", "lokdhun" },
            new[] { "Tamil", "sony music south", "think music", "lahari tamil", "sun tv", "u1 records" },
            new[] { "Telugu", "aditya music", "lahari music telugu", "mango music" },
            new[] { "Kannada", "anand audio", "lahari music kannada", "pappend" },
            new[] { "Malayalam", "muzik247", "goodwill entertainments", "satyam audios" },
            new[] { "Bhojpuri", "wave music", "worldwide records bhojpuri" },
            new[] { "Haryanvi", "sonotek", "mor haryanvi" }
        };

        // Romanised words common in songs of one language and rare in English.
        private static readonly HashSet<string> HindiWords = Words(
            "dil mera meri mere tera teri tere tujhe mujhe hai hain kya nahi nahin ishq pyaar pyar mohabbat sanam saathiya yaara yaar " +
            "jaana jaan zindagi aankhein ankhon raat chaand chand baarish dard tum tumhe hum humko main ho gaya kabhi kahin aaj kal " +
            "bas sun suno mann jiya ve ae re hoon tha thi chal chalo saath saajna saiyaan sajna kesariya raabta tujh kuch kuchh " +
            "zara phir lag jaa gale khwab dhadkan bekhayali");
        private static readonly HashSet<string> PunjabiWords = Words(
            "tu tenu menu mainu kudi munda jatt jatti gabru yaar yaara pind sohni sohniye nakhre jutti laung lachi tera teri " +
            "ni ve kithe kiven hun ajj kalla dil viah gedi lalkaara peg patiala desi dhol bhangra sardar akhiyan putt bapu chann");
        private static readonly HashSet<string> EnglishWords = Words(
            "the you your love me my baby night girl boy feel heart never always tonight dance with what when where how " +
            "don't can't i'm we they this that like just know want need time life world");

        private static HashSet<string> Words(string list)
        {
            return new HashSet<string>(list.Split(' ').Where(w => w.Length > 0), StringComparer.OrdinalIgnoreCase);
        }

        // Apple Music's primary genres, mapped to the genres this app groups by.
        private static readonly Dictionary<string, string> AppleGenres = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "Bollywood", "Soundtrack" }, { "Indian Pop", "Pop" }, { "Punjabi Pop", "Pop" }, { "Punjabi", "Pop" }, { "Tamil", "Soundtrack" },
            { "Telugu", "Soundtrack" }, { "Hip-Hop/Rap", "Hip-Hop" }, { "Hip-Hop", "Hip-Hop" }, { "Rap", "Rap" }, { "R&B/Soul", "R&B" },
            { "Devotional & Spiritual", "Devotional" }, { "Devotional", "Devotional" }, { "Indian Classical", "Classical" },
            { "Carnatic Classical", "Classical" }, { "Hindustani Classical", "Classical" }, { "Ghazals", "Ghazal" }, { "Sufi", "Sufi" },
            { "Qawwali", "Sufi" }, { "Regional Indian", "Folk" }, { "Indian Folk", "Folk" }, { "Folk", "Folk" },
            { "Singer/Songwriter", "Indie" }, { "Alternative", "Indie" }, { "Indie Pop", "Indie" }, { "Dance", "Dance" },
            { "Electronic", "Electronic" }, { "House", "Electronic" }, { "Soundtrack", "Soundtrack" }, { "Pop", "Pop" }, { "Rock", "Rock" },
            { "Jazz", "Jazz" }, { "Classical", "Classical" }, { "Country", "Country" }, { "Reggae", "Reggae" }, { "Latin", "Latin" },
            { "K-Pop", "Pop" }, { "J-Pop", "Pop" }, { "Metal", "Rock" }, { "Hard Rock", "Rock" }, { "Blues", "Blues" }, { "Lofi", "Lo-fi" }
        };

        // Words in a title or description that give the genre away, strongest first.
        private static readonly string[][] GenreWords =
        {
            new[] { "Devotional", "bhajan", "aarti", "aarati", "mantra", "kirtan", "shabad", "gurbani", "chalisa", "stotram", "bhakti", "naat", "hymn", "worship", "devotional" },
            new[] { "Sufi", "qawwali", "sufi" },
            new[] { "Ghazal", "ghazal" },
            new[] { "Lo-fi", "lofi", "lo-fi", "slowed", "reverb" },
            new[] { "Rap", "rap", "rapper", "cypher", "diss" },
            new[] { "Hip-Hop", "hip hop", "hip-hop", "hiphop", "drill", "trap" },
            new[] { "Dance", "remix", "dj ", "party", "club mix", "edm", "dance" },
            new[] { "Folk", "folk", "lok geet", "boliyan", "lavani" },
            new[] { "Classical", "raag", "raga", "classical", "carnatic", "hindustani", "instrumental" },
            new[] { "Romantic", "romantic", "love song", "ishq", "pyaar", "pyar", "mohabbat", "sanam", "dil", "valentine" },
            new[] { "Sad", "sad song", "breakup", "dard", "bewafa" },
            new[] { "Soundtrack", "from the film", "from the movie", "full video song", "ost", "soundtrack" }
        };

        // Fill in what's missing. Either string may come back empty when nothing points anywhere.
        internal static void Guess(YouTubeInfo yt, string title, CatalogMatch match, out string language, out string genre)
        {
            string channel = (yt == null ? "" : yt.Channel) ?? "";
            string description = (yt == null ? "" : yt.Description) ?? "";
            if (description.Length > 800) description = description.Substring(0, 800);
            string matchText = match == null ? "" : (match.Title + " " + match.Album + " " + match.Artist);
            string text = (title + " " + matchText).Trim();
            string all = (text + " " + channel + " " + description).ToLowerInvariant();

            language = ByScript(title + " " + matchText) ?? ByName(all) ?? ByLabel(channel.ToLowerInvariant()) ?? ByWords(text) ?? "";
            if (language.Length == 0 && match != null && match.Genre != null)
            {
                if (match.Genre.IndexOf("Bollywood", StringComparison.OrdinalIgnoreCase) >= 0) language = "Hindi";
                else if (match.Genre.IndexOf("Punjabi", StringComparison.OrdinalIgnoreCase) >= 0) language = "Punjabi";
                else if (match.Genre.IndexOf("Tamil", StringComparison.OrdinalIgnoreCase) >= 0) language = "Tamil";
                else if (match.Genre.IndexOf("Telugu", StringComparison.OrdinalIgnoreCase) >= 0) language = "Telugu";
                else if (match.Genre.IndexOf("K-Pop", StringComparison.OrdinalIgnoreCase) >= 0) language = "Korean";
            }

            genre = ByGenreWords((title + " " + description).ToLowerInvariant()) ?? "";
            string mapped;
            if (genre.Length == 0 && match != null && !String.IsNullOrEmpty(match.Genre) && AppleGenres.TryGetValue(match.Genre.Trim(), out mapped))
                genre = mapped;
            if (genre.Length == 0 && match != null && !String.IsNullOrEmpty(match.Genre)) genre = MusicLibrary.CleanGenreTag(match.Genre);
            // Most film songs on YouTube are film soundtracks; with nothing better, say so for Indian languages.
            if (genre.Length == 0 && (language == "Hindi" || language == "Tamil" || language == "Telugu" || language == "Kannada" || language == "Malayalam") &&
                Regex.IsMatch(all, @"\b(film|movie|song|video song)\b"))
                genre = "Soundtrack";
            if (genre.Length == 0 && language == "Punjabi") genre = "Pop";
        }

        internal static string ByScript(string text)
        {
            var counts = new Dictionary<string, int>();
            foreach (char c in text ?? "")
            {
                foreach (var script in Scripts)
                {
                    if (c < script.Value[0] || c > script.Value[1]) continue;
                    int n;
                    counts.TryGetValue(script.Key, out n);
                    counts[script.Key] = n + 1;
                    break;
                }
            }
            if (counts.Count == 0) return null;
            var top = counts.OrderByDescending(p => p.Value).First();
            // Hangul or kana with Han characters is still Korean or Japanese.
            if (top.Key == "Chinese" && counts.ContainsKey("Japanese")) return "Japanese";
            return top.Value >= 2 ? top.Key : null;
        }

        private static string ByName(string lower)
        {
            foreach (var entry in Named)
                for (int i = 1; i < entry.Length; i++)
                    if (Regex.IsMatch(lower, @"(^|[^a-z])" + Regex.Escape(entry[i]) + @"([^a-z]|$)")) return entry[0];
            return null;
        }

        private static string ByLabel(string channelLower)
        {
            if (channelLower.Length == 0) return null;
            foreach (var entry in Labels)
                for (int i = 1; i < entry.Length; i++)
                    if (channelLower.Contains(entry[i])) return entry[0];
            return null;
        }

        private static string ByWords(string text)
        {
            var words = Regex.Matches((text ?? "").ToLowerInvariant(), @"[a-z']+").Cast<Match>().Select(m => m.Value).ToList();
            if (words.Count == 0) return null;
            int hindi = words.Count(w => HindiWords.Contains(w)), punjabi = words.Count(w => PunjabiWords.Contains(w));
            int english = words.Count(w => EnglishWords.Contains(w));
            if (punjabi >= 2 && punjabi > hindi) return "Punjabi";
            if (hindi >= 2 && hindi >= english) return "Hindi";
            if (english >= 2 && english > hindi + punjabi) return "English";
            return null;
        }

        private static string ByGenreWords(string lower)
        {
            foreach (var entry in GenreWords)
                for (int i = 1; i < entry.Length; i++)
                    if (Regex.IsMatch(lower, @"(^|[^a-z])" + Regex.Escape(entry[i].Trim()) + @"([^a-z]|$)")) return entry[0];
            return null;
        }
    }
}
