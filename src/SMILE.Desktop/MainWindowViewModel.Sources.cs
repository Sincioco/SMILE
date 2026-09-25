using System.IO;
using Microsoft.Win32;
using SMILE.Engine;

namespace SMILE.Desktop;

public sealed partial class MainWindowViewModel
{
    private DesktopProjectSession? _projectSession;

    private void NewDocument()
    {
        CancelLiveTranspilation();

        // New is an editor reset, not a second request for the packaged
        // language reference. Advancing the revision even when the editor was
        // already empty prevents a pending startup read from winning the race
        // and putting language.smile back into the new document.
        _sourceRevision++;
        if (_sourceText.Length != 0)
        {
            _sourceText = string.Empty;
            OnPropertyChanged(nameof(SourceText));
        }

        _currentFilePath = null;
        _projectSession = null;
        ResetGeneratedTargetsForEmptySource();
    }

    private async Task<string?> LoadLanguageSourceAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _languageSourceReader(cancellationToken).ConfigureAwait(true);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!DesktopExceptionPolicy.IsFatal(ex))
        {
            string details = ReportException("Load language reference", ex, stage: LanguageFileName);
            AppendConciseError("Load language reference", ex, details, stage: LanguageFileName);
            return null;
        }
    }

    private async Task OpenAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "SMILE source or application project (*.smile;*.smileproj)|*.smile;*.smileproj|All files (*.*)|*.*",
            Title = "Open SMILE source or project"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await RunOperationAsync("Opening SMILE source or project", async cancellationToken =>
        {
            long revision = _sourceRevision;
            var opened = await Task.Run(() => DesktopProjectSession.Open(dialog.FileName), cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            if (revision != _sourceRevision)
            {
                AppendOutput("Open was not applied because the source changed during loading. Your edits were preserved.");
                return;
            }
            _currentFilePath = opened.SourcePath;
            _projectSession = opened.Session;
            if (SourceText == opened.Text) _sourceRevision++;
            else SourceText = opened.Text;
        }).ConfigureAwait(true);
        ScheduleLiveTranspilation();
    }

    private async Task SaveAsync()
    {
        if (_currentFilePath is null)
        {
            await SaveAsAsync().ConfigureAwait(true);
            return;
        }

        await File.WriteAllTextAsync(_currentFilePath, SourceText).ConfigureAwait(true);
        OperationStatus = $"Saved {Path.GetFileName(_currentFilePath)}";
    }

    private async Task SaveAsAsync()
    {
        var dialog = new SaveFileDialog
        {
            Filter = "SMILE source (*.smile)|*.smile|All files (*.*)|*.*",
            FileName = "PrintEverywhere.smile",
            Title = "Save SMILE source"
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        _currentFilePath = dialog.FileName;
        if (_projectSession is not null && !_projectSession.StartupPath.Equals(_currentFilePath, StringComparison.OrdinalIgnoreCase))
            _projectSession = null;
        await SaveAsync().ConfigureAwait(true);
        _sourceRevision++;
        ScheduleLiveTranspilation();
    }

    public async Task<SmileFormatResult?> FormatSourceAsync(string source)
    {
        SmileFormatResult? result = null;
        long revision = _sourceRevision;
        DesktopProjectSession? project = _projectSession;
        await RunOperationAsync("Formatting SMILE source", async cancellationToken =>
        {
            SmileFormatResult formatted = await Task.Run(() =>
            {
                if (project is null) return SmileSourceFormatter.Format(source);
                SmileCompilationInput input = project.Snapshot(source);
                return SmileSourceFormatter.Format(input.Sources[0], input.Sources);
            }, cancellationToken).ConfigureAwait(true);
            cancellationToken.ThrowIfCancellationRequested();
            result = formatted;
        }).ConfigureAwait(true);
        ScheduleLiveTranspilation();
        if (revision != _sourceRevision)
        {
            OperationStatus = "Source changed while formatting; current edits were preserved";
            return null;
        }
        return result;
    }
}
