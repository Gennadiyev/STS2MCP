using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
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

    private static HttpListener? _listener;
    private static Thread? _serverThread;
    private static LocalApiAccess? _apiAccess;
    private static bool _allowProfileDelete;
    private static readonly MainThreadWorkQueue _mainThreadQueue = new(TimeSpan.FromSeconds(5), TimeProvider.System);
    internal static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
    private static readonly JsonSerializerOptions _compactJsonOptions = new(_jsonOptions) { WriteIndented = false };

    private static (int Port, bool AllowProfileDelete) LoadConfig()
    {
        try
        {
            string? modDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location);
            if (modDir == null) return (DefaultPort, false);

            string configPath = Path.Combine(modDir, ConfigFileName);
            if (!File.Exists(configPath))
            {
                try
                {
                    var defaultConfig = new Dictionary<string, object>
                    {
                        ["port"] = DefaultPort,
                        ["allow_profile_delete"] = false
                    };
                    string json = JsonSerializer.Serialize(defaultConfig, _jsonOptions);
                    File.WriteAllText(configPath, json);
                    GD.Print($"[STS2 MCP] Created default config at {configPath}");
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
                {
                    GD.Print($"[STS2 MCP] No config found at {configPath}; using default port {DefaultPort}");
                }
                return (DefaultPort, false);
            }

            string content = File.ReadAllText(configPath);
            using var doc = JsonDocument.Parse(content);
            bool allowProfileDelete = doc.RootElement.TryGetProperty("allow_profile_delete", out var deleteElem)
                && deleteElem.ValueKind == JsonValueKind.True;
            if (doc.RootElement.TryGetProperty("port", out var portElem)
                && portElem.TryGetInt32(out int port)
                && port is > 0 and <= 65535)
            {
                return (port, allowProfileDelete);
            }

            GD.PrintErr($"[STS2 MCP] Invalid or missing 'port' in {configPath}, using default {DefaultPort}");
            return (DefaultPort, allowProfileDelete);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[STS2 MCP] Failed to load config: {ex.Message}, using default port {DefaultPort}");
            return (DefaultPort, false);
        }
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

            var (port, allowProfileDelete) = LoadConfig();
            _allowProfileDelete = allowProfileDelete;
            string userDataDirectory = Godot.OS.GetUserDataDir();
            _apiAccess = LocalApiAccess.LoadOrCreate(userDataDirectory);

            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://localhost:{port}/");
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();

            _serverThread = new Thread(ServerLoop)
            {
                IsBackground = true,
                Name = "STS2_MCP_Server"
            };
            _serverThread.Start();

            GD.Print($"[STS2 MCP] v{Version} server started on http://localhost:{port}/");
            GD.Print($"[STS2 MCP] API token file: {Path.Combine(userDataDirectory, LocalApiAccess.TokenFileName)}");
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

    private static void ProcessMainThreadQueue() => _mainThreadQueue.ProcessFrame(10);

    internal static Task<T> RunOnMainThread<T>(Func<T> func) => _mainThreadQueue.Enqueue(func);

    private static void ServerLoop()
    {
        while (_listener?.IsListening == true)
        {
            try
            {
                var context = _listener.GetContext();
                // Handle each request asynchronously so we don't block the listener
                ThreadPool.QueueUserWorkItem(static ctx => _ = HandleRequest(ctx), context, preferLocal: false);
            }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
        }
    }

    private static async Task HandleRequest(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;
            string path = request.Url?.AbsolutePath ?? "/";
            bool isBrowserRequest = request.Headers["Origin"] != null || request.Headers["Sec-Fetch-Site"] != null;
            int? rejection = (_apiAccess ?? throw new InvalidOperationException("Local API access is unavailable."))
                .GetRejectionStatus(
                    request.RemoteEndPoint?.Address, isBrowserRequest, request.HttpMethod, path,
                    request.Headers["Authorization"]);

            if (rejection == 403)
            {
                SendError(response, 403, "Only native clients on the local machine can use this API.");
                return;
            }

            if (rejection == 401)
            {
                response.Headers["WWW-Authenticate"] = "Bearer";
                SendError(response, 401, "A valid bearer token is required.");
                return;
            }

            if (path == "/")
            {
                if (request.HttpMethod == "GET")
                    SendJson(response, new { message = $"Hello from STS2 MCP v{Version}", status = "ok" });
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/singleplayer")
            {
                // Hard-block singleplayer endpoint during multiplayer runs
                // to prevent calling the non-sync-safe end_turn path
                if (IsMultiplayerRun())
                {
                    SendActionResult(response, BlockedAction(RunModeConflict(isMultiplayer: true)));
                    return;
                }

                if (request.HttpMethod == "GET")
                    await HandleGetState(request, response).ConfigureAwait(false);
                else if (request.HttpMethod == "POST")
                    await HandlePostAction(request, response).ConfigureAwait(false);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/multiplayer")
            {
                // Guard: reject multiplayer endpoint during singleplayer runs
                if (!IsMultiplayerRun())
                {
                    SendActionResult(response, BlockedAction(RunModeConflict(isMultiplayer: false)));
                    return;
                }

                if (request.HttpMethod == "GET")
                    HandleGetMultiplayerState(request, response);
                else if (request.HttpMethod == "POST")
                    await HandlePostMultiplayerAction(request, response).ConfigureAwait(false);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/profiles")
            {
                if (request.HttpMethod == "GET")
                    HandleGetProfiles(response);
                else if (request.HttpMethod == "POST")
                    HandlePostProfiles(request, response);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/profile")
            {
                if (request.HttpMethod == "GET")
                    HandleGetProfile(response);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/compendium")
            {
                if (request.HttpMethod == "GET")
                    HandleGetCompendium(response);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/wiki")
            {
                if (request.HttpMethod == "GET")
                    HandleGetWiki(request, response);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else if (path == "/api/v1/chinese-names")
            {
                if (request.HttpMethod == "GET")
                    HandleGetChineseNames(request, response);
                else
                    SendError(response, 405, "Method not allowed");
            }
            else
            {
                SendError(response, 404, "Not found");
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (ex is MainThreadQueueTimeoutException)
                    SendError(context.Response, 503, ex.Message);
                else
                    SendError(context.Response, 500, $"Internal error: {ex.Message}");
            }
            catch { /* response may already be closed */ }
        }
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

    private static ActionBlock RunModeConflict(bool isMultiplayer) => new("run_mode_conflict",
        isMultiplayer ? "Multiplayer run is active. Use /api/v1/multiplayer instead." :
            "Not in a multiplayer run. Use /api/v1/singleplayer instead.", false);

    private static void SendActionResult(HttpListenerResponse response, Dictionary<string, object?> result, bool compact = false)
    {
        if (result.GetValueOrDefault("reason") is "run_mode_conflict" ||
            (result.GetValueOrDefault("stop_reason") is "run_mode_conflict" &&
             result.GetValueOrDefault("submitted") is List<string> { Count: 0 }))
            response.StatusCode = (int)HttpStatusCode.Conflict;
        SendJson(response, result, compact);
    }

    private static void HandleGetMultiplayerState(HttpListenerRequest request, HttpListenerResponse response)
    {
        string format = request.QueryString["format"] ?? "json";

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
        catch (Exception ex) when (ex is not MainThreadQueueTimeoutException)
        {
            GD.PrintErr($"[STS2 MCP] HandleGetMultiplayerState: {ex}");
            try
            {
                response.StatusCode = 500;
                SendJson(response, new Dictionary<string, object?>
                {
                    ["error"] = $"Failed to read multiplayer game state: {ex.Message}",
                    ["exception_type"] = ex.GetType().FullName,
                    ["stack_trace"] = ex.StackTrace
                });
            }
            catch { /* response may be unusable */ }
        }
    }

    private static async Task HandlePostMultiplayerAction(HttpListenerRequest request, HttpListenerResponse response)
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
            SendError(response, 400, "Invalid JSON");
            return;
        }

        if (parsed == null || !parsed.TryGetValue("action", out var actionElem))
        {
            SendError(response, 400, "Missing 'action' field");
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
                var result = await RunOnMainThread(() => ExecuteMenuSelect(option, seed)).ConfigureAwait(false);
                SendJson(response, result);
            }
            catch (Exception ex) when (ex is not MainThreadQueueTimeoutException)
            {
                SendError(response, 500, $"Menu action failed: {ex.Message}");
            }
            return;
        }

        try
        {
            var completion = await RunOnMainThread(() => ExecuteMultiplayerAction(action, parsed)).ConfigureAwait(false);
            try
            {
                var result = await completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
                SendActionResult(response, result);
            }
            catch (TimeoutException) when (!completion.IsCompleted)
            {
                SendJson(response, BlockedAction(new("action_pending",
                    "The multiplayer action has not finished. Check fresh game state before retrying.", false)));
            }
        }
        catch (Exception ex) when (ex is not MainThreadQueueTimeoutException)
        {
            SendError(response, 500, $"Multiplayer action failed: {ex.Message}");
        }
    }

    private static async Task HandleGetState(HttpListenerRequest request, HttpListenerResponse response)
    {
        string format = request.QueryString["format"] ?? "json";
        bool compact = bool.TryParse(request.QueryString["compact"], out bool compactValue) && compactValue;
        bool wait = bool.TryParse(request.QueryString["wait_for_ready"], out bool waitValue) && waitValue;

        try
        {
            var state = wait
                ? (await ReadReadyState(compact).ConfigureAwait(false)).State
                : await RunOnMainThread(() => BuildGameState(compact)).ConfigureAwait(false);

            if (format == "markdown")
            {
                try
                {
                    SendText(response, FormatAsMarkdown(state), "text/markdown");
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[STS2 MCP] FormatAsMarkdown failed, returning JSON: {ex}");
                    SendJson(response, state, compact);
                }
            }
            else
            {
                SendJson(response, state, compact);
            }
        }
        catch (Exception ex) when (ex is not MainThreadQueueTimeoutException)
        {
            GD.PrintErr($"[STS2 MCP] HandleGetState: {ex}");
            try
            {
                response.StatusCode = 500;
                SendJson(response, new Dictionary<string, object?>
                {
                    ["error"] = $"Failed to read game state: {ex.Message}",
                    ["exception_type"] = ex.GetType().FullName,
                    ["stack_trace"] = ex.StackTrace
                });
            }
            catch { /* response may be unusable */ }
        }
    }

    private static async Task HandlePostAction(HttpListenerRequest request, HttpListenerResponse response)
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
            SendError(response, 400, "Invalid JSON");
            return;
        }

        if (parsed == null || !parsed.TryGetValue("action", out var actionElem))
        {
            SendError(response, 400, "Missing 'action' field");
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
                var result = await RunOnMainThread(() => ExecuteMenuSelect(option, seed)).ConfigureAwait(false);
                SendJson(response, result);
            }
            catch (Exception ex) when (ex is not MainThreadQueueTimeoutException)
            {
                SendError(response, 500, $"Menu action failed: {ex.Message}");
            }
            return;
        }

        try
        {
            if (action == "play_card_sequence")
            {
                SendActionResult(response, await ExecuteCardSequence(parsed).ConfigureAwait(false), compact: true);
                return;
            }

            var wait = (action is "play_card" or "end_turn" or "choose_map_node" or "combat_confirm_selection") &&
                parsed.TryGetValue("return_state", out var returnState) && returnState.ValueKind == JsonValueKind.True
                ? new CombatStateWait() : null;
            var result = await RunOnMainThread(() => ExecuteAction(action, parsed, wait)).ConfigureAwait(false);
            if (wait != null && result.GetValueOrDefault("status") is "ok")
            {
                try
                {
                    var ready = await ReadReadyState(compact: true, action: wait).ConfigureAwait(false);
                    result["state"] = ready.State;
                    if (ready.TimedOut)
                        result["state_wait_timed_out"] = true;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[STS2 MCP] Action succeeded but follow-up state failed: {ex}");
                    result["state_error"] = "The action was accepted. Refresh game state to check the result.";
                }
            }
            SendActionResult(response, result, compact: wait != null);
        }
        catch (Exception ex) when (ex is not MainThreadQueueTimeoutException)
        {
            SendError(response, 500, $"Action failed: {ex.Message}");
        }
    }
}
