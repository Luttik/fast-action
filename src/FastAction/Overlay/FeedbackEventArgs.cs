using Microsoft.UI.Xaml.Controls;

namespace FastAction.Overlay;

public sealed class FeedbackEventArgs : EventArgs
{
    public FeedbackEventArgs(string title, string message, InfoBarSeverity severity)
    {
        Title = title;
        Message = message;
        Severity = severity;
    }

    public string Title { get; }

    public string Message { get; }

    public InfoBarSeverity Severity { get; }
}
