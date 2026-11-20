using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Grouping surface one step lighter than the window.</summary>
    /// <example><code>Card(CardHeader(CardTitle("Storage"), CardDescription("Where haulers put things.")),
    ///     CardContent(Label("...")), CardFooter(Button("Apply", Apply, ButtonVariant.Primary)))</code></example>
    internal sealed record Card(Node[] Children) : UIComponent
    {
        /// <summary>Adds a soft shadow, e.g. over busy content.</summary>
        public bool Elevated { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var card = Column(Children).Padding(t.Space(3.5f)).Gap(t.Space(2.5f))
                .Bg(t.Card).Radius(t.RadiusLg).Bevel(t.Raised(t.Card, t.BevelWidthSm)).Border(t.CardBorder);
            return Elevated ? card.Shadow(t.ShadowSm, t.ShadowColor) : card;
        }
    }

    internal sealed record CardTitle(string Text) : UIComponent
    {
        protected internal override Node Build()
        {
            var t = UseTheme();
            return Elements.Text(Text).TextSize(t.TextTitle).Bold().Color(t.Foreground).Truncate();
        }
    }

    /// <summary>Titled card section for grouping related settings.</summary>
    internal sealed record Panel(string Title, Node[] Children) : UIComponent
    {
        /// <summary>Sits at the right of the header.</summary>
        public Node? Extra { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Column(
                    Row(Elements.Text(Title).TextSize(t.TextCaption).Bold().Color(t.MutedForeground).Truncate().Grow().Shrink(), Extra)
                        .Height(30).Padding(t.Space(3), 0).Gap(t.Space(2)).Align(Align.Center),
                    Row().Height(1).Bg(t.Border),
                    Column(Children).Padding(t.Space(3)).Gap(t.Space(2)))
                .Bg(t.Card).Radius(t.RadiusLg).Bevel(t.Raised(t.Card, t.BevelWidthSm)).Border(t.CardBorder);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.Card Card(params Node?[] children) => new(NodeArrays.Normalize(children));
        public static BoxNode CardHeader(params Node?[] children) => Column(children).Gap(4);
        public static Components.CardTitle CardTitle(string text) => new(text);
        public static Components.Label CardDescription(string text) => Muted(text);
        public static BoxNode CardContent(params Node?[] children) => Column(children).Gap(8);
        /// <summary>Right-aligned row for card actions.</summary>
        public static BoxNode CardFooter(params Node?[] children) => Row(children).Gap(8).Justify(Rimact.Justify.End).Align(Rimact.Align.Center);
        public static Components.Panel Panel(string title, params Node?[] children) => new(title, NodeArrays.Normalize(children));
    }
}
