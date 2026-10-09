using NewEditor.Data;
using NewEditor.Data.NARCTypes;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    public partial class OverworldEditor : Form
    {
        TextNARC textNARC => MainEditor.textNarc;
        ZoneDataNARC zoneNARC => MainEditor.zoneDataNarc;
        MapMatrixNARC mapMatrixNarc => MainEditor.mapMatrixNarc;
        OverworldObjectsNARC overworldObjectNarc => MainEditor.overworldsNarc;

        OverworldMapView mapView;
        PictureBox spritePreview;
        bool syncingNpc;
        bool syncingProxy;
        ComboBox proxyInteractBox;
        ComboBox proxyItemBox;
        CheckBox proxyRailBox;
        NumericUpDown proxyConditionBox;
        NumericUpDown proxySideBox;
        Label proxyHintLabel;
        Label proxyItemLabel;

        public ZoneDataEntry CurrentZone
        {
            get { return zoneIdDropdown.SelectedItem as ZoneDataEntry; }
        }

        public MapMatrixEntry CurrentMatrix
        {
            get
            {
                if (!(zoneIdDropdown.SelectedItem is ZoneDataEntry z)) return null;
                int mx = z.matrix;
                if (mapMatrixNarc == null || mx < 0 || mx >= mapMatrixNarc.matricies.Count) return null;
                return mapMatrixNarc.matricies[mx];
            }
        }

        public OverworldObjectsEntry CurrentObjects
        {
            get
            {
                int id = (int)mapIDNumberBox.Value;
                if (overworldObjectNarc == null || id < 0 || id >= overworldObjectNarc.objects.Count) return null;
                return overworldObjectNarc.objects[id];
            }
        }

        public MapFilesNARC MapFiles
        {
            get { return MainEditor.mapFilesNarc; }
        }

        public int SelectedNpcIndex
        {
            get
            {
                if (CurrentObjects == null || CurrentObjects.NPCs == null || CurrentObjects.NPCs.Count == 0) return -1;
                return (int)npcIDNumberBox.Value;
            }
        }

        public OverworldEditor()
        {
            InitializeComponent();

            zoneIdDropdown.Items.AddRange(zoneNARC.zones.ToArray());
            mapNameDropdown.Items.AddRange(textNARC.textFiles[VersionConstants.ZoneNameTextFileID].text.ToArray());
            setItemDropdown.Items.AddRange(textNARC.textFiles[VersionConstants.ItemNameTextFileID].text.ToArray());
            warpDestMapDropdown.Items.AddRange(zoneNARC.zones.ToArray());

            AttachMapView();
            AttachSpritePreview();
            SetupProxyEditor();
        }

        void SetupProxyEditor()
        {
            if (furnitureTab == null) return;
            furnitureTab.Text = "Proxies";

            label40.Text = "Script:";
            label46.Text = "X:";
            label45.Text = "Height:";
            label44.Text = "Z:";
            label46.Visible = true;
            label45.Visible = true;
            label44.Visible = true;
            furnitureXPosNumberBox.Visible = true;
            furnitureYPosNumberBox.Visible = true;
            furnitureZPosNumberBox.Visible = true;
            furnitureXPosNumberBox.Maximum = 100000;
            furnitureZPosNumberBox.Maximum = 100000;
            furnitureYPosNumberBox.Maximum = 100000;
            furnitureXPosNumberBox.Minimum = -100000;
            furnitureZPosNumberBox.Minimum = -100000;
            furnitureYPosNumberBox.Minimum = -100000;

            proxyHintLabel = new Label
            {
                AutoSize = false,
                Location = new Point(160, 8),
                Size = new Size(390, 44),
                Text = "Interaction spots with no NPC (signs, hidden items). CTRMap calls these proxies. Not 3D props. Ctrl+click the map to place one. Rail-positioned proxies are edited here but not drawn."
            };
            furnitureTab.Controls.Add(proxyHintLabel);

            var condLabel = new Label { AutoSize = true, Location = new Point(160, 58), Text = "Condition:" };
            proxyConditionBox = new NumericUpDown
            {
                Location = new Point(240, 54),
                Size = new Size(70, 22),
                Maximum = 65535,
                Minimum = 0
            };
            var faceLabel = new Label { AutoSize = true, Location = new Point(320, 58), Text = "Facing:" };
            proxyInteractBox = new ComboBox
            {
                Location = new Point(370, 54),
                Size = new Size(110, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            proxyInteractBox.Items.AddRange(OverworldFurniture.InteractNames);
            proxyRailBox = new CheckBox
            {
                AutoSize = true,
                Location = new Point(160, 88),
                Text = "Rail position"
            };
            proxyRailBox.CheckedChanged += (s, e) => UpdateProxyRailLabels();
            var sideLabel = new Label { AutoSize = true, Location = new Point(280, 90), Text = "Side:" };
            proxySideBox = new NumericUpDown
            {
                Location = new Point(320, 86),
                Size = new Size(70, 22),
                Maximum = 32767,
                Minimum = -32768,
                Enabled = false
            };
            furnitureTab.Controls.Add(condLabel);
            furnitureTab.Controls.Add(proxyConditionBox);
            furnitureTab.Controls.Add(faceLabel);
            furnitureTab.Controls.Add(proxyInteractBox);
            furnitureTab.Controls.Add(proxyRailBox);
            furnitureTab.Controls.Add(sideLabel);
            furnitureTab.Controls.Add(proxySideBox);

            proxyItemLabel = new Label { AutoSize = true, Location = new Point(160, 118), Text = "Item:" };
            proxyItemBox = new ComboBox
            {
                Location = new Point(200, 114),
                Size = new Size(220, 22),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            if (textNARC != null && textNARC.textFiles.Count > VersionConstants.ItemNameTextFileID)
                proxyItemBox.Items.AddRange(textNARC.textFiles[VersionConstants.ItemNameTextFileID].text.ToArray());
            furnitureScriptNumberBox.ValueChanged += (s, e) => { if (!syncingProxy) UpdateProxyItemMode(); };
            furnitureTab.Controls.Add(proxyItemLabel);
            furnitureTab.Controls.Add(proxyItemBox);
        }

        void UpdateProxyItemMode()
        {
            if (proxyItemBox == null) return;
            int script = (int)furnitureScriptNumberBox.Value;
            int item = ScriptItemId(script);
            if (script == 0)
            {
                proxyItemBox.Enabled = false;
                proxyItemBox.SelectedIndex = -1;
                proxyItemLabel.Text = "Item:";
                proxyHintLabel.Text = "Script 0 is stored as 0. Apply writes it back unchanged. Condition is left alone.";
            }
            else if (item >= 0)
            {
                proxyItemBox.Enabled = false;
                if (item < proxyItemBox.Items.Count) proxyItemBox.SelectedIndex = item;
                proxyItemLabel.Text = "Item (from script):";
                proxyHintLabel.Text = "Item comes from the script, same as the NPC tab. Condition is not the item.";
            }
            else
            {
                proxyItemBox.Enabled = false;
                proxyItemBox.SelectedIndex = -1;
                proxyItemLabel.Text = "Item:";
                proxyHintLabel.Text = "Script is written only from the Script box. Condition is a raw field; meaning is not confirmed.";
            }
        }

        // Same lookup the NPC tab uses. 7000-7399 is file 1240 (BW2) / 864 (BW).
        // 8000-8399 is tried against the next file for hidden-item scripts such as 8212.
        int ScriptItemId(int script)
        {
            try
            {
                int file = -1, seq = -1;
                if (script >= 7000 && script < 7400) { file = MainEditor.RomType == RomType.BW2 ? 1240 : 864; seq = script - 7000; }
                else if (script >= 8000 && script < 8400) { file = MainEditor.RomType == RomType.BW2 ? 1241 : 865; seq = script - 8000; }
                if (file < 0 || MainEditor.scriptNarc == null) return -1;
                var sf = MainEditor.scriptNarc.scriptFiles[file];
                if (seq < 0 || seq >= sf.sequences.Count) return -1;
                return sf.sequences[seq].commands[1].parameters[1];
            }
            catch { return -1; }
        }

        public string ItemName(int id)
        {
            try
            {
                var names = textNARC.textFiles[VersionConstants.ItemNameTextFileID].text;
                if (id >= 0 && id < names.Count) return names[id];
            }
            catch { }
            return null;
        }

        void UpdateProxyRailLabels()
        {
            bool rail = proxyRailBox != null && proxyRailBox.Checked;
            label46.Text = rail ? "Line:" : "X:";
            label44.Text = rail ? "Front:" : "Z:";
            if (proxySideBox != null) proxySideBox.Enabled = rail;
        }

        void AttachSpritePreview()
        {
            if (npcSpriteIDNumberBox == null) return;
            spritePreview = new PictureBox
            {
                Size = new Size(64, 64),
                SizeMode = PictureBoxSizeMode.Zoom,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Color.FromArgb(18, 20, 24),
                Location = new Point(npcSpriteIDNumberBox.Right + 10, npcSpriteIDNumberBox.Top - 20)
            };
            Control parent = npcSpriteIDNumberBox.Parent ?? this;
            parent.Controls.Add(spritePreview);
            spritePreview.BringToFront();

            var up = new Button
            {
                Text = "▲",
                Size = new Size(22, 20),
                Location = new Point(spritePreview.Right + 2, spritePreview.Top),
                FlatStyle = FlatStyle.Flat
            };
            var down = new Button
            {
                Text = "▼",
                Size = new Size(22, 20),
                Location = new Point(spritePreview.Right + 2, spritePreview.Bottom - 20),
                FlatStyle = FlatStyle.Flat
            };
            up.Click += (s, e) => OverworldSpritePreview.StepFrame(spritePreview, (int)npcSpriteIDNumberBox.Value, -1);
            down.Click += (s, e) => OverworldSpritePreview.StepFrame(spritePreview, (int)npcSpriteIDNumberBox.Value, 1);
            parent.Controls.Add(up);
            parent.Controls.Add(down);
            up.BringToFront();
            down.BringToFront();

            OverworldSpritePreview.Bind(spritePreview, npcSpriteIDNumberBox);
        }

        void AttachMapView()
        {
            mapView = new OverworldMapView();
            mapView.Attach(this);

            const int leftGutter = 896;
            MinimumSize = new Size(leftGutter + 280, Math.Max(Height, 560));
            if (Width < leftGutter + 420) Width = leftGutter + 420;

            var holder = new Panel
            {
                Location = new Point(leftGutter, 8),
                Size = new Size(Math.Max(240, ClientSize.Width - leftGutter - 8), ClientSize.Height - 16),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right
            };
            holder.Controls.Add(mapView);
            Controls.Add(holder);
            holder.BringToFront();

            npcXPositionNumberBox.ValueChanged += NpcCoordBoxChanged;
            npcYPositionNumberBox.ValueChanged += NpcCoordBoxChanged;
            EventHandler redraw = (s, ev) => { if (mapView != null) mapView.InvalidateMap(); };
            npcSpriteIDNumberBox.ValueChanged += redraw;
            npcXLeashNumberBox.ValueChanged += redraw;
            npcYLeashNumberBox.ValueChanged += redraw;
            npcSightRangeNumberBox.ValueChanged += redraw;
            npcDirectionNumberBox.ValueChanged += redraw;
            AttachSidePosBox();
        }

        NumericUpDown npcSidePosBox;
        bool railSideEnabled;

        void AttachSidePosBox()
        {
            if (npcDirectionNumberBox == null) return;
            var parent = npcDirectionNumberBox.Parent;
            if (parent == null) return;
            var label = new Label
            {
                Text = "Side pos",
                AutoSize = true,
                Location = new Point(npcDirectionNumberBox.Left - 62, npcDirectionNumberBox.Bottom + 8)
            };
            npcSidePosBox = new NumericUpDown
            {
                Location = new Point(npcDirectionNumberBox.Left, npcDirectionNumberBox.Bottom + 4),
                Size = npcDirectionNumberBox.Size,
                Minimum = -32768,
                Maximum = 32767
            };
            npcSidePosBox.ValueChanged += SidePosChanged;
            parent.Controls.Add(label);
            parent.Controls.Add(npcSidePosBox);
            mapTypeNumberBox.ValueChanged += (s, e) => RefreshSidePosEnabled();
            RefreshSidePosEnabled();
        }

        void RefreshSidePosEnabled()
        {
            int mapType = mapTypeNumberBox == null ? 0 : (int)mapTypeNumberBox.Value;
            railSideEnabled = mapType != 0 && mapType != 16;
            if (npcSidePosBox == null) return;
            npcSidePosBox.Enabled = railSideEnabled;
            npcSidePosBox.ReadOnly = !railSideEnabled;
        }

        void SidePosChanged(object sender, EventArgs e)
        {
            if (syncingNpc || !railSideEnabled) return;
            var objs = CurrentObjects;
            if (objs == null || objs.NPCs == null) return;
            int i = (int)npcIDNumberBox.Value;
            if (i < 0 || i >= objs.NPCs.Count) return;
            objs.NPCs[i].unknown5 = (short)npcSidePosBox.Value;
        }

        void NpcCoordBoxChanged(object sender, EventArgs e)
        {
            if (syncingNpc) return;
            var objs = CurrentObjects;
            if (objs == null || objs.NPCs == null || objs.NPCs.Count == 0) return;
            int i = (int)npcIDNumberBox.Value;
            if (i < 0 || i >= objs.NPCs.Count) return;
            // Only write the box that actually changed. Writing both here used to
            // stamp the previous NPC's leftover Y onto the newly selected one
            // while npcIDNumberBox_ValueChanged was still filling the form.
            if (sender == npcXPositionNumberBox)
                objs.NPCs[i].xPosition = (short)npcXPositionNumberBox.Value;
            else if (sender == npcYPositionNumberBox)
                objs.NPCs[i].yPosition = (short)npcYPositionNumberBox.Value;
            else
            {
                objs.NPCs[i].xPosition = (short)npcXPositionNumberBox.Value;
                objs.NPCs[i].yPosition = (short)npcYPositionNumberBox.Value;
            }
            if (mapView != null) mapView.InvalidateMap();
        }

        public void SelectNpc(int index)
        {
            if (CurrentObjects == null || CurrentObjects.NPCs == null) return;
            if (index < 0 || index >= CurrentObjects.NPCs.Count) return;
            if (overworlObjectTabs.TabPages.Count > 0)
            {
                overworlObjectTabs.SelectedIndex = 0;
                overworlObjectTabs.TabPages[0].Enabled = true;
            }
            if (index > npcIDNumberBox.Maximum) npcIDNumberBox.Maximum = index;
            if (npcIDNumberBox.Value != index)
                npcIDNumberBox.Value = index;
            else
                npcIDNumberBox_ValueChanged(this, EventArgs.Empty);
        }

        public void SelectNpc(int index, bool scrollIntoView)
        {
            SelectNpc(index);
            if (scrollIntoView && mapView != null) mapView.ScrollSelectedIntoView();
        }

        public int PlaceBlankNpcAt(short x, short y)
        {
            var objs = CurrentObjects;
            if (objs == null || objs.NPCs == null) return -1;
            if (objs.NPCs.Count >= 255) return -1;
            var npc = new OverworldNPC
            {
                xPosition = x,
                yPosition = y,
                defaultDirection = 1
            };
            objs.NPCs.Add(npc);
            objs.ApplyData();
            int index = objs.NPCs.Count - 1;
            if (overworlObjectTabs.TabPages.Count > 0)
            {
                overworlObjectTabs.SelectedIndex = 0;
                overworlObjectTabs.TabPages[0].Enabled = true;
            }
            npcIDNumberBox.Maximum = index;
            npcCountLabel.Text = "/ " + npcIDNumberBox.Maximum.ToString();
            SelectNpc(index, false);
            if (statusText != null)
                statusText.Text = "Added NPC " + index + " at (" + x + ", " + y + ") - " + DateTime.Now.StatusText();
            if (mapView != null) mapView.InvalidateMap();
            return index;
        }

        public void MoveNpcTo(int index, short x, short y, bool persist)
        {
            var objs = CurrentObjects;
            if (objs == null || objs.NPCs == null || index < 0 || index >= objs.NPCs.Count) return;
            var npc = objs.NPCs[index];
            npc.xPosition = x;
            npc.yPosition = y;
            syncingNpc = true;
            try
            {
                if (npcXPositionNumberBox.Value != x) npcXPositionNumberBox.Value = x;
                if (npcYPositionNumberBox.Value != y) npcYPositionNumberBox.Value = y;
            }
            finally { syncingNpc = false; }
            if (persist)
            {
                objs.ApplyData();
                statusText.Text = "Moved NPC " + index + " to (" + x + ", " + y + ") - " + DateTime.Now.StatusText();
            }
            if (mapView != null) mapView.InvalidateMap();
        }

        private void LoadZoneIntoEditor(object sender, EventArgs e)
        {
            if (zoneIdDropdown.SelectedItem is ZoneDataEntry z && z.bytes.Length == 48)
            {
                mapTypeNumberBox.Value = z.mapType;
                mapMatrixNumberBox.Value = z.matrix;
                scriptFileNumberBox.Value = z.scriptFile;
                textFileNumberBox.Value = z.storyTextFile;
                encounterFileNumberBox.Value = z.encounterFile;
                mapIDNumberBox.Value = z.mapId;
                parentMapIDNumberBox.Value = z.parentMapId;
                weatherNumberBox.Value = z.weather;
                textureNumberBox.Value = z.texture;
                unk1NumberBox.Value = z.unknown1;
                unk2NumberBox.Value = z.unknown2;
                unk3NumberBox.Value = z.unknown3;
                unk4NumberBox.Value = z.unknown4;
                cameraNumberBox.Value = z.camera;
                flagsNumberBox.Value = z.flags;
                mapNameDropdown.SelectedIndex = z.nameId;
                challengeLevelNumberBox.Value = z.difficultyLevelChange;
                flyXNumberBox.Value = z.flyX;
                flyYNumberBox.Value = z.flyZ;

                mapTypeNumberBox.Enabled = true;
                mapMatrixNumberBox.Enabled = true;
                scriptFileNumberBox.Enabled = true;
                textFileNumberBox.Enabled = true;
                encounterFileNumberBox.Enabled = true;
                mapIDNumberBox.Enabled = true;
                parentMapIDNumberBox.Enabled = true;
                weatherNumberBox.Enabled = true;
                textureNumberBox.Enabled = true;
                unk1NumberBox.Enabled = true;
                unk2NumberBox.Enabled = true;
                unk3NumberBox.Enabled = true;
                unk4NumberBox.Enabled = true;
                cameraNumberBox.Enabled = true;
                flagsNumberBox.Enabled = true;
                mapNameDropdown.Enabled = true;
                applyZoneButton.Enabled = true;
                challengeLevelNumberBox.Enabled = true;
                flyXNumberBox.Enabled = true;
                flyYNumberBox.Enabled = true;

                if (overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs.Count > 0)
                {
                    overworlObjectTabs.TabPages[0].Enabled = true;

                    npcIDNumberBox.Value = 0;
                    npcIDNumberBox.Maximum = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs.Count - 1;
                    npcCountLabel.Text = "/ " + npcIDNumberBox.Maximum.ToString();

                    npcIDNumberBox_ValueChanged(sender, e);
                }
                else
                {
                    overworlObjectTabs.TabPages[0].Enabled = false;
                }

                if (overworldObjectNarc.objects[(int)mapIDNumberBox.Value].furniture.Count > 0)
                {
                    overworlObjectTabs.TabPages[1].Enabled = true;

                    furnitureIDNumberBox.Value = 0;
                    furnitureIDNumberBox.Maximum = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].furniture.Count - 1;
                    furnitureCountLabel.Text = "/ " + furnitureIDNumberBox.Maximum.ToString();

                    furnitureIDNumberBox_ValueChanged(sender, e);
                }
                else
                {
                    // Keep the tab selectable so the first proxy can be added.
                    overworlObjectTabs.TabPages[1].Enabled = true;
                    furnitureIDNumberBox.Maximum = 0;
                    furnitureCountLabel.Text = "/ -";
                }

                if (overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps.Count > 0)
                {
                    overworlObjectTabs.TabPages[2].Enabled = true;

                    warpIDNumberBox.Value = 0;
                    warpIDNumberBox.Maximum = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps.Count - 1;
                    warpCountLabel.Text = "/ " + warpIDNumberBox.Maximum.ToString();

                    warpIDNumberBox_ValueChanged(sender, e);
                }
                else
                {
                    overworlObjectTabs.TabPages[2].Enabled = false;
                }

                if (overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers.Count > 0)
                {
                    overworlObjectTabs.TabPages[3].Enabled = true;

                    triggerIDNumberBox.Value = 0;
                    triggerIDNumberBox.Maximum = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers.Count - 1;
                    triggerCountLabel.Text = "/ " + triggerIDNumberBox.Maximum.ToString();

                    triggerIDNumberBox_ValueChanged(sender, e);
                }
                else
                {
                    overworlObjectTabs.TabPages[3].Enabled = false;
                }

                levelScriptsListBox.Items.Clear();
                levelScriptsListBox.Items.AddRange(overworldObjectNarc.objects[(int)mapIDNumberBox.Value].staticLevelScripts.ToArray());
                levelScriptsListBox.Items.AddRange(overworldObjectNarc.objects[(int)mapIDNumberBox.Value].dynamicLevelScripts.ToArray());
                label54.Visible = false;
                label55.Visible = false;
                label56.Visible = false;
                label57.Visible = false;
                levelScriptIdNumberBox.Visible = false;
                levelScriptTypeNumberBox.Visible = false;
                levelScriptVarNumberBox.Visible = false;
                levelScriptConstNumberBox.Visible = false;

                if (mapView != null) mapView.Rebuild();

                //string text = "";
                //for (int n = 0; n < overworldObjectNarc.objects[(int)mapIDNumberBox.Value].endData.Count; n++)
                //{
                //    text += overworldObjectNarc.objects[(int)mapIDNumberBox.Value].endData[n].ToString("X2");
                //    if (n % 6 == 5) text += "\n";
                //    else text += " ";
                //}
                //extraDataTextBox.Text = text;
            }
            else
            {
                mapTypeNumberBox.Enabled = false;
                mapMatrixNumberBox.Enabled = false;
                scriptFileNumberBox.Enabled = false;
                textFileNumberBox.Enabled = false;
                encounterFileNumberBox.Enabled = false;
                mapIDNumberBox.Enabled = false;
                parentMapIDNumberBox.Enabled = false;
                weatherNumberBox.Enabled = false;
                textureNumberBox.Enabled = false;
                unk1NumberBox.Enabled = false;
                unk2NumberBox.Enabled = false;
                unk3NumberBox.Enabled = false;
                unk4NumberBox.Enabled = false;
                cameraNumberBox.Enabled = false;
                flagsNumberBox.Enabled = false;
                mapNameDropdown.Enabled = false;
                applyZoneButton.Enabled = false;
                challengeLevelNumberBox.Enabled = false;
                flyXNumberBox.Enabled = false;
                flyYNumberBox.Enabled = false;

                overworlObjectTabs.Enabled = false;
                if (mapView != null) mapView.Rebuild();
            }
        }

        private void ApplyZoneData(object sender, EventArgs e)
        {
            if (zoneIdDropdown.SelectedItem is ZoneDataEntry z && z.bytes.Length == 48)
            {
                z.mapType = (byte)mapTypeNumberBox.Value;
                z.matrix = (short)mapMatrixNumberBox.Value;
                z.scriptFile = (short)scriptFileNumberBox.Value;
                z.storyTextFile = (short)textFileNumberBox.Value;
                z.encounterFile = (byte)encounterFileNumberBox.Value;
                z.mapId = (short)mapIDNumberBox.Value;
                z.parentMapId = (short)parentMapIDNumberBox.Value;
                z.unknown1 = (byte)unk1NumberBox.Value;
                z.unknown2 = (byte)unk2NumberBox.Value;
                z.unknown3 = (byte)unk3NumberBox.Value;
                z.unknown4 = (short)unk4NumberBox.Value;
                z.camera = (byte)cameraNumberBox.Value;
                z.flags = (byte)flagsNumberBox.Value;
                z.weather = (byte)weatherNumberBox.Value;
                z.texture = (short)textureNumberBox.Value;
                z.nameId = (byte)mapNameDropdown.SelectedIndex;
                z.difficultyLevelChange = (byte)challengeLevelNumberBox.Value;
                z.flyX = (int)flyXNumberBox.Value;
                z.flyZ = (int)flyYNumberBox.Value;

                z.ApplyData();

                statusText.Text = "Saved overworld header data - " + DateTime.Now.StatusText();
            }
        }

        private void openTextFileButton_Click(object sender, EventArgs e)
        {
            Program.main.OpenTextViewer(sender, e);
            if (MainEditor.textViewer != null)
            {
                MainEditor.textViewer.storyTextRadioButton.Checked = true;
                if (textFileNumberBox.Value > 0 && textFileNumberBox.Value < MainEditor.textViewer.fileNumComboBox.Items.Count) MainEditor.textViewer.fileNumComboBox.SelectedIndex = (int)textFileNumberBox.Value;
                else MessageBox.Show("Could not find the text file by index");
            }
        }

        public static Dictionary<int, int> overlayZones = new Dictionary<int, int>()
        {
            { 381, 53 },
            { 52, 51 },
            { 193, 55 },
            { 192, 55 },
            { 573, 63 },
            { 490, 58 },
            { 491, 58 },
            { 463, 67 },
            { 465, 67 },
            { 474, 67 },
            { 565, 68 },
            { 614, 68 },
            { 53, 68 },
            { 604, 66 },
            { 427, 66 },
            { 139, 66 },
            { 213, 66 },
            { 566, 62 },
            { 567, 62 },
            { 568, 62 },
            { 574, 62 },
            { 140, 52 },
            { 141, 52 },
            { 142, 52 },
            { 143, 52 },
            { 144, 52 },
            { 241, 54 },
            { 242, 54 },
            { 243, 54 },
            { 244, 54 },
            { 245, 54 },
            { 561, 64 },
            { 564, 64 },
            { 553, 64 },
            { 563, 64 },
            { 558, 64 },
            { 579, 64 },
            { 580, 64 },
            { 581, 64 },
            { 582, 64 },
            { 583, 64 },
            { 66, 50 },
            { 67, 50 },
            { 68, 50 },
            { 69, 50 },
            { 70, 50 },
            { 71, 50 },
            { 72, 50 },
            { 73, 50 },
            { 74, 50 },
            { 75, 50 },
            { 76, 50 },
            { 478, 61 },
            { 479, 61 },
            { 480, 61 },
            { 481, 61 },
            { 482, 61 },
            { 483, 61 },
            { 484, 61 },
            { 485, 61 },
            { 486, 61 },
            { 487, 61 },
            { 492, 61 },
            { 493, 61 },
            { 1, 65 },
            { 8, 65 },
            { 20, 65 },
            { 41, 65 },
            { 65, 65 },
            { 99, 65 },
            { 109, 65 },
            { 115, 65 },
            { 122, 65 },
            { 146, 65 },
            { 398, 65 },
            { 407, 65 },
            { 413, 65 },
            { 425, 65 },
            { 435, 65 },
            { 443, 65 },
            { 454, 65 },
            { 460, 65 },
            { 472, 65 },
            { 602, 65 },
        };

        private void openScriptFileButton_Click(object sender, EventArgs e)
        {
            Program.main.OpenScriptEditor(sender, e);
            if (MainEditor.scriptEditor != null)
            {
                if (scriptFileNumberBox.Value >= 0 && scriptFileNumberBox.Value < MainEditor.scriptEditor.scriptFileDropdown.Items.Count)
                {
                    MainEditor.scriptEditor.scriptFileDropdown.SelectedIndex = (int)scriptFileNumberBox.Value;
                    if (overlayZones.ContainsKey(zoneIdDropdown.SelectedIndex) && MainEditor.RomType == RomType.BW2)
                    {
                        MainEditor.scriptEditor.loadedOverlayDropdown.SelectedItem = overlayZones[zoneIdDropdown.SelectedIndex].ToString();
                    }
                    else MainEditor.scriptEditor.loadedOverlayDropdown.SelectedIndex = 0;
                }
                else MessageBox.Show("Could not find the script file by index");
            }
        }

        private void openEncounterFileButton_Click(object sender, EventArgs e)
        {
            Program.main.OpenEncounterEditor(sender, e);
            if (MainEditor.encounterEditor != null)
            {
                if (encounterFileNumberBox.Value > 0 && encounterFileNumberBox.Value < MainEditor.encounterNarc.mainEncounterPools.Count) MainEditor.encounterEditor.encounterRouteNameDropdown.SelectedItem = MainEditor.encounterNarc.mainEncounterPools[(int)encounterFileNumberBox.Value];
                else MessageBox.Show("Could not find the encounter file by index");
            }
        }

        private void npcIDNumberBox_ValueChanged(object sender, EventArgs e)
        {
            OverworldNPC npc = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs[(int)npcIDNumberBox.Value];

            syncingNpc = true;
            try
            {
                npcSpriteIDNumberBox.Value = npc.sprite;
                npcFlagNumberBox.Value = npc.flag;
                npcScriptNumberBox.Value = npc.scriptUsed;
                npcXLeashNumberBox.Value = npc.horizontalLeash;
                npcYLeashNumberBox.Value = npc.verticalLeash;
                npcSightRangeNumberBox.Value = npc.sightRange;
                npcMovementPermissionsNumberBox.Value = npc.movementPermissions;
                npcXPositionNumberBox.Value = npc.xPosition;
                npcYPositionNumberBox.Value = npc.yPosition;
                npcZPositionNumberBox.Value = npc.zPosition;
                npcDirectionNumberBox.Value = npc.defaultDirection;
                if (npcSidePosBox != null)
                {
                    decimal side = npc.unknown5;
                    if (side < npcSidePosBox.Minimum) side = npcSidePosBox.Minimum;
                    if (side > npcSidePosBox.Maximum) side = npcSidePosBox.Maximum;
                    npcSidePosBox.Value = side;
                }
                RefreshSidePosEnabled();
            }
            finally { syncingNpc = false; }
            if (mapView != null) mapView.InvalidateMap();
        }

        private void furnitureIDNumberBox_ValueChanged(object sender, EventArgs e)
        {
            var objs = CurrentObjects;
            if (objs == null || objs.furniture == null || objs.furniture.Count == 0) return;
            int index = (int)furnitureIDNumberBox.Value;
            if (index < 0 || index >= objs.furniture.Count) return;
            ShowProxy(objs.furniture[index]);
        }

        void ShowProxy(OverworldFurniture fur)
        {
            if (fur == null || proxyConditionBox == null || proxyInteractBox == null) return;
            syncingProxy = true;
            try
            {
                furnitureScriptNumberBox.Value = ClampBox(furnitureScriptNumberBox, fur.scriptUsed);
                proxyConditionBox.Value = ClampBox(proxyConditionBox, fur.condition);
                int face = fur.interactibility;
                if (face < 0 || face >= proxyInteractBox.Items.Count) face = 0;
                proxyInteractBox.SelectedIndex = face;
                proxyRailBox.Checked = fur.rail;
                if (fur.rail)
                {
                    furnitureXPosNumberBox.Value = ClampBox(furnitureXPosNumberBox, fur.railLine);
                    furnitureZPosNumberBox.Value = ClampBox(furnitureZPosNumberBox, fur.railFront);
                    proxySideBox.Value = ClampBox(proxySideBox, fur.railSide);
                }
                else
                {
                    furnitureXPosNumberBox.Value = ClampBox(furnitureXPosNumberBox, fur.gridX);
                    furnitureZPosNumberBox.Value = ClampBox(furnitureZPosNumberBox, fur.gridZ);
                    proxySideBox.Value = 0;
                }
                furnitureYPosNumberBox.Value = ClampBox(furnitureYPosNumberBox, fur.height);
                UpdateProxyRailLabels();
                UpdateProxyItemMode();
            }
            finally { syncingProxy = false; }
            if (mapView != null) mapView.InvalidateMap();
        }

        static decimal ClampBox(NumericUpDown box, int value)
        {
            if (value < box.Minimum) return box.Minimum;
            if (value > box.Maximum) return box.Maximum;
            return value;
        }

        void ReadProxyFields(OverworldFurniture fur)
        {
            if (fur == null || proxyInteractBox == null) return;
            fur.scriptUsed = (short)furnitureScriptNumberBox.Value;
            fur.condition = (short)proxyConditionBox.Value;
            fur.interactibility = (short)Math.Max(0, proxyInteractBox.SelectedIndex);
            fur.rail = proxyRailBox.Checked;
            fur.height = (int)furnitureYPosNumberBox.Value;
            if (fur.rail)
            {
                fur.railLine = (short)furnitureXPosNumberBox.Value;
                fur.railFront = (short)furnitureZPosNumberBox.Value;
                fur.railSide = (short)proxySideBox.Value;
            }
            else
            {
                fur.gridX = (int)furnitureXPosNumberBox.Value;
                fur.gridZ = (int)furnitureZPosNumberBox.Value;
            }
        }

        public int SelectedProxyIndex
        {
            get
            {
                if (CurrentObjects == null || CurrentObjects.furniture == null || CurrentObjects.furniture.Count == 0) return -1;
                return (int)furnitureIDNumberBox.Value;
            }
        }

        public bool ProxyTabActive
        {
            get { return overworlObjectTabs != null && overworlObjectTabs.SelectedIndex == 1; }
        }

        public void SelectProxy(int index)
        {
            var objs = CurrentObjects;
            if (objs == null || objs.furniture == null) return;
            if (index < 0 || index >= objs.furniture.Count) return;
            if (overworlObjectTabs.TabPages.Count > 1)
            {
                overworlObjectTabs.TabPages[1].Enabled = true;
                overworlObjectTabs.SelectedIndex = 1;
            }
            if (index > furnitureIDNumberBox.Maximum) furnitureIDNumberBox.Maximum = index;
            furnitureCountLabel.Text = "/ " + furnitureIDNumberBox.Maximum.ToString();
            if (furnitureIDNumberBox.Value != index)
                furnitureIDNumberBox.Value = index;
            else
                ShowProxy(objs.furniture[index]);
        }

        public int PlaceProxyAt(int x, int z)
        {
            var objs = CurrentObjects;
            if (objs == null || objs.furniture == null) return -1;
            if (objs.furniture.Count >= 255) return -1;
            var fur = new OverworldFurniture
            {
                gridX = x,
                gridZ = z,
                interactibility = 4
            };
            objs.furniture.Add(fur);
            objs.ApplyData();
            int index = objs.furniture.Count - 1;
            furnitureIDNumberBox.Maximum = index;
            furnitureCountLabel.Text = "/ " + index.ToString();
            SelectProxy(index);
            statusText.Text = "Added proxy " + index + " at (" + x + ", " + z + ") - " + DateTime.Now.StatusText();
            if (mapView != null) mapView.InvalidateMap();
            return index;
        }

        public void MoveProxyTo(int index, int x, int z, bool persist)
        {
            var objs = CurrentObjects;
            if (objs == null || objs.furniture == null || index < 0 || index >= objs.furniture.Count) return;
            var fur = objs.furniture[index];
            if (fur.rail) return;
            fur.gridX = x;
            fur.gridZ = z;
            if (SelectedProxyIndex == index)
            {
                syncingProxy = true;
                try
                {
                    furnitureXPosNumberBox.Value = ClampBox(furnitureXPosNumberBox, x);
                    furnitureZPosNumberBox.Value = ClampBox(furnitureZPosNumberBox, z);
                }
                finally { syncingProxy = false; }
            }
            if (persist)
            {
                objs.ApplyData();
                statusText.Text = "Moved proxy " + index + " to (" + x + ", " + z + ") - " + DateTime.Now.StatusText();
            }
            if (mapView != null) mapView.InvalidateMap();
        }

        private void warpIDNumberBox_ValueChanged(object sender, EventArgs e)
        {
            OverworldWarp warp = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps[(int)warpIDNumberBox.Value];

            warpDestMapDropdown.SelectedIndex = warp.destinationMap;
            warpDestWarpNumberBox.Value = warp.destinationWarp;
            warpExitXNumberBox.Value = (decimal)(warp.rail ? warp.exitX : ((warp.exitX - 8) / 16f));
            warpExitYNumberBox.Value = (decimal)(warp.rail ? warp.exitY : (warp.exitY / 16f));
            warpExitZNumberBox.Value = (decimal)(warp.rail ? warp.exitZ : ((warp.exitZ - 8) / 16f));
            warpRailCheckBox.Checked = warp.rail;
            warpWidthNumberBox.Value = warp.width;
            warpHeightNumberBox.Value = warp.height;
            warpDirectionDropdown.SelectedIndex = warp.unknown1;
            warpTransitionTypeNumberBox.Value = warp.unknown2;
        }

        private void triggerIDNumberBox_ValueChanged(object sender, EventArgs e)
        {
            OverworldTrigger trigger = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers[(int)triggerIDNumberBox.Value];

            triggerConstValNumberBox.Value = trigger.constantValue;
            triggerConstRefNumberBox.Value = trigger.constantReference;
            triggerScriptNumberBox.Value = trigger.scriptUsed;
            triggerXNumberBox.Value = trigger.xPosition;
            triggerYNumberBox.Value = trigger.yPosition;
            triggerWidthNumberBox.Value = trigger.width;
            triggerHeightNumberBox.Value = trigger.height;
            triggerYNumberBox.Value = trigger.yPosition;
            triggerZNumberBox.Value = trigger.zPosition;
        }

        private void addObjectButton_Click(object sender, EventArgs e)
        {
            if (overworlObjectTabs.SelectedIndex == 0) overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs.Add(new OverworldNPC());
            else if (overworlObjectTabs.SelectedIndex == 1)
            {
                var added = new OverworldFurniture { interactibility = 4 };
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].furniture.Add(added);
            }
            else if (overworlObjectTabs.SelectedIndex == 2) overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps.Add(new OverworldWarp());
            else if (overworlObjectTabs.SelectedIndex == 3) overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers.Add(new OverworldTrigger());
            else if (overworlObjectTabs.SelectedIndex == 4)
            {
                MessageBox.Show("Please use \"Add Static\" or \"Add Dynamic\" to specify what type of level script you are adding.");
                return;
            }
            overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
            LoadZoneIntoEditor(sender, e);
        }

        private void removeObjectButton_Click(object sender, EventArgs e)
        {
            if (overworlObjectTabs.SelectedIndex == 0 && overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs.Count > 0)
            {
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs.RemoveAt((int)npcIDNumberBox.Value);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
            }
            else if (overworlObjectTabs.SelectedIndex == 1 && overworldObjectNarc.objects[(int)mapIDNumberBox.Value].furniture.Count > 0)
            {
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].furniture.RemoveAt((int)furnitureIDNumberBox.Value);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
            }
            else if (overworlObjectTabs.SelectedIndex == 2 && overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps.Count > 0)
            {
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps.RemoveAt((int)warpIDNumberBox.Value);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
            }
            else if (overworlObjectTabs.SelectedIndex == 3 && overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers.Count > 0)
            {
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers.RemoveAt((int)triggerIDNumberBox.Value);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
            }
            else if (overworlObjectTabs.SelectedIndex == 4 && levelScriptsListBox.SelectedItem is StaticLevelScript sl)
            {
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].staticLevelScripts.Remove(sl);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
            }
            else if (overworlObjectTabs.SelectedIndex == 4 && levelScriptsListBox.SelectedItem is DynamicLevelScript dl)
            {
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].dynamicLevelScripts.Remove(dl);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
            }
        }

        private void applyObjectButton_Click(object sender, EventArgs e)
        {
            if (overworlObjectTabs.SelectedIndex == 0)
            {
                OverworldNPC npc = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].NPCs[(int)npcIDNumberBox.Value];

                npc.sprite = (short)npcSpriteIDNumberBox.Value;
                npc.flag = (short)npcFlagNumberBox.Value;
                npc.scriptUsed = (short)npcScriptNumberBox.Value;
                npc.horizontalLeash = (short)npcXLeashNumberBox.Value;
                npc.verticalLeash = (short)npcYLeashNumberBox.Value;
                npc.sightRange = (short)npcSightRangeNumberBox.Value;
                npc.movementPermissions = (short)npcMovementPermissionsNumberBox.Value;
                npc.xPosition = (short)npcXPositionNumberBox.Value;
                npc.yPosition = (short)npcYPositionNumberBox.Value;
                npc.zPosition = (short)npcZPositionNumberBox.Value;
                npc.defaultDirection = (short)npcDirectionNumberBox.Value;
                if (npcSidePosBox != null && railSideEnabled)
                    npc.unknown5 = (short)npcSidePosBox.Value;

                statusText.Text = "Saved npc data - " + DateTime.Now.StatusText();
                if (mapView != null) mapView.InvalidateMap();
            }
            if (overworlObjectTabs.SelectedIndex == 1)
            {
                var list = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].furniture;
                if (list.Count == 0) return;
                OverworldFurniture fur = list[(int)furnitureIDNumberBox.Value];
                ReadProxyFields(fur);
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                statusText.Text = "Saved proxy script " + fur.scriptUsed + " - " + DateTime.Now.StatusText();
                if (mapView != null) mapView.InvalidateMap();
            }
            else if (overworlObjectTabs.SelectedIndex == 2)
            {
                OverworldWarp warp = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].warps[(int)warpIDNumberBox.Value];

                warp.destinationMap = (short)warpDestMapDropdown.SelectedIndex;
                warp.destinationWarp = (short)warpDestWarpNumberBox.Value;
                warp.rail = warpRailCheckBox.Checked;
                warp.exitX = (short)(warp.rail ? warpExitXNumberBox.Value :(warpExitXNumberBox.Value * 16 + 8));
                warp.exitY = (short)(warp.rail ? warpExitYNumberBox.Value : (warpExitYNumberBox.Value * 16));
                warp.exitZ = (short)(warp.rail ? warpExitZNumberBox.Value : (warpExitZNumberBox.Value * 16 + 8));
                warp.width = (short)warpWidthNumberBox.Value;
                warp.height = (short)warpHeightNumberBox.Value;
                warp.unknown1 = (byte)warpDirectionDropdown.SelectedIndex;
                warp.unknown2 = (byte)warpTransitionTypeNumberBox.Value;

                statusText.Text = "Saved warp data - " + DateTime.Now.StatusText();
                if (mapView != null) mapView.InvalidateMap();
            }
            else if (overworlObjectTabs.SelectedIndex == 3)
            {
                OverworldTrigger trigger = overworldObjectNarc.objects[(int)mapIDNumberBox.Value].triggers[(int)triggerIDNumberBox.Value];

                trigger.constantValue = (short)triggerConstValNumberBox.Value;
                trigger.constantReference = (short)triggerConstRefNumberBox.Value;
                trigger.scriptUsed = (short)triggerScriptNumberBox.Value;
                trigger.xPosition = (short)triggerXNumberBox.Value;
                trigger.yPosition = (short)triggerYNumberBox.Value;
                trigger.zPosition = (short)triggerZNumberBox.Value;
                trigger.width = (short)triggerWidthNumberBox.Value;
                trigger.height = (short)triggerHeightNumberBox.Value;

                statusText.Text = "Saved trigger data - " + DateTime.Now.StatusText();
                if (mapView != null) mapView.InvalidateMap();
            }
            else if (overworlObjectTabs.SelectedIndex == 4)
            {
                if (levelScriptsListBox.SelectedItem is StaticLevelScript sl)
                {
                    if (levelScriptTypeNumberBox.Value < 2)
                    {
                        MessageBox.Show("Static Level Script type flag must be at least 2.");
                        return;
                    }
                    sl.type = (short)levelScriptTypeNumberBox.Value;
                    sl.scriptID = (int)levelScriptIdNumberBox.Value;
                }
                if (levelScriptsListBox.SelectedItem is DynamicLevelScript dl)
                {
                    dl.scriptID = (short)levelScriptIdNumberBox.Value;
                    dl.checkVar = (short)levelScriptVarNumberBox.Value;
                    dl.checkConst = (short)levelScriptConstNumberBox.Value;
                }
                var i = levelScriptsListBox.SelectedItem;
                overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
                LoadZoneIntoEditor(sender, e);
                levelScriptsListBox.SelectedItem = i;

                statusText.Text = "Saved level script data - " + DateTime.Now.StatusText();
            }

            overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
        }

        //private void ApplyEndData(List<byte> destination)
        //{
        //    string str = extraDataTextBox.Text.Replace('\n', ' ');
        //
        //    //Test for improper text length
        //    if (str.Length % 3 == 2 && str[extraDataTextBox.Text.Length - 1] != ' ') str += ' ';
        //    if (str.Length < 3 || str.Length % 3 != 0)
        //    {
        //        MessageBox.Show("Byte Data detected an incorrect format");
        //        return;
        //    }
        //
        //    //Test for improper text values
        //    for (int i = 2; i < str.Length; i += 3) if (str[i] != ' ' ||
        //            (!char.IsDigit(str[i - 1]) && !(str[i - 1] >= 'A' && str[i - 1] <= 'F')) ||
        //            (!char.IsDigit(str[i - 2]) && !(str[i - 2] >= 'A' && str[i - 2] <= 'F')))
        //        {
        //            MessageBox.Show("Byte Data detected an incorrect format");
        //            return;
        //        }
        //
        //    //Convert data to file
        //    destination.Clear();
        //    for (int i = 0; i < str.Length; i += 3)
        //    {
        //        destination.Add(byte.Parse(str.Substring(i, 2), System.Globalization.NumberStyles.HexNumber));
        //    }
        //}

        private void npcScriptNumberBox_ValueChanged(object sender, EventArgs e)
        {
            int i = (int)npcScriptNumberBox.Value;
            if (i >= 7000 && i < 7400)
            {
                if (MainEditor.scriptNarc != null)
                {
                    try
                    {
                        int item = MainEditor.scriptNarc.scriptFiles[MainEditor.RomType == RomType.BW2 ? 1240 : 864].sequences[i - 7000].commands[1].parameters[1];
                        string name = MainEditor.textNarc.textFiles[VersionConstants.ItemNameTextFileID].text[item];
                        giveItemLabel.Text = "Give Item: " + name;

                        setItemDropdown.SelectedIndex = MainEditor.scriptNarc.scriptFiles[MainEditor.RomType == RomType.BW2 ? 1240 : 864].sequences[i - 7000].commands[1].parameters[1];
                        setItemDropdown.Enabled = true;
                        setItemButton.Enabled = true;
                    }
                    catch
                    {
                        giveItemLabel.Text = "";
                        setItemDropdown.SelectedIndex = 0;
                        setItemDropdown.Enabled = false;
                        setItemButton.Enabled = false;
                    }
                }
            }
            else if (i > 3000 && i < MainEditor.trainerNarc.trainers.Count + 3000)
            {
                if (MainEditor.trainerNarc != null)
                {
                    giveItemLabel.Text = "Trainer: " + MainEditor.trainerNarc.trainers[i - 3000].ToString();
                }
            }
            else if (i > 5000 && i < MainEditor.trainerNarc.trainers.Count + 5000)
            {
                if (MainEditor.trainerNarc != null)
                {
                    giveItemLabel.Text = "Trainer: " + MainEditor.trainerNarc.trainers[i - 5000].ToString();
                }
            }
            else
            {
                giveItemLabel.Text = "";
                setItemDropdown.SelectedIndex = 0;
                setItemDropdown.Enabled = false;
                setItemButton.Enabled = false;
            }
        }

        private void setItemButton_Click(object sender, EventArgs e)
        {
            int i = (int)npcScriptNumberBox.Value;
            if (i >= 7000 && i < 7400 && setItemDropdown.SelectedIndex > 0)
            {
                if (MainEditor.scriptNarc != null)
                {
                    string name = MainEditor.textNarc.textFiles[VersionConstants.ItemNameTextFileID].text[setItemDropdown.SelectedIndex];
                    giveItemLabel.Text = "Give Item: " + name;

                    MainEditor.scriptNarc.scriptFiles[MainEditor.RomType == RomType.BW2 ? 1240 : 864].sequences[i - 7000].commands[1].parameters[1] = setItemDropdown.SelectedIndex;
                    MainEditor.scriptNarc.scriptFiles[MainEditor.RomType == RomType.BW2 ? 1240 : 864].ApplyData();
                }
            }
        }

        private void warpRailTextBox_CheckedChanged(object sender, EventArgs e)
        {
            if (warpRailCheckBox.Checked)
            {
                exitXText.Text = "Line:";
                exitYText.Text = "Pos:";
                exitZText.Text = "Side:";
            }
            else
            {
                exitXText.Text = "Exit X:";
                exitYText.Text = "Exit Y:";
                exitZText.Text = "Exit Z:";
            }
        }

        private void SelectLevelScript(object sender, EventArgs e)
        {
            if (levelScriptsListBox.SelectedItem is StaticLevelScript sl)
            {
                label54.Visible = true;
                label55.Visible = true;
                label56.Visible = false;
                label57.Visible = false;
                levelScriptIdNumberBox.Visible = true;
                levelScriptIdNumberBox.Value = sl.scriptID;
                levelScriptTypeNumberBox.Visible = true;
                levelScriptTypeNumberBox.Value = sl.type;
                levelScriptVarNumberBox.Visible = false;
                levelScriptConstNumberBox.Visible = false;
            }
            if (levelScriptsListBox.SelectedItem is DynamicLevelScript dl)
            {
                label54.Visible = true;
                label55.Visible = false;
                label56.Visible = true;
                label57.Visible = true;
                levelScriptIdNumberBox.Visible = true;
                levelScriptIdNumberBox.Value = dl.scriptID;
                levelScriptTypeNumberBox.Visible = false;
                levelScriptVarNumberBox.Visible = true;
                levelScriptVarNumberBox.Value = dl.checkVar;
                levelScriptConstNumberBox.Visible = true;
                levelScriptConstNumberBox.Value = dl.checkConst;
            }
        }

        private void addStaticScriptButton_Click(object sender, EventArgs e)
        {
            overworldObjectNarc.objects[(int)mapIDNumberBox.Value].staticLevelScripts.Add(new StaticLevelScript()
            {
                scriptID = 1,
                type = 2
            });
            overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
            LoadZoneIntoEditor(sender, e);
        }

        private void addDynamicScriptButton_Click(object sender, EventArgs e)
        {
            overworldObjectNarc.objects[(int)mapIDNumberBox.Value].dynamicLevelScripts.Add(new DynamicLevelScript()
            {
                checkVar = 0x4000,
                checkConst = 1,
                scriptID = 1,
            });
            overworldObjectNarc.objects[(int)mapIDNumberBox.Value].ApplyData();
            LoadZoneIntoEditor(sender, e);
        }
    }
}
