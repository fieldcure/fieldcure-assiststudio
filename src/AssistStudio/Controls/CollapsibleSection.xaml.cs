using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace FieldCure.AssistStudio.Controls;

/// <summary>
/// A reusable collapsible section control with header, sub-header, and toggle functionality.
/// </summary>
public sealed partial class CollapsibleSection : UserControl, INotifyPropertyChanged
{
    #region Fields

    private bool _isExpanded = true;

    #endregion

    #region Constructor

    /// <summary>
    /// Initializes a new instance of the <see cref="CollapsibleSection"/> class.
    /// </summary>
    public CollapsibleSection()
    {
        InitializeComponent();
    }

    #endregion

    #region Dependency Properties

    /// <summary>
    /// The header text for the section.
    /// </summary>
    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(CollapsibleSection),
            new PropertyMetadata(string.Empty, OnHeaderChanged));

    /// <summary>
    /// The sub-header text displayed next to the header with reduced opacity.
    /// </summary>
    public static readonly DependencyProperty SubHeaderProperty =
        DependencyProperty.Register(nameof(SubHeader), typeof(string), typeof(CollapsibleSection),
            new PropertyMetadata(string.Empty));

    /// <summary>
    /// The content to display in the collapsible area.
    /// </summary>
    public static readonly DependencyProperty BodyProperty =
        DependencyProperty.Register(nameof(Body), typeof(UIElement), typeof(CollapsibleSection),
            new PropertyMetadata(null));

    /// <summary>
    /// Spacing between content elements.
    /// </summary>
    public static readonly DependencyProperty ContentSpacingProperty =
        DependencyProperty.Register(nameof(ContentSpacing), typeof(double), typeof(CollapsibleSection),
            new PropertyMetadata(12.0));

    /// <summary>
    /// Whether the section is expanded by default.
    /// </summary>
    public static readonly DependencyProperty IsExpandedProperty =
        DependencyProperty.Register(nameof(IsExpanded), typeof(bool), typeof(CollapsibleSection),
            new PropertyMetadata(true, OnIsExpandedChanged));

    #endregion

    #region Properties

    /// <summary>
    /// Gets or sets the header text for the section.
    /// </summary>
    public string Header
    {
        get => (string)GetValue(HeaderProperty);
        set => SetValue(HeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the sub-header text displayed next to the header.
    /// </summary>
    public string SubHeader
    {
        get => (string)GetValue(SubHeaderProperty);
        set => SetValue(SubHeaderProperty, value);
    }

    /// <summary>
    /// Gets or sets the content to display in the collapsible area.
    /// </summary>
    public UIElement? Body
    {
        get => (UIElement?)GetValue(BodyProperty);
        set => SetValue(BodyProperty, value);
    }

    /// <summary>
    /// Gets or sets the spacing between content elements.
    /// </summary>
    public double ContentSpacing
    {
        get => (double)GetValue(ContentSpacingProperty);
        set => SetValue(ContentSpacingProperty, value);
    }

    /// <summary>
    /// Gets or sets whether the section is expanded.
    /// </summary>
    public bool IsExpanded
    {
        get => (bool)GetValue(IsExpandedProperty);
        set => SetValue(IsExpandedProperty, value);
    }

    /// <summary>
    /// Gets the visibility of the content based on expansion state.
    /// </summary>
    public Visibility ContentVisibility => _isExpanded ? Visibility.Visible : Visibility.Collapsed;

    /// <summary>
    /// Gets the initial icon rotation angle based on expansion state.
    /// </summary>
    public double InitialIconAngle => _isExpanded ? 0 : 180;

    /// <summary>
    /// Gets the toggle tooltip text.
    /// </summary>
    public string ToggleTooltip => _isExpanded ? L("CollapsibleSection_CollapseTooltip") : L("CollapsibleSection_ExpandTooltip");

    /// <summary>
    /// Gets the accessibility name for the toggle button.
    /// </summary>
    public string ToggleAccessibilityName => $"{ToggleTooltip}: {Header}";

    #endregion

    #region Event Handlers

    /// <summary>
    /// Handles header property changes.
    /// </summary>
    private static void OnHeaderChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CollapsibleSection section)
        {
            section.OnPropertyChanged(nameof(ToggleAccessibilityName));
        }
    }

    /// <summary>
    /// Handles IsExpanded property changes.
    /// </summary>
    private static void OnIsExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CollapsibleSection section)
        {
            section._isExpanded = (bool)e.NewValue;
            section.UpdateVisualState(animated: false);
        }
    }

    /// <summary>
    /// Handles toggle button click.
    /// </summary>
    private void OnHeaderButtonClick(object sender, RoutedEventArgs e)
    {
        IsExpanded = !IsExpanded;
        _isExpanded = IsExpanded;
        UpdateVisualState(animated: true);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Loads a localized string by key, returning a fallback if loading fails.
    /// </summary>
    private static string L(string key)
    {
        try
        {
            var loader = new ResourceLoader();
            return loader.GetString(key);
        }
        catch
        {
            return key switch
            {
                "CollapsibleSection_CollapseTooltip" => "Collapse section",
                "CollapsibleSection_ExpandTooltip" => "Expand section",
                _ => key
            };
        }
    }

    /// <summary>
    /// Updates the visual state with optional animation.
    /// </summary>
    private void UpdateVisualState(bool animated)
    {
        OnPropertyChanged(nameof(ContentVisibility));
        OnPropertyChanged(nameof(ToggleTooltip));
        OnPropertyChanged(nameof(ToggleAccessibilityName));

        if (animated)
        {
            var storyboard = new Storyboard();
            var rotationAnimation = new DoubleAnimation
            {
                To = _isExpanded ? 0 : 180,
                Duration = TimeSpan.FromMilliseconds(200),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            Storyboard.SetTarget(rotationAnimation, IconRotation);
            Storyboard.SetTargetProperty(rotationAnimation, "Angle");
            storyboard.Children.Add(rotationAnimation);
            storyboard.Begin();
        }
        else
        {
            IconRotation.Angle = _isExpanded ? 0 : 180;
        }
    }

    #endregion

    #region INotifyPropertyChanged

    /// <summary>
    /// Occurs when a property value changes.
    /// </summary>
    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// Raises the PropertyChanged event for the specified property.
    /// </summary>
    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    #endregion
}
