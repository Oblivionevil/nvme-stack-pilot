using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace NvmeDriverSwitch.Services
{
    /// <summary>
    /// Verwaltet die SafeBoot-Absicherung fuer nvmedisk. Ein Eintrag gilt nur dann als
    /// gueltig, wenn sein Standardwert als REG_SZ exakt "Service" enthaelt.
    /// </summary>
    public sealed class SafeBootService
    {
        private const string SafeBootRoot = @"SYSTEM\CurrentControlSet\Control\SafeBoot";
        private const string ServiceName = "nvmedisk";
        private static readonly string[] SetNames = { "Minimal", "Network" };

        public const string NvmeDiskClassGuid = "{75416E63-5912-4DFA-AE8F-3EFACCAFFB14}";

        public sealed class Snapshot
        {
            internal Snapshot(Dictionary<string, KeyState> states)
            {
                States = states;
            }

            internal Dictionary<string, KeyState> States { get; private set; }
        }

        internal sealed class KeyState
        {
            public bool KeyExists;
            public bool DefaultValueExists;
            public RegistryValueKind Kind;
            public object Value;
        }

        public string MinimalDisplayPath
        {
            get { return @"HKLM\" + SafeBootRoot + @"\Minimal\" + ServiceName; }
        }

        public string NetworkDisplayPath
        {
            get { return @"HKLM\" + SafeBootRoot + @"\Network\" + ServiceName; }
        }

        private static RegistryKey OpenHklm()
        {
            return RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
        }

        public bool IsValid(string setName)
        {
            ValidateSetName(setName);
            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(PathFor(setName), false))
            {
                if (key == null) return false;
                var state = ReadKeyState(key);
                return IsValidServiceValue(state.Kind, state.Value, state.DefaultValueExists);
            }
        }

        /// <summary>Nur lesen - dieser Eintrag gehoert Windows.</summary>
        public bool IsClassGuidPresent(string setName)
        {
            ValidateSetName(setName);
            using (var hklm = OpenHklm())
            using (var key = hklm.OpenSubKey(
                SafeBootRoot + @"\" + setName + @"\" + NvmeDiskClassGuid, false))
            {
                return key != null;
            }
        }

        public Snapshot CaptureSnapshot()
        {
            var states = new Dictionary<string, KeyState>(StringComparer.OrdinalIgnoreCase);
            using (var hklm = OpenHklm())
            {
                foreach (var setName in SetNames)
                {
                    using (var key = hklm.OpenSubKey(PathFor(setName), false))
                    {
                        states[setName] = key == null
                            ? new KeyState()
                            : ReadKeyState(key);
                    }
                }
            }
            return new Snapshot(CloneStates(states));
        }

        public void RestoreSnapshot(Snapshot snapshot)
        {
            if (snapshot == null) throw new ArgumentNullException("snapshot");

            using (var hklm = OpenHklm())
            {
                foreach (var setName in SetNames)
                {
                    var state = snapshot.States[setName];
                    if (!state.KeyExists)
                    {
                        using (var parent = hklm.OpenSubKey(SafeBootRoot + @"\" + setName, true))
                        {
                            if (parent != null) parent.DeleteSubKeyTree(ServiceName, false);
                        }
                        continue;
                    }

                    using (var key = hklm.CreateSubKey(PathFor(setName)))
                    {
                        if (key == null)
                            throw new InvalidOperationException("SafeBoot-Schlüssel konnte für das Rollback nicht geöffnet werden.");

                        if (state.DefaultValueExists)
                            key.SetValue(null, CloneValue(state.Value), state.Kind);
                        else
                            key.DeleteValue(string.Empty, false);
                    }
                }
            }

            var restored = CaptureSnapshot();
            if (!StatesEqual(snapshot.States, restored.States))
                throw new InvalidOperationException("Der vorherige SafeBoot-Zustand konnte nicht vollständig wiederhergestellt werden.");
        }

        public void Create()
        {
            foreach (var setName in SetNames)
                CreateOne(setName);

            if (!SetNames.All(IsValid))
                throw new InvalidOperationException("Die SafeBoot-Absicherung konnte nicht vollständig verifiziert werden.");
        }

        public void Remove()
        {
            foreach (var setName in SetNames)
                RemoveOne(setName);
        }

        internal static bool IsValidServiceValue(RegistryValueKind kind, object value, bool exists)
        {
            return exists && kind == RegistryValueKind.String &&
                   string.Equals(value as string, "Service", StringComparison.OrdinalIgnoreCase);
        }

        private static void CreateOne(string setName)
        {
            using (var hklm = OpenHklm())
            using (var key = hklm.CreateSubKey(PathFor(setName)))
            {
                if (key == null)
                    throw new InvalidOperationException("SafeBoot-Schlüssel konnte nicht geöffnet werden: " + setName);
                key.SetValue(null, "Service", RegistryValueKind.String);
            }
        }

        private static void RemoveOne(string setName)
        {
            using (var hklm = OpenHklm())
            using (var parent = hklm.OpenSubKey(SafeBootRoot + @"\" + setName, true))
            {
                if (parent == null) return;
                parent.DeleteSubKeyTree(ServiceName, false);
            }
        }

        private static string PathFor(string setName)
        {
            return SafeBootRoot + @"\" + setName + @"\" + ServiceName;
        }

        private static void ValidateSetName(string setName)
        {
            if (!SetNames.Contains(setName, StringComparer.OrdinalIgnoreCase))
                throw new ArgumentOutOfRangeException("setName", "Unbekannter SafeBoot-Satz.");
        }

        private static KeyState ReadKeyState(RegistryKey key)
        {
            var state = new KeyState { KeyExists = true };
            try
            {
                state.Kind = key.GetValueKind(string.Empty);
                state.Value = CloneValue(key.GetValue(null, null,
                    RegistryValueOptions.DoNotExpandEnvironmentNames));
                state.DefaultValueExists = true;
            }
            catch (IOException)
            {
                state.DefaultValueExists = false;
            }
            catch (ArgumentException)
            {
                state.DefaultValueExists = false;
            }
            return state;
        }

        private static Dictionary<string, KeyState> CloneStates(IDictionary<string, KeyState> source)
        {
            var clone = new Dictionary<string, KeyState>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in source)
            {
                clone[pair.Key] = new KeyState
                {
                    KeyExists = pair.Value.KeyExists,
                    DefaultValueExists = pair.Value.DefaultValueExists,
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

        private static bool StatesEqual(IDictionary<string, KeyState> left,
            IDictionary<string, KeyState> right)
        {
            foreach (var setName in SetNames)
            {
                var a = left[setName];
                var b = right[setName];
                if (a.KeyExists != b.KeyExists || a.DefaultValueExists != b.DefaultValueExists)
                    return false;
                if (!a.DefaultValueExists) continue;
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
    }
}
