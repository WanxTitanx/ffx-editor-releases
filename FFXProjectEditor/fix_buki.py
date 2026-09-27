import os
import re

buki_file = "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/BukiGetTreasureCatalog/BukiGetTreasureCatalog_Control.axaml"

aroma_styles = """
		<!-- Aroma Premium -->
		<Style Selector="TextBox">
			<Setter Property="Background" Value="#08FFFFFF" />
			<Setter Property="BorderBrush" Value="#22FFFFFF" />
			<Setter Property="BorderThickness" Value="1" />
			<Setter Property="CornerRadius" Value="3" />
			<Setter Property="MinHeight" Value="26" />
			<Setter Property="VerticalContentAlignment" Value="Center" />
		</Style>
		<Style Selector="TextBox:focus /template/ Border#PART_BorderElement">
			<Setter Property="Background" Value="#11FFFFFF" />
			<Setter Property="BorderBrush" Value="{DynamicResource AccentCoolBrush}" />
		</Style>
		<Style Selector="TextBox:pointerover /template/ Border#PART_BorderElement">
			<Setter Property="Background" Value="#1AFFFFFF" />
			<Setter Property="BorderBrush" Value="#44FFFFFF" />
		</Style>
		<Style Selector="ComboBox">
			<Setter Property="Background" Value="#08FFFFFF" />
			<Setter Property="BorderBrush" Value="#22FFFFFF" />
			<Setter Property="BorderThickness" Value="1" />
			<Setter Property="CornerRadius" Value="3" />
			<Setter Property="MinHeight" Value="26" />
			<Setter Property="VerticalContentAlignment" Value="Center" />
		</Style>
		<Style Selector="ComboBox:pointerover /template/ Border#Background">
			<Setter Property="Background" Value="#1AFFFFFF" />
			<Setter Property="BorderBrush" Value="#44FFFFFF" />
		</Style>
		<Style Selector="ComboBox:focus /template/ Border#Background">
			<Setter Property="Background" Value="#11FFFFFF" />
			<Setter Property="BorderBrush" Value="{DynamicResource AccentCoolBrush}" />
		</Style>
		<Style Selector="CheckBox.flag">
			<Setter Property="Width" Value="140" />
			<Setter Property="Margin" Value="0,0,8,4" />
		</Style>
"""

with open(buki_file, 'r', encoding='utf-8') as f:
    content = f.read()

# Inject styles if not present
if "<!-- Aroma Premium -->" not in content:
    content = content.replace("</UserControl.Styles>", aroma_styles + "\n    </UserControl.Styles>")

# Add top margin to MasterHeader StackPanel
def add_top_margin(match):
    stack_panel_tag = match.group(1)
    if 'Margin=' in stack_panel_tag:
        new_tag = re.sub(r'Margin="([^",]+),\s*0\s*,([^",]+),\s*([^",]+)"', r'Margin="\1,36,\2,\3"', stack_panel_tag)
        return match.group(0).replace(stack_panel_tag, new_tag)
    else:
        new_tag = stack_panel_tag.replace('<StackPanel ', '<StackPanel Margin="0,36,0,0" ')
        return match.group(0).replace(stack_panel_tag, new_tag)

content = re.sub(r'(<common:ModuleMasterDetail_Shell\.MasterHeader>\s*)(<StackPanel[^>]*>)', add_top_margin, content)

with open(buki_file, 'w', encoding='utf-8') as f:
    f.write(content)

print("Fixed BukiGetTreasureCatalog!")
