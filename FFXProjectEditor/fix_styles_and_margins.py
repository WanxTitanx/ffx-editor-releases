import os
import re

files_to_style = [
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/KeyItemEditor/KeyItemEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/TreasureEditor/TreasureEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/WeaponGear/WeaponGear_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/MixTableEditor/MixTableEditor_Control.axaml"
]

for filepath in files_to_style:
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. Fix the "sunken" text by removing hardcoded Height="24" and setting MinHeight="26"
    content = re.sub(r'<Setter Property="Height" Value="24"\s*/>', '', content)
    content = re.sub(r'<Setter Property="MinHeight" Value="24"\s*/>', '<Setter Property="MinHeight" Value="26" />\n\t\t\t<Setter Property="VerticalContentAlignment" Value="Center" />', content)

    # 2. Add top margin to MasterHeader's first StackPanel to avoid overlapping the GUARDED pill
    # Look for <common:ModuleMasterDetail_Shell.MasterHeader>\s*<StackPanel ...>
    # Note: WeaponGear has <StackPanel Spacing="2"> without margin
    # KeyItem has <StackPanel Spacing="4" Margin="2,0,2,2">
    
    def add_top_margin(match):
        stack_panel_tag = match.group(1)
        if 'Margin=' in stack_panel_tag:
            # Replace existing margin's top value (which is 0) with 36
            # Margin="L,T,R,B" or Margin="All" or Margin="X,Y"
            # Let's just blindly replace Margin="2,0,2,2" with Margin="2,36,2,2"
            new_tag = re.sub(r'Margin="([^",]+),\s*0\s*,([^",]+),\s*([^",]+)"', r'Margin="\1,36,\2,\3"', stack_panel_tag)
            # If it was Margin="2,0,2,2", now it's Margin="2,36,2,2"
            return match.group(0).replace(stack_panel_tag, new_tag)
        else:
            # Add Margin="0,36,0,0"
            new_tag = stack_panel_tag.replace('<StackPanel ', '<StackPanel Margin="0,36,0,0" ')
            return match.group(0).replace(stack_panel_tag, new_tag)

    content = re.sub(r'(<common:ModuleMasterDetail_Shell\.MasterHeader>\s*)(<StackPanel[^>]*>)', add_top_margin, content)

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

print("Fixed sunken text and MasterHeader margins!")
