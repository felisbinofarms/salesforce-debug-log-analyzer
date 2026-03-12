namespace SalesforceDebugAnalyzer.Services.Abstractions;

public enum DialogButton { OK, OKCancel, YesNo, YesNoCancel }
public enum DialogResult { None, OK, Cancel, Yes, No }
public enum DialogIcon { None, Info, Warning, Error, Question }

public interface IDialogService
{
    Task<DialogResult> ShowMessageAsync(string message, string title,
        DialogButton buttons = DialogButton.OK, DialogIcon icon = DialogIcon.None);

    Task<string?> ShowOpenFileDialogAsync(string title, string filter);
    Task<string?> ShowOpenFolderDialogAsync(string title);
    Task<string?> ShowSaveFileDialogAsync(string title, string filter, string defaultFileName);
}
