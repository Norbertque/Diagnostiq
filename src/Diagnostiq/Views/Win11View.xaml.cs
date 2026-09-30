using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Core;
using Diagnostiq.Core.Win11;
using Diagnostiq.Presentation;
using Wpf.Ui.Controls;

namespace Diagnostiq.Views;

/// <summary>Every Windows 11 requirement with its result and, for fixable ones, the vendor-specific fix.</summary>
public partial class Win11View : UserControl
{
    private readonly MainWindow _window;

    /// <param name="embedded">Shown inside Manual mode, which has its own navigation: no back button.</param>
    public Win11View(MainWindow window, SystemSnapshot snapshot, bool embedded = false)
    {
        InitializeComponent();
        _window = window;
        BackButton.Visibility = embedded ? Visibility.Collapsed : Visibility.Visible;
        var report = snapshot.Win11;
        var summary = HomeModel.Summarize(report, snapshot.Os.Value, snapshot.IsAdmin);
        DataContext = new { Summary = summary };
        NoteText.Visibility = summary.Note is null ? Visibility.Collapsed : Visibility.Visible;
        CheckList.ItemsSource = report.Checks.Select(c => new CheckRow(c)).ToList();

        var hints = snapshot.Device?.Hints;
        KeysText.Text = hints is null
            ? "Press the setup key while the laptop starts."
            : $"Press {hints.SetupKey} while the laptop starts. Boot menu: {hints.BootMenuKey}.";
        VendorNote.Text = hints?.Note ?? "";
        VendorNote.Visibility = hints?.Note is null ? Visibility.Collapsed : Visibility.Visible;

        // shutdown /fw only works on UEFI firmware and needs admin rights.
        bool canRestart = snapshot.Firmware.Value?.Uefi == true && snapshot.IsAdmin;
        RestartFirmwareButton.Visibility = canRestart ? Visibility.Visible : Visibility.Collapsed;

        SourceText.Text = $"Processor support is checked against {SupportedCpus.ListDate}. " +
                          "Microsoft's PC Health Check app gives the official answer.";
    }

    private void Back_Click(object sender, RoutedEventArgs e) => _window.ShowHome();

    private async void RestartFirmware_Click(object sender, RoutedEventArgs e)
    {
        var answer = await _window.AskAsync("Restart into BIOS setup?",
            "The laptop restarts straight into BIOS setup and Diagnostiq closes. Save the report first if you need this session's results, and save your work in other apps.",
            "Restart now");
        if (answer == ContentDialogResult.Primary) FirmwareProbe.RestartToFirmwareSetup();
    }

    public sealed record CheckRow(Win11Check Check)
    {
        public CheckState State => Check.State;
        public string Title => Check.Title;
        public string Detail => Check.Detail;
        public string? Hint => Check.Hint;
        public string HintLabel => Check.State == CheckState.Fail ? "How to fix: " : "Recommended: ";
        public Visibility HintVisibility => Check.Hint is null ? Visibility.Collapsed : Visibility.Visible;

        /// <summary>What a screen reader says for the row (the list reads items by their ToString): the status included.</summary>
        public override string ToString()
        {
            string status = State switch
            {
                CheckState.Pass => "met",
                CheckState.Warn => "met, with a warning",
                CheckState.Fail => "not met",
                _ => "couldn't check",
            };
            return $"{Title}: {status}. {Detail}{(Hint is null ? "" : " " + HintLabel + Hint)}";
        }
    }
}
