using System;

namespace ErenshorSkills
{
    /// <summary>
    /// Lightweight compatibility view over a property in the native Lunaris
    /// settings object. This is intentionally not a BepInEx ConfigEntry.
    /// </summary>
    internal sealed class ConfigEntry<T>
    {
        private readonly Func<T> _read;
        private readonly Action<T> _write;

        internal ConfigEntry(string key, Func<T> read, Action<T> write)
        {
            if (string.IsNullOrWhiteSpace(key)) throw new ArgumentException("Config key is required.", nameof(key));
            _read = read ?? throw new ArgumentNullException(nameof(read));
            _write = write ?? throw new ArgumentNullException(nameof(write));
            Definition = new ConfigDefinition(key);
        }

        internal ConfigDefinition Definition { get; }

        internal T Value
        {
            get => _read();
            set => _write(value);
        }
    }

    internal sealed class ConfigDefinition
    {
        internal ConfigDefinition(string key) { Key = key; }
        internal string Key { get; }
    }

    /// <summary>Adapts the native Lunaris logger to the established mod callsites.</summary>
    internal sealed class ModLogger
    {
        private readonly Lunaris.ILog _logger;
        internal ModLogger(Lunaris.ILog logger)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }
        internal void LogInfo(string message) => _logger.LogInfo(message ?? string.Empty);
        internal void LogWarning(string message) => _logger.LogWarning(message ?? string.Empty);
        internal void LogError(string message) => _logger.LogError(message ?? string.Empty);
        internal void LogDebug(string message) => _logger.LogDebug(message ?? string.Empty);
    }
}
