using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using FFXProjectEditor.Diagnostics;
using Xunit;

namespace FFXProjectEditor.Tests.Diagnostics;

/// <summary>
/// DebugLog v2 — validates the test assembly build contract: Debug writes the requested
/// diagnostic marker and format, while Release accepts the calls without emitting it or
/// selecting a working-directory mirror that could mutate a shipped package.
/// </summary>
public class DebugLogTests
{
    private static string DevLogPath => Path.Combine(Directory.GetCurrentDirectory(), "work", "editor-debug.log");

    private static string ReadDevLog()
    {
        string p = DevLogPath;
        return File.Exists(p) ? File.ReadAllText(p) : string.Empty;
    }

    [Fact]
    public void Info_EscritaEmInglesNaoLanca()
    {
        using var state = DebugLogTestStateScope.Enter();
        string marker = $"debuglog-test-info-{Guid.NewGuid():N}";
        DebugLog.Activate(state.Area);
        DebugLog.Info(state.Area, marker);
        string content = ReadDevLog();
        AssertMarkerMatchesBuildConfiguration(marker, content);
    }

    [Fact]
    public void Format_ContemTimestampAreaETipo()
    {
        using var state = DebugLogTestStateScope.Enter();
        string marker = $"debuglog-test-warn-{Guid.NewGuid():N}";
        DebugLog.Activate(state.Area);
        DebugLog.Warn(state.Area, marker);
        string content = ReadDevLog();
#if DEBUG
        Assert.Contains(marker, content);
        Assert.Contains($"[{state.Area}][WARN]", content);
        Assert.Matches("\\d{4}-\\d{2}-\\d{2}", content);
#else
        Assert.DoesNotContain(marker, content);
#endif
    }

    [Fact]
    public void Error_ComExcecaoIncluiTipo()
    {
        using var state = DebugLogTestStateScope.Enter();
        string marker = $"debuglog-test-err-{Guid.NewGuid():N}";
        DebugLog.Activate(state.Area);
        DebugLog.Error(state.Area, marker, new InvalidOperationException("boom"));
        string content = ReadDevLog();
#if DEBUG
        Assert.Contains(marker, content);
        Assert.Contains("InvalidOperationException", content);
#else
        Assert.DoesNotContain(marker, content);
#endif
    }

    [Fact]
    public void WorkingDirectoryMirror_MatchesBuildConfiguration()
    {
        string? devLogFile = typeof(DebugLog)
            .GetField("_devLogFile", BindingFlags.Static | BindingFlags.NonPublic)?
            .GetValue(null) as string;
#if DEBUG
        Assert.Equal(DevLogPath, devLogFile);
#else
        Assert.Null(devLogFile);
#endif
    }

    [Theory]
    [InlineData(false, null, null, false)]
    [InlineData(true, null, null, true)]
    [InlineData(false, null, "dev", true)]
    [InlineData(true, null, "prod", false)]
    [InlineData(false, "prod", "dev", true)]
    [InlineData(true, "dev", "prod", false)]
    public void ResolveDevMode_EnvironmentOverridesBuildAndOptionalConfig(
        bool isDebugBuild,
        string? configMode,
        string? environmentMode,
        bool expected)
    {
        Assert.Equal(expected, DebugLog.ResolveDevMode(isDebugBuild, configMode, environmentMode));
    }

    private static void AssertMarkerMatchesBuildConfiguration(string marker, string content)
    {
#if DEBUG
        Assert.Contains(marker, content);
#else
        Assert.DoesNotContain(marker, content);
#endif
    }

    /// <summary>
    /// Holds DebugLog's own gate while a test installs a unique area-local state. This keeps
    /// supported environment and JSON overrides observable to production only, never to these
    /// build-configuration tests, and restores every touched member before releasing the gate.
    /// </summary>
    private sealed class DebugLogTestStateScope : IDisposable
    {
        private readonly object _gate;
        private readonly PropertyInfo _isDevModeProperty;
        private readonly Dictionary<string, int> _flags;
        private readonly HashSet<string> _activeAreas;
        private readonly bool _previousIsDevMode;
        private readonly bool _hadAreaFlag;
        private readonly int _previousAreaLevel;
        private readonly bool _wasAreaActive;
        private bool _disposed;

        private DebugLogTestStateScope()
        {
            _gate = RequireStaticValue<object>(RequireStaticField("_gate"));
            Monitor.Enter(_gate);
            try
            {
                AssertProductBuildMatchesTestConfiguration();
                _isDevModeProperty = RequireIsDevModeProperty();
                _flags = RequireStaticValue<Dictionary<string, int>>(RequireStaticField("_flags"));
                _activeAreas = RequireStaticValue<HashSet<string>>(RequireStaticField("_activeAreas"));
                _previousIsDevMode = RequireStaticValue<bool>(_isDevModeProperty.GetMethod!);

                Area = CreateUniqueArea(_flags, _activeAreas);
                _hadAreaFlag = _flags.TryGetValue(Area, out _previousAreaLevel);
                _wasAreaActive = _activeAreas.Contains(Area);

                _isDevModeProperty.SetValue(null, ExpectedDebugBuild);
                _flags[Area] = 1;
            }
            catch
            {
                Monitor.Exit(_gate);
                throw;
            }
        }

        internal string Area { get; }

        internal static DebugLogTestStateScope Enter() => new();

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;
            try
            {
                _isDevModeProperty.SetValue(null, _previousIsDevMode);
            }
            finally
            {
                try
                {
                    if (_hadAreaFlag)
                        _flags[Area] = _previousAreaLevel;
                    else
                        _flags.Remove(Area);
                }
                finally
                {
                    try
                    {
                        if (_wasAreaActive)
                            _activeAreas.Add(Area);
                        else
                            _activeAreas.Remove(Area);
                    }
                    finally
                    {
                        Monitor.Exit(_gate);
                    }
                }
            }
        }

        private static string CreateUniqueArea(
            Dictionary<string, int> flags,
            HashSet<string> activeAreas)
        {
            string area;
            do
            {
                area = $"debuglog-test-area-{Guid.NewGuid():N}";
            }
            while (flags.ContainsKey(area) || activeAreas.Contains(area));

            return area;
        }

        private static void AssertProductBuildMatchesTestConfiguration()
        {
            bool productIsDebugBuild = RequireStaticValue<bool>(RequireStaticField("_isDebugBuild"));
            Assert.Equal(ExpectedDebugBuild, productIsDebugBuild);
        }

        private static FieldInfo RequireStaticField(string name)
        {
            return typeof(DebugLog).GetField(name, BindingFlags.Static | BindingFlags.NonPublic)
                ?? throw new Xunit.Sdk.XunitException($"DebugLog reflection member '{name}' was not found.");
        }

        private static PropertyInfo RequireIsDevModeProperty()
        {
            PropertyInfo property = typeof(DebugLog).GetProperty(
                    nameof(DebugLog.IsDevMode),
                    BindingFlags.Static | BindingFlags.Public)
                ?? throw new Xunit.Sdk.XunitException("DebugLog reflection property 'IsDevMode' was not found.");
            if (property.PropertyType != typeof(bool) || property.GetMethod == null ||
                property.GetSetMethod(nonPublic: true) == null)
            {
                throw new Xunit.Sdk.XunitException(
                    "DebugLog reflection property 'IsDevMode' does not expose the expected bool getter and private setter.");
            }

            return property;
        }

        private static T RequireStaticValue<T>(FieldInfo field)
        {
            object? value = field.GetValue(null);
            return value is T typed
                ? typed
                : throw new Xunit.Sdk.XunitException(
                    $"DebugLog reflection field '{field.Name}' did not provide {typeof(T).FullName}.");
        }

        private static T RequireStaticValue<T>(MethodInfo getter)
        {
            object? value = getter.Invoke(null, null);
            return value is T typed
                ? typed
                : throw new Xunit.Sdk.XunitException(
                    $"DebugLog reflection getter '{getter.Name}' did not provide {typeof(T).FullName}.");
        }
    }

    private static bool ExpectedDebugBuild
    {
        get
        {
#if DEBUG
            return true;
#else
            return false;
#endif
        }
    }
}
