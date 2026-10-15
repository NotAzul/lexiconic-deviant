using System.Runtime.InteropServices;
using System.Windows;

namespace FenceForge.Services;

public sealed record ClipboardReadResult(string? Text, string? Error)
{
    public bool Succeeded => Text is not null;
}

public sealed class ClipboardService
{
    private const int Attempts = 5;

    public async Task<ClipboardReadResult> ReadTextAsync()
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                var data = System.Windows.Clipboard.GetDataObject();
                if (data is null)
                {
                    return new ClipboardReadResult(null, "The clipboard is empty.");
                }

                string? text = null;
                if (data.GetDataPresent(System.Windows.DataFormats.UnicodeText, autoConvert: true))
                {
                    text = data.GetData(System.Windows.DataFormats.UnicodeText, autoConvert: true) as string;
                }
                else if (data.GetDataPresent(System.Windows.DataFormats.Text, autoConvert: true))
                {
                    text = data.GetData(System.Windows.DataFormats.Text, autoConvert: true) as string;
                }

                return string.IsNullOrWhiteSpace(text)
                    ? new ClipboardReadResult(null, "The clipboard does not contain usable text.")
                    : new ClipboardReadResult(text, null);
            }
            catch (COMException) when (attempt + 1 < Attempts)
            {
                await Task.Delay(40 * (attempt + 1));
            }
            catch (ExternalException) when (attempt + 1 < Attempts)
            {
                await Task.Delay(40 * (attempt + 1));
            }
            catch (Exception error) when (error is COMException or ExternalException)
            {
                return new ClipboardReadResult(
                    null,
                    "The clipboard is temporarily unavailable. Close any application holding it and try again.");
            }
        }

        return new ClipboardReadResult(null, "The clipboard is temporarily unavailable.");
    }

    public async Task<string?> WriteTextAsync(string text)
    {
        for (var attempt = 0; attempt < Attempts; attempt++)
        {
            try
            {
                if (text.Length == 0)
                {
                    System.Windows.Clipboard.Clear();
                }
                else
                {
                    System.Windows.Clipboard.SetDataObject(text, copy: true);
                }

                return null;
            }
            catch (COMException) when (attempt + 1 < Attempts)
            {
                await Task.Delay(40 * (attempt + 1));
            }
            catch (ExternalException) when (attempt + 1 < Attempts)
            {
                await Task.Delay(40 * (attempt + 1));
            }
            catch (Exception error) when (error is COMException or ExternalException)
            {
                return "FenceForge could not write to the clipboard. The result is still available here; try Copy again.";
            }
        }

        return "FenceForge could not write to the clipboard. The result is still available here; try Copy again.";
    }
}
