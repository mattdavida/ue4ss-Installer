using System.ComponentModel;
using System.Runtime.CompilerServices;
using Avalonia.Media.Imaging;

namespace UE4SSInstaller.Services;

public sealed class DetectedGame : INotifyPropertyChanged
{
    private string? _channelLabel;
    private string? _channelLabelTip;

    public required string Name { get; init; }
    public required string InstallPath { get; init; }
    public required string Win64Path { get; init; }
    public string? AppId { get; init; }
    public string? ExePath { get; init; }
    public Bitmap? Icon { get; init; }

    public string? ChannelLabel
    {
        get => _channelLabel;
        set
        {
            if (value == _channelLabel)
                return;

            _channelLabel = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChannelLabel));
        }
    }

    public string? ChannelLabelTip
    {
        get => _channelLabelTip;
        set
        {
            if (value == _channelLabelTip)
                return;

            _channelLabelTip = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(HasChannelLabelTip));
        }
    }

    public string Initial
    {
        get
        {
            foreach (var c in Name)
            {
                if (char.IsLetterOrDigit(c))
                    return char.ToUpperInvariant(c).ToString();
            }

            return "?";
        }
    }

    public bool HasIcon => Icon is not null;
    public bool HasChannelLabel => !string.IsNullOrEmpty(ChannelLabel);
    public bool HasChannelLabelTip => !string.IsNullOrEmpty(ChannelLabelTip);
    public string? SupportBadge => KnownSignatureCatalog.Find(this)?.SupportBadge;
    public bool HasSupportBadge => !string.IsNullOrEmpty(SupportBadge);

    public event PropertyChangedEventHandler? PropertyChanged;

    public void ApplyInstallState(InstallState state)
    {
        ChannelLabel = state.GameBadge;
        ChannelLabelTip = state.GameBadgeTip;
    }

    private void OnPropertyChanged([CallerMemberName] string? name = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
