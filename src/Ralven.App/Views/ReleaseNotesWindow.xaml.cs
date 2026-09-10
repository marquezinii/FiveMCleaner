using System.Windows;
using System.Windows.Input;
using Ralven.App.Services;

namespace Ralven.App.Views;

/// <summary>
/// One category section as rendered in the panel: <see cref="Category"/>
/// drives icon and color through the <c>DataTrigger</c>s in
/// <c>ReleaseNotesWindow.xaml</c>; <see cref="BulletsText"/> is the raw,
/// already-localized, newline-separated bullet text for that category (each
/// line already prefixed with "• " in the resx source, the same convention
/// <c>Privacy.Collects.Items</c> already uses).
/// </summary>
public sealed record ReleaseNoteSectionDisplayItem(
    ReleaseNoteCategory Category,
    string Label,
    string BulletsText);

/// <summary>
/// Version notes in the shared resizable modal reader. The caller owns seen-version persistence.
/// </summary>
public partial class ReleaseNotesWindow : Ralven.App.Controls.DialogWindow
{
    /// <summary>
    /// Categories render in this fixed order regardless of the order they
    /// were listed in the catalog entry — a category simply does not appear
    /// unless <see cref="ReleaseNoteVersion.Categories"/> actually lists it.
    /// </summary>
    private static readonly ReleaseNoteCategory[] DisplayOrder =
    [
        ReleaseNoteCategory.Added,
        ReleaseNoteCategory.Improved,
        ReleaseNoteCategory.Fixed,
        ReleaseNoteCategory.Removed,
        ReleaseNoteCategory.Security
    ];

    /// <summary>
    /// Keeps only the categories <paramref name="categories"/> actually
    /// lists, in <see cref="DisplayOrder"/> — a category with no content for
    /// this version simply never appears. A plain static method (rather than
    /// inline construction logic) so it stays unit testable without
    /// constructing a real WPF window on an STA thread.
    /// </summary>
    public static IReadOnlyList<ReleaseNoteCategory> OrderCategories(
        IReadOnlyList<ReleaseNoteCategory> categories) =>
        DisplayOrder.Where(categories.Contains).ToList();

    private readonly ILocalizationService localization;

    public ReleaseNotesWindow(ReleaseNoteVersion entry, ILocalizationService? localization = null)
    {
        ArgumentNullException.ThrowIfNull(entry);
        this.localization = localization ?? LocalizationService.Current;
        Entry = entry;
        Sections = OrderCategories(entry.Categories)
            .Select(category => new ReleaseNoteSectionDisplayItem(
                category,
                this.localization.GetString($"ReleaseNotes.Category.{category}"),
                this.localization.GetString($"ReleaseNotes.{entry.Version.Replace('.', '_')}.{category}")))
            .ToList();
        InitializeComponent();
        DataContext = this;
        Loaded += (_, _) => CloseButton.Focus();
    }

    public ReleaseNoteVersion Entry { get; }

    public IReadOnlyList<ReleaseNoteSectionDisplayItem> Sections { get; }

    public string WindowTitleText => F("ReleaseNotes.WindowTitle", Entry.Version);

    public string VersionLabel => Entry.ReleaseDate is { } date
        ? F(
            "ReleaseNotes.VersionLabelWithDate",
            Entry.Version,
            date.ToDateTime(TimeOnly.MinValue).ToString("d", localization.CurrentCulture))
        : F("ReleaseNotes.VersionLabel", Entry.Version);

    private void Close_Click(object sender, RoutedEventArgs e) => Close();

    private string F(string key, params object?[] arguments) => localization.Format(key, arguments);

    /// <summary>Esc closes, same as the X and the "Fechar" button.</summary>
    private void CloseWindowCommandBinding_Executed(object sender, ExecutedRoutedEventArgs e) =>
        SystemCommands.CloseWindow(this);
}
