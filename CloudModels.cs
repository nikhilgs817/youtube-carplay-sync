using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace YouTubeMp3
{
    // Anything that can pick the right catalogue match and add language and genre: a local server or a cloud API.
    internal interface ISongModel : IDisposable
    {
        int ClassifyTimeout { get; set; }
        Task<string[]> ListAsync(CancellationToken cancel);
        Task<IDictionary<string, object>> AskJsonAsync(string model, string system, object input, object schema, CancellationToken cancel);
    }

    // The ways to connect AI, in the order Settings shows them.
    internal static class AiProviders
    {
        internal const string None = "None";
        internal const string Ollama = LocalModels.Ollama;
        internal const string Vllm = LocalModels.Vllm;
        internal const string Claude = "Claude";
        internal const string ChatGpt = "ChatGPT";
        internal const string Gemini = "Gemini";
        internal const string Grok = "Grok";

        internal static readonly string[] All = { None, Ollama, Vllm, Claude, ChatGpt, Gemini, Grok };

        internal static string Label(string provider)
        {
            switch (provider)
            {
                case Ollama: return "Local model: Ollama";
                case Vllm: return "Local model: vLLM or other OpenAI-style server";
                case Claude: return "Claude (Anthropic API key)";
                case ChatGpt: return "ChatGPT (OpenAI API key)";
                case Gemini: return "Gemini (Google AI Studio API key)";
                case Grok: return "Grok (xAI API key)";
                default: return "No AI: built-in rules (lower accuracy)";
            }
        }

        internal static bool IsLocal(string provider) { return provider == Ollama || provider == Vllm; }
        internal static bool IsCloud(string provider) { return provider == Claude || provider == ChatGpt || provider == Gemini || provider == Grok; }

        // Where to get a key, said in words the person can follow; the app opens no links.
        internal static string KeyHelp(string provider)
        {
            switch (provider)
            {
                case Claude: return "Create a key at console.anthropic.com › API keys. Usage is billed to your Anthropic account.";
                case ChatGpt: return "Create a key at platform.openai.com › API keys. Usage is billed to your OpenAI account, separate from a ChatGPT subscription.";
                case Gemini: return "Create a key at aistudio.google.com › Get API key. Google's free tier covers light use.";
                case Grok: return "Create a key at console.x.ai › API keys. Usage is billed to your xAI account.";
                case Vllm: return "Optional. Only if your server asks for one.";
                default: return "";
            }
        }

        // Starting suggestions so the list is never empty. Test & load models replaces them with what the account can use.
        internal static string[] SuggestedModels(string provider)
        {
            switch (provider)
            {
                case Claude: return new[] { "claude-opus-5-5", "claude-sonnet-5-5", "claude-haiku-5-5" };
                case ChatGpt: return new[] { "gpt-5.5", "gpt-5.4", "gpt-5.4-mini" };
                case Gemini: return new[] { "gemini-3.5-flash", "gemini-3.1-flash-lite", "gemini-2.5-pro" };
                case Grok: return new[] { "grok-4.7", "grok-4.3" };
                case Ollama: return new[] { "qwen3:30b-a3b", "gemma3:27b" };
                default: return new string[0];
            }
        }

        // The client for the chosen provider, or null when AI is off or not set up yet.
        internal static ISongModel Create(Preferences prefs)
        {
            string provider = prefs.ActiveProvider;
            if (provider == None || String.IsNullOrWhiteSpace(prefs.ModelFor(provider))) return null;
            if (IsLocal(provider))
            {
                string address = prefs.EndpointFor(provider);
                return new LocalModels(provider, address, prefs.GetKey(provider)) { ClassifyTimeout = prefs.Timeout };
            }
            string key = prefs.GetKey(provider);
            if (key.Length == 0) return null;
            return new CloudModels(provider, key) { ClassifyTimeout = prefs.Timeout };
        }
    }

    // Claude, ChatGPT, Gemini and Grok over their own HTTPS APIs. Only song details are sent: the YouTube title, channel,
    // a short description and the catalogue matches. Never audio, files or anything else from this PC.
    internal sealed class CloudModels : ISongModel
    {
        private readonly JavaScriptSerializer json = new JavaScriptSerializer { MaxJsonLength = Int32.MaxValue };
        private readonly HttpClient http;
        private readonly string provider;
        private readonly string key;
        public int ClassifyTimeout { get; set; }

        internal CloudModels(string provider, string apiKey, HttpMessageHandler handler = null)
        {
            if (!AiProviders.IsCloud(provider)) throw new ArgumentException("Not a cloud provider: " + provider);
            if (String.IsNullOrWhiteSpace(apiKey)) throw new ArgumentException("Add your " + provider + " API key in Settings.");
            this.provider = provider;
            key = apiKey.Trim();
            ClassifyTimeout = 120;
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (NotSupportedException) { }
            http = new HttpClient(handler ?? new HttpClientHandler { AllowAutoRedirect = false });
            http.Timeout = Timeout.InfiniteTimeSpan;
            http.DefaultRequestHeaders.UserAgent.ParseAdd("YouTubeCarPlaySync/" + AppInfo.Version);
        }

        private string Base
        {
            get
            {
                switch (provider)
                {
                    case AiProviders.Claude: return "https://api.anthropic.com/v1/";
                    case AiProviders.ChatGpt: return "https://api.openai.com/v1/";
                    case AiProviders.Gemini: return "https://generativelanguage.googleapis.com/v1beta/";
                    default: return "https://api.x.ai/v1/";
                }
            }
        }

        private HttpRequestMessage Request(HttpMethod method, string path, object payload, IEnumerable<string> betas = null)
        {
            var request = new HttpRequestMessage(method, Base + path);
            if (provider == AiProviders.Claude)
            {
                request.Headers.Add("x-api-key", key);
                request.Headers.Add("anthropic-version", "2026-10-21");
                if (betas != null && betas.Any()) request.Headers.Add("anthropic-beta", String.Join(",", betas.ToArray()));
            }
            else if (provider == AiProviders.Gemini) request.Headers.Add("x-goog-api-key", key);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);
            if (payload != null) request.Content = new StringContent(json.Serialize(payload), Encoding.UTF8, "application/json");
            return request;
        }

        // The models this key can use for text, newest names first.
        public async Task<string[]> ListAsync(CancellationToken cancel)
        {
            var names = new List<string>();
            string path = provider == AiProviders.Claude ? "models?limit=100" : provider == AiProviders.Gemini ? "models?pageSize=200" : "models";
            var reply = await SendAsync(() => Request(HttpMethod.Get, path, null), 20, cancel).ConfigureAwait(false);
            var entries = (Get(reply, provider == AiProviders.Gemini ? "models" : "data") as object[]) ?? new object[0];
            foreach (var entry in entries)
            {
                if (provider == AiProviders.Gemini)
                {
                    var methods = Get(entry, "supportedGenerationMethods") as object[];
                    string name = Get(entry, "name") as string ?? "";
                    if (methods == null || !methods.Contains("generateContent")) continue;
                    if (name.StartsWith("models/", StringComparison.Ordinal)) name = name.Substring(7);
                    if (name.Contains("embedding") || name.Contains("image") || name.Contains("tts") || name.Contains("aqa")) continue;
                    names.Add(name);
                }
                else
                {
                    string id = Get(entry, "id") as string ?? "";
                    if (provider == AiProviders.ChatGpt && !IsOpenAiChatModel(id)) continue;
                    if (provider == AiProviders.Grok && (id.Contains("image") || id.Contains("vision") || id.Contains("imagine"))) continue;
                    if (id.Length > 0) names.Add(id);
                }
            }
            return names.Distinct(StringComparer.Ordinal).OrderByDescending(n => n, StringComparer.OrdinalIgnoreCase).ToArray();
        }

        // OpenAI's list also holds speech, image, embedding and moderation models: keep the chat ones.
        internal static bool IsOpenAiChatModel(string id)
        {
            if (!(id.StartsWith("gpt-", StringComparison.Ordinal) || id.StartsWith("o", StringComparison.Ordinal) || id == "chat-latest")) return false;
            foreach (var skip in new[] { "audio", "realtime", "tts", "transcribe", "image", "search", "embedding", "moderation", "instruct", "codex" })
                if (id.Contains(skip)) return false;
            return true;
        }

        public async Task<IDictionary<string, object>> AskJsonAsync(string model, string system, object input, object schema, CancellationToken cancel)
        {
            string user = json.Serialize(input);
            string text;
            switch (provider)
            {
                case AiProviders.Claude: text = await AskClaude(model, system, user, schema, cancel).ConfigureAwait(false); break;
                case AiProviders.Gemini: text = await AskGemini(model, system, user, schema, cancel).ConfigureAwait(false); break;
                default: text = await AskOpenAiStyle(model, system, user, schema, cancel).ConfigureAwait(false); break;
            }
            return ParseObject(text);
        }

        // Anthropic Messages API with JSON outputs (output_config.format). Forced tool use is not accepted by the newest
        // models, and structured outputs return exactly the schema's object.
        private async Task<string> AskClaude(string model, string system, string user, object schema, CancellationToken cancel)
        {
            var payload = new Dictionary<string, object>
            {
                { "model", model }, { "max_tokens", 8192 }, { "system", system },
                { "messages", new[] { new { role = "user", content = user } } },
                { "output_config", new Dictionary<string, object> { { "format", new { type = "json_schema", schema = schema } } } }
            };
            // A safety decline on these models is retried on a fallback model inside the same call.
            var betas = new List<string>();
            if (UsesServerFallback(model))
            {
                betas.Add("server-side-fallback-2026-07-01");
                payload["fallbacks"] = "default";
            }
            var reply = await SendAsync(() => Request(HttpMethod.Post, "messages", payload, betas), ClassifyTimeout, cancel).ConfigureAwait(false);
            string stop = Get(reply, "stop_reason") as string;
            if (stop == "refusal") throw new InvalidOperationException("Claude declined to answer for this song.");
            if (stop == "max_tokens") throw new InvalidOperationException("Claude's answer was cut off.");
            var blocks = (Get(reply, "content") as object[]) ?? new object[0];
            var text = blocks.Where(b => (Get(b, "type") as string) == "text").Select(b => Get(b, "text") as string).LastOrDefault();
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Claude returned an empty answer.");
            return text;
        }

        internal static bool UsesServerFallback(string model)
        {
            return model == "claude-opus-5-5" || model == "claude-sonnet-5-5" || model == "claude-opus-5" ||
                   model == "claude-fable-5-1" || model == "claude-fable-5-2";
        }

        // OpenAI and xAI share the chat completions shape. Strict JSON schema first; an older model that rejects it gets
        // plain JSON mode, with the fields named in the instructions.
        private async Task<string> AskOpenAiStyle(string model, string system, string user, object schema, CancellationToken cancel)
        {
            Func<object, Dictionary<string, object>> build = format => new Dictionary<string, object>
            {
                { "model", model },
                { "messages", new object[] { new { role = "system", content = system + FieldsHint(schema) }, new { role = "user", content = user } } },
                { "response_format", format }
            };
            object reply = null;
            bool strictRejected = false;
            try
            {
                var strict = build(new { type = "json_schema", json_schema = new { name = "song_info", strict = true, schema = schema } });
                reply = await SendAsync(() => Request(HttpMethod.Post, "chat/completions", strict), ClassifyTimeout, cancel).ConfigureAwait(false);
            }
            catch (ModelHttpException ex)
            {
                if (ex.Status != 400) throw;
                strictRejected = true;   // C# 5: no await inside catch, so retry below
            }
            if (strictRejected)
            {
                var loose = build(new { type = "json_object" });
                reply = await SendAsync(() => Request(HttpMethod.Post, "chat/completions", loose), ClassifyTimeout, cancel).ConfigureAwait(false);
            }
            var choices = Get(reply, "choices") as object[];
            var message = choices == null ? null : Get(choices.FirstOrDefault(), "message");
            if (Get(message, "refusal") is string) throw new InvalidOperationException(provider + " declined to answer for this song.");
            var text = Get(message, "content") as string;
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException(provider + " returned an empty answer.");
            return text;
        }

        // Gemini generateContent in JSON mode. The fields are named in the instructions, which every Gemini model accepts.
        private async Task<string> AskGemini(string model, string system, string user, object schema, CancellationToken cancel)
        {
            var payload = new Dictionary<string, object>
            {
                { "systemInstruction", new { parts = new[] { new { text = system + FieldsHint(schema) } } } },
                { "contents", new[] { new { role = "user", parts = new[] { new { text = user } } } } },
                { "generationConfig", new Dictionary<string, object> { { "responseMimeType", "application/json" }, { "temperature", 0 } } }
            };
            string name = model.StartsWith("models/", StringComparison.Ordinal) ? model.Substring(7) : model;
            var reply = await SendAsync(() => Request(HttpMethod.Post, "models/" + Uri.EscapeDataString(name) + ":generateContent", payload),
                ClassifyTimeout, cancel).ConfigureAwait(false);
            var candidates = Get(reply, "candidates") as object[];
            var first = candidates == null ? null : candidates.FirstOrDefault();
            if (first == null) throw new InvalidOperationException("Gemini returned no answer" + Blocked(reply) + ".");
            var parts = Get(Get(first, "content"), "parts") as object[];
            string text = parts == null ? null : String.Concat(parts.Select(p => Get(p, "text") as string ?? "").ToArray());
            if (String.IsNullOrWhiteSpace(text)) throw new InvalidOperationException("Gemini returned an empty answer.");
            return text;
        }

        private static string Blocked(object reply)
        {
            var reason = Get(Get(reply, "promptFeedback"), "blockReason") as string;
            return String.IsNullOrEmpty(reason) ? "" : " (" + reason + ")";
        }

        // "Return one JSON object with exactly these fields: match (integer), title (string), …"
        internal static string FieldsHint(object schema)
        {
            var map = new JavaScriptSerializer().DeserializeObject(new JavaScriptSerializer().Serialize(schema)) as IDictionary<string, object>;
            var props = map == null ? null : Get(map, "properties") as IDictionary<string, object>;
            if (props == null) return "";
            return " Return one JSON object with exactly these fields: " +
                String.Join(", ", props.Select(p => p.Key + " (" + (Get(p.Value, "type") as string ?? "string") + ")").ToArray()) + ".";
        }

        internal static IDictionary<string, object> ParseObject(string text)
        {
            string t = text.Trim();
            // Some models wrap JSON in a code fence.
            if (t.StartsWith("```", StringComparison.Ordinal))
            {
                int start = t.IndexOf('\n');
                int end = t.LastIndexOf("```", StringComparison.Ordinal);
                if (start > 0 && end > start) t = t.Substring(start + 1, end - start - 1).Trim();
            }
            int open = t.IndexOf('{'), close = t.LastIndexOf('}');
            if (open < 0 || close <= open) throw new InvalidOperationException("The model did not return a JSON object.");
            var result = new JavaScriptSerializer().DeserializeObject(t.Substring(open, close - open + 1)) as IDictionary<string, object>;
            if (result == null) throw new InvalidOperationException("The model did not return a JSON object.");
            return result;
        }

        private async Task<object> SendAsync(Func<HttpRequestMessage> make, int timeout, CancellationToken cancel)
        {
            for (int attempt = 0; ; attempt++)
            {
                using (var request = make())
                using (var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancel))
                {
                    deadline.CancelAfter(TimeSpan.FromSeconds(Math.Max(10, timeout)));
                    HttpResponseMessage response;
                    try { response = await http.SendAsync(request, deadline.Token).ConfigureAwait(false); }
                    catch (OperationCanceledException)
                    {
                        cancel.ThrowIfCancellationRequested();
                        throw new TimeoutException(provider + " did not answer within " + timeout + " seconds.");
                    }
                    catch (HttpRequestException ex)
                    {
                        throw new InvalidOperationException("Couldn't reach " + provider + ". Check your internet connection.", ex);
                    }
                    using (response)
                    {
                        string body = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                        int code = (int)response.StatusCode;
                        if (response.IsSuccessStatusCode) return json.DeserializeObject(body);
                        // Busy or overloaded: wait and try once more.
                        if ((code == 429 || code == 529 || code == 503) && attempt == 0)
                        {
                            await Task.Delay(TimeSpan.FromSeconds(4), cancel).ConfigureAwait(false);
                            continue;
                        }
                        throw new ModelHttpException(code, Explain(code, ServerMessage(body)));
                    }
                }
            }
        }

        private string Explain(int code, string detail)
        {
            string why;
            if (code == 401 || code == 403) why = provider + " refused the API key. Check it in Settings";
            else if (code == 404) why = provider + " doesn't know that model. Select Test & load models and pick one from the list";
            else if (code == 402) why = provider + " says the account has no credit left";
            else if (code == 429) why = provider + " says too many requests or no credit left. Wait a minute, or check the account's billing";
            else if (code >= 500) why = provider + " had a server problem. Try again later";
            else why = provider + " answered HTTP " + code.ToString(CultureInfo.InvariantCulture);
            return why + (String.IsNullOrEmpty(detail) ? "." : ": " + detail);
        }

        // The error text the API sent, without echoing anything that could hold the key.
        private string ServerMessage(string body)
        {
            try
            {
                var map = json.DeserializeObject(body);
                var error = Get(map, "error");
                string message = Get(error, "message") as string ?? Get(map, "message") as string ?? (error as string);
                if (String.IsNullOrEmpty(message)) return "";
                message = message.Replace(key, "***").Trim();
                return message.Length > 200 ? message.Substring(0, 200) + "…" : message;
            }
            catch (ArgumentException) { return ""; }
            catch (InvalidOperationException) { return ""; }
        }

        private static object Get(object value, string property)
        {
            var map = value as IDictionary<string, object>;
            object found;
            return map != null && map.TryGetValue(property, out found) ? found : null;
        }

        public void Dispose() { http.Dispose(); }
    }

    internal sealed class ModelHttpException : InvalidOperationException
    {
        internal readonly int Status;
        internal ModelHttpException(int status, string message) : base(message) { Status = status; }
    }
}
