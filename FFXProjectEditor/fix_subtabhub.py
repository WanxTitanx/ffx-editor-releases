import os

subtabhub_path = r"C:\Users\wande\Documents\ffx-editor-main\FFXProjectEditor\Modules\SubTabHub\SubTabHub_Control.axaml"

with open(subtabhub_path, "r", encoding="utf-8") as f:
    content = f.read()

# Replace Margin="18,12,18,0" with Margin="18,12,18,16" to give space below the pill
content = content.replace('Margin="18,12,18,0"', 'Margin="18,12,18,16"')

with open(subtabhub_path, "w", encoding="utf-8") as f:
    f.write(content)

print("Updated SubTabHub_Control.axaml margin!")
