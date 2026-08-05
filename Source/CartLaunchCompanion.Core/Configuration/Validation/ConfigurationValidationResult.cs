namespace CartLaunchCompanion.Core.Configuration.Validation;

public sealed class ConfigurationValidationResult
{
    public List<ConfigurationValidationIssue> Issues { get; } = [];

    public bool IsValid =>
        Issues.All(issue => issue.Severity != ValidationSeverity.Error);

    public IEnumerable<ConfigurationValidationIssue> Errors =>
        Issues.Where(issue => issue.Severity == ValidationSeverity.Error);

    public IEnumerable<ConfigurationValidationIssue> Warnings =>
        Issues.Where(issue => issue.Severity == ValidationSeverity.Warning);
}

public sealed record ConfigurationValidationIssue(
    string Path,
    string Message,
    ValidationSeverity Severity);

public enum ValidationSeverity
{
    Warning,
    Error
}
