using System.ComponentModel;
using BgRemover.Core;

namespace BgRemover.App;

public sealed class FileItem : INotifyPropertyChanged
{
    private string _status = "Queued";

    public FileItem(string path) { Path = path; }

    public string Path { get; }
    public string Name => System.IO.Path.GetFileName(Path);
    public MatteDocument? Doc { get; set; }

    public string Status
    {
        get => _status;
        set { _status = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Status))); }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
