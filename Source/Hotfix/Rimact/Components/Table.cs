using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Width is in pixels, or Size.Fill to share out the leftover width.</summary>
    internal sealed record TableColumn(string Header, Size Width)
    {
        public TextAlign Align { get; init; } = TextAlign.MiddleLeft;
        public string? Tooltip { get; init; }
    }

    internal sealed record TableHeader(IReadOnlyList<TableColumn> Columns) : UIComponent
    {
        public float RowHeight { get; init; } = 24f;

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Column(
                Row(Columns.Select(c => (Node?)Elements.Text(c.Header).TextSize(t.TextCaption).Bold().Anchor(c.Align).Truncate()
                        .Width(c.Width).Padding(t.Space(1.5f), 0).Tooltip(c.Tooltip)))
                    .Height(RowHeight).Fg(t.MutedForeground),
                Row().Height(1).Bg(t.Border));
        }
    }

    /// <summary>Data row. Cells take the column widths and clip overflow.</summary>
    internal sealed record TableRow(IReadOnlyList<TableColumn> Columns, Node?[] Cells) : UIComponent
    {
        public float RowHeight { get; init; } = 28f;
        public bool Striped { get; init; }
        public bool Selected { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var cells = new Node?[Columns.Count];
            for (int i = 0; i < Columns.Count; i++)
            {
                var cell = i < Cells.Length ? Cells[i] : null;
                cells[i] = Row(cell).Width(Columns[i].Width).Clip().Padding(t.Space(1.5f), 0).Align(Align.Center)
                    .Justify(Columns[i].Align is TextAlign.MiddleRight or TextAlign.UpperRight or TextAlign.LowerRight ? Justify.End
                        : Columns[i].Align is TextAlign.MiddleCenter or TextAlign.UpperCenter or TextAlign.LowerCenter ? Justify.Center : Justify.Start);
            }
            var normal = new Visual { Bg = Striped ? t.Muted : null, Overlay = Selected ? t.SelectedOverlay : null, Radius = t.RadiusSm };
            var row = Row(cells).Height(RowHeight).Visual(normal, new Visual { Overlay = Selected ? Theme.WithAlpha(t.SelectedOverlay, t.SelectedOverlay.a + 0.06f) : t.HoverOverlay });
            if (!Selected) return row;
            return Stack(row, Row().Width(2).FillHeight().Bg(t.Indicator).AlignSelf(Align.Start)).Height(RowHeight);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.TableColumn TableColumn(string header, Size width, TextAlign align = TextAlign.MiddleLeft) =>
            new(header, width) { Align = align };

        public static Components.TableHeader TableHeader(IReadOnlyList<Components.TableColumn> columns) => new(columns);

        public static Components.TableRow TableRow(IReadOnlyList<Components.TableColumn> columns, params Node?[] cells) => new(columns, cells);

        /// <summary>Header plus striped rows. For many rows use a VirtualList of TableRow instead.</summary>
        /// <example><code>Table(new[] { TableColumn("Name", Size.Fill), TableColumn("Age", 60, TextAlign.MiddleRight) },
        ///     pawns.Select(p => new Node?[] { Label(p.Name), Label(p.Age.ToString()) }))</code></example>
        public static BoxNode Table(IReadOnlyList<Components.TableColumn> columns, IEnumerable<Node?[]> rows) =>
            Column(new Node?[] { TableHeader(columns) }.Concat(rows.Select((cells, i) => (Node?)new Components.TableRow(columns, cells) { Striped = i % 2 == 1 })));
    }
}
