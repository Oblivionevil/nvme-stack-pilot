using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using NvmeDriverSwitch.Infrastructure;
using NvmeDriverSwitch.Models;
using NvmeDriverSwitch.Services;

namespace NvmeDriverSwitch.ViewModels
{
    public sealed class MainViewModel : INotifyPropertyChanged
    {
        private readonly FeatureOverrideService _overrides = new FeatureOverrideService();
        private readonly SafeBootService _safeBoot = new SafeBootService();
        private readonly NvmeStatusService _status = new NvmeStatusService();
        private readonly PreflightService _preflight = new PreflightService();
        private readonly RebootService _reboot = new RebootService();

        private readonly DispatcherTimer _timer;

        public MainViewModel()
        {
            Overrides = new ObservableCollection<OverrideEntry>();
            Controllers = new ObservableCollection<NvmeControllerInfo>();
            PreflightIssues = new ObservableCollection<PreflightIssue>();

            // Schreibaktionen bleiben gesperrt, bis ein vollstaendiger Status vorliegt, und
            // waehrend ein Snapshot im Hintergrund gelesen wird. So kann kein veralteter
            // Snapshot eine gerade ausgefuehrte Registry-Aenderung wieder uebermalen.
            EnableNativeCommand = new RelayCommand(EnableNative,
                () => CanMutate && ConfiguredMode != StackMode.Native);
            DisableNativeCommand = new RelayCommand(DisableNative,
                () => CanMutate && HasAnyMinimalOverride);
            RemoveOverrideCommand = new RelayCommand(RemoveOverride,
                p => CanMutate && p is OverrideEntry && ((OverrideEntry)p).IsRemovable);
            CreateSafeBootCommand = new RelayCommand(CreateSafeBoot,
                () => CanMutate && !(SafeBootMinimalPresent && SafeBootNetworkPresent));
            RefreshCommand = new RelayCommand(() => { var ignore = RefreshAsync(); },
                () => !_isBusy && !_isMutating);
            RebootCommand = new RelayCommand(DoReboot,
                () => _hasSnapshot && !_isBusy && !_isMutating);

            _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
            _timer.Tick += (s, e) => { var ignore = RefreshAsync(); };
        }

        // ---- Sammlungen ----

        public ObservableCollection<OverrideEntry> Overrides { get; private set; }
        public ObservableCollection<NvmeControllerInfo> Controllers { get; private set; }
        public ObservableCollection<PreflightIssue> PreflightIssues { get; private set; }

        // ---- Kommandos ----

        public ICommand EnableNativeCommand { get; private set; }
        public ICommand DisableNativeCommand { get; private set; }
        public ICommand RemoveOverrideCommand { get; private set; }
        public ICommand CreateSafeBootCommand { get; private set; }
        public ICommand RefreshCommand { get; private set; }
        public ICommand RebootCommand { get; private set; }

        // ---- Zustand ----

        private StackMode _configuredMode;
        public StackMode ConfiguredMode
        {
            get { return _configuredMode; }
            private set
            {
                if (!SetField(ref _configuredMode, value)) return;
                Raise("ConfiguredModeText");
                // Aendert die Verfuegbarkeit der beiden Umschalt-Schaltflaechen.
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private StackMode _runtimeMode;
        public StackMode RuntimeMode
        {
            get { return _runtimeMode; }
            private set
            {
                if (!SetField(ref _runtimeMode, value)) return;
                Raise("RuntimeModeText");
                Raise("StatusHeadline");
                Raise("StatusBrush");
                Raise("StatusBackground");
            }
        }

        private Verdict _currentVerdict;
        public Verdict CurrentVerdict
        {
            get { return _currentVerdict; }
            private set
            {
                if (!SetField(ref _currentVerdict, value)) return;
                Raise("VerdictText");
                Raise("StatusBrush");
                Raise("StatusBackground");
                Raise("RebootBannerVisibility");
                Raise("RebootBannerTitle");
                Raise("RebootBannerDetail");
                Raise("IgnoredHintVisibility");
            }
        }

        /// <summary>Wiedereintritts-Sperre fuer RefreshAsync und Schreibaktionen.</summary>
        private bool _isBusy;
        private bool _isMutating;
        private bool _hasSnapshot;

        private bool CanMutate
        {
            get { return _hasSnapshot && !_isBusy && !_isMutating; }
        }

        private bool _hasAnyMinimalOverride;
        public bool HasAnyMinimalOverride
        {
            get { return _hasAnyMinimalOverride; }
            private set
            {
                if (!SetField(ref _hasAnyMinimalOverride, value)) return;
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _safeBootMinimalPresent;
        public bool SafeBootMinimalPresent
        {
            get { return _safeBootMinimalPresent; }
            private set
            {
                if (!SetField(ref _safeBootMinimalPresent, value)) return;
                Raise("SafeBootMinimalText");
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _safeBootNetworkPresent;
        public bool SafeBootNetworkPresent
        {
            get { return _safeBootNetworkPresent; }
            private set
            {
                if (!SetField(ref _safeBootNetworkPresent, value)) return;
                Raise("SafeBootNetworkText");
                CommandManager.InvalidateRequerySuggested();
            }
        }

        private bool _safeBootClassGuidPresent;
        public bool SafeBootClassGuidPresent
        {
            get { return _safeBootClassGuidPresent; }
            private set { if (SetField(ref _safeBootClassGuidPresent, value)) Raise("SafeBootClassGuidText"); }
        }

        private int _ghostCount;
        public int GhostCount
        {
            get { return _ghostCount; }
            private set
            {
                if (!SetField(ref _ghostCount, value)) return;
                Raise("GhostText");
                Raise("GhostVisibility");
            }
        }

        private string _buildText = "";
        public string BuildText
        {
            get { return _buildText; }
            private set { SetField(ref _buildText, value); }
        }

        private string _lastWriteText = "noch keine Änderung durch diese App";
        public string LastWriteText
        {
            get { return _lastWriteText; }
            private set { SetField(ref _lastWriteText, value); }
        }

        // ---- Anzeigetexte ----

        public string StatusHeadline
        {
            get
            {
                return RuntimeMode == StackMode.Native
                    ? "Nativer NVMe-Treiber AKTIV"
                    : "Klassischer NVMe-Treiber aktiv";
            }
        }

        public string RuntimeModeText
        {
            get
            {
                return RuntimeMode == StackMode.Native
                    ? "nvmedisk.sys (nativ)"
                    : "stornvme.sys + disk.sys (klassisch)";
            }
        }

        public string ConfiguredModeText
        {
            get { return ConfiguredMode == StackMode.Native ? "nativ" : "klassisch"; }
        }

        public string VerdictText
        {
            get
            {
                switch (CurrentVerdict)
                {
                    case Verdict.RebootRequired:
                        return "Neustart erforderlich";
                    case Verdict.UntrackedMismatch:
                        return "Abweichung mit unbekanntem Ursprung";
                    case Verdict.IgnoredByWindows:
                        return "Von Windows ignoriert";
                    default:
                        return "Konfiguration und Laufzeit stimmen überein";
                }
            }
        }

        public Brush StatusBrush
        {
            get
            {
                switch (CurrentVerdict)
                {
                    case Verdict.RebootRequired: return Res("Warn");
                    case Verdict.UntrackedMismatch: return Res("Warn");
                    case Verdict.IgnoredByWindows: return Res("Bad");
                    default: return RuntimeMode == StackMode.Native ? Res("Ok") : Res("Accent");
                }
            }
        }

        public Brush StatusBackground
        {
            get
            {
                switch (CurrentVerdict)
                {
                    case Verdict.RebootRequired: return Res("WarnBg");
                    case Verdict.UntrackedMismatch: return Res("WarnBg");
                    case Verdict.IgnoredByWindows: return Res("BadBg");
                    default: return RuntimeMode == StackMode.Native ? Res("OkBg") : Brushes.Transparent;
                }
            }
        }

        public Visibility RebootBannerVisibility
        {
            get
            {
                return CurrentVerdict == Verdict.RebootRequired ||
                       CurrentVerdict == Verdict.UntrackedMismatch
                    ? Visibility.Visible
                    : Visibility.Collapsed;
            }
        }

        public string RebootBannerTitle
        {
            get
            {
                return CurrentVerdict == Verdict.UntrackedMismatch
                    ? "Konfiguration und Laufzeit weichen voneinander ab."
                    : "Die Registry wurde geändert - wirksam wird das erst nach einem Neustart.";
            }
        }

        public string RebootBannerDetail
        {
            get
            {
                return CurrentVerdict == Verdict.UntrackedMismatch
                    ? "Die aktuelle Änderung wurde nicht von dieser App protokolliert. Werte prüfen; ein Neustart kann erforderlich sein."
                    : "Nach dem Neustart prüft die App selbst, ob Windows die Änderung übernommen hat.";
            }
        }

        public Visibility IgnoredHintVisibility
        {
            get { return CurrentVerdict == Verdict.IgnoredByWindows ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility PreflightVisibility
        {
            get { return PreflightIssues.Count > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public Visibility GhostVisibility
        {
            get { return GhostCount > 0 ? Visibility.Visible : Visibility.Collapsed; }
        }

        public string GhostText
        {
            get
            {
                return GhostCount == 1
                    ? "1 getrenntes NvmeDisk-Gerät in der Registry - der native Stack lief hier bereits."
                    : GhostCount + " getrennte NvmeDisk-Geräte in der Registry - der native Stack lief hier bereits.";
            }
        }

        public string SafeBootMinimalText
        {
            get { return SafeBootMinimalPresent ? "gültig (REG_SZ = Service)" : "fehlt oder ist ungültig"; }
        }

        public string SafeBootNetworkText
        {
            get { return SafeBootNetworkPresent ? "gültig (REG_SZ = Service)" : "fehlt oder ist ungültig"; }
        }

        public string SafeBootClassGuidText
        {
            get
            {
                return SafeBootClassGuidPresent
                    ? "von Windows angelegt - wird nur gelesen, nie verändert"
                    : "nicht vorhanden - wird von dieser App nicht angelegt";
            }
        }

        public string OverridesPathText
        {
            get { return FeatureOverrideService.OverridesDisplayPath; }
        }

        public string SafeBootPathText
        {
            get { return _safeBoot.MinimalDisplayPath + "  /  ...\\Network\\nvmedisk"; }
        }

        public string AdminText
        {
            get
            {
                using (var id = WindowsIdentity.GetCurrent())
                {
                    var principal = new WindowsPrincipal(id);
                    return principal.IsInRole(WindowsBuiltInRole.Administrator)
                        ? "als Administrator"
                        : "OHNE Administratorrechte - Schreibzugriffe schlagen fehl";
                }
            }
        }

        // ---- Ablauf ----

        public void Start()
        {
            _timer.Start();
            var ignore = RefreshAsync();
        }

        public async Task RefreshAsync()
        {
            if (_isBusy || _isMutating) return;
            SetBusy(true);
            try
            {
                var snapshot = await Task.Run(() => BuildSnapshot());
                Apply(snapshot);
            }
            catch (Exception ex)
            {
                _hasSnapshot = false;
                CommandManager.InvalidateRequerySuggested();
                MessageBox.Show("Status konnte nicht ermittelt werden:\n\n" + ex.Message,
                    "NVMe Stack Pilot", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                SetBusy(false);
            }
        }

        private SystemStatus BuildSnapshot()
        {
            var s = new SystemStatus();

            var overrideState = _overrides.ReadState();
            s.Overrides.AddRange(overrideState.Entries);
            s.ConfiguredMode = overrideState.IsNativeConfigured ? StackMode.Native : StackMode.Classic;
            s.HasAnyMinimalOverride = overrideState.HasAnyMinimalValue;
            s.ConfigurationFingerprint = overrideState.Fingerprint;
            s.RuntimeMode = _status.GetRuntimeMode();

            s.SafeBootMinimalPresent = _safeBoot.IsValid("Minimal");
            s.SafeBootNetworkPresent = _safeBoot.IsValid("Network");
            s.SafeBootClassGuidPresent = _safeBoot.IsClassGuidPresent("Minimal");

            s.Controllers.AddRange(_status.GetControllers());
            s.GhostNvmeDiskCount = _status.GetGhostNvmeDiskCount();
            s.PreflightIssues.AddRange(_preflight.Run());

            s.WindowsBuild = _status.GetWindowsBuild();
            s.LastBootUtc = _status.GetLastBootUtc();
            s.LastWriteUtc = _reboot.GetLastWriteUtc();
            s.PendingChange = _reboot.GetPendingChange();

            s.Verdict = StatusJudge.Judge(s);
            return s;
        }

        private void Apply(SystemStatus s)
        {
            // Die Listen nur dann neu aufbauen, wenn sich wirklich etwas geaendert hat.
            // Ein Clear/Refill im Fuenf-Sekunden-Takt wuerde die Eintraege sichtbar flackern lassen.
            Sync(Overrides, s.Overrides, SignatureOf, ref _overridesSignature);
            Sync(Controllers, s.Controllers, SignatureOf, ref _controllersSignature);
            Sync(PreflightIssues, s.PreflightIssues, SignatureOf, ref _preflightSignature);

            ConfiguredMode = s.ConfiguredMode;
            HasAnyMinimalOverride = s.HasAnyMinimalOverride;
            RuntimeMode = s.RuntimeMode;
            CurrentVerdict = s.Verdict;
            SafeBootMinimalPresent = s.SafeBootMinimalPresent;
            SafeBootNetworkPresent = s.SafeBootNetworkPresent;
            SafeBootClassGuidPresent = s.SafeBootClassGuidPresent;
            GhostCount = s.GhostNvmeDiskCount;
            BuildText = s.WindowsBuild;

            LastWriteText = s.LastWriteUtc.HasValue
                ? "letzte Änderung " + s.LastWriteUtc.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm")
                : "noch keine Änderung durch diese App";

            _hasSnapshot = true;
            if (s.Verdict == Verdict.InSync && s.PendingChange != null)
                _reboot.ClearPendingChange();

            Raise("PreflightVisibility");
            CommandManager.InvalidateRequerySuggested();
        }

        // ---- Listenabgleich ----

        private string _overridesSignature;
        private string _controllersSignature;
        private string _preflightSignature;

        /// <summary>Ersetzt den Listeninhalt nur, wenn sich die Signatur geaendert hat.</summary>
        private static void Sync<T>(ObservableCollection<T> target, List<T> source,
            Func<List<T>, string> signature, ref string lastSignature)
        {
            var current = signature(source);
            if (current == lastSignature) return;

            lastSignature = current;
            target.Clear();
            foreach (var item in source) target.Add(item);
        }

        private static string SignatureOf(List<OverrideEntry> items)
        {
            var sb = new StringBuilder();
            foreach (var e in items)
                sb.Append(e.Name).Append('|').Append(e.Data).Append('|').Append(e.Kind).Append('|')
                  .Append(e.Role).Append('|').Append(e.Note).Append(';');
            return sb.ToString();
        }

        private static string SignatureOf(List<NvmeControllerInfo> items)
        {
            var sb = new StringBuilder();
            foreach (var c in items)
            {
                sb.Append(c.InstanceId).Append('|').Append(c.Service).Append('|').Append(c.ConfigManagerErrorCode).Append('{');
                foreach (var d in c.Disks)
                {
                    sb.Append(d.InstanceId).Append('|').Append(d.Service).Append('|')
                      .Append(d.SizeBytes).Append('|').Append(d.DiskNumber).Append('|').Append(d.IsBootDisk).Append(';');
                }
                sb.Append('}');
            }
            return sb.ToString();
        }

        private static string SignatureOf(List<PreflightIssue> items)
        {
            var sb = new StringBuilder();
            foreach (var p in items)
                sb.Append(p.Severity).Append('|').Append(p.Title).Append('|').Append(p.Detail).Append(';');
            return sb.ToString();
        }

        // ---- Aktionen ----

        private void EnableNative()
        {
            ExecuteConfirmedMutation(BuildEnableConfirmation,
                "Nativen NVMe-Treiber aktivieren", EnableNativeAtomically);
        }

        private string BuildEnableConfirmation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Folgende Registry-Änderungen werden vorgenommen:");
            sb.AppendLine();
            sb.AppendLine(FeatureOverrideService.OverridesDisplayPath);
            foreach (var name in FeatureOverrideService.MinimalSet)
                sb.AppendLine("    " + name + "   (REG_DWORD) = 1");
            sb.AppendLine();
            sb.AppendLine(_safeBoot.MinimalDisplayPath);
            sb.AppendLine("    (Standard)   (REG_SZ) = Service");
            sb.AppendLine(_safeBoot.NetworkDisplayPath);
            sb.AppendLine("    (Standard)   (REG_SZ) = Service");
            sb.AppendLine();
            sb.AppendLine("Unbekannte und unabhängige Overrides bleiben unangetastet.");
            sb.AppendLine("Wirksam erst nach einem Neustart.");

            if (PreflightIssues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("VORFLUGHINWEISE:");
                foreach (var issue in PreflightIssues)
                    sb.AppendLine("  • " + issue.Title + ": " + issue.Detail);
                sb.AppendLine();
                sb.AppendLine("Nur fortfahren, wenn diese Hinweise geprüft und geklärt wurden.");
            }

            return sb.ToString();
        }

        private void EnableNativeAtomically()
        {
            var overrideBefore = _overrides.CaptureMinimalSnapshot();
            var safeBootBefore = _safeBoot.CaptureSnapshot();

            try
            {
                // Die Recovery-Absicherung muss gueltig sein, bevor der bootrelevante
                // Treiber-Override geschrieben wird.
                _safeBoot.Create();
                _overrides.EnableNative();

                var written = _overrides.ReadState();
                if (!written.IsNativeConfigured)
                    throw new InvalidOperationException("Die native Konfiguration konnte nicht verifiziert werden.");

                _reboot.MarkWritten(StackMode.Native, written.Fingerprint);
            }
            catch (Exception operationError)
            {
                var rollbackErrors = new List<string>();
                bool overridesRestored = TryRollback(
                    () => _overrides.RestoreMinimalSnapshot(overrideBefore),
                    "Overrides", rollbackErrors);

                if (overridesRestored)
                {
                    TryRollback(() => _safeBoot.RestoreSnapshot(safeBootBefore),
                        "SafeBoot", rollbackErrors);
                }
                else
                {
                    rollbackErrors.Add("SafeBoot wurde vorsorglich beibehalten, weil das Overrides-Rollback fehlschlug.");
                }

                throw BuildRollbackException("Aktivierung", operationError, rollbackErrors);
            }
        }

        private void DisableNative()
        {
            ExecuteConfirmedMutation(BuildDisableConfirmation,
                "Zurück auf den klassischen NVMe-Treiber", DisableNativeAtomically);
        }

        private string BuildDisableConfirmation()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Folgende Werte werden gelöscht:");
            sb.AppendLine();
            sb.AppendLine(FeatureOverrideService.OverridesDisplayPath);
            foreach (var name in FeatureOverrideService.MinimalSet)
                sb.AppendLine("    " + name);
            sb.AppendLine();
            sb.AppendLine("Die gültigen SafeBoot-Einträge bleiben bestehen.");
            sb.AppendLine("Wirksam erst nach einem Neustart.");
            return sb.ToString();
        }

        private void DisableNativeAtomically()
        {
            var overrideBefore = _overrides.CaptureMinimalSnapshot();
            try
            {
                _overrides.DisableNative();
                var written = _overrides.ReadState();
                if (written.HasAnyMinimalValue)
                    throw new InvalidOperationException("Die klassische Konfiguration konnte nicht verifiziert werden.");

                _reboot.MarkWritten(StackMode.Classic, written.Fingerprint);
            }
            catch (Exception operationError)
            {
                var rollbackErrors = new List<string>();
                TryRollback(() => _overrides.RestoreMinimalSnapshot(overrideBefore),
                    "Overrides", rollbackErrors);
                throw BuildRollbackException("Deaktivierung", operationError, rollbackErrors);
            }
        }

        private void RemoveOverride(object parameter)
        {
            var entry = parameter as OverrideEntry;
            if (entry == null || !entry.IsRemovable) return;

            ExecuteConfirmedMutation(
                () => "Bekannten NVMe-Altwert löschen?\n\n" +
                      FeatureOverrideService.OverridesDisplayPath +
                      "\n    " + entry.Name + "   (" + entry.Kind + ") = " + entry.Data +
                      "\n\n" + entry.Note,
                "NVMe-Altwert entfernen",
                () => _overrides.RemoveLegacyValue(entry.Name));
        }

        private void CreateSafeBoot()
        {
            ExecuteConfirmedMutation(
                () => "Folgende Schlüssel werden angelegt oder repariert:\n\n" +
                      _safeBoot.MinimalDisplayPath + "\n    (Standard)   (REG_SZ) = Service\n" +
                      _safeBoot.NetworkDisplayPath + "\n    (Standard)   (REG_SZ) = Service\n\n" +
                      "Damit bleibt ein Datenträger am nativen Stack auch im abgesicherten Modus erreichbar.",
                "SafeBoot-Absicherung anlegen", CreateSafeBootAtomically);
        }

        private void CreateSafeBootAtomically()
        {
            var before = _safeBoot.CaptureSnapshot();
            try
            {
                _safeBoot.Create();
            }
            catch (Exception operationError)
            {
                var rollbackErrors = new List<string>();
                TryRollback(() => _safeBoot.RestoreSnapshot(before), "SafeBoot", rollbackErrors);
                throw BuildRollbackException("SafeBoot-Reparatur", operationError, rollbackErrors);
            }
        }

        private void DoReboot()
        {
            if (!TryBeginMutation()) return;

            try
            {
                if (!Confirm("Windows in 5 Sekunden neu starten?\n\nNicht gespeicherte Arbeiten gehen verloren.",
                        "Neustart")) return;
                _reboot.Reboot();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Neustart fehlgeschlagen:\n\n" + ex.Message,
                    "NVMe Stack Pilot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EndMutation();
            }
        }

        private void ExecuteConfirmedMutation(Func<string> messageFactory, string caption, Action action)
        {
            if (!TryBeginMutation()) return;
            try
            {
                if (!Confirm(messageFactory(), caption)) return;
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Die Änderung ist fehlgeschlagen:\n\n" + DescribeException(ex),
                    "NVMe Stack Pilot", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EndMutation();
            }
        }

        private bool TryBeginMutation()
        {
            if (!CanMutate) return false;
            _isMutating = true;
            _timer.Stop();
            CommandManager.InvalidateRequerySuggested();
            return true;
        }

        private void EndMutation()
        {
            _isMutating = false;
            _timer.Start();
            CommandManager.InvalidateRequerySuggested();
            var ignore = RefreshAsync();
        }

        private void SetBusy(bool value)
        {
            if (_isBusy == value) return;
            _isBusy = value;
            CommandManager.InvalidateRequerySuggested();
        }

        private static bool TryRollback(Action rollback, string label, List<string> errors)
        {
            try
            {
                rollback();
                return true;
            }
            catch (Exception ex)
            {
                errors.Add(label + "-Rollback fehlgeschlagen: " + ex.Message);
                return false;
            }
        }

        private static Exception BuildRollbackException(string operation, Exception original,
            List<string> rollbackErrors)
        {
            var message = operation + " fehlgeschlagen: " + original.Message;
            if (rollbackErrors.Count == 0)
                message += "\n\nAlle begonnenen Änderungen wurden zurückgesetzt.";
            else
                message += "\n\nManuelle Prüfung erforderlich:\n- " +
                           string.Join("\n- ", rollbackErrors.ToArray());
            return new InvalidOperationException(message, original);
        }

        private static string DescribeException(Exception ex)
        {
            var messages = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                if (!string.IsNullOrWhiteSpace(current.Message) && !messages.Contains(current.Message))
                    messages.Add(current.Message);
            }
            return string.Join("\n", messages.ToArray());
        }

        private static bool Confirm(string text, string caption)
        {
            return MessageBox.Show(text, caption, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel) == MessageBoxResult.OK;
        }

        private static Brush Res(string key)
        {
            var brush = Application.Current != null ? Application.Current.TryFindResource(key) as Brush : null;
            return brush ?? Brushes.Gray;
        }

        // ---- INotifyPropertyChanged ----

        public event PropertyChangedEventHandler PropertyChanged;

        private bool SetField<T>(ref T field, T value, [CallerMemberName] string propertyName = null)
        {
            if (Equals(field, value)) return false;
            field = value;
            Raise(propertyName);
            return true;
        }

        private void Raise(string propertyName)
        {
            var handler = PropertyChanged;
            if (handler != null) handler(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
