using NewEditor.Data;
using NewEditor.Data.NARCTypes;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    public partial class EncounterEditor : Form
    {
        EncounterNARC encounterNarc => MainEditor.encounterNarc;
        TextNARC textNARC => MainEditor.textNarc;

        List<EncounterSlot> encountersClipboard;

        public EncounterEditor()
        {
            InitializeComponent();
            AddPlannerImportButton();

            encounterRouteNameDropdown.Items.AddRange(encounterNarc.encounterPools.ToArray());
            encounterPokemonNameDropDown.Items.AddRange(textNARC.textFiles[VersionConstants.PokemonNameTextFileID].text.ToArray());
        }

        private void AddPlannerImportButton()
        {
            var importPlannerJsonButton = new Button();
            importPlannerJsonButton.Location = new Point(370, 10);
            importPlannerJsonButton.Name = "importPlannerJsonButton";
            importPlannerJsonButton.Size = new Size(170, 28);
            importPlannerJsonButton.Text = "Import Planner JSON...";
            importPlannerJsonButton.UseVisualStyleBackColor = true;
            importPlannerJsonButton.Click += ImportPlannerJsonButton_Click;
            Controls.Add(importPlannerJsonButton);
        }

        private void ImportPlannerJsonButton_Click(object sender, EventArgs e)
        {
            if (MainEditor.RomType != RomType.BW2)
            {
                MessageBox.Show("Planner JSON import currently supports Black 2 / White 2 encounter pools.");
                return;
            }

            using (OpenFileDialog dlg = new OpenFileDialog())
            {
                dlg.Filter = "Planner JSON (*.json)|*.json|All files (*.*)|*.*";
                dlg.Title = "Import encounter planner JSON";
                if (dlg.ShowDialog() != DialogResult.OK) return;

                try
                {
                    string text = System.IO.File.ReadAllText(dlg.FileName);
                    var result = PlannerEncounterImport.Import(text, encounterNarc);

                    var selected = encounterRouteNameDropdown.SelectedItem;
                    encounterRouteNameDropdown.Items.Clear();
                    encounterRouteNameDropdown.Items.AddRange(encounterNarc.encounterPools.ToArray());
                    if (selected is EncounterEntry keep)
                    {
                        var match = encounterNarc.encounterPools.FirstOrDefault(p => p.nameID == keep.nameID && p.season == keep.season);
                        if (match != null) encounterRouteNameDropdown.SelectedItem = match;
                    }

                    string skipped = result.SkippedTypes.Count > 0 ? string.Join(", ", result.SkippedTypes) : "none";
                    string unmapped = result.UnmappedLocations.Count > 0 ? string.Join(", ", result.UnmappedLocations) : "none";
                    string missing = result.MissingPools.Count > 0 ? string.Join(", ", result.MissingPools.Distinct()) : "none";
                    string warns = result.Warnings.Count > 0 ? "\n\nWarnings:\n" + string.Join("\n", result.Warnings.Take(20)) : "";

                    MessageBox.Show(
                        "Wrote " + result.TablesWritten + " tables (" + result.SlotsWritten + " slots).\n\n" +
                        "Skipped types (not in wild NARC): " + skipped + "\n" +
                        "Unmapped locations: " + unmapped + "\n" +
                        "Missing Frost pools: " + missing +
                        warns,
                        "Planner import");

                    statusText.Text = "Imported planner JSON - " + DateTime.Now.StatusText();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Import failed:\n" + ex.Message);
                }
            }
        }
    }
}
