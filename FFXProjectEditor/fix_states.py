import re

filepath = "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/BattleKernel/Commands/KernelCommands_Control.axaml"

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

# Replace the plain :focus with template targeting
content = content.replace('<Style Selector="TextBox:focus">', '<Style Selector="TextBox:focus /template/ Border#PART_BorderElement">')

# Add pointerover and combobox overrides
overrides = """
        <Style Selector="TextBox:pointerover /template/ Border#PART_BorderElement">
            <Setter Property="Background" Value="#1AFFFFFF" />
            <Setter Property="BorderBrush" Value="#44FFFFFF" />
        </Style>
        <Style Selector="ComboBox:pointerover /template/ Border#Background">
            <Setter Property="Background" Value="#1AFFFFFF" />
            <Setter Property="BorderBrush" Value="#44FFFFFF" />
        </Style>
        <Style Selector="ComboBox:focus /template/ Border#Background">
            <Setter Property="Background" Value="#11FFFFFF" />
            <Setter Property="BorderBrush" Value="{DynamicResource AccentCoolBrush}" />
        </Style>
"""

# Insert overrides before </UserControl.Styles>
content = content.replace('    </UserControl.Styles>', overrides + '\n    </UserControl.Styles>')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)

print(f"Fixed {filepath}")
