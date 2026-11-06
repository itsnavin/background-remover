using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BgRemover.Core;
using Microsoft.Win32;

namespace BgRemover.App;

public partial class MainWindow : Window
{
    private static readonly string[] Exts = { ".png", ".jpg", ".jpeg", ".webp", ".bmp", ".gif", ".tif", ".tiff" };

    private readonly ObservableCollection<FileItem> _items = new();
    private readonly SemaphoreSlim _modelLock = new(1, 1);
    private readonly string _modelPath = Path.Combine(AppContext.BaseDirectory, "models", "isnet-general-use.onnx");
    private readonly BackgroundSpec _bg = new();
    private OnnxMatteModel? _model;
    private volatile bool _useGpu = true, _useFlip;
    private FileItem? _cur;
    private RenderOutput? _output;
    private int _renderVersion, _selectVersion;
    private bool _ready, _panning, _painting;
    private Point _panStart, _panOrigin;
    private CancellationTokenSource? _batchCts;
    private bool _batchRunning;
    private readonly System.Windows.Threading.DispatcherTimer _renderTimer = new() { Interval = TimeSpan.FromMilliseconds(120) };

    public MainWindow()
    {
        InitializeComponent();
        FileList.ItemsSource = _items;
        _renderTimer.Tick += (_, _) => { _renderTimer.Stop(); _ = RenderAsync(); };
        _ready = true;
    }

    // ---------- file handling ----------

    private void AddFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p))
                files.AddRange(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories).Where(IsImage));
            else if (File.Exists(p) && IsImage(p)) files.Add(p);
        }
        foreach (var f in files.Where(f => _items.All(i => !string.Equals(i.Path, f, StringComparison.OrdinalIgnoreCase))))
            _items.Add(new FileItem(f));
        if (FileList.SelectedItem == null && _items.Count > 0) FileList.SelectedIndex = 0;
        Hint.Visibility = _items.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private static bool IsImage(string p) => Exts.Contains(Path.GetExtension(p).ToLowerInvariant());

    private void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] paths) AddFiles(paths);
    }

    private void Open_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Multiselect = true,
            Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.gif;*.tif;*.tiff|All files|*.*",
        };
        if (dlg.ShowDialog(this) == true) AddFiles(dlg.FileNames);
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        if (_batchRunning) return;
        _items.Clear();
        _cur = null; _output = null;
        MainImage.Source = null; OriginalImage.Source = null;
        Hint.Visibility = Visibility.Visible;
    }

    // ---------- model + processing ----------

    private OnnxMatteModel GetModel()
    {
        if (_model != null) return _model;
        if (!File.Exists(_modelPath))
            throw new FileNotFoundException("Model file not found: " + _modelPath +
                "\nReinstall the app or place isnet-general-use.onnx in the 'models' folder next to the executable.");
        _model = new OnnxMatteModel(_modelPath, _useGpu) { FlipAveraging = _useFlip };
        var desc = _model.Description; Dispatcher.BeginInvoke(() => ProviderText.Text = "Execution provider: " + desc);
        return _model;
    }

    private void ResetModel()
    {
        if (_batchRunning) return;
        _modelLock.Wait();
        try { _model?.Dispose(); _model = null; }
        finally { _modelLock.Release(); }
        foreach (var i in _items) if (i != _cur && i.Doc is { HasEdits: false }) { i.Doc = null; i.Status = "Queued"; }
    }

    private void Gpu_Click(object sender, RoutedEventArgs e) { _useGpu = GpuCheck.IsChecked == true; ResetModel(); }
    private void Flip_Click(object sender, RoutedEventArgs e) { _useFlip = FlipCheck.IsChecked == true; ResetModel(); }

    private async Task<MatteDocument> EnsureDocAsync(FileItem item, CancellationToken ct)
    {
        if (item.Doc != null) return item.Doc;
        item.Status = "Processing…";
        var doc = await Task.Run(() =>
        {
            var img = ImageIO.Load(item.Path);
            ct.ThrowIfCancellationRequested();
            _modelLock.Wait(ct);
            try
            {
                var alpha = Matting.ComputeAlpha(img, GetModel(), RefineOptions.Default, ct);
                return new MatteDocument(img, alpha);
            }
            finally { _modelLock.Release(); }
        }, ct);
        item.Doc = doc;
        item.Status = "Done";
        return doc;
    }

    private async void FileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FileList.SelectedItem is not FileItem item) return;
        int ver = ++_selectVersion;
        if (_cur != null && _cur != item && _cur.Doc is { HasEdits: false }) { _cur.Doc = null; _cur.Status = "Queued"; }
        _cur = item;
        _output = null;
        ShowBusy("Removing background…");
        try
        {
            var doc = await EnsureDocAsync(item, CancellationToken.None);
            if (ver != _selectVersion) return;
            OriginalImage.Source = ToSource(doc.Source);
            await RenderAsync();
            FitToView();
            SetStatus($"{item.Name} — {doc.Source.Width}×{doc.Source.Height}");
        }
        catch (Exception ex)
        {
            item.Status = "Failed";
            SetStatus("Error: " + ex.Message);
            if (ver == _selectVersion) MessageBox.Show(this, ex.Message, "Could not process image", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally { if (ver == _selectVersion) HideBusy(); }
    }

    // ---------- settings / rendering ----------

    private EditSettings ReadSettings()
    {
        _bg.Kind = BgSolid.IsChecked == true ? BackgroundKind.Solid
                 : BgImage.IsChecked == true && _bg.Image != null ? BackgroundKind.Image
                 : BackgroundKind.Transparent;
        var hex = BgHex.Text.Trim().TrimStart('#');
        if (hex.Length == 6 && int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int rgb))
        {
            _bg.R = (byte)(rgb >> 16); _bg.G = (byte)(rgb >> 8); _bg.B = (byte)rgb;
        }
        return new EditSettings
        {
            Feather = (float)FeatherSlider.Value,
            EdgeShift = (float)ShiftSlider.Value,
            Decontaminate = DecontCheck.IsChecked == true,
            Background = _bg.Clone(),
        };
    }

    private void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ScheduleRender();
    }

    private void ScheduleRender() { _renderTimer.Stop(); _renderTimer.Start(); }

    private async Task RenderAsync()
    {
        var doc = _cur?.Doc;
        if (doc == null) return;
        int ver = ++_renderVersion;
        var settings = ReadSettings();
        try
        {
            var (src, alphaSrc) = await Task.Run(() =>
            {
                var o = doc.Render(settings);
                return (ToSource(o.Image), o);
            });
            if (ver != _renderVersion || doc != _cur?.Doc) return;
            _output = alphaSrc;
            MainImage.Source = src;
            UpdateViewMode();
        }
        catch (Exception ex) { SetStatus("Render error: " + ex.Message); }
    }

    private static BitmapSource ToSource(Bitmap32 b)
    {
        var bs = BitmapSource.Create(b.Width, b.Height, 96, 96, PixelFormats.Pbgra32, null, b.ToPremultiplied(), b.Width * 4);
        bs.Freeze();
        return bs;
    }

    private void ViewMode_Changed(object sender, RoutedEventArgs e) { if (_ready) UpdateViewMode(); }
    private void Split_Changed(object sender, RoutedPropertyChangedEventArgs<double> e) { if (_ready) UpdateViewMode(); }

    private void UpdateViewMode()
    {
        if (MainImage.Source is not BitmapSource main) return;
        double w = main.PixelWidth, h = main.PixelHeight;
        MainImage.Width = w; MainImage.Height = h;
        OriginalImage.Width = w; OriginalImage.Height = h;
        OriginalImage.Clip = null;
        OriginalImage.Visibility = Visibility.Collapsed;

        if (ViewMask.IsChecked == true && _output != null)
        {
            var mask = new Bitmap32(_output.Image.Width, _output.Image.Height, Bitmap32.MaskToBgra(_output.Alpha));
            OriginalImage.Source = ToSource(mask);
            OriginalImage.Visibility = Visibility.Visible;
        }
        else if (ViewOriginal.IsChecked == true || ViewSplit.IsChecked == true)
        {
            if (_cur?.Doc != null) OriginalImage.Source = ToSource(_cur.Doc.Source);
            OriginalImage.Visibility = Visibility.Visible;
            if (ViewSplit.IsChecked == true)
                OriginalImage.Clip = new RectangleGeometry(new Rect(0, 0, w * SplitSlider.Value, h));
        }
    }

    // ---------- viewer ----------

    private void Fit_Click(object sender, RoutedEventArgs e) => FitToView();
    private void Viewer_SizeChanged(object sender, SizeChangedEventArgs e) { }

    private void FitToView()
    {
        if (MainImage.Source is not BitmapSource s) return;
        double vw = Viewer.ActualWidth, vh = Viewer.ActualHeight;
        if (vw < 10 || vh < 10) return;
        double z = Math.Min(vw / s.PixelWidth, vh / s.PixelHeight) * 0.97;
        Zoom.ScaleX = Zoom.ScaleY = z;
        Pan.X = (vw - s.PixelWidth * z) / 2;
        Pan.Y = (vh - s.PixelHeight * z) / 2;
    }

    private void Viewer_MouseWheel(object sender, MouseWheelEventArgs e)
    {
        var p = e.GetPosition(Viewer);
        double z0 = Zoom.ScaleX, z1 = Math.Clamp(z0 * (e.Delta > 0 ? 1.15 : 1 / 1.15), 0.02, 64);
        Pan.X = p.X - (p.X - Pan.X) * z1 / z0;
        Pan.Y = p.Y - (p.Y - Pan.Y) * z1 / z0;
        Zoom.ScaleX = Zoom.ScaleY = z1;
        UpdateCursor(p);
    }

    private bool BrushActive => ToolKeep.IsChecked == true || ToolRemove.IsChecked == true;

    private void Tool_Changed(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        Viewer.Cursor = BrushActive ? Cursors.None : Cursors.Arrow;
        BrushCursor.Visibility = Visibility.Collapsed;
    }

    private void Viewer_MouseDown(object sender, MouseButtonEventArgs e)
    {
        var doc = _cur?.Doc;
        if (e.ChangedButton == MouseButton.Right || e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && !BrushActive))
        {
            _panning = true; _panStart = e.GetPosition(Viewer); _panOrigin = new Point(Pan.X, Pan.Y);
            Viewer.CaptureMouse();
            return;
        }
        if (e.ChangedButton == MouseButton.Left && BrushActive && doc != null)
        {
            doc.BeginStroke();
            _painting = true;
            Viewer.CaptureMouse();
            Stamp(e.GetPosition(Stage));
        }
    }

    private void Viewer_MouseMove(object sender, MouseEventArgs e)
    {
        var p = e.GetPosition(Viewer);
        if (_panning)
        {
            Pan.X = _panOrigin.X + (p.X - _panStart.X);
            Pan.Y = _panOrigin.Y + (p.Y - _panStart.Y);
        }
        else if (_painting) Stamp(e.GetPosition(Stage));
        UpdateCursor(p);
    }

    private void Viewer_MouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_painting) { _painting = false; ScheduleRender(); }
        _panning = false;
        Viewer.ReleaseMouseCapture();
    }

    private void Viewer_MouseLeave(object sender, MouseEventArgs e) => BrushCursor.Visibility = Visibility.Collapsed;


    private void Stamp(Point imagePoint)
    {
        var doc = _cur?.Doc;
        if (doc == null) return;
        double r = BrushSize.Value / 2 / Zoom.ScaleX;
        bool keep = ToolKeep.IsChecked == true;
        doc.Stamp(imagePoint.X, imagePoint.Y, r, BrushHardness.Value, keep);
        ScheduleRender();
    }

    private void UpdateCursor(Point viewerPoint)
    {
        if (!BrushActive) { BrushCursor.Visibility = Visibility.Collapsed; return; }
        double d = BrushSize.Value;
        BrushCursor.Width = d; BrushCursor.Height = d;
        BrushCursor.Stroke = ToolKeep.IsChecked == true ? Brushes.LimeGreen : Brushes.Red;
        Canvas.SetLeft(BrushCursor, viewerPoint.X - d / 2);
        Canvas.SetTop(BrushCursor, viewerPoint.Y - d / 2);
        BrushCursor.Visibility = Visibility.Visible;
    }

    // ---------- edits ----------

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Z && Keyboard.Modifiers == ModifierKeys.Control && Keyboard.FocusedElement is not TextBox)
        {
            Undo_Click(sender, e); e.Handled = true;
        }
    }

    private void Undo_Click(object sender, RoutedEventArgs e)
    {
        if (_cur?.Doc?.Undo() == true) ScheduleRender();
    }

    private void ResetEdits_Click(object sender, RoutedEventArgs e)
    {
        var doc = _cur?.Doc;
        if (doc == null) return;
        doc.BeginStroke();
        Array.Fill(doc.Edits, (byte)128);
        ScheduleRender();
    }

    private void PickBgImage_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            _bg.Image = ImageIO.Load(dlg.FileName);
            BgImage.IsChecked = true;
            ScheduleRender();
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Could not load background"); }
    }

    // ---------- export ----------

    private ExportFormat SelectedFormat => (ExportFormat)Math.Max(0, FormatBox.SelectedIndex);

    private static EditSettings ForFormat(EditSettings s, ExportFormat f)
    {
        if (f == ExportFormat.Jpeg && s.Background.Kind == BackgroundKind.Transparent)
        {
            s.Background = new BackgroundSpec { Kind = BackgroundKind.Solid, R = 255, G = 255, B = 255 };
        }
        return s;
    }

    private async void SaveCurrent_Click(object sender, RoutedEventArgs e)
    {
        if (_cur?.Doc == null) { SetStatus("Open an image first."); return; }
        var fmt = SelectedFormat;
        var dlg = new SaveFileDialog
        {
            FileName = Path.GetFileNameWithoutExtension(_cur.Path) + "_nobg" + ImageIO.Extension(fmt),
            Filter = fmt switch { ExportFormat.Png => "PNG|*.png", ExportFormat.Jpeg => "JPEG|*.jpg", _ => "WebP|*.webp" },
        };
        if (dlg.ShowDialog(this) != true) return;
        var doc = _cur.Doc; var settings = ForFormat(ReadSettings(), fmt); var path = dlg.FileName;
        ShowBusy("Saving…");
        try
        {
            await Task.Run(() => ImageIO.Save(path, doc.Render(settings).Image, fmt));
            SetStatus("Saved " + path);
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "Save failed"); }
        finally { HideBusy(); }
    }

    private async void ExportAll_Click(object sender, RoutedEventArgs e)
    {
        if (_items.Count == 0 || _batchRunning) return;
        var dlg = new OpenFolderDialog { Title = "Choose output folder" };
        if (dlg.ShowDialog(this) != true) return;
        var outDir = dlg.FolderName;
        var fmt = SelectedFormat;
        var settings = ForFormat(ReadSettings(), fmt);
        var items = _items.ToList();

        _batchRunning = true; ExportAllBtn.IsEnabled = false; CancelBtn.IsEnabled = true;
        _batchCts = new CancellationTokenSource();
        var ct = _batchCts.Token;
        int ok = 0, failed = 0;
        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var item = items[i];
                SetStatus($"Exporting {i + 1}/{items.Count}: {item.Name}");
                Progress.Value = (double)i / items.Count;
                try
                {
                    bool keep = item.Doc != null;
                    var doc = await EnsureDocAsync(item, ct);
                    var target = UniquePath(outDir, Path.GetFileNameWithoutExtension(item.Path) + "_nobg", ImageIO.Extension(fmt));
                    await Task.Run(() => ImageIO.Save(target, doc.Render(settings.Clone()).Image, fmt), ct);
                    item.Status = "Exported";
                    if (!keep && item != _cur && !doc.HasEdits) item.Doc = null;
                    ok++;
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { item.Status = "Failed: " + ex.Message; failed++; }
            }
            SetStatus($"Exported {ok} image(s) to {outDir}" + (failed > 0 ? $", {failed} failed" : ""));
        }
        catch (OperationCanceledException) { SetStatus($"Cancelled after {ok} image(s)."); }
        finally
        {
            Progress.Value = 0; _batchRunning = false; ExportAllBtn.IsEnabled = true; CancelBtn.IsEnabled = false;
            _batchCts?.Dispose(); _batchCts = null;
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => _batchCts?.Cancel();

    private static string UniquePath(string dir, string name, string ext)
    {
        var p = Path.Combine(dir, name + ext);
        for (int n = 2; File.Exists(p); n++) p = Path.Combine(dir, $"{name}_{n}{ext}");
        return p;
    }

    // ---------- misc ----------

    private void ShowBusy(string text) { BusyText.Text = text; Busy.Visibility = Visibility.Visible; }
    private void HideBusy() => Busy.Visibility = Visibility.Collapsed;
    private void SetStatus(string text) => StatusText.Text = text;

    protected override void OnClosed(EventArgs e)
    {
        _batchCts?.Cancel();
        _model?.Dispose();
        base.OnClosed(e);
    }
}
