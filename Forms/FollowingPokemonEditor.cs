using NewEditor.Data;
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
    /// laid out after Pokeweb's Following Pokémon page. Each tab edits a draft
    /// that its Save button writes into the loaded ROM.
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
        readonly Dictionary<string, Bitmap> thumbnails = new Dictionary<string, Bitmap>();
        readonly List<SearchComboBox.Option> speciesOptions = new List<SearchComboBox.Option>();
        readonly List<SearchComboBox.Option> itemOptions = new List<SearchComboBox.Option>();

        TabControl tabs;
        TabPage dialogueTab, giftTab, overviewTab, appearanceTab, creditsTab;
        SearchComboBox overviewSpecies;

        public FollowingPokemonEditor(FollowingData data)
        {
            this.data = data;
            speciesNames = Texts(VersionConstants.PokemonNameTextFileID);
            itemNames = Texts(VersionConstants.ItemNameTextFileID);
            typeNames = Texts(VersionConstants.TypeNameTextFileID);
            zoneNames = Texts(VersionConstants.ZoneNameTextFileID);
            for (int i = 1; i < Math.Min(speciesNames.Count, 1024); i++) speciesOptions.Add(new SearchComboBox.Option(i, SpeciesName(i) + " #" + i));
            for (int i = 1; i < itemNames.Count; i++) itemOptions.Add(new SearchComboBox.Option(i, ItemName(i) + " #" + i));

            Text = "Following Pokémon";
            Size = new Size(1100, 780);
            MinimumSize = new Size(900, 600);
            StartPosition = FormStartPosition.CenterScreen;

            tabs = new TabControl { Dock = DockStyle.Fill };
            dialogueTab = new TabPage("Dialogue");
            giftTab = new TabPage("Gifts");
            overviewTab = new TabPage("Pokémon overview");
            appearanceTab = new TabPage("Appearances");
            creditsTab = new TabPage("Credits");
            tabs.TabPages.AddRange(new[] { dialogueTab, giftTab, overviewTab, appearanceTab, creditsTab });
            tabs.SelectedIndexChanged += (s, e) => { if (tabs.SelectedTab == overviewTab) RenderOverview(); };

            Controls.Add(tabs);
            Controls.Add(BuildHeader());

            dialogueView = new RuleView(this, true);
            giftView = new RuleView(this, false);
            dialogueView.Build(dialogueTab);
            giftView.Build(giftTab);
            BuildOverview();
            BuildAppearances();
            BuildCredits();

            FormClosing += (s, e) =>
            {
                if (e.CloseReason == CloseReason.ApplicationExitCall) return;
                if (!ResolveUnsaved("closing")) e.Cancel = true;
            };
            FormClosed += (s, e) =>
            {
                animation?.Stop();
                foreach (Bitmap b in thumbnails.Values) b?.Dispose();
                DisposeFrames();
            };
        }

        public bool HasUnsavedChanges => dialogueView.Dirty || giftView.Dirty || appearancesDirty;

        /// <summary>
        /// Ask about each tab with unsaved changes: Yes saves it, No leaves it
        /// unsaved, Cancel stops. False when the caller should stop, after a
        /// Cancel or a save that failed.
        /// </summary>
        public bool ResolveUnsaved(string beforeWhat)
        {
            foreach (RuleView view in new[] { dialogueView, giftView })
            {
                if (!view.Dirty) continue;
                tabs.SelectedTab = view.Page;
                DialogResult answer = MessageBox.Show("Save the changes to " + view.Noun + " before " + beforeWhat + "?", "Following Pokémon",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !view.Save())) return false;
            }
            if (appearancesDirty)
            {
                tabs.SelectedTab = appearanceTab;
                DialogResult answer = MessageBox.Show("Save the changes to appearances before " + beforeWhat + "?", "Following Pokémon",
                    MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                if (answer == DialogResult.Cancel || (answer == DialogResult.Yes && !SaveAppearances())) return false;
            }
            return true;
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
            string key = "resource:" + appearance.ResourceId;
            if (!thumbnails.TryGetValue(key, out Bitmap bitmap))
            {
                try { bitmap = FollowerSprites.Frame(data.GetResource(appearance.ResourceId), appearance.Asymmetric, 1, 0, false); }
                catch { bitmap = null; }
                thumbnails[key] = bitmap;
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
                e.Graphics.Clear(PreviewBack);
                if (image != null) DrawPixelated(e.Graphics, image, ClientRectangle);
                else TextRenderer.DrawText(e.Graphics, Fallback, Font, ClientRectangle, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }

        // --------------------------------------------------------------- Header

        Control BuildHeader()
        {
            var header = new Panel { Dock = DockStyle.Top, Height = 76, Padding = new Padding(10, 8, 10, 4) };
            int appearances = data.Registry?.Entries.Count ?? 0;
            string status = "Following Pokémon runtime " + (data.Version != null ? data.Version + " " : "") + "detected · " +
                (data.Enabled ? "following enabled" : "following disabled") + " · " + appearances + " appearances";
            var title = new Label { Text = status, AutoSize = true, Location = new Point(10, 8), Font = new Font(Font, FontStyle.Bold) };
            var note = new Label
            {
                AutoSize = true, Location = new Point(10, 30),
                Text = data.Problems.Count > 0 ? "Some follower files could not be read: " + string.Join("; ", data.Problems)
                    : "Saving a tab writes it into the loaded ROM. Use Save Rom on the main window to write the ROM file."
            };
            if (data.Problems.Count > 0) note.ForeColor = Color.IndianRed;
            var label = new Label { Text = "View rules for Pokémon", AutoSize = true, Location = new Point(10, 53) };
            overviewSpecies = new SearchComboBox { Location = new Point(160, 49), Width = 260 };
            overviewSpecies.SetOptions(speciesOptions);
            overviewSpecies.ValueChosen += (s, e) => { tabs.SelectedTab = overviewTab; RenderOverview(); };
            var hint = new Label { Text = "Type a name or number to search.", AutoSize = true, Location = new Point(428, 53), ForeColor = SystemColors.GrayText };
            header.Controls.AddRange(new Control[] { title, note, label, overviewSpecies, hint });
            return header;
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

        static int ValueOf(ComboBox combo) => combo.SelectedItem is Choice choice ? choice.Value : 0;

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

        static ComboBox Options(string[] labels)
        {
            var combo = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
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

        // ------------------------------------------------------------ Rule tabs

        RuleView dialogueView, giftView;

        /// <summary>
        /// One rule tab: an ordered list of rule cards and the selected rule's
        /// fields. Edits change a draft; Save encodes it into the ROM.
        /// </summary>
        class RuleView
        {
            readonly FollowingPokemonEditor owner;
            public readonly bool IsDialogue;
            public TabPage Page;
            public bool Dirty;
            public string Noun => IsDialogue ? "dialogue" : "gifts";
            readonly List<FollowerDialogueRule> dialogues;
            readonly List<FollowerGiftRule> gifts;
            bool loading;

            ListBox list;
            Label sizeLabel, statusLabel, empty;
            Button remove, up, down, save, revert;
            TableLayoutPanel editor;
            PixelBox sprite;
            Label spriteCaption, zoneName;
            NumericUpDown zone, form, chance, slot, quantity;
            SearchComboBox species, item;
            ComboBox type, hp, friendship, status, facing, before, after;
            TextBox minX, maxX, minY, maxY, text, itemName;

            public RuleView(FollowingPokemonEditor owner, bool dialogue)
            {
                this.owner = owner;
                IsDialogue = dialogue;
                if (dialogue) dialogues = owner.data.Dialogues.Select(r => r.Copy()).ToList();
                else gifts = owner.data.Gifts.Select(r => r.Copy()).ToList();
            }

            public int Count => IsDialogue ? dialogues.Count : gifts.Count;
            public FollowerRuleConditions Rule(int index) => IsDialogue ? (FollowerRuleConditions)dialogues[index] : gifts[index];
            public IEnumerable<FollowerRuleConditions> Rules => IsDialogue ? dialogues.Cast<FollowerRuleConditions>() : gifts;

            public string Primary(int index)
            {
                if (IsDialogue) return dialogues[index].Text.Replace('\n', ' ');
                var gift = gifts[index];
                return owner.ItemName(gift.ItemId) + (gift.Quantity > 1 ? " ×" + gift.Quantity : "") + " · claim slot " + gift.Slot;
            }

            /// <summary>Redraw the cards and the selected rule's sprite after the sprites change.</summary>
            public void Refresh()
            {
                list.Invalidate();
                if (list.SelectedIndex >= 0 && list.SelectedIndex < Count) ShowCaption(list.SelectedIndex);
            }

            public void Select(int index)
            {
                if (index >= 0 && index < list.Items.Count) list.SelectedIndex = index;
            }

            public void Build(TabPage page)
            {
                Page = page;
                var split = owner.Split(page, 420);
                page.Padding = new Padding(6);

                var intro = new Label
                {
                    Dock = DockStyle.Top, Height = 56, Padding = new Padding(2, 4, 2, 0),
                    Text = IsDialogue
                        ? "Conditional dialogue: special lines in a location or for a matching Pokémon. Rules are checked from top to bottom; if none matches, the normal HGSS-style reaction is used."
                        : "Follower gifts: one-time rewards when the player talks to a matching follower. Each Pokémon remembers ten claim slots; the first matching unclaimed gift wins."
                };
                list = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 46, IntegralHeight = false };
                list.DrawItem += DrawCard;
                list.SelectedIndexChanged += (s, e) => Show();

                var buttons = new FlowLayoutPanel { Dock = DockStyle.Bottom, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(0, 4, 0, 0) };
                var add = new Button { Text = IsDialogue ? "Add dialogue rule" : "Add gift rule", AutoSize = true };
                remove = new Button { Text = "Remove", AutoSize = true };
                up = new Button { Text = "Move up", AutoSize = true };
                down = new Button { Text = "Move down", AutoSize = true };
                save = new Button { Text = IsDialogue ? "Save dialogue" : "Save gifts", AutoSize = true };
                revert = new Button { Text = "Discard changes", AutoSize = true };
                add.Click += (s, e) => Add();
                remove.Click += (s, e) => Remove();
                up.Click += (s, e) => Move(-1);
                down.Click += (s, e) => Move(1);
                save.Click += (s, e) => Save();
                revert.Click += (s, e) => Revert();
                buttons.Controls.AddRange(new Control[] { add, remove, up, down, save, revert });
                sizeLabel = new Label { Dock = DockStyle.Bottom, Height = 20 };
                statusLabel = new Label { Dock = DockStyle.Bottom, Height = 36 };
                split.Panel1.Controls.Add(list);
                split.Panel1.Controls.Add(intro);
                split.Panel1.Controls.Add(statusLabel);
                split.Panel1.Controls.Add(sizeLabel);
                split.Panel1.Controls.Add(buttons);

                empty = new Label { Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Text = "No " + (IsDialogue ? "dialogue" : "gift") + " rules yet. Add one to get started.", Visible = false };
                editor = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoScroll = true, Padding = new Padding(8, 4, 8, 8) };
                editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                editor.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
                editor.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
                split.Panel2.Controls.Add(editor);
                split.Panel2.Controls.Add(empty);
                BuildFields();

                Fill();
                // Selected once the list has its size, so the first card stays in view.
                owner.Load += (s, e) =>
                {
                    if (Count > 0) { list.SelectedIndex = 0; list.TopIndex = 0; }
                    else Show();
                    UpdateSize();
                };
            }

            void BuildFields()
            {
                sprite = new PixelBox { Size = new Size(96, 96), Fallback = "Any" };
                spriteCaption = new Label { AutoSize = true, Padding = new Padding(0, 36, 0, 0) };
                editor.Controls.Add(sprite, 0, 0);
                editor.Controls.Add(spriteCaption, 1, 0);
                editor.SetColumnSpan(spriteCaption, 3);

                int row = 1;
                if (!IsDialogue)
                {
                    Section(editor, "Reward", ref row);
                    slot = Number(0, 9);
                    item = new SearchComboBox { Width = 300 };
                    item.SetOptions(owner.itemOptions);
                    quantity = Number(1, 99);
                    itemName = new TextBox { Width = 300, MaxLength = 191 };
                    Wide(editor, ref row, "Item", item);
                    Pair(editor, ref row, "Claim slot", slot, "Quantity", quantity);
                    Wide(editor, ref row, "Name shown in dialogue", itemName);
                }
                Section(editor, IsDialogue ? "Location and Pokémon" : "When this gift applies", ref row);
                zone = Number(0, 65535);
                zoneName = new Label { AutoSize = true, Padding = new Padding(0, 6, 0, 0) };
                species = new SearchComboBox { Width = 300 };
                species.SetOptions(new[] { new SearchComboBox.Option(0, "Any species") }.Concat(owner.speciesOptions));
                form = Number(-1, 255);
                type = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150 };
                type.Items.Add(new Choice(-1, "Any type"));
                for (int i = 0; i < owner.typeNames.Count; i++) type.Items.Add(new Choice(i, owner.TypeName(i)));
                Pair(editor, ref row, "Zone ID", zone, "", zoneName);
                Wide(editor, ref row, "Species", species);
                Pair(editor, ref row, "Form (-1 = any)", form, "Type", type);

                Section(editor, "Player tile bounds (optional, inclusive; blank is unrestricted)", ref row);
                minX = new TextBox { Width = 90 }; maxX = new TextBox { Width = 90 };
                minY = new TextBox { Width = 90 }; maxY = new TextBox { Width = 90 };
                Pair(editor, ref row, "Min X", minX, "Max X", maxX);
                Pair(editor, ref row, "Min Y", minY, "Max Y", maxY);
                var boundsNote = new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "Matrix tile coordinates from raw NPC X/Y fields; X increases right, Y increases down." };
                Wide(editor, ref row, "", boundsNote);

                Section(editor, "Optional conditions", ref row);
                hp = Options(FollowerRules.HpConditions);
                friendship = Options(FollowerRules.FriendshipConditions);
                status = Options(FollowerRules.StatusConditions);
                facing = Options(FollowerRules.FacingConditions);
                Pair(editor, ref row, "HP", hp, "Friendship", friendship);
                Pair(editor, ref row, "Status", status, "Direction", facing);
                if (IsDialogue)
                {
                    chance = Number(1, 100);
                    Pair(editor, ref row, "Trigger chance (%)", chance, "", new Label());
                    before = Options(FollowerRules.Animations);
                    after = Options(FollowerRules.Animations);
                    Pair(editor, ref row, "Before dialogue", before, "After dialogue", after);
                }
                text = new TextBox { Multiline = true, Height = 64, Width = 460, MaxLength = 191, ScrollBars = ScrollBars.Vertical, AcceptsReturn = true };
                Wide(editor, ref row, IsDialogue ? "Dialogue text" : "Success message", text);
                var tokens = new Label { AutoSize = true, Text = IsDialogue ? "Tokens: {nickname}, {player}, {location}" : "Tokens: {nickname}, {player}, {location}, {item}" };
                Wide(editor, ref row, "", tokens);
                editor.RowCount = row + 1;

                EventHandler changed = (s, e) => Apply();
                foreach (NumericUpDown n in new[] { zone, form, chance, slot, quantity })
                    if (n != null) n.ValueChanged += changed;
                foreach (ComboBox c in new[] { type, hp, friendship, status, facing, before, after })
                    if (c != null) c.SelectedIndexChanged += changed;
                foreach (TextBox t in new[] { minX, maxX, minY, maxY, text, itemName })
                    if (t != null) t.TextChanged += changed;
                species.ValueChosen += changed;
                if (item != null) item.ValueChosen += (s, e) => ItemChosen();
            }

            void Fill()
            {
                list.BeginUpdate();
                list.Items.Clear();
                for (int i = 0; i < Count; i++) list.Items.Add(i);
                list.EndUpdate();
            }

            /// <summary>A rule card: sprite, primary text, zone and qualifiers, like Pokeweb's rule summaries.</summary>
            void DrawCard(object sender, DrawItemEventArgs e)
            {
                e.DrawBackground();
                if (e.Index < 0 || e.Index >= Count) return;
                var rule = Rule(e.Index);
                var box = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 3, 40, 40);
                using (var back = new SolidBrush(PreviewBack)) e.Graphics.FillRectangle(back, box);
                Bitmap image = owner.Thumbnail(owner.PreviewAppearance(rule.Species, rule.Form));
                if (image != null) DrawPixelated(e.Graphics, image, box);
                else TextRenderer.DrawText(e.Graphics, rule.Species == null ? "Any" : "?", e.Font, box, Color.Gainsboro, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                var line = new Rectangle(box.Right + 8, e.Bounds.Y + 4, e.Bounds.Width - box.Width - 16, 18);
                TextRenderer.DrawText(e.Graphics, (e.Index + 1) + ". " + Primary(e.Index), e.Font, line, e.ForeColor, TextFormatFlags.EndEllipsis);
                line.Y += 20;
                TextRenderer.DrawText(e.Graphics, owner.ZoneLabel(rule.Zone) + " · " + owner.RuleQualifier(rule), e.Font, line, e.ForeColor, TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            }

            static string Bound(int? value) => value == null ? "" : value.ToString();

            void Show()
            {
                int index = list.SelectedIndex;
                bool any = index >= 0 && index < Count;
                editor.Visible = any;
                empty.Visible = !any;
                remove.Enabled = any;
                up.Enabled = any && index > 0;
                down.Enabled = any && index < Count - 1;
                if (!any) return;

                loading = true;
                var rule = Rule(index);
                ShowCaption(index);
                zone.Value = rule.Zone;
                zoneName.Text = owner.ZoneLabel(rule.Zone);
                species.Value = rule.Species ?? 0;
                form.Value = rule.Form ?? -1;
                SelectValue(type, rule.Type ?? -1);
                minX.Text = Bound(rule.MinX); maxX.Text = Bound(rule.MaxX);
                minY.Text = Bound(rule.MinY); maxY.Text = Bound(rule.MaxY);
                SelectValue(hp, rule.Hp ?? 0);
                SelectValue(friendship, rule.Friendship ?? 0);
                SelectValue(status, rule.Status ?? 0);
                SelectValue(facing, rule.Facing ?? 0);
                if (IsDialogue)
                {
                    var d = dialogues[index];
                    chance.Value = Math.Max(1, Math.Min(100, d.Chance));
                    SelectValue(before, d.BeforeAnimation ?? 0);
                    SelectValue(after, d.AfterAnimation ?? 0);
                    text.Text = d.Text.Replace("\n", Environment.NewLine);
                }
                else
                {
                    var g = gifts[index];
                    slot.Value = Math.Max(0, Math.Min(9, g.Slot));
                    item.Value = g.ItemId;
                    quantity.Value = Math.Max(1, Math.Min(99, g.Quantity));
                    itemName.Text = g.ItemName;
                    text.Text = g.Text.Replace("\n", Environment.NewLine);
                }
                loading = false;
            }

            void ShowCaption(int index)
            {
                var rule = Rule(index);
                sprite.Image = owner.Thumbnail(owner.PreviewAppearance(rule.Species, rule.Form));
                sprite.Fallback = rule.Species == null ? "Any" : "?";
                spriteCaption.Text = (IsDialogue ? "Dialogue rule " : "Gift rule ") + (index + 1) + " · " +
                    (rule.Species == null ? "Any Pokémon" : owner.SpeciesName(rule.Species.Value) + " #" + rule.Species) + " · " + owner.ZoneLabel(rule.Zone);
            }

            /// <summary>Copy the fields into the selected rule.</summary>
            void Apply()
            {
                int index = list.SelectedIndex;
                if (loading || index < 0 || index >= Count) return;
                var rule = Rule(index);
                rule.Zone = (int)zone.Value;
                zoneName.Text = owner.ZoneLabel(rule.Zone);
                rule.Species = species.Value > 0 ? species.Value : null;
                rule.Form = form.Value >= 0 ? (int?)form.Value : null;
                rule.Type = ValueOf(type) >= 0 ? (int?)ValueOf(type) : null;
                string bad = null;
                rule.MinX = ParseBound(minX, rule.MinX, "Min X", ref bad);
                rule.MaxX = ParseBound(maxX, rule.MaxX, "Max X", ref bad);
                rule.MinY = ParseBound(minY, rule.MinY, "Min Y", ref bad);
                rule.MaxY = ParseBound(maxY, rule.MaxY, "Max Y", ref bad);
                rule.Hp = Optional(hp);
                rule.Friendship = Optional(friendship);
                rule.Status = Optional(status);
                rule.Facing = Optional(facing);
                if (IsDialogue)
                {
                    var d = dialogues[index];
                    d.Chance = (int)chance.Value;
                    d.BeforeAnimation = Optional(before);
                    d.AfterAnimation = Optional(after);
                    d.Text = text.Text.Replace("\r\n", "\n");
                }
                else
                {
                    var g = gifts[index];
                    g.Slot = (int)slot.Value;
                    g.ItemId = item.Value ?? g.ItemId;
                    g.Quantity = (int)quantity.Value;
                    g.ItemName = itemName.Text;
                    g.Text = text.Text.Replace("\r\n", "\n");
                }
                MarkDirty(bad);
                ShowCaption(index);
                list.Invalidate(list.GetItemRectangle(index));
            }

            static int? Optional(ComboBox combo) => ValueOf(combo) > 0 ? (int?)ValueOf(combo) : null;

            static int? ParseBound(TextBox box, int? previous, string name, ref string bad)
            {
                string value = box.Text.Trim();
                if (value.Length == 0) return null;
                if (int.TryParse(value, out int parsed) && parsed >= -32768 && parsed <= 32767) return parsed;
                bad = name + " must be a whole number from -32768 to 32767.";
                return previous;
            }

            /// <summary>Pokeweb fills the dialogue name when it is blank or a placeholder; also when it was the old item's name.</summary>
            void ItemChosen()
            {
                int index = list.SelectedIndex;
                if (loading || index < 0) return;
                var g = gifts[index];
                string current = itemName.Text.Trim(), previous = owner.ItemName(g.ItemId);
                g.ItemId = item.Value ?? g.ItemId;
                if (current.Length == 0 || current == "Item" || System.Text.RegularExpressions.Regex.IsMatch(current, "^Item \\d+$") || current == previous)
                    itemName.Text = owner.ItemName(g.ItemId);
                Apply();
            }

            void MarkDirty(string problem = null)
            {
                Dirty = true;
                Page.Text = (IsDialogue ? "Dialogue" : "Gifts") + " *";
                statusLabel.ForeColor = problem != null ? Color.IndianRed : Color.Empty;
                statusLabel.Text = problem ?? "Unsaved changes.";
                UpdateSize();
            }

            /// <summary>The archive's size against the runtime's 4 KiB buffer, or why it can't be encoded yet.</summary>
            void UpdateSize()
            {
                try
                {
                    int bytes = IsDialogue ? FollowerRules.EncodeDialogues(dialogues).Length : FollowerRules.EncodeGifts(gifts).Length;
                    sizeLabel.ForeColor = Color.Empty;
                    sizeLabel.Text = "Archive size: " + bytes.ToString("N0") + " / " + FollowerRules.MaxBytes.ToString("N0") + " bytes";
                }
                catch (ArgumentException ex)
                {
                    sizeLabel.ForeColor = Color.IndianRed;
                    sizeLabel.Text = ex.Message;
                }
            }

            void Add()
            {
                if (IsDialogue) dialogues.Add(new FollowerDialogueRule { Text = "{nickname} is looking around." });
                else gifts.Add(new FollowerGiftRule { ItemId = 1, ItemName = owner.ItemName(1), Text = "{nickname} found a {item}!" });
                Fill();
                list.SelectedIndex = Count - 1;
                MarkDirty();
            }

            void Remove()
            {
                int index = list.SelectedIndex;
                if (index < 0) return;
                if (IsDialogue) dialogues.RemoveAt(index); else gifts.RemoveAt(index);
                Fill();
                if (Count > 0) list.SelectedIndex = Math.Min(index, Count - 1); else Show();
                MarkDirty();
            }

            void Move(int step)
            {
                int index = list.SelectedIndex, target = index + step;
                if (index < 0 || target < 0 || target >= Count) return;
                if (IsDialogue) { var r = dialogues[index]; dialogues[index] = dialogues[target]; dialogues[target] = r; }
                else { var r = gifts[index]; gifts[index] = gifts[target]; gifts[target] = r; }
                Fill();
                list.SelectedIndex = target;
                MarkDirty();
            }

            /// <summary>Encode the draft into the ROM. False when a rule is invalid; the reason is shown.</summary>
            public bool Save()
            {
                try
                {
                    if (IsDialogue) owner.data.SaveDialogues(dialogues); else owner.data.SaveGifts(gifts);
                }
                catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException)
                {
                    statusLabel.ForeColor = Color.IndianRed;
                    statusLabel.Text = ex.Message;
                    MessageBox.Show(ex.Message, "Following Pokémon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
                Dirty = false;
                Page.Text = IsDialogue ? "Dialogue" : "Gifts";
                statusLabel.ForeColor = Color.Empty;
                statusLabel.Text = "Saved " + Count + " " + (IsDialogue ? "dialogue" : "gift") + (Count == 1 ? " rule" : " rules") +
                    " into the ROM. Save the ROM from the main window to keep them.";
                UpdateSize();
                return true;
            }

            void Revert()
            {
                if (!Dirty) return;
                if (MessageBox.Show("Discard the unsaved changes to " + Noun + "?", "Following Pokémon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                if (IsDialogue) { dialogues.Clear(); dialogues.AddRange(owner.data.Dialogues.Select(r => r.Copy())); }
                else { gifts.Clear(); gifts.AddRange(owner.data.Gifts.Select(r => r.Copy())); }
                Dirty = false;
                Page.Text = IsDialogue ? "Dialogue" : "Gifts";
                statusLabel.Text = "";
                Fill();
                if (Count > 0) list.SelectedIndex = 0; else Show();
                UpdateSize();
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
                RuleView view = dialogue ? dialogueView : giftView;
                if (e.Index < 0 || (int)list.Items[e.Index] >= view.Count) return;
                int index = (int)list.Items[e.Index];
                var rule = view.Rule(index);
                var text = new Rectangle(e.Bounds.X + 6, e.Bounds.Y + 4, e.Bounds.Width - 12, 18);
                TextRenderer.DrawText(e.Graphics, view.Primary(index), e.Font, text, e.ForeColor, TextFormatFlags.EndEllipsis);
                text.Y += 20;
                TextRenderer.DrawText(e.Graphics, ZoneLabel(rule.Zone) + " · " + RuleQualifier(rule) + " · Rule " + (index + 1), e.Font, text, e.ForeColor, TextFormatFlags.EndEllipsis);
                e.DrawFocusRectangle();
            };
            list.DoubleClick += (s, e) =>
            {
                if (list.SelectedIndex < 0) return;
                RuleView view = dialogue ? dialogueView : giftView;
                tabs.SelectedTab = view.Page;
                view.Select((int)list.SelectedItem);
            };
            return list;
        }

        void RenderOverview()
        {
            if (overviewSpecies.Value == null) return;
            int species = overviewSpecies.Value.Value;
            HashSet<int> types = SpeciesTypes(species);
            overviewSprite.Image = Thumbnail(PreviewAppearance(species, 0));
            overviewTitle.Text = SpeciesName(species) + " #" + species;
            overviewTypes.Text = types.Count > 0 ? string.Join(" / ", types.Select(TypeName)) + " · Double-click a rule to open it." : "Type data unavailable";
            foreach (var pair in new[] { (overviewDialogues, dialogueView), (overviewGifts, giftView) })
            {
                pair.Item1.BeginUpdate();
                pair.Item1.Items.Clear();
                for (int i = 0; i < pair.Item2.Count; i++)
                    if (pair.Item2.Rule(i).AppliesTo(species, types)) pair.Item1.Items.Add(i);
                pair.Item1.EndUpdate();
            }
        }

        // ---------------------------------------------------------- Appearances

        ListBox appearanceList;
        TextBox appearanceSearch;
        NumericUpDown followDistance, height, shiftAmount;
        ComboBox shiftScope;
        CheckBox fallbackOnly, walking;
        Label appearanceDetail, appearanceStatus, distanceNote;
        readonly PixelBox[] previews = new PixelBox[4];
        Bitmap[,] frames;
        Timer animation;
        int tick;
        bool showingAppearance, appearancesDirty;
        List<FollowerAppearance> appearanceDraft = new List<FollowerAppearance>();
        byte[] landDraft;
        Dictionary<string, PendingSprite> pendingSprites = new Dictionary<string, PendingSprite>();
        List<FollowerAppearance> shownAppearances = new List<FollowerAppearance>();

        void ResetAppearanceDraft()
        {
            appearanceDraft = (data.Registry?.Entries ?? new List<FollowerAppearance>()).Select(e => e.Copy()).ToList();
            landDraft = data.Positioning == null ? null : (byte[])data.Positioning.Land.Clone();
            pendingSprites = new Dictionary<string, PendingSprite>();
            appearancesDirty = false;
            appearanceTab.Text = "Appearances";
        }

        void BuildAppearances()
        {
            ResetAppearanceDraft();
            appearanceTab.Padding = new Padding(8);
            var split = Split(appearanceTab, 400);

            var filters = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 64, WrapContents = true };
            int fallbacks = appearanceDraft.Count(e => e.Placeholder);
            var catalog = new Label { AutoSize = true, Text = "Catalog: " + appearanceDraft.Count + " appearances · " + fallbacks + " fallback appearances", Padding = new Padding(0, 4, 0, 4) };
            filters.Controls.Add(catalog);
            filters.SetFlowBreak(catalog, true);
            filters.Controls.Add(new Label { AutoSize = true, Text = "Search", Padding = new Padding(0, 6, 0, 0) });
            appearanceSearch = new TextBox { Width = 170 };
            fallbackOnly = new CheckBox { Text = "Fallback artwork only", AutoSize = true, Padding = new Padding(8, 2, 0, 0) };
            filters.Controls.Add(appearanceSearch);
            filters.Controls.Add(fallbackOnly);
            appearanceList = new ListBox { Dock = DockStyle.Fill, DrawMode = DrawMode.OwnerDrawFixed, ItemHeight = 40, IntegralHeight = false };
            appearanceList.DrawItem += DrawAppearance;
            appearanceList.SelectedIndexChanged += (s, e) => ShowAppearance();
            appearanceSearch.TextChanged += (s, e) => FillAppearances();
            fallbackOnly.CheckedChanged += (s, e) => FillAppearances();
            var searchHint = new Label { Dock = DockStyle.Bottom, Height = 20, ForeColor = SystemColors.GrayText, Text = "Search by species name or number, for example Eevee or 133." };
            split.Panel1.Controls.Add(appearanceList);
            split.Panel1.Controls.Add(filters);
            split.Panel1.Controls.Add(searchHint);

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
            followDistance = Number(0, FollowingData.MaxFollowDistance);
            height = Number(-128, 127);
            tuning.Controls.Add(new Label { Text = "Follow distance", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 0);
            tuning.Controls.Add(followDistance, 1, 0);
            distanceNote = new Label { Text = "World units behind the player, 0–12. 64-pixel followers add their own bonus.", AutoSize = true, Padding = new Padding(6, 6, 0, 0) };
            tuning.Controls.Add(distanceNote, 2, 0);
            tuning.Controls.Add(new Label { Text = "Height", AutoSize = true, Padding = new Padding(0, 6, 6, 0) }, 0, 1);
            tuning.Controls.Add(height, 1, 1);
            tuning.Controls.Add(new Label { Text = "Sprite pixels; positive raises it. The shadow stays on the ground.", AutoSize = true, Padding = new Padding(6, 6, 0, 0) }, 2, 1);
            right.Controls.Add(tuning);
            followDistance.ValueChanged += (s, e) => AppearanceEdited();
            height.ValueChanged += (s, e) => AppearanceEdited();

            var shift = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 8, 0, 0) };
            shiftAmount = Number(-32, 32);
            shiftAmount.Width = 60;
            shiftScope = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 230 };
            shiftScope.Items.AddRange(new object[] { "every appearance", "the appearances listed on the left" });
            shiftScope.SelectedIndex = 0;
            var shiftButton = new Button { Text = "Shift heights", AutoSize = true };
            shiftButton.Click += (s, e) => ShiftHeights();
            shift.Controls.Add(new Label { Text = "Shift height by", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            shift.Controls.Add(shiftAmount);
            shift.Controls.Add(new Label { Text = "pixels for", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            shift.Controls.Add(shiftScope);
            shift.Controls.Add(shiftButton);
            right.Controls.Add(shift);
            right.Controls.Add(new Label { AutoSize = true, ForeColor = SystemColors.GrayText, Text = "Positive raises, negative lowers. Heights stop at -128 and 127." });

            right.Controls.Add(new Label
            {
                AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 12, 0, 4),
                Text = "PNG layout: rows up, down, left, right; columns idle and step. Use 64×128 for 32-pixel frames or 128×256 for 64-pixel frames. Maximum 15 opaque DS colors plus transparency."
            });
            var actions = new FlowLayoutPanel { AutoSize = true };
            var replace = new Button { Text = "Replace selected PNG", AutoSize = true };
            var export = new Button { Text = "Export selected PNG", AutoSize = true };
            var exportPack = new Button { Text = "Export asset pack", AutoSize = true };
            var importPack = new Button { Text = "Import asset pack", AutoSize = true };
            replace.Click += (s, e) => ReplacePng();
            export.Click += (s, e) => ExportPng();
            exportPack.Click += (s, e) => ExportPack();
            importPack.Click += (s, e) => ImportPack();
            actions.Controls.AddRange(new Control[] { replace, export, exportPack, importPack });
            right.Controls.Add(actions);

            var saving = new FlowLayoutPanel { AutoSize = true, Margin = new Padding(0, 12, 0, 0) };
            var save = new Button { Text = "Save appearances", AutoSize = true };
            var revert = new Button { Text = "Discard changes", AutoSize = true };
            save.Click += (s, e) => SaveAppearances();
            revert.Click += (s, e) => RevertAppearances();
            saving.Controls.AddRange(new Control[] { save, revert });
            right.Controls.Add(saving);
            appearanceStatus = new Label { AutoSize = true, MaximumSize = new Size(620, 0), Padding = new Padding(0, 4, 0, 0) };
            right.Controls.Add(appearanceStatus);

            animation = new Timer { Interval = 1000 / 12 };
            animation.Tick += (s, e) => { tick += 5; DrawPreviews(); };
            FillAppearances();
            animation.Start();
        }

        void BuildCredits()
        {
            creditsTab.Padding = new Padding(12);
            var credits = new TextBox
            {
                Dock = DockStyle.Fill, Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, ScrollBars = ScrollBars.Vertical,
                Text = ("Sprite credits\n\n" + SpriteCredits + "\n\n" + string.Join("\n\n", OverworldCredits.Select(c => c[0] + "\n" + c[1]))).Replace("\n", Environment.NewLine)
            };
            creditsTab.Controls.Add(credits);
        }

        /// <summary>Species name anywhere in the text, or an exact species number ("133" or "#133").</summary>
        bool MatchesSearch(FollowerAppearance a, string query)
        {
            if (query.Length == 0) return true;
            string digits = query.TrimStart('#');
            if (digits.Length > 0 && digits.All(char.IsDigit)) return int.TryParse(digits, out int number) && a.Species == number;
            return SpeciesName(a.Species).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void FillAppearances()
        {
            string query = appearanceSearch.Text.Trim();
            string previous = SelectedAppearance?.Key;
            shownAppearances = appearanceDraft.Where(e => MatchesSearch(e, query) && (!fallbackOnly.Checked || e.Placeholder)).ToList();
            appearanceList.BeginUpdate();
            appearanceList.Items.Clear();
            for (int i = 0; i < shownAppearances.Count; i++) appearanceList.Items.Add(i);
            appearanceList.EndUpdate();
            int keep = previous == null ? -1 : shownAppearances.FindIndex(e => e.Key == previous);
            if (shownAppearances.Count > 0) appearanceList.SelectedIndex = keep >= 0 ? keep : 0;
            else ShowAppearance();
        }

        string AppearanceLabel(FollowerAppearance a) =>
            SpeciesName(a.Species) + " #" + a.Species + " · form " + a.Form + " · " + FollowerAppearance.GenderNames[Math.Min(2, a.Gender)] +
            " · " + (a.Shiny ? "shiny" : "normal") + (a.Placeholder && !pendingSprites.ContainsKey(a.Key) ? " · placeholder" : "") +
            (a.OffsetY != 0 ? " · height " + a.OffsetY : "") + (pendingSprites.ContainsKey(a.Key) ? " · new sprite" : "");

        /// <summary>The draft's sprite: a pending replacement, or the ROM's.</summary>
        byte[] DraftSprite(FollowerAppearance a) => data.Sprite(a, pendingSprites);
        bool DraftAsymmetric(FollowerAppearance a) => pendingSprites.TryGetValue(a.Key, out PendingSprite p) ? p.Asymmetric : a.Asymmetric;

        Bitmap DraftThumbnail(FollowerAppearance a)
        {
            if (!pendingSprites.ContainsKey(a.Key)) return Thumbnail(a);
            string key = "pending:" + a.Key;
            if (!thumbnails.TryGetValue(key, out Bitmap bitmap))
            {
                try { bitmap = FollowerSprites.Frame(DraftSprite(a), DraftAsymmetric(a), 1, 0, false); }
                catch { bitmap = null; }
                thumbnails[key] = bitmap;
            }
            return bitmap;
        }

        void DrawAppearance(object sender, DrawItemEventArgs e)
        {
            e.DrawBackground();
            if (e.Index < 0 || e.Index >= shownAppearances.Count) return;
            var a = shownAppearances[e.Index];
            var box = new Rectangle(e.Bounds.X + 4, e.Bounds.Y + 2, 36, 36);
            using (var back = new SolidBrush(PreviewBack)) e.Graphics.FillRectangle(back, box);
            Bitmap sprite = DraftThumbnail(a);
            if (sprite != null) DrawPixelated(e.Graphics, sprite, box);
            var text = new Rectangle(box.Right + 8, e.Bounds.Y, e.Bounds.Width - box.Width - 16, e.Bounds.Height);
            TextRenderer.DrawText(e.Graphics, AppearanceLabel(a), e.Font, text, e.ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            e.DrawFocusRectangle();
        }

        FollowerAppearance SelectedAppearance =>
            appearanceList != null && appearanceList.SelectedIndex >= 0 && appearanceList.SelectedIndex < shownAppearances.Count ? shownAppearances[appearanceList.SelectedIndex] : null;

        void DisposeFrames()
        {
            if (frames == null) return;
            foreach (Bitmap b in frames) b?.Dispose();
            frames = null;
        }

        /// <summary>The runtime's follow distance for a draft row: the largest of its four gaps, or -1 without a row.</summary>
        int DraftDistance(FollowerAppearance a)
        {
            int row = a.DescriptorRow - FollowingData.StockRows;
            if (landDraft == null || row < 0 || row * 12 + 4 > landDraft.Length) return -1;
            int gap = 0;
            for (int i = 0; i < 4; i++) gap = Math.Max(gap, landDraft[row * 12 + i]);
            return gap;
        }

        void ShowAppearance()
        {
            DisposeFrames();
            var a = SelectedAppearance;
            showingAppearance = true;
            if (a == null)
            {
                appearanceDetail.Text = "No matching appearances.";
                foreach (var p in previews) p.Image = null;
                showingAppearance = false;
                return;
            }
            byte[] resource = DraftSprite(a);
            bool asymmetric = DraftAsymmetric(a);
            frames = new Bitmap[4, 2];
            for (int d = 0; d < 4; d++)
                for (int pose = 0; pose < 2; pose++)
                {
                    try { frames[d, pose] = FollowerSprites.Frame(resource, asymmetric, d, pose == 0 ? 0 : (d == 1 ? 5 : 10), true); }
                    catch { frames[d, pose] = null; }
                }
            int distance = DraftDistance(a);
            followDistance.Enabled = distance >= 0;
            followDistance.Value = Math.Max(0, Math.Min(FollowingData.MaxFollowDistance, distance));
            distanceNote.Text = distance >= 0 ? "World units behind the player, 0–12. 64-pixel followers add their own bonus."
                : "This ROM has no positioning row for it; the runtime uses its default distance.";
            height.Value = Math.Max(-128, Math.Min(127, a.OffsetY));
            int size = frames[0, 0]?.Width ?? a.Size;
            appearanceDetail.Text = AppearanceLabel(a) + "\n" +
                (pendingSprites.ContainsKey(a.Key) ? "New sprite, saved with the next appearance save" : a.Placeholder ? "Fallback artwork (shared with another appearance)" : "Own artwork") +
                " · " + size + "-pixel frames · " + (asymmetric ? "8 frames" : "6 frames, right mirrored from left") +
                " · resource " + a.ResourceId + " · descriptor row " + a.DescriptorRow;
            DrawPreviews();
            showingAppearance = false;
        }

        void MarkAppearancesDirty(string message)
        {
            appearancesDirty = true;
            appearanceTab.Text = "Appearances *";
            appearanceStatus.ForeColor = Color.Empty;
            appearanceStatus.Text = message;
        }

        /// <summary>Follow distance fills all four gaps of the row, the one value the runtime uses.</summary>
        void AppearanceEdited()
        {
            var a = SelectedAppearance;
            if (showingAppearance || a == null) return;
            a.OffsetY = (int)height.Value;
            int row = a.DescriptorRow - FollowingData.StockRows;
            if (followDistance.Enabled && landDraft != null && row >= 0 && row * 12 + 4 <= landDraft.Length)
                for (int i = 0; i < 4; i++) landDraft[row * 12 + i] = (byte)followDistance.Value;
            MarkAppearancesDirty("Unsaved changes.");
            appearanceList.Invalidate(appearanceList.GetItemRectangle(appearanceList.SelectedIndex));
        }

        void ShiftHeights()
        {
            int amount = (int)shiftAmount.Value;
            if (amount == 0) return;
            List<FollowerAppearance> scope = shiftScope.SelectedIndex == 0 ? appearanceDraft : shownAppearances;
            if (MessageBox.Show("Shift the height of " + scope.Count + " appearances by " + (amount > 0 ? "+" : "") + amount + " pixels?", "Following Pokémon",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            int clamped = 0;
            foreach (FollowerAppearance a in scope)
            {
                int target = a.OffsetY + amount, value = Math.Max(-128, Math.Min(127, target));
                if (value != target) clamped++;
                a.OffsetY = value;
            }
            MarkAppearancesDirty("Shifted " + scope.Count + " heights by " + (amount > 0 ? "+" : "") + amount +
                (clamped > 0 ? "; " + clamped + " stopped at the -128 to 127 limit" : "") + ". Unsaved.");
            appearanceList.Invalidate();
            ShowAppearance();
        }

        void ReplacePng()
        {
            var a = SelectedAppearance;
            if (a == null) return;
            var open = new OpenFileDialog { Filter = "PNG image|*.png", Title = "Replace " + AppearanceLabel(a) };
            if (open.ShowDialog() != DialogResult.OK) return;
            try
            {
                byte[] btx;
                using (var image = new Bitmap(open.FileName)) btx = FollowerSprites.EncodeSheet(image);
                pendingSprites[a.Key] = new PendingSprite { Btx = btx, Asymmetric = true, Placeholder = false };
                thumbnails.Remove("pending:" + a.Key);
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is ArgumentException || ex is OutOfMemoryException || ex is IOException)
            {
                MessageBox.Show(ex is OutOfMemoryException ? "The file isn't a readable PNG." : ex.Message, "Replace PNG", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            MarkAppearancesDirty("Replaced the sprite for " + AppearanceLabel(a) + ". Unsaved.");
            appearanceList.Invalidate();
            ShowAppearance();
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

        /// <summary>Pokeweb's asset pack: manifest.json plus one BTX sprite per appearance.</summary>
        void ExportPack()
        {
            var save = new SaveFileDialog { Filter = "Zip archive|*.zip", FileName = "following-assets.zip" };
            if (save.ShowDialog() != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            try
            {
                var manifest = new System.Text.StringBuilder("{\n  \"schemaVersion\": 1,\n  \"entries\": [");
                using (var zip = new System.IO.Compression.ZipArchive(System.IO.File.Create(save.FileName), System.IO.Compression.ZipArchiveMode.Create))
                {
                    for (int i = 0; i < appearanceDraft.Count; i++)
                    {
                        var a = appearanceDraft[i];
                        byte[] sprite = DraftSprite(a);
                        if (sprite == null) continue;
                        string path = "assets/" + a.Key.Replace(':', '-') + ".btx";
                        using (var stream = zip.CreateEntry(path, System.IO.Compression.CompressionLevel.Optimal).Open()) stream.Write(sprite, 0, sprite.Length);
                        bool pending = pendingSprites.ContainsKey(a.Key);
                        manifest.Append(i == 0 ? "\n" : ",\n").Append("    { \"key\": { \"species\": ").Append(a.Species).Append(", \"form\": ").Append(a.Form)
                            .Append(", \"gender\": ").Append(a.Gender).Append(", \"shiny\": ").Append(a.Shiny ? "true" : "false")
                            .Append(" }, \"path\": \"").Append(path).Append("\", \"profile\": \"").Append(DraftAsymmetric(a) ? "pokemon-asymmetric" : "pokemon-mirrored")
                            .Append("\", \"source\": \"").Append(pending ? "png" : "stock").Append("\", \"placeholder\": ")
                            .Append(!pending && a.Placeholder ? "true" : "false").Append(" }");
                    }
                    manifest.Append("\n  ]\n}\n");
                    byte[] json = System.Text.Encoding.UTF8.GetBytes(manifest.ToString());
                    using (var stream = zip.CreateEntry("manifest.json").Open()) stream.Write(json, 0, json.Length);
                }
                appearanceStatus.ForeColor = Color.Empty;
                appearanceStatus.Text = "Exported " + appearanceDraft.Count + " appearances to " + System.IO.Path.GetFileName(save.FileName) + ".";
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                MessageBox.Show(ex.Message, "Export asset pack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            finally { Cursor = Cursors.Default; }
        }

        /// <summary>Read a Pokeweb asset pack; each listed appearance gets its sprite as a pending replacement.</summary>
        void ImportPack()
        {
            var open = new OpenFileDialog { Filter = "Zip archive|*.zip", Title = "Import asset pack" };
            if (open.ShowDialog() != DialogResult.OK) return;
            Cursor = Cursors.WaitCursor;
            int imported = 0, unknown = 0;
            var problems = new List<string>();
            try
            {
                if (new System.IO.FileInfo(open.FileName).Length > 64 * 1024 * 1024) throw new InvalidDataException("The asset pack is larger than 64 MiB.");
                var byKey = appearanceDraft.ToDictionary(e => e.Key);
                var staged = new Dictionary<string, PendingSprite>();
                using (var zip = System.IO.Compression.ZipFile.OpenRead(open.FileName))
                {
                    var manifestEntry = zip.GetEntry("manifest.json") ?? throw new InvalidDataException("The asset pack has no manifest.json.");
                    System.Text.Json.JsonDocument manifest;
                    using (var stream = manifestEntry.Open()) manifest = System.Text.Json.JsonDocument.Parse(stream);
                    using (manifest)
                    {
                        var root = manifest.RootElement;
                        if (!root.TryGetProperty("schemaVersion", out var version) || version.GetInt32() != 1 || !root.TryGetProperty("entries", out var entries) ||
                            entries.ValueKind != System.Text.Json.JsonValueKind.Array || entries.GetArrayLength() > 6144)
                            throw new InvalidDataException("Unsupported asset pack manifest.");
                        foreach (var item in entries.EnumerateArray())
                        {
                            var k = item.GetProperty("key");
                            string key = k.GetProperty("species").GetInt32() + ":" + k.GetProperty("form").GetInt32() + ":" + k.GetProperty("gender").GetInt32() + ":" +
                                (k.GetProperty("shiny").GetBoolean() ? 1 : 0);
                            if (!byKey.ContainsKey(key)) { unknown++; continue; }
                            string path = item.GetProperty("path").GetString();
                            var entry = zip.GetEntry(path);
                            if (entry == null || entry.Length > 2 * 1024 * 1024) { problems.Add(key + ": sprite file missing or too large"); continue; }
                            var bytes = new byte[entry.Length];
                            using (var stream = entry.Open())
                                for (int read = 0; read < bytes.Length;) { int n = stream.Read(bytes, read, bytes.Length - read); if (n <= 0) break; read += n; }
                            bool asymmetric = item.TryGetProperty("profile", out var profile) ? profile.GetString() == "pokemon-asymmetric" : FollowerSprites.IsAsymmetric(bytes);
                            try { FollowerSprites.Validate(bytes, asymmetric, out _); }
                            catch (InvalidDataException ex) { problems.Add(key + ": " + ex.Message); continue; }
                            bool placeholder = item.TryGetProperty("placeholder", out var flag) && flag.ValueKind == System.Text.Json.JsonValueKind.True;
                            staged[key] = new PendingSprite { Btx = bytes, Asymmetric = asymmetric, Placeholder = placeholder };
                            imported++;
                        }
                    }
                }
                foreach (var pair in staged) { pendingSprites[pair.Key] = pair.Value; thumbnails.Remove("pending:" + pair.Key); }
            }
            catch (Exception ex) when (ex is InvalidDataException || ex is IOException || ex is System.Text.Json.JsonException || ex is KeyNotFoundException || ex is InvalidOperationException)
            {
                Cursor = Cursors.Default;
                MessageBox.Show("The asset pack could not be read: " + ex.Message, "Import asset pack", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Cursor = Cursors.Default;
            if (imported > 0) MarkAppearancesDirty("Imported " + imported + " sprites. Unsaved.");
            if (unknown > 0 || problems.Count > 0)
                MessageBox.Show("Imported " + imported + " sprites." + (unknown > 0 ? "\n" + unknown + " appearances aren't in this ROM's catalog and were skipped." : "") +
                    (problems.Count > 0 ? "\n" + problems.Count + " were refused:\n" + string.Join("\n", problems.Take(10)) + (problems.Count > 10 ? "\n…" : "") : ""),
                    "Import asset pack", MessageBoxButtons.OK, MessageBoxIcon.Information);
            appearanceList.Invalidate();
            ShowAppearance();
        }

        public bool SaveAppearances()
        {
            if (!appearancesDirty) return true;
            Cursor = Cursors.WaitCursor;
            try { data.SaveAppearances(appearanceDraft, landDraft, pendingSprites); }
            catch (Exception ex) when (ex is ArgumentException || ex is InvalidOperationException || ex is InvalidDataException)
            {
                Cursor = Cursors.Default;
                appearanceStatus.ForeColor = Color.IndianRed;
                appearanceStatus.Text = ex.Message;
                MessageBox.Show(ex.Message, "Following Pokémon", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }
            Cursor = Cursors.Default;
            int sprites = pendingSprites.Count;
            foreach (Bitmap b in thumbnails.Values) b?.Dispose();
            thumbnails.Clear();
            ResetAppearanceDraft();
            FillAppearances();
            dialogueView.Refresh();
            giftView.Refresh();
            appearanceStatus.ForeColor = Color.Empty;
            appearanceStatus.Text = "Saved appearances" + (sprites > 0 ? " and " + sprites + " sprites" : "") + " into the ROM. Save the ROM from the main window to keep them.";
            return true;
        }

        void RevertAppearances()
        {
            if (!appearancesDirty) return;
            if (MessageBox.Show("Discard the unsaved changes to appearances?", "Following Pokémon", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            ResetAppearanceDraft();
            appearanceStatus.Text = "";
            FillAppearances();
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
    }
}
