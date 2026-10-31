using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Xunit;

namespace PinList.Tests;

public class ReorderTests : UiTest
{
    [AvaloniaFact]
    public void Rows_slide_from_odd_offsets_in_any_language()
    {
        var culture = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo("hr-HR"); // decimal comma
        var panel = new Canvas { Width = 200, Height = 200 };
        var row = new Border { Tag = "row", Width = 50, Height = 20 };
        Canvas.SetLeft(row, 10);
        Canvas.SetTop(row, 0);
        panel.Children.Add(row);
        var w = new Window { Content = panel, UseLayoutRounding = false };
        panel.UseLayoutRounding = row.UseLayoutRounding = false; // keep the odd offsets
        try
        {
            w.Show();
            Pump();
            var before = Reorder.Capture(panel, c => c is Border);
            // a tiny sideways difference (printed as 1E-14 by default) and a fractional one
            Canvas.SetLeft(row, 10 - 1e-14);
            Canvas.SetTop(row, -40.5);
            Pump();
            UiHost.UnhandledErrors.Clear();
            Reorder.Animate(panel, before, c => c is Border);
            Pump();
            Assert.Empty(UiHost.UnhandledErrors);
        }
        finally
        {
            w.Close();
            CultureInfo.CurrentCulture = culture;
        }
    }
}
