using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;
using static Rimact.Elements;
using static Rimact.Hooks;

namespace Rimact.Components
{
    /// <summary>Info button that opens vanilla's info card for a Def or a Thing.</summary>
    internal sealed record InfoCardButton(object Target) : UIComponent
    {
        public float Size { get; init; } = 18f;
        /// <summary>For a def made from stuff. Defaults to its default stuff.</summary>
        public ThingDef? Stuff { get; init; }

        protected internal override Node Build()
        {
            var t = UseTheme();
            var backend = UseHost().Backend;
            var target = Target;
            var stuff = Stuff;
            return Row(Image(Glyph.Info).Size(Size - 4, Size - 4))
                .Size(Size, Size).Center()
                .Fg(t.MutedForeground).HoverFg(t.Foreground)
                .Tooltip(T("DefInfoTip", "Information"))
                .OnClick(() =>
                {
                    backend.PlaySound(UiSound.Click);
                    Open(target, stuff);
                });
        }

        private static void Open(object target, ThingDef? stuff)
        {
            switch (target)
            {
                case Thing thing and IConstructible c when thing.def.entityDefToBuild is { } built:
                    Find.WindowStack.Add(built is ThingDef builtDef ? new Dialog_InfoCard(builtDef, StuffFor(builtDef, c.EntityToBuildStuff())) : new Dialog_InfoCard(built));
                    break;
                case Thing thing:
                    Find.WindowStack.Add(new Dialog_InfoCard(thing));
                    break;
                case ThingDef { MadeFromStuff: true } td:
                    Find.WindowStack.Add(new Dialog_InfoCard(td, StuffFor(td, stuff)));
                    break;
                case Def def:
                    Find.WindowStack.Add(new Dialog_InfoCard(def));
                    break;
            }
        }

        // Vanilla's stats report throws for a stuffed def without stuff
        private static ThingDef? StuffFor(ThingDef def, ThingDef? stuff) => def.MadeFromStuff ? stuff ?? GenStuff.DefaultStuffFor(def) : null;
    }

    /// <summary>A def as vanilla lists it: icon, label and info card button.</summary>
    internal sealed record DefLabel(Def Def) : UIComponent
    {
        public ThingDef? Stuff { get; init; }
        public float IconSize { get; init; } = 24f;
        public bool InfoCard { get; init; } = true;
        /// <summary>Shows the def's description as a tooltip.</summary>
        public bool Description { get; init; } = true;

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Row(
                    Image(Icon.Of(Def, Stuff)).Size(IconSize, IconSize),
                    Text(GameText.LabelOf(Def)).Truncate().Grow().Shrink(),
                    InfoCard ? new InfoCardButton(Def) { Stuff = Stuff } : null)
                .Gap(t.Space(2)).Align(Align.Center).MinHeight(IconSize)
                .Tooltip(Description && !string.IsNullOrEmpty(Def.description) ? Def.description : null);
        }
    }

    /// <summary>A thing as the inspect pane lists it: icon, label with stack count and info card button.</summary>
    internal sealed record ThingLabel(Thing Thing) : UIComponent
    {
        public float IconSize { get; init; } = 24f;
        public bool InfoCard { get; init; } = true;

        protected internal override Node Build()
        {
            var t = UseTheme();
            return Row(
                    Image(Icon.Of(Thing)).Size(IconSize, IconSize),
                    Text(Thing.LabelCap).Truncate().Grow().Shrink(),
                    InfoCard ? new InfoCardButton(Thing) : null)
                .Gap(t.Space(2)).Align(Align.Center).MinHeight(IconSize);
        }
    }

    /// <summary>Searchable def picker. Searches labels, mod names and defNames. Defs from 2+ mods get a mod filter.</summary>
    internal sealed record DefPicker<T>(T? Value, Action<T?>? OnChange) : UIComponent where T : Def
    {
        /// <summary>Every T in the database by default.</summary>
        /// <remarks>Options rebuild only when Defs or Filter change by value. Prefer a list to a fresh LINQ query.</remarks>
        public IEnumerable<T>? Defs { get; init; }
        public Func<T, bool>? Filter { get; init; }
        /// <summary>Adds a "None" option on top that reports null.</summary>
        public bool AllowNone { get; init; }
        public string? Placeholder { get; init; }
        public float PanelWidth { get; init; } = 340f;
        public int VisibleRows { get; init; } = 10;

        protected internal override Node Build()
        {
            var strings = UseStrings();
            var defs = GameText.UseStable(Defs);
            var filter = GameText.UseStable(Filter);
            bool none = AllowNone;
            var options = UseMemo(() =>
            {
                var list = new List<SelectOption<T?>>();
                if (none) list.Add(new SelectOption<T?>(null, strings.None));
                list.AddRange(GameText.Options(defs ?? DefDatabase<T>.AllDefsListForReading, filter).Select(o =>
                    new SelectOption<T?>(o.Value, o.Label) { Icon = o.Icon, Detail = o.Detail, Keywords = o.Keywords, Group = o.Group }));
                return (IReadOnlyList<SelectOption<T?>>)list;
            }, defs, filter, none, strings.None);
            return new Combobox<T?>(Value, options, OnChange)
            {
                Placeholder = Placeholder,
                PanelWidth = PanelWidth,
                VisibleRows = VisibleRows,
            };
        }
    }

    /// <summary>Searchable picker for several defs.</summary>
    internal sealed record DefMultiPicker<T>(IReadOnlyCollection<T> Values, Action<IReadOnlyCollection<T>>? OnChange) : UIComponent where T : Def
    {
        public IEnumerable<T>? Defs { get; init; }
        public Func<T, bool>? Filter { get; init; }
        public string? Placeholder { get; init; }
        public float PanelWidth { get; init; } = 340f;
        public int VisibleRows { get; init; } = 10;

        protected internal override Node Build()
        {
            var defs = GameText.UseStable(Defs);
            var filter = GameText.UseStable(Filter);
            var options = UseMemo(() => (IReadOnlyList<SelectOption<T>>)GameText.Options(defs ?? DefDatabase<T>.AllDefsListForReading, filter), defs, filter);
            return new MultiSelect<T>(Values, options, OnChange)
            {
                Placeholder = Placeholder,
                PanelWidth = PanelWidth,
                VisibleRows = VisibleRows,
            };
        }
    }

    internal static class GameText
    {
        /// <summary>Keeps the earlier instance while value equals it field by field, so rebuilt lambdas keep memos valid.</summary>
        public static T UseStable<T>(T value)
        {
            var kept = UseRef(value);
            if (!ReferenceEquals(kept.Current, value) && !PropEquality.Equal(kept.Current, value)) kept.Current = value;
            return kept.Current;
        }

        /// <summary>Capitalized label, or the defName when there is no label.</summary>
        public static string LabelOf(Def def) => string.IsNullOrEmpty(def.label) ? def.defName : def.LabelCap.RawText;

        /// <summary>Resolves tags to colors. Returns the raw text if that fails, e.g. outside a game.</summary>
        public static string Resolve(TaggedString text)
        {
            try
            {
                return text.Resolve() ?? "";
            }
            catch (Exception)
            {
                return text.RawText ?? "";
            }
        }

        public static string? ModOf(Def def) => def.modContentPack?.Name;

        /// <summary>Picker options sorted by label, with icons, mods and defNames.</summary>
        public static List<SelectOption<T>> Options<T>(IEnumerable<T> defs, Func<T, bool>? filter) where T : Def =>
            defs.Where(d => filter?.Invoke(d) ?? true)
                .Select(d =>
                {
                    var mod = ModOf(d);
                    return new SelectOption<T>(d, LabelOf(d)) { Icon = d, Detail = mod, Keywords = d.defName, Group = mod };
                })
                .OrderBy(o => o.Label, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
    }
}

namespace Rimact
{
    internal static partial class Elements
    {
        public static ImageNode DefIcon(Def def, float size = 24f, ThingDef? stuff = null) => Image(Icon.Of(def, stuff)).Size(size, size);

        public static ImageNode PawnPortrait(Pawn pawn, float width = 64f, float height = 64f) => Image(Icon.PortraitOf(pawn)).Size(width, height);

        public static Components.InfoCardButton InfoCardButton(Def def, ThingDef? stuff = null) => new(def) { Stuff = stuff };

        public static Components.InfoCardButton InfoCardButton(Thing thing) => new(thing);

        public static Components.DefLabel DefLabel(Def def) => new(def);

        public static Components.DefPicker<T> DefPicker<T>(T? value, Action<T?>? onChange) where T : Def => new(value, onChange);

        public static Components.DefMultiPicker<T> DefMultiPicker<T>(IReadOnlyCollection<T> values, Action<IReadOnlyCollection<T>>? onChange) where T : Def =>
            new(values, onChange);
    }
}
