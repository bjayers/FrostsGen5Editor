using NewEditor.Data;
using NewEditor.Data.NARCTypes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    /// <summary>
    /// Following Pokémon: the follower runtime's dialogue, gifts and appearances,
    /// laid out after Pokeweb's Following Pokémon page. This build reads and
    /// previews everything; editing and saving come next.
    /// </summary>
    public class FollowingPokemonEditor : Form
    {
        public const string SpriteCredits = "Smogon Sprite Project, TraviS, LennyBitao, MyMarshlands, DarkusShadow, CarmaNekko, kiriaura, Gnomowladny, Krune, n-kin, JaegerLucciano23, joshr691, Jefelin, MultiDiegoDani, onigin_pixelart, Prodigal96, zerudez, leparagon, arinoelle, diegotoon20, gardow, greyenna, conyjams, kingofthe-x-roads, RayquazaFlygon, metalflygon08 on DeviantArt, and MaMe, maple, Layell, SelenaFF, Sopita Yorita, zlolxd - Pokémon Sprites";
        static readonly string[][] OverworldCredits =
        {
            new[] { "Gen 1-5 Pokemon Overworlds", "MissingLukey, help-14, Kymoyonian, cSc-A7X, 2and2makes5, Pokegirl4ever, Fernandojl, Silver-Skies, TyranitarDark, Getsuei-H, Kid1513, Milomilotic11, Kyt666, kdiamo11, Chocosrawlooid, Syledude, Gallanty, Gizamimi-Pichu, 2and2makes5, Zyon17,LarryTurbo, spritesstealer, LarryTurbo" },
            new[] { "Gen 6 Pokemon Overworlds", "princess-pheonix, LunarDusk, Wolfang62, TintjeMadelintje101, piphybuilder88" },
            new[] { "Gen 7 Pokemon Overworlds", "Larry Turbo, princess-pheonix" },
            new[] { "Gen 8 Pokemon Overworlds", "SageDeoxys, Wolfang62, LarryTurbo, tammyclaydon" },
            new[] { "PLA Pokemon Overworlds", "Boonzeet, DarkusShadow, princess-phoenix, Ezeart, WolfPP" },
            new[] { "Gen 9 Pokemon Overworlds", "Azria, DarkusShadow, EduarPokeN, Carmanekko, StarWolff, Caruban" },
            new[] { "PLZA Pokemon Overworlds", "DarkusShadow" },
        };
        static readonly Color PreviewBack = Color.FromArgb(0x39, 0x43, 0x4D);

        readonly FollowingData data;
        readonly List<string> speciesNames, itemNames, typeNames, zoneNames;
        readonly Dictionary<int, Bitmap> thumbnails = new Dictionary<int, Bitmap>();

        TabControl tabs;
        TabPage dialogueTab, giftTab, overviewTab, appearanceTab;
        ComboBox overviewSpecies;

        public FollowingPokemonEditor(FollowingData data)
        {
            this.data = data;
            speciesNames = Texts(VersionConstants.PokemonNameTextFileID);
            itemNames = Texts(VersionConstants.ItemNameTextFileID);
            typeNames = Texts(VersionConstants.TypeNameTextFileID);
            zoneNames = Texts(VersionConstants.ZoneNameTextFileID);

            Text = "Following Pokémon";
            Size = new Size(1100, 760);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;

            tabs = new TabControl { Dock = DockStyle.Fill };
            dialogueTab = new TabPage("Dialogue");
            giftTab = new TabPage("Gifts");
            overviewTab = new TabPage("Pokémon overview");
            appearanceTab = new TabPage("Appearances");
            tabs.TabPages.AddRange(new[] { dialogueTab, giftTab, overviewTab, appearanceTab });

            Controls.Add(tabs);
            Controls.Add(BuildHeader());

            BuildRuleTab(dialogueTab, true);
            BuildRuleTab(giftTab, false);
            BuildOverview();
            BuildAppearances();
            FormClosed += (s, e) =>
            {
                animation?.Stop();
                foreach (Bitmap b in thumbnails.Values) b?.Dispose();
                DisposeFrames();
            };
        }

        // ---------------------------------------------------------------- Names

        static List<string> Texts(int fileId)
        {
            var narc = MainEditor.textNarc;
            if (narc == null || fileId < 0 || fileId >= narc.textFiles.Count) return new List<string>();
            return narc.textFiles[fileId].text ?? new List<string>();
        }

        static string At(List<string> list, int index, string fallback) =>
            index >= 0 && index < list.Count && !string.IsNullOrWhiteSpace(list[index]) ? list[index] : fallback;

        string SpeciesName(int species) => At(speciesNames, species, "Species " + species);
        string ItemName(int item) => At(itemNames, item, "Item " + item);
        string TypeName(int type) => At(typeNames, type, "Type " + type);

        string ZoneLabel(int zone)
        {
            if (zone == 0) return "Any zone";
            var zones = MainEditor.zoneDataNarc?.zones;
            string name = zones != null && zone < zones.Count ? At(zoneNames, zones[zone].nameId, null) : null;
            return name != null ? name + " · Zone " + zone : "Zone " + zone;
        }

        /// <summary>The species' own and its alternate forms' types, as Pokeweb collects them.</summary>
        HashSet<int> SpeciesTypes(int species)
        {
            var types = new HashSet<int>();
            var pokemon = MainEditor.pokemonDataNarc?.pokemon;
            if (pokemon == null || species >= pokemon.Count) return types;
            var ids = new List<int> { species };
            if (pokemon[species].formsStart > 0)
                for (int i = 0; i < pokemon[species].numberOfForms - 1; i++) ids.Add(pokemon[species].formsStart + i);
            foreach (int id in ids)
                if (id < pokemon.Count) { types.Add(pokemon[id].type1); types.Add(pokemon[id].type2); }
            return types;
        }

        // -------------------------------------------------------------- Sprites

        static int DisplayForm(int species, int form) => species == 201 ? 27 : species == 493 ? 0 : form;

        /// <summary>The appearance a rule's sprite shows: normal colours, male, then genderless, then any.</summary>
        FollowerAppearance PreviewAppearance(int? species, int? form)
        {
            if (species == null || data.Registry == null) return null;
            int shown = DisplayForm(species.Value, form ?? 0);
            var matching = data.Registry.Entries.Where(e => e.Species == species && !e.Shiny && e.Form == shown).ToList();
            return matching.FirstOrDefault(e => e.Gender == 0) ?? matching.FirstOrDefault(e => e.Gender == 2) ?? matching.FirstOrDefault()
                ?? data.Registry.Entries.FirstOrDefault(e => e.Species == species && !e.Shiny && e.Form == 0);
        }

        Bitmap Thumbnail(FollowerAppearance appearance)
        {
            if (appearance == null) return null;
            if (!thumbnails.TryGetValue(appearance.ResourceId, out Bitmap bitmap))
            {
                try { bitmap = FollowerSprites.Frame(data.GetResource(appearance.ResourceId), appearance.Asymmetric, 1, 0, false); }
                catch { bitmap = null; }
                thumbnails[appearance.ResourceId] = bitmap;
            }
            return bitmap;
        }

        static void DrawPixelated(Graphics g, Image image, Rectangle box)
        {
            if (image == null) return;
            g.InterpolationMode = InterpolationMode.NearestNeighbor;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            int scale = Math.Max(1, Math.Min(box.Width / image.Width, box.Height / image.Height));
            var target = new Rectangle(box.X + (box.Width - image.Width * scale) / 2, box.Y + (box.Height - image.Height * scale) / 2, image.Width * scale, image.Height * scale);
            g.DrawImage(image, target);
        }

        /// <summary>A sprite box that scales by whole pixels, like Pokeweb's pixelated canvases.</summary>
        class PixelBox : Control
        {
            Image image;
            public Image Image { get => image; set { image = value; Invalidate(); } }
            public string Fallback = "";
            public PixelBox()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                BackColor = PreviewBack;
            }
            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(BackColor);
                if (image != null) DrawPixelated(e.Graphics, image, ClientRectangle);
                else TextRenderer.DrawText(e.Graphics, Fallback, Font, ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // --------------------------------------------------------------- Header

        Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 74, Padding = new Padding(10, 8, 10, 4) };
            int appearances = data.Registry?.Entries.Count ?? 0;
            string status = "Following Pokémon runtime " + (data.Version != null ? data.Version + " " : "") + "detected · " +
                (data.Enabled ? "following enabled" : "following disabled") + " · " + appearances + " appearances · " +
                data.Dialogues.Count + " dialogue rules · " + data.Gifts.Count + " gift rules";
            var title = new Label { Text = status, AutoSize = true, Location = new Point(10, 8), Font = new Font(Font, FontStyle.Bold) };
            var note = new Label
            {
                AutoSize = true, Location = new Point(10, 30),
                Text = data.Problems.Count > 0 ? "Some follower files could not be read: " + string.Join("; ", data.Problems)
                    : "Read-only preview: editing and saving arrive in the next build."
            };
            if (data.Problems.Count > 0) note.ForeColor = Color.IndianRed;
            var label = new Label { Text = "View rules for Pokémon", AutoSize = true, Location = new Point(10, 52) };
            overviewSpecies = SpeciesCombo(false);
            overviewSpecies.Location = new Point(160, 48);
            overviewSpecies.Width = 220;
            overviewSpecies.SelectedIndexChanged += (s, e) => { RenderOverview(); tabs.SelectedTab = overviewTab; };
            header.Controls.AddRange(new Control[] { title, note, label, overviewSpecies });
            return header;
        }

        ComboBox SpeciesCombo(bool withAny)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
            if (withAny) combo.Items.Add(new Choice(0, "Any species"));
            for (int i = 1; i < Math.Min(speciesNames.Count, 1024); i++)
                combo.Items.Add(new Choice(i, SpeciesName(i) + " #" + i));
            return combo;
        }

        class Choice
        {
            public readonly int Value;
            readonly string text;
            public Choice(int value, string text) { Value = value; this.text = text; }
            public override string ToString() => text;
        }

        static void SelectValue(ComboBox combo, int value)
        {
            foreach (object item in combo.Items)
                if (item is Choice choice && choice.Value == value) { combo.SelectedItem = item; return; }
            combo.SelectedIndex = combo.Items.Count > 0 ? 0 : -1;
        }

        // ------------------------------------------------------------ Rule tabs

        class RuleView
        {
            public bool dialogue;
            public ListBox list;
            public PixelBox sprite;
            public Label spriteCaption;
            public NumericUpDown zone, form, chance, slot, quantity;
            public Label zoneName;
            public ComboBox species, type, hp, friendship, status, facing, before, after, item;
            public TextBox minX, maxX, minY, maxY, text, itemName;
            public Control editor;
        }
        RuleView dialogueView, giftView;

        void BuildRuleTab(TabPage page, bool dialogue)
        {
            var view = new RuleView { dialogue = dialogue };
            if (dialogue) dialogueView = view; else giftView = view;

            var split = Split(page, 420);
            page.Padding = new Padding(6);

            var intro = new Label
            {
                Dock = DockStyle.Top, Height = 56, Padding = new Padding(2, 4, 2, 0),
                Text = dialogue
                    ? "Conditional dialogue: special lines in a location or for a matching Pokémon. Rules are checked from top to bottom; if none matches, the normal HGSS-style reaction is used."
                    : "Follower gifts: one-time rewards when the player talks to a matching follower. Each Pokémon remembers ten claim slots; the first matching unclaimed gift wins."
            };
            view.list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46, IntegralHeight = false };
            int count = dialogue ? data.Dialogues.Count : data.Gifts.Count;
            for (int i = 0; i < count; i++) view.list.Items.Add(i);
            view.list.DrawItem += (s, e) => DrawRule(view, e);
            view.list.SelectedIndexChanged += (s, e) => ShowRule(view);

            var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 4, 0, 0) };
            foreach (string label in new[] { dialogue ? "Add dialogue rule" : "Add gift rule", "Remove", "Move up", "Move down", dialogue ? "Save dialogue" : "Save gifts" })
                buttons.Controls.Add(new Button { Text = label, AutoSize = true, Enabled = false });
            split.Panel1.Controls.Add(view.list);
            split.Panel1.Controls.Add(intro);
            split.Panel1.Controls.Add(buttons);

            var editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, Padding = new Padding(8, 4, 8, 8) };
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            view.editor = editor;
            split.Panel2.Controls.Add(editor);

            view.sprite = new PixelBox { Size = new Size(96, 96), Fallback = "Any" };
            view.spriteCaption = new Label { AutoSize = true, Padding = new Padding(0, 36, 0, 0) };
            editor.Controls.Add(view.sprite, 0, 0);
            editor.Controls.Add(view.spriteCaption, 1, 0);
            editor.SetColumnSpan(view.spriteCaption, 3);

            int row = 1;
            if (!dialogue)
            {
                Section(editor, "Reward", ref row);
                view.slot = Number(0, 9);
                view.item = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220 };
                for (int i = 1; i < itemNames.Count; i++) view.item.Items.Add(new Choice(i, ItemName(i) + " #" + i));
                view.quantity = Number(1, 99);
                view.itemName = new TextBox { Width = 220, MaxLength = 191 };
                Pair(editor, ref row, "Claim slot", view.slot, "Item", view.item);
                Pair(editor, ref row, "Quantity", view.quantity, "Name shown in dialogue", view.itemName);
            }
            Section(editor, dialogue ? "Location and Pokémon" : "When this gift applies", ref row);
            view.zone = Number(0, 65535);
            view.zoneName = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
            view.species = SpeciesCombo(true);
            view.form = Number(-1, 255);
            view.type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 160 };
            view.type.Items.Add(new Choice(-1, "Any type"));
            for (int i = 0; i < typeNames.Count; i++) view.type.Items.Add(new Choice(i, TypeName(i)));
            Pair(editor, ref row, "Zone ID", view.zone, "", view.zoneName);
            Pair(editor, ref row, "Species", view.species, "Form (-1 = any)", view.form);
            Pair(editor, ref row, "Type", view.type, "", new Label());

            Section(editor, "Player tile bounds (optional, inclusive; blank is unrestricted)", ref row);
            view.minX = BoundBox(); view.maxX = BoundBox(); view.minY = BoundBox(); view.maxY = BoundBox();
            Pair(editor, ref row, "Min X", view.minX, "Max X", view.maxX);
            Pair(editor, ref row, "Min Y", view.minY, "Max Y", view.maxY);

            Section(editor, "Optional conditions", ref row);
            view.hp = Options(FollowerRules.HpConditions);
            view.friendship = Options(FollowerRules.FriendshipConditions);
            view.status = Options(FollowerRules.StatusConditions);
            view.facing = Options(FollowerRules.FacingConditions);
            Pair(editor, ref row, "HP", view.hp, "Friendship", view.friendship);
            Pair(editor, ref row, "Status", view.status, "Direction", view.facing);
            if (dialogue)
            {
                view.chance = Number(1, 100);
                Pair(editor, ref row, "Trigger chance (%)", view.chance, "", new Label());
                view.before = Options(FollowerRules.Animations);
                view.after = Options(FollowerRules.Animations);
                Pair(editor, ref row, "Before dialogue", view.before, "After dialogue", view.after);
            }
            view.text = new TextBox { Multiline = true, Height = 64, Width = 520, MaxLength = 191, ScrollBars = ScrollBars.Vertical };
            Wide(editor, ref row, dialogue ? "Dialogue text" : "Success message", view.text);
            var tokens = new Label { AutoSize = true, Text = dialogue ? "Tokens: {nickname}, {player}, {location}" : "Tokens: {nickname}, {player}, {location}, {item}" };
            Wide(editor, ref row, "", tokens);
            editor.RowCount = row + 1;

            foreach (Control c in editor.Controls)
                if (!(c is Label) && !(c is PixelBox)) c.Enabled = false;
            // Selected once the list has its size, so the first card stays in view.
            Load += (s, e) =>
            {
                if (count > 0) { view.list.SelectedIndex = 0; view.list.TopIndex = 0; }
                else ShowRule(view);
            };
        }

        /// <summary>
        /// A left list and right detail pane. The divider is placed once the
        /// window has its size; set earlier, SplitContainer ignores it.
        /// </summary>
        SplitContainer Split(Control parent, int distance)
        {
            var split = new SplitContainer { Dock = DockStyle.Fill, FixedPanel = FixedPanel.Panel1, Panel1MinSize = 280 };
            parent.Controls.Add(split);
            Load += (s, e) => split.SplitterDistance = distance;
            return split;
        }

        static NumericUpDown Number(int min, int max) => new NumericUpDown { Minimum = min, Maximum = max, Width = 90 };
        static TextBox BoundBox() => new TextBox { Width = 90 };
        static ComboBox Options(string[] labels)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180 };
            for (int i = 0; i < labels.Length; i++)
                if (labels[i] != "") combo.Items.Add(new Choice(i, labels[i]));
            return combo;
        }

        static void Section(TableLayoutPanel table, string text, ref int row)
        {
            var label = new Label { Text = text, AutoSize = true, Font = new Font(table.Font, FontStyle.Bold), Padding = new Padding(0, 10, 0, 2) };
            table.Controls.Add(label, 0, row);
            table.SetColumnSpan(label, 4);
            row++;
        }

        static void Pair(TableLayoutPanel table, ref int row, string first, Control firstControl, string second, Control secondControl)
        {
            table.Controls.Add(new Label { Text = first, AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, row);
            table.Controls.Add(firstControl, 1, row);
            table.Controls.Add(new Label { Text = second, AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 2, row);
            table.Controls.Add(secondControl, 3, row);
            row++;
        }

        static void Wide(TableLayoutPanel table, ref int row, string label, Control control)
        {
            table.Controls.Add(new Label { Text = label, AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, row);
            table.Controls.Add(control, 1, row);
            table.SetColumnSpan(control, 3);
            row++;
        }

        FollowerRuleConditions Rule(RuleView view, int index) =>
            view.dialogue ? (FollowerRuleConditions)data.Dialogues[index] : data.Gifts[index];

        string RulePrimary(RuleView view, int index)
        {
            if (view.dialogue) return data.Dialogues[index].Text.Replace('\n', ' ');
            var gift = data.Gifts[index];
            return ItemName(gift.ItemId) + (gift.Quantity > 1 ? " ×" + gift.Quantity : "") + " · claim slot " + gift.Slot;
        }

        string RuleQualifier(FollowerRuleConditions rule)
        {
            var parts = new List<string> { rule.Species == null ? "Any species" : SpeciesName(rule.Species.Value) };
            if (rule.Form != null) parts.Add("Form " + rule.Form);
            if (rule.Type != null) parts.Add(TypeName(rule.Type.Value));
            if (rule.MinX != null) parts.Add("minX: " + rule.MinX);
            if (rule.MaxX != null) parts.Add("maxX: " + rule.MaxX);
            if (rule.MinY != null) parts.Add("minY: " + rule.MinY);
            if (rule.MaxY != null) parts.Add("maxY: " + rule.MaxY);
            return string.Join(" · ", parts);
        }

        /// <summary>A rule card: sprite, primary text, zone and qualifiers, like Pokeweb's rule summaries.</summary>
        void DrawRule(RuleView view, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0) return;
            var rule = Rule(view, e.Index);
            var box = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, 40, 40);
            using (var back = new SolidBrush(PreviewBack)) e.Graphics.FillRectangle(back, box);
            Bitmap sprite = Thumbnail(PreviewAppearance(rule.Species, rule.Form));
            if (sprite != null) DrawPixelated(e.Graphics, sprite, box);
            else TextRenderer.DrawText(e.Graphics, rule.Species == null ? "Any" : "?", e.Font, box, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            var text = new Rectangle(box.Right + 8, e.Bounds.Y + 4, e.Bounds.Width - box.Width - 16, 18);
            TextRenderer.DrawText(e.Graphics, (e.Index + 1) + ". " + RulePrimary(view, e.Index), e.Font, text, e.ForeColor, TextFormatFlags.EndEllipsis);
            text.Y += 20;
            TextRenderer.DrawText(e.Graphics, ZoneLabel(rule.Zone) + " · " + RuleQualifier(rule), e.Font, text, e.ForeColor, TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        static string Bound(int? value) => value == null ? "" : value.ToString();

        void ShowRule(RuleView view)
        {
            int index = view.list.SelectedIndex;
            if (index < 0)
            {
                view.sprite.Image = null;
                view.spriteCaption.Text = (view.dialogue ? "No dialogue rules" : "No gift rules") + " in this ROM.";
                return;
            }
            var rule = Rule(view, index);
            var appearance = PreviewAppearance(rule.Species, rule.Form);
            view.sprite.Image = Thumbnail(appearance);
            view.sprite.Fallback = rule.Species == null ? "Any" : "?";
            view.spriteCaption.Text = (view.dialogue ? "Dialogue rule " : "Gift rule ") + (index + 1) + " · " +
                (rule.Species == null ? "Any Pokémon" : SpeciesName(rule.Species.Value) + " #" + rule.Species) + " · " + ZoneLabel(rule.Zone);

            view.zone.Value = rule.Zone;
            view.zoneName.Text = ZoneLabel(rule.Zone);
            SelectValue(view.species, rule.Species ?? 0);
            view.form.Value = rule.Form ?? -1;
            SelectValue(view.type, rule.Type ?? -1);
            view.minX.Text = Bound(rule.MinX); view.maxX.Text = Bound(rule.MaxX);
            view.minY.Text = Bound(rule.MinY); view.maxY.Text = Bound(rule.MaxY);
            SelectValue(view.hp, rule.Hp ?? 0);
            SelectValue(view.friendship, rule.Friendship ?? 0);
            SelectValue(view.status, rule.Status ?? 0);
            SelectValue(view.facing, rule.Facing ?? 0);
            if (view.dialogue)
            {
                var d = data.Dialogues[index];
                view.chance.Value = Math.Max(1, Math.Min(100, d.Chance));
                SelectValue(view.before, d.BeforeAnimation ?? 0);
                SelectValue(view.after, d.AfterAnimation ?? 0);
                view.text.Text = d.Text.Replace("\n", Environment.NewLine);
            }
            else
            {
                var g = data.Gifts[index];
                view.slot.Value = g.Slot;
                SelectValue(view.item, g.ItemId);
                view.quantity.Value = Math.Max(1, Math.Min(99, g.Quantity));
                view.itemName.Text = g.ItemName;
                view.text.Text = g.Text.Replace("\n", Environment.NewLine);
            }
        }

        // ------------------------------------------------------------- Overview

        PixelBox overviewSprite;
        Label overviewTitle, overviewTypes;
        ListBox overviewDialogues, overviewGifts;

        void BuildOverview()
        {
            overviewTab.Padding = new Padding(8);
            var top = new Panel { Dock = DockStyle.Top, Height = 92 };
            overviewSprite = new PixelBox { Size = new Size(80, 80), Location = new Point(0, 4), Fallback = "?" };
            overviewTitle = new Label { AutoSize = true, Location = new Point(92, 18), Font = new Font(Font.FontFamily, 12, FontStyle.Bold), Text = "Pokémon overview" };
            overviewTypes = new Label { AutoSize = true, Location = new Point(92, 46), Text = "Choose a Pokémon above to collect all of its applicable dialogue and gift rules in one view." };
            top.Controls.AddRange(new Control[] { overviewSprite, overviewTitle, overviewTypes });

            var columns = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2 };
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            columns.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            columns.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            columns.Controls.Add(new Label { Text = "Dialogue", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 0, 0);
            columns.Controls.Add(new Label { Text = "Gifts", AutoSize = true, Font = new Font(Font, FontStyle.Bold) }, 1, 0);
            overviewDialogues = OverviewList(true);
            overviewGifts = OverviewList(false);
            columns.Controls.Add(overviewDialogues, 0, 1);
            columns.Controls.Add(overviewGifts, 1, 1);
            overviewTab.Controls.Add(columns);
            overviewTab.Controls.Add(top);
        }

        ListBox OverviewList(bool dialogue)
        {
            var list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46, IntegralHeight = false };
            list.DrawItem += (s, e) =>
            {
                e.DrawBackground();
                if (e.Index < 0) return;
                int index = (int)list.Items[e.Index];
                RuleView view = dialogue ? dialogueView : giftView;
                var rule = Rule(view, index);
                var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 4, e.Bounds.Width - 12, 18);
                TextRenderer.DrawText(e.Graphics, RulePrimary(view, index), e.Font, text, e.ForeColor, TextFormatFlags.EndEllipsis);
                text.Y += 20;
                TextRenderer.DrawText(e.Graphics, ZoneLabel(rule.Zone) + " · " + RuleQualifier(rule) + " · Rule " + (index + 1), e.Font, text, e.ForeColor, TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            list.DoubleClick += (s, e) =>
            {
                if (list.SelectedIndex < 0) return;
                RuleView view = dialogue ? dialogueView : giftView;
                tabs.SelectedTab = dialogue ? dialogueTab : giftTab;
                view.list.SelectedIndex = (int)list.SelectedItem;
            };
            return list;
        }

        void RenderOverview()
        {
            if (!(overviewSpecies.SelectedItem is Choice choice)) return;
            int species = choice.Value;
            HashSet<int> types = SpeciesTypes(species);
            overviewSprite.Image = Thumbnail(PreviewAppearance(species, 0));
            overviewTitle.Text = SpeciesName(species) + " #" + species;
            overviewTypes.Text = types.Count > 0 ? string.Join(" / ", types.Select(TypeName)) + " · Double-click a rule to open it." : "Type data unavailable";
            overviewDialogues.Items.Clear();
            overviewGifts.Items.Clear();
            for (int i = 0; i < data.Dialogues.Count; i++)
                if (data.Dialogues[i].AppliesTo(species, types)) overviewDialogues.Items.Add(i);
            for (int i = 0; i < data.Gifts.Count; i++)
                if (data.Gifts[i].AppliesTo(species, types)) overviewGifts.Items.Add(i);
        }

        // ---------------------------------------------------------- Appearances

        ListBox appearanceList;
        NumericUpDown speciesFilter, followDistance, height;
        CheckBox fallbackOnly, walking;
        Label appearanceDetail;
        readonly PixelBox[] previews = new PixelBox[4];
        Bitmap[,] frames;
        Timer animation;
        int tick;
        List<FollowerAppearance> shownAppearances = new List<FollowerAppearance>();

        void BuildAppearances()
        {
            appearanceTab.Padding = new Padding(8);
            var split = Split(appearanceTab, 400);

            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, WrapContents = true };
            int fallbacks = data.Registry?.Entries.Count(e => e.Placeholder) ?? 0;
            filters.Controls.Add(new Label { AutoSize = true, Text = "Catalog: " + (data.Registry?.Entries.Count ?? 0) + " appearances · " + fallbacks + " fallback appearances", Padding = new Padding(0, 4, 0, 4) });
            filters.SetFlowBreak(filters.Controls[0], true);
            filters.Controls.Add(new Label { AutoSize = true, Text = "Species number (0 = all)", Padding = new Padding(0, 6, 0, 0) });
            speciesFilter = Number(0, 1023);
            fallbackOnly = new CheckBox { Text = "Fallback artwork only", AutoSize = true, Padding = new Padding(8, 2, 0, 0) };
            filters.Controls.Add(speciesFilter);
            filters.Controls.Add(fallbackOnly);
            appearanceList = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 40, IntegralHeight = false };
            appearanceList.DrawItem += DrawAppearance;
            appearanceList.SelectedIndexChanged += (s, e) => ShowAppearance();
            speciesFilter.ValueChanged += (s, e) => FillAppearances();
            fallbackOnly.CheckedChanged += (s, e) => FillAppearances();
            split.Panel1.Controls.Add(appearanceList);
            split.Panel1.Controls.Add(filters);

            var right = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true, Padding = new Padding(10, 0, 0, 0) };
            split.Panel2.Controls.Add(right);
            appearanceDetail = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 2, 0, 8) };
            right.Controls.Add(appearanceDetail);

            var row = new FlowLayoutPanel { AutoSize = true, WrapContents = false };
            for (int i = 0; i < 4; i++)
            {
                var figure = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, Margin = new Padding(0, 0, 10, 0) };
                previews[i] = new PixelBox { Size = new Size(128, 128) };
                figure.Controls.Add(previews[i]);
                figure.Controls.Add(new Label { Text = FollowerSprites.Directions[i], Width = 128, TextAlign = ContentAlignment.MiddleCenter });
                row.Controls.Add(figure);
            }
            right.Controls.Add(row);
            walking = new CheckBox { Text = "Walk animation", Checked = true, AutoSize = true, Margin = new Padding(0, 6, 0, 6) };
            right.Controls.Add(walking);

            var tuning = new TableLayoutPanel { AutoSize = true, ColumnCount = 3, Margin = new Padding(0, 4, 0, 4) };
            followDistance = Number(0, 12);
            height = Number(-128, 127);
            followDistance.Enabled = height.Enabled = false;
            tuning.Controls.Add(new Label { Text = "Follow distance", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
            tuning.Controls.Add(followDistance, 1, 0);
            tuning.Controls.Add(new Label { Text = "World units behind the player, 0–12. 64-pixel followers add their own bonus.", AutoSize = true, Padding = new Padding(6, 6, 0, 0) }, 2, 0);
            tuning.Controls.Add(new Label { Text = "Height", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 1);
            tuning.Controls.Add(height, 1, 1);
            tuning.Controls.Add(new Label { Text = "Sprite pixels; positive raises it. The shadow stays on the ground.", AutoSize = true, Padding = new Padding(6, 6, 0, 0) }, 2, 1);
            right.Controls.Add(tuning);

            right.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 8, 0, 4),
                Text = "PNG layout: rows up, down, left, right; columns idle and step. Use 64×128 for 32-pixel frames or 128×256 for 64-pixel frames. Maximum 15 opaque DS colors plus transparency."
            });
            var actions = new FlowLayoutPanel { AutoSize = true };
            var export = new Button { Text = "Export selected PNG", AutoSize = true };
            export.Click += (s, e) => ExportPng();
            actions.Controls.Add(new Button { Text = "Replace selected PNG", AutoSize = true, Enabled = false });
            actions.Controls.Add(export);
            actions.Controls.Add(new Button { Text = "Export asset pack", AutoSize = true, Enabled = false });
            actions.Controls.Add(new Button { Text = "Import asset pack", AutoSize = true, Enabled = false });
            right.Controls.Add(actions);

            var credits = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 16, 0, 0), ForeColor = SystemColors.GrayText };
            credits.Text = "Sprite credits\n" + SpriteCredits + "\n\n" + string.Join("\n", OverworldCredits.Select(c => c[0] + " — " + c[1]));
            right.Controls.Add(credits);

            animation = new Timer { Interval = 1000 / 12 };
            animation.Tick += (s, e) => { tick += 5; DrawPreviews(); };
            FillAppearances();
            animation.Start();
        }

        void FillAppearances()
        {
            int species = (int)speciesFilter.Value;
            shownAppearances = (data.Registry?.Entries ?? new List<FollowerAppearance>())
                .Where(e => (species == 0 || e.Species == species) && (!fallbackOnly.Checked || e.Placeholder)).ToList();
            appearanceList.BeginUpdate();
            appearanceList.Items.Clear();
            for (int i = 0; i < shownAppearances.Count; i++) appearanceList.Items.Add(i);
            appearanceList.EndUpdate();
            if (shownAppearances.Count > 0) appearanceList.SelectedIndex = 0;
            else ShowAppearance();
        }

        string AppearanceLabel(FollowerAppearance a) =>
            SpeciesName(a.Species) + " #" + a.Species + " · form " + a.Form + " · " + FollowerAppearance.GenderNames[Math.Min(2, a.Gender)] +
            " · " + (a.Shiny ? "shiny" : "normal") + (a.Placeholder ? " · placeholder" : "");

        void DrawAppearance(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= shownAppearances.Count) return;
            var a = shownAppearances[e.Index];
            var box = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, 36, 36);
            using (var back = new SolidBrush(PreviewBack)) e.Graphics.FillRectangle(back, box);
            Bitmap sprite = Thumbnail(a);
            if (sprite != null) DrawPixelated(e.Graphics, sprite, box);
            var text = new Rectangle(box.Right + 8, e.Bounds.Y, e.Bounds.Width - box.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, AppearanceLabel(a), e.Font, text, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        FollowerAppearance SelectedAppearance =>
            appearanceList.SelectedIndex >= 0 && appearanceList.SelectedIndex < shownAppearances.Count ? shownAppearances[appearanceList.SelectedIndex] : null;

        void DisposeFrames()
        {
            if (frames == null) return;
            foreach (Bitmap b in frames) b?.Dispose();
            frames = null;
        }

        void ShowAppearance()
        {
            DisposeFrames();
            var a = SelectedAppearance;
            if (a == null)
            {
                appearanceDetail.Text = "No matching appearances.";
                foreach (var p in previews) p.Image = null;
                return;
            }
            byte[] resource = data.GetResource(a.ResourceId);
            frames = new Bitmap[4, 2];
            for (int d = 0; d < 4; d++)
                for (int pose = 0; pose < 2; pose++)
                {
                    try { frames[d, pose] = FollowerSprites.Frame(resource, a.Asymmetric, d, pose == 0 ? 0 : (d == 1 ? 5 : 10), true); }
                    catch { frames[d, pose] = null; }
                }
            int distance = data.FollowDistance(a);
            followDistance.Value = Math.Max(0, Math.Min(12, distance));
            height.Value = a.OffsetY;
            appearanceDetail.Text = AppearanceLabel(a) + "\n" +
                (a.Placeholder ? "Fallback artwork (shared with another appearance)" : "Own artwork") + " · " + a.Size + "-pixel frames · " +
                (a.Asymmetric ? "8 frames" : "6 frames, right mirrored from left") + " · resource " + a.ResourceId + " · descriptor row " + a.DescriptorRow +
                (distance < 0 ? "\nNo positioning row: the runtime uses its default distance." : "");
            DrawPreviews();
        }

        /// <summary>Four frames per direction at 12 fps, as Pokeweb's preview cycles ticks 0, 5, 10 and 15.</summary>
        void DrawPreviews()
        {
            if (frames == null) return;
            for (int d = 0; d < 4; d++)
            {
                int phase = walking.Checked ? tick % 20 : 0;
                int pose = d == 1 ? (phase >= 5 && phase < 15 ? 1 : 0) : (phase >= 10 ? 1 : 0);
                previews[d].Image = frames[d, pose];
            }
        }

        /// <summary>The selected appearance as a two-column sheet: rows up, down, left, right; idle then step.</summary>
        void ExportPng()
        {
            var a = SelectedAppearance;
            if (a == null || frames == null || frames[0, 0] == null) return;
            var save = new SaveFileDialog { Filter = "PNG image|*.png", FileName = "follower-" + a.Key.Replace(':', '-') + ".png" };
            if (save.ShowDialog() != DialogResult.OK) return;
            int size = frames[0, 0].Width;
            using (var sheet = new Bitmap(size * 2, size * 4, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(sheet))
                    for (int d = 0; d < 4; d++)
                        for (int pose = 0; pose < 2; pose++)
                            if (frames[d, pose] != null) g.DrawImageUnscaled(frames[d, pose], pose * size, d * size);
                sheet.Save(save.FileName, ImageFormat.Png);
            }
        }
    }
}
