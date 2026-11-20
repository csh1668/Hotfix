using System;
using System.Collections.Generic;
using System.Linq;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Column of a <see cref="DataTable{T}"/>. Cell builds the cell for a row.</summary>
    internal sealed record DataColumn<T>(string Header, Size Width, Func<T, Node?> Cell)
    {
        /// <summary>Value rows sort by. null makes the column unsortable.</summary>
        public Func<T, IComparable?>? SortKey { get; init; }
        public TextAlign Align { get; init; } = TextAlign.MiddleLeft;
        public string? Tooltip { get; init; }
    }

    /// <summary>Table over a list. Header clicks cycle ascending, descending, unsorted.</summary>
    /// <example><code>var cols = new[] { DataColumn&lt;Mod&gt;("Name", Size.Fill, m =&gt; Text(m.Name), m =&gt; m.Name) };
    /// new DataTable&lt;Mod&gt;(mods, cols) { KeyOf = m =&gt; m.Id, Selected = sel.Value, OnSelectedChange = sel.Set }</code></example>
    internal sealed record DataTable<T>(IReadOnlyList<T> Rows, IReadOnlyList<DataColumn<T>> Columns) : UIComponent
    {
        /// <summary>Unique row key. Defaults to the row's index.</summary>
        public Func<T, string>? KeyOf { get; init; }
        public IReadOnlyCollection<string>? Selected { get; init; }
        /// <summary>Setting it adds a checkbox column. Rows are selected by KeyOf.</summary>
        public Action<IReadOnlyCollection<string>>? OnSelectedChange { get; init; }
        public Action<T>? OnRowClick { get; init; }
        public float RowHeight { get; init; } = 28f;
        /// <summary>Builds only the visible rows. Give the table a height.</summary>
        public bool Virtual { get; init; }
        public string? EmptyText { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var strings = UseStrings();
            var host = UseHost();
            var sort = UseState((Column: -1, Descending: false));

            var rows = Rows;
            var keyOf = KeyOf;
            string Key(int i) => keyOf?.Invoke(rows[i]) ?? i.ToString();
            var order = Enumerable.Range(0, rows.Count);
            var (sortColumn, descending) = sort.Value;
            if (sortColumn >= 0 && sortColumn < Columns.Count && Columns[sortColumn].SortKey is { } by)
                order = descending
                    ? order.OrderByDescending(i => by(rows[i]), Comparer<IComparable?>.Default)
                    : order.OrderBy(i => by(rows[i]), Comparer<IComparable?>.Default);
            var view = order.ToArray();

            var onSelected = OnSelectedChange;
            bool selectable = onSelected != null;
            var selected = new HashSet<string>(Selected ?? Array.Empty<string>());
            var columns = new List<TableColumn>(Columns.Count + 1);
            if (selectable) columns.Add(new TableColumn("", 32f));
            columns.AddRange(Columns.Select(c => new TableColumn(c.Header, c.Width) { Align = c.Align }));

            var headerCells = new List<Node?>(columns.Count);
            if (selectable)
            {
                int count = view.Count(i => selected.Contains(Key(i)));
                bool all = view.Length > 0 && count == view.Length;
                headerCells.Add(Row(new Checkbox(all, _ => onSelected!(all ? Array.Empty<string>() : view.Select(Key).ToArray()))
                { Indeterminate = count > 0 && !all }.Key("select-all"))
                    .Width(32f).Padding(t.Space(1.5f), 0).Align(Align.Center));
            }
            for (int c = 0; c < Columns.Count; c++)
            {
                var col = Columns[c];
                int index = c;
                bool active = sortColumn == c;
                var caption = Text(col.Header).TextSize(t.TextCaption).Bold().Truncate().Shrink();
                Node? arrow = active ? new Chevron(false) { Size = 10f, Direction = descending ? 90f : -90f } : null;
                var cell = Row(caption, arrow).Width(col.Width).Padding(t.Space(1.5f), 0).Gap(t.Space(1.5f)).Align(Align.Center)
                    .Justify(columns[selectable ? c + 1 : c].Align is TextAlign.MiddleRight or TextAlign.UpperRight or TextAlign.LowerRight ? Justify.End : Justify.Start)
                    .Tooltip(col.Tooltip);
                if (col.SortKey != null)
                    cell = cell.Fg(active ? t.Foreground : t.MutedForeground).HoverFg(t.Foreground).OnClick(() =>
                    {
                        host.Backend.PlaySound(UiSound.Click);
                        sort.Set(s => s.Column != index ? (index, false) : s.Descending ? (-1, false) : (index, true));
                    });
                headerCells.Add(cell);
            }
            var header = Column(Row(headerCells).Height(24).Fg(t.MutedForeground), Row().Height(1).Bg(t.Border));

            if (view.Length == 0)
                return Column(header, Row(Text(EmptyText ?? strings.NoResults).Color(t.MutedForeground)).Height(RowHeight * 2).Center());

            var onRowClick = OnRowClick;
            Node RowAt(int position)
            {
                int i = view[position];
                string key = Key(i);
                bool isSelected = selected.Contains(key);
                var cells = new Node?[columns.Count];
                int at = 0;
                if (selectable)
                    cells[at++] = new Checkbox(isSelected, on =>
                    {
                        var next = new List<string>(Selected ?? Array.Empty<string>());
                        next.Remove(key);
                        if (on) next.Add(key);
                        onSelected!(next);
                    }).Key("select:" + key);
                foreach (var col in Columns) cells[at++] = col.Cell(rows[i]);
                Node row = new TableRow(columns, cells) { RowHeight = RowHeight, Striped = position % 2 == 1, Selected = isSelected };
                var item = rows[i];
                if (onRowClick != null) row = row.OnClick(() => onRowClick(item));
                return row.Key(key);
            }

            if (Virtual)
                return Column(header, new VirtualList(view.Length, RowHeight, RowAt) { KeyOf = p => Key(view[p]) }.Grow());
            var body = new Node?[view.Length + 1];
            body[0] = header;
            for (int p = 0; p < view.Length; p++) body[p + 1] = RowAt(p);
            return Column(body);
        }
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static Components.DataColumn<T> DataColumn<T>(string header, Size width, Func<T, Node?> cell, Func<T, IComparable?>? sortKey = null) =>
            new(header, width, cell) { SortKey = sortKey };

        public static Components.DataTable<T> DataTable<T>(IReadOnlyList<T> rows, IReadOnlyList<Components.DataColumn<T>> columns) => new(rows, columns);
    }
}
