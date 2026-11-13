using CDriveCleaner.Core.Rules;
using CDriveCleaner.Core.Scanning;
using CDriveCleaner.Core.Text;

namespace CDriveCleaner.App;

/// <summary>Explicit confirmation before anything is deleted (SPEC section 7). The safe button has the focus.</summary>
internal sealed class ConfirmCleanForm : Form
{
    public ConfirmCleanForm(IReadOnlyList<RuleScanResult> chosen)
    {
        Text = "确认清理";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(LogicalToDeviceUnits(680), LogicalToDeviceUnits(400));

        var heading = new Label
        {
            Text = "以下项目里的文件将被永久删除：不经过回收站，删除后无法恢复。",
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Margin = new Padding(0, 0, 0, 8),
        };

        var list = new ListView
        {
            View = View.Details,
            FullRowSelect = true,
            HeaderStyle = ColumnHeaderStyle.Nonclickable,
            Dock = DockStyle.Fill,
            AccessibleName = "待清理项目",
        };
        list.Columns.Add("项目", LogicalToDeviceUnits(200));
        list.Columns.Add("文件数", LogicalToDeviceUnits(70), HorizontalAlignment.Right);
        list.Columns.Add("大小", LogicalToDeviceUnits(80), HorizontalAlignment.Right);
        list.Columns.Add("风险", LogicalToDeviceUnits(45));
        list.Columns.Add("提示", LogicalToDeviceUnits(225));
        foreach (var result in chosen)
        {
            var item = new ListViewItem(
            [
                result.Rule.Name,
                result.Files.ToString("N0"),
                ByteSize.Format(result.Bytes),
                Texts.Risk(result.Rule.Risk),
                result.Rule.Note ?? "",
            ]);
            if (result.Rule.Risk == RiskLevel.Medium)
            {
                item.ForeColor = Texts.MediumRiskColor;
            }

            list.Items.Add(item);
        }

        var notes = new List<string>
        {
            $"合计 {chosen.Sum(r => r.Files):N0} 个文件，{ByteSize.Format(chosen.Sum(r => r.Bytes))}。正被其他程序使用的文件会自动跳过。",
        };
        if (chosen.Any(r => r.Rule.Risk == RiskLevel.Medium))
        {
            notes.Add("其中包含中风险项目，请先看清“提示”一栏。");
        }

        if (chosen.Any(r => r.Rule.Kind == RuleKind.RecycleBin))
        {
            notes.Add("回收站将被清空，里面的全部内容都无法再还原。");
        }

        var footer = new Label
        {
            Text = string.Join(Environment.NewLine, notes),
            AutoSize = true,
            Margin = new Padding(0, 8, 0, 8),
        };

        var deleteButton = new Button { Text = "永久删除", AutoSize = true, DialogResult = DialogResult.OK };
        var cancelButton = new Button { Text = "取消", AutoSize = true, DialogResult = DialogResult.Cancel };
        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            Dock = DockStyle.Fill,
            AutoSize = true,
            WrapContents = false,
        };
        buttons.Controls.Add(cancelButton);
        buttons.Controls.Add(deleteButton);

        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(LogicalToDeviceUnits(12)),
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(heading, 0, 0);
        layout.Controls.Add(list, 0, 1);
        layout.Controls.Add(footer, 0, 2);
        layout.Controls.Add(buttons, 0, 3);
        Controls.Add(layout);

        CancelButton = cancelButton;
        ActiveControl = cancelButton;
    }
}
