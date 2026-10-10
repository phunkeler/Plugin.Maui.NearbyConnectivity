using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using NearbyChat.Services;
using Plugin.Maui.BottomSheet.Navigation;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.ViewModels;

/// <summary>
/// The sheet behind the Home page's presence row: edits the name and the avatar nearby devices
/// see. While anything runs it disables Save and offers to stop everything instead, so a change
/// is never left waiting for the session to go idle.
/// </summary>
/// <remarks>
/// The sheet checks only for a blank name, so it can say so. The byte limit is
/// <see cref="NearbyPresence"/>'s own: the record throws on a name over
/// <see cref="NearbyPresence.MaxDisplayNameBytes"/> UTF-8 bytes, and the sheet words it for a user.
/// The avatar goes into <see cref="NearbyPresence.Info"/> through <see cref="PresenceAvatar"/>.
/// </remarks>
public partial class PresenceViewModel : ObservableObject, INavigationAware
{
    /// <summary>
    /// The navigation parameter that carries an <see cref="Action"/> to run after a save, so the
    /// page that opened the sheet can refresh what it shows without the sheet knowing the page.
    /// </summary>
    public const string OnSavedParameter = "OnSaved";

    readonly IBottomSheetNavigationService _bottomSheetNavigationService;
    readonly INearbyConnectivity _nearby;
    readonly NearbySessionState _status;
    readonly ConnectionTracker _connectionTracker;
    Action? _onSaved;

    public PresenceViewModel(
        IBottomSheetNavigationService bottomSheetNavigationService,
        INearbyConnectivity nearby,
        NearbySessionState status,
        ConnectionTracker connectionTracker)
    {
        ArgumentNullException.ThrowIfNull(bottomSheetNavigationService);
        ArgumentNullException.ThrowIfNull(nearby);
        ArgumentNullException.ThrowIfNull(status);
        ArgumentNullException.ThrowIfNull(connectionTracker);

        _bottomSheetNavigationService = bottomSheetNavigationService;
        _nearby = nearby;
        _status = status;
        _connectionTracker = connectionTracker;

        Faces = Enumerable.Range(0, PresenceAvatar.FaceCount)
            .Select(i => new AvatarTile(
                i,
                PresenceAvatar.FaceGlyph(i),
                PresenceAvatar.CornerFraction(i),
                PresenceAvatar.GalleryColorIndex(i),
                PresenceAvatar.DescribeFace(i),
                new RelayCommand(() => Face = i)))
            .ToList();
        Palette = Enumerable.Range(0, PresenceAvatar.Palette.Count)
            .Select(i => new AvatarTile(
                i,
                string.Empty,
                0.5,
                i,
                PresenceAvatar.DescribeColor(i),
                new RelayCommand(() => Color = i)))
            .ToList();
    }

    /// <summary>
    /// The name being edited. Save stays disabled while it is blank.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string Name { get; set; } = string.Empty;

    /// <summary>
    /// The chosen face, an index into <see cref="Faces"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Choice))]
    public partial int Face { get; set; }

    /// <summary>
    /// The chosen color, an index into <see cref="Palette"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(Choice))]
    public partial int Color { get; set; } = 2;

    /// <summary>
    /// The face and color together, what the preview and Save use.
    /// </summary>
    public AvatarChoice Choice => new(Face, Color);

    public IReadOnlyList<AvatarTile> Faces { get; }

    public IReadOnlyList<AvatarTile> Palette { get; }

    /// <summary>
    /// Why the last save was refused, or <see langword="null"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasError))]
    public partial string? Error { get; private set; }

    /// <summary>
    /// Whether <see cref="Error"/> is set.
    /// </summary>
    public bool HasError => Error is not null;

    /// <summary>
    /// What keeps the session from taking a new presence now, or <see langword="null"/> when idle.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBusy), nameof(CanSave))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    public partial string? BusyReason { get; private set; }

    /// <summary>
    /// Whether the session is running anything, which disables Save.
    /// </summary>
    public bool IsBusy => BusyReason is not null;

    /// <inheritdoc/>
    public void OnNavigatedTo(IBottomSheetNavigationParameters parameters)
    {
        _onSaved = parameters.TryGetValue(OnSavedParameter, out var onSaved)
            ? onSaved as Action
            : null;

        var current = _nearby.Presence;
        Name = current.DisplayName;

        if (PresenceAvatar.Decode(current.Info) is { } choice)
        {
            Face = choice.Face;
            Color = choice.Color;
        }

        Error = null;
        _status.PropertyChanged += OnSessionChanged;
        _connectionTracker.Connections.PropertyChanged += OnSessionChanged;
        RefreshBusy();
    }

    /// <inheritdoc/>
    public void OnNavigatedFrom(IBottomSheetNavigationParameters parameters)
    {
        _status.PropertyChanged -= OnSessionChanged;
        _connectionTracker.Connections.PropertyChanged -= OnSessionChanged;
    }

    void OnSessionChanged(object? sender, PropertyChangedEventArgs e) => RefreshBusy();

    void RefreshBusy()
    {
        if (_status.IsAdvertising)
        {
            BusyReason = "Advertising is on";
        }
        else if (_status.IsDiscovering)
        {
            BusyReason = "Discovery is on";
        }
        else
        {
            BusyReason = _connectionTracker.Connections.Count switch
            {
                0 => null,
                1 => "1 connection open",
                var count => $"{count} connections open",
            };
        }
    }

    partial void OnFaceChanged(int value)
    {
        foreach (var tile in Faces)
        {
            tile.IsSelected = tile.Index == value;
        }
    }

    partial void OnColorChanged(int value)
    {
        foreach (var tile in Palette)
        {
            tile.IsSelected = tile.Index == value;
        }
    }

    [RelayCommand]
    void SurpriseMe()
    {
        Face = Random.Shared.Next(PresenceAvatar.FaceCount);
        Color = Random.Shared.Next(PresenceAvatar.Palette.Count);
    }

    /// <summary>
    /// Stops advertising, discovery and every connection, which is what enables Save.
    /// </summary>
    [RelayCommand]
    async Task StopAll()
    {
        await _nearby.StopAllAsync();
        RefreshBusy();
    }

    /// <summary>
    /// Whether Save can run: the session is idle and the name is not blank. The two reasons it
    /// cannot are both visible, the banner and the empty field, so Save needs no message of its own.
    /// </summary>
    public bool CanSave => !IsBusy && !string.IsNullOrWhiteSpace(Name);

    [RelayCommand(CanExecute = nameof(CanSave))]
    async Task Save()
    {
        var name = Name.Trim();
        RefreshBusy();

        if (name.Length == 0 || IsBusy)
        {
            return;
        }

        try
        {
            _nearby.SetPresence(_nearby.Presence with
            {
                DisplayName = name,
                Info = PresenceAvatar.Encode(new AvatarChoice(Face, Color)),
            });
        }
        catch (ArgumentException)
        {
            Error = $"Enter a name of up to {NearbyPresence.MaxDisplayNameBytes} bytes. "
                + "Accented letters, other scripts and emoji take 2 to 4 bytes each.";

            return;
        }

        Error = null;
        _onSaved?.Invoke();
        await _bottomSheetNavigationService.GoBackAsync();
    }

    [RelayCommand]
    async Task Close() => await _bottomSheetNavigationService.GoBackAsync();
}
