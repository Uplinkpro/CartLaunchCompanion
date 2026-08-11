namespace CartLaunchCompanion.Desktop.ViewModels;

public sealed class GameShelfViewModel(
    string name,
    IEnumerable<GameCardViewModel> games)
{
    public string Name { get; } = name;
    public bool HasName => !string.IsNullOrWhiteSpace(Name);
    public ObservableCollection<GameCardViewModel> Games { get; } = new(games);
}
