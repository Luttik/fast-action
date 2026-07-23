@{
    Severity     = @('Error', 'Warning')

    ExcludeRules = @(
        # This is a personal tool run interactively from a known local path;
        # Write-Host is used intentionally for colored console feedback.
        'PSAvoidUsingWriteHost'
    )
}
