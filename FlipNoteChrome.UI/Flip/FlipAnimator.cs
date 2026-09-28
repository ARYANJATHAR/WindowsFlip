using System.Windows;
using System.Windows.Media.Animation;

namespace FlipNoteChrome.UI.Flip;

public interface IFlipAnimator
{
    Task AnimateToBackAsync(Window window, int durationMs = 350);
    Task AnimateToFrontAsync(Window window, int durationMs = 350);
}

// Lightweight 2D fallback that looks like 3D: scaleX 1->0 swap ->0->1 + fade
public sealed class ScaleFlipAnimator : IFlipAnimator
{
    public async Task AnimateToBackAsync(Window window, int durationMs = 350)
    {
        int half = durationMs / 2;
        var scale = window.RenderTransform as System.Windows.Media.ScaleTransform;
        if (scale == null)
        {
            scale = new System.Windows.Media.ScaleTransform(1, 1);
            window.RenderTransform = scale;
            window.RenderTransformOrigin = new Point(0.5, 0.5);
        }
        var animOut = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(half)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        var tcs = new TaskCompletionSource();
        animOut.Completed += (_, _) => tcs.SetResult();
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animOut);
        await tcs.Task;
    }

    public async Task AnimateToFrontAsync(Window window, int durationMs = 350)
    {
        int half = durationMs / 2;
        var scale = window.RenderTransform as System.Windows.Media.ScaleTransform ?? new System.Windows.Media.ScaleTransform(0, 1);
        window.RenderTransform = scale;
        window.RenderTransformOrigin = new Point(0.5, 0.5);
        var animIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(half)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        var tcs = new TaskCompletionSource();
        animIn.Completed += (_, _) => tcs.SetResult();
        scale.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, animIn);
        await tcs.Task;
    }
}
