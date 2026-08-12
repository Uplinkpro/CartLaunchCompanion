using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace CartLaunchCompanion.Configurator;

public sealed class CollectionGameEditor : INotifyPropertyChanged, IDisposable
{
    private Bitmap? _coverPreview;
    public required string Name { get; init; }
    public required string ConfigurationPath { get; init; }
    public Bitmap? CoverPreview { get => _coverPreview; set { _coverPreview?.Dispose(); _coverPreview = value; Changed(); Changed(nameof(HasCoverPreview)); } }
    public bool HasCoverPreview => CoverPreview is not null;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Dispose() => CoverPreview = null;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class CollectionShelfEditor : INotifyPropertyChanged
{
    private string _name = "";
    public string Name { get => _name; set { _name = value; Changed(); } }
    public ObservableCollection<CollectionGameEditor> Games { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
