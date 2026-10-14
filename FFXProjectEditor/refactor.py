import re
import glob

def refactor_file(filepath):
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    lines = content.split('\n')
    new_lines = []
    in_field = False
    field_lines = []
    changed = False
    
    for line in lines:
        if '<StackPanel Classes="field"' in line:
            in_field = True
            field_lines = [line]
        elif in_field:
            field_lines.append(line)
            if '</StackPanel>' in line:
                in_field = False
                field_content = '\n'.join(field_lines)
                tb_match = re.search(r'<TextBlock([^>]+)/>', field_content)
                if not tb_match:
                    new_lines.extend(field_lines)
                    continue
                tb_inner = tb_match.group(1).strip()
                input_match = re.search(r'<(TextBox|ComboBox|NumericUpDown)(.*?)(/>|>(.*?)</\1>)', field_content, re.DOTALL)
                if not input_match:
                    new_lines.extend(field_lines)
                    continue
                tag = input_match.group(1)
                input_inner = input_match.group(2).strip()
                is_self_closing = input_match.group(3) == '/>'
                inner_content = input_match.group(4) if not is_self_closing else ''
                
                sp_match = re.search(r'<StackPanel Classes="field"\s*(Width="\d+")?', field_lines[0])
                width_attr = sp_match.group(1) if sp_match and sp_match.group(1) else 'Width="170"'
                
                grid_start = f'                                          <Grid Classes="fieldGrid" {width_attr}>'
                tb_line = f'                                              <TextBlock {tb_inner} />'
                if is_self_closing:
                    input_line = f'                                              <{tag} Grid.Column="1" {input_inner} />'
                else:
                    input_line = f'                                              <{tag} Grid.Column="1" {input_inner}>\n{inner_content}\n                                              </{tag}>'
                grid_end = '                                          </Grid>'
                
                new_lines.append(grid_start)
                new_lines.append(tb_line)
                new_lines.append(input_line)
                new_lines.append(grid_end)
                changed = True
        else:
            new_lines.append(line)
            
    if changed:
        with open(filepath, 'w', encoding='utf-8') as f:
            f.write('\n'.join(new_lines))
        print(f"Refactored {filepath}")

for path in glob.glob("C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/ControlTemplates/**/*.axaml", recursive=True):
    refactor_file(path)
for path in glob.glob("C:/Users/wande/Documents/ffx-editor-main/FFXProjectEditor/Modules/**/*.axaml", recursive=True):
    refactor_file(path)
