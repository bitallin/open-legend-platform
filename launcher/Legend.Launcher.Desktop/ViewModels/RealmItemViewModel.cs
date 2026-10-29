using CommunityToolkit.Mvvm.ComponentModel;
using Legend.Launcher.Core;

namespace Legend.Launcher.Desktop.ViewModels;

public partial class RealmItemViewModel : ObservableObject
{
    public RealmEntry Entry { get; }

    public RealmItemViewModel(RealmEntry entry)
    {
        Entry = entry;
        RefreshLatency();
    }

    public string RealmId => Entry.RealmId;
    public string RealmName => Entry.RealmName;
    public string Address => $"{Entry.Host}:{Entry.Port}";
    public string Status => Entry.Status;

    [ObservableProperty]
    private string _latencyText = "-";

    public void RefreshLatency()
    {
        LatencyText = Entry.LatencyMs is int ms ? $"{ms} ms" : "-";
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(Address));
        OnPropertyChanged(nameof(RealmName));
    }
}
