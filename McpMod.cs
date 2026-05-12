using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Multiplayer.Game;

namespace STS2_MCP;

[ModInitializer("Initialize")]
public static partial class McpMod
{
    public const string Version = "0.4.0";
    public const int DefaultPort = 15526;
    private const string ConfigFileName = "STS2_MCP.conf";
    private const string DotEnvFileName = ".env";
    private const string PortEnvVar = "STS2_PORT";

    private static HttpListener? _listener;
    private static Thread? _serverThread;
    private static string[] _boundPrefixes = Array.Empty<string>();
    private static readonly ConcurrentQueue<Action> _mainThreadQueue = new();
    internal static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    private static int LoadPort()
    {
        try
        {
            string? modDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);

            if (TryLoadPortFromEnvironment(out int envPort))
                return envPort;

            if (TryLoadPortFromDotEnv(modDir, out int dotEnvPort))
                return dotEnvPort;

            if (modDir == null) return DefaultPort;

            string configPath = Path.Combine(modDir, ConfigFileName);
            if (!File.Exists(configPath))
            {
                try
                {
                    var defaultConfig = new Dictionary<string, object> { ["port"] = DefaultPort };
                    string json = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
                    File.WriteAllText(configPath, json);
                    GD.Print($"[STS2 MCP] Created default config at {configPath}");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    GD.Print($"[STS2 MCP] No config found at {configPath}; using default port {DefaultPort}");
                }
                return DefaultPort;
            }

            string content = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("port", out var portElem)
                && portElem.TryGetInt32(out int port)
                && port is > 0 and <= 65535)
            {
                return port;
            }

            GD.PrintErr($"[STS2 MCP] Invalid or missing 'port' in {configPath}, using default {DefaultPort}");
            return DefaultPort;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to load config: {ex.Message}, using default port {DefaultPort}");
            return DefaultPort;
        }
    }

    private static bool TryLoadPortFromEnvironment(out int port)
    {
        var configured = System.Environment.GetEnvironmentVariable(PortEnvVar);
        if (string.IsNullOrWhiteSpace(configured))
        {
            port = 0;
            return false;
        }

        return TryParseConfiguredPort(configured, PortEnvVar, out port);
    }

    private static bool TryLoadPortFromDotEnv(string? modDir, out int port)
    {
        foreach (var envPath in GetDotEnvPaths(modDir))
        {
            try
            {
                if (!File.Exists(envPath))
                    continue;

                foreach (var line in File.ReadLines(envPath))
                {
                    var stripped = line.Trim();
                    if (stripped.Length == 0 || stripped.StartsWith("#", StringComparison.Ordinal) || !stripped.Contains('='))
                        continue;

                    var separator = stripped.IndexOf('=');
                    var key = stripped[..separator].Trim();
                    if (key.StartsWith("export ", StringComparison.Ordinal))
                        key = key["export ".Length..].Trim();

                    if (!string.Equals(key, PortEnvVar, StringComparison.Ordinal))
                        continue;

                    var value = ParseDotEnvValue(stripped[(separator + 1)..]);
                    return TryParseConfiguredPort(value, $"{envPath}:{PortEnvVar}", out port);
                }
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                GD.PrintErr($"[STS2 MCP] Failed to read {envPath}: {ex.Message}");
            }
        }

        port = 0;
        return false;
    }

    private static IEnumerable<string> GetDotEnvPaths(string? modDir)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        if (!string.IsNullOrWhiteSpace(modDir))
        {
            var modEnvPath = Path.Combine(modDir, DotEnvFileName);
            if (seen.Add(modEnvPath))
                yield return modEnvPath;
        }

        var cwdEnvPath = Path.Combine(System.Environment.CurrentDirectory, DotEnvFileName);
        if (seen.Add(cwdEnvPath))
            yield return cwdEnvPath;
    }

    private static string ParseDotEnvValue(string rawValue)
    {
        var value = rawValue.Trim();
        if (value.Length >= 2 && (value[0] == '\'' || value[0] == '"'))
        {
            var quote = value[0];
            var closingQuoteIndex = value.IndexOf(quote, 1);
            if (closingQuoteIndex != -1)
                return value[1..closingQuoteIndex];
        }

        var commentIndex = value.IndexOf('#');
        if (commentIndex >= 0)
            value = value[..commentIndex].TrimEnd();

        return value;
    }

    private static bool TryParseConfiguredPort(string value, string source, out int port)
    {
        if (int.TryParse(value.Trim(), out port) && port is > 0 and <= 65535)
            return true;

        GD.PrintErr($"[STS2 MCP] Invalid port value from {source}: {value}; using next configured port source");
        port = 0;
        return false;
    }

    public static void Initialize()
    {
        try
        {
            // Optional settings UI patches should not block the HTTP bridge itself.
            TryApplyHarmonyPatches();

            // Connect to main thread process frame for action execution
            var tree = (SceneTree)Engine.GetMainLoop();
            tree.Connect(SceneTree.SignalName.ProcessFrame, Callable.From(ProcessMainThreadQueue));

            int port = LoadPort();
            StartHttpServer(port);

            _serverThread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "STS2_MCP_Server"
            };
            _serverThread.Start();

            GD.Print($"[STS2 MCP] v{Version} server started on {string.Join(", ", _boundPrefixes)}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to start: {ex}");
        }
    }

    private static void TryApplyHarmonyPatches()
    {
        try
        {
            new Harmony("com.sts2mcp").PatchAll();
        }
        catch (Exception ex)
        {
            GD.Print(
                $"[STS2 MCP] Optional Harmony settings UI injection skipped: {ex.GetType().Name}: {ex.Message}");
        }
    }

    private static void StartHttpServer(int port)
    {
        var attempts = new List<string[]>
        {
            new[] { $"http://+:{port}/" }
        };

        var ipv4Prefixes = GetIpv4Prefixes(port);
        if (ipv4Prefixes.Count > 0)
            attempts.Add(ipv4Prefixes.ToArray());

        attempts.Add(new[]
        {
            $"http://localhost:{port}/",
            $"http://127.0.0.1:{port}/"
        });

        Exception? lastError = null;

        foreach (var prefixes in attempts)
        {
            HttpListener? candidate = null;
            try
            {
                candidate = new HttpListener();
                foreach (var prefix in prefixes)
                    candidate.Prefixes.Add(prefix);
                candidate.Start();

                _listener = candidate;
                _boundPrefixes = prefixes;
                return;
            }
            catch (Exception ex)
            {
                lastError = ex;
                try { candidate?.Close(); } catch { }
            }
        }

        throw new InvalidOperationException(
            $"Could not bind STS2 MCP on port {port}. Tried wildcard, explicit IPv4, and loopback prefixes.",
            lastError);
    }

    private static List<string> GetIpv4Prefixes(int port)
    {
        var prefixes = new List<string>();

        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up)
                    continue;

                foreach (var addressInfo in nic.GetIPProperties().UnicastAddresses)
                {
                    if (addressInfo.Address.AddressFamily != AddressFamily.InterNetwork)
                        continue;

                    var address = addressInfo.Address.ToString();
                    if (address == "127.0.0.1" || address.StartsWith("169.254.", StringComparison.Ordinal))
                        continue;

                    var prefix = $"http://{address}:{port}/";
                    if (!prefixes.Contains(prefix))
                        prefixes.Add(prefix);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to enumerate IPv4 listeners: {ex.Message}");
        }

        return prefixes;
    }

    private static void ProcessMainThreadQueue()
    {
        int processed = 0;
        while (_mainThreadQueue.TryDequeue(out var action) && processed < 10)
        {
            try { action(); }
            catch (Exception ex) { GD.PrintErr($"[STS2 MCP] Main thread action error: {ex}"); }
            processed++;
        }
    }

    internal static Task<T> RunOnMainThread<T>(Func<T> func)
    {
        var tcs = new TaskCompletionSource<T>();
        _mainThreadQueue.Enqueue(() =>
        {
            try { tcs.SetResult(func()); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    internal static Task RunOnMainThread(Action action)
    {
        var tcs = new TaskCompletionSource<bool>();
        _mainThreadQueue.Enqueue(() =>
        {
            try { action(); tcs.SetResult(true); }
            catch (Exception ex) { tcs.SetException(ex); }
        });
        return tcs.Task;
    }

    private static void ServerLoop()
    {
        while (_listener?.IsListening == true)
        {
            try
            {
                var context = _listener.GetContext();
                // Handle each request asynchronously so we don't block the listener
                ThreadPool.QueueUserWorkItem(_ => HandleRequest(context));
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private static void HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            response.Headers.Add("Access-Control-Allow-Origin", "*");
            response.Headers.Add("Access-Control-Allow-Methods", "GET, POST, OPTIONS");
            response.Headers.Add("Access-Control-Allow-Headers", "Content-Type");

            if (request.HttpMethod == "OPTIONS")
            {
                response.StatusCode = 204;
                response.Close();
                return;
            }

            string path = request.Url?.AbsolutePath ?? "/";

            if (path == "/")
            {
                if (request.HttpMethod == "GET")
                {
                    var endpoints = BuildEndpointIndex();
                    SendJson(response, new
                    {
                        message = $"Hello from STS2 MCP v{Version}",
                        status = "ok",
                        kind = "api_index",
                        version = Version,
                        bound_prefixes = _boundPrefixes,
                        endpoint_count = endpoints.Count,
                        endpoints
                    });
                }
                else
                {
                    SendMethodNotAllowed(response);
                }
            }
            else if (path == "/api/v1/singleplayer")
            {
                if (request.HttpMethod != "GET" && request.HttpMethod != "POST")
                {
                    SendMethodNotAllowed(response);
                    return;
                }

                // Hard-block singleplayer endpoint during multiplayer runs
                // to prevent calling the non-sync-safe end_turn path
                if (IsMultiplayerRun())
                {
                    SendError(response, 409,
                        "Multiplayer run is active. Use /api/v1/multiplayer instead.",
                        "multiplayer_run_active");
                    return;
                }

                if (request.HttpMethod == "GET")
                    HandleGetState(request, response);
                else if (request.HttpMethod == "POST")
                    HandlePostAction(request, response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/multiplayer")
            {
                if (request.HttpMethod != "GET" && request.HttpMethod != "POST")
                {
                    SendMethodNotAllowed(response);
                    return;
                }

                // Guard: reject multiplayer endpoint during singleplayer runs
                if (!IsMultiplayerRun())
                {
                    SendError(response, 409,
                        "Not in a multiplayer run. Use /api/v1/singleplayer instead.",
                        "not_multiplayer_run");
                    return;
                }

                if (request.HttpMethod == "GET")
                    HandleGetMultiplayerState(request, response);
                else if (request.HttpMethod == "POST")
                    HandlePostMultiplayerAction(request, response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/settings")
            {
                if (request.HttpMethod == "GET")
                    HandleGetSettings(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/profiles")
            {
                if (request.HttpMethod == "GET")
                    HandleGetProfiles(response);
                else if (request.HttpMethod == "POST")
                    HandlePostProfiles(request, response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/profile")
            {
                if (request.HttpMethod == "GET")
                    HandleGetProfile(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/compendium")
            {
                if (request.HttpMethod == "GET")
                    HandleGetCompendium(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/bestiary")
            {
                if (request.HttpMethod == "GET")
                    HandleGetBestiary(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/glossary/cards")
            {
                if (request.HttpMethod == "GET")
                    HandleGetGlossaryCards(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/glossary/relics")
            {
                if (request.HttpMethod == "GET")
                    HandleGetGlossaryRelics(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/glossary/potions")
            {
                if (request.HttpMethod == "GET")
                    HandleGetGlossaryPotions(response);
                else
                    SendMethodNotAllowed(response);
            }
            else if (path == "/api/v1/glossary/keywords")
            {
                if (request.HttpMethod == "GET")
                    HandleGetGlossaryKeywords(response);
                else
                    SendMethodNotAllowed(response);
            }
            else
            {
                SendNotFound(response);
            }
        }
        catch (Exception ex)
        {
            try
            {
                SendError(context.Response, 500, $"Internal error: {ex.Message}", "internal_error");
            }
            catch { /* response may already be closed */ }
        }
    }

    private static List<Dictionary<string, object?>> BuildEndpointIndex()
    {
        return new List<Dictionary<string, object?>>
        {
            new() { ["method"] = "GET", ["path"] = "/api/v1/singleplayer", ["description"] = "Read singleplayer state with status/kind envelope and active-run context" },
            new() { ["method"] = "POST", ["path"] = "/api/v1/singleplayer", ["description"] = "Perform singleplayer action" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/multiplayer", ["description"] = "Read multiplayer state with status/kind envelope and active-run context" },
            new() { ["method"] = "POST", ["path"] = "/api/v1/multiplayer", ["description"] = "Perform multiplayer action" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/settings", ["description"] = "Read settings and preferences" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/profile", ["description"] = "Read active profile progress plus normalized save/run context" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/compendium", ["description"] = "Read Compendium-shaped profile progress plus normalized save/run context" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/bestiary", ["description"] = "Read monster and encounter metadata" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/glossary/cards", ["description"] = "Read active-run card pool metadata plus profile/save/run context" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/glossary/relics", ["description"] = "Read active-run relic pool metadata plus profile/save/run context" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/glossary/potions", ["description"] = "Read active-run potion pool metadata plus profile/save/run context" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/glossary/keywords", ["description"] = "Read active-run keyword metadata plus profile/save/run context" },
            new() { ["method"] = "GET", ["path"] = "/api/v1/profiles", ["description"] = "List profile slots plus normalized save context" },
            new() { ["method"] = "POST", ["path"] = "/api/v1/profiles", ["description"] = "Switch or delete profile slots" }
        };
    }

    // Called on HTTP thread (not main thread) as a best-effort guard.
    // The try/catch handles race conditions during run transitions.
    // Authoritative checks happen inside RunOnMainThread lambdas.
    internal static bool IsMultiplayerRun()
    {
        try
        {
            return MegaCrit.Sts2.Core.Runs.RunManager.Instance.IsInProgress
                && MegaCrit.Sts2.Core.Runs.RunManager.Instance.NetService.Type.IsMultiplayer();
        }
        catch { return false; }
    }

    private static void SendMethodNotAllowed(HttpListenerResponse response)
        => SendError(response, 405, "Method not allowed", "method_not_allowed");

    private static void SendNotFound(HttpListenerResponse response)
        => SendError(response, 404, "Not found", "not_found");

    private static void HandleGetMultiplayerState(HttpListenerRequest request, HttpListenerResponse response)
    {
        string format = request.QueryString["format"] ?? "json";
        if (!TryValidateStateFormat(response, format))
            return;

        try
        {
            var stateTask = RunOnMainThread(() => BuildMultiplayerGameState());
            var state = stateTask.GetAwaiter().GetResult();

            if (format == "markdown")
            {
                string md = FormatAsMarkdown(state);
                SendText(response, md, "text/markdown");
            }
            else
            {
                SendJson(response, state);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] HandleGetMultiplayerState: {ex}");
            try
            {
                SendError(response, 500, $"Failed to read multiplayer game state: {ex.Message}", "multiplayer_state_read_failed");
            }
            catch { /* response may be unusable */ }
        }
    }

    private static void HandlePostMultiplayerAction(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            body = reader.ReadToEnd();

        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
        }
        catch
        {
            SendError(response, 400, "Invalid JSON", "invalid_json");
            return;
        }

        if (parsed == null || !parsed.TryGetValue("action", out var actionElem))
        {
            SendError(response, 400, "Missing 'action' field", "missing_action");
            return;
        }
        if (actionElem.ValueKind != JsonValueKind.String)
        {
            SendError(response, 400, "'action' field must be a string", "invalid_action_type");
            return;
        }

        string action = actionElem.GetString() ?? "";

        // Menu actions (FTUE/popup dismissal, game-over, character select, etc.) are
        // scene-tree-driven and equally valid in MP. Route them to the shared handler
        // so MP clients can dismiss blocking FTUE prompts without going through the
        // run-mode-specific dispatcher.
        if (action == "menu_select")
        {
            try
            {
                var option = parsed.TryGetValue("option", out var optElem) ? optElem.GetString() ?? "" : "";
                var seed = parsed.TryGetValue("seed", out var seedElem) ? seedElem.GetString() : null;
                var resultTask = RunOnMainThread(() => ExecuteMenuSelect(option, seed));
                var result = resultTask.GetAwaiter().GetResult();
                SendActionResultJson(response, result);
            }
            catch (Exception ex)
            {
                SendActionError(response, "Menu action failed", ex);
            }
            return;
        }

        try
        {
            var resultTask = RunOnMainThread(() => ExecuteMultiplayerAction(action, parsed));
            var result = resultTask.GetAwaiter().GetResult();
            SendActionResultJson(response, result);
        }
        catch (Exception ex)
        {
            SendActionError(response, "Multiplayer action failed", ex);
        }
    }

    private static void HandleGetState(HttpListenerRequest request, HttpListenerResponse response)
    {
        string format = request.QueryString["format"] ?? "json";
        if (!TryValidateStateFormat(response, format))
            return;

        try
        {
            var stateTask = RunOnMainThread(() => BuildGameState());
            var state = stateTask.GetAwaiter().GetResult();

            if (format == "markdown")
            {
                try
                {
                    SendText(response, FormatAsMarkdown(state), "text/markdown");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[STS2 MCP] FormatAsMarkdown failed, returning JSON: {ex}");
                    SendJson(response, state);
                }
            }
            else
            {
                SendJson(response, state);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] HandleGetState: {ex}");
            try
            {
                SendError(response, 500, $"Failed to read game state: {ex.Message}", "singleplayer_state_read_failed");
            }
            catch { /* response may be unusable */ }
        }
    }

    private static bool TryValidateStateFormat(HttpListenerResponse response, string format)
    {
        if (format is "json" or "markdown")
            return true;

        SendError(response, 400, "Invalid format. Use: json, markdown", "invalid_format");
        return false;
    }

    private static void HandlePostAction(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body;
        using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
            body = reader.ReadToEnd();

        Dictionary<string, JsonElement>? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(body);
        }
        catch
        {
            SendError(response, 400, "Invalid JSON", "invalid_json");
            return;
        }

        if (parsed == null || !parsed.TryGetValue("action", out var actionElem))
        {
            SendError(response, 400, "Missing 'action' field", "missing_action");
            return;
        }
        if (actionElem.ValueKind != JsonValueKind.String)
        {
            SendError(response, 400, "'action' field must be a string", "invalid_action_type");
            return;
        }

        string action = actionElem.GetString() ?? "";

        // Handle menu actions separately (no run required)
        if (action == "menu_select")
        {
            try
            {
                var option = parsed.TryGetValue("option", out var optElem) ? optElem.GetString() ?? "" : "";
                var seed = parsed.TryGetValue("seed", out var seedElem) ? seedElem.GetString() : null;
                var resultTask = RunOnMainThread(() => ExecuteMenuSelect(option, seed));
                var result = resultTask.GetAwaiter().GetResult();
                SendActionResultJson(response, result);
            }
            catch (Exception ex)
            {
                SendActionError(response, "Menu action failed", ex);
            }
            return;
        }

        try
        {
            var resultTask = RunOnMainThread(() => ExecuteAction(action, parsed));
            var result = resultTask.GetAwaiter().GetResult();
            SendActionResultJson(response, result);
        }
        catch (Exception ex)
        {
            SendActionError(response, "Action failed", ex);
        }
    }

    private static void SendActionError(HttpListenerResponse response, string prefix, Exception ex)
    {
        var statusCode = ex is InvalidOperationException or FormatException or OverflowException
            ? 400
            : 500;
        var errorCode = statusCode == 400 ? "invalid_action_payload" : "action_failed";
        SendError(response, statusCode, $"{prefix}: {ex.Message}", errorCode);
    }

    private static void SendActionResultJson(HttpListenerResponse response, Dictionary<string, object?> result)
    {
        if (result.TryGetValue("status", out var status) && status as string == "error")
        {
            if (result.TryGetValue("error_code", out var errorCode))
            {
                response.StatusCode = (errorCode as string) switch
                {
                    "missing_menu_option" or "unknown_menu_option" or "unknown_action" or "unknown_multiplayer_action" => 400,
                    "not_on_menu" or "run_not_in_progress" or "not_multiplayer_run" or "blocking_popup_active" => 409,
                    "local_player_unavailable" => 409,
                    "timeline_manual_action_required" => 409,
                    _ => 400
                };
            }
            else
            {
                response.StatusCode = 400;
            }
        }
        SendJson(response, result);
    }
}
