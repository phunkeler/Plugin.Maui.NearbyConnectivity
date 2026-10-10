using System.Windows.Input;
using CommunityToolkit.Mvvm.ComponentModel;

namespace NearbyChat.ViewModels;

/// <summary>
/// One pick in the presence sheet: a face, or a color. Faces carry a glyph and a silhouette;
/// colors carry only <see cref="PaletteIndex"/>. The view turns the fraction and the index into
/// a shape and a <see cref="Color"/>.
/// </summary>
/// <param name="index">The tile's position in the sheet.</param>
/// <param name="glyph">The face glyph, or empty for a color tile.</param>
/// <param name="cornerFraction">The silhouette's corner radius as a fraction of the tile size.</param>
/// <param name="paletteIndex">The index into the avatar palette.</param>
/// <param name="description">What a screen reader says for the tile, the same words the preview
/// uses for the same pick.</param>
/// <param name="select">The command that picks this tile.</param>
public sealed partial class AvatarTile(
    int index,
    string glyph,
    double cornerFraction,
    int paletteIndex,
    string description,
    ICommand select) : ObservableObject
{
    public int Index { get; } = index;

    public string Glyph { get; } = glyph;

    public double CornerFraction { get; } = cornerFraction;

    public int PaletteIndex { get; } = paletteIndex;

    public string Description { get; } = description;

    public ICommand Select { get; } = select;

    [ObservableProperty]
    public partial bool IsSelected { get; set; }
}
