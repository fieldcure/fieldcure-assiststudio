using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.Windows.ApplicationModel.Resources;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace AssistStudio.Controls;

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

    /// <summary>
    /// Whether to use a compact visual style (smaller header, subdued colors).
    /// Suited for sections nested inside cards where the header should be subordinate.
    /// </summary>
    public static readonly DependencyProperty IsCompactProperty =
        DependencyProperty.Register(nameof(IsCompact), typeof(bool), typeof(CollapsibleSection),
            new PropertyMetadata(false, OnIsCompactChanged));

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
    /// Gets or sets whether to use a compact visual style (smaller header, subdued colors).
    /// </summary>
    public bool IsCompact
    {
        get => (bool)GetValue(IsCompactProperty);
        set => SetValue(IsCompactProperty, value);
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

    #region Events

    /// <summary>
    /// Occurs when the section is expanded (IsExpanded becomes true).
    /// </summary>
    public event EventHandler? Expanded;

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

            if (section._isExpanded)
                section.Expanded?.Invoke(section, EventArgs.Empty);
        }
    }

    /// <summary>
    /// Applies compact visual adjustments when the IsCompact property changes.
    /// </summary>
    private static void OnIsCompactChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CollapsibleSection section)
            section.ApplyCompactStyle((bool)e.NewValue);
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

    private static readonly ResourceLoader Res = new();

    /// <summary>
    /// Loads a localized string by key. Falls back to English defaults for known
    /// tooltip keys, or to the key itself when no English fallback is defined.
    /// </summary>
    private static string L(string key)
    {
        var value = Res.GetString(key);
        if (value is { Length: > 0 }) return value;
        return key switch
        {
            "CollapsibleSection_CollapseTooltip" => "Collapse section",
            "CollapsibleSection_ExpandTooltip" => "Expand section",
            _ => key
        };
    }

    /// <summary>
    /// Applies compact visual style: smaller font, secondary text color, reduced button height.
    /// </summary>
    private void ApplyCompactStyle(bool compact)
    {
        if (compact)
        {
            HeaderText.Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"];
            HeaderText.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
            HeaderText.Opacity = 0.7;
            SubHeaderText.FontSize = 11;
            SubHeaderText.Opacity = 0.5;
            HeaderButton.MinHeight = 32;
            HeaderButton.Padding = new Thickness(8, 2, 8, 2);
            ToggleIcon.FontSize = 10;
            ContentContainer.FontSize = 12;
        }
        else
        {
            HeaderText.Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"];
            HeaderText.Opacity = 1.0;
            SubHeaderText.FontSize = 12;
            SubHeaderText.Opacity = 0.6;
            HeaderButton.MinHeight = 40;
            HeaderButton.Padding = new Thickness(12, 4, 12, 4);
            ToggleIcon.FontSize = 12;
            ContentContainer.ClearValue(FontSizeProperty);
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
