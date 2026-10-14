using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;

namespace FFXProjectEditor.Diagnostics;

/// <summary>
/// DEBUG LOG v2 - REGRA GERAL: TODO modulo do editor DEVE ter resposta de debug.
/// LOG EM INGLES SEMPRE (fora da i18n - o diagnostico e monolingue EN, como os hooks C++).
///
/// MODELO DE FLAGS:
///   GLOBAL (a flag global diz o MODELO): auto (Debug build -> dev, Release -> prod) | dev | prod.
///     FFX_DEBUG_MODE=dev|prod forca (um Release problematico vira dev sem rebuild).
///   POR AREA (debug_flags.json): 0=OFF | 1=ACTIVE (apita so quando manipula a area) |
///     2=ALWAYS (apita sempre que ha erro, independente de manipular - respeita o global: no produto
///     so a flag 3 apita) | 3=FORCEDEBUG (apita MESMO no produto - bugs raros de producao).
///
/// SAIDA: debugger + %LOCALAPPDATA%\\FFXProjectEditor\\debug.log; work/editor-debug.log exists only in Debug builds.
/// ROTACAO: 5MB -> .old1..old9 (mesma convencao da lane C++ do hook).
/// </summary>
public static class DebugLog
{
    private static readonly bool _isDebugBuild;
    private static readonly string? _envMode;
    public static bool IsDevMode { get; private set; }

    private static readonly Dictionary<string, int> _flags = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _activeAreas = new(StringComparer.OrdinalIgnoreCase);

    private static readonly object _gate = new();
    private static readonly string _logFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "debug.log");
    private static readonly string _flagsFile = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "FFXProjectEditor", "debug_flags.json");
    private static string? _devLogFile;
    private static string? _devFlagsFile;
    private const long MaxLogBytes = 5 * 1024 * 1024;
    private const int MaxOldLogs = 9;

    static DebugLog()
    {
#if DEBUG
        _isDebugBuild = true;
#else
        _isDebugBuild = false;
#endif
        _envMode = Environment.GetEnvironmentVariable("FFX_DEBUG_MODE");
        IsDevMode = _isDebugBuild;
        try
        {
            _devFlagsFile = Path.Combine(Directory.GetCurrentDirectory(), "work", "debug_flags.json");
            LoadConfig();
            // A Release executable may enable verbose diagnostics through config, but its install/package
            // directory remains immutable. The cwd mirror is therefore a compile-time Debug-only sink;
            // forced Release diagnostics continue to use the app-owned LocalAppData sink exclusively.
            if (_isDebugBuild)
            {
                _devLogFile = Path.Combine(Directory.GetCurrentDirectory(), "work", "editor-debug.log");
                Directory.CreateDirectory(Path.GetDirectoryName(_devLogFile)!);
            }
            if (IsDevMode || HasForceDebugFlag())
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logFile)!);
                Write("=== DebugLog v2: mode=" + (IsDevMode ? "dev" : "prod") + (IsDevMode != _isDebugBuild ? " (forced)" : "") + " ===");
            }
        }
        catch { }
    }

    private static bool HasForceDebugFlag()
    {
        foreach (int level in _flags.Values)
        {
            if (level >= 3) return true;
        }
        return false;
    }

    private static void LoadConfig()
    {
        string? file = null;
        if (File.Exists(_flagsFile)) file = _flagsFile;
        else if (_devFlagsFile != null && File.Exists(_devFlagsFile)) file = _devFlagsFile;
        string? configMode = null;
        if (file != null)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;
                if (root.TryGetProperty("mode", out var modeEl) && modeEl.ValueKind == JsonValueKind.String)
                    configMode = modeEl.GetString();
                if (root.TryGetProperty("flags", out var flagsEl) && flagsEl.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in flagsEl.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number)
                            _flags[prop.Name] = prop.Value.GetInt32();
                        else if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int v))
                            _flags[prop.Name] = v;
                    }
                }
            }
            catch { }
        }

        // Environment mode is an independent last-mile diagnostic control. It must remain effective
        // on a clean installation where no debug_flags.json exists, and it deliberately wins over
        // the optional file so support can override a stale local configuration without modifying it.
        IsDevMode = ResolveDevMode(_isDebugBuild, configMode, _envMode);
        ApplyDefault();
    }

    internal static bool ResolveDevMode(bool isDebugBuild, string? configMode, string? envMode)
    {
        bool isDevMode = isDebugBuild;
        if (configMode != null)
        {
            if (configMode.Equals("dev", StringComparison.OrdinalIgnoreCase)) isDevMode = true;
            else if (configMode.Equals("prod", StringComparison.OrdinalIgnoreCase)) isDevMode = false;
        }

        if (envMode != null)
        {
            if (envMode.Equals("dev", StringComparison.OrdinalIgnoreCase)) isDevMode = true;
            else if (envMode.Equals("prod", StringComparison.OrdinalIgnoreCase)) isDevMode = false;
        }

        return isDevMode;
    }

    private static void ApplyDefault()
    {
        if (!_flags.ContainsKey("*"))
            _flags["*"] = IsDevMode ? 1 : 0;
    }

    public static void Activate(string area) { lock (_gate) _activeAreas.Add(area); }
    public static void Deactivate(string area) { lock (_gate) _activeAreas.Remove(area); }

    /// <summary>True if the exact area OR any dotted prefix of it is currently active. The module router activates the
    /// module id (e.g. "aurora") in SetModule, while the domain code logs with dotted sub-areas ("Aurora.Render",
    /// "Hub.BuildUrl", …). Without prefix matching those logs are silently dropped in dev (level==1 requires active)
    /// and — as we saw 2026-08-15 — the RealGame/EditViewer debugging was blind. Prefix matching keeps the module-scoped
    /// contract (flag 1 = "logs only while this area is being handled") but lets the dotted child areas inherit it.</summary>
    public static bool IsActive(string area)
    {
        lock (_gate)
        {
            for (int i = area.Length; i > 0; i--)
            {
                if (area[i - 1] == '.')
                {
                    if (_activeAreas.Contains(area[..i])) return true;
                }
            }
            return _activeAreas.Contains(area);
        }
    }

    private static int Level(string area)
    {
        if (_flags.TryGetValue(area, out int l)) return l;
        if (_flags.TryGetValue("*", out int d)) return d;
        return _isDebugBuild ? 1 : 0;
    }

    private static bool ShouldLog(string area)
    {
        try
        {
            int level = Level(area);
            if (level <= 0) return false;
            if (!IsDevMode && level < 3) return false;
            if (level == 1 && !IsActive(area)) return false;
            return true;
        }
        catch { return false; }
    }

    public static void Info(string area, string message, [CallerMemberName] string? caller = null)
    { if (ShouldLog(area)) Write($"[{area}] ({caller}) {message}"); }

    public static void Warn(string area, string message, [CallerMemberName] string? caller = null)
    { if (ShouldLog(area)) Write($"[{area}][WARN] ({caller}) {message}"); }

    public static void Error(string area, string message, Exception? ex = null, [CallerMemberName] string? caller = null)
    { if (ShouldLog(area)) Write($"[{area}][ERROR] ({caller}) {message}" + (ex != null ? $" :: {ex.GetType().Name}: {ex.Message}" : "")); }

    private static void Write(string line)
    {
        string full = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} {line}";
        lock (_gate)
        {
            try { Debug.WriteLine(full); } catch { }
            try
            {
                RotateIfNeeded();
                File.AppendAllText(_logFile, full + Environment.NewLine);
                if (_devLogFile != null)
                    File.AppendAllText(_devLogFile, full + Environment.NewLine);
            }
            catch { }
        }
    }

    private static void RotateIfNeeded()
    {
        try
        {
            var fi = new FileInfo(_logFile);
            if (!fi.Exists || fi.Length < MaxLogBytes) return;
            string drop = _logFile + $".old{MaxOldLogs}";
            if (File.Exists(drop)) File.Delete(drop);
            for (int i = MaxOldLogs - 1; i >= 1; i--)
            {
                string src = _logFile + $".old{i}";
                if (File.Exists(src)) File.Move(src, _logFile + $".old{i + 1}", overwrite: true);
            }
            if (File.Exists(_logFile)) File.Move(_logFile, _logFile + ".old1", overwrite: true);
        }
        catch { }
    }
}
