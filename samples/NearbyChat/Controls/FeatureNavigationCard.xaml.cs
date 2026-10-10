using System.Windows.Input;

namespace NearbyChat.Controls;

public partial class FeatureNavigationCard : CardView
{
    public static readonly BindableProperty FeatureIconBadgeStyleProperty = BindableProperty.Create(
        propertyName: nameof(FeatureIconBadgeStyle),
        returnType: typeof(Style),
        declaringType: typeof(FeatureNavigationCard));

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        propertyName: nameof(Command),
        returnType: typeof(ICommand),
        declaringType: typeof(FeatureNavigationCard),
        defaultValue: default(ICommand));

    public static readonly BindableProperty FeatureIsActiveProperty = BindableProperty.Create(
        propertyName: nameof(FeatureIsActive),
        returnType: typeof(bool),
        declaringType: typeof(FeatureNavigationCard));

    public static readonly BindableProperty ActiveSubtitleProperty = BindableProperty.Create(
        propertyName: nameof(ActiveSubtitle),
        returnType: typeof(string),
        declaringType: typeof(FeatureNavigationCard),
        defaultValue: "Active");

    public string ActiveSubtitle
    {
        get => (string)GetValue(ActiveSubtitleProperty);
        set => SetValue(ActiveSubtitleProperty, value);
    }

    public bool FeatureIsActive
    {
        get => (bool)GetValue(FeatureIsActiveProperty);
        set => SetValue(FeatureIsActiveProperty, value);
    }

    public Style FeatureIconBadgeStyle
    {
        get => (Style)GetValue(FeatureIconBadgeStyleProperty);
        set => SetValue(FeatureIconBadgeStyleProperty, value);
    }

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public FeatureNavigationCard()
    {
        InitializeComponent();
    }

    public static readonly BindableProperty TapAutomationIdProperty = BindableProperty.Create(
        propertyName: nameof(TapAutomationId),
        returnType: typeof(string),
        declaringType: typeof(FeatureNavigationCard));

    public string? TapAutomationId
    {
        get => (string?)GetValue(TapAutomationIdProperty);
        set => SetValue(TapAutomationIdProperty, value);
    }
}
