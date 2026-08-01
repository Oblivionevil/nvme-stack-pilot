using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;
using NvmeDriverSwitch.Models;

namespace NvmeDriverSwitch.Services
{
    /// <summary>
    /// Liest und schreibt die FeatureManagement-Overrides, die den nativen NVMe-Stack freischalten.
    /// Unbekannte Werte werden ausschliesslich angezeigt. Entfernbar sind nur bekannte alte NVMe-Werte.
    /// </summary>
    public sealed class FeatureOverrideService
    {
        public const string OverridesPath =
            @"SYSTEM\CurrentControlSet\Policies\Microsoft\FeatureManagement\Overrides";

        public const string OverridesDisplayPath = @"HKLM\" + OverridesPath;

        private static readonly string[] MinimalNames = { "1853569164", "156965516", "3244671118" };

        /// <summary>Bestaetigter Client-Minimalsatz (Stand 08/2026, Win11 24H2).</summary>
        public static readonly IReadOnlyList<string> MinimalSet = Array.AsReadOnly(MinimalNames);

        /// <summary>Nur diese bekannten alten NVMe-Werte duerfen ueber die App entfernt werden.</summary>
        private static readonly Dictionary<string, string> KnownLegacy =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "735209102",  "bekannter NVMe-Altwert aus früheren Sammelanleitungen" },
                { "1409234060", "bekannter NVMe-Altwert aus früheren Sammelanleitungen" }
            };

        /// <summary>Bekannte, aber bewusst schreibgeschuetzte Werte anderer Plattformen.</summary>
        private static readonly Dictionary<string, string> KnownReadOnly =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "1176759950", "offizieller Schlüssel für Windows Server 2025; wird nicht verändert" }
            };

        public sealed class State
        {
            internal State(List<OverrideEntry> entries, bool isNativeConfigured,
                bool hasAnyMinimalValue, string fingerprint)
            {
                Entries = entries;
                IsNativeConfigured = isNativeConfigured;
                HasAnyMinimalValue = hasAnyMinimalValue;
                Fingerprint = fingerprint;
            }

            public List<OverrideEntry> Entries { get; private set; }
            public bool IsNativeConfigured { get; private set; }
            public bool HasAnyMinimalValue { get; private set; }
            public string Fingerprint { get; private set; }
        }

        public sealed class MinimalSnapshot
        {
            internal MinimalSnapshot(Dictionary<string, RegistryValueState> values)
            {
                Values = values;
            }

            internal Dictionary<string, RegistryValueState> Values { get; private set; }
        }

        internal sealed class RegistryValueState
        {
            public bool Exists;
            public RegistryValueKind Kind;
            public object Value;
        }

        private static RegistryKey OpenHklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        /// <summary>Liest Eintraege, Schaltzustand und Fingerprint in einem Registry-Snapshot.</summary>
        public State ReadState()
        {
            var entries = new List<OverrideEntry>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(OverridesPath, false))
            {
                var minimalStates = ReadMinimalStates(key);

                if (key != null)
                {
                    foreach (var name in key.GetValueNames())
                    {
                        bool isMinimal = MinimalNames.Contains(name, StringComparer.OrdinalIgnoreCase);
                        var valueState = isMinimal ? minimalStates[name] : ReadValue(key, name);
                        seen.Add(name);

                        if (isMinimal)
                        {
                            bool enabled = IsExactDwordOne(valueState);
                            entries.Add(new OverrideEntry
                            {
                                Name = name,
                                Data = Describe(valueState.Value),
                                Kind = DescribeKind(valueState.Kind),
                                Role = enabled ? OverrideRole.Minimal : OverrideRole.Missing,
                                Note = enabled ? "aktiv" : "vorhanden, aber nicht als REG_DWORD 1 gesetzt"
                            });
                            continue;
                        }

                        string note;
                        OverrideRole role;
                        if (KnownLegacy.TryGetValue(name, out note))
                        {
                            role = OverrideRole.Legacy;
                        }
                        else
                        {
                            role = OverrideRole.Unknown;
                            if (!KnownReadOnly.TryGetValue(name, out note))
                                note = "unbekannter oder unabhängiger Feature-Override; wird nicht verändert";
                        }

                        entries.Add(new OverrideEntry
                        {
                            Name = name,
                            Data = Describe(valueState.Value),
                            Kind = DescribeKind(valueState.Kind),
                            Role = role,
                            Note = note
                        });
                    }
                }

                foreach (var name in MinimalNames)
                {
                    if (seen.Contains(name)) continue;
                    entries.Add(new OverrideEntry
                    {
                        Name = name,
                        Data = "-",
                        Kind = "-",
                        Role = OverrideRole.Missing,
                        Note = "nicht vorhanden"
                    });
                }

                entries = entries
                    .OrderBy(e => RoleOrder(e.Role))
                    .ThenBy(e => MinimalIndex(e.Name))
                    .ThenBy(e => e.Name, StringComparer.Ordinal)
                    .ToList();

                return new State(
                    entries,
                    MinimalNames.All(n => IsExactDwordOne(minimalStates[n])),
                    MinimalNames.Any(n => minimalStates[n].Exists),
                    ComputeFingerprint(minimalStates));
            }
        }

        public MinimalSnapshot CaptureMinimalSnapshot()
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(OverridesPath, false))
            {
                return new MinimalSnapshot(CloneStates(ReadMinimalStates(key)));
            }
        }

        public void RestoreMinimalSnapshot(MinimalSnapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            using (var hklm = OpenHklm())
            using (var key = hklm.CreateSubKey(OverridesPath))
            {
                if (key == null)
                    throw new InvalidOperationException("Overrides-Schlüssel konnte für das Rollback nicht geöffnet werden.");

                foreach (var name in MinimalNames)
                {
                    var state = snapshot.Values[name];
                    if (state.Exists)
                        key.SetValue(name, CloneValue(state.Value), state.Kind);
                    else
                        key.DeleteValue(name, false);
                }

                var restored = ReadMinimalStates(key);
                if (!StatesEqual(snapshot.Values, restored))
                    throw new InvalidOperationException("Der vorherige Overrides-Zustand konnte nicht vollständig wiederhergestellt werden.");
            }
        }

        public void EnableNative()
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.CreateSubKey(OverridesPath))
            {
                if (key == null)
                    throw new InvalidOperationException("Overrides-Schlüssel konnte nicht geöffnet werden.");

                foreach (var name in MinimalNames)
                    key.SetValue(name, 1, RegistryValueKind.DWord);

                var written = ReadMinimalStates(key);
                if (!MinimalNames.All(n => IsExactDwordOne(written[n])))
                    throw new InvalidOperationException("Die NVMe-Overrides konnten nicht vollständig verifiziert werden.");
            }
        }

        public void DisableNative()
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(OverridesPath, true))
            {
                if (key == null) return;

                foreach (var name in MinimalNames)
                    key.DeleteValue(name, false);

                var remaining = ReadMinimalStates(key);
                if (MinimalNames.Any(n => remaining[n].Exists))
                    throw new InvalidOperationException("Nicht alle NVMe-Overrides konnten entfernt werden.");
            }
        }

        /// <summary>Entfernt ausschliesslich einen explizit bekannten alten NVMe-Wert.</summary>
        public void RemoveLegacyValue(string name)
        {
            if (string.IsNullOrEmpty(name) || !KnownLegacy.ContainsKey(name))
                throw new InvalidOperationException("Dieser Wert ist nicht als entfernbarer NVMe-Altwert freigegeben.");

            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(OverridesPath, true))
            {
                if (key == null) return;
                key.DeleteValue(name, false);

                if (key.GetValueNames().Contains(name, StringComparer.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Der NVMe-Altwert konnte nicht entfernt werden.");
            }
        }

        internal static bool IsExactDwordOne(RegistryValueKind kind, object value)
        {
            return kind == RegistryValueKind.DWord && value is int && (int)value == 1;
        }

        private static bool IsExactDwordOne(RegistryValueState state)
        {
            return state != null && state.Exists && IsExactDwordOne(state.Kind, state.Value);
        }

        private static Dictionary<string, RegistryValueState> ReadMinimalStates(RegistryKey key)
        {
            var states = new Dictionary<string, RegistryValueState>(StringComparer.OrdinalIgnoreCase);
            foreach (var name in MinimalNames)
                states[name] = ReadValue(key, name);
            return states;
        }

        private static RegistryValueState ReadValue(RegistryKey key, string name)
        {
            if (key == null) return new RegistryValueState();

            try
            {
                var kind = key.GetValueKind(name);
                var value = key.GetValue(name, null, RegistryValueOptions.DoNotExpandEnvironmentNames);
                return new RegistryValueState { Exists = true, Kind = kind, Value = CloneValue(value) };
            }
            catch (IOException)
            {
                return new RegistryValueState();
            }
            catch (ArgumentException)
            {
                return new RegistryValueState();
            }
        }

        private static Dictionary<string, RegistryValueState> CloneStates(
            IDictionary<string, RegistryValueState> source)
        {
            var clone = new Dictionary<string, RegistryValueState>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                clone[pair.Key] = new RegistryValueState
                {
                    Exists = pair.Value.Exists,
                    Kind = pair.Value.Kind,
                    Value = CloneValue(pair.Value.Value)
                };
            }
            return clone;
        }

        private static object CloneValue(object value)
        {
            var bytes = value as byte[];
            if (bytes != null) return bytes.ToArray();

            var strings = value as string[];
            if (strings != null) return strings.ToArray();

            return value;
        }

        private static bool StatesEqual(IDictionary<string, RegistryValueState> left,
            IDictionary<string, RegistryValueState> right)
        {
            foreach (var name in MinimalNames)
            {
                var a = left[name];
                var b = right[name];
                if (a.Exists != b.Exists) return false;
                if (!a.Exists) continue;
                if (a.Kind != b.Kind || !ValuesEqual(a.Value, b.Value)) return false;
            }
            return true;
        }

        private static bool ValuesEqual(object left, object right)
        {
            var leftBytes = left as byte[];
            var rightBytes = right as byte[];
            if (leftBytes != null || rightBytes != null)
                return leftBytes != null && rightBytes != null && leftBytes.SequenceEqual(rightBytes);

            var leftStrings = left as string[];
            var rightStrings = right as string[];
            if (leftStrings != null || rightStrings != null)
                return leftStrings != null && rightStrings != null && leftStrings.SequenceEqual(rightStrings);

            return Equals(left, right);
        }

        private static string ComputeFingerprint(IDictionary<string, RegistryValueState> states)
        {
            var canonical = new StringBuilder();
            foreach (var name in MinimalNames)
            {
                var state = states[name];
                canonical.Append(name).Append('|');
                if (!state.Exists)
                {
                    canonical.Append("missing;");
                    continue;
                }

                canonical.Append((int)state.Kind).Append('|')
                    .Append(EncodeValue(state.Value)).Append(';');
            }

            using (var sha = SHA256.Create())
            {
                return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(canonical.ToString())));
            }
        }

        private static string EncodeValue(object value)
        {
            var bytes = value as byte[];
            if (bytes != null) return "b:" + Convert.ToBase64String(bytes);

            var strings = value as string[];
            if (strings != null)
            {
                return "m:" + string.Join(",", strings.Select(s =>
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty))).ToArray());
            }

            var text = Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            return "s:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        }

        private static string Describe(object raw)
        {
            if (raw == null) return "-";
            var bytes = raw as byte[];
            if (bytes != null) return BitConverter.ToString(bytes);
            var strings = raw as string[];
            if (strings != null) return string.Join(", ", strings);
            return Convert.ToString(raw, CultureInfo.CurrentCulture);
        }

        private static string DescribeKind(RegistryValueKind kind)
        {
            switch (kind)
            {
                case RegistryValueKind.DWord: return "REG_DWORD";
                case RegistryValueKind.QWord: return "REG_QWORD";
                case RegistryValueKind.String: return "REG_SZ";
                case RegistryValueKind.ExpandString: return "REG_EXPAND_SZ";
                case RegistryValueKind.MultiString: return "REG_MULTI_SZ";
                case RegistryValueKind.Binary: return "REG_BINARY";
                default: return kind.ToString();
            }
        }

        private static int MinimalIndex(string name)
        {
            int index = Array.FindIndex(MinimalNames,
                n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
            return index < 0 ? int.MaxValue : index;
        }

        private static int RoleOrder(OverrideRole role)
        {
            switch (role)
            {
                case OverrideRole.Minimal:
                case OverrideRole.Missing:
                    return 0;
                case OverrideRole.Legacy:
                    return 1;
                default:
                    return 2;
            }
        }
    }
}
