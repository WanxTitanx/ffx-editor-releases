using Avalonia.Controls;
using FFXProjectEditor.Core;
using FFXProjectEditor.Resources;

namespace FFXProjectEditor.Controls
{
    public partial class CapabilityBadge : UserControl
    {
        public CapabilityBadge()
        {
            InitializeComponent();
        }

        public void SetCapability(CapabilityDescriptor descriptor, bool isAvailable = true)
        {
            if (descriptor == null) return;

            // Set Evidence level styling & text
            BadgeBorder.Classes.Clear();
            BadgeBorder.Classes.Add("badge");

            switch (descriptor.Evidence)
            {
                case EvidenceLevel.Production:
                    BadgeBorder.Classes.Add("production");
                    IconText.Text = "✓";
                    BadgeText.Text = Strings.U_CapProduction;
                    break;
                case EvidenceLevel.Partial:
                    BadgeBorder.Classes.Add("partial");
                    IconText.Text = "◐";
                    BadgeText.Text = Strings.U_CapPartial;
                    break;
                case EvidenceLevel.NeedsTesting:
                    BadgeBorder.Classes.Add("needstesting");
                    IconText.Text = "🧪";
                    BadgeText.Text = Strings.U_CapNeedsTesting;
                    break;
                case EvidenceLevel.Blocked:
                    BadgeBorder.Classes.Add("blocked");
                    IconText.Text = "⛔";
                    BadgeText.Text = Strings.U_CapBlocked;
                    break;
                case EvidenceLevel.Research:
                    BadgeBorder.Classes.Add("research");
                    IconText.Text = "🔬";
                    BadgeText.Text = Strings.U_CapResearch;
                    break;
            }

            // Set Mode text
            ModeText.Text = descriptor.Mode switch
            {
                CapabilityMode.ReadOnly => Strings.U_CapReadOnly,
                CapabilityMode.OfflineWriter => "Offline Writer",
                CapabilityMode.RuntimeTool => Strings.U_CapRuntimeTool,
                CapabilityMode.Lab => Strings.U_CapLaboratory,
                CapabilityMode.Research => "Research Only",
                _ => descriptor.Mode.ToString()
            };

            Opacity = isAvailable ? 1.0 : 0.55;
        }
    }
}
