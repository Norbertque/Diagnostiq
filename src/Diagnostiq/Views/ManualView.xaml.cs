using System.Windows;
using System.Windows.Controls;
using Diagnostiq.Core;
using Diagnostiq.Views.Manual;
using Wpf.Ui.Controls;

namespace Diagnostiq.Views;

/// <summary>Manual mode: a rail of sections, each page created on first visit.</summary>
public partial class ManualView : UserControl
{
    private readonly MainWindow _window;
    private readonly SystemSnapshot _snapshot;
    private readonly Dictionary<string, FrameworkElement> _pages = [];

    public ManualView(MainWindow window, SystemSnapshot snapshot)
    {
        InitializeComponent();
        _window = window;
        _snapshot = snapshot;
        Nav.ItemsSource = new NavItem[]
        {
            new("tests", "Tests", SymbolRegular.Toolbox24),
            new("hardware", "Hardware", SymbolRegular.DeveloperBoard24),
            new("win11", "Windows 11", SymbolRegular.Window24),
            new("windows", "Windows & security", SymbolRegular.ShieldKeyhole24),
            new("report", "Report", SymbolRegular.Document24),
        };
        Nav.SelectedIndex = 0;
    }

    public void Select(string key)
    {
        var item = Nav.Items.Cast<NavItem>().FirstOrDefault(i => i.Key == key);
        if (item is not null) Nav.SelectedItem = item;
    }

    private void Nav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (Nav.SelectedItem is not NavItem item) return;
        if (!_pages.TryGetValue(item.Key, out var page))
        {
            page = item.Key switch
            {
                "tests" => new TestsPage(_window),
                "hardware" => new HardwarePage(_snapshot, _window),
                "win11" => new Win11View(_window, _snapshot, embedded: true),
                "windows" => new WindowsPage(_snapshot),
                _ => new ReportPage(_window),
            };
            _pages[item.Key] = page;
        }
        Page.Content = page;
    }

    private void Home_Click(object sender, RoutedEventArgs e) => _window.ShowHome();

    private sealed record NavItem(string Key, string Title, SymbolRegular Icon);
}
