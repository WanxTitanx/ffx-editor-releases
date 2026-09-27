import os
import re

files_to_clean = [
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/KeyItemEditor/KeyItemEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/TreasureEditor/TreasureEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/WeaponGear/WeaponGear_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/MixTableEditor/MixTableEditor_Control.axaml",
    "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/BukiGetTreasureCatalog/BukiGetTreasureCatalog_Control.axaml"
]

for filepath in files_to_clean:
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    # 1. Remove Aroma Premium block completely
    content = re.sub(r'<!-- Aroma Premium -->.*?<!-- NumericUpDown Fix for global TextBox styles -->', '<!-- Aroma Premium -->', content, flags=re.DOTALL)
    content = re.sub(r'<!-- Aroma Premium -->.*?</UserControl\.Styles>', '</UserControl.Styles>', content, flags=re.DOTALL)
    
    # 2. Revert the margin hack
    # Find any Margin="*,36,*,*" and change it back to what it might have been, or remove it.
    # Actually, it's safer to just replace 36 with 0
    content = re.sub(r'Margin="([^",]+),\s*36\s*,([^",]+),\s*([^",]+)"', r'Margin="\1,0,\2,\3"', content)
    content = content.replace('Margin="0,36,0,0"', '')

    with open(filepath, 'w', encoding='utf-8') as f:
        f.write(content)

print("Files cleaned up!")
