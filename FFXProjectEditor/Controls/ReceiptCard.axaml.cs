using System;
using Avalonia.Controls;
using FFXProjectEditor.Core;

using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Controls
{
    public partial class ReceiptCard : UserControl
    {
        public ReceiptCard()
        {
            InitializeComponent();
        }

        public void SetReceipt(CommitReceipt receipt)
        {
            if (receipt == null) return;

            ReceiptIdText.Text = $"ID: #{receipt.ReceiptId[..Math.Min(8, receipt.ReceiptId.Length)]}";
            SummaryText.Text = string.Format(Strings.U_RcptSummary, receipt.FilesWritten, receipt.BytesWritten.ToString("N0"));
            TimestampText.Text = string.Format(Strings.U_RcptAppliedAt, receipt.Timestamp.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss"));

            if (receipt.BackupPaths != null && receipt.BackupPaths.Count > 0)
            {
                BackupsText.Text = string.Format(Strings.U_RcptBackup, receipt.BackupPaths[0]);
            }
            else
            {
                BackupsText.Text = Strings.F2_no_backup_needed_writing_to_new_file_62fdd39c;
            }
        }
    }
}
