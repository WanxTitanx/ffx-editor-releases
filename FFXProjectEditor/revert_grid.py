import re
import glob
import os

def revert_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    original_content = content
    
    # We want to find blocks like:
    # <Grid Classes="fieldGrid" Width="170">
    #     <TextBlock Text="Name" />
    #     <TextBox Grid.Column="1" Text="{Binding Name}" />
    # </Grid>
    # And convert them back to:
    # <StackPanel Classes="field" Width="170">
    #     <TextBlock Text="Name" />
    #     <TextBox Text="{Binding Name}" />
    # </StackPanel>

    # Replace the start tag
    content = re.sub(r'<Grid Classes="fieldGrid"(.*?)ColumnDefinitions="[^"]*"(.*?)>', r'<StackPanel Classes="field"\1\2>', content)
    content = re.sub(r'<Grid Classes="fieldGrid"(.*?)>', r'<StackPanel Classes="field"\1>', content)
    
    # Replace Grid.Column="1" inside TextBox/ComboBox/NumericUpDown
    content = re.sub(r'<(TextBox|ComboBox|NumericUpDown)([^>]*?) Grid\.Column="1"([^>]*?)>', r'<\1\2\3>', content)
    content = re.sub(r'<(TextBox|ComboBox|NumericUpDown)([^>]*?)Grid\.Column="1"([^>]*?)>', r'<\1\2\3>', content)
    
    # Replace the end tag
    content = re.sub(r'</Grid>\s*(?=<!--|$|<)', r'</StackPanel>\n', content) # not perfectly precise, let's just do a simpler search and replace for </Grid> only where it matches

    if "StackPanel Classes=\"field\"" in content:
        # Since replacing </Grid> safely is hard without matching tags, we can just use the fact that it's indented.
        # Actually, let's parse line by line to be safe, since we know exactly how refactor.py formatted it.
        pass

    # A better way: replace block by block
    def replacer(match):
        width_attr = match.group(1)
        tb_line = match.group(2)
        tag = match.group(3)
        input_inner = match.group(4)
        closing = match.group(5)
        # Remove Grid.Column="1"
        input_inner = input_inner.replace(' Grid.Column="1"', '')
        return f'<StackPanel Classes="field"{width_attr}>\n{tb_line}\n<{tag}{input_inner}{closing}\n</StackPanel>'

    content = re.sub(r'<Grid Classes="fieldGrid"([^>]*)>\s*(<TextBlock[^>]*>)\s*<(TextBox|ComboBox|NumericUpDown)([^>]*)(/>|>[^<]*</\3>)\s*</Grid>', replacer, original_content, flags=re.DOTALL)
    
    # Also fix Style Selectors
    content = content.replace('<Style Selector="Grid.fieldGrid">', '<Style Selector="StackPanel.field">')
    content = content.replace('<Style Selector="Grid.fieldGrid > TextBlock">', '<Style Selector="StackPanel.field > TextBlock">')

    if content != original_content:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write(content)
        print(f"Reverted grids in {os.path.basename(filepath)}")

for path in glob.glob("C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/ControlTemplates/**/*.axaml", recursive=True):
    revert_file(path)
for path in glob.glob("C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/**/*.axaml", recursive=True):
    revert_file(path)
