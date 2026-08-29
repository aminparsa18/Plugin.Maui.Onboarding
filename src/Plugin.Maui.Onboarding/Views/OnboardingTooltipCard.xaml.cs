using System.Windows.Input;

namespace Plugin.Maui.Onboarding.Views;

/// <summary>Title/description + Next/Skip. Plain BindableProperty-in, ICommand-out — no internal events.</summary>
public partial class OnboardingTooltipCard : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(OnboardingTooltipCard), string.Empty);

    public static readonly BindableProperty DescriptionProperty =
        BindableProperty.Create(nameof(Description), typeof(string), typeof(OnboardingTooltipCard), string.Empty);

    public static readonly BindableProperty IsLastStepProperty =
        BindableProperty.Create(nameof(IsLastStep), typeof(bool), typeof(OnboardingTooltipCard), false, propertyChanged: OnIsLastStepChanged);

    /// <summary>Named "StepContent" rather than "Content" — this type is itself a <see cref="ContentView"/>,
    /// and "Content" is already its own implicit content property (the <c>&lt;Border&gt;</c> root in the
    /// XAML). A property named "Content" here would shadow that.</summary>
    public static readonly BindableProperty StepContentProperty =
        BindableProperty.Create(nameof(StepContent), typeof(View), typeof(OnboardingTooltipCard));

    /// <summary>Baked-in literal default (`#E6000000`) so the card renders sensibly with zero setup —
    /// override via this property (directly, or through <see cref="OnboardingCoordinator.TooltipBackgroundColor"/>)
    /// rather than assuming a host app resource key.</summary>
    public static readonly BindableProperty TooltipBackgroundColorProperty =
        BindableProperty.Create(nameof(TooltipBackgroundColor), typeof(Color), typeof(OnboardingTooltipCard), Color.FromArgb("#E6000000"));

    /// <summary>Transparent by default — the card renders with no visible border/stroke out of the box.
    /// Override via this property (directly, or through <see cref="OnboardingCoordinator.TooltipBorderColor"/>)
    /// to add one.</summary>
    public static readonly BindableProperty TooltipBorderColorProperty =
        BindableProperty.Create(nameof(TooltipBorderColor), typeof(Color), typeof(OnboardingTooltipCard), Colors.Transparent);

    // Plain English literal defaults — this library has no localized-resource dependency of its own.
    // Consumers who need other wording (or localization) just set NextText/SkipText, same as any other
    // bindable property.
    public static readonly BindableProperty NextTextProperty =
        BindableProperty.Create(nameof(NextText), typeof(string), typeof(OnboardingTooltipCard), "Next");

    public static readonly BindableProperty SkipTextProperty =
        BindableProperty.Create(nameof(SkipText), typeof(string), typeof(OnboardingTooltipCard), "Skip");

    public static readonly BindableProperty NextCommandProperty =
        BindableProperty.Create(nameof(NextCommand), typeof(ICommand), typeof(OnboardingTooltipCard));

    public static readonly BindableProperty SkipCommandProperty =
        BindableProperty.Create(nameof(SkipCommand), typeof(ICommand), typeof(OnboardingTooltipCard));

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Description
    {
        get => (string)GetValue(DescriptionProperty);
        set => SetValue(DescriptionProperty, value);
    }

    public bool IsLastStep
    {
        get => (bool)GetValue(IsLastStepProperty);
        set => SetValue(IsLastStepProperty, value);
    }

    /// <summary>Extra content shown between the description and the Next/Skip buttons, built per step by
    /// <see cref="OnboardingStep.Content"/>. Null (the default) leaves the row collapsed.</summary>
    public View? StepContent
    {
        get => (View?)GetValue(StepContentProperty);
        set => SetValue(StepContentProperty, value);
    }

    public Color TooltipBackgroundColor
    {
        get => (Color)GetValue(TooltipBackgroundColorProperty);
        set => SetValue(TooltipBackgroundColorProperty, value);
    }

    public Color TooltipBorderColor
    {
        get => (Color)GetValue(TooltipBorderColorProperty);
        set => SetValue(TooltipBorderColorProperty, value);
    }

    /// <summary>"Next" normally, "Done" once <see cref="IsLastStep"/> is true — kept in sync automatically.</summary>
    public string NextText
    {
        get => (string)GetValue(NextTextProperty);
        private set => SetValue(NextTextProperty, value);
    }

    public string SkipText
    {
        get => (string)GetValue(SkipTextProperty);
        set => SetValue(SkipTextProperty, value);
    }

    public ICommand? NextCommand
    {
        get => (ICommand?)GetValue(NextCommandProperty);
        set => SetValue(NextCommandProperty, value);
    }

    public ICommand? SkipCommand
    {
        get => (ICommand?)GetValue(SkipCommandProperty);
        set => SetValue(SkipCommandProperty, value);
    }

    public OnboardingTooltipCard()
    {
        InitializeComponent();
    }

    private static void OnIsLastStepChanged(BindableObject bindable, object oldValue, object newValue)
    {
        var card = (OnboardingTooltipCard)bindable;
        card.NextText = (bool)newValue ? "Done" : "Next";
    }
}
