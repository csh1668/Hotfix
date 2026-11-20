using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Settings row: label, control, optional description and error. Label on top by default.</summary>
    internal sealed record Field(string Label, Node Control) : UIComponent
    {
        public string? Description { get; init; }
        public string? Error { get; init; }
        /// <summary>Label and description in a LabelWidth column on the left.</summary>
        public bool Horizontal { get; init; }
        public float LabelWidth { get; init; } = 180f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            var label = Elements.Text(Label).Color(t.Foreground).Wrap().Anchor(TextAlign.UpperLeft);
            Node? description = string.IsNullOrEmpty(Description) ? null
                : Elements.Text(Description!).TextSize(t.TextCaption).Color(t.MutedForeground).Wrap().Anchor(TextAlign.UpperLeft);
            Node? error = string.IsNullOrEmpty(Error) ? null
                : Elements.Text(Error!).TextSize(t.TextCaption).Color(t.Destructive).Wrap().Anchor(TextAlign.UpperLeft);

            if (!Horizontal) return Column(label, Control, description, error).Gap(t.Space(1));
            return Row(
                    Column(label, description).Gap(2).Width(LabelWidth),
                    Column(Control, error).Gap(t.Space(1)).Grow().Shrink())
                .Gap(t.Space(3)).Align(Align.Start);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Field Field(string label, Node control, string? description = null, string? error = null) =>
            new(label, control) { Description = description, Error = error };

        /// <summary>Field with the label on the left.</summary>
        public static Components.Field FieldRow(string label, Node control, string? description = null, string? error = null) =>
            new(label, control) { Description = description, Error = error, Horizontal = true };
    }
}
