using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Media.Media3D;

namespace FlipNoteChrome.UI.Flip;

// True 3D flip using Viewport3D AxisAngleRotation3D 0->180.
// Window must contain a Viewport3D named "FlipViewport" with an AxisAngleRotation3D named "FlipRotation".
public sealed class Viewport3DFlipAnimator : IFlipAnimator
{
    public Task AnimateToBackAsync(Window window, int durationMs = 350) => Animate(window, 0, 180, durationMs);
    public Task AnimateToFrontAsync(Window window, int durationMs = 350) => Animate(window, 180, 0, durationMs);

    private static Task Animate(Window window, double from, double to, int durationMs)
    {
        var viewport = window.FindName("FlipViewport") as System.Windows.Controls.Viewport3D;
        if (viewport == null) return new ScaleFlipAnimator().AnimateToBackAsync(window, durationMs);
        var rot = viewport.FindName("FlipRotation") as AxisAngleRotation3D;
        if (rot == null) return new ScaleFlipAnimator().AnimateToBackAsync(window, durationMs);

        var tcs = new TaskCompletionSource();
        var anim = new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(durationMs))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
            FillBehavior = FillBehavior.HoldEnd
        };
        anim.Completed += (_, _) => tcs.TrySetResult();
        rot.BeginAnimation(AxisAngleRotation3D.AngleProperty, anim);
        return tcs.Task;
    }
}
