using LocalVault.Models;

namespace LocalVault.UI;

public sealed class PasswordHistoryDialog : Form
{
    private const string Mask = "••••••••";

    private readonly Func<string, bool> _copyToClipboard;
    private readonly DataGridView _grid = new()
    {
        Dock = DockStyle.Fill,
        ReadOnly = true,
        AllowUserToAddRows = false,
        AllowUserToDeleteRows = false,
        AllowUserToResizeRows = false,
        MultiSelect = false,
        SelectionMode = DataGridViewSelectionMode.FullRowSelect,
        AutoGenerateColumns = false,
        RowHeadersVisible = false
    };
    private readonly CheckBox _revealAll = new() { Text = "すべて表示", AutoSize = true };
    private readonly List<HistoryRow> _rows;

    // pending: 編集画面で確定済みだが、まだ保存していない変更で履歴へ移る予定の旧値。
    public PasswordHistoryDialog(
        VaultItem item,
        Func<string, bool> copyToClipboard,
        IEnumerable<PasswordHistoryEntry>? pending = null)
    {
        AppBranding.Apply(this);
        _copyToClipboard = copyToClipboard;
        Text = $"パスワード履歴 - {item.Name}";
        Width = 620;
        Height = 420;
        StartPosition = FormStartPosition.CenterParent;

        _rows = (pending ?? [])
            .Select(x => new HistoryRow(x, item.Username, isPending: true))
            .Concat(item.PasswordHistory
                .OrderByDescending(x => x.RecordedAtUtc)
                .Select(x => new HistoryRow(x, item.Username, isPending: false)))
            .ToList();

        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "置き換え日時", DataPropertyName = nameof(HistoryRow.RecordedAt), Width = 190 });
        _grid.Columns.Add(new DataGridViewTextBoxColumn { HeaderText = "ユーザー名 / 項目名: 値", DataPropertyName = nameof(HistoryRow.DisplayPassword), AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill });
        _grid.DataSource = _rows;
        _grid.CellDoubleClick += (_, e) => { if (e.RowIndex >= 0) CopyCurrent(); };
        _grid.CellMouseDown += (_, e) =>
        {
            // 右クリックした行を対象にしてからメニューを出す。
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
                _grid.CurrentCell = _grid.Rows[e.RowIndex].Cells[Math.Max(e.ColumnIndex, 0)];
        };

        var menu = new ContextMenuStrip();
        var toggleItem = new ToolStripMenuItem("この行を表示", null, (_, _) => ToggleCurrent());
        menu.Items.Add(toggleItem);
        menu.Items.Add(new ToolStripMenuItem("コピー", null, (_, _) => CopyCurrent()));
        menu.Opening += (_, e) =>
        {
            if (CurrentRow is not { } row)
            {
                e.Cancel = true;
                return;
            }
            toggleItem.Text = row.Reveal ? "この行を隠す" : "この行を表示";
        };
        _grid.ContextMenuStrip = menu;

        _revealAll.CheckedChanged += (_, _) =>
        {
            foreach (var row in _rows) row.Reveal = _revealAll.Checked;
            _grid.Refresh();
        };

        var copy = new Button { Text = "選択した履歴をコピー", AutoSize = true };
        copy.Click += (_, _) => CopyCurrent();
        var close = new Button { Text = "閉じる", DialogResult = DialogResult.Cancel, AutoSize = true };

        var top = new FlowLayoutPanel { Dock = DockStyle.Top, AutoSize = true, Padding = new Padding(8) };
        top.Controls.Add(_revealAll);
        top.Controls.Add(copy);
        top.Controls.Add(new Label
        {
            Text = "ダブルクリックでコピー、右クリックで行ごとに表示",
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(8, 7, 0, 0)
        });

        var bottom = new FlowLayoutPanel { Dock = DockStyle.Bottom, Height = 48, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(8) };
        bottom.Controls.Add(close);

        if (_rows.Count == 0)
        {
            _revealAll.Enabled = false;
            copy.Enabled = false;
            Controls.Add(new Label
            {
                Text = "履歴はありません。",
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleCenter,
                ForeColor = SystemColors.GrayText
            });
        }
        else
        {
            Controls.Add(_grid);
        }
        Controls.Add(top);
        Controls.Add(bottom);
        CancelButton = close;
    }

    private HistoryRow? CurrentRow => _grid.CurrentRow?.DataBoundItem as HistoryRow;

    private void ToggleCurrent()
    {
        if (CurrentRow is not { } row) return;
        row.Reveal = !row.Reveal;
        _grid.InvalidateRow(_grid.CurrentRow!.Index);
    }

    private void CopyCurrent()
    {
        if (CurrentRow is not { } row) return;
        if (!_copyToClipboard(row.Entry.Password))
            MessageBox.Show("クリップボードを使用できませんでした。", "コピー失敗", MessageBoxButtons.OK, MessageBoxIcon.Warning);
    }

    private sealed class HistoryRow
    {
        private readonly string _currentUsername;
        private readonly bool _isPending;

        public HistoryRow(PasswordHistoryEntry entry, string currentUsername, bool isPending)
        {
            Entry = entry;
            _currentUsername = currentUsername;
            _isPending = isPending;
        }

        public PasswordHistoryEntry Entry { get; }
        public bool Reveal { get; set; }

        public string RecordedAt => _isPending
            ? "未保存（保存時に記録）"
            : Entry.RecordedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss") ?? "日時不明";

        public string DisplayPassword
        {
            get
            {
                var value = Reveal ? Entry.Password : Mask;
                if (!string.IsNullOrWhiteSpace(Entry.FieldName))
                    return $"{Entry.FieldName}: {value}";

                // 記録時のユーザー名がない履歴（既存Vault・インポート分）は現在のユーザー名で表示する。
                var username = Entry.Username ?? _currentUsername;
                return string.IsNullOrWhiteSpace(username)
                    ? $"(ユーザー名なし): {value}"
                    : $"{username}: {value}";
            }
        }
    }
}
