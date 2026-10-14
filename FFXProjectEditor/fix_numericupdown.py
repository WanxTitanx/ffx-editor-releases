import os

files_to_style = [
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/KeyItemEditor/KeyItemEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/TreasureEditor/TreasureEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/WeaponGear/WeaponGear_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/MixTableEditor/MixTableEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/BukiGetTreasureCatalog/BukiGetTreasureCatalog_Control.axaml"
]

numeric_updown_fix = """
		<!-- NumericUpDown Fix for global TextBox styles -->
		<Style Selector="NumericUpDown">
			<Setter Property="Background" Value="#08FFFFFF" />
			<Setter Property="BorderBrush" Value="#22FFFFFF" />
			<Setter Property="BorderThickness" Value="1" />
			<Setter Property="CornerRadius" Value="3" />
			<Setter Property="MinHeight" Value="26" />
			<Setter Property="VerticalContentAlignment" Value="Center" />
		</Style>
		<Style Selector="NumericUpDown /template/ TextBox">
			<Setter Property="Background" Value="Transparent" />
			<Setter Property="BorderThickness" Value="0" />
			<Setter Property="MinHeight" Value="20" />
			<Setter Property="Margin" Value="0" />
		</Style>
		<Style Selector="NumericUpDown /template/ TextBox:pointerover /template/ Border#PART_BorderElement">
			<Setter Property="Background" Value="Transparent" />
			<Setter Property="BorderBrush" Value="Transparent" />
		</Style>
		<Style Selector="NumericUpDown /template/ TextBox:focus /template/ Border#PART_BorderElement">
			<Setter Property="Background" Value="Transparent" />
			<Setter Property="BorderBrush" Value="Transparent" />
		</Style>
"""

for filepath in files_to_style:
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # Add the fix before </UserControl.Styles> if not present
    if "<!-- NumericUpDown Fix" not in content:
        content = content.replace("</UserControl.Styles>", numeric_updown_fix + "\n    </UserControl.Styles>")
    
    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

print("Injected NumericUpDown fix!")
