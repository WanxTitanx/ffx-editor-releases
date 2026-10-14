using System;
using Avalonia.Controls;

namespace FFXProjectEditor.Controls
{
    public partial class EmptyState : UserControl
    {
        public event EventHandler? ActionClicked;

        public EmptyState()
        {
            InitializeComponent();
            ActionButton.Click += (s, e) => ActionClicked?.Invoke(this, EventArgs.Empty);
        }

        public void Configure(string icon, string title, string description, string? actionText = null)
        {
            IconText.Text = icon;
            TitleText.Text = title;
            DescriptionText.Text = description;

            if (!string.IsNullOrWhiteSpace(actionText))
            {
                ActionButton.Content = actionText;
                ActionButton.IsVisible = true;
            }
            else
            {
                ActionButton.IsVisible = false;
            }
        }
    }
}
