import re

filepath = "C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/BattleKernel/Commands/KernelCommands_Control.axaml"

with open(filepath, 'r', encoding='utf-8') as f:
    content = f.read()

# Remove the setter for ColumnDefinitions
content = re.sub(r'<Setter Property="ColumnDefinitions" Value="\*, 50" />\n?', '', content)

# Inject ColumnDefinitions into Grid.fieldGrid
content = content.replace('<Grid Classes="fieldGrid" ', '<Grid Classes="fieldGrid" ColumnDefinitions="*, 50" ')

with open(filepath, 'w', encoding='utf-8') as f:
    f.write(content)

print(f"Fixed {filepath}")
