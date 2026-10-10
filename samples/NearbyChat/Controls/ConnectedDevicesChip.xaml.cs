using System.Windows.Input;

namespace NearbyChat.Controls;

public partial class ConnectedDevicesChip : ContentView
{
    public static readonly BindableProperty CountProperty = BindableProperty.Create(
        propertyName: nameof(Count),
        returnType: typeof(int),
        declaringType: typeof(ConnectedDevicesChip));

    public static readonly BindableProperty CommandProperty = BindableProperty.Create(
        propertyName: nameof(Command),
        returnType: typeof(ICommand),
        declaringType: typeof(ConnectedDevicesChip));

    public int Count
    {
        get => (int)GetValue(CountProperty);
        set => SetValue(CountProperty, value);
    }

    public ICommand Command
    {
        get => (ICommand)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public ConnectedDevicesChip()
    {
        InitializeComponent();
    }

    public void Celebrate()
    {
        const string name = nameof(Celebrate);
        Badge.AbortAnimation(name);

        var wiggle = new Animation
        {
            { 0.00, 0.35, new Animation(v => Badge.Scale = v, 1.0, 1.35, Easing.CubicOut) },
            { 0.35, 1.00, new Animation(v => Badge.Scale = v, 1.35, 1.0, Easing.SpringOut) },
            { 0.00, 0.25, new Animation(v => Badge.Rotation = v, 0, -14) },
            { 0.25, 0.50, new Animation(v => Badge.Rotation = v, -14, 12) },
            { 0.50, 0.75, new Animation(v => Badge.Rotation = v, 12, -6) },
            { 0.75, 1.00, new Animation(v => Badge.Rotation = v, -6, 0) },
        };

        wiggle.Commit(Badge, name, length: 650, finished: (_, _) =>
        {
            Badge.Scale = 1;
            Badge.Rotation = 0;
        });
    }
}
