using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
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
            // Der Hintergrund-Ping darf die Schaltflaechen nicht kurz deaktivieren;
            // sonst blinken sie im Fuenf-Sekunden-Takt. Er laeuft deshalb still.
            _timer.Tick += (s, e) => { var ignore = RefreshAsync(silent: true); };
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
        private bool _isRefreshing;
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

        private string _lastWriteText = LocalizedStrings.Get("LastWriteNone");
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
                    ? LocalizedStrings.Get("StatusNativeActive")
                    : LocalizedStrings.Get("StatusClassicActive");
            }
        }

        public string RuntimeModeText
        {
            get
            {
                return RuntimeMode == StackMode.Native
                    ? LocalizedStrings.Get("RuntimeNative")
                    : LocalizedStrings.Get("RuntimeClassic");
            }
        }

        public string ConfiguredModeText
        {
            get
            {
                return ConfiguredMode == StackMode.Native
                    ? LocalizedStrings.Get("ConfiguredNative")
                    : LocalizedStrings.Get("ConfiguredClassic");
            }
        }

        public string VerdictText
        {
            get
            {
                switch (CurrentVerdict)
                {
                    case Verdict.RebootRequired:
                        return LocalizedStrings.Get("VerdictRebootRequired");
                    case Verdict.UntrackedMismatch:
                        return LocalizedStrings.Get("VerdictUntrackedMismatch");
                    case Verdict.IgnoredByWindows:
                        return LocalizedStrings.Get("VerdictIgnored");
                    default:
                        return LocalizedStrings.Get("VerdictInSync");
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
                    ? LocalizedStrings.Get("RebootBannerTitleMismatch")
                    : LocalizedStrings.Get("RebootBannerTitleChanged");
            }
        }

        public string RebootBannerDetail
        {
            get
            {
                return CurrentVerdict == Verdict.UntrackedMismatch
                    ? LocalizedStrings.Get("RebootBannerDetailMismatch")
                    : LocalizedStrings.Get("RebootBannerDetailChanged");
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
                    ? LocalizedStrings.Get("GhostSingle")
                    : LocalizedStrings.Format("GhostMany", GhostCount);
            }
        }

        public string SafeBootMinimalText
        {
            get
            {
                return SafeBootMinimalPresent
                    ? LocalizedStrings.Get("SafeBootValid")
                    : LocalizedStrings.Get("SafeBootInvalid");
            }
        }

        public string SafeBootNetworkText
        {
            get
            {
                return SafeBootNetworkPresent
                    ? LocalizedStrings.Get("SafeBootValid")
                    : LocalizedStrings.Get("SafeBootInvalid");
            }
        }

        public string SafeBootClassGuidText
        {
            get
            {
                return SafeBootClassGuidPresent
                    ? LocalizedStrings.Get("SafeBootClassGuidPresent")
                    : LocalizedStrings.Get("SafeBootClassGuidMissing");
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
                        ? LocalizedStrings.Get("AdminRunning")
                        : LocalizedStrings.Get("AdminMissing");
                }
            }
        }

        // ---- Ablauf ----

        public void Start()
        {
            _timer.Start();
            var ignore = RefreshAsync(silent: true);
        }

        /// <summary>
        /// Aktualisiert den Status. Bei <paramref name="silent"/> bleibt der sichtbare
        /// Busy-Zustand der Schaltflaechen unveraendert (Hintergrund-Ping); der interne
        /// Lock verhindert trotzdem ueberlappende Refreshes und Schreibaktionen.
        /// </summary>
        public async Task RefreshAsync(bool silent = false)
        {
            if (_isBusy || _isMutating || _isRefreshing) return;
            _isRefreshing = true;
            if (!silent) SetBusy(true);
            try
            {
                var snapshot = await Task.Run(() => BuildSnapshot());
                Apply(snapshot);
            }
            catch (Exception ex)
            {
                _hasSnapshot = false;
                CommandManager.InvalidateRequerySuggested();
                MessageBox.Show(LocalizedStrings.Format("StatusErrorFormat", ex.Message),
                    LocalizedStrings.Get("StatusErrorTitle"), MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            finally
            {
                if (!silent) SetBusy(false);
                _isRefreshing = false;
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
                ? LocalizedStrings.Format("LastWriteFormat",
                    s.LastWriteUtc.Value.ToLocalTime().ToString("g", CultureInfo.CurrentCulture))
                : LocalizedStrings.Get("LastWriteNone");

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
                LocalizedStrings.Get("EnableConfirmationTitle"), EnableNativeAtomically);
        }

        private string BuildEnableConfirmation()
        {
            var sb = new StringBuilder();
            sb.AppendLine(LocalizedStrings.Get("EnableConfirmationHeader"));
            sb.AppendLine();
            sb.AppendLine(FeatureOverrideService.OverridesDisplayPath);
            foreach (var name in FeatureOverrideService.MinimalSet)
                sb.AppendLine(LocalizedStrings.Format("EnableOverrideLineFormat", name));
            sb.AppendLine();
            sb.AppendLine(_safeBoot.MinimalDisplayPath);
            sb.AppendLine(LocalizedStrings.Get("EnableDefaultValueLine"));
            sb.AppendLine(_safeBoot.NetworkDisplayPath);
            sb.AppendLine(LocalizedStrings.Get("EnableDefaultValueLine"));
            sb.AppendLine();
            sb.AppendLine(LocalizedStrings.Get("EnableUntouchedNote"));
            sb.AppendLine(LocalizedStrings.Get("EffectiveAfterRestart"));

            if (PreflightIssues.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine(LocalizedStrings.Get("PreflightWarningsHeader"));
                foreach (var issue in PreflightIssues)
                    sb.AppendLine(LocalizedStrings.Format("PreflightIssueLineFormat", issue.Title, issue.Detail));
                sb.AppendLine();
                sb.AppendLine(LocalizedStrings.Get("PreflightContinueNote"));
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
                    throw new InvalidOperationException(LocalizedStrings.Get("ErrorVerifyNative"));

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
                    rollbackErrors.Add(LocalizedStrings.Get("RollbackSafeBootKept"));
                }

                throw BuildRollbackException(LocalizedStrings.Get("OperationActivation"), operationError, rollbackErrors);
            }
        }

        private void DisableNative()
        {
            ExecuteConfirmedMutation(BuildDisableConfirmation,
                LocalizedStrings.Get("DisableConfirmationTitle"), DisableNativeAtomically);
        }

        private string BuildDisableConfirmation()
        {
            var sb = new StringBuilder();
            sb.AppendLine(LocalizedStrings.Get("DisableConfirmationHeader"));
            sb.AppendLine();
            sb.AppendLine(FeatureOverrideService.OverridesDisplayPath);
            foreach (var name in FeatureOverrideService.MinimalSet)
                sb.AppendLine("    " + name);
            sb.AppendLine();
            sb.AppendLine(LocalizedStrings.Get("DisableSafeBootNote"));
            sb.AppendLine(LocalizedStrings.Get("EffectiveAfterRestart"));
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
                    throw new InvalidOperationException(LocalizedStrings.Get("ErrorVerifyClassic"));

                _reboot.MarkWritten(StackMode.Classic, written.Fingerprint);
            }
            catch (Exception operationError)
            {
                var rollbackErrors = new List<string>();
                TryRollback(() => _overrides.RestoreMinimalSnapshot(overrideBefore),
                    "Overrides", rollbackErrors);
                throw BuildRollbackException(LocalizedStrings.Get("OperationDeactivation"), operationError, rollbackErrors);
            }
        }

        private void RemoveOverride(object parameter)
        {
            var entry = parameter as OverrideEntry;
            if (entry == null || !entry.IsRemovable) return;

            ExecuteConfirmedMutation(
                () => LocalizedStrings.Format("RemoveOverrideFormat",
                    FeatureOverrideService.OverridesDisplayPath, entry.Name, entry.Kind, entry.Data, entry.Note),
                LocalizedStrings.Get("RemoveOverrideTitle"),
                () => _overrides.RemoveLegacyValue(entry.Name));
        }

        private void CreateSafeBoot()
        {
            ExecuteConfirmedMutation(
                () => LocalizedStrings.Format("CreateSafeBootFormat",
                    _safeBoot.MinimalDisplayPath, _safeBoot.NetworkDisplayPath),
                LocalizedStrings.Get("CreateSafeBootTitle"), CreateSafeBootAtomically);
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
                throw BuildRollbackException(LocalizedStrings.Get("OperationSafeBootRepair"), operationError, rollbackErrors);
            }
        }

        private void DoReboot()
        {
            if (!TryBeginMutation()) return;

            try
            {
                if (!Confirm(LocalizedStrings.Get("RebootConfirmFormat"),
                        LocalizedStrings.Get("RebootConfirmTitle"))) return;
                _reboot.Reboot();
            }
            catch (Exception ex)
            {
                MessageBox.Show(LocalizedStrings.Format("RestartFailedFormat", ex.Message),
                    LocalizedStrings.Get("RestartFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
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
                MessageBox.Show(LocalizedStrings.Format("OperationFailedFormat", DescribeException(ex)),
                    LocalizedStrings.Get("OperationFailedTitle"), MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                EndMutation();
            }
        }

        private bool TryBeginMutation()
        {
            // Auch waehrend eines stillen Hintergrund-Refreshes keine Registry-Aenderung
            // starten, sonst koennte ein gerade gelesener Snapshot sie wieder uebermalen.
            if (!CanMutate || _isRefreshing) return false;
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
                errors.Add(LocalizedStrings.Format("RollbackFailedFormat", label, ex.Message));
                return false;
            }
        }

        private static Exception BuildRollbackException(string operation, Exception original,
            List<string> rollbackErrors)
        {
            var message = LocalizedStrings.Format("OperationFailedWithNameFormat", operation, original.Message);
            if (rollbackErrors.Count == 0)
                message += "\n\n" + LocalizedStrings.Get("RollbackComplete");
            else
                message += "\n\n" +
                           LocalizedStrings.Format("RollbackManualFormat",
                               string.Join("\n- ", rollbackErrors.ToArray()));
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
