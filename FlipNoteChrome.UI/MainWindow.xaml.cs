using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using FlipNoteChrome.Core.Storage;
using FlipNoteChrome.Core.WindowIdentity;
using Microsoft.Win32;

namespace FlipNoteChrome.UI;

public partial class MainWindow : Window
{
    private readonly SqliteNoteStore _store = new();
    private AppSettings _settings = AppSettings.Load();
    private bool _loading;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += (_, _) => LoadAll();
        Closing += (s, e) =>
        {
            if (s is Window w && Application.Current is App)
            {
                e.Cancel = true;
                w.Hide();
            }
        };
    }

    private void LoadAll()
    {
        _loading = true;
        _settings = AppSettings.Load();
        DbPathText.Text = _store.DbPath;
        NoteCountText.Text = $"{_store.GetAll().Count} notes";
        RefreshChromeList();

        AnimSlider.Value = Math.Clamp(_settings.AnimationMs, 200, 600);
        AnimLabel.Text = $"{(int)AnimSlider.Value} ms";
        True3DCheck.IsChecked = _settings.UseTrue3D;
        AlwaysOnTopCheck.IsChecked = _settings.AlwaysOnTop;
        AutoStartCheck.IsChecked = _settings.AutoStart;
        ExcludeBox.Text = string.Join(", ", _settings.ExcludeList);

        var fonts = new[] { "Segoe UI", "Cascadia Code", "Consolas", "Arial" };
        FontBox.SelectedIndex = Math.Max(0, Array.IndexOf(fonts, _settings.FontFamily));
        var sizes = new[] { "12", "14", "16", "18" };
        FontSizeBox.SelectedIndex = Math.Max(1, Array.IndexOf(sizes, _settings.FontSize.ToString("0")));
        _loading = false;
    }

    private void RefreshChromeList()
    {
        var list = WindowIdentityService.EnumerateChromeWindows();
        ChromeList.Items.Clear();
        if (list.Count == 0) ChromeList.Items.Add("(No Chrome windows found - open Chrome)");
        else foreach (var (hwnd, id) in list)
            ChromeList.Items.Add($"{id.OriginalTitle}  [{id.StableKey[..8]}]");
    }

    private void Search_Click(object sender, RoutedEventArgs e) => new Notes.SearchWindow(_store).Show();
    private void Refresh_Click(object sender, RoutedEventArgs e) { _store.GetAll(); LoadAll(); }
    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        var dir = Path.GetDirectoryName(_store.DbPath)!;
        Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
    }
    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Export all notes as .md files" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _store.ExportAll(dlg.SelectedPath);
            MessageBox.Show($"Exported {_store.GetAll().Count} notes to {dlg.SelectedPath}");
        }
    }
    private void Hide_Click(object sender, RoutedEventArgs e) { SaveSettings(); Hide(); }

    private void AnimSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (AnimLabel != null) AnimLabel.Text = $"{(int)e.NewValue} ms";
        if (!_loading) { _settings.AnimationMs = (int)e.NewValue; _settings.Save(); }
    }

    private void SettingChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        SaveSettings();
    }
    private void SettingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading) return;
        SaveSettings();
    }

    private void SaveSettings()
    {
        _settings.AnimationMs = (int)AnimSlider.Value;
        _settings.UseTrue3D = True3DCheck.IsChecked == true;
        _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _settings.AutoStart = AutoStartCheck.IsChecked == true;
        if (FontBox.SelectedItem is ComboBoxItem fi) _settings.FontFamily = fi.Content.ToString()!;
        if (FontSizeBox.SelectedItem is ComboBoxItem si && double.TryParse(si.Content.ToString(), out var s)) _settings.FontSize = s;
        _settings.ExcludeList = ExcludeBox.Text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        _settings.Save();
        // Auto-start registry
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", true);
            string exe = Process.GetCurrentProcess().MainModule?.FileName ?? "";
            // Use publish exe path if available
            var pubExe = Path.Combine(AppContext.BaseDirectory, "FlipNoteChrome.UI.exe");
            if (File.Exists(pubExe)) exe = pubExe;
            if (_settings.AutoStart) key?.SetValue("FlipNoteChrome", $"\"{exe}\"");
            else key?.DeleteValue("FlipNoteChrome", false);
        }
        catch { }
    }
}
