using System;
using System.Collections.Generic;
using System.Globalization;
using GitTabSync.Sync;

namespace GitTabSync.Settings
{
    /// <summary>
    /// Resolves a setting for one repository by walking a <see cref="SyncContext.ScopeChain"/> from
    /// the narrowest scope outwards, and stores overrides back.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The stored overrides are held in memory and consulted per decision, rather than being read
    /// from disk each time or collapsed into a flat answer once. Reading per decision would put
    /// file I/O in the branch-switch path; collapsing once would defeat the point of per-branch
    /// settings, since the answer is only true for the branch it was computed on.
    /// </para>
    /// <para>
    /// Global scopes are read from and written to the defaults document; every other scope belongs
    /// to the repository's own document. Scopes found in the wrong document are ignored, so a
    /// hand-edited file cannot make one repository's branch override apply to all of them.
    /// </para>
    /// </remarks>
    public sealed class SettingsResolver : ISyncSettings
    {
        private readonly ISettingsStore _store;
        private readonly string _repositoryWorkingDirectory;
        private readonly ITabSyncLog _log;
        private readonly object _gate = new object();

        private Dictionary<SettingScope, Dictionary<SyncSetting, bool>> _overrides;

        public SettingsResolver(
            ISettingsStore store,
            string repositoryWorkingDirectory,
            ITabSyncLog? log = null)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _repositoryWorkingDirectory = repositoryWorkingDirectory
                ?? throw new ArgumentNullException(nameof(repositoryWorkingDirectory));
            _log = log ?? NullTabSyncLog.Instance;
            _overrides = Read();
        }

        /// <summary>Re-reads both documents, discarding anything held in memory.</summary>
        public void Reload()
        {
            var read = Read();

            lock (_gate)
            {
                _overrides = read;
            }
        }

        public bool IsEnabled(SyncSetting setting, SyncContext context) => Resolve(setting, context).Value;

        public ResolvedSetting Resolve(SyncSetting setting, SyncContext context)
        {
            if (context is null)
            {
                throw new ArgumentNullException(nameof(context));
            }

            lock (_gate)
            {
                foreach (var scope in context.ScopeChain)
                {
                    if (_overrides.TryGetValue(scope, out var values)
                        && values.TryGetValue(setting, out var value))
                    {
                        return new ResolvedSetting(setting, value, scope, isExplicit: true);
                    }
                }
            }

            return new ResolvedSetting(
                setting,
                SyncSettingCatalog.DefaultFor(setting),
                SettingScope.Global,
                isExplicit: false);
        }

        /// <summary>
        /// What is set at exactly this scope, or <c>null</c> when nothing is — the "inherit" state.
        /// </summary>
        public bool? GetOverride(SettingScope scope, SyncSetting setting)
        {
            if (scope is null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            lock (_gate)
            {
                return _overrides.TryGetValue(scope, out var values) && values.TryGetValue(setting, out var value)
                    ? value
                    : (bool?)null;
            }
        }

        /// <summary>
        /// Sets a setting at one scope, or clears it when <paramref name="value"/> is <c>null</c>
        /// so the scope inherits again.
        /// </summary>
        public void Set(SettingScope scope, SyncSetting setting, bool? value)
        {
            if (scope is null)
            {
                throw new ArgumentNullException(nameof(scope));
            }

            ScopedSettings document;

            lock (_gate)
            {
                if (value is null)
                {
                    if (_overrides.TryGetValue(scope, out var existing))
                    {
                        existing.Remove(setting);

                        // Do not leave an empty scope behind; it would accumulate in the file and
                        // show up as an override in the UI that says nothing.
                        if (existing.Count == 0)
                        {
                            _overrides.Remove(scope);
                        }
                    }
                }
                else
                {
                    if (!_overrides.TryGetValue(scope, out var values))
                    {
                        values = new Dictionary<SyncSetting, bool>();
                        _overrides[scope] = values;
                    }

                    values[setting] = value.Value;
                }

                document = BuildDocument(globalScopes: scope.Kind == SettingScopeKind.Global);
            }

            try
            {
                if (scope.Kind == SettingScopeKind.Global)
                {
                    _store.SaveDefaults(document);
                }
                else
                {
                    _store.Save(_repositoryWorkingDirectory, document);
                }

                _log.Info(string.Format(
                    CultureInfo.InvariantCulture,
                    "{0} is now {1} at {2}.",
                    SyncSettingCatalog.NameOf(setting),
                    value is null ? "inherited" : value.Value ? "on" : "off",
                    scope));
            }
            catch (Exception e)
            {
                // In-memory state still reflects the change, so the current session behaves as the
                // user asked; only the persistence was lost.
                _log.Error("Failed to save settings.", e);
            }
        }

        /// <summary>
        /// Every stored override, narrowest scope last, for a UI that lists what has been set.
        /// </summary>
        public IReadOnlyList<SettingOverrideInfo> Overrides
        {
            get
            {
                var result = new List<SettingOverrideInfo>();

                lock (_gate)
                {
                    foreach (var pair in _overrides)
                    {
                        foreach (var setting in SyncSettingCatalog.All)
                        {
                            if (pair.Value.TryGetValue(setting, out var value))
                            {
                                result.Add(new SettingOverrideInfo(pair.Key, setting, value));
                            }
                        }
                    }
                }

                result.Sort(CompareOverrides);
                return result;
            }
        }

        private static int CompareOverrides(SettingOverrideInfo a, SettingOverrideInfo b)
        {
            var byKind = ((int)a.Scope.Kind).CompareTo((int)b.Scope.Kind);
            if (byKind != 0)
            {
                return byKind;
            }

            var byKey = string.CompareOrdinal(a.Scope.Key, b.Scope.Key);
            return byKey != 0 ? byKey : ((int)a.Setting).CompareTo((int)b.Setting);
        }

        private Dictionary<SettingScope, Dictionary<SyncSetting, bool>> Read()
        {
            var result = new Dictionary<SettingScope, Dictionary<SyncSetting, bool>>();

            Merge(result, _store.LoadDefaults(), globalScopes: true);
            Merge(result, _store.Load(_repositoryWorkingDirectory), globalScopes: false);

            return result;
        }

        private static void Merge(
            Dictionary<SettingScope, Dictionary<SyncSetting, bool>> target,
            ScopedSettings? document,
            bool globalScopes)
        {
            if (document?.Scopes is null)
            {
                return;
            }

            foreach (var stored in document.Scopes)
            {
                if (stored?.Values is null || !TryReadScope(stored, out var scope))
                {
                    // An unrecognised kind means a file written by a newer build. Skipping the
                    // scope loses one override; guessing at it would apply a setting somewhere the
                    // user never asked for.
                    continue;
                }

                if ((scope!.Kind == SettingScopeKind.Global) != globalScopes)
                {
                    continue;
                }

                foreach (var value in stored.Values)
                {
                    if (value is null || !SyncSettingCatalog.TryParse(value.Setting, out var setting))
                    {
                        continue;
                    }

                    if (!target.TryGetValue(scope, out var values))
                    {
                        values = new Dictionary<SyncSetting, bool>();
                        target[scope] = values;
                    }

                    values[setting] = value.On;
                }
            }
        }

        private static bool TryReadScope(ScopeOverrides stored, out SettingScope? scope)
        {
            scope = null;

            if (!Enum.TryParse<SettingScopeKind>(stored.Kind, ignoreCase: false, out var kind)
                || !Enum.IsDefined(typeof(SettingScopeKind), kind))
            {
                // TryParse also accepts the numeric form ("3"), which IsDefined then filters when
                // it names no member.
                return false;
            }

            var headKey = stored.HeadKey ?? string.Empty;
            var path = stored.Path ?? string.Empty;

            switch (kind)
            {
                case SettingScopeKind.Global:
                    scope = SettingScope.Global;
                    return true;
                case SettingScopeKind.Repository:
                    scope = SettingScope.Repository;
                    return true;
                case SettingScopeKind.Branch:
                    if (headKey.Length == 0)
                    {
                        return false;
                    }

                    scope = SettingScope.Branch(headKey);
                    return true;
                case SettingScopeKind.Solution:
                case SettingScopeKind.Project:
                    if (headKey.Length == 0 || path.Length == 0)
                    {
                        return false;
                    }

                    scope = kind == SettingScopeKind.Solution
                        ? SettingScope.Solution(headKey, path)
                        : SettingScope.Project(headKey, path);
                    return true;
                default:
                    return false;
            }
        }

        private ScopedSettings BuildDocument(bool globalScopes)
        {
            var document = new ScopedSettings();

            foreach (var pair in _overrides)
            {
                if ((pair.Key.Kind == SettingScopeKind.Global) != globalScopes)
                {
                    continue;
                }

                var stored = new ScopeOverrides
                {
                    Kind = pair.Key.Kind.ToString(),
                    HeadKey = pair.Key.HeadKey,
                    Path = pair.Key.Path,
                };

                // Catalog order rather than dictionary order, so a file rewritten without changes
                // is byte-identical and a diff of it means something.
                foreach (var setting in SyncSettingCatalog.All)
                {
                    if (pair.Value.TryGetValue(setting, out var value))
                    {
                        stored.Values.Add(new SettingOverride
                        {
                            Setting = SyncSettingCatalog.NameOf(setting),
                            On = value,
                        });
                    }
                }

                if (stored.Values.Count != 0)
                {
                    document.Scopes.Add(stored);
                }
            }

            document.Scopes.Sort(static (a, b) =>
            {
                var byKind = string.CompareOrdinal(a.Kind, b.Kind);
                if (byKind != 0)
                {
                    return byKind;
                }

                var byHead = string.CompareOrdinal(a.HeadKey, b.HeadKey);
                return byHead != 0 ? byHead : string.CompareOrdinal(a.Path, b.Path);
            });

            return document;
        }
    }
}
