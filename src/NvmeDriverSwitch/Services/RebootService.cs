using System;
using System.Globalization;
using System.Text;
using Microsoft.Win32;
using NvmeDriverSwitch.Infrastructure;
using NvmeDriverSwitch.Models;

namespace NvmeDriverSwitch.Services
{
    /// <summary>
    /// Speichert Zielzustand und Fingerprint einer noch ausstehenden Konfigurationsaenderung.
    /// Nur ein passender Marker darf spaeter als Beleg fuer "von Windows ignoriert" dienen.
    /// </summary>
    public sealed class RebootService
    {
        private const string StatePath = @"Software\NvmeStackPilot";
        private const string LastWriteValue = "LastWriteUtc";
        private const string PendingChangeValue = "PendingChangeV1";

        public void MarkWritten(StackMode targetMode, string configurationFingerprint)
        {
            if (string.IsNullOrWhiteSpace(configurationFingerprint))
                throw new ArgumentException("Konfigurations-Fingerprint fehlt.", "configurationFingerprint");

            var now = DateTime.UtcNow;
            string marker = Serialize(new PendingChange
            {
                WrittenUtc = now,
                TargetMode = targetMode,
                ConfigurationFingerprint = configurationFingerprint
            });

            using (var key = Registry.CurrentUser.CreateSubKey(StatePath))
            {
                if (key == null)
                    throw new InvalidOperationException("Neustart-Marker konnte nicht gespeichert werden.");

                // Ein einzelner Registry-Wert verhindert gemischte Markerfelder bei Teilfehlern.
                key.SetValue(PendingChangeValue, marker, RegistryValueKind.String);

                // Reine Anzeigehistorie: Ein Fehler hier darf die bereits konsistente
                // Systemkonfiguration und den atomaren Pending-Marker nicht zurueckrollen.
                try
                {
                    key.SetValue(LastWriteValue, now.ToFileTimeUtc(), RegistryValueKind.QWord);
                }
                catch (Exception)
                {
                }
            }
        }

        public PendingChange GetPendingChange()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StatePath, false))
                {
                    if (key == null) return null;
                    return Deserialize(key.GetValue(PendingChangeValue) as string);
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public void ClearPendingChange()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StatePath, true))
                {
                    if (key != null) key.DeleteValue(PendingChangeValue, false);
                }
            }
            catch (Exception)
            {
                // Der Marker ist nur Diagnosezustand; ein fehlgeschlagenes Aufraeumen
                // darf den bereits verifizierten Treiberzustand nicht beeintraechtigen.
            }
        }

        public DateTime? GetLastWriteUtc()
        {
            DateTime? history = null;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StatePath, false))
                {
                    if (key != null)
                    {
                        var raw = key.GetValue(LastWriteValue);
                        if (raw != null) history = DateTime.FromFileTimeUtc(Convert.ToInt64(raw));
                    }
                }
            }
            catch (Exception)
            {
            }

            var pending = GetPendingChange();
            if (pending == null) return history;
            if (!history.HasValue || pending.WrittenUtc > history.Value) return pending.WrittenUtc;
            return history;
        }

        /// <summary>Loest den Neustart aus. Wirft bei Fehlschlag eine Win32Exception.</summary>
        public void Reboot()
        {
            NativeMethods.Reboot(5);
        }

        internal static string Serialize(PendingChange marker)
        {
            if (marker == null) throw new ArgumentNullException("marker");
            string encodedFingerprint = Convert.ToBase64String(
                Encoding.UTF8.GetBytes(marker.ConfigurationFingerprint ?? string.Empty));
            return string.Join("|", new[]
            {
                "v1",
                marker.WrittenUtc.ToFileTimeUtc().ToString(CultureInfo.InvariantCulture),
                ((int)marker.TargetMode).ToString(CultureInfo.InvariantCulture),
                encodedFingerprint
            });
        }

        internal static PendingChange Deserialize(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            var parts = raw.Split('|');
            if (parts.Length != 4 || parts[0] != "v1") return null;

            long fileTime;
            int targetMode;
            if (!long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out fileTime) ||
                !int.TryParse(parts[2], NumberStyles.Integer, CultureInfo.InvariantCulture, out targetMode) ||
                !Enum.IsDefined(typeof(StackMode), targetMode))
            {
                return null;
            }

            try
            {
                var fingerprint = Encoding.UTF8.GetString(Convert.FromBase64String(parts[3]));
                if (string.IsNullOrWhiteSpace(fingerprint)) return null;
                return new PendingChange
                {
                    WrittenUtc = DateTime.FromFileTimeUtc(fileTime),
                    TargetMode = (StackMode)targetMode,
                    ConfigurationFingerprint = fingerprint
                };
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
