namespace NvmeDriverSwitch.Models
{
    public enum PreflightSeverity
    {
        Info,
        Warning
    }

    /// <summary>Ein Befund der Vorflugpruefung. Blockiert nie, informiert nur.</summary>
    public sealed class PreflightIssue
    {
        public PreflightIssue(PreflightSeverity severity, string title, string detail)
        {
            Severity = severity;
            Title = title;
            Detail = detail;
        }

        public PreflightSeverity Severity { get; private set; }

        public string Title { get; private set; }

        public string Detail { get; private set; }
    }
}
