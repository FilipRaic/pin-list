using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;

namespace PinList;

/// <summary>Shown while the user signs in to Google in their browser (spec 4.2).</summary>
static class SignInDialog
{
    public static async Task<bool> Run(Window? owner, Func<CancellationToken, Task<bool>> signIn)
    {
        using var cancel = new CancellationTokenSource();
        var dlg = new ConfirmDialog("Google sign-in", "Waiting for Google sign-in in your browser…",
            new[] { ("Cancel", DialogButtonKind.Normal) }, 0)
        {
            Topmost = false, // the user signs in in the browser meanwhile; this window mustn't cover it
        };
        var shown = DialogHost.Run(dlg, owner);
        var work = signIn(cancel.Token);
        if (await Task.WhenAny(shown, work) == shown) cancel.Cancel(); // Cancel or Esc
        else dlg.Close();
        return await work;
    }
}
