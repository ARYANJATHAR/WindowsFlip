using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using FlipNoteChrome.Core.Storage;
using FlipNoteChrome.Core.WindowIdentity;

namespace FlipNoteChrome.UI.Flip;

public partial class FlipWindow : Window
{
    private readonly SqliteNoteStore _store;
    private readonly WindowIdentity _identity;
    private readonly IntPtr _chromeHwnd;
    private readonly ChromeOverlayTracker _tracker;
    private readonly DispatcherTimer _saveTimer;
    private bool _isSaving;
    private bool _isClosingFlip;
    private bool _initialized;
    private bool _chromeRestored;

    public FlipWindow(IntPtr chromeHwnd, WindowIdentity identity, SqliteNoteStore store)
    {
        InitializeComponent();
        _chromeHwnd = chromeHwnd;
        _identity = identity;
        _store = store;
        _tracker = new ChromeOverlayTracker(this, _chromeHwnd);

        var note = _store.GetOrCreate(_identity);
        EditorBox.Text = note.Markdown;
        TitleText.Text = string.IsNullOrWhiteSpace(_identity.OriginalTitle) ? "Chrome Notes" : _identity.OriginalTitle.Length > 56 ? _identity.OriginalTitle[..56] + "…" : _identity.OriginalTitle;
        SubtitleText.Text = _identity.NormalizedTitle == "" ? "Alt+Click title bar to flip back" : _identity.NormalizedTitle;
        UpdateWordCount();

        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(800) };
        _saveTimer.Tick += (_, _) => _ = SaveAsync();
        _initialized = true;

        Loaded += async (_, _) =>
        {
            try
            {
                // Position directly over Chrome before hiding it
                if (FlipNoteChrome.Core.Interop.NativeMethods.GetWindowRect(_chromeHwnd, out var r0)
                    || FlipNoteChrome.Core.Interop.NativeMethods.DwmGetWindowAttribute(_chromeHwnd, FlipNoteChrome.Core.Interop.NativeMethods.DWMWA_EXTENDED_FRAME_BOUNDS, out r0, System.Runtime.InteropServices.Marshal.SizeOf<FlipNoteChrome.Core.Interop.NativeMethods.RECT>()) == 0)
                {
                    Left = r0.Left; Top = r0.Top; Width = r0.Width; Height = r0.Height;
                }
                _tracker.Start();
                System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"{DateTime.Now:HH:mm:ss} FlipWindow Loaded for {identity.StableKey[..8]} hwnd={chromeHwnd}\n");
                BitmapSource? captured = null;
                try { captured = FrontCaptureService.Capture(_chromeHwnd); } catch (Exception ex) { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"Capture ex {ex}\n"); }
                var settings = AppSettings.Load();
                bool useTrue3D = captured != null && settings.UseTrue3D;
                int dur = Math.Clamp(settings.AnimationMs, 200, 600);
                // Hide Chrome so only the flip shows — preserves exact bounds, removes peek-through (rounded corners)
                try { FlipNoteChrome.Core.Interop.NativeMethods.ShowWindow(_chromeHwnd, FlipNoteChrome.Core.Interop.NativeMethods.SW_HIDE); } catch { }
                _tracker.Stop(); // stop polling while hidden
                if (useTrue3D)
                {
                    try
                    {
                        FrontBrush.ImageSource = captured;
                        BackBrush.Visual = RootBorder;
                        FlipViewport.Visibility = Visibility.Visible;
                        RootBorder.Opacity = 0;
                        var animator = new Viewport3DFlipAnimator();
                        await animator.AnimateToFrontAsync(this, dur);
                        FlipViewport.Visibility = Visibility.Collapsed;
                        RootBorder.Opacity = 1;
                    }
                    catch (Exception ex)
                    {
                        System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"3D anim ex {ex}\n");
                        RenderTransformOrigin = new Point(0.5, 0.5);
                        RenderTransform = new ScaleTransform(0, 1);
                        await new ScaleFlipAnimator().AnimateToFrontAsync(this, dur);
                        RenderTransform = new ScaleTransform(1, 1);
                    }
                }
                else
                {
                    RenderTransformOrigin = new Point(0.5, 0.5);
                    RenderTransform = new ScaleTransform(0, 1);
                    await new ScaleFlipAnimator().AnimateToFrontAsync(this, dur);
                    RenderTransform = new ScaleTransform(1, 1);
                }
            }
            catch (Exception ex)
            {
                System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"Loaded fatal {ex}\n");
                FlipViewport.Visibility = Visibility.Collapsed;
                RootBorder.Opacity = 1;
                RenderTransform = new ScaleTransform(1, 1);
            }
            EditorBox.Focus();
            EditorBox.CaretIndex = EditorBox.Text.Length;
        };
        Closing += (_, _) => { _tracker.Dispose(); RestoreChrome(); };
    }

    private void EditorBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateWordCount();
        if (!_initialized) return;
        if (!_initialized) return;
        SaveIndicator.Text = "○ Unsaved";
        SaveIndicator.Foreground = new SolidColorBrush(Color.FromRgb(0xA8, 0xA2, 0x9E));
        _saveTimer.Stop(); _saveTimer.Start();
    }

    private void UpdateWordCount()
    {
        int words = string.IsNullOrWhiteSpace(EditorBox.Text) ? 0 : EditorBox.Text.Split([' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries).Length;
        WordCountText.Text = $"{words} words • {EditorBox.Text.Length} chars";
    }

    private Task SaveAsync()
    {
        _saveTimer.Stop();
        if (_isSaving) return Task.CompletedTask;
        _isSaving = true;
        try
        {
            _store.SaveWithHistory(_identity, EditorBox.Text);
            SaveIndicator.Text = "● Saved";
            SaveIndicator.Foreground = new SolidColorBrush(Color.FromRgb(0x86, 0xEF, 0xAC));
        }
        finally { _isSaving = false; }
        return Task.CompletedTask;
    }

    private void WrapSelection(string before, string after)
    {
        int start = EditorBox.SelectionStart;
        int length = EditorBox.SelectionLength;
        var replacement = before + EditorBox.SelectedText + after;
        EditorBox.SelectedText = replacement;
        EditorBox.SelectionStart = start + before.Length;
        EditorBox.SelectionLength = length;
        EditorBox.Focus();
    }

    private void PrefixLine(string prefix)
    {
        int caret = EditorBox.CaretIndex;
        int line = EditorBox.GetLineIndexFromCharacterIndex(caret);
        int start = EditorBox.GetCharacterIndexFromLineIndex(line);
        EditorBox.Text = EditorBox.Text.Insert(start, prefix);
        EditorBox.CaretIndex = caret + prefix.Length;
        EditorBox.Focus();
    }

    private void Heading_Click(object sender, RoutedEventArgs e) => PrefixLine("# ");
    private void List_Click(object sender, RoutedEventArgs e) => PrefixLine("- ");
    private void Bold_Click(object sender, RoutedEventArgs e) => WrapSelection("**", "**");
    private void Italic_Click(object sender, RoutedEventArgs e) => WrapSelection("*", "*");
    private void Strike_Click(object sender, RoutedEventArgs e) => WrapSelection("~~", "~~");
    private void Link_Click(object sender, RoutedEventArgs e) => WrapSelection("[", "](url)");
    private void Table_Click(object sender, RoutedEventArgs e)
    {
        const string table = "| Column 1 | Column 2 |\n| --- | --- |\n|  |  |";
        EditorBox.SelectedText = table;
        EditorBox.Focus();
    }
    private void ClearFormatting_Click(object sender, RoutedEventArgs e)
    {
        var text = EditorBox.SelectedText;
        if (string.IsNullOrEmpty(text)) return;
        text = text.Replace("**", "").Replace("~~", "").Replace("`", "").Replace("*", "");
        EditorBox.SelectedText = text;
        EditorBox.Focus();
    }

    private async void FlipBack_Click(object sender, RoutedEventArgs e) => await DoFlipBackAsync();
    private async void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        // Alt+Click or double-click on flipped window should flip back
        bool altDown = Keyboard.IsKeyDown(Key.LeftAlt) || Keyboard.IsKeyDown(Key.RightAlt) || (FlipNoteChrome.Core.Interop.NativeMethods.GetAsyncKeyState(FlipNoteChrome.Core.Interop.NativeMethods.VK_MENU) & 0x8000) != 0;
        if ((altDown && e.LeftButton == MouseButtonState.Pressed) || e.ClickCount == 2)
        {
            e.Handled = true;
            await DoFlipBackAsync();
            return;
        }
        // Drag only on plain left-click without Alt — avoid blocking flip hotkey
        if (e.LeftButton == MouseButtonState.Pressed && !altDown && e.ClickCount == 1)
        {
            try { DragMove(); } catch (Exception ex) { try { System.IO.File.AppendAllText(System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "FlipNoteChrome", "flip.log"), $"DragMove ex {ex.Message}\n"); } catch { } }
            e.Handled = true;
        }
    }

    public async Task DoFlipBackAsync()
    {
        if (_isClosingFlip) return;
        _isClosingFlip = true;
        await SaveAsync();
        var settings = AppSettings.Load();
        int dur = Math.Clamp(settings.AnimationMs, 200, 600);
        if (FlipViewport.Visibility == Visibility.Visible || FrontBrush.ImageSource != null)
        {
            try
            {
                FlipRotation.Angle = 0;
                BackBrush.Visual = RootBorder;
                FlipViewport.Visibility = Visibility.Visible;
                RootBorder.Opacity = 0;
                // FrontBrush still holds captured Chrome — flip back reveals it, then we show live Chrome
                var animator = new Viewport3DFlipAnimator();
                await animator.AnimateToBackAsync(this, dur);
                FlipRotation.Angle = 180;
            }
            catch { await new ScaleFlipAnimator().AnimateToBackAsync(this, dur); }
        }
        else
        {
            await new ScaleFlipAnimator().AnimateToBackAsync(this, Math.Max(220, dur - 70));
        }
        _tracker.Stop();
        // Restore Chrome exactly where it was (preserves maximize/Aero Snap)
        RestoreChrome();
        Close();
    }

    public void RestoreChrome()
    {
        if (_chromeRestored) return;
        _chromeRestored = true;
        try
        {
            FlipNoteChrome.Core.Interop.NativeMethods.ShowWindow(_chromeHwnd, FlipNoteChrome.Core.Interop.NativeMethods.SW_SHOW);
            FlipNoteChrome.Core.Interop.NativeMethods.SetWindowPos(_chromeHwnd, new IntPtr(-2), (int)Left, (int)Top, (int)Width, (int)Height, 0x0010);
            FlipNoteChrome.Core.Interop.NativeMethods.SetForegroundWindow(_chromeHwnd);
        }
        catch { }
    }

    protected override void OnPreviewKeyDown(KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { _ = DoFlipBackAsync(); e.Handled = true; }
        base.OnPreviewKeyDown(e);
    }
    protected override void OnDpiChanged(DpiScale oldDpi, DpiScale newDpi)
    {
        base.OnDpiChanged(oldDpi, newDpi);
        try { _tracker?.Stop(); _tracker?.Start(); } catch { }
    }
}
