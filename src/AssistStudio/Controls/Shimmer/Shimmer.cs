using Microsoft.UI.Composition;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using System;
using System.Numerics;
using Windows.UI;

namespace AssistStudio.Controls;

/// <summary>
/// A shimmer control that provides a placeholder loading animation effect.
/// Uses composition animations to create a smooth gradient shimmer across the control surface.
/// </summary>
[TemplatePart(Name = PART_Shape, Type = typeof(Border))]
public partial class Shimmer : Control
{
    private const float InitialStartPointX = -7.92f;
    private const string PART_Shape = "Shape";

    #region Fields

    private ExpressionAnimation? _sizeAnimation;
    private Vector2KeyFrameAnimation? _gradientStartPointAnimation;
    private Vector2KeyFrameAnimation? _gradientEndPointAnimation;
    private CompositionColorGradientStop? _gradientStop1;
    private CompositionColorGradientStop? _gradientStop2;
    private CompositionColorGradientStop? _gradientStop3;
    private CompositionColorGradientStop? _gradientStop4;
    private CompositionRoundedRectangleGeometry? _rectangleGeometry;
    private ShapeVisual? _shapeVisual;
    private CompositionLinearGradientBrush? _shimmerMaskGradient;
    private Border? _shape;

    private bool _initialized;
    private bool _animationStarted;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="Shimmer"/> class.
    /// </summary>
    public Shimmer()
    {
        DefaultStyleKey = typeof(Shimmer);
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    #endregion

    #region Overrides

    /// <summary>
    /// Invoked when the control template is applied.
    /// </summary>
    protected override void OnApplyTemplate()
    {
        base.OnApplyTemplate();

        _shape = GetTemplateChild(PART_Shape) as Border;
        if (_initialized is false && TryInitializationResource() && IsActive)
        {
            TryStartAnimation();
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Handles the Loaded event.
    /// </summary>
    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized is false && TryInitializationResource() && IsActive)
        {
            TryStartAnimation();
        }

        ActualThemeChanged += OnActualThemeChanged;
    }

    /// <summary>
    /// Handles the Unloaded event.
    /// </summary>
    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        ActualThemeChanged -= OnActualThemeChanged;
        StopAnimation();

        if (_initialized && _shape != null)
        {
            ElementCompositionPreview.SetElementChildVisual(_shape, null);

            _rectangleGeometry!.Dispose();
            _shapeVisual!.Dispose();
            _shimmerMaskGradient!.Dispose();
            _gradientStop1!.Dispose();
            _gradientStop2!.Dispose();
            _gradientStop3!.Dispose();
            _gradientStop4!.Dispose();

            _initialized = false;
        }
    }

    /// <summary>
    /// Handles theme changes to update gradient colors.
    /// </summary>
    private void OnActualThemeChanged(FrameworkElement sender, object args)
    {
        if (_initialized is false)
        {
            return;
        }

        SetGradientStopColorsByTheme();
    }

    /// <summary>
    /// Attempts to initialize composition resources for the shimmer animation.
    /// </summary>
    /// <returns>True if initialization succeeded; otherwise, false.</returns>
    private bool TryInitializationResource()
    {
        if (_initialized)
        {
            return true;
        }

        if (_shape is null || IsLoaded is false)
        {
            return false;
        }

        var rootVisual = ElementCompositionPreview.GetElementVisual(_shape);
        var compositor = rootVisual.Compositor;

        _rectangleGeometry = compositor.CreateRoundedRectangleGeometry();
        _shapeVisual = compositor.CreateShapeVisual();
        _shimmerMaskGradient = compositor.CreateLinearGradientBrush();
        _gradientStop1 = compositor.CreateColorGradientStop();
        _gradientStop2 = compositor.CreateColorGradientStop();
        _gradientStop3 = compositor.CreateColorGradientStop();
        _gradientStop4 = compositor.CreateColorGradientStop();
        SetGradientAndStops();
        SetGradientStopColorsByTheme();
        _rectangleGeometry.CornerRadius = new Vector2((float)CornerRadius.TopLeft);
        var spriteShape = compositor.CreateSpriteShape(_rectangleGeometry);
        spriteShape.FillBrush = _shimmerMaskGradient;
        _shapeVisual.Shapes.Add(spriteShape);
        ElementCompositionPreview.SetElementChildVisual(_shape, _shapeVisual);

        _initialized = true;
        return true;
    }

    /// <summary>
    /// Configures the gradient brush and gradient stops for the shimmer effect.
    /// </summary>
    private void SetGradientAndStops()
    {
        _shimmerMaskGradient!.StartPoint = new Vector2(InitialStartPointX, 0.0f);
        _shimmerMaskGradient.EndPoint = new Vector2(0.0f, 1.0f);

        _gradientStop1!.Offset = 0.273f;
        _gradientStop2!.Offset = 0.436f;
        _gradientStop3!.Offset = 0.482f;
        _gradientStop4!.Offset = 0.643f;

        _shimmerMaskGradient.ColorStops.Add(_gradientStop1);
        _shimmerMaskGradient.ColorStops.Add(_gradientStop2);
        _shimmerMaskGradient.ColorStops.Add(_gradientStop3);
        _shimmerMaskGradient.ColorStops.Add(_gradientStop4);
    }

    /// <summary>
    /// Sets the gradient stop colors based on the current theme.
    /// </summary>
    private void SetGradientStopColorsByTheme()
    {
        switch (ActualTheme)
        {
            case ElementTheme.Default:
            case ElementTheme.Dark:
                _gradientStop1!.Color = Color.FromArgb((byte)(255 * 6.05 / 100), 255, 255, 255);
                _gradientStop2!.Color = Color.FromArgb((byte)(255 * 3.26 / 100), 255, 255, 255);
                _gradientStop3!.Color = Color.FromArgb((byte)(255 * 3.26 / 100), 255, 255, 255);
                _gradientStop4!.Color = Color.FromArgb((byte)(255 * 6.05 / 100), 255, 255, 255);
                break;
            case ElementTheme.Light:
                _gradientStop1!.Color = Color.FromArgb((byte)(255 * 5.37 / 100), 0, 0, 0);
                _gradientStop2!.Color = Color.FromArgb((byte)(255 * 2.89 / 100), 0, 0, 0);
                _gradientStop3!.Color = Color.FromArgb((byte)(255 * 2.89 / 100), 0, 0, 0);
                _gradientStop4!.Color = Color.FromArgb((byte)(255 * 5.37 / 100), 0, 0, 0);
                break;
        }
    }

    /// <summary>
    /// Attempts to start the shimmer animation if conditions are met.
    /// </summary>
    private void TryStartAnimation()
    {
        if (_animationStarted || _initialized is false || _shape is null || _shapeVisual is null || _rectangleGeometry is null)
        {
            return;
        }

        var rootVisual = ElementCompositionPreview.GetElementVisual(_shape);
        var compositor = rootVisual.Compositor;

        // Bind ShapeVisual and RectangleGeometry size to the root visual's size
        _sizeAnimation = compositor.CreateExpressionAnimation("root.Size");
        _sizeAnimation.SetReferenceParameter("root", rootVisual);
        _shapeVisual.StartAnimation(nameof(ShapeVisual.Size), _sizeAnimation);
        _rectangleGeometry.StartAnimation(nameof(CompositionRoundedRectangleGeometry.Size), _sizeAnimation);

        _gradientStartPointAnimation = compositor.CreateVector2KeyFrameAnimation();
        _gradientStartPointAnimation.Duration = Duration;
        _gradientStartPointAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
        _gradientStartPointAnimation.InsertKeyFrame(0.0f, new Vector2(InitialStartPointX, 0.0f));
        _gradientStartPointAnimation.InsertKeyFrame(1.0f, Vector2.Zero);
        _shimmerMaskGradient!.StartAnimation(nameof(CompositionLinearGradientBrush.StartPoint), _gradientStartPointAnimation);

        _gradientEndPointAnimation = compositor.CreateVector2KeyFrameAnimation();
        _gradientEndPointAnimation.Duration = Duration;
        _gradientEndPointAnimation.IterationBehavior = AnimationIterationBehavior.Forever;
        _gradientEndPointAnimation.InsertKeyFrame(0.0f, new Vector2(1.0f, 0.0f));
        _gradientEndPointAnimation.InsertKeyFrame(1.0f, new Vector2(-InitialStartPointX, 1.0f));
        _shimmerMaskGradient.StartAnimation(nameof(CompositionLinearGradientBrush.EndPoint), _gradientEndPointAnimation);

        _animationStarted = true;
    }

    /// <summary>
    /// Stops the shimmer animation and disposes animation resources.
    /// </summary>
    private void StopAnimation()
    {
        if (_animationStarted is false)
        {
            return;
        }

        _shapeVisual!.StopAnimation(nameof(ShapeVisual.Size));
        _rectangleGeometry!.StopAnimation(nameof(CompositionRoundedRectangleGeometry.Size));
        _shimmerMaskGradient!.StopAnimation(nameof(CompositionLinearGradientBrush.StartPoint));
        _shimmerMaskGradient.StopAnimation(nameof(CompositionLinearGradientBrush.EndPoint));

        _sizeAnimation!.Dispose();
        _gradientStartPointAnimation!.Dispose();
        _gradientEndPointAnimation!.Dispose();
        _animationStarted = false;
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// Identifies the <see cref="Duration"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty DurationProperty = DependencyProperty.Register(
       nameof(Duration),
       typeof(object),
       typeof(Shimmer),
       new PropertyMetadata(defaultValue: TimeSpan.FromMilliseconds(1600), PropertyChanged));

    /// <summary>
    /// Identifies the <see cref="IsActive"/> dependency property.
    /// </summary>
    public static readonly DependencyProperty IsActiveProperty = DependencyProperty.Register(
      nameof(IsActive),
      typeof(bool),
      typeof(Shimmer),
      new PropertyMetadata(defaultValue: true, PropertyChanged));

    /// <summary>
    /// Gets or sets the animation duration.
    /// </summary>
    /// <value>The duration of the shimmer animation. Default is 1600 milliseconds.</value>
    public TimeSpan Duration
    {
        get => (TimeSpan)GetValue(DurationProperty);
        set => SetValue(DurationProperty, value);
    }

    /// <summary>
    /// Gets or sets a value indicating whether the shimmer animation is active.
    /// </summary>
    /// <value>True if the animation is playing; otherwise, false. Default is true.</value>
    public bool IsActive
    {
        get => (bool)GetValue(IsActiveProperty);
        set => SetValue(IsActiveProperty, value);
    }

    /// <summary>
    /// Handles changes to dependency properties.
    /// </summary>
    private static void PropertyChanged(DependencyObject s, DependencyPropertyChangedEventArgs e)
    {
        var self = (Shimmer)s;
        if (self.IsActive)
        {
            self.StopAnimation();
            self.TryStartAnimation();
        }
        else
        {
            self.StopAnimation();
        }
    }

    #endregion
}
