using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace NewEditor.Forms
{
    /// <summary>
    /// A drop-down you can type into to narrow its options, or scroll as usual.
    /// Typing matches any part of a name, or the start of a number ("25" or
    /// "#25"). Enter or a click picks an option; leaving the box without picking
    /// one puts the previous choice back.
    /// </summary>
    public class SearchComboBox : ComboBox
    {
        public class Option
        {
            public readonly int Value;
            public readonly string Label;
            public Option(int value, string label) { Value = value; Label = label; }
            public override string ToString() => Label;
        }

        List<Option> options = new List<Option>();
        Option chosen;
        bool updating;

        /// <summary>Raised when the user picks a different option.</summary>
        public event EventHandler ValueChosen;

        public SearchComboBox()
        {
            DropDownStyle = ComboBoxStyle.DropDown;
            AutoCompleteMode = AutoCompleteMode.None;
            MaxDropDownItems = 16;
        }

        public void SetOptions(IEnumerable<Option> list)
        {
            options = list.ToList();
            chosen = chosen == null ? null : options.FirstOrDefault(o => o.Value == chosen.Value);
            ShowAll();
        }

        /// <summary>The chosen option's value, or null. Setting it does not raise ValueChosen.</summary>
        public int? Value
        {
            get => chosen?.Value;
            set
            {
                chosen = value == null ? null : options.FirstOrDefault(o => o.Value == value.Value);
                ShowAll();
            }
        }

        static bool Matches(Option option, string query)
        {
            if (query.Length == 0) return true;
            string digits = query.TrimStart('#');
            if (digits.Length > 0 && digits.All(char.IsDigit) && option.Value.ToString().StartsWith(digits)) return true;
            return option.Label.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        void ShowAll()
        {
            updating = true;
            BeginUpdate();
            Items.Clear();
            Items.AddRange(options.Cast<object>().ToArray());
            EndUpdate();
            SelectedItem = chosen;
            if (chosen == null) Text = "";
            updating = false;
        }

        protected override void OnTextUpdate(EventArgs e)
        {
            base.OnTextUpdate(e);
            if (updating) return;
            string text = Text;
            int caret = SelectionStart;
            Option[] matches = options.Where(o => Matches(o, text.Trim())).ToArray();
            updating = true;
            BeginUpdate();
            Items.Clear();
            Items.AddRange(matches.Cast<object>().ToArray());
            EndUpdate();
            if (matches.Length > 0 && !DroppedDown)
            {
                DroppedDown = true;
                Cursor.Current = Cursors.Default;
            }
            // Opening the list selects the text; put the typing back as it was.
            Text = text;
            SelectionStart = caret;
            SelectionLength = 0;
            updating = false;
        }

        protected override void OnSelectionChangeCommitted(EventArgs e)
        {
            base.OnSelectionChangeCommitted(e);
            if (!updating && SelectedItem is Option option) Choose(option);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                Option pick = SelectedItem as Option ?? (Items.Count > 0 ? Items[0] as Option : null);
                DroppedDown = false;
                if (pick != null) Choose(pick); else ShowAll();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            if (e.KeyCode == Keys.Escape)
            {
                DroppedDown = false;
                ShowAll();
                e.Handled = e.SuppressKeyPress = true;
                return;
            }
            base.OnKeyDown(e);
        }

        protected override void OnDropDown(EventArgs e)
        {
            // Opened with the arrow while showing the current choice: offer everything.
            if (!updating && (chosen == null ? Text.Length == 0 : Text == chosen.Label)) ShowAll();
            base.OnDropDown(e);
        }

        protected override void OnLeave(EventArgs e)
        {
            base.OnLeave(e);
            Option exact = options.FirstOrDefault(o => string.Equals(o.Label, Text.Trim(), StringComparison.OrdinalIgnoreCase));
            if (exact != null && exact != chosen) Choose(exact);
            else ShowAll();
        }

        void Choose(Option option)
        {
            bool changed = chosen != option;
            chosen = option;
            // The list is being changed from inside its own selection event; finish that first.
            if (IsHandleCreated) BeginInvoke(new Action(ShowAll)); else ShowAll();
            if (changed) ValueChosen?.Invoke(this, EventArgs.Empty);
        }
    }
}
