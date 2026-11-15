using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace EmuShelf.App.ViewModels;

// The model view (M47 B12, proposals.html, approved 2026-11-15): a game on the Big Type Stage is either its big name alone (the name
// view) or its medium alone in the middle (the model view), switched with LB and RB. The shell decides which (it owns the choice);
// the Stage says what follows from it for the views and the native Stage.
public sealed partial class HandheldStageViewModel
{
    /// <summary>
    /// The model view shows: the game's medium (or, for a platform with no authored medium, its flat cover) alone in the middle of the
    /// Stage, no big name; the game's name is the line under where the name would be. Set by the shell, Big Type only.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMedium), nameof(HasFlatCover), nameof(HasBigTypeCover), nameof(HasBigTypeMedium), nameof(NameBoxWidth))]
    public partial bool IsModelView { get; set; }

    /// <summary>The game on the Stage (its library id), or -1 for anything that is not a game: the model view flips when it changes.</summary>
    public long GameId { get; internal set; } = -1;

    /// <summary>The model view's medium: the game's 3D medium in the middle of the Stage.</summary>
    public bool HasModelMedium => HandheldTokens.IsBigType && IsModelView && HasMedium;

    /// <summary>The model view's flat cover: a game whose platform has no authored medium (or whose GPU path failed) stands in its place.</summary>
    public bool HasModelCover => HandheldTokens.IsBigType && IsModelView && HasFlatCover;

    /// <summary>The big name shows: the name view's name, never the model view's (whose name is the line).</summary>
    public bool ShowsBigTypeName => HasBigTypeName && !IsModelView;

    /// <summary>
    /// The right stick on the model view's medium: how far it has turned it (the proposal's CSS degrees) since it took hold, and
    /// whether it still holds it. <see cref="ModelHoldVersion"/> counts the changes.
    /// </summary>
    public (float Pitch, float Yaw, bool Held) ModelHold { get; private set; }

    [ObservableProperty]
    public partial long ModelHoldVersion { get; private set; }

    internal void SetModelHold(float pitch, float yaw, bool held)
    {
        ModelHold = (pitch, yaw, held);
        ModelHoldVersion++;
    }

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        switch (e.PropertyName)
        {
            case nameof(HasMedium):
                OnPropertyChanged(nameof(HasModelMedium));
                break;
            case nameof(HasFlatCover):
                OnPropertyChanged(nameof(HasModelCover));
                break;
            case nameof(HasBigTypeName):
            case nameof(IsModelView):
                OnPropertyChanged(nameof(ShowsBigTypeName));
                break;
        }
    }
}
