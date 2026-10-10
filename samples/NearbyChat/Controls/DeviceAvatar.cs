using CommunityToolkit.Maui;
using Microsoft.Maui.Controls.Shapes;
using NearbyChat.Services;
using Plugin.Maui.NearbyConnectivity;

namespace NearbyChat.Controls;

/// <summary>
/// A device's avatar: its chosen face on its chosen color, or its initials on a neutral tile
/// when its <see cref="NearbyPresence.Info"/> carries no avatar this app can read.
/// </summary>
/// <remarks>
/// Bind <see cref="Presence"/> for a device, or set <see cref="Choice"/> and <see cref="DisplayName"/>
/// directly for a preview of a choice not yet saved.
/// </remarks>
public sealed class DeviceAvatar : ContentView
{
    public static readonly BindableProperty PresenceProperty = BindableProperty.Create(
        nameof(Presence), typeof(NearbyPresence), typeof(DeviceAvatar), propertyChanged: OnPresenceChanged);

    public static readonly BindableProperty ChoiceProperty = BindableProperty.Create(
        nameof(Choice), typeof(AvatarChoice?), typeof(DeviceAvatar), propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty DisplayNameProperty = BindableProperty.Create(
        nameof(DisplayName), typeof(string), typeof(DeviceAvatar), string.Empty, propertyChanged: OnAppearanceChanged);

    public static readonly BindableProperty AvatarSizeProperty = BindableProperty.Create(
        nameof(AvatarSize), typeof(double), typeof(DeviceAvatar), 40d, propertyChanged: OnAppearanceChanged);

    readonly Border _tile;
    readonly Label _face;
    readonly Label _initials;

    public DeviceAvatar()
    {
        _face = new Label
        {
            FontFamily = "NearbyChatIcons",
            TextColor = Colors.White,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        _initials = new Label
        {
            FontAttributes = FontAttributes.Bold,
            HorizontalOptions = LayoutOptions.Center,
            VerticalOptions = LayoutOptions.Center,
        };
        ApplyThemeColor(_initials, Label.TextColorProperty, "TextSecondary");

        _tile = new Border
        {
            StrokeThickness = 0,
            Content = new Grid { Children = { _face, _initials } },
        };

        Content = _tile;
        Render();
    }

    public NearbyPresence? Presence
    {
        get => (NearbyPresence?)GetValue(PresenceProperty);
        set => SetValue(PresenceProperty, value);
    }

    public AvatarChoice? Choice
    {
        get => (AvatarChoice?)GetValue(ChoiceProperty);
        set => SetValue(ChoiceProperty, value);
    }

    public string DisplayName
    {
        get => (string)GetValue(DisplayNameProperty);
        set => SetValue(DisplayNameProperty, value);
    }

    public double AvatarSize
    {
        get => (double)GetValue(AvatarSizeProperty);
        set => SetValue(AvatarSizeProperty, value);
    }

    static void OnPresenceChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var view = (DeviceAvatar)bindable;
        var presence = newValue as NearbyPresence;

        view.Choice = presence is null ? null : PresenceAvatar.Decode(presence.Info);
        view.DisplayName = presence?.DisplayName ?? string.Empty;
    }

    static void OnAppearanceChanged(BindableObject bindable, object oldValue, object newValue)
        => ((DeviceAvatar)bindable).Render();

    /// <summary>
    /// Applies one of Colors.xaml's <see cref="AppThemeColor"/> tokens from code, so the fallback
    /// tile follows the same semantic tokens the XAML does instead of naming a primitive.
    /// </summary>
    static void ApplyThemeColor(BindableObject target, BindableProperty property, string key)
    {
        if (Application.Current?.Resources.TryGetValue(key, out var resource) is true
            && resource is AppThemeColor color)
        {
            target.SetAppThemeColor(property, color.Light, color.Dark);
        }
    }

    void Render()
    {
        var size = AvatarSize;
        _tile.WidthRequest = size;
        _tile.HeightRequest = size;
        _face.FontSize = size * 0.8;
        _initials.FontSize = size * 0.36;

        if (Choice is { } choice)
        {
            _tile.StrokeShape = new RoundRectangle { CornerRadius = size * PresenceAvatar.CornerFraction(choice.Face) };
            _tile.BackgroundColor = PresenceAvatar.Palette[choice.Color].Value;
            _face.Text = PresenceAvatar.FaceGlyph(choice.Face);
            _face.IsVisible = true;
            _initials.IsVisible = false;
            SemanticProperties.SetDescription(this, PresenceAvatar.Describe(choice));
            return;
        }

        _tile.StrokeShape = new RoundRectangle { CornerRadius = size * 0.32 };
        ApplyThemeColor(_tile, BackgroundColorProperty, "Divider");
        _face.IsVisible = false;
        _initials.Text = PresenceAvatar.Initials(DisplayName);
        _initials.IsVisible = true;
        SemanticProperties.SetDescription(this, $"Initials {_initials.Text}");
    }
}
