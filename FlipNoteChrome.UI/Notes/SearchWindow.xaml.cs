using System.Windows;
using System.Windows.Controls;
using FlipNoteChrome.Core.Storage;

namespace FlipNoteChrome.UI.Notes;

public partial class SearchWindow : Window
{
    private readonly SqliteNoteStore _store;
    public SearchWindow(SqliteNoteStore store)
    {
        InitializeComponent();
        _store = store;
        Refresh("");
    }
    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e) => Refresh(SearchBox.Text);
    private void Refresh(string q) => ResultsList.ItemsSource = _store.Search(q, 100);
    private void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new System.Windows.Forms.FolderBrowserDialog { Description = "Export all notes" };
        if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
        {
            _store.ExportAll(dlg.SelectedPath);
            MessageBox.Show($"Exported {_store.GetAll().Count} notes to {dlg.SelectedPath}");
        }
    }
    private void History_Click(object sender, RoutedEventArgs e)
    {
        HistoryPanel.Visibility = HistoryPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        if (ResultsList.SelectedItem is Note n) ShowHistory(n);
    }
    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ResultsList.SelectedItem is Note n && HistoryPanel.Visibility == Visibility.Visible) ShowHistory(n);
    }
    private void ShowHistory(Note n)
    {
        var h = _store.GetHistory(n.StableKey, 20);
        HistoryList.Items.Clear();
        if (h.Count == 0) HistoryList.Items.Add("(No history yet — edit and save to create versions)");
        else foreach (var (id, md, dt) in h) HistoryList.Items.Add($"{dt:yyyy-MM-dd HH:mm} — {md.Length} chars: {md[..Math.Min(80, md.Length)].Replace("\n"," ")}");
    }
}
